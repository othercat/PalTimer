using HFrame.ENT;
using System.Collections.Generic;

namespace Pal98Timer
{
    internal sealed class HardcoreDisplaySnapshot
    {
        internal static readonly HardcoreDisplaySnapshot Empty = new HardcoreDisplaySnapshot("", "");
        internal readonly string Status, Device;
        internal bool Visible { get { return Status.Length != 0; } }
        internal HardcoreDisplaySnapshot(string status, string device) { Status = status; Device = device; }
    }

    // No timer, route or pause controls: this only observes the existing run lifecycle.
    internal sealed class HardcoreRunEvidence
    {
        private readonly object sync = new object();
        private HardcoreSnapshot current, identity, previous;
        private bool started, completed, invalid, requestedSeen;
        private string error = "";
        private uint disconnectCount, reconnectCount;
        private uint segmentDisconnects, segmentReconnects;
        private string runtimeIdentity;
        private HardcoreControlSnapshot restartRequest;
        private readonly HashSet<string> consumedTickets = new HashSet<string>();
        private uint restarts;
        private long restartBusyDeadline;
        internal bool RestartPending { get { lock(sync) return restartRequest!=null && !invalid && !completed; } }
        internal uint Restarts { get { lock(sync) return restarts; } }

        internal void Reset()
        {
            lock (sync)
            {
                current = identity = previous = null;
                started = completed = invalid = requestedSeen = false;
                error = ""; disconnectCount = reconnectCount = 0;
                segmentDisconnects=segmentReconnects=restarts=0;
                runtimeIdentity=null; restartRequest=null; consumedTickets.Clear();
                restartBusyDeadline=0;
            }
        }

        internal void ImportUnverified()
        {
            lock (sync)
            {
                Reset();
                started = invalid = true; error = "导入成绩的硬核身份未知";
            }
        }

        internal void Observe(HardcoreSnapshot snapshot, bool runStarted, HardcoreControlSnapshot control=null,
            HardcoreRestartSnapshot restart=null, string observedRuntimeIdentity=null,
            uint observedPid=0, long observedCreation=0, bool helperAlive=false, bool targetAlive=false,
            bool controlBusy=false, bool restartBusy=false)
        {
            lock (sync)
            {
                current = snapshot;
                requestedSeen |= snapshot != null && snapshot.Requested;
                if (completed) return;
                if (!started && runStarted)
                {
                    started = true;
                    // First confirmation after timing began cannot certify the earlier portion.
                    if (snapshot != null && snapshot.RulesSupported && snapshot.State == HardcoreState.Active &&
                        previous != null && previous.SameRun(snapshot) && previous.State == HardcoreState.Active)
                        identity = snapshot;
                    else { invalid = true; error = snapshot != null && snapshot.Requested && !snapshot.RulesSupported ?
                        "硬核规则版本不受支持，请更新计时器" : "跑次开始时缺少连续硬核生效证据"; }
                }
                if (started && identity != null)
                {
                    if (identity.SameRun(snapshot) && !string.IsNullOrEmpty(observedRuntimeIdentity)) {
                        if (runtimeIdentity==null) runtimeIdentity=observedRuntimeIdentity;
                        else if(runtimeIdentity!=observedRuntimeIdentity) Invalidate("本局 DLL 或内容身份发生变化");
                    }
                    if (ObserveRestart(snapshot,control,restart,observedRuntimeIdentity,observedPid,observedCreation,helperAlive,targetAlive,controlBusy,restartBusy)) {
                        if(invalid) restartRequest=null;
                        return;
                    }
                    if (snapshot == null) Invalidate("本局运行时证据中断");
                    else if (!identity.SameRun(snapshot)) Invalidate("本局曾切换进程、规则或键盘绑定");
                    else if (snapshot.State != HardcoreState.Active && snapshot.State != HardcoreState.Disconnected &&
                        snapshot.State != HardcoreState.Unfocused) Invalidate("本局硬核请求曾等待、关闭或被拒绝");
                    else if (snapshot.DisconnectCount < segmentDisconnects || snapshot.ReconnectCount < segmentReconnects ||
                        previous != null && snapshot.LastChangeQpc < previous.LastChangeQpc)
                        Invalidate("本局硬核运行时计数回退");
                    else UpdateCounts(snapshot);
                }
                previous = snapshot;
            }
        }

