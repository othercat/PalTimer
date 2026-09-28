using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pal98Timer
{
    public partial class GForm
    {
        private CompetitionClient competition;
        private CompetitionSettingsForm competitionSettingsForm;
        private CompetitionOverlayForm competitionOverlay;
        private System.Windows.Forms.Timer competitionUiTimer;
        private bool competitionCloseReady, competitionClosing;
        private void InitializeCompetition()
        {
            competition = new CompetitionClient(gameManaged: true);
            var item = new ToolStripMenuItem("联机与排名…");
            item.Click += delegate {
                if (competitionSettingsForm == null || competitionSettingsForm.IsDisposed)
                {
                    competitionSettingsForm = new CompetitionSettingsForm(competition);
                    competitionSettingsForm.Show(this);
                }
                else competitionSettingsForm.Activate();
            };
            // App-level item: core unload must not delete it.
            mnData.Items.Add(item);
            competitionUiTimer = new System.Windows.Forms.Timer { Interval = 500 };
            competitionUiTimer.Tick += delegate {
                competition.ObserveGame(core?.CompetitionGameProcess);
                var cfg = competition.Settings;
                if (cfg.Overlay && (competitionOverlay == null || competitionOverlay.IsDisposed))
                {
                    competitionOverlay = new CompetitionOverlayForm(competition);
                    competitionOverlay.FormClosed += delegate {
                        if (!competitionClosing) { var next = competition.Settings; next.Overlay = false; competition.ConfigureAppearance(next); }
                    };
                    competitionOverlay.Show();
                }
                if (!cfg.Overlay && competitionOverlay != null && !competitionOverlay.IsDisposed) competitionOverlay.Close();
            };
            competitionUiTimer.Start();
        }
        internal void PublishCompetition(TimerCore source, CompetitionObservation observation)
        { if (ReferenceEquals(core, source)) competition?.Publish(observation); }
        internal bool CompetitionEnabled(TimerCore source) { return ReferenceEquals(core, source) && competition != null && competition.Enabled; }
        internal void InvalidateCompetition(TimerCore source, string token)
        { if (ReferenceEquals(core, source)) competition?.Invalidate(token); }
        private async void CloseCompetitionThenExit()
        {
            if (competitionClosing) return;
            competitionClosing = true; competitionUiTimer?.Stop();
            // No network wait on the UI thread and no extra modal prompt.
            if (competition != null && !await competition.CloseAsync())
            {
                competitionClosing = false;
                if (competitionSettingsForm == null || competitionSettingsForm.IsDisposed)
                { competitionSettingsForm = new CompetitionSettingsForm(competition); competitionSettingsForm.Show(this); }
                return;
            }
            competitionCloseReady = true; Close();
        }
    }

}
