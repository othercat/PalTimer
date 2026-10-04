using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Pal98Timer;

// Runs in an isolated copy. The native host binding is tested separately by
// the private native probe; this exercises the real frozen host's loader.
internal static class OnlineModuleLoaderBehavior
{
    private static object Call(Assembly assembly, string type, string method, params object[] args)
    {
        return assembly.GetType("Pal98Timer." + type, true)
            .GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, args);
    }
    private static object Property(object value, string name)
    {
        var type = value.GetType();
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return property != null ? property.GetValue(value) : type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value);
    }
    private static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    private const string SettingsJson = "{\"schema\":\"PAL98.ToolLaunchSettings.v1\",\"competition_upload\":{\"schema\":\"PAL98.TimerOnlineSettings.v1\",\"xiaorou\":{\"enabled\":true,\"server\":\"https://fixture.invalid\",\"use_custom_competition\":true,\"custom_competition_id\":\"fixture-cup\"}}}";
    private static void WriteLock(Assembly host, string root, string producer, string runtime, string timer, int version = 2)
    {
        string active = Path.Combine(root, "palmod", "TournamentLock", "v1");
        var files = new List<object>();
        var names = new List<string> { "config.ini", "mod.ini", "dxwrapper.ini" };
        if (version == 2) names.Add("palmod/common-tools.v1.json");
        foreach (var name in names) {
            byte[] bytes = Encoding.UTF8.GetBytes(name.EndsWith(".json") ? SettingsJson : "[extra]\nMapSpeedTicks=10\n");
            string path = Path.Combine(active, "snapshots", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, bytes);
            files.Add(new { name, snapshot = "snapshots/" + name, size = bytes.Length, sha256 = Hash(bytes) });
        }
        byte[] dependency = { 1, 2, 3 }; File.WriteAllBytes(Path.Combine(root, "DATA.MKF"), dependency);
        var manifest = new {
            schema = "PAL98.TournamentLock.v" + version, version, locked = true,
            locker_name = "Fixture", competition_name = "Fixture", competition_display_name = "Fixture比赛专用",
            display_lines = new[] { "one", "two", "three", "four" }, display_line_overrides = new[] { true, true, true, true },
            configuration_code_marker = "abcdef", locked_footer_line = "锁定者Fixture : abcdef",
            configuration_id = "302c759c-8798-48e9-a0dd-bb1a93418b8b", configuration_sha256 = new string('a', 64),
            producer_version = producer, settings_contract = "PAL98.Settings.v1", minimum_runtime = runtime, minimum_timer = timer,
            dependencies = new[] { new { path = "DATA.MKF", size = dependency.Length, sha256 = Hash(dependency) } },
            absent_files = new string[0], files = files.ToArray()
        };
        byte[] body = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(manifest));
        File.WriteAllBytes(Path.Combine(active, "manifest.json"), body);
        // Same isolated fixture convention as signed_locks. Never log/store key.
        byte[] key = Encoding.ASCII.GetBytes((string)Call(host, "TournamentLockInfoReader", "GetIntegrityKey"));
        try {
            using (var hmac = new HMACSHA256(key))
                File.WriteAllText(Path.Combine(active, "manifest.sig"), BitConverter.ToString(hmac.ComputeHash(body)).Replace("-", "").ToLowerInvariant(), Encoding.ASCII);
        } finally { Array.Clear(key, 0, key.Length); }
        // A writable live copy must not override signed endpoint or membership.
        Directory.CreateDirectory(Path.Combine(root, "palmod"));
        File.WriteAllText(Path.Combine(root, "palmod", "common-tools.v1.json"), SettingsJson.Replace("fixture.invalid", "untrusted.invalid"));
    }
    private static void Rejected(Assembly online, string root, string message)
    {
        try { Call(online, "CompetitionGameSettings", "LoadDirectory", root); }
        catch (TargetInvocationException e) { if (e.InnerException is InvalidDataException) return; throw; }
        throw new Exception(message);
    }
    private static void ConfigurationReaders(IPalTimerOnlineV1 module)
    {
        var host = typeof(IPalTimerOnlineV1).Assembly; var online = module.GetType().Assembly;
        var versions = new[] {
            new[] { "1.6.8.17", "1.6.8.17", "3.37.7.6" },
            new[] { "1.7.0.0", "1.7.0.0", "3.37.7.16" },
            new[] { "1.7.1.0", "1.7.1.0", "3.37.7.17" },
            new[] { "1.7.1.1", "1.7.1.1", "3.37.7.17" }
        };
        foreach (var v in versions) {
            foreach (var assembly in new[] { host, online })
                if (!(bool)Call(assembly, "TournamentLockInfoReader", "SupportedLockVersions", v[0], "PAL98.Settings.v1", v[1], v[2]))
                    throw new Exception(assembly.GetName().Name + " rejects supported signed lock " + v[0]);
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configuration-fixtures", v[0]);
            WriteLock(host, root, v[0], v[1], v[2]);
            string signature = Path.Combine(root, "palmod", "TournamentLock", "v1", "manifest.sig");
            byte[] originalSignature = File.ReadAllBytes(signature);
            var loaded = Call(online, "CompetitionGameSettings", "LoadDirectory", root);
            if (!(bool)Property(loaded, "Enabled") || (string)Property(loaded, "Server") != "https://fixture.invalid" ||
                (string)Property(loaded, "CustomCompetitionId") != "fixture-cup") throw new Exception("Signed server configuration not loaded: " + v[0]);
            if (!originalSignature.SequenceEqual(File.ReadAllBytes(signature))) throw new Exception("Reader rewrote lock.");
            File.AppendAllText(signature, "0"); Rejected(online, root, "Corrupted signature accepted.");
            File.WriteAllBytes(signature, originalSignature);
            File.WriteAllBytes(Path.Combine(root, "DATA.MKF"), new byte[] { 3, 2, 1 }); Rejected(online, root, "Changed dependency accepted.");
            Console.WriteLine("PASS packaged signed settings " + v[0] + "; signature/dependencies remain enforced");
        }
        foreach (var v in new[] { new[] { "1.7.1.1", "1.7.0.0", "3.37.7.17" }, new[] { "99.0.0.0", "99.0.0.0", "3.37.7.17" } }) {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configuration-fixtures", "rejected-" + v[0]);
            WriteLock(host, root, v[0], v[1], v[2]); Rejected(online, root, "Unknown/mixed lock version accepted.");
        }
        string legacy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configuration-fixtures", "legacy-v1");
        WriteLock(host, legacy, "", "", "", 1);
        if ((bool)Property(Call(online, "CompetitionGameSettings", "LoadDirectory", legacy), "Enabled"))
            throw new Exception("Legacy lock inherited unsigned live upload configuration.");
        string unlocked = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configuration-fixtures", "unlocked");
        Directory.CreateDirectory(Path.Combine(unlocked, "palmod"));
        var path = Path.Combine(unlocked, "palmod", "common-tools.v1.json");
        File.WriteAllText(path, SettingsJson);
        var settings = Call(online, "CompetitionGameSettings", "LoadDirectory", unlocked);
        if (!(bool)Property(settings, "Enabled") || Property(settings, "CustomCompetitionId") != null)
            throw new Exception("Unlocked settings must grant daily only.");
        File.WriteAllText(path, SettingsJson.Replace("\"enabled\":true", "\"enabled\":false"));
        if ((bool)Property(Call(online, "CompetitionGameSettings", "LoadDirectory", unlocked), "Enabled"))
            throw new Exception("Disabled configuration enabled online.");
        File.WriteAllText(path, "{}"); Rejected(online, unlocked, "Malformed live configuration accepted.");
        Console.WriteLine("PASS mixed/future/legacy/unlocked/disabled/malformed configuration boundaries");
    }
    [STAThread]
    static int Main(string[] args)
    {
        IDisposable lease = null;
        try {
            bool accepted = false;
            var loader = typeof(IPalTimerOnlineV1).Assembly.GetType("Pal98Timer.OnlineModuleLoader", true);
            try {
                lease = (IDisposable)loader.GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { AppDomain.CurrentDomain.BaseDirectory });
                accepted = true;
            } catch (TargetInvocationException e) { Console.WriteLine("LOAD rejected: " + e.InnerException.GetType().Name); }
            if (accepted != (args[0] == "valid")) throw new Exception("Unexpected component acceptance.");
            if (accepted) {
                var module = (IPalTimerOnlineV1)lease.GetType().GetField("Module", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(lease);
                if (module.ApiVersion != 1) throw new Exception("Wrong API.");
                ConfigurationReaders(module);
                bool writeRejected = false;
                try { using (File.Open("PalTimerOnline.dll", FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { } }
                catch (IOException) { writeRejected = true; }
                if (!writeRejected) throw new Exception("Verified composition was replaceable during use.");
                module.CloseAsync().GetAwaiter().GetResult();
            }
            // Optional component failure does not prevent local stopwatch use.
            var local = new PTimer(); local.Start(); Thread.Sleep(35); local.Stop();
            if (local.CurrentTSOnly.TotalMilliseconds < 20) throw new Exception("Local stopwatch unavailable.");
            Console.WriteLine("PASS loader=" + accepted + "; local stopwatch remains available");
            return 0;
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { lease?.Dispose(); }
    }
}
