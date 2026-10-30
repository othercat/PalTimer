using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pal98Timer
{
    public partial class GForm
    {
        private IPalTimerOnlineV1 competition;
        private OnlineModuleLease onlineLease;
        private System.Windows.Forms.Timer competitionUiTimer;
        private Process onlineTarget;
        private long onlineTargetGeneration;
        private bool competitionCloseReady, competitionClosing;
        private string onlineLoadStatus = "正在准备联机组件，本地计时可正常使用。";
        private volatile string onlineCaption = " [联机组件加载中]";
        internal string CompetitionOverlayCaption => onlineCaption;
        private async void InitializeCompetition()
        {
            var item = new ToolStripMenuItem("联机与排名…");
            item.Click += delegate {
                try { if (competition != null) { competition.ShowSettings(this); return; } }
                catch (Exception error) { DisableOnline(error); }
                MessageBox.Show(this, onlineLoadStatus, "联机与排名", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            mnData.Items.Add(item);
            try {
                var lease = await Task.Run(() => OnlineModuleLoader.Load(AppDomain.CurrentDomain.BaseDirectory));
                if (IsDisposed || competitionClosing) { lease.Dispose(); return; }
                onlineLease = lease; competition = lease.Module;
                competitionUiTimer = new System.Windows.Forms.Timer { Interval = 500 };
                competitionUiTimer.Tick += delegate {
                    try {
                        var process = core?.CompetitionGameProcess;
                        if (!ReferenceEquals(process, onlineTarget)) { onlineTarget = process; ++onlineTargetGeneration; }
                        competition.ObserveTarget(process?.Id ?? 0, onlineTargetGeneration);
                        competition.UiTick();
                        var status = competition as IPalTimerOnlineStatusV1;
                        onlineCaption = status?.StatusCaption ??
                            (!string.IsNullOrEmpty(competition.LiveCaption) ? competition.LiveCaption : competition.Enabled ? " [联机连接中]" : " [联机关闭或等待游戏]");
                        item.ToolTipText = status?.StatusDetail ?? onlineCaption.Trim();
                    } catch (Exception error) { DisableOnline(error); }
                };
                competitionUiTimer.Start();
            } catch (Exception error) { DisableOnline(error); }
        }
        private void DisableOnline(Exception error)
        {
            competition = null; onlineCaption = " [联机组件不可用]";
            onlineLoadStatus = "联机组件不可用，仅本地计时。请完整覆盖配套的计时器、联机组件、认证组件和公开登记文件后重开。\n" + error.GetType().Name;
            // Publication may fail on the timing thread. Never operate a
            // WinForms timer there or block local timing waiting for the UI.
            if (InvokeRequired) {
                try { BeginInvoke(new Action(() => competitionUiTimer?.Stop())); } catch (InvalidOperationException) { }
            } else competitionUiTimer?.Stop();
        }
        internal void PublishCompetition(TimerCore source, CompetitionObservation o)
        {
            var module = competition;
            if (!ReferenceEquals(core, source) || module == null) return;
            try { module.Publish(CreateCompetitionSnapshot(o)); }
            catch (Exception error) { DisableOnline(error); }
        }
        internal static OnlineSnapshotV1 CreateCompetitionSnapshot(CompetitionObservation o)
        {
            var g = o.Gameplay; var r = o.Ranking; var h = o.Hardcore;
            var extensions = new System.Collections.Generic.Dictionary<string, string>();
            if (o.RestartMilliseconds.HasValue) extensions["restart_ms"] = o.RestartMilliseconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (g?.walk_frame_ms.HasValue == true) extensions["sdlpal.walk_frame_ms"] = g.walk_frame_ms.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            // Actual SDL speeds are observations, not ranking rules. Preserve
            // unknown values and API 1 compatibility through optional extensions.
            if (GameplayIdentity.IsSdlFamily(g?.family))
            {
                if (g.speed.HasValue) extensions["sdlpal.speed"] = g.speed.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (g.battle_speed.HasValue) extensions["sdlpal.battle_speed"] = g.battle_speed.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            if (GameplayIdentity.IsSdlFamily(g?.family) && o.LiveTimingValid.HasValue)
                extensions["sdlpal.live_timing_valid"] = o.LiveTimingValid.Value ? "true" : "false";
            if (GameplayIdentity.IsSdlFamily(g?.family) && o.ReleaseRevision != null) extensions["sdlpal.release_revision"] = o.ReleaseRevision;
            return new OnlineSnapshotV1(o.Token, o.Core, o.DllHash, o.GameVersion, o.ValidationError, o.TimelineId, o.GameTitle,
                g == null ? null : new OnlineGameplayV1(g.schema, g.rules_sha256, g.content_id, g.content_sha256, g.family, g.fade_ms, g.map_speed_ticks),
                r == null ? null : new OnlineRankingV1(r.schema, r.configuration_id, r.covered, r.rules),
                h == null ? null : new OnlineHardcoreV1(h.requested, h.run_verified, h.rules_version, h.evidence_status),
                o.Step, o.FadeMilliseconds, o.MapSpeedTicks, o.TotalMilliseconds, o.ObservedAt, o.Finished, o.BeganHere,
                o.Splits.Select(s => new OnlineSplitV1(s.checkpoint_id, s.elapsed_ms, s.status)), extensions);
        }
        internal void PublishCompetitionClock(TimerCore source, string token, long elapsed, bool running, long observedTick)
        { if (ReferenceEquals(core, source)) try { competition?.PublishClock(token, elapsed, running, observedTick); } catch (Exception error) { DisableOnline(error); } }
        internal bool CompetitionEnabled(TimerCore source)
        { try { return ReferenceEquals(core, source) && competition != null && competition.Enabled; } catch (Exception error) { DisableOnline(error); return false; } }
        internal void InvalidateCompetition(TimerCore source, string token)
        { if (ReferenceEquals(core, source)) try { competition?.Invalidate(token); } catch (Exception error) { DisableOnline(error); } }
        private async void CloseCompetitionThenExit()
        {
            if (competitionClosing) return;
            competitionClosing = true; competitionUiTimer?.Stop();
            try {
                if (competition != null && !await competition.CloseAsync()) {
                    competitionClosing = false; competition.ShowSettings(this); return;
                }
            } catch { }
            competitionCloseReady = true; Close();
        }
    }
}
