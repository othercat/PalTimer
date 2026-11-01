using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Loads the exact frozen EXE; does not construct a form, game process or network client.
static class UnifiedProductBehavior
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    static int Main(string[] args)
    {
        try {
            var product = Assembly.LoadFrom(args[0]);
            Check(product.GetName().Version.ToString() == "3.38.6.1", "unified EXE version");
            var cores = (List<string>)product.GetType("Pal98Timer.TimerCore").GetMethod("GetAllCores", F).Invoke(null, null);
            foreach (string name in new[] {"Pal98Dx9Automatic", "Pal98Dx9Fast800", "Pal98Dx9Fast800Speed", "SdlpalGeshi"})
                Check(cores.Contains(name), "shared product enumerates " + name);
            Check(!cores.Contains("Hunqian167") && !cores.Contains("Dream220Visible"), "retired original story cores remain absent");
            Check(product.GetType("Pal98Timer.TournamentLockInfoReader") == null, "retired original configuration lock remains absent");
            string[] profiles = {"pal-dos-v1","pal98-classic-v6-v1","geshi-easy-v1","dream22-puppet100-v1","dream222-normal-v1","dream222-hard-v1","dreamspace39x-hard-v1","dream211-v1","hunqian-branch-v1","hunqian167-easy-v1","hunqian167-hard-v1","mogai10-v1","dream211-pudu-v1","dream211-yueying-v1","tianqi20-v1","dream211-yueying-rat-v1","geshi-hard-v1","geshi-nightwolf-v1","geshi-fanmade-v1","geshi-fanmade-entertainment-v1"};
            var bridge = product.GetType("Pal98Timer.SdlpalBridge");
            var ruleType = product.GetType("Pal98Timer.SdlpalSplitRule");
            foreach (string profile in profiles) {
                Check((bool)bridge.GetMethod("SupportedProfile", F).Invoke(null, new object[] {profile}), "frozen EXE supports " + profile);
                object rules = ruleType.GetMethod("Defaults", F, null, new[] {typeof(string)}, null).Invoke(null, new object[] {profile});
                ruleType.GetMethod("Validate", F).Invoke(null, new[] {rules});
                Check(((Array)rules).Length > 1, "frozen EXE contains route for " + profile);
            }
            Check(product.GetType("Pal98Timer.OriginalChallengeIdentity") != null, "original challenge identity remains in the same EXE");
            var online = Assembly.LoadFrom(args[1]);
            Check(online.GetName().Version.ToString() == "1.0.2.0", "unified Online version");
            Check(online.GetType("Pal98.Challenges.ChallengeRelayContract") != null, "frozen Online uses generic challenge relay");
            Console.WriteLine("PASS unified product total=" + checks + "; game/network/UI=none"); return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
