using Pal98Timer;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

internal static partial class LiveBehavior
{
    static CompetitionObservation NoReviewObservation(string token)
    {
        var value = Observe(token, 0);
        value.Ranking.rules["challenge.id"] = "78e33b31-ea04-410a-8cb2-8e0ce555965c";
        value.Ranking.rules["challenge.revision"] = "1";
        value.Ranking.rules["challenge.content_id"] = "paldll-dx9-suiji-v0-2";
        value.Ranking.rules["challenge.rules_sha256"] = new string('7',64);
        value.Ranking.configuration_id = RankingConfiguration.Digest(value.Ranking.rules);
        value.RestartMilliseconds = 1234;
        return value;
    }
    static Transport NoReviewTransport() => new Transport { RejectOriginalFacts = true,
        Features = new[] { CompetitionProtocol.RestartTimeFeature, "original-challenge-ranking.v1" } };
    static async Task NoReviewChallengeTests(string root)
    {
        var wire = NoReviewTransport(); var client = Client(root, "no-review-compat", wire, new Auth());
        try {
            await Until(() => client.StatusCaption == " [已连接:1042]", "no-review menu initially connects");
            var value = NoReviewObservation("original-no-review");
            string before = CompetitionProtocol.Json().Serialize(value.Ranking);
            client.Publish(value); client.PublishClock(value.Token, 1200, true, Stopwatch.GetTimestamp()); client.Retry();
            await Until(() => wire.OriginalRejections == 1 && client.LiveDetail.Contains("免审挑战实时连接兼容中"), "422 original admission recovers through acknowledged neutral heartbeat");
            var compatible = wire.Last.observation;
            Check(client.StatusCaption == " [已连接:1042]" && !wire.LastHasRanking && compatible.configuration_id == null &&
                compatible.custom_competition_id == null && wire.Last.compare == null, "compatibility never advertises challenge or ordinary-board eligibility");
            Check(compatible.state == "running" && compatible.elapsed_ms >= 1200 && compatible.restart_ms == 1234 && compatible.splits.Length == 2,
                "compatibility retains actual clock, restart and complete route");
            Check(before == CompetitionProtocol.Json().Serialize(value.Ranking) && value.ValidationError == "" && value.RestartMilliseconds == 1234,
                "wire-only fallback preserves complete local ranking and review evidence");
            int updates = wire.Updates;
            // Advance only the worker deadline, leaving the compatibility policy lease intact.
            typeof(CompetitionClient).GetField("nextLiveDueTick", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(client, 0L);
            await Until(() => wire.Updates > updates, "next heartbeat uses acknowledged compatibility");
            Check(wire.ChallengeReads == 1 && wire.OriginalRejections == 1, "policy lookup and rejected probe are bounded within one minute");
            wire.RejectOriginalFacts = false;
            typeof(CompetitionClient).GetField("nextQuery", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(client, DateTime.MinValue);
            typeof(CompetitionClient).GetField("nextLiveChallengeProbeTick", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(client, 0L);
            typeof(CompetitionClient).GetField("nextLiveDueTick", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(client, 0L);
            await Until(() => wire.LastHasRanking && !client.LiveDetail.Contains("免审挑战实时连接兼容中"), "server repair restores full challenge identity on the bounded automatic probe");
            Check(wire.Last.observation.configuration_id == value.Ranking.configuration_id && wire.Last.compare != null && wire.MaxActive == 1,
                "full recovery preserves configuration and serialized requests");
        } finally { await client.CloseAsync(); }

        foreach (string scenario in new[] { "reviewed", "wrong-type", "invisible", "offline", "revision", "hash", "auth", "unrelated-422", "bad-ack" }) {
            wire = NoReviewTransport();
            if (scenario == "reviewed") wire.ChallengeRequiresApproval = true;
            if (scenario == "wrong-type") wire.ChallengeRequiresApproval = "false";
            if (scenario == "invisible") wire.ChallengeReadStatus = 404;
            if (scenario == "offline") wire.ChallengeReadStatus = 0;
            if (scenario == "revision") wire.ChallengeRevision = "2";
            if (scenario == "hash") wire.ChallengeHash = new string('8',64);
            if (scenario == "auth") wire.UpdateStatus = 403;
            if (scenario == "unrelated-422") wire.UpdateStatus = 422;
            if (scenario == "bad-ack") wire.WrongAck = true;
            client = Client(root, "no-review-deny-" + scenario, wire, new Auth());
            try {
                await Until(() => client.Enabled, "no-review denial fixture enabled: " + scenario);
                var value = scenario == "unrelated-422" ? Observe(scenario, 0) : NoReviewObservation(scenario);
                client.Publish(value); client.Retry();
                await Until(() => client.StatusCaption == " [连接失败]" && wire.Updates > 0, "no false compatible ACK: " + scenario);
                Check(!client.LiveDetail.Contains("免审挑战实时连接兼容中"), "failed response cannot manufacture compatibility: " + scenario);
                if (scenario == "auth" || scenario == "unrelated-422") Check(wire.ChallengeReads == 0, "only original challenge 422 triggers policy read: " + scenario);
            } finally { await client.CloseAsync(); }
        }

        wire = NoReviewTransport(); wire.ChallengeReadGate = new TaskCompletionSource<bool>();
        client = Client(root, "no-review-selection-race", wire, new Auth());
        try {
            await Until(() => client.Enabled, "selection-race client enabled");
            client.Publish(NoReviewObservation("old-challenge")); client.Retry();
            await Until(() => wire.ChallengeReads == 1, "old challenge policy lookup held in background");
            client.Invalidate("new-game"); client.Publish(Observe("new-game", 0));
            wire.ChallengeReadGate.SetResult(true);
            await Until(() => wire.Last.observation.configuration_id == Observe("new-game", 0).Ranking.configuration_id,
                "selection change publishes the new game rather than old challenge fallback");
            Check(!client.LiveDetail.Contains("免审挑战实时连接兼容中") && wire.ChallengeReads == 1,
                "stale policy response cannot mark another game compatible");
        } finally { wire.ChallengeReadGate.TrySetResult(true); await client.CloseAsync(); }
    }
}
