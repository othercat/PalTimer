using Pal98Timer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

internal static partial class CompetitionBehavior
{
    // Invoke both mapping methods from the built product pair. The test client
    // then uses the ordinary completion path with fake auth/transport only.
    static object CopyBridgeObservation(object source, Type targetType)
    {
        var target = Activator.CreateInstance(targetType, true);
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var field in source.GetType().GetFields(flags))
        {
            var destination = targetType.GetField(field.Name, flags);
            // Context is populated by Online after it resolves a target; the
            // host DTO deliberately has no client-owned target context.
            if (destination == null && field.Name == "Context" && field.GetValue(source) == null) continue;
            if (destination == null) throw new Exception("Observation field not carried by runtime: " + field.Name);
            object value = field.GetValue(source);
            if (value != null && !destination.FieldType.IsInstanceOfType(value))
            {
                var serializer = CompetitionProtocol.Json();
                var deserialize = serializer.GetType().GetMethods().Single(m => m.Name == "Deserialize" && m.IsGenericMethodDefinition);
                value = deserialize.MakeGenericMethod(destination.FieldType).Invoke(serializer, new object[] { serializer.Serialize(value) });
            }
            destination.SetValue(target, value);
        }
        return target;
    }

    static async Task SpeedSnapshotBridgeCases()
    {
        string runtime = Environment.GetEnvironmentVariable("PALTIMER_TEST_RUNTIME");
        if (string.IsNullOrEmpty(runtime))
        {
            Console.WriteLine("SKIP built EXE/Online completion bridge: pass -RuntimeDirectory to run_competition_tests.ps1");
            return;
        }
        var host = Assembly.LoadFrom(Path.Combine(runtime, "Pal98Timer.exe"));
        var online = Assembly.LoadFrom(Path.Combine(runtime, "PalTimerOnline.dll"));
        var observationType = host.GetType("Pal98Timer.CompetitionObservation", true);
        var create = host.GetType("Pal98Timer.GForm", true).GetMethod("CreateCompetitionSnapshot", BindingFlags.Static | BindingFlags.NonPublic);
        var read = online.GetType("Pal98Timer.OnlineModule", true).GetMethod("ReadSnapshot", BindingFlags.Static | BindingFlags.NonPublic);
        Func<CompetitionObservation, CompetitionObservation> roundTrip = source => {
            object snapshot = create.Invoke(null, new[] { CopyBridgeObservation(source, observationType) });
            return (CompetitionObservation)CopyBridgeObservation(read.Invoke(null, new[] { snapshot }), typeof(CompetitionObservation));
        };
        string evidence = Path.Combine(root, "speed-bridge-payloads");
        Directory.CreateDirectory(evidence);
        var categories = new HashSet<string>();
        foreach (string profile in new[] { "geshi-easy-v1", "dream222-normal-v1", "dream222-hard-v1", "dreamspace39x-hard-v1" })
        foreach (bool wuqiang in new[] { false, true })
        {
            string name = profile + (wuqiang ? "-wuqiang" : "-normal");
            var state = new SdlpalSnapshot { profile = profile, verified = true, exeHash = new string('1', 64),
                resourceHashes = new[] { "DATA.MKF", "M.MSG", "SSS.MKF", "WORD.DAT" }.ToDictionary(p => p, p => new string('2', 64)),
                respawnRule = "pal98-current-scene-v1", wuqiang = wuqiang };
            var facts = state.RankingFacts();
            string category = RankingConfiguration.Digest(facts);
            Check(categories.Add(category), "bridge retains eight content/effect categories: " + name);
            Func<int, CompetitionObservation> observe = step => {
                var value = SdlpalObservation(name, step);
                value.Gameplay = new GameplayIdentity { family = SdlpalBridge.ProfileFamily(profile), content_id = profile,
                    content_sha256 = facts["content"], rules_sha256 = category, fade_ms = step == 0 ? 800 : 600,
                    map_speed_ticks = 0, walk_frame_ms = step == 0 ? 90 : 35, speed = step == 0 ? 0 : 9, battle_speed = step == 0 ? 0 : 4 };
                value.Ranking = new RankingConfiguration { covered = true, configuration_id = category, rules = facts };
                value.GameTitle = profile; value.ReleaseRevision = "r58"; value.LiveTimingValid = true;
                value.FadeMilliseconds = value.Gameplay.fade_ms; value.TotalMilliseconds = step == 0 ? 0 : step == 1 ? 500 : 1200;
                if (profile != "geshi-easy-v1") {
                    value.Splits[0].checkpoint_id = "dream-ship"; value.Splits[1].checkpoint_id = "dream-ending";
                }
                value.TimelineId = TimelineIdentity.Create(category, CompetitionProtocol.RouteHash(value.Splits.Select(s => s.checkpoint_id)), false);
                return value;
            };
            var initial = roundTrip(observe(0));
            var final = roundTrip(observe(2));
            Check(initial.Gameplay.speed == 0 && initial.Gameplay.battle_speed == 0 && final.Gameplay.speed == 9 && final.Gameplay.battle_speed == 4,
                "actual EXE/Online bridge preserves initial and final speeds: " + name);
            Check(initial.Ranking.Valid(initial.Gameplay) && final.Ranking.Valid(final.Gameplay) &&
                initial.Ranking.configuration_id == final.Ranking.configuration_id,
                "actual bridge keeps same configuration and complete observations: " + name);
            var wire = new Fake { Features = new[] { CompetitionProtocol.RestartTimeFeature, CompetitionProtocol.MaintenanceFeature, CompetitionProtocol.ContentEffectsFeature } };
            var client = new CompetitionClient(Store("bridge-" + name), wire, new FakeAuth());
            try {
                client.Configure(Config());
                await Until(() => wire.Registers > 0, "offline bridge transport registered: " + name);
                client.Publish(initial); client.Publish(roundTrip(observe(1))); client.Publish(final);
                await Until(() => wire.Posts == 1, "ordinary completion from product bridge reaches fake transport: " + name);
                string payload = wire.Payloads.Single();
                var run = CompetitionProtocol.Json().Deserialize<CompetitionRun>(payload);
                Check(run.observations.issues.Length == 0 && run.observations.finished && run.observations.began_here &&
                    run.gameplay.speed == 9 && run.gameplay.battle_speed == 4 &&
                    run.observations.initial_gameplay.speed == 0 && run.observations.initial_gameplay.battle_speed == 0 &&
                    run.ranking.configuration_id == category && run.release_revision == "r58",
                    "ordinary signed payload preserves complete initial/final evidence: " + name);
                File.WriteAllText(Path.Combine(evidence, name + ".json"), payload);
            } finally { await client.CloseAsync(); }
        }
    }
}
