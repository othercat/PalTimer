using System;
using System.Collections.Generic;
using System.Linq;

namespace Pal98Timer
{
    // New observations use the server's extensible complete-configuration
    // namespace. The frozen host's local/PB identity and old signed outbox
    // records retain their original facts and hashes.
    internal static class SdlpalOnlineConfiguration
    {
        internal const string WuQiangPlus = "config.slot.RuntimeWuQiangPlus";
        internal const string SlotResidue = "config.slot.RuntimePrebattleSlotResidue";
        internal const string ChallengeDigest = "config.slot.RuntimeChallengeDigest";

        internal static CompetitionObservation Prepare(CompetitionObservation observation)
        {
            var ranking = observation?.Ranking;
            if (!GameplayIdentity.IsSdlFamily(observation?.Gameplay?.family) || ranking?.rules == null ||
                !new[] { "wuqiang_plus", "prebattle_slot_residue", "challenge_config_sha256" }.Any(ranking.rules.ContainsKey))
                return observation;

            // Never repair invalid/missing source evidence or invent a complete
            // configuration while the host's asynchronous reader is starting.
            if (ranking.Value("config.schema") != Pal98.Challenges.EffectiveChallengeConfiguration.Schema ||
                !ranking.Valid(observation.Gameplay) || observation.Gameplay.rules_sha256 != ranking.configuration_id ||
                observation.Hardcore == null || !observation.Hardcore.Valid || observation.Splits == null ||
                observation.TimelineId != TimelineIdentity.Create(ranking.configuration_id,
                    CompetitionProtocol.RouteHash(observation.Splits.Select(s => s.checkpoint_id)), observation.Hardcore.requested) ||
                new[] { WuQiangPlus, SlotResidue, ChallengeDigest }.Any(ranking.rules.ContainsKey))
                return observation;

            var copy = ranking.Copy();
            Move(copy.rules, "wuqiang_plus", WuQiangPlus, value => value);
            Move(copy.rules, "prebattle_slot_residue", SlotResidue, value => value);
            Move(copy.rules, "challenge_config_sha256", ChallengeDigest, EncodeDigest);
            copy.configuration_id = RankingConfiguration.Digest(copy.rules);
            var gameplay = observation.Gameplay.Copy();
            gameplay.rules_sha256 = copy.configuration_id;
            if (!copy.Valid(gameplay)) return observation;

            observation.Ranking = copy;
            observation.Gameplay = gameplay;
            observation.TimelineId = TimelineIdentity.Create(copy.configuration_id,
                CompetitionProtocol.RouteHash(observation.Splits.Select(s => s.checkpoint_id)), observation.Hardcore.requested);
            return observation;
        }

        private static void Move(IDictionary<string, string> facts, string source, string target, Func<string, string> encode)
        {
            if (!facts.TryGetValue(source, out string value)) return;
            facts.Add(target, encode(value));
            facts.Remove(source);
        }

        // Complete-configuration scalar values are bounded decimal/slug text.
        // Unpadded lowercase base32 preserves all 256 bits within that grammar
        // ("sha256-" + 52 characters), including digests starting with a digit.
        internal static string EncodeDigest(string digest)
        {
            if (!CompetitionProtocol.Digest(digest)) throw new ArgumentException("Invalid challenge digest.");
            const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
            var result = new System.Text.StringBuilder("sha256-");
            int accumulator = 0, bits = 0;
            for (int offset = 0; offset < digest.Length; offset += 2) {
                accumulator = (accumulator << 8) | Convert.ToByte(digest.Substring(offset, 2), 16);
                bits += 8;
                while (bits >= 5) { bits -= 5; result.Append(alphabet[(accumulator >> bits) & 31]); }
            }
            if (bits != 0) result.Append(alphabet[(accumulator << (5 - bits)) & 31]);
            return result.ToString();
        }
    }
}
