// Built assembly checks with isolated files; no real game, memory writes or network.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

static class EffectiveConfigurationHost
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static int checks;
    static void Check(bool value, string why) { if (!value) throw new Exception(why); ++checks; Console.WriteLine("PASS " + why); }
    static object Call(Type type, object obj, string name, params object[] args) => type.GetMethod(name, All).Invoke(obj, args);
    static Dictionary<string, object> Map(object value) => (Dictionary<string, object>)value;
    static int Main(string[] args)
    {
        try {
            var assembly = Assembly.LoadFrom(args[0]); var type = assembly.GetType("Pal98Timer.SdlpalChallengeConfiguration", true);
            var ranking = assembly.GetType("Pal98Timer.RankingConfiguration", true);
            var helper = assembly.GetType("Pal98.Challenges.EffectiveChallengeConfiguration", true);
            var json = new JavaScriptSerializer {MaxJsonLength=1048576};
            string root = args[2]; Directory.CreateDirectory(Path.Combine(root, "palmod"));
            string selected = Path.Combine(root, "palmod/challenge-selection.v1.json"), iniPath = Path.Combine(root, "challenge-rules.ini");
            string originalSelection = File.ReadAllText(Path.Combine(args[1], "online-selection.json")), ini = File.ReadAllText(Path.Combine(args[1], "online.ini"));
            File.WriteAllText(selected, originalSelection); File.WriteAllText(iniPath, ini);
            object subject = Activator.CreateInstance(type, true);
            Func<Dictionary<string,string>> facts = () => (Dictionary<string,string>)type.GetProperty("Facts", All).GetValue(subject);
            Func<string> error = () => (string)type.GetProperty("Error", All).GetValue(subject);
            Func<string> digest = () => (string)Call(ranking, null, "Digest", facts());
            ulong created = (ulong)DateTime.UtcNow.AddSeconds(2).ToFileTimeUtc();
            Call(type, subject, "ReadFiles", root, 7001, created, "pal98-classic-v6-v1");
            Check(error() == null && facts()["config.slot.BattleAtWill"] == "1" && facts()["config.slot.PriorityBattleAtWill"] == "37", "Online source with retained local values reads without losing configuration");
            Check(facts().Keys.Count(k => k.StartsWith("config.slot.")) >= 67 && facts().ContainsKey("config.source.rules_sha256"), "Complete flags, hero, priorities and immutable online source are present");
            string first = digest(); var firstFacts = new Dictionary<string,string>(facts());
            var document = Map(json.DeserializeObject(originalSelection)); Map(Map(document["activation"])["challenge"])["title"] = "封面文字刷新";
            File.WriteAllText(selected, json.Serialize(document));
            Call(type, subject, "ReadFiles", root, 7001, created, "pal98-classic-v6-v1");
            Check(error() == null && digest() == first, "Display-only metadata does not invalidate the process configuration");
            string changed = ini.Replace("\nBattleAtWill=1\n", "\nBattleAtWill=0\n");
            document["effective_ini"] = changed; File.WriteAllText(selected, json.Serialize(document)); File.WriteAllText(iniPath, changed);
            Call(type, subject, "ReadFiles", root, 7001, created, "pal98-classic-v6-v1");
            Check(error() != null && digest() == first && facts()["config.slot.BattleAtWill"] == "1", "In-process edits retain complete startup evidence and request restart");
            Call(type, subject, "ReadFiles", root, 7002, created, "pal98-classic-v6-v1");
            Check(error() == null && digest() != first && facts()["config.slot.BattleAtWill"] == "0", "New process picks up changed local fallback with a new configuration ID");
            var wire = new Dictionary<string,string>(facts()) {{"family", "sdlpal-classic98"}};
            Check((bool)Call(helper, null, "ValidFacts", wire), "Complete SDL evidence validates in the timer contract");
            wire.Remove("config.slot.Hero"); Check(!(bool)Call(helper, null, "ValidFacts", wire), "Missing hero cannot masquerade as complete configuration");
            string tampered = changed.Replace("\nSoloArt=0\n", "\nSoloArt=1\n"); document["effective_ini"] = tampered;
            File.WriteAllText(selected, json.Serialize(document)); File.WriteAllText(iniPath, tampered);
            string before = digest(); Call(type, subject, "ReadFiles", root, 7002, created, "pal98-classic-v6-v1");
            Check(error() != null && digest() == before, "Changing an online-defined value is rejected without clearing prior evidence");
            File.WriteAllText(selected, "{\"active\":false}");
            File.WriteAllText(iniPath, "[Challenge]\nSchemaVersion=1\nHero=li-xiaoyao\n");
            File.WriteAllText(Path.Combine(root,"config.ini"), "[Gameplay]\nSteal99=1\n");
            Call(type, subject, "ReadFiles", root, 7003, created, "geshi-easy-v1");
            Check(error() == null && facts()["config.slot.Steal99"] == "1" && facts()["config.slot.AutoCollect"] == "1" && facts()["config.slot.SpiritShop"] == "1", "Old local files publish explicit loader fallback values");
            Check(facts().Keys.Count(k => k.StartsWith("config.slot.")) == 67, "Local play also includes all actual configuration slots");
            Console.WriteLine("PASS " + checks + " effective configuration checks"); return 0;
        } catch (Exception error) {Console.Error.WriteLine(error); return 1;}
    }
}
