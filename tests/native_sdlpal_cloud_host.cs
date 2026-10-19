// SPDX-License-Identifier: GPL-2.0-only
// Companion to native_sdlpal_relay_host.cs. All timer/game bytes are copied
// from the delivered package. Only ICloudStepClient is an offline substitute.
using Pal98Timer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static partial class NativeSdlpalRelayHost
{
    const BindingFlags All = Members | BindingFlags.Static;
    static FieldInfo Field(Type type, string name)
    {
        for (; type != null; type = type.BaseType)
        {
            var field = type.GetField(name, All | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new MissingFieldException(name);
    }
    static void Set(object target, string name, object value) => Field(target.GetType(), name).SetValue(target, value);
    static Delegate Adapt(Type type, Func<object[], object> invoke)
    {
        var signature = type.GetMethod("Invoke");
        var args = signature.GetParameters().Select(p => Expression.Parameter(p.ParameterType)).ToArray();
        Expression body = Expression.Invoke(Expression.Constant(invoke), Expression.NewArrayInit(typeof(object), args.Select(p => Expression.Convert(p, typeof(object)))));
        body = signature.ReturnType == typeof(void) ? (Expression)Expression.Block(body, Expression.Empty()) : Expression.Convert(body, signature.ReturnType);
        return Expression.Lambda(type, body, args).Compile();
    }
    sealed class OfflineFiles : RealProxy
    {
        readonly string directory;
        readonly int id;
        internal int Uploads, Downloads;
        internal bool FailUpload, FailDownload;
        internal Action AfterDownload;
        internal string LastLocal, LastRemote;
        internal OfflineFiles(string directory, int id) : base(Product.GetType("Pal98Timer.ICloudStepClient", true))
        { this.directory = directory; this.id = id; }
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            try
            {
                object result = null;
                switch (call.MethodName)
                {
                    case "get_Initialized": result = true; break;
                    case "get_NextDataKind": result = 2; break;
                    case "Step":
                        result = Activator.CreateInstance(Product.GetType("Pal98Timer.CloudStepResult", true), true);
                        Set(result, "Initialized", true); Set(result, "Id", id); break;
                    case "Upload":
                        ++Uploads; LastLocal = (string)call.Args[0]; LastRemote = (string)call.Args[1];
                        if (FailUpload) throw new IOException("offline upload unavailable");
                        File.Copy(LastLocal, Remote(LastRemote), false); break;
                    case "Download":
                        ++Downloads; LastRemote = (string)call.Args[0]; LastLocal = (string)call.Args[1];
                        if (FailDownload) throw new IOException("offline download unavailable");
                        File.Copy(Remote(LastRemote), LastLocal, false); AfterDownload?.Invoke(); break;
                    case "Stop": break;
                    default: throw new InvalidOperationException("Unexpected offline call: " + call.MethodName);
                }
                return new ReturnMessage(result, null, 0, call.LogicalCallContext, call);
            }
            catch (Exception error) { return new ReturnMessage(error, call); }
        }
        internal string Remote(string key)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(key, "\\Ageshi-[0-9]+-[a-f0-9]{32}\\.bin\\z"))
                throw new InvalidDataException("Unsafe offline object key");
            return Path.Combine(directory, key);
        }
    }
    sealed class CloudEndpoint : IDisposable
    {
        internal readonly Endpoint Game;
        internal readonly OfflineFiles Files;
        internal readonly ToolStripMenuItem SaveMenu = new ToolStripMenuItem(), LoadMenu = new ToolStripMenuItem();
        readonly object runner;
        internal bool Valid = true;
        internal CloudEndpoint(Endpoint game, string directory, int id)
        {
            Game = game; Files = new OfflineFiles(directory, id);
            Set(Game.Core, "cloudSave", SaveMenu); Set(Game.Core, "cloudLoad", LoadMenu);
            Game.Core.OnCloudPending();
            Check(!SaveMenu.Enabled && !LoadMenu.Enabled, "unactivated cloud menus remain disabled");
            bool denied = false;
            try { Game.Ui.OUpload("must-not-be-read.bin", "must-not-be-sent.bin"); }
            catch (Exception error) { denied = error.Message.Contains("版本不匹配"); }
            Check(denied && Files.Uploads == 0, "unactivated upload never reaches transport or reads a file");
            denied = false;
            try { Game.Ui.ODownload("must-not-be-read.bin", "must-not-be-written.bin"); }
            catch (Exception error) { denied = error.Message.Contains("版本不匹配"); }
            Check(denied && Files.Downloads == 0, "unactivated download never reaches transport or writes a file");
            var constructor = Product.GetType("Pal98Timer.CloudSessionRunner", true).GetConstructors(All).Single();
            runner = constructor.Invoke(new object[] { Adapt(constructor.GetParameters()[0].ParameterType, a => Files.GetTransparentProxy()), 5, null });
            Set(Game.Ui, "cloudRunner", runner); Bind();
        }
        internal void Bind()
        {
            Valid = true;
            long run = Game.Core.ScoreRunSequence;
            var method = runner.GetType().GetMethod("Start", All); var p = method.GetParameters();
            object session = method.Invoke(runner, new object[] { Game.Core.CoreName, new Func<bool>(() => Valid && run == Game.Core.ScoreRunSequence),
                Adapt(p[2].ParameterType, a => {
                    var payload = Activator.CreateInstance(Product.GetType("Pal98Timer.CloudPayload", true), true);
                    Set(payload, "SuppressScores", true); return payload;
                }), Adapt(p[3].ParameterType, a => null) });
            Set(Game.Ui, "cloud", session);
            Check(SpinWait.SpinUntil(() => Game.Ui.CloudID() >= 0, 3000), "offline cloud file session initializes independently of ranked score eligibility");
            Game.Core.OnCloudOK(); Check(SaveMenu.Enabled && LoadMenu.Enabled, "initialized cloud enables both menus");
        }
        public void Dispose()
        {
            ((IDisposable)runner).Dispose();
            Check((bool)Call(runner, "WaitForExit", 3000), "offline cloud worker terminates");
            SaveMenu.Dispose(); LoadMenu.Dispose();
        }
    }
    static bool Generated(Type type) => type.IsDefined(typeof(CompilerGeneratedAttribute), false);
    static object CloudClosure(Type type, SdlpalGeshi core, string path, string code)
    {
        var closure = FormatterServices.GetUninitializedObject(type);
        foreach (var field in type.GetFields(All | BindingFlags.DeclaredOnly))
        {
            if (field.FieldType == typeof(SdlpalGeshi)) field.SetValue(closure, core);
            else if (field.Name == "path") field.SetValue(closure, path);
            else if (field.Name == "code") field.SetValue(closure, code);
            else if (Generated(field.FieldType) && field.FieldType.DeclaringType == typeof(SdlpalGeshi))
                field.SetValue(closure, CloudClosure(field.FieldType, core, path, code));
        }
        return closure;
    }
    static void CloudOperation(Endpoint game, bool upload, string path, string code)
    {
        // LOAD acknowledgement is published before the next safe exploration
        // frame. A human opens the menu later; the fast host must wait for that
        // real frame rather than bypass the production canControl guard.
        if (!SpinWait.SpinUntil(() => !game.Process.HasExited && (bool)ReadNative(game.Process)["canControl"], 3000))
            throw new IOException("Native endpoint did not return to a safe exploration frame");
        // Invoke the actual compiled operation passed by CloudSave/CloudLoad to
        // RunRelay. Outer dialogs/code entry are deliberately not fabricated.
        string prefix = upload ? "<CloudSave>" : "<CloudLoad>";
        var methods = Product.GetTypes().Where(t => t.DeclaringType == typeof(SdlpalGeshi) && Generated(t))
            .SelectMany(t => t.GetMethods(All | BindingFlags.DeclaredOnly))
            .Where(m => m.Name.StartsWith(prefix, StringComparison.Ordinal) && m.ReturnType == typeof(Task) && m.GetParameters().Length == 1).ToArray();
        if (methods.Length != 1) throw new InvalidDataException("Cannot identify unique production " + prefix + " operation");
        var run = typeof(SdlpalGeshi).GetMethod("RunRelay", Members);
        var operation = Delegate.CreateDelegate(run.GetParameters()[0].ParameterType, CloudClosure(methods[0].DeclaringType, game.Core, path, code), methods[0]);
        SynchronizationContext.SetSynchronizationContext(null);
        try { ((Task)Call(game.Core, "RunRelay", operation)).GetAwaiter().GetResult(); }
        finally
        {
            // Same production temporary cleanup used by both outer handlers.
            // Failure currently requires a new CloudSave; it does not retain a
            // reusable .bin. Verify that retry can reproduce the live state.
            typeof(SdlpalGeshi).GetMethod("TryDeleteTemporary", All).Invoke(null, new object[] { path });
        }
    }
    static string NewCode() => "geshi-42-" + Guid.NewGuid().ToString("N");
    static string Temporary() => Path.Combine(Root, "cloud-operation-" + Guid.NewGuid().ToString("N") + ".bin");
    static Dictionary<string, object> Package(string path) => Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
    static void RejectDownload(CloudEndpoint receiver, string code, string name, bool nativeReject = false)
    {
        var game = receiver.Game;
        var before = ReadNative(game.Process); ulong ack = Ack(game.Process)[0];
        int downloads = receiver.Files.Downloads;
        string previousSave = Hash(File.ReadAllBytes(game.Slot250));
        var elapsed = game.Core.GetMainWatch(); int step = game.Core.CurrentStep;
        string temporary = Temporary(); bool rejected = false;
        try { CloudOperation(game, false, temporary, code); }
        catch (Exception error) when (error is IOException || error is InvalidDataException || error is ArgumentException || error is InvalidOperationException || error is FormatException ||
            name == "truncated JSON" && error is System.Resources.MissingManifestResourceException)
        { rejected = true; Evidence["rejected: " + name] = error.GetType().Name + ": " + error.Message; }
        var after = ReadNative(game.Process);
        Check(receiver.Files.Downloads == downloads + 1 && receiver.Files.LastRemote == code + ".bin",
            name + " reaches the intended cloud download before rejection");
        Check(rejected && game.Core.IsUIPause && game.Core.GetMainWatch() == elapsed && game.Core.CurrentStep == step,
            name + " leaves the timer and node unchanged and paused");
        Check(Convert.ToUInt64(after["epoch"]) == Convert.ToUInt64(before["epoch"]) && Convert.ToInt32(after["scene"]) == Convert.ToInt32(before["scene"]) &&
            Convert.ToInt64(after["cash"]) == Convert.ToInt64(before["cash"]), name + " leaves the live native game unchanged");
        Check(!File.Exists(temporary), name + " cleans its temporary download");
        if (nativeReject)
            Check(Ack(game.Process)[0] != ack && Ack(game.Process)[1] == 4 && Directory.GetFiles(Path.GetDirectoryName(game.Slot250), "250.rpg.backup-*").Any(p => Hash(File.ReadAllBytes(p)) == previousSave),
                name + " preserves previous reserved-slot bytes in an atomic backup");
        else Check(Ack(game.Process)[0] == ack && Hash(File.ReadAllBytes(game.Slot250)) == previousSave, name + " issues no native command or reserved-slot overwrite");
    }
    static void RunCloud()
    {
        Type settings = Product.GetType("Pal98Timer.TimerUserSettings"), storeType = Product.GetType("Pal98Timer.TimerSettingsStore");
        object store = Activator.CreateInstance(storeType, Members, null, new object[] { Root, Path.Combine(Root, "localappdata") }, null);
        settings.GetField("store", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, store);
        settings.GetMethod("ClearPathCache", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        SoundConfig.ins.GlobalEnabled = false;
        string cloud = Path.Combine(Root, "offline-cloud-objects"); Directory.CreateDirectory(cloud);
        var timeA = TimeSpan.FromMilliseconds(37500); var timeB = TimeSpan.FromMilliseconds(77250);
        using (var a = new Endpoint(Path.Combine(Root, "game-a"), 1, 1, timeA))
        using (var b = new Endpoint(Path.Combine(Root, "game-b"), 2, 0, timeB))
        using (var ca = new CloudEndpoint(a, cloud, 42))
        using (var cb = new CloudEndpoint(b, cloud, 43))
        {
            string codeA = NewCode(), codeB = NewCode();
            foreach (var entry in new[] { new { Endpoint = ca, Code = codeA }, new { Endpoint = cb, Code = codeB } })
            {
                string path = Temporary(); CloudOperation(entry.Endpoint.Game, true, path, entry.Code);
                var package = Package(entry.Endpoint.Files.Remote(entry.Code + ".bin"));
                Check(entry.Endpoint.Files.LastRemote == entry.Code + ".bin" && entry.Endpoint.Files.LastLocal == path,
                    "production cloud upload passes the correct local path and opaque object key");
                Check(Convert.FromBase64String((string)package["saveBase64"]).SequenceEqual(File.ReadAllBytes(entry.Endpoint.Game.Slot250)),
                    "cloud object contains the exact real native acknowledged save");
                Check(!File.Exists(path) && entry.Endpoint.Game.Core.IsUIPause, "successful upload cleans its temporary and stays paused");
            }
            ulong epochA = Convert.ToUInt64(ReadNative(a.Process)["epoch"]), epochB = Convert.ToUInt64(ReadNative(b.Process)["epoch"]);
            CloudOperation(b, false, Temporary(), codeA); CloudOperation(a, false, Temporary(), codeB);
            VerifyLoaded(a, 1, 93, 9817, 0, timeB, epochA); VerifyLoaded(b, 2, 227, 31265, 1, timeA, epochB);
            Check(b.Core.CheckPoints[0].Status == CheckPointStatus.Completed && b.Core.CheckPoints[0].Current == TimeSpan.FromSeconds(20), "cloud restore preserves completed split state and time");
            ca.Bind(); cb.Bind();
            string failedCode = NewCode(), failedPath = Temporary(); ca.Files.FailUpload = true;
            bool failed = false;
            try { CloudOperation(a, true, failedPath, failedCode); } catch (IOException) { failed = true; }
            Check(failed && !File.Exists(ca.Files.Remote(failedCode + ".bin")) && !File.Exists(failedPath) && a.Core.IsUIPause && a.Core.GetMainWatch() == timeB,
                "upload failure reports failure, cleans temporary only, and preserves paused timer");
            ca.Files.FailUpload = false; string retry = NewCode(); CloudOperation(a, true, Temporary(), retry);
            var retried = Package(ca.Files.Remote(retry + ".bin"));
            Check(Convert.FromBase64String((string)retried["saveBase64"]).SequenceEqual(File.ReadAllBytes(a.Slot250)) &&
                (string)Json.Deserialize<Dictionary<string, object>>((string)retried["timer"])["Current"] == "00:01:17.250", "new cloud-save attempt after failure reexports the complete current game and timer");
            ca.Files.FailDownload = true; RejectDownload(ca, codeB, "download failure"); ca.Files.FailDownload = false;
            CloudOperation(a, false, Temporary(), retry);
            Check(a.Core.GetMainWatch() == timeB && a.Core.IsUIPause && Convert.ToInt64(ReadNative(a.Process)["cash"]) == 9817, "download retry restores the exact uploaded state"); ca.Bind();
            var mutations = new Dictionary<string, Action<Dictionary<string, object>>> {
                { "wrong engine/resource identity", p => p["identity"] = "different-engine" },
                { "wrong package schema", p => p["schema"] = "unknown" },
                { "corrupt save checksum", p => p["saveSha256"] = new string('0',64) },
                { "invalid base64 save", p => p["saveBase64"] = "?" },
                { "invalid timer core", p => { var t = Json.Deserialize<Dictionary<string, object>>((string)p["timer"]); t["TimerCore"] = "PAL98"; p["timer"] = Json.Serialize(t); } },
                { "invalid timer node", p => { var t = Json.Deserialize<Dictionary<string, object>>((string)p["timer"]); t["Step"] = 99; p["timer"] = Json.Serialize(t); } }
            };
            foreach (var mutation in mutations)
            {
                string code = NewCode(); var bad = Package(ca.Files.Remote(codeB + ".bin")); mutation.Value(bad);
                File.WriteAllText(ca.Files.Remote(code + ".bin"), Json.Serialize(bad)); RejectDownload(ca, code, mutation.Key);
            }
            string malformed = NewCode(); File.WriteAllText(ca.Files.Remote(malformed + ".bin"), "{"); RejectDownload(ca, malformed, "truncated JSON");
            string large = NewCode(); File.WriteAllBytes(ca.Files.Remote(large + ".bin"), new byte[5 * 1024 * 1024 + 1]); RejectDownload(ca, large, "oversized cloud object");
            ca.Files.AfterDownload = () => ca.Valid = false; RejectDownload(ca, codeB, "session invalidated during transfer"); ca.Files.AfterDownload = null; ca.Bind();
            string truncated = NewCode(); var badNative = Package(ca.Files.Remote(codeB + ".bin")); byte[] shortSave = Convert.FromBase64String((string)badNative["saveBase64"]).Take(1024).ToArray();
            badNative["saveBase64"] = Convert.ToBase64String(shortSave); badNative["saveSha256"] = Hash(shortSave);
            File.WriteAllText(ca.Files.Remote(truncated + ".bin"), Json.Serialize(badNative)); RejectDownload(ca, truncated, "native rejection of truncated RPG", true);
            Check(ca.Files.Uploads == 3 && cb.Files.Uploads == 1 && ca.Files.Downloads == 13 && cb.Files.Downloads == 1, "all file operations passed through the real GForm and serialized cloud runner");
            Check(a.Core.GetScoreValidationError().Contains("诊断") && b.Core.GetScoreValidationError().Contains("诊断"), "cloud file success does not grant ranked eligibility");
            Check(a.Process.WaitForExit(25000) && b.Process.WaitForExit(25000) && a.Process.ExitCode == 0 && b.Process.ExitCode == 0, "both native cloud fixtures exit successfully");
            Evidence["cloudUploads"] = ca.Files.Uploads + cb.Files.Uploads; Evidence["cloudDownloads"] = ca.Files.Downloads + cb.Files.Downloads;
            Evidence["network"] = false; Evidence["outerDialogsExecuted"] = false;
        }
    }
}
