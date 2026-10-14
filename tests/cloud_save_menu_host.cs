using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Runtime.Serialization;
using System.Threading;
using Pal98Timer;

// Executes the shipping InitUI upload worker, GForm.OUpload and CloudSessionRunner.
// Only ICloudStepClient is replaced by an offline proxy; no product test hooks,
// real cloud credentials, PAL attachment, player files or visible dialogs are used.
internal static class CloudSaveMenuHost
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Assembly Product = typeof(TimerCore).Assembly;
    static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(c => c.Value);
    static int checks;
    static void Check(bool ok, string message) { ++checks; if (!ok) throw new Exception(message); }
    static FieldInfo Field(Type type, string name)
    {
        for (; type != null; type = type.BaseType) {
            var field = type.GetField(name, All | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new MissingFieldException(name);
    }
    static object Get(object target, string name) => Field(target.GetType(), name).GetValue(target);
    static void Set(object target, string name, object value) => Field(target.GetType(), name).SetValue(target, value);
    static object Call(object target, string name, params object[] args)
    {
        for (var t = target.GetType(); t != null; t = t.BaseType) {
            var method = t.GetMethod(name, All | BindingFlags.DeclaredOnly);
            if (method != null) return method.Invoke(target, args);
        }
        throw new MissingMethodException(name);
    }
    static IEnumerable<MethodBase> Calls(MethodInfo method)
    {
        var body = method.GetMethodBody();
        if (body == null) yield break;
        var bytes = body.GetILAsByteArray();
        for (int i = 0; i < bytes.Length;) {
            short value = bytes[i++];
            if (value == 0xfe) value = (short)(0xfe00 | bytes[i++]);
            var op = Codes[value];
            int size;
            switch (op.OperandType) {
                case OperandType.InlineNone: size = 0; break;
                case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                case OperandType.InlineVar: size = 2; break;
                case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                case OperandType.InlineSwitch: size = 4 + 4 * BitConverter.ToInt32(bytes, i); break;
                default: size = 4; break;
            }
            if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Ldftn)
                yield return method.Module.ResolveMethod(BitConverter.ToInt32(bytes, i));
            i += size;
        }
    }
    static bool Generated(Type t) => t.IsDefined(typeof(CompilerGeneratedAttribute), false);
    static MethodInfo UploadWorker(Type coreType)
    {
        var workers = Product.GetTypes().Where(t => t.DeclaringType == coreType && Generated(t))
            .SelectMany(t => t.GetMethods(All | BindingFlags.DeclaredOnly))
            .Where(m => m.Name.StartsWith("<InitUI>", StringComparison.Ordinal) && Calls(m).Any(c => c.DeclaringType == typeof(GForm) && c.Name == "OUpload")).ToArray();
        Check(workers.Length == 1, coreType.Name + " must have exactly one real menu upload worker");
        return workers[0];
    }
    static Delegate Adapt(Type type, Func<object[], object> invoke)
    {
        var signature = type.GetMethod("Invoke");
        var args = signature.GetParameters().Select(p => Expression.Parameter(p.ParameterType)).ToArray();
        Expression body = Expression.Invoke(Expression.Constant(invoke), Expression.NewArrayInit(typeof(object), args.Select(p => Expression.Convert(p, typeof(object)))));
        body = signature.ReturnType == typeof(void) ? (Expression)Expression.Block(body, Expression.Empty()) : Expression.Convert(body, signature.ReturnType);
        return Expression.Lambda(type, body, args).Compile();
    }
    sealed class OfflineCloud : RealProxy
    {
        internal int Uploads;
        internal byte[] Uploaded;
        internal bool Fail;
        internal OfflineCloud() : base(Product.GetType("Pal98Timer.ICloudStepClient", true)) { }
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            try {
                object result = null;
                switch (call.MethodName) {
                    case "get_Initialized": result = true; break;
                    case "get_NextDataKind": result = 2; break;
                    case "Step":
                        result = Activator.CreateInstance(Product.GetType("Pal98Timer.CloudStepResult", true), true);
                        Set(result, "Initialized", true); Set(result, "Id", 42); break;
                    case "Upload":
                        ++Uploads;
                        if (Fail) throw new IOException("offline upload failure");
                        Uploaded = File.ReadAllBytes((string)call.Args[0]); break;
                    case "Stop": break;
                    default: throw new InvalidOperationException("Unexpected offline call: " + call.MethodName);
                }
                return new ReturnMessage(result, null, 0, call.LogicalCallContext, call);
            } catch (Exception error) { return new ReturnMessage(error, call); }
        }
    }
    sealed class CapturedUi : SynchronizationContext
    {
        internal int Sends;
        internal Exception Error;
        public override void Send(SendOrPostCallback callback, object state)
        {
            ++Sends;
            Error = FindException(callback, new HashSet<object>());
            // Inspect the actual UI continuation without opening a modal window.
        }
        static Exception FindException(object value, HashSet<object> seen)
        {
            if (value == null || !seen.Add(value)) return null;
            if (value is Exception) return (Exception)value;
            var callback = value as Delegate;
            if (callback != null) return FindException(callback.Target, seen);
            if (!Generated(value.GetType())) return null;
            foreach (var field in value.GetType().GetFields(All)) {
                var error = FindException(field.GetValue(value), seen);
                if (error != null) return error;
            }
            return null;
        }
    }
    static object Closure(Type type, TimerCore core, Process savedProcess, long savedRun, string file)
    {
        var result = FormatterServices.GetUninitializedObject(type);
        foreach (var field in type.GetFields(All | BindingFlags.DeclaredOnly)) {
            object value = null;
            if (typeof(TimerCore).IsAssignableFrom(field.FieldType)) value = core;
            else if (field.Name == "uploadProcess") value = savedProcess;
            else if (field.Name == "uploadRun") value = savedRun;
            else if (field.Name == "fn" || field.Name == "tn") value = file;
            else if (Generated(field.FieldType) && field.FieldType.DeclaringType == typeof(仙剑98柔情DX9))
                value = Closure(field.FieldType, core, savedProcess, savedRun, file);
            if (value != null) field.SetValue(result, value);
        }
        return result;
    }
    static void Scenario(bool automatic, string scenario)
    {
        var ui = (GForm)FormatterServices.GetUninitializedObject(typeof(GForm));
        var captured = new CapturedUi(); ui.SyncContext = captured;
        TimerCore core = automatic ? (TimerCore)new Pal98Dx9Automatic(null) : new 仙剑98柔情DX9(null);
        Set(core, "form", ui); Set(ui, "core", core);
        Set(core, "importedUnverifiedTiming", true); Set(core, "competitionImportedRun", true);
        Set(core, "scoreRunSequence", 7L);
        Call(Get(core, "hardcoreRun"), "ImportUnverified");
        var watch = (PTimer)Get(core, "MT"); watch.SetTS(TimeSpan.FromMilliseconds(123456));
        string scoreError = core.GetScoreValidationError();
        Check(scoreError.Contains("接力"), "fixture must reproduce the imported relay score denial");
        bool scoreDenied = false;
        try { core.EnsureScoreValid(); } catch (InvalidOperationException e) { scoreDenied = e.Message == scoreError; }
        Check(scoreDenied, "ranked score validation must remain active");
        var fake = new OfflineCloud { Fail = scenario == "network-failure" };
        var runnerType = Product.GetType("Pal98Timer.CloudSessionRunner", true);
        var constructor = runnerType.GetConstructors(All).Single();
        var runner = constructor.Invoke(new object[] { Adapt(constructor.GetParameters()[0].ParameterType, a => fake.GetTransparentProxy()), 5, null });
        using (var originalProcess = Process.GetCurrentProcess())
        using (var replacementProcess = Process.GetCurrentProcess()) {
            // Process objects are identity tokens only. PID stays zero; no game or IPC is probed.
            Set(core, "PalProcess", originalProcess);
            string file = (automatic ? "auto-" : "dx9-") + scenario + ".bin";
            byte[] bytes = { 1, 3, 5, 7, 9 }; File.WriteAllBytes(file, bytes);
            try {
                var start = runnerType.GetMethod("Start", All); var p = start.GetParameters();
                object session = start.Invoke(runner, new object[] { core.CoreName, new Func<bool>(() => true),
                    Adapt(p[2].ParameterType, a => {
                        var payload = Activator.CreateInstance(Product.GetType("Pal98Timer.CloudPayload", true), true);
                        Set(payload, "SuppressScores", true); return payload;
                    }), Adapt(p[3].ParameterType, a => null) });
                Set(ui, "cloudRunner", runner); Set(ui, "cloud", session);
                Check(SpinWait.SpinUntil(() => (int)session.GetType().GetProperty("CloudID", All).GetValue(session) == 42, 3000), "offline session must initialize");
                var worker = UploadWorker(typeof(仙剑98柔情DX9));
                var closure = Closure(worker.DeclaringType, core, originalProcess, 7L, file);
                if (scenario == "changed-run") Set(core, "scoreRunSequence", 8L);
                if (scenario == "changed-process") Set(core, "PalProcess", replacementProcess);
                if (scenario == "process-exited") Set(core, "PalProcess", null);
                worker.Invoke(closure, null);
                Check(captured.Sends == 1, "the real worker must dispatch exactly one UI completion");
                bool stale = scenario == "changed-run" || scenario == "changed-process" || scenario == "process-exited";
                if (stale) {
                    Check(fake.Uploads == 0, "stale process/run must never reach upload");
                    Check(captured.Error is InvalidOperationException && captured.Error.Message.Contains("导出后游戏或跑次已改变"), "stale export must retain its exact rejection");
                } else if (fake.Fail) {
                    Check(fake.Uploads == 1 && captured.Error is IOException && captured.Error.Message == "offline upload failure", "real worker must surface transfer failure");
                } else {
                    Check(fake.Uploads == 1 && captured.Error == null, "imported relay must upload through the real menu worker; uploads=" + fake.Uploads + "; error=" + captured.Error?.Message);
                    Check(fake.Uploaded.SequenceEqual(bytes), "upload must receive the exported bytes");
                }
                Check(File.Exists(file) == (stale || fake.Fail), "only successful upload may delete its temporary export");
                if (File.Exists(file)) Check(File.ReadAllBytes(file).SequenceEqual(bytes), "failed upload must preserve exact export bytes");
                Check(core.GetScoreValidationError() == scoreError, "cloud save cannot certify imported score");
                Check(!(bool)Call(core, "CanBeginCompetitionHere", 0), "cloud save cannot grant competition eligibility");
                var hardcore = Call(core, "CaptureHardcoreEvidence");
                Check(!(bool)hardcore.GetType().GetProperty("run_verified").GetValue(hardcore), "cloud save cannot grant hardcore eligibility");
                Check(watch.CurrentTS == TimeSpan.FromMilliseconds(123456), "cloud save must preserve imported elapsed time");
                Console.WriteLine("PASS " + (automatic ? "AUTO " : "DX9 ") + scenario);
            } finally {
                ((IDisposable)runner).Dispose();
                Check((bool)Call(runner, "WaitForExit", 3000), "offline worker must exit");
                if (File.Exists(file)) File.Delete(file);
            }
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try {
            Directory.SetCurrentDirectory(args[0]);
            Check(Calls(typeof(Pal98Dx9Automatic).GetMethod("InitUI")).Any(m => m.DeclaringType == typeof(仙剑98柔情DX9) && m.Name == "InitUI"), "automatic core must use the tested DX9 menu");
            foreach (bool automatic in new[] { false, true })
                foreach (string scenario in new[] { "imported", "changed-run", "changed-process", "process-exited", "network-failure" }) Scenario(automatic, scenario);
            foreach (var type in new[] { typeof(仙剑98柔情), typeof(仙剑98柔情不欢乐模式) })
                Check(!Calls(UploadWorker(type)).Any(m => m.Name == "EnsureScoreValid"), type.Name + " personal cloud entry must remain independent of score validity");
            Console.WriteLine("PASS: " + checks + " checks; real compiled menu callbacks; offline transport; no PAL or network.");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
