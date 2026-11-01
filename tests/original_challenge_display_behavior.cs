// Refs othercat/PALDLL_DX9#270. Cross-check server INI vectors without a game or network.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Pal98.Challenges;
using Wire = Pal98.Challenges.ChallengeRelayContract;
using Runtime = Pal98.Challenges.OriginalChallengeRuntimeContract;

static class OriginalChallengeDisplayBehavior
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    static void Reject(string ini, string message)
    {
        try { Runtime.Values(ini); } catch (InvalidDataException) { Check(true, message); return; }
        throw new Exception(message);
    }
    static int Main(string[] args)
    {
        try {
            var vectors = Wire.Object(File.ReadAllText(args[0]));
            string old = Wire.String(vectors, "old_ini"), off = Wire.String(vectors, "off_ini"), on = Wire.String(vectors, "on_ini");
            var legacy = Runtime.Values(old); var disabled = Runtime.Values(off); var enabled = Runtime.Values(on);
            Check(!old.Contains("OriginalHighCashDisplay") && legacy["OriginalHighCashDisplay"] == 0, "legacy signed INI stays unchanged and remains readable");
            Check(disabled["OriginalHighCashDisplay"] == 0 && enabled["OriginalHighCashDisplay"] == 1, "new off/on server outputs are understood");
            Check(Wire.String(vectors, "old_sha256") == "7d749ad982983d09925767d50b2ddfe68248c0206968f525e7fac778719ce15d", "legacy snapshot digest is unchanged");
            Check(Wire.String(vectors, "off_sha256") != Wire.String(vectors, "on_sha256"), "display selection remains part of the challenge snapshot digest");
            const string content = "paldll-dx9-pal98fix-classic-v5";
            var expected = Runtime.Content(content);
            var facts = new Dictionary<string, string> {
                ["family"] = "standard", ["content"] = Wire.String(expected, "content_sha256"),
                ["content_package_id"] = Wire.String(expected, "package"), ["fade_ms"] = "1200", ["map_speed_ticks"] = "10"
            };
            foreach (var pair in Wire.Map(expected["ranking_resources"])) facts[pair.Key] = (string)pair.Value;
            foreach (var pair in Wire.Map(Runtime.Catalog["bindings"])) facts[(string)pair.Value] = disabled[pair.Key].ToString(CultureInfo.InvariantCulture);
            string before = Wire.Serialize(facts);
            Check(Runtime.Matches(content, legacy, facts, false), "legacy challenge still matches existing native facts");
            Check(Runtime.Matches(content, disabled, facts, false) && Runtime.Matches(content, enabled, facts, false), "display off/on uses identical gameplay and PB facts");
            Check(Runtime.Matches(content, enabled, facts, true), "nostalgia does not require a new display gameplay fact");
            Check(before == Wire.Serialize(facts) && !Wire.Map(Runtime.Catalog["bindings"]).ContainsKey("OriginalHighCashDisplay"), "matching never decorates or mutates native facts");
            Reject(off + "OriginalHighCashDisplay=0\n", "duplicate display slot is rejected");
            Reject(off.Replace("OriginalHighCashDisplay=0\n", "OriginalHighCashDisplay=2\n"), "invalid display value is rejected");
            Reject(off.Replace("OriginalFastFade=0\n", ""), "missing gameplay slot is still rejected");
            Reject(old + "OriginalFutureOption=1\n", "unknown slots remain rejected on legacy snapshots");
            Console.WriteLine("PASS: " + checks + " original display contract checks"); return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
