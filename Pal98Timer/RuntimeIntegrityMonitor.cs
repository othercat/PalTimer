using HFrame.ENT;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Pal98Timer
{
    internal sealed class RuntimeIntegrityEvidence
    {
        internal PalLiveProcessIdentity Identity;
        internal int RequestedProcessId;
        internal RuntimeIntegritySnapshot Runtime;
        internal RuntimeIntegritySnapshot LastAlert;
        internal string ReleaseId = "", Detail = "", GraphicsChain = "", PalDllVersion = "";
        internal bool Frozen, HeartbeatValid, FileMismatchSeen, CodeMismatchSeen, FileRecheckInProgress, PalDllMismatchSeen;
        internal bool ReadFailed, DiagnosticUnavailable;
        internal long FilesVerifiedAt;
        internal uint StickyAlerts;
        internal IntegrityCheckState Files, Code, PalDll;
        internal string Summary(bool cloudCertified)
        {
            var parts = new List<string>();
            if ((StickyAlerts & 1) != 0) parts.Add("[数值改写已拦截]");
            if (CodeMismatchSeen || (StickyAlerts & 4) != 0) parts.Add("[运行完整性异常]");
            if ((StickyAlerts & 2) != 0) parts.Add("[随机数待核验]");
            if (PalDllMismatchSeen || PalDll == IntegrityCheckState.Mismatch) parts.Add("[测试版]");
            if ((StickyAlerts & 8) != 0) parts.Add("[采样不完整]");
            if ((StickyAlerts & (16 | 32)) != 0) parts.Add("[随机运行异常]");
            // Progress, success, activation and expected coverage states belong
            // in metadata. The player-facing summary contains problems only.
            if (ReadFailed) parts.Add("[核验读取失败]");
            if (Runtime != null && Runtime.Protection == 4 || (StickyAlerts & 64) != 0) parts.Add("[保护不可用]");
            return string.Join("", parts);
        }
        internal void Fill(HObj target, bool cloudCertified)
        {
            var data = new HObj();
            data["Schema"] = "PAL98.RuntimeIntegrity.v1";
            data["ReleaseSchema"] = "PAL98.ReleaseIntegrity.v1";
            data["ReleaseId"] = ReleaseId; data["ReleaseFrozen"] = Frozen;
            data["ProcessId"] = Identity == null ? RequestedProcessId : Identity.Pid;
            data["ProcessCreationTime"] = Identity == null ? "" : Identity.CreationTime.ToString(System.Globalization.CultureInfo.InvariantCulture);
            data["FileState"] = Files.ToString(); data["CodeState"] = Code.ToString();
            data["PalDllState"] = PalDll.ToString(); data["PalDllVersion"] = PalDllVersion;
            data["PalDllMismatchSeen"] = PalDllMismatchSeen;
            data["FileRecheckInProgress"] = FileRecheckInProgress;
            data["ReadFailed"] = ReadFailed; data["DiagnosticUnavailable"] = DiagnosticUnavailable;
            data["FilesVerifiedAtQpc"] = FilesVerifiedAt.ToString(System.Globalization.CultureInfo.InvariantCulture);
            data["FileMismatchSeen"] = FileMismatchSeen; data["CodeMismatchSeen"] = CodeMismatchSeen;
            data["HeartbeatValid"] = HeartbeatValid; data["Alerts"] = StickyAlerts;
            // Legacy HObj interprets a string starting/ending in [] as JSON.
            // A readable prefix keeps bracketed status labels round-trip safe.
            data["Summary"] = "运行核验：" + Summary(cloudCertified); data["Detail"] = "说明：" + Detail; data["GraphicsChain"] = GraphicsChain;
            data["CloudIdActivated"] = cloudCertified;
            data["CloudStatus"] = cloudCertified ? "云ID已激活" : "未云认证";
            data["Protection"] = Runtime == null ? "Unknown" : Runtime.Protection.ToString(System.Globalization.CultureInfo.InvariantCulture);
            data["Build"] = Runtime == null ? "" : Runtime.Build;
            data["EventSequence"] = Runtime == null ? 0 : Runtime.EventSequence;
            data["Generation"] = Runtime == null ? 0 : Runtime.Generation;
            data["RandomCalls"] = Runtime == null ? "0" : Runtime.RandomCalls.ToString(System.Globalization.CultureInfo.InvariantCulture);
            data["ProcessedCalls"] = Runtime == null ? "0" : Runtime.ProcessedCalls.ToString(System.Globalization.CultureInfo.InvariantCulture);
            data["DroppedCalls"] = Runtime == null ? "0" : Runtime.DroppedCalls.ToString(System.Globalization.CultureInfo.InvariantCulture);
            data["LastEventQpc"] = Runtime == null ? "0" : Runtime.LastEvent.ToString(System.Globalization.CultureInfo.InvariantCulture);
            data["EventRole"] = Runtime == null ? 0 : Runtime.Role;
            data["EventField"] = Runtime == null ? 0 : Runtime.Field;
            data["ObservedValue"] = Runtime == null ? 0 : Runtime.Observed;
            data["ExpectedValue"] = Runtime == null ? 0 : Runtime.Expected;
            data["RuntimeDetail"] = Runtime == null ? "" : "运行说明：" + Runtime.Detail;
            if (LastAlert != null)
            {
                data["LastAlertEventSequence"] = LastAlert.EventSequence;
                data["LastAlertGeneration"] = LastAlert.Generation;
                data["LastAlertRole"] = LastAlert.Role; data["LastAlertField"] = LastAlert.Field;
                data["LastAlertObservedValue"] = LastAlert.Observed; data["LastAlertExpectedValue"] = LastAlert.Expected;
                data["LastAlertDetail"] = "运行说明：" + LastAlert.Detail;
            }
            data["DiagnosticOnly"] = true;
            target["RuntimeIntegrity"] = data;
        }
    }

    // The score/timing gate deliberately has no dependency on this class.
    internal sealed class RuntimeIntegrityMonitor : IDisposable
    {
        internal const int ObservationMilliseconds = 500;
        internal const int BackgroundSliceMilliseconds = 1000;
        internal const int FileRecheckSeconds = 300;
        internal const int ModuleRefreshSeconds = 10;
        private static readonly object ledgerSync = new object();
        private static readonly Dictionary<string, RuntimeIntegrityEvidence> ledger = new Dictionary<string, RuntimeIntegrityEvidence>();
        private static int workerGate; // Core changes cannot overlap scanning workers.
        private readonly TimingModeReader timingReader = new TimingModeReader();
        private readonly object targetSync = new object();
        private volatile RuntimeIntegrityEvidence evidence = new RuntimeIntegrityEvidence();
        private volatile bool observed, disposed;
        private Process targetProcess;
        private long targetGeneration;
        private long nextObservation;
        private int working;
        private Session session;
        internal string Append(string title, bool cloudCertified)
        {
            string summary = observed ? evidence.Summary(cloudCertified) : "";
            return summary.Length == 0 ? title : summary + " " + title;
        }
        internal string Summary(bool cloudCertified) { return observed ? evidence.Summary(cloudCertified) : ""; }
        internal void Fill(HObj target, bool cloudCertified) { evidence.Fill(target, cloudCertified); }

        internal long SelectTarget(Process process)
        {
            lock (targetSync)
            {
                if (!ReferenceEquals(targetProcess, process))
                {
                    targetProcess = process; ++targetGeneration;
                    Interlocked.Exchange(ref nextObservation, 0);
                    if (process != null)
                    {
                        observed = true;
                        int pid = 0;
                        try { pid = process.Id; } catch (InvalidOperationException) { }
                        // Invalidate the old display synchronously, before any
                        // throttle or busy worker can defer connecting this target.
                        evidence = new RuntimeIntegrityEvidence { RequestedProcessId = pid, Detail = "正在核验新的目标进程" };
                    }
                    else
                    {
                        // Shutdown can stop the heartbeat before the next process
                        // scan detaches. Availability errors no longer describe a
                        // live target; keep confirmed score evidence separately.
                        var detached = Copy(evidence);
                        detached.ReadFailed = false;
                        detached.DiagnosticUnavailable = false;
                        detached.HeartbeatValid = false;
                        detached.FileRecheckInProgress = false;
                        evidence = detached;
                    }
                }
                return targetGeneration;
            }
        }
        private bool IsSelected(Process process, long generation)
        { lock (targetSync) return !disposed && targetGeneration == generation && ReferenceEquals(targetProcess, process); }
        private void Publish(Process process, long generation, RuntimeIntegrityEvidence state)
        {
            Remember(state);
            lock (targetSync)
                if (!disposed && targetGeneration == generation && ReferenceEquals(targetProcess, process)) evidence = Copy(state);
        }
        private void PublishSession(Process process, long generation, RuntimeIntegrityEvidence state)
        {
            session.Evidence = Copy(state);
            Publish(process, generation, state);
        }
        private void Checkpoint(RuntimeIntegrityEvidence state)
        {
            // Preserve newly observed alerts if a later independent read fails,
            // without exposing an intermediate scan state to the title/OBS.
            session.Evidence = Copy(state);
        }

        internal void Observe(Process process)
        {
            if (disposed) return;
            long generation = SelectTarget(process);
            if (process == null) return;
            long now = Stopwatch.GetTimestamp();
            if (now < Interlocked.Read(ref nextObservation) || Interlocked.CompareExchange(ref working, 1, 0) != 0) return;
            if (Interlocked.CompareExchange(ref workerGate, 1, 0) != 0) { Interlocked.Exchange(ref working, 0); return; }
            Interlocked.Exchange(ref nextObservation, now + Stopwatch.Frequency * ObservationMilliseconds / 1000);
            Task.Run(() =>
            {
                try { if (IsSelected(process, generation)) ObserveWorker(process, generation); }
                catch (Exception ex) when (ReleaseIntegrityVerifier.ReadFailure(ex) || ex is System.Security.Cryptography.CryptographicException)
                {
                    var failed = session != null && session.Generation == generation ? Copy(session.Evidence) : new RuntimeIntegrityEvidence();
                    if (failed.Identity == null) { try { failed.RequestedProcessId = process.Id; } catch (InvalidOperationException) { } }
                    failed.Files = IntegrityCheckState.Incomplete; failed.Code = IntegrityCheckState.Incomplete;
                    failed.HeartbeatValid = false; failed.Detail = "核验读取失败：" + ex.Message;
                    failed.ReadFailed = true;
                    Publish(process, generation, failed);
                }
                finally
                {
                    if (disposed && session != null) { session.Dispose(); session = null; }
                    Interlocked.Exchange(ref workerGate, 0); Interlocked.Exchange(ref working, 0);
                }
            });
        }
        private void ObserveWorker(Process process, long generation)
        {
            var identity = PalLiveProcessIdentity.Read(process);
            if (identity == null) throw new IOException("无法确认目标 PAL 进程身份");
            if (session == null || !session.Process.Identity.SameInstance(identity))
            {
                if (session != null) session.Dispose();
                session = null;
                var initial = new RuntimeIntegrityEvidence { Identity = identity };
                lock (ledgerSync)
                {
                    RuntimeIntegrityEvidence prior;
                    if (ledger.TryGetValue(Key(identity), out prior)) initial = Copy(prior);
                }
                session = new Session(identity.Pid);
                if (!session.Process.Identity.SameInstance(identity)) { session.Dispose(); session = null; throw new IOException("目标进程在连接时发生变化"); }
                initial.Files = IntegrityCheckState.Incomplete; initial.Code = IntegrityCheckState.Incomplete; initial.PalDll = IntegrityCheckState.Incomplete; initial.HeartbeatValid = false;
                initial.FilesVerifiedAt = 0; initial.FileRecheckInProgress = false;
                session.Evidence = initial;
            }
            session.Generation = generation;
            var state = Copy(session.Evidence);
            var manifest = session.Manifest;
            state.Identity = identity; state.ReleaseId = manifest.release_id; state.Frozen = manifest.frozen;
            long now = Stopwatch.GetTimestamp();
            var runtime = RuntimeIntegrityReader.Read(identity, Stopwatch.Frequency, state.Runtime);
            state.HeartbeatValid = runtime != null;
            state.ReadFailed = false;
            state.DiagnosticUnavailable = runtime == null && (session.NativeReady || now - session.StartedAt >= 30 * Stopwatch.Frequency);
            if (runtime != null)
            {
                state.Runtime = runtime; state.StickyAlerts |= runtime.Alerts;
                if (runtime.DroppedCalls != 0) state.StickyAlerts |= 8;
                if (runtime.Alerts != 0 && (state.LastAlert == null || runtime.EventSequence != state.LastAlert.EventSequence)) state.LastAlert = runtime;
                session.NativeReady = true;
            }
            Checkpoint(state);
            RuntimeTimingMode mode = timingReader.Read(process);
            if (session.Verifier == null || !SameMode(session.Mode, mode) || now >= session.NextFileScan)
            {
                if (session.Verifier == null || !SameMode(session.Mode, mode))
                {
                    state.Files = IntegrityCheckState.Incomplete; state.GraphicsChain = "";
                    state.FilesVerifiedAt = 0;
                }
                if (session.Verifier != null) session.Verifier.Dispose();
                session.Mode = mode; session.Verifier = new ReleaseIntegrityVerifier(Path.GetDirectoryName(identity.ExecutablePath), manifest, mode);
                session.NextFileScan = long.MaxValue; session.NextFileSlice = 0;
            }
            if (!session.Verifier.Complete && now >= session.NextFileSlice)
            {
                if (state.FilesVerifiedAt == 0) session.Verifier.Advance();
                else session.Verifier.AdvanceBackground();
                session.NextFileSlice = now + Stopwatch.Frequency *
                    (state.FilesVerifiedAt == 0 ? ObservationMilliseconds : BackgroundSliceMilliseconds) / 1000;
            }
            state.FileRecheckInProgress = !session.Verifier.Complete;
            state.PalDll = session.Verifier.PalDllState; state.PalDllVersion = session.Verifier.PalDllVersion;
            if (state.PalDll == IntegrityCheckState.Mismatch) state.PalDllMismatchSeen = true;
            state.ReadFailed = session.Verifier.HasReadFailure;
            // A routine same-content rescan is work in progress, not loss of the
            // last completed verification. Actual failures invalidate it at once.
            if (state.FilesVerifiedAt == 0 || session.Verifier.Complete || session.Verifier.HasReadFailure ||
                session.Verifier.State != IntegrityCheckState.Incomplete)
            {
                state.Files = session.Verifier.State; state.GraphicsChain = session.Verifier.GraphicsChain ?? "";
                state.Detail = session.Verifier.Detail ?? "";
            }
            if (session.Verifier.Complete && state.Files == IntegrityCheckState.Match && session.NextFileScan == long.MaxValue)
                state.FilesVerifiedAt = Stopwatch.GetTimestamp();
            if (state.Files == IntegrityCheckState.Mismatch) state.FileMismatchSeen = true;
            Checkpoint(state);
            if (session.Verifier.Complete && session.NextFileScan == long.MaxValue) session.NextFileScan = now + FileRecheckSeconds * Stopwatch.Frequency;

            // The first stable diagnostic heartbeat is published after native
            // initialization. Preparing refers to role baseline readiness, not RNG.
            state.Code = IntegrityCheckState.Incomplete;
            if (session.NativeReady && manifest.frozen)
            {
                if (runtime != null && !string.Equals(runtime.Build, manifest.build, StringComparison.Ordinal))
                { state.CodeMismatchSeen = true; state.Detail = "运行构建身份与定版清单不匹配"; }
                if (session.Modules == null || now >= session.NextModules)
                { session.Modules = session.Process.Modules(); session.NextModules = now + ModuleRefreshSeconds * Stopwatch.Frequency; }
                CheckLoadedModules(session, state);
                Checkpoint(state);
                // Losing an untrusted heartbeat must not suppress already-ready
                // RNG/code checks. Conditional dispatch stays unverified until
                // its protection state is known again.
                var active = manifest.memory_regions.Where(r => r.protection_states == null || runtime != null && r.protection_states.Contains(runtime.Protection)).ToArray();
                string activeKey = string.Join("\n", active.Select(r => r.id));
                if (session.ActiveRegions != activeKey) { session.ActiveRegions = activeKey; session.CheckedRegions.Clear(); session.RegionIndex = 0; }
                if (active.Length != 0)
                {
                    var region = active[session.RegionIndex++ % active.Length];
                    if (!session.Process.VerifyRegion(region, session.Modules))
                    { state.CodeMismatchSeen = true; state.Detail = "关键代码不匹配：" + region.id; }
                    Checkpoint(state);
                    session.CheckedRegions.Add(region.id);
                    if (session.CheckedRegions.Count == active.Length && (runtime != null || manifest.memory_regions.All(r => r.protection_states == null)))
                        state.Code = IntegrityCheckState.Match;
                }
            }
            if (state.CodeMismatchSeen) state.Code = IntegrityCheckState.Mismatch;
            if (!session.Process.IsCurrent()) throw new IOException("核验期间目标进程已退出");
            if (!state.HeartbeatValid) state.Detail = "诊断心跳未就绪或已过期；尚不能确认运行状态";
            PublishSession(process, generation, state);
        }
        private static void CheckLoadedModules(Session session, RuntimeIntegrityEvidence state)
        {
            var profile = session.Manifest.FindProfile(session.Mode);
            var chain = session.Manifest.graphics_chains.SingleOrDefault(c => c.id == state.GraphicsChain);
            foreach (var file in session.Manifest.files.Concat(profile == null ? new ReleaseIntegrityFile[0] : profile.files)
                .Concat(chain == null ? new ReleaseIntegrityFile[0] : chain.files).Where(f => f.module != null))
            {
                string expected = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(session.Process.Identity.ExecutablePath), file.path));
                // A registered proxy may coexist with the Windows DLL of the
                // same name. The manifest file already binds the exact path;
                // code/fixup lookups by name remain independently unambiguous.
                if (IntegrityProcessReader.ModuleAtPath(session.Modules, file.module, expected) == null)
                { state.CodeMismatchSeen = true; state.Detail = "实际加载模块来自其它目录：" + file.module; }
            }
        }
        private static bool SameMode(RuntimeTimingMode first, RuntimeTimingMode second)
        { return first == null ? second == null : first.SameRun(second); }
        private static string Key(PalLiveProcessIdentity identity) { return identity.Pid + ":" + identity.CreationTime + ":" + identity.ExecutablePath.ToUpperInvariant(); }
        private static void Remember(RuntimeIntegrityEvidence state)
        {
            if (state.Identity != null && state.Identity.CreationTime > 0) lock (ledgerSync) ledger[Key(state.Identity)] = Copy(state);
        }
        private static RuntimeIntegrityEvidence Copy(RuntimeIntegrityEvidence source)
        {
            return new RuntimeIntegrityEvidence { Identity = source.Identity, RequestedProcessId = source.RequestedProcessId, Runtime = source.Runtime, LastAlert = source.LastAlert, ReleaseId = source.ReleaseId, Detail = source.Detail,
                GraphicsChain = source.GraphicsChain, Frozen = source.Frozen, HeartbeatValid = source.HeartbeatValid, FileMismatchSeen = source.FileMismatchSeen,
                CodeMismatchSeen = source.CodeMismatchSeen, StickyAlerts = source.StickyAlerts, Files = source.Files, Code = source.Code,
                FilesVerifiedAt = source.FilesVerifiedAt, FileRecheckInProgress = source.FileRecheckInProgress,
                ReadFailed = source.ReadFailed, DiagnosticUnavailable = source.DiagnosticUnavailable,
                PalDll = source.PalDll, PalDllVersion = source.PalDllVersion, PalDllMismatchSeen = source.PalDllMismatchSeen };
        }
        public void Dispose()
        {
            disposed = true;
            // Do not wait for disk IO on the UI thread. The sole worker owns cleanup.
            if (Interlocked.CompareExchange(ref working, 1, 0) == 0)
            {
                if (session != null) { session.Dispose(); session = null; }
                Interlocked.Exchange(ref working, 0);
            }
        }
        private sealed class Session : IDisposable
        {
            internal readonly IntegrityProcessReader Process;
            internal readonly ReleaseIntegrityManifest Manifest;
            internal ReleaseIntegrityVerifier Verifier;
            internal RuntimeTimingMode Mode;
            internal Dictionary<string, IntegrityModule> Modules;
            internal long NextFileScan, NextFileSlice, NextModules;
            internal long StartedAt = Stopwatch.GetTimestamp();
            internal string ActiveRegions;
            internal int RegionIndex;
            internal long Generation;
            internal bool NativeReady;
            internal RuntimeIntegrityEvidence Evidence;
            internal readonly HashSet<string> CheckedRegions = new HashSet<string>(StringComparer.Ordinal);
            internal Session(int pid)
            {
                Manifest = ReleaseIntegrityManifest.LoadEmbedded();
                Process = new IntegrityProcessReader(pid);
            }
            public void Dispose() { if (Verifier != null) Verifier.Dispose(); Process.Dispose(); }
        }
    }
}
