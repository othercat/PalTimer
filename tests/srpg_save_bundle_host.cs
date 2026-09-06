using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

internal static class SrpgSaveBundleHost
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 4194304 };
    private static int Main(string[] args)
    {
        try
        {
            string root = Path.GetFullPath(args[1]);
            Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
                new AssemblyName(eventArgs.Name).Name == assembly.GetName().Name ? assembly : null;
            Type packageType = assembly.GetType("Pal98Timer.SRPGobj", true);
            Type bundleType = assembly.GetType("Pal98Timer.SRPGSaveBundleTransport", true);
            Type flagsType = assembly.GetType("Pal98Timer.SRPGSidecarTransport", true);
            BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            Action<string, object[]> invoke = (name, values) => bundleType.GetMethod(name, flags).Invoke(null, values);
            byte[] rpg = File.ReadAllBytes(Path.Combine(root, "2.RPG"));
            byte[] custom = File.ReadAllBytes(Path.Combine(root, "2.RPG.pal98-custom-roles.json"));
            string libraryPath = Path.Combine(root, "palmod/CustomRoles/roles.json");
            byte[] library = File.ReadAllBytes(libraryPath);
            string pointerPath = Path.Combine(root, "palmod/Profiles/current.json");
            byte[] pointer = File.ReadAllBytes(pointerPath);
            object package = Activator.CreateInstance(packageType);
            packageType.GetField("TimerStr").SetValue(package, "{}");
            string customPath = Path.Combine(root, "2.RPG.pal98-custom-roles.json");
            File.Delete(customPath);
            var delayedSidecar = new Thread(() => { Thread.Sleep(150); File.WriteAllBytes(customPath, custom); });
            delayedSidecar.Start();
            try { invoke("Capture", new object[] { root, Path.Combine(root, "2.RPG"), package }); }
            finally { delayedSidecar.Join(); File.WriteAllBytes(customPath, custom); }
            flagsType.GetMethod("CaptureFlyingFlagSnapshot", flags).Invoke(null, new object[] { root, package });
            Equal(rpg, (byte[])packageType.GetField("RPG").GetValue(package), "export must use the exact six-column disk RPG");
            using (var stream = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(stream, package);
                stream.Position = 0;
                package = formatter.Deserialize(stream);
            }
            byte[] bundle = (byte[])packageType.GetField("Pal98SaveBundle").GetValue(package);
            string target = Path.Combine(root, "1.RPG");
            string customTarget = target + ".pal98-custom-roles.json";
            string legacyTarget = target + ".pal98-ext-magics.json";
            string flagTarget = Path.Combine(root, "PalDrawCard.FlyingFlagAll.v1.bin");
            byte[] old = Encoding.ASCII.GetBytes("previous-file-must-survive-failure");
            foreach (string path in new[] { target, customTarget, legacyTarget, flagTarget }) File.WriteAllBytes(path, old);
            Action<int> failAfterTwo = n => { if (n == 2) throw new IOException("injected failure between files"); };
            Reject(() => invoke("Import", new object[] { root, "1.RPG", package, failAfterTwo }), "partial write failure");
            foreach (string path in new[] { target, customTarget, legacyTarget, flagTarget })
                Equal(old, File.ReadAllBytes(path), "transaction rollback restores every old file");

            invoke("Import", new object[] { root, "1.RPG", package, null });
            Equal(rpg, File.ReadAllBytes(target), "import preserves every RPG byte including six native skill columns");
            var beforeState = Json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(custom));
            var afterState = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(customTarget));
            Assert((string)afterState["save_file"] == "1.RPG", "2.RPG sidecar is rebound to slot 1");
            beforeState["save_file"] = "1.RPG";
            Assert(Json.Serialize(beforeState) == Json.Serialize(afterState), "all ten role states, skills, experience and modifiers survive import");
            Assert(!File.Exists(legacyTarget) && !File.Exists(flagTarget), "absent/inactive sidecars are backed up and cleared");
            byte[] importedCustom = File.ReadAllBytes(customTarget);

            File.WriteAllText(libraryPath, Encoding.UTF8.GetString(library) + " ");
            Reject(() => invoke("Import", new object[] { root, "1.RPG", package, null }), "different fixed library");
            File.WriteAllBytes(libraryPath, library);
            File.WriteAllText(pointerPath, "{}");
            Reject(() => invoke("Import", new object[] { root, "1.RPG", package, null }), "invalid target profile");
            File.WriteAllBytes(pointerPath, pointer);
            byte[] badRpg = (byte[])rpg.Clone(); badRpg[508] ^= 1;
            packageType.GetField("RPG").SetValue(package, badRpg);
            Reject(() => invoke("Import", new object[] { root, "1.RPG", package, null }), "wrong RPG hash");
            packageType.GetField("RPG").SetValue(package, rpg);
            var bad = Json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(bundle));
            bad["custom_roles"] = null;
            packageType.GetField("Pal98SaveBundle").SetValue(package, Encoding.UTF8.GetBytes(Json.Serialize(bad)));
            Reject(() => invoke("Import", new object[] { root, "1.RPG", package, null }), "missing custom snapshot");
            packageType.GetField("Pal98SaveBundle").SetValue(package, bundle);
            Reject(() => invoke("Import", new object[] { root, "../1.RPG", package, null }), "slot path traversal");
            Reject(() => invoke("ValidateLegacyTarget", new object[] { root }), "old incomplete SRPG on v1.62");
            Equal(rpg, File.ReadAllBytes(target), "rejected imports leave RPG intact");
            Equal(importedCustom, File.ReadAllBytes(customTarget), "rejected imports leave custom roles intact");
            // An old non-PALDLL target keeps the legacy import path.
            string oldRoot = Path.Combine(root, "legacy-target"); Directory.CreateDirectory(oldRoot);
            invoke("ValidateLegacyTarget", new object[] { oldRoot });
            Console.WriteLine("PASS: exact RPG export; 2-to-1 rebind; all roles 6..15; serialization; identity refusal; transactional rollback; legacy target boundary");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Equal(byte[] a, byte[] b, string reason) { Assert(a.SequenceEqual(b), reason); }
    private static void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Reject(Action action, string reason)
    {
        try { action(); }
        catch (TargetInvocationException error) when (error.InnerException is IOException || error.InnerException is InvalidDataException) { return; }
        throw new Exception("Expected refusal: " + reason);
    }
}