        private void UpdateCounts(HardcoreSnapshot snapshot)
        {
            disconnectCount+=snapshot.DisconnectCount-segmentDisconnects;
            reconnectCount+=snapshot.ReconnectCount-segmentReconnects;
            segmentDisconnects=snapshot.DisconnectCount; segmentReconnects=snapshot.ReconnectCount;
        }
        // Only a committed request from this run plus the helper's actual launch
        // result may bridge a gap. PID/path/time proximity never authorizes it.
        private bool ObserveRestart(HardcoreSnapshot snapshot,HardcoreControlSnapshot control,HardcoreRestartSnapshot result,
            string observedRuntimeIdentity,uint observedPid,long observedCreation,bool helperAlive,bool targetAlive,bool controlBusy,bool restartBusy)
        {
            if(restartRequest==null && control!=null && control.Owns(identity)) {
                if(control.State==3) Invalidate("本局重启衔接失败");
                if(control.State==2) {
                    if(runtimeIdentity==null || consumedTickets.Contains(control.Ticket)) { Invalidate("重启衔接缺少原始身份或重复使用"); return false; }
                    restartRequest=control;
                }
            }
            if(restartRequest==null) return false;
            if(control==null ? !controlBusy : !control.Owns(identity) || control.State!=2 || control.Ticket!=restartRequest.Ticket ||
                control.HelperPid!=restartRequest.HelperPid || control.HelperCreation!=restartRequest.HelperCreation) {
                Invalidate("重启请求或助手证据不完整"); return true;
            }
            if(result==null ? !restartBusy : !result.Matches(restartRequest) || result.State==3) {
                Invalidate("重启请求或助手证据不完整"); return true;
            }
            if(identity.SameRun(snapshot)) {
                if(snapshot.State!=HardcoreState.Active && snapshot.State!=HardcoreState.Unfocused && snapshot.State!=HardcoreState.Disconnected)
                    Invalidate("重启前硬核状态失效");
                else if(snapshot.DisconnectCount<segmentDisconnects || snapshot.ReconnectCount<segmentReconnects ||
                    previous!=null && snapshot.LastChangeQpc<previous.LastChangeQpc) Invalidate("重启前硬核计数回退");
                else { UpdateCounts(snapshot); previous=snapshot; }
            }
            if(control==null || result==null) {
                // A previously committed ticket may wait briefly for a seqlock
                // writer. It stays unverified; missing/corrupt frames never use
                // this path, and a stalled writer cannot wait indefinitely.
                long now=System.Diagnostics.Stopwatch.GetTimestamp();
                if(restartBusyDeadline==0) restartBusyDeadline=now+System.Diagnostics.Stopwatch.Frequency;
                else if(now>=restartBusyDeadline) Invalidate("重启证据持续写入未完成");
                return true;
            }
            restartBusyDeadline=0;
            if(result.State!=2) {
                if(!helperAlive) Invalidate("重启助手已退出且没有有效启动结果");
                if(observedPid!=0 && (observedPid!=identity.Pid || observedCreation!=identity.ProcessCreation)) Invalidate("新进程没有助手启动凭据");
                return true;
            }
            if(!targetAlive) { Invalidate("助手启动的游戏已退出"); return true; }
            if(observedPid!=0 && !(observedPid==identity.Pid && observedCreation==identity.ProcessCreation) &&
                (observedPid!=result.NewPid || observedCreation!=result.NewCreation)) {
                Invalidate("连接进程与助手启动结果不符"); return true;
            }
            if(snapshot==null || identity.SameRun(snapshot)) return true;
            if(snapshot.Pid!=result.NewPid || snapshot.ProcessCreation!=result.NewCreation || !snapshot.Requested ||
                !snapshot.RulesSupported || !identity.SamePolicy(snapshot)) {
                Invalidate("重启后规则、配置或键盘绑定发生变化"); return true;
            }
            if(snapshot.State==HardcoreState.Rejected || snapshot.State==HardcoreState.Off) {
                Invalidate("重启后硬核状态失效"); return true;
            }
            if(!string.IsNullOrEmpty(observedRuntimeIdentity) && observedRuntimeIdentity!=runtimeIdentity) {
                Invalidate("重启后 DLL 或内容身份发生变化"); return true;
            }
            // Local confirmation and asynchronous identity verification must both
            // finish before a resumed run can again be exported as verified.
            if(snapshot.State!=HardcoreState.Active || string.IsNullOrEmpty(observedRuntimeIdentity)) return true;
            consumedTickets.Add(restartRequest.Ticket); restartRequest=null; ++restarts;
            identity=previous=snapshot; segmentDisconnects=segmentReconnects=0; UpdateCounts(snapshot);
            return true; // sticky invalid is deliberately never reset here
        }

