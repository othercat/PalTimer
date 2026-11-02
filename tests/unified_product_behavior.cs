using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Loads the exact frozen EXE; does not construct a form, game process or network client.
static class UnifiedProductBehavior
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    const BindingFlags I = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    static object Route(Type type, string profile, string id)
    {
        var rules = (Array)type.GetMethod("Defaults", F, null, new[] {typeof(string)}, null).Invoke(null, new object[] {profile});
        return rules.Cast<object>().Single(r => (string)type.GetProperty("Id").GetValue(r) == id);
    }
    static bool Matches(object rule, Type eventType, int team, int scene, int result, int enemy)
    {
        var battle = Activator.CreateInstance(eventType, true);
        eventType.GetProperty("team").SetValue(battle, team);
        eventType.GetProperty("scene").SetValue(battle, scene);
        eventType.GetProperty("result").SetValue(battle, result);
        eventType.GetProperty("enemies").SetValue(battle, new[] {enemy});
        return (bool)rule.GetType().GetMethod("Matches", I, null, new[] {eventType}, null).Invoke(rule, new[] {battle});
    }
    static int Main(string[] args)
    {
        try {
            var product = Assembly.LoadFrom(args[0]);
            Check(product.GetName().Version.ToString() == "3.38.6.6", "unified EXE version");
            var cores = (List<string>)product.GetType("Pal98Timer.TimerCore").GetMethod("GetAllCores", F).Invoke(null, null);
            foreach (string name in new[] {"Pal98Dx9Automatic", "Pal98Dx9Fast800", "Pal98Dx9Fast800Speed", "SdlpalGeshi"})
                Check(cores.Contains(name), "shared product enumerates " + name);
            Check(!cores.Contains("Hunqian167") && !cores.Contains("Dream220Visible"), "retired original story cores remain absent");
            Check(product.GetType("Pal98Timer.TournamentLockInfoReader") == null, "retired original configuration lock remains absent");
            string[] profiles = {"pal-dos-v1","pal98-classic-v6-v1","geshi-easy-v1","dream22-puppet100-v1","dream222-normal-v1","dream222-hard-v1","dreamspace39x-hard-v1","dreamspace35-v1","dream211-v1","hunqian-branch-v1","hunqian167-easy-v1","hunqian167-hard-v1","mogai10-v1","dream211-pudu-v1","dream211-yueying-v1","tianqi20-v1","dream211-yueying-rat-v1","geshi-hard-v1","geshi-nightwolf-v1","geshi-fanmade-v1","geshi-fanmade-entertainment-v1"};
            var bridge = product.GetType("Pal98Timer.SdlpalBridge");
            var ruleType = product.GetType("Pal98Timer.SdlpalSplitRule");
            foreach (string profile in profiles) {
                Check((bool)bridge.GetMethod("SupportedProfile", F).Invoke(null, new object[] {profile}), "frozen EXE supports " + profile);
                object rules = ruleType.GetMethod("Defaults", F, null, new[] {typeof(string)}, null).Invoke(null, new object[] {profile});
                ruleType.GetMethod("Validate", F).Invoke(null, new[] {rules});
                Check(((Array)rules).Length > 1, "frozen EXE contains route for " + profile);
            }
            var battleType = product.GetType("Pal98Timer.SdlpalBattleEvent");
            var dragon35 = Route(ruleType, "dreamspace35-v1", "dream-blood-dragon");
            Check(Matches(dragon35, battleType, 386, 295, 3, 572), "3.5 dragon route accepts observed illusion phase victory");
            Check(Matches(dragon35, battleType, 386, 295, 3, 573), "3.5 dragon route accepts observed dragon phase victory");
            Check(!Matches(dragon35, battleType, 385, 295, 3, 573), "3.5 dragon route rejects another team");
            Check(!Matches(dragon35, battleType, 386, 294, 3, 573), "3.5 dragon route rejects another scene");
            Check(!Matches(dragon35, battleType, 386, 295, 1, 573), "3.5 dragon route rejects a loss");
            var dragon39 = Route(ruleType, "dreamspace39x-hard-v1", "dream-blood-dragon");
            Check(Matches(dragon39, battleType, 386, 295, 3, 572) && !Matches(dragon39, battleType, 386, 295, 3, 573), "3.9 dragon route retains its exact legacy enemy");
            var snapshotType = product.GetType("Pal98Timer.SdlpalSnapshot");
            foreach (string profile in profiles) {
                var snapshot = Activator.CreateInstance(snapshotType, true);
                snapshotType.GetProperty("profile").SetValue(snapshot, profile);
                snapshotType.GetProperty("resourceHashes").SetValue(snapshot, new Dictionary<string, string> { {"sss", new string('a', 64)} });
                var rank = snapshotType.GetMethod("RankingFacts", I);
                var identity = snapshotType.GetProperty("ResourceEngineIdentity", I);
                var ordinary = (Dictionary<string, string>)rank.Invoke(snapshot, new object[] {false});
                string ordinaryIdentity = (string)identity.GetValue(snapshot);
                Check(!ordinary.ContainsKey("return_linger"), "default follow flag preserves ordinary facts: " + profile);
                snapshotType.GetProperty("returnLinger").SetValue(snapshot, true);
                var follow = (Dictionary<string, string>)rank.Invoke(snapshot, new object[] {false});
                Check(follow.Count == ordinary.Count + 1 && follow["return_linger"] == "1" && ordinary.All(p => follow[p.Key] == p.Value) &&
                    (string)identity.GetValue(snapshot) == ordinaryIdentity + "|return_linger=1", "follow facts and runtime identity separate only the enabled option: " + profile);
                snapshotType.GetProperty("returnLinger").SetValue(snapshot, false);
                Check((string)identity.GetValue(snapshot) == ordinaryIdentity, "disabling follow restores ordinary runtime identity: " + profile);
            }
            Check(product.GetType("Pal98Timer.OriginalChallengeIdentity") != null, "original challenge identity remains in the same EXE");
            var online = Assembly.LoadFrom(args[1]);
            Check(online.GetName().Version.ToString() == "1.0.2.5", "unified Online version");
            Check(online.GetType("Pal98.Challenges.ChallengeRelayContract") != null, "frozen Online uses generic challenge relay");
            Console.WriteLine("PASS unified product total=" + checks + "; game/network/UI=none"); return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
