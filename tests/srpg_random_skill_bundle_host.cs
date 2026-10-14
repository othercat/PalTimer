using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

internal static class SrpgRandomSkillBundleHost
{
    private const string Suffix = ".pal98-random-skills.json";
    private const string On = "[extra]\r\nRandomLevelUpSkills=1\r\nRandomSkillVictoryAdd=1\r\n";
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 4194304 };
    private static Type packageType, transport, flags;
    private static string fakeDll;
    private static int checks;

    private static int Main(string[] args)
    {
        try
        {
            Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
                new AssemblyName(e.Name).Name == assembly.GetName().Name ? assembly : null;
            packageType = assembly.GetType("Pal98Timer.SRPGobj", true);
            transport = assembly.GetType("Pal98Timer.SRPGSaveBundleTransport", true);
            flags = assembly.GetType("Pal98Timer.SRPGSidecarTransport", true);
            string root = Path.GetFullPath(args[1]);
            fakeDll = Path.GetFullPath(args[2]);
            string a = MakeGame(Path.Combine(root, "A"), false);
            string b = MakeGame(Path.Combine(root, "B"), false);
            byte[] aRpg = File.ReadAllBytes(Path.Combine(a, "2.RPG"));
            byte[] bRpg = (byte[])aRpg.Clone(); bRpg[100] = 37;
            File.WriteAllBytes(Path.Combine(b, "2.RPG"), bRpg);
            Config(a, On); Config(b, On);
            var aState = State("2.RPG", aRpg, uint.MaxValue, uint.MaxValue - 2);
            var bState = State("2.RPG", bRpg, 12345, 27);
            WriteState(a, "2.RPG", aState); WriteState(b, "2.RPG", bState);
            object aPackage = Serialize(Capture(a, "2.RPG"));
            object bPackage = Serialize(Capture(b, "2.RPG"));
            Check((string)Bundle(aPackage)["schema"] == "PalTimer.PAL98SaveBundle.v2", "new version refuses silent old-reader loss");
            Import(b, "1.RPG", aPackage);
            Import(a, "1.RPG", bPackage);
            SameState(aState, b, "1.RPG"); SameState(bState, a, "1.RPG");
            Import(a, "3.RPG", Serialize(Capture(b, "1.RPG")));
            SameState(aState, a, "3.RPG");
            Equal(aRpg, File.ReadAllBytes(Path.Combine(a, "3.RPG")), "roundtrip preserves exact RPG");
            Check((string)packageType.GetField("TimerStr").GetValue(aPackage) == "donor timer fixture", "timer payload unchanged");

            // Every publish boundary, including after the random receipt, must roll back.
            string[] targetFiles = { "1.RPG", "1.RPG.pal98-custom-roles.json", "1.RPG.pal98-ext-magics.json", "PalDrawCard.FlyingFlagAll.v1.bin", "1.RPG" + Suffix };
            for (int failure = 1; failure <= targetFiles.Length; ++failure)
            {
                foreach (string file in targetFiles) File.WriteAllText(Path.Combine(b, file), "previous " + file);
                var before = targetFiles.ToDictionary(file => file, file => File.ReadAllBytes(Path.Combine(b, file)));
                int boundary = failure;
                Reject(() => Import(b, "1.RPG", aPackage, n => { if (n == boundary) throw new IOException("injected publish failure"); }), "rollback boundary " + failure);
                foreach (string file in targetFiles) Equal(before[file], File.ReadAllBytes(Path.Combine(b, file)), "rollback " + failure + " " + file);
            }
            File.Delete(Path.Combine(b, "1.RPG" + Suffix));
            Reject(() => Import(b, "1.RPG", aPackage, n => { if (n == 5) throw new IOException("new-file rollback"); }), "new receipt rollback");
            Check(!File.Exists(Path.Combine(b, "1.RPG" + Suffix)), "rollback does not leave a newly created receipt");
            Import(b, "1.RPG", aPackage);
            SameState(aState, b, "1.RPG");

            // Recompute envelope checksum for malformed receipts to exercise the decoder.
            var mutations = new Action<Dictionary<string, object>>[] {
                s => s["seed"] = 0, s => s["seed"] = -1, s => s["seed"] = 4294967296L,
                s => s["seed"] = 1.5, s => s["seed"] = true,
                s => s["victory"] = -1, s => s["victory"] = 4294967296L,
                s => s["initialized"] = 65536, s => s["initialized"] = -1,
                s => s["save_file"] = "3.RPG", s => s["rpg_sha256"] = new string('a', 64),
                s => s["schema"] = "unknown", s => s.Remove("victory"), s => s["extra"] = 1,
                s => s["queues"] = new object[15], s => s["queues"] = null,
                s => ReplaceQueue(s, new object[] { 370, 370 }), s => ReplaceQueue(s, new object[] { 0 }),
                s => ReplaceQueue(s, new object[] { -1 }), s => ReplaceQueue(s, new object[] { 32768 }),
                s => ReplaceQueue(s, new object[] { "370" }),
                s => ReplaceQueue(s, Enumerable.Range(1, 1000).Cast<object>().ToArray())
            };
            byte[] expectedRpg = File.ReadAllBytes(Path.Combine(b, "1.RPG"));
            byte[] expectedReceipt = File.ReadAllBytes(Path.Combine(b, "1.RPG" + Suffix));
            foreach (var mutate in mutations)
            {
                object bad = Serialize(aPackage);
                var state = ReadJson(Convert.FromBase64String((string)Bundle(bad)["random_skills"]));
                mutate(state); SetReceipt(bad, Bytes(state));
                Reject(() => Import(b, "1.RPG", bad), "malformed receipt");
                Equal(expectedRpg, File.ReadAllBytes(Path.Combine(b, "1.RPG")), "refusal preserves RPG");
                Equal(expectedReceipt, File.ReadAllBytes(Path.Combine(b, "1.RPG" + Suffix)), "refusal preserves receipt");
            }
            foreach (Action<Dictionary<string, object>> mutate in new Action<Dictionary<string, object>>[] {
                d => d["random_skills_sha256"] = new string('0', 64),
                d => d["random_skills"] = "invalid base64!", d => d.Remove("random_skills"),
                d => d.Remove("random_skills_sha256"), d => d["random_skills"] = null,
                d => d["schema"] = "PalTimer.PAL98SaveBundle.v3"
            })
            {
                object bad = Serialize(aPackage); var bundle = Bundle(bad); mutate(bundle); SetBundle(bad, bundle);
                Reject(() => Import(b, "1.RPG", bad), "malformed envelope");
            }
            object oversized = Serialize(aPackage); SetReceipt(oversized, new byte[128 * 1024 + 1]);
            Reject(() => Import(b, "1.RPG", oversized), "oversized receipt");

            // Old files do not contain recoverable history; do not guess a seed or queue.
            object legacy = Serialize(aPackage); var oldBundle = Bundle(legacy);
            oldBundle["schema"] = "PalTimer.PAL98SaveBundle.v1";
            oldBundle.Remove("random_skills"); oldBundle.Remove("random_skills_sha256"); SetBundle(legacy, oldBundle);
            Import(b, "1.RPG", legacy);
            Check(true, "legacy restore is independent of target random-skill flags");
            Config(b, "[extra]\r\nRandomLevelUpSkills=0\r\n");
            File.WriteAllText(Path.Combine(b, "1.RPG" + Suffix), "unknown old receipt");
            Import(b, "1.RPG", legacy);
            Check(File.ReadAllText(Path.Combine(b, "1.RPG" + Suffix)) == "unknown old receipt", "legacy unknown snapshot leaves old receipt alone");

            Config(a, "[extra]\r\nRandomLevelUpSkills=0\r\n");
            SameReceipt(aState, Capture(a, "2.RPG"), "valid receipt survives disk flag disabled during an active game");
            File.Delete(Path.Combine(a, "config.ini"));
            Reject(() => Capture(a, "2.RPG"), "missing configuration must not silently discard state");
            Config(a, "[extra]\r\nRandomLevelUpSkills=0\r\n");
            using (var locked = new FileStream(Path.Combine(a, "config.ini"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Reject(() => Capture(a, "2.RPG"), "unreadable configuration must not silently discard state");
            File.WriteAllText(Path.Combine(a, "2.RPG" + Suffix), "obsolete disabled-feature receipt");
            object absent = Capture(a, "2.RPG");
            Check(Bundle(absent)["random_skills"] == null && Bundle(absent)["random_skills_sha256"] == null, "disabled feature ignores stale source receipt");
            Import(b, "1.RPG", absent);
            Check(!File.Exists(Path.Combine(b, "1.RPG" + Suffix)), "explicit absence clears destination receipt");
            Check(Directory.GetFiles(b, "1.RPG" + Suffix + ".paltimer-backup-*").Any(p => File.ReadAllText(p) == "unknown old receipt"), "cleared receipt retains recovery backup");
            Config(b, On);
            Import(b, "1.RPG", absent);
            Check(!File.Exists(Path.Combine(b, "1.RPG" + Suffix)), "explicit absence restores without inventing random history when target flag is on");
            File.Delete(Path.Combine(b, "config.ini"));
            Import(b, "1.RPG", aPackage);
            SameState(aState, b, "1.RPG");
            Config(b, On);
            using (var locked = new FileStream(Path.Combine(b, "config.ini"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Import(b, "1.RPG", aPackage);
            SameState(aState, b, "1.RPG");

            // Match PALDLL's missing-key migration, including an explicit new zero key.
            foreach (var item in new[] {
                Tuple.Create(On, true),
                Tuple.Create("[extra]\r\nRandomLevelUpSkills=1\r\n", false),
                Tuple.Create("[extra]\r\nRandomLevelUpSkills=1\r\nRandomSkillVictoryLearnAndForget=1\r\n", true),
                Tuple.Create("[extra]\r\nRandomLevelUpSkills=1\r\nRandomSkillVictoryLearnAndForget=1\r\nRandomSkillVictoryAdd=0\r\n", false),
                Tuple.Create("[extra]\r\nRandomLevelUpSkills=1\r\nRandomSkillVictoryLearnAndForget=1\r\nRandomSkillVictoryForgetPrevious=0\r\n", false),
                Tuple.Create("[extra]\r\nRandomLevelUpSkills=0\r\nRandomSkillVictoryAdd=1\r\n", false)
            })
            {
                Config(a, item.Item1);
                Check((bool)Call("RequiresRandomSkills", a) == item.Item2, "INI compatibility");
            }
            Config(a, On);
            string receiptPath = Path.Combine(a, "2.RPG" + Suffix);
            File.Delete(receiptPath);
            var delayed = new Thread(() => { Thread.Sleep(180); WriteState(a, "2.RPG", aState); });
            delayed.Start();
            try { SameReceipt(aState, Capture(a, "2.RPG"), "waits for delayed receipt"); }
            finally { delayed.Join(); }
            File.WriteAllText(receiptPath, "incomplete JSON");
            Reject(() => Capture(a, "2.RPG"), "damaged source refuses export");
            File.Delete(receiptPath);
            Reject(() => Capture(a, "2.RPG"), "missing source refuses export");
            var stale = State("2.RPG", bRpg, 42, 13); WriteState(a, "2.RPG", stale);
            Reject(() => Capture(a, "2.RPG"), "old RPG hash refuses export");
            WriteState(a, "2.RPG", aState);
            // Leave current-code consumer fixtures, not player saves, for the C++ decoder.
            Import(b, "1.RPG", aPackage);
            MakeGame(Path.Combine(root, "legacy-bundle-fixture"), true);
            Console.WriteLine("PASS: " + checks + " checks; mutual/roundtrip transport, real SRPG serialization, all publish rollbacks, v1/v2 compatibility, source-save race, malformed state, INI migration; no network/game used");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static object Call(string name, params object[] values)
    { return transport.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, values); }
    private static object Capture(string root, string name)
    {
        object package = Activator.CreateInstance(packageType);
        packageType.GetField("TimerStr").SetValue(package, "donor timer fixture");
        Call("Capture", root, Path.Combine(root, name), package);
        flags.GetMethod("CaptureFlyingFlagSnapshot", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { root, package });
        return package;
    }
    private static void Import(string root, string name, object package, Action<int> fail = null) { Call("Import", root, name, package, fail); }
    private static void Config(string root, string text) { File.WriteAllText(Path.Combine(root, "config.ini"), text, new UTF8Encoding(false)); }
    private static Dictionary<string, object> Bundle(object package) { return ReadJson((byte[])packageType.GetField("Pal98SaveBundle").GetValue(package)); }
    private static void SetBundle(object package, Dictionary<string, object> bundle) { packageType.GetField("Pal98SaveBundle").SetValue(package, Bytes(bundle)); }
    private static void SetReceipt(object package, byte[] receipt)
    { var bundle = Bundle(package); bundle["random_skills"] = Convert.ToBase64String(receipt); bundle["random_skills_sha256"] = Hash(receipt); SetBundle(package, bundle); }
    private static object Serialize(object package)
    { using (var stream = new MemoryStream()) { var f = new BinaryFormatter(); f.Serialize(stream, package); stream.Position = 0; return f.Deserialize(stream); } }
    private static Dictionary<string, object> ReadJson(byte[] bytes) { return Json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(bytes)); }
    private static byte[] Bytes(object value) { return Encoding.UTF8.GetBytes(Json.Serialize(value)); }
    private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    private static void ReplaceQueue(Dictionary<string, object> state, object[] values)
    { var queues = ((System.Collections.IEnumerable)state["queues"]).Cast<object>().ToArray(); queues[0] = values; state["queues"] = queues; }
    private static Dictionary<string, object> State(string slot, byte[] rpg, uint seed, uint victory)
    {
        return new Dictionary<string, object> {
            { "schema", "PAL98.RandomSkillRun.v1" }, { "save_file", slot }, { "rpg_sha256", Hash(rpg) },
            { "seed", seed }, { "victory", victory }, { "initialized", 65535 },
            { "queues", Enumerable.Range(0, 16).Select(r => new[] { 370 + r, 2148 + r, 32767 - r }).ToArray() }
        };
    }
    private static void WriteState(string root, string slot, Dictionary<string, object> state) { File.WriteAllBytes(Path.Combine(root, slot + Suffix), Bytes(state)); }
    private static void SameReceipt(Dictionary<string, object> expected, object package, string reason)
    { Equal(Bytes(expected), Convert.FromBase64String((string)Bundle(package)["random_skills"]), reason); }
    private static void SameState(Dictionary<string, object> expected, string root, string slot)
    {
        var copy = ReadJson(Bytes(expected)); copy["save_file"] = slot;
        var actual = ReadJson(File.ReadAllBytes(Path.Combine(root, slot + Suffix)));
        Check(Json.Serialize(copy) == Json.Serialize(actual), "all random state survives and rebinds to " + slot);
    }
    private static void Equal(byte[] left, byte[] right, string reason) { Check(left.SequenceEqual(right), reason); }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); ++checks; }
    private static void Reject(Action action, string reason)
    {
        try { action(); }
        catch (TargetInvocationException error) when (error.InnerException is IOException || error.InnerException is InvalidDataException) { ++checks; return; }
        throw new Exception("Expected refusal: " + reason);
    }
    private static string MakeGame(string root, bool customProfile)
    {
        Directory.CreateDirectory(root); File.Copy(fakeDll, Path.Combine(root, "PAL.dll"));
        Config(root, "[extra]\r\n");
        byte[] sss = new byte[44]; Buffer.BlockCopy(BitConverter.GetBytes(12), 0, sss, 0, 4); Buffer.BlockCopy(BitConverter.GetBytes(44), 0, sss, 4, 4);
        byte[] word = Encoding.ASCII.GetBytes("fixture");
        File.WriteAllBytes(Path.Combine(root, "SSS.MKF"), sss); File.WriteAllBytes(Path.Combine(root, "WORD.DAT"), word);
        byte[] rpg = new byte[14064 + 32]; File.WriteAllBytes(Path.Combine(root, "2.RPG"), rpg);
        if (!customProfile) return root;
        string stage = Path.Combine(root, "palmod/Profiles/fixture"); Directory.CreateDirectory(Path.Combine(stage, "manifest")); Directory.CreateDirectory(Path.Combine(stage, "resources"));
        File.WriteAllBytes(Path.Combine(stage, "resources/SSS.MKF"), sss); File.WriteAllBytes(Path.Combine(stage, "resources/WORD.DAT"), word);
        Func<string, byte[], object> resource = (kind, bytes) => new { kind, relative_path = "resources/" + kind, size_bytes = bytes.Length, sha256 = Hash(bytes) };
        byte[] descriptor = Bytes(new { schema = "PAL98.GameProfile.v1", profile_id = "test", profile_version = "1", required_runtime = new { capabilities = new string[0] }, resource_set = new[] { resource("SSS.MKF", sss), resource("WORD.DAT", word) } });
        File.WriteAllBytes(Path.Combine(stage, "manifest/game-profile.json"), descriptor);
        File.WriteAllBytes(Path.Combine(root, "palmod/Profiles/current.json"), Bytes(new { schema = "PAL98.EffectiveGameProfilePointer.v1", profile_id = "test", profile_version = "1", staging_relative_path = "fixture", descriptor_sha256 = Hash(descriptor) }));
        Directory.CreateDirectory(Path.Combine(root, "palmod/CustomRoles"));
        File.WriteAllBytes(Path.Combine(root, "palmod/CustomRoles/roles.json"), Bytes(new { schema = "PAL98.CustomRoleLibrary.v1", content_scope = "all-effective-content-profiles", library_id = "test", library_version = "1", custom_roles = Enumerable.Range(6, 10).Select(id => new { role_id = id }).ToArray() }));
        File.WriteAllBytes(Path.Combine(root, "2.RPG.pal98-custom-roles.json"), Bytes(new { schema = "PAL98.CustomRoleSaveState.v1", schema_version = 1, save_file = "2.RPG", rpg_size = rpg.Length, rpg_sha256 = Hash(rpg), library_id = "test", library_version = "1", roles = Enumerable.Range(6, 10).Select(id => new { role_id = id, fields = Enumerable.Repeat(id, 75).ToArray(), experience_words = new uint[16], modifiers = new int[98] }).ToArray() }));
        return root;
    }
}