        private void Invalidate(string reason) { if (!invalid) error = reason; invalid = true; }
        internal void Complete() { lock (sync) { if(restartRequest!=null) Invalidate("重启衔接尚未完成"); completed = started; } }

        internal HardcoreDisplaySnapshot CaptureDisplay()
        {
            lock (sync)
            {
                // Old/ordinary sessions keep their existing window layout.
                if (!requestedSeen) return HardcoreDisplaySnapshot.Empty;
                string text = RestartPending ? "硬核等待重启衔接" : current == null ? "硬核未知" : current.StateLabel;
                if (current != null && current.Requested)
                {
                    text += " 断开" + current.DisconnectCount + "/恢复" + current.ReconnectCount;
                    if (current.State == HardcoreState.Rejected) text += " " + HardcoreSnapshot.SingleLine(current.ReasonText);
                }
                if (started && invalid) text += " · 本局硬核未验证";
                return new HardcoreDisplaySnapshot(text, current == null ? "" : current.DeviceLabel);
            }
        }

        internal CompetitionHardcore CaptureCompetition()
        {
            lock (sync) {
                bool requested = started ? requestedSeen : current != null && current.Requested;
                bool verified = started && !invalid && identity != null && restartRequest==null;
                return new CompetitionHardcore { requested = requested, run_verified = requested && verified,
                    rules_version = requested ? (int)((identity ?? current)?.RulesVersion ?? 0) : 0,
                    evidence_status = !requested ? "ordinary" : verified ? "runtime_observed" : "unverified" };
            }
        }

        internal void Fill(HObj data)
        {
            lock (sync)
            {
                data["HardcoreContractVersion"] = 1;
                data["HardcoreRuntimeState"] = current == null ? "Unknown" : current.State.ToString();
                data["HardcoreRequested"] = current != null && current.Requested;
                data["HardcoreRunVerified"] = started && !invalid && identity != null && restartRequest==null;
                data["HardcoreRunEvidenceStatus"] = !started ? "not_started" : invalid ? "unverified" : restartRequest!=null ? "restart_pending" : "runtime_observed";
                data["HardcoreRestartCount"] = restarts;
                data["HardcoreValidationError"] = error;
                data["HardcoreRunCompleted"] = completed;
                data["HardcoreHardwareReview"] = "unknown";
                data["HardcoreHardwareVerified"] = false;
                // Only the run's own identity is exported; no imported/best-file fields participate.
                data["HardcoreProducerVersion"] = identity == null ? 0u : identity.ProducerVersion;
                data["HardcoreRulesVersion"] = identity == null ? 0u : identity.RulesVersion;
                data["HardcoreBlacklistVersion"] = identity == null ? 0u : identity.BlacklistVersion;
                data["HardcoreProcessId"] = identity == null ? 0u : identity.Pid;
                data["HardcoreProcessCreation"] = identity == null ? 0L : identity.ProcessCreation;
                data["HardcoreConfigurationSha256"] = identity == null ? "" : identity.ConfigurationHash;
                data["HardcoreBindingSha256"] = identity == null ? "" : identity.BindingHash;
                data["HardcoreDeviceName"] = identity == null ? "" : identity.DeviceName;
                data["HardcoreKeyboardTransport"] = identity == null ? "unknown" :
                    identity.KeyboardTransport == HardcoreKeyboardTransport.Ps2 ? "ps2" : "usb";
                data["HardcoreVendorId"] = identity == null ? 0 : identity.VendorId;
                data["HardcoreProductId"] = identity == null ? 0 : identity.ProductId;
                data["HardcoreDisconnectCount"] = disconnectCount;
                data["HardcoreReconnectCount"] = reconnectCount;
            }
        }
    }
}
