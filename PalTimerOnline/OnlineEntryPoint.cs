using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pal98Timer
{
    public static class OnlineEntryPoint
    {
        public static int InitializeGeshiConfiguration(string snapshotFile, string outputDirectory) =>
            GeshiConfigurationInitialization.Execute(snapshotFile, outputDirectory);
        public static int ExecuteChallengeRequest(string gameRoot, string requestFile, string responseFile) =>
            ChallengeBridge.Execute(gameRoot, requestFile, responseFile);
        public static int InitializeSdlpalConfiguration(string snapshotFile, string outputDirectory, string[] checkpointIds) =>
            GeshiConfigurationInitialization.Execute(snapshotFile, outputDirectory, checkpointIds);
        public static IPalTimerOnlineV1 Create(int apiVersion, string timerVersion)
        {
            if (apiVersion != 1) throw new NotSupportedException("Unsupported online host API.");
            return new OnlineModule(timerVersion);
        }
    }
    internal sealed class OnlineModule : IPalTimerOnlineV1, IPalTimerOnlineStatusV1
    {
        private readonly CompetitionClient client;
        private readonly System.Threading.CancellationTokenSource challengeStop = new System.Threading.CancellationTokenSource();
        private Task challengeRefresh;
        private CompetitionSettingsForm settingsForm;
        private CompetitionOverlayForm overlay;
        private CompetitionClockWarningForm clockWarningForm;
        private long shownClockWarning;
        private int pid;
        private long generation;
        private bool closing;
        private int disposed;
        public int ApiVersion => 1;
        public bool Enabled => client.Enabled;
        public string LiveCaption => client.LiveCaption;
        public string StatusCaption => client.StatusCaption;
        public string StatusDetail => client.StatusDetail;
        internal OnlineModule(string timerVersion) : this(new CompetitionClient(gameManaged: true, timerVersion: timerVersion)) {
            challengeRefresh = ChallengeBridge.RefreshInstallation(AppDomain.CurrentDomain.BaseDirectory,challengeStop.Token);
        }
        internal OnlineModule(CompetitionClient client) { this.client = client; }
        public void ObserveTarget(int processId, long epoch)
        {
            if (pid == processId && generation == epoch) return;
            pid = processId; generation = epoch;
            // Resolving/reading the selected process configuration is owned by
            // the single background client, never by the timing thread.
            client.ObserveTarget(processId, epoch);
        }
        public void Publish(OnlineSnapshotV1 value)
        {
            if (value == null) return;
            client.Publish(ReadSnapshot(value));
        }
        internal static CompetitionObservation ReadSnapshot(OnlineSnapshotV1 value)
        {
            string restart;
            long milliseconds;
            long? restartMilliseconds = null;
            if (value.Extensions.TryGetValue("restart_ms", out restart) && restart != null)
            {
                if (!long.TryParse(restart, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out milliseconds))
                    throw new ArgumentException("Invalid restart_ms extension.");
                restartMilliseconds = milliseconds;
            }
            return new CompetitionObservation {
                Token = value.Token, Core = value.Core, DllHash = value.DllHash, GameVersion = value.GameVersion,
                ValidationError = value.ValidationError, TimelineId = value.TimelineId, GameTitle = value.GameTitle,
                Step = value.Step, FadeMilliseconds = value.Fade, MapSpeedTicks = value.Speed, TotalMilliseconds = value.Total,
                RestartMilliseconds = restartMilliseconds,
                LiveTimingValid = ReadLiveTimingValid(value),
                ReleaseRevision = ReadReleaseRevision(value),
                ObservedAt = value.ObservedAt, Finished = value.Finished, BeganHere = value.BeganHere,
                Gameplay = value.Gameplay == null ? null : new GameplayIdentity { schema = value.Gameplay.Schema,
                    rules_sha256 = value.Gameplay.RulesHash, content_id = value.Gameplay.ContentId, content_sha256 = value.Gameplay.ContentHash,
                    family = value.Gameplay.Family, fade_ms = value.Gameplay.Fade, map_speed_ticks = value.Gameplay.Speed,
                    walk_frame_ms = ReadWalkFrame(value),
                    speed = ReadSdlSpeed(value, "sdlpal.speed"),
                    battle_speed = ReadSdlSpeed(value, "sdlpal.battle_speed") },
                Ranking = value.Ranking == null ? null : new RankingConfiguration { schema = value.Ranking.Schema,
                    configuration_id = value.Ranking.Id, covered = value.Ranking.Covered,
                    rules = value.Ranking.Rules?.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) },
                Hardcore = value.Hardcore == null ? null : new CompetitionHardcore { requested = value.Hardcore.Requested,
                    run_verified = value.Hardcore.Verified, rules_version = value.Hardcore.RulesVersion, evidence_status = value.Hardcore.Status },
                Splits = value.Splits.Select(s => new CompetitionSplit { checkpoint_id = s.Id, elapsed_ms = s.Elapsed, status = s.Status }).ToArray()
            };
        }
        public void PublishClock(string token, long elapsed, bool running, long observedTick) => client.PublishClock(token, elapsed, running, observedTick);
        private static bool? ReadLiveTimingValid(OnlineSnapshotV1 value)
        {
            string text;
            if (!GameplayIdentity.IsSdlFamily(value.Gameplay?.Family) || !value.Extensions.TryGetValue("sdlpal.live_timing_valid", out text)) return null;
            if (text == "true") return true;
            if (text == "false") return false;
            throw new ArgumentException("Invalid sdlpal.live_timing_valid extension.");
        }
        private static string ReadReleaseRevision(OnlineSnapshotV1 value)
        {
            string revision;
            if (!GameplayIdentity.IsSdlFamily(value.Gameplay?.Family) || !value.Extensions.TryGetValue("sdlpal.release_revision", out revision)) return null;
            if (!CompetitionProtocol.ReleaseRevision(revision)) throw new ArgumentException("Invalid sdlpal.release_revision extension.");
            return revision;
        }
        private static int? ReadWalkFrame(OnlineSnapshotV1 value)
        {
            string text; int number;
            return value.Extensions != null && value.Extensions.TryGetValue("sdlpal.walk_frame_ms", out text) &&
                int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number) ? number : (int?)null;
        }
        private static int? ReadSdlSpeed(OnlineSnapshotV1 value, string key)
        {
            string text; int number;
            if (!GameplayIdentity.IsSdlFamily(value.Gameplay?.Family) || !value.Extensions.TryGetValue(key, out text)) return null;
            if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number) || number < 0 || number > 100)
                throw new ArgumentException("Invalid " + key + " extension.");
            return number;
        }
        public void Invalidate(string token) => client.Invalidate(token);
        public void ShowSettings(IWin32Window owner)
        {
            if (settingsForm == null || settingsForm.IsDisposed) { settingsForm = new CompetitionSettingsForm(client); settingsForm.Show(owner); }
            else settingsForm.Activate();
        }
        public void UiTick()
        {
            var warning = client.ClockWarning;
            if (warning == null) {
                clockWarningForm?.Close(); clockWarningForm = null;
            } else if (!closing && shownClockWarning != warning.Id) {
                clockWarningForm?.Close();
                shownClockWarning = warning.Id;
                clockWarningForm = new CompetitionClockWarningForm(warning.Message);
                clockWarningForm.Show();
            } else if (clockWarningForm != null && !clockWarningForm.IsDisposed) {
                clockWarningForm.UpdateMessage(warning.Message);
            }
            var cfg = client.Settings;
            if (cfg.Overlay && (overlay == null || overlay.IsDisposed)) {
                overlay = new CompetitionOverlayForm(client);
                overlay.FormClosed += delegate { if (!closing) { var next = client.Settings; next.Overlay = false; client.ConfigureAppearance(next); } };
                overlay.Show();
            }
            if (!cfg.Overlay && overlay != null && !overlay.IsDisposed) overlay.Close();
        }
        public async Task<bool> CloseAsync() {
            if (System.Threading.Volatile.Read(ref disposed) != 0) return true;
            closing = true; challengeStop.Cancel();
            if (challengeRefresh != null) await Task.WhenAny(challengeRefresh,Task.Delay(2000));
            bool closed = await client.CloseAsync(); if (!closed) closing = false; return closed;
        }
        public void Dispose() {
            if (System.Threading.Interlocked.Exchange(ref disposed,1) != 0) return;
            closing = true; challengeStop.Cancel(); clockWarningForm?.Close(); overlay?.Close(); settingsForm?.Close(); client.Dispose();
            // The refresh can still be unwinding its linked cancellation source.
            if (challengeRefresh == null || challengeRefresh.IsCompleted) challengeStop.Dispose();
            else challengeRefresh.ContinueWith(_ => challengeStop.Dispose(), TaskScheduler.Default);
        }
    }
}
