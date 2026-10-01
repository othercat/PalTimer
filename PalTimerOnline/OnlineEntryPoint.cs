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
        public static IPalTimerOnlineV1 Create(int apiVersion, string timerVersion)
        {
            if (apiVersion != 1) throw new NotSupportedException("Unsupported online host API.");
            return new OnlineModule(timerVersion);
        }
    }
    internal sealed class OnlineModule : IPalTimerOnlineV1
    {
        private readonly CompetitionClient client;
        private CompetitionSettingsForm settingsForm;
        private CompetitionOverlayForm overlay;
        private int pid;
        private long generation;
        private bool closing;
        public int ApiVersion => 1;
        public bool Enabled => client.Enabled;
        public string LiveCaption => client.LiveCaption;
        internal OnlineModule(string timerVersion) { client = new CompetitionClient(gameManaged: true, timerVersion: timerVersion); }
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
            client.Publish(new CompetitionObservation {
                Token = value.Token, Core = value.Core, DllHash = value.DllHash, GameVersion = value.GameVersion,
                ValidationError = value.ValidationError, TimelineId = value.TimelineId, GameTitle = value.GameTitle,
                Step = value.Step, FadeMilliseconds = value.Fade, MapSpeedTicks = value.Speed, TotalMilliseconds = value.Total,
                ObservedAt = value.ObservedAt, Finished = value.Finished, BeganHere = value.BeganHere,
                Gameplay = value.Gameplay == null ? null : new GameplayIdentity { schema = value.Gameplay.Schema,
                    rules_sha256 = value.Gameplay.RulesHash, content_id = value.Gameplay.ContentId, content_sha256 = value.Gameplay.ContentHash,
                    family = value.Gameplay.Family, fade_ms = value.Gameplay.Fade, map_speed_ticks = value.Gameplay.Speed },
                Ranking = value.Ranking == null ? null : new RankingConfiguration { schema = value.Ranking.Schema,
                    configuration_id = value.Ranking.Id, covered = value.Ranking.Covered,
                    rules = value.Ranking.Rules?.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) },
                Hardcore = value.Hardcore == null ? null : new CompetitionHardcore { requested = value.Hardcore.Requested,
                    run_verified = value.Hardcore.Verified, rules_version = value.Hardcore.RulesVersion, evidence_status = value.Hardcore.Status },
                Splits = value.Splits.Select(s => new CompetitionSplit { checkpoint_id = s.Id, elapsed_ms = s.Elapsed, status = s.Status }).ToArray()
            });
        }
        public void PublishClock(string token, long elapsed, bool running, long observedTick) => client.PublishClock(token, elapsed, running, observedTick);
        public void Invalidate(string token) => client.Invalidate(token);
        public void ShowSettings(IWin32Window owner)
        {
            if (settingsForm == null || settingsForm.IsDisposed) { settingsForm = new CompetitionSettingsForm(client); settingsForm.Show(owner); }
            else settingsForm.Activate();
        }
        public void UiTick()
        {
            var cfg = client.Settings;
            if (cfg.Overlay && (overlay == null || overlay.IsDisposed)) {
                overlay = new CompetitionOverlayForm(client);
                overlay.FormClosed += delegate { if (!closing) { var next = client.Settings; next.Overlay = false; client.ConfigureAppearance(next); } };
                overlay.Show();
            }
            if (!cfg.Overlay && overlay != null && !overlay.IsDisposed) overlay.Close();
        }
        public async Task<bool> CloseAsync() { closing = true; bool closed = await client.CloseAsync(); if (!closed) closing = false; return closed; }
        public void Dispose() { closing = true; overlay?.Close(); settingsForm?.Close(); client.Dispose(); }
    }
}
