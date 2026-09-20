using HFrame.ENT;

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

        internal void Reset()
        {
            lock (sync)
            {
                current = identity = previous = null;
                started = completed = invalid = requestedSeen = false;
                error = ""; disconnectCount = reconnectCount = 0;
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

        internal void Observe(HardcoreSnapshot snapshot, bool runStarted)
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
                    if (snapshot != null && snapshot.State == HardcoreState.Active &&
                        previous != null && previous.SameRun(snapshot) && previous.State == HardcoreState.Active)
                        identity = snapshot;
                    else { invalid = true; error = "跑次开始时缺少连续硬核生效证据"; }
                }
                if (started && identity != null)
                {
                    if (snapshot == null) Invalidate("本局运行时证据中断");
                    else if (!identity.SameRun(snapshot)) Invalidate("本局曾切换进程、规则或键盘绑定");
                    else if (snapshot.State != HardcoreState.Active && snapshot.State != HardcoreState.Disconnected &&
                        snapshot.State != HardcoreState.Unfocused) Invalidate("本局硬核请求曾等待、关闭或被拒绝");
                    else if (snapshot.DisconnectCount < disconnectCount || snapshot.ReconnectCount < reconnectCount ||
                        previous != null && snapshot.LastChangeQpc < previous.LastChangeQpc)
                        Invalidate("本局硬核运行时计数回退");
                    else { disconnectCount = snapshot.DisconnectCount; reconnectCount = snapshot.ReconnectCount; }
                }
                previous = snapshot;
            }
        }

        private void Invalidate(string reason) { if (!invalid) error = reason; invalid = true; }
        internal void Complete() { lock (sync) completed = started; }

        internal HardcoreDisplaySnapshot CaptureDisplay()
        {
            lock (sync)
            {
                // Old/ordinary sessions keep their existing window layout.
                if (!requestedSeen) return HardcoreDisplaySnapshot.Empty;
                string text = current == null ? "硬核未知" : current.StateLabel;
                if (current != null && current.Requested)
                {
                    text += " 断开" + current.DisconnectCount + "/恢复" + current.ReconnectCount;
                    if (current.State == HardcoreState.Rejected) text += " " + HardcoreSnapshot.SingleLine(current.ReasonText);
                }
                if (started && invalid) text += " · 本局硬核未验证";
                return new HardcoreDisplaySnapshot(text, current == null ? "" : current.DeviceLabel);
            }
        }

        internal void Fill(HObj data)
        {
            lock (sync)
            {
                data["HardcoreContractVersion"] = 1;
                data["HardcoreRuntimeState"] = current == null ? "Unknown" : current.State.ToString();
                data["HardcoreRequested"] = current != null && current.Requested;
                data["HardcoreRunVerified"] = started && !invalid && identity != null;
                data["HardcoreRunEvidenceStatus"] = !started ? "not_started" : invalid ? "unverified" : "runtime_observed";
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
                data["HardcoreVendorId"] = identity == null ? 0 : identity.VendorId;
                data["HardcoreProductId"] = identity == null ? 0 : identity.ProductId;
                data["HardcoreDisconnectCount"] = disconnectCount;
                data["HardcoreReconnectCount"] = reconnectCount;
            }
        }
    }
}
