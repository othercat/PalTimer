// SPDX-License-Identifier: GPL-2.0-only
// Uses the published timer assembly and two native diagnostic game processes.
// No arbitrary game-memory writes, synthetic MMF replies, production cloud
// client or score uploads. --cloud exercises an offline file transport.
using Pal98Timer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static partial class NativeSdlpalRelayHost
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly Product = typeof(SdlpalGeshi).Assembly;
    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 4194304, RecursionLimit = 64 };
    static readonly List<string> Checks = new List<string>();
    static readonly Dictionary<string, object> Evidence = new Dictionary<string, object>();
    static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Checks.Add(name); Console.WriteLine("PASS " + name);
    }
    static object Call(object target, string name, params object[] args)
    {
        try { return target.GetType().GetMethod(name, Members).Invoke(target, args); }
        catch (TargetInvocationException e) { throw e.InnerException; }
    }
    static object Property(object target, string name) => target.GetType().GetProperty(name, Members).GetValue(target);
    static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    static Dictionary<string, object> ReadNative(Process process)
    {
        using (var map = MemoryMappedFile.OpenExisting("Local\\SDLPAL.LostEdit.v1." + process.Id, MemoryMappedFileRights.Read))
        using (var view = map.CreateViewAccessor(0, 4194304, MemoryMappedFileAccess.Read))
        {
            for (int i = 0; i < 50; i++)
            {
                uint sequence = view.ReadUInt32(20), length = view.ReadUInt32(24);
                if ((sequence & 1) != 0 || length < 2 || length > 4194240) { Thread.Sleep(5); continue; }
                byte[] bytes = new byte[length]; view.ReadArray(64, bytes, 0, bytes.Length);
                Thread.MemoryBarrier();
                if (sequence == view.ReadUInt32(20)) return Json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(bytes));
            }
        }
        throw new IOException("No coherent native snapshot");
    }
    static ulong[] Ack(Process process)
    {
        using (var map = MemoryMappedFile.OpenExisting("Local\\SDLPAL.LostEdit.Control.v1." + process.Id, MemoryMappedFileRights.Read))
        using (var view = map.CreateViewAccessor(0, 128, MemoryMappedFileAccess.Read))
            return new[] { view.ReadUInt64(64), (ulong)view.ReadUInt32(72), view.ReadUInt64(80) };
    }

    sealed class Endpoint : IDisposable
    {
        internal Process Process;
        internal SdlpalGeshi Core;
        internal GRender Renderer;
        internal GForm Ui;
        internal string DirectoryPath;
        readonly Form controlState = new Form();
        internal Endpoint(string directory, int slot, int step, TimeSpan elapsed)
        {
            DirectoryPath = directory;
            var start = new ProcessStartInfo(Path.Combine(directory, "sdlpal.exe"), "--verify-bridge " + slot) {
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };
            start.EnvironmentVariables["SDL_VIDEODRIVER"] = "dummy";
            start.EnvironmentVariables["SDL_AUDIODRIVER"] = "dummy";
            Process = Process.Start(start);
            try
            {
                var wait = Stopwatch.StartNew(); Dictionary<string, object> state = null;
                while (wait.ElapsedMilliseconds < 10000)
                {
                    if (Process.HasExited) throw new Exception("Native fixture exited before attach: " + Process.ExitCode);
                    try { state = ReadNative(Process); if ((bool)state["canControl"]) break; }
                    catch (FileNotFoundException) { }
                    Thread.Sleep(20);
                }
                Check(state != null && (bool)state["diagnostic"] && (bool)state["canControl"], "native endpoint " + slot + " is diagnostic and ready");
                // Do not run GForm's startup, cloud, hotkeys or networking. Donate
                // an unshown Control window field for IsHandleCreated checks.
                var form = Ui = (GForm)FormatterServices.GetUninitializedObject(typeof(GForm));
                var window = typeof(Control).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance);
                window.SetValue(form, window.GetValue(controlState));
                Core = new SdlpalGeshi(form);
                Call(Core, "InitCheckPoints");
                form.rr = Renderer = new GRender(controlState);
                foreach (var point in Core.CheckPoints) point.SetUIItem(Renderer.AddItem(point.GetNickName(), point.Best));
                object bridge = Activator.CreateInstance(Product.GetType("Pal98Timer.SdlpalBridge"), true);
                try { Call(bridge, "Attach", Process.GetProcessById(Process.Id)); }
                catch { ((IDisposable)bridge).Dispose(); throw; }
                typeof(SdlpalGeshi).GetField("bridge", Members).SetValue(Core, bridge);
                Call(Core, "OnTick"); Core.IsUIPause = true; Core.SetTS(elapsed); Core.Jump(step);
                if (step == 1) { Core.CheckPoints[0].SetCurrentTSForLoad(TimeSpan.FromSeconds(20)); Core.CheckPoints[0].Status = CheckPointStatus.Completed; }
                Evidence["initial" + slot] = state;
            }
            catch { Dispose(); throw; }
        }
        internal void Relay(string method, string path)
        {
            var callback = new RelayCallback { Core = Core, Method = method, Path = path };
            var run = typeof(SdlpalGeshi).GetMethod("RunRelay", Members);
            Type callbackType = run.GetParameters()[0].ParameterType;
            var invoke = typeof(RelayCallback).GetMethod("Invoke").MakeGenericMethod(callbackType.GenericTypeArguments[0]);
            Delegate operation = Delegate.CreateDelegate(callbackType, callback, invoke);
            // There is no UI message loop in this opt-in integration host.
            SynchronizationContext.SetSynchronizationContext(null);
            ((Task)Call(Core, "RunRelay", operation)).GetAwaiter().GetResult();
        }
        internal string Slot250 => Path.Combine((string)ReadNative(Process)["savePath"], "250.rpg");
        public void Dispose()
        {
            Core?.Unload();
            if (Process != null) { if (!Process.HasExited) { Process.Kill(); Process.WaitForExit(3000); } Process.Dispose(); Process = null; }
            controlState.Dispose();
        }
    }
    sealed class RelayCallback
    {
        internal SdlpalGeshi Core; internal string Method, Path;
        public Task Invoke<T>(T snapshot) => (Task)Call(Core, Method, Path, snapshot);
    }
    static void VerifyLoaded(Endpoint receiver, int originalSlot, int scene, long cash, int step, TimeSpan elapsed, ulong epoch)
    {
        var actual = ReadNative(receiver.Process);
        Check(Convert.ToInt32(actual["scene"]) == scene && Convert.ToInt64(actual["cash"]) == cash, "native game " + originalSlot + " loaded the other sender's scene and cash");
        Check(Convert.ToUInt64(actual["epoch"]) == epoch + 1 && Convert.ToInt32(actual["loadedFrom"]) == 250 && Convert.ToInt32(actual["saveSlot"]) == originalSlot,
            "native game " + originalSlot + " advances epoch and preserves ordinary save slot");
        Check(receiver.Core.GetMainWatch() == elapsed && receiver.Core.CurrentStep == step && receiver.Core.IsUIPause,
            "timer " + originalSlot + " restores exact time and node while paused");
        Check(receiver.Core.CheckPoints.Select((point, index) => receiver.Renderer.GetItem(index).Status == point.Status &&
                  receiver.Renderer.GetItem(index).Cur == point.Current).All(matches => matches) &&
              receiver.Core.GetPointSpan() == (step == 1 ? "[0~1] 0:20.00" : "--"),
            "timer " + originalSlot + " renderer and displayed interval match restored data");
        Check(receiver.Core.GetScoreValidationError().Contains("诊断") && !(bool)Property(receiver.Core, "CompetitionPublicationAllowed"),
            "timer " + originalSlot + " still blocks competition publication");
        Evidence["loaded" + originalSlot] = actual;
        Evidence["timer" + originalSlot] = Json.DeserializeObject(receiver.Core.GetRStr());
    }
    static void Run()
    {
        Type settings = Product.GetType("Pal98Timer.TimerUserSettings"), storeType = Product.GetType("Pal98Timer.TimerSettingsStore");
        object store = Activator.CreateInstance(storeType, Members, null, new object[] { Root, Path.Combine(Root, "localappdata") }, null);
        settings.GetField("store", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, store);
        settings.GetMethod("ClearPathCache", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        SoundConfig.ins.GlobalEnabled = false;
        TimeSpan timeA = TimeSpan.FromMilliseconds(37500), timeB = TimeSpan.FromMilliseconds(77250);
        using (var a = new Endpoint(Path.Combine(Root, "game-a"), 1, 1, timeA))
        using (var b = new Endpoint(Path.Combine(Root, "game-b"), 2, 0, timeB))
        {
            string pathA = Path.Combine(Root, "sender-a.sdlrelay"), pathB = Path.Combine(Root, "sender-b.sdlrelay");
            a.Relay("SavePackage", pathA); b.Relay("SavePackage", pathB);
            byte[] saveA = File.ReadAllBytes(a.Slot250), saveB = File.ReadAllBytes(b.Slot250);
            Check(saveA.Length == 194288 && saveB.Length == 194288 && !saveA.SequenceEqual(saveB), "two native SAVE acknowledgements produce distinct complete RPG files");
            foreach (var pair in new[] { new { Path = pathA, Bytes = saveA }, new { Path = pathB, Bytes = saveB } })
            {
                var package = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(pair.Path));
                Check(Convert.FromBase64String((string)package["saveBase64"]).SequenceEqual(pair.Bytes) && (string)package["saveSha256"] == Hash(pair.Bytes),
                    "published SavePackage preserves exact acknowledged bytes: " + Path.GetFileName(pair.Path));
            }
            ulong epochA = Convert.ToUInt64(ReadNative(a.Process)["epoch"]), epochB = Convert.ToUInt64(ReadNative(b.Process)["epoch"]);
            b.Relay("LoadPackage", pathA); a.Relay("LoadPackage", pathB);
            VerifyLoaded(a, 1, 93, 9817, 0, timeB, epochA);
            VerifyLoaded(b, 2, 227, 31265, 1, timeA, epochB);
            Check(b.Core.CheckPoints[0].Status == CheckPointStatus.Completed && b.Core.CheckPoints[0].Current == TimeSpan.FromSeconds(20), "completed sender split and its time survive import");
            Check(Directory.GetFiles(Path.Combine(a.DirectoryPath, "save"), "250.rpg.backup-*").Any(p => File.ReadAllBytes(p).SequenceEqual(saveA)) &&
                  Directory.GetFiles(Path.Combine(b.DirectoryPath, "save"), "250.rpg.backup-*").Any(p => File.ReadAllBytes(p).SequenceEqual(saveB)), "both receivers preserve the previous reserved-slot bytes");

            // Bad package identity must fail before writing a command or file.
            var bad = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(pathA));
            bad["identity"] = "different-engine";
            string invalid = Path.Combine(Root, "wrong-identity.sdlrelay"); File.WriteAllText(invalid, Json.Serialize(bad), new UTF8Encoding(false));
            ulong ack = Ack(a.Process)[0]; string before = Hash(File.ReadAllBytes(a.Slot250)); bool rejected = false;
            try { a.Relay("LoadPackage", invalid); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && Ack(a.Process)[0] == ack && Hash(File.ReadAllBytes(a.Slot250)) == before && a.Core.GetMainWatch() == timeB, "wrong identity never reaches native LOAD or changes the timer/save");

            // A correctly hashed but truncated RPG passes package validation,
            // reaches the real engine, and must not restore game or timer state.
            bad = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(pathA));
            byte[] truncated = saveA.Take(1024).ToArray(); bad["saveBase64"] = Convert.ToBase64String(truncated); bad["saveSha256"] = Hash(truncated);
            invalid = Path.Combine(Root, "native-invalid.sdlrelay"); File.WriteAllText(invalid, Json.Serialize(bad), new UTF8Encoding(false));
            rejected = false; try { a.Relay("LoadPackage", invalid); } catch (IOException) { rejected = true; }
            var response = Ack(a.Process); var unchanged = ReadNative(a.Process);
            Check(rejected && response[0] != ack && response[1] == 4 && Convert.ToUInt64(unchanged["epoch"]) == epochA + 1 &&
                  Convert.ToInt32(unchanged["scene"]) == 93 && Convert.ToInt64(unchanged["cash"]) == 9817 && a.Core.GetMainWatch() == timeB && a.Core.IsUIPause,
                  "native rejection of a truncated RPG leaves the live game and timer unchanged");
            Evidence["nativeRejectedAck"] = response;
            Check(a.Process.WaitForExit(25000) && b.Process.WaitForExit(25000) && a.Process.ExitCode == 0 && b.Process.ExitCode == 0, "both real native fixtures exit successfully");
        }
    }
    [STAThread] static int Main(string[] args)
    {
        bool cloudMode = args.Contains("--cloud");
        bool passed = false;
        try { if (cloudMode) RunCloud(); else Run(); passed = true; return 0; }
        catch (Exception error) { Evidence["error"] = error.ToString(); Console.Error.WriteLine(error); return 1; }
        finally
        {
            Evidence["schema"] = "PalTimer.NativeGeshiRelayAcceptance.v1"; Evidence["passed"] = passed; Evidence["checks"] = Checks;
            Evidence["timerExeSha256"] = Hash(File.ReadAllBytes(Product.Location));
            Evidence["scope"] = cloudMode
                ? "Published CloudSave/CloudLoad operation delegates, GForm and CloudSessionRunner with two real diagnostic engines and offline file transport; no production cloud, credentials, outer dialogs or score uploads."
                : "Published SavePackage/LoadPackage/RunRelay with real diagnostic engines on one host; not GUI dialogs, two physical computers or cloud transport.";
            File.WriteAllText(Path.Combine(Root, "result.json"), Json.Serialize(Evidence), new UTF8Encoding(false));
            Console.WriteLine("Checks=" + Checks.Count + " passed=" + passed);
        }
    }
}
