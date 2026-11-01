// Fake-transport integration. The fixture resolver grants only throwaway roots.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pal98Timer;
using Pal98.LocalDisplay;

internal static partial class CompetitionBehavior
{
    static CompetitionPlayer IdentityPlayer(string name, bool bound = true) =>
        new CompetitionPlayer { id = bound ? "11111111-1111-4111-8111-111111111111" : null, bound = bound, display_name = name };
    static async Task LocalIdentityCases()
    {
        string directory = Path.Combine(root, "identity-a"), nextDirectory = Path.Combine(root, "identity-b");
        Directory.CreateDirectory(directory); Directory.CreateDirectory(nextDirectory);
        string selected = directory, endpoint = Config().Server, runToken = null;
        var wire = new Fake { Player = IdentityPlayer("Captain船长2092") };
        var store = Store("display-identity"); int target = 0;
        using (var process = Process.GetCurrentProcess()) {
            var client = new CompetitionClient(store, wire, new FakeAuth(), true, resolveGame: p =>
                new CompetitionGameResolution { Settings = new CompetitionSettings { Enabled = true, Server = endpoint },
                    IdentityRoot = selected, Executable = process.MainModule.FileName, ProcessId = process.Id,
                    CreatedAt = process.StartTime.ToUniversalTime().ToFileTimeUtc() });
            Func<string, PlayerIdentity> read = dir => LocalPlayerIdentity.Load(dir).Value;
            Action<string> manual = name => LocalPlayerIdentity.Save(selected, LocalPlayerIdentity.Manual(name), null, false);
            Func<string, byte[]> bytes = dir => File.ReadAllBytes(Path.Combine(dir, LocalPlayerIdentity.RelativePath));
            Func<Task> menu = async () => {
                int registrations = wire.Registers; client.ObserveTarget(process.Id, ++target);
                await Until(() => client.Enabled && wire.Registers > registrations, "new target enters menu and registers");
                await Task.Delay(300);
            };
            Action start = () => {
                runToken = Guid.NewGuid().ToString("N");
                client.Invalidate(runToken);
                client.Publish(Observation(runToken, 0));
                client.PublishClock(runToken, 100, true, Stopwatch.GetTimestamp());
            };
            Func<Task> retry = async () => {
                int registrations = wire.Registers; client.Retry();
                await Until(() => wire.Registers > registrations, "fresh registration reply received");
                await Task.Delay(300);
            };
            manual("鼻祖"); byte[] before = bytes(directory);
            await menu();
            Check(before.SequenceEqual(bytes(directory)), "menu registration does not overwrite local identity");
            client.Publish(Observation("paused-menu", 0));
            client.PublishClock("paused-menu", 100, false, Stopwatch.GetTimestamp());
            await Task.Delay(400);
            Check(before.SequenceEqual(bytes(directory)), "paused clock cannot start identity synchronization");
            start();
            await Until(() => read(directory)?.display_name == "Captain船长2092", "entering gameplay and starting timer synchronizes identity");
            Check(read(directory).source == "timer" && read(directory).bound && read(directory).player_id == wire.Player.id,
                "server identity retains stable player ID and binding");
            Check(!File.Exists(Path.Combine(store.Root, LocalPlayerIdentity.RelativePath)), "timer storage directory never receives game identity");
            manual("鼻祖"); before = bytes(directory); await retry();
            client.Publish(Observation(runToken, 1));
            client.PublishClock(runToken, 1000, false, Stopwatch.GetTimestamp());
            client.PublishClock(runToken, 1001, true, Stopwatch.GetTimestamp());
            await Task.Delay(500);
            Check(before.SequenceEqual(bytes(directory)), "subsequent responses, pause and resume do not resynchronize this game process");
            await menu();
            Check(before.SequenceEqual(bytes(directory)), "restarted game still preserves edit while in menu");
            start();
            await Until(() => read(directory)?.source == "timer", "same server name overwrites manual edit on next gameplay start");

            manual("丿灬夜狼"); await menu(); before = bytes(directory);
            wire.RegisterStatus = 503; start();
            await Until(() => File.ReadAllText(Path.Combine(store.Root, "network.log")).Contains("device-register-retry"),
                "failed gameplay-start synchronization exercised");
            Check(before.SequenceEqual(bytes(directory)) && client.Enabled, "HTTP failure preserves offline identity and timing");
            wire.RegisterStatus = 200; wire.RegistrationBody = "invalid json"; await retry();
            Check(before.SequenceEqual(bytes(directory)), "malformed response cannot clear manual name");
            wire.RegistrationBody = null; wire.Player = null; await retry();
            Check(before.SequenceEqual(bytes(directory)), "missing player is not an explicit unbinding");
            wire.Player = IdentityPlayer("bad ID"); wire.Player.id = "bad"; await retry();
            Check(before.SequenceEqual(bytes(directory)), "invalid bound player ID preserves cache");
            wire.Player = IdentityPlayer("", false); await retry();
            Check(read(directory).source == "timer" && !read(directory).bound && read(directory).display_name == "" && read(directory).player_id == "",
                "successful explicit unbinding clears previous name and ID");
            manual("鼻祖"); wire.Player = IdentityPlayer(" "); await menu(); start();
            await Until(() => read(directory)?.source == "timer", "empty successful server name overrides local edit");
            Check(!read(directory).bound && read(directory).display_name == "", "empty successful name uses default");

            manual("鼻祖"); wire.Player = null; await menu(); start();
            await retry(); wire.Player = IdentityPlayer("Captain船长2092");
            client.Publish(Observation(runToken, 1));
            await Until(() => read(directory)?.display_name == "Captain船长2092", "pending start can complete through validated fresh standings");

            manual("鼻祖"); await menu(); before = bytes(directory);
            var oldGate = new TaskCompletionSource<bool>(); wire.RegisterGate = oldGate;
            int registrations = wire.Registers; start();
            await Until(() => wire.Registers > registrations, "old target start request held in flight");
            selected = nextDirectory; manual("local B");
            wire.Player = IdentityPlayer("丿灬夜狼"); wire.RegisterGate = null;
            client.ObserveTarget(process.Id, ++target); oldGate.SetResult(true);
            await Until(() => wire.Registers > registrations + 1, "next target registers in menu"); await Task.Delay(300);
            Check(before.SequenceEqual(bytes(directory)) && read(nextDirectory).display_name == "local B", "late response changes neither old game nor new menu identity");
            start();
            await Until(() => read(nextDirectory)?.display_name == "丿灬夜狼", "next game synchronizes its own fresh player after timing starts");

            manual("manual before server switch"); before = bytes(nextDirectory);
            endpoint = "https://another.invalid"; wire.Player = IdentityPlayer("鼻祖"); await menu();
            Check(before.SequenceEqual(bytes(nextDirectory)), "server switch alone does not write local identity");
            start();
            await Until(() => read(nextDirectory)?.display_name == "鼻祖", "new server identity synchronized only after gameplay starts");
            Check(read(nextDirectory).server_origin == endpoint, "new identity retains its actual server");

            manual("local before close"); await menu(); before = bytes(nextDirectory);
            oldGate = new TaskCompletionSource<bool>(); wire.RegisterGate = oldGate;
            registrations = wire.Registers; start();
            await Until(() => wire.Registers > registrations, "start request held before target detaches");
            client.ObserveTarget(0, ++target); wire.RegisterGate = null; oldGate.SetResult(true);
            await Until(() => !client.Enabled, "detached target stops publication");
            Check(before.SequenceEqual(bytes(nextDirectory)), "late response after game close preserves local file");
            await client.CloseAsync();
        }
        using (var process = Process.GetCurrentProcess()) {
            var badWire = new Fake { Player = IdentityPlayer("Captain船长2092") };
            var bad = new CompetitionClient(Store("identity-pid"), badWire, new FakeAuth(), true, resolveGame: p =>
                new CompetitionGameResolution { Settings = Config(), IdentityRoot = directory,
                    Executable = process.MainModule.FileName, ProcessId = process.Id, CreatedAt = 1 });
            byte[] before = File.ReadAllBytes(Path.Combine(directory, LocalPlayerIdentity.RelativePath));
            bad.ObserveTarget(process.Id, 1);
            await Until(() => bad.View.Reply?.player != null, "PID incarnation fixture ready");
            bad.Publish(Observation("pid-reused", 0)); bad.PublishClock("pid-reused", 1, true, Stopwatch.GetTimestamp());
            await Until(() => badWire.Registers >= 2, "PID creation validation exercised"); await Task.Delay(300);
            Check(before.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, LocalPlayerIdentity.RelativePath))), "mismatched creation time prevents publication");
            await bad.CloseAsync();
        }
        foreach (int status in new[] { 401, 503 }) using (var process = Process.GetCurrentProcess()) {
            var rejectedWire = new Fake { RegisterStatus = status, Player = IdentityPlayer("Captain船长2092") };
            var rejected = new CompetitionClient(Store("identity-rejected-" + status), rejectedWire, new FakeAuth(), true,
                resolveGame: p => new CompetitionGameResolution { Settings = Config(), IdentityRoot = directory,
                    ProcessId = process.Id, CreatedAt = process.StartTime.ToUniversalTime().ToFileTimeUtc(), Executable = process.MainModule.FileName });
            byte[] before = File.ReadAllBytes(Path.Combine(directory, LocalPlayerIdentity.RelativePath));
            rejected.ObserveTarget(process.Id, 1);
            await Until(() => rejectedWire.Registers == 1 && rejected.LiveDetail.Contains("连接失败"), "menu registration fails with " + status);
            rejected.Publish(Observation("retry-" + status, 0));
            rejected.PublishClock("retry-" + status, 1, true, Stopwatch.GetTimestamp());
            await Task.Delay(700);
            Check(rejectedWire.Registers == 1, "starting gameplay does not bypass rejected credentials or retry deadline: " + status);
            Check(before.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, LocalPlayerIdentity.RelativePath))), "rejected refresh leaves offline identity unchanged: " + status);
            await rejected.CloseAsync();
        }
    }

    static bool PublishedFileContains(string path, string expected)
    {
        // A sharing violation during this bounded poll is not a completed write.
        try { return File.ReadAllText(path).Contains(expected); }
        catch (IOException error) when ((error.HResult & 0xffff) == 32) { return false; }
    }

    static async Task IdentityProgramCases()
    {
        var json = CompetitionProtocol.Json();
        string fixture = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "identity-registration.json");
        var reply = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(fixture));
        var player = (Dictionary<string, object>)reply["player"];
        var identity = LocalPlayerIdentity.Server(Config().Server, (string)player["id"], (string)player["display_name"], true);
        var bytes = IdentityBehaviorSnapshot.Build(reply, identity, identity.player_id, true);
        Check(Encoding.UTF8.GetString(bytes).Contains("我大意了"), "real server response becomes a bounded local program");
        var failures = new[] {
            File.ReadAllText(fixture).Replace("geshi.death-taunt", "unknown.hook"),
            File.ReadAllText(fixture).Replace("\"portrait\"", "\"execute\""),
            File.ReadAllText(fixture).Replace("\"mafan\"", "\"../file\""),
            File.ReadAllText(fixture).Replace("我大意了", new string('长', 25)),
            File.ReadAllText(fixture).Replace("我大意了", "bad\\nline"),
            File.ReadAllText(fixture).Replace("\"color\":79", "\"color\":true"),
            File.ReadAllText(fixture).Replace("\"x\":3", "\"x\":230"),
        };
        foreach (string bad in failures) {
            bool rejected = false;
            try { IdentityBehaviorSnapshot.Build(json.Deserialize<Dictionary<string, object>>(bad), identity, identity.player_id, true); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "invalid remote display program rejected before publication");
        }
        var noBundle = new Dictionary<string, object>(); bool missing = false;
        try { IdentityBehaviorSnapshot.Build(noBundle, identity, identity.player_id, true); } catch (InvalidDataException) { missing = true; }
        Check(missing, "advertised feature cannot silently omit its bundle");
        Check(Encoding.UTF8.GetString(IdentityBehaviorSnapshot.Build(noBundle, identity, identity.player_id, false)).Contains("\"bindings\":[]"), "legacy server produces explicit empty overrides");
        string directory = Path.Combine(root, "identity-program"); Directory.CreateDirectory(directory);
        using (var process = Process.GetCurrentProcess()) {
            var wire = new Fake { RegistrationBody = File.ReadAllText(fixture) };
            var client = new CompetitionClient(Store("program-sync"), wire, new FakeAuth(), true, resolveGame: p =>
                new CompetitionGameResolution { Settings = Config(), IdentityRoot = directory, Executable = process.MainModule.FileName,
                    ProcessId = process.Id, CreatedAt = process.StartTime.ToUniversalTime().ToFileTimeUtc() });
            string identityPath = Path.Combine(directory, LocalPlayerIdentity.RelativePath), behaviorPath = Path.Combine(directory, IdentityBehaviorSnapshot.RelativePath);
            int epoch = 0;
            Func<Task> menu = async () => { int previous = wire.Registers; client.ObserveTarget(process.Id, ++epoch);
                await Until(() => wire.Registers > previous, "script target registers in menu"); await Task.Delay(300); };
            Action start = () => { string token = "script-" + epoch; client.Invalidate(token); client.Publish(Observation(token, 0)); client.PublishClock(token, 1, true, Stopwatch.GetTimestamp()); };
            await menu(); Check(!File.Exists(behaviorPath) && !File.Exists(identityPath), "menu cannot install a remote script"); start();
            await Until(() => File.Exists(identityPath), "gameplay installs remote script and identity");
            var installed = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(behaviorPath));
            Check((string)installed["identity_revision"] == LocalPlayerIdentity.Load(directory).Value.revision &&
                (string)installed["player_id"] == identity.player_id, "behavior and identity have the same generation and player");
            // Export actual timer-produced files for the native pixel/state regression.
            string export = Path.Combine(root, "native-behavior-fixture"); Directory.CreateDirectory(export);
            File.Copy(identityPath, Path.Combine(export, "player-identity.v1.json")); File.Copy(behaviorPath, Path.Combine(export, "player-behavior.v1.json"));
            byte[] priorIdentity = File.ReadAllBytes(identityPath), priorProgram = File.ReadAllBytes(behaviorPath);
            wire.RegistrationBody = failures[1]; await menu(); start(); await Task.Delay(700);
            Check(priorIdentity.SequenceEqual(File.ReadAllBytes(identityPath)) && priorProgram.SequenceEqual(File.ReadAllBytes(behaviorPath)), "invalid remote script preserves both previous files");
            wire.RegistrationBody = File.ReadAllText(fixture).Replace("我大意了", "新的台词"); client.Retry();
            await Until(() => PublishedFileContains(behaviorPath, "新的台词"), "retry applies a new script without a game build");
            var empty = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(fixture));
            ((Dictionary<string, object>)empty["identity_behaviors"])["bindings"] = new object[0]; wire.RegistrationBody = json.Serialize(empty);
            await menu(); start(); await Until(() => PublishedFileContains(behaviorPath, "\"bindings\":[]"), "disabled server binding clears previous override");
            await client.CloseAsync();
        }
        byte[] last = File.ReadAllBytes(Path.Combine(directory, LocalPlayerIdentity.RelativePath));
        byte[] lastBehavior = File.ReadAllBytes(Path.Combine(directory, IdentityBehaviorSnapshot.RelativePath));
        Check(!LocalPlayerIdentity.Save(directory, identity, null, false, replace => false, bytes), "revoked target cannot commit staged files");
        Check(last.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, LocalPlayerIdentity.RelativePath))) &&
            lastBehavior.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, IdentityBehaviorSnapshot.RelativePath))) &&
            Directory.GetFiles(Path.Combine(directory, "palmod"), "*.tmp").Length == 0, "cancelled publication preserves both files and cleans own staging");
    }
}
