using HFrame.ENT;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pal98Timer
{
    [TimerCoreDisplayName("仙剑98 DX9 自动玩法（实验）")]
    public class Pal98Dx9Automatic : 仙剑98柔情DX9
    {
        private readonly GameplayModeReader gameplayReader = new GameplayModeReader();
        private readonly object timelineSync = new object();
        private GameplaySnapshot selected;
        private string route, identity, frozen, validation = "", referenceJson;
        private bool requested, frozenHardcore, startedWithoutIdentity;
        private bool archiveCompletedRun;
        private bool completed;
        private GameplayStartObservation startObservation;
        private System.Diagnostics.Process startProcess;
        private bool startHardcore;
        private Task archiveTask;
        private string archiveError = "";
        private long epoch;
        private readonly string unknown = Guid.NewGuid().ToString("N");
        public Pal98Dx9Automatic(GForm form) : base(form) { CoreName = "PAL98DX9_AUTO"; }
        internal override bool CompetitionMetadataPending => base.CompetitionMetadataPending || startObservation?.Pending == true;
        private protected override GameplaySnapshot DisplayGameplaySnapshot => selected;
        protected override int TimingModeMs => selected?.fade_ms ?? RecordedTimingMode?.FadeMilliseconds ?? 1200;
        protected override int TimingMapSpeedTicks => selected?.map_speed_ticks ?? RecordedTimingMode?.MapSpeedTicks ?? 10;
        public override string ActiveBestPath => TimerUserSettings.TimelinePath(selected == null || identity == null ?
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Timelines", "Unclassified", unknown, "best.json") :
            TimelineIdentity.PathFor(AppDomain.CurrentDomain.BaseDirectory, identity, selected.covered && validation.Length == 0, "best.json"));
        protected override string RelayFileName => Path.Combine(Path.GetDirectoryName(ActiveBestPath), "SRPG.bin");
        protected override string GetScoreSavePath(DateTime now) => identity != null &&
            (GameplayRestartPending || GameplayContinuationError.Length != 0 || requested && !CaptureHardcoreEvidence().run_verified) ?
            TimerUserSettings.TimelinePath(TimelineIdentity.PathFor(AppDomain.CurrentDomain.BaseDirectory, identity, false, "best.json")) : ActiveBestPath;
        protected override void InitCheckPoints()
        {
            base.InitCheckPoints();
            route = CompetitionProtocol.RouteHash(CheckPoints.Select(p => CompetitionProtocol.CheckpointId(p.Name)));
            gameplayReader.Route = route;
            SetHardcoreGameplayRoute(route);
            foreach (var point in CheckPoints) point.SetBestReference(new CheckPointNewer { Name = point.Name, BestTS = TimeSpan.Zero, NickName = "" });
        }
        protected override void LoadBest()
        {
            // Automatic pre-start selection has already loaded bytes in the background.
            Best = null;
            string json; lock (timelineSync) json = referenceJson;
            if (string.IsNullOrEmpty(json)) return;
            var data = new HObj(json); ValidateBestReference(data);
            Best = new Dictionary<string, CheckPointNewer>();
            foreach (HObj item in data.GetValue<HObj>("CheckPoints").ToList())
                Best[item.GetValue<string>("name")] = new CheckPointNewer { Name = item.GetValue<string>("name"),
                    NickName = item.HasValue("des") ? item.GetValue<string>("des") : "", BestTS = ConvertTimeSpan(item.GetValue<string>("time")) };
        }
        public override void RefreshBestReference()
        {
            // Explicit save/edit happens outside the timing loop.
            string path = ActiveBestPath; long observed = epoch;
            string json = File.Exists(path) ? File.ReadAllText(path, GetFileEncodeType(path)) : null;
            lock (timelineSync) { if (observed != epoch) return; referenceJson = json; }
            base.RefreshBestReference();
        }
        private void SelectIdentity(GameplaySnapshot snapshot, bool hardcore)
        {
            string candidate = hardcore ? snapshot.HardcoreTimelineId : snapshot.OrdinaryTimelineId;
            if (candidate == null || startedWithoutIdentity) return;
            if (identity == candidate) {
                if (!snapshot.covered) validation = "本局玩法覆盖证据无效，保留未归类记录。";
                if (frozen == null) selected = snapshot; // Seeds belong to the run, not its timeline.
                return;
            }
            if (frozen != null) { validation = "本局玩法规则或硬核分类已改变，保留本地记录；请重置后开始新时间线。"; return; }
            selected = snapshot; identity = candidate; requested = hardcore;
            string path = ActiveBestPath; long observed = ++epoch;
            lock (timelineSync) referenceJson = null;
            foreach (var point in CheckPoints) point.SetBestReference(new CheckPointNewer { Name = point.Name, NickName = "", BestTS = TimeSpan.Zero });
            RefreshClearPrediction();
            Task.Run(() => {
                string json = null;
                try { if (File.Exists(path)) json = File.ReadAllText(path, GetFileEncodeType(path)); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) { }
                lock (timelineSync) { if (epoch == observed) { referenceJson = json; referenceReady = true; } }
            });
        }
        private bool referenceReady;
        protected override void OnGameplayStarting()
        {
            startProcess = CompetitionGameProcess;
            startHardcore = CaptureHardcoreEvidence().requested;
            startObservation = gameplayReader.CaptureStart(startProcess);
        }
        private void FreezeStartIdentity()
        {
            if (frozen != null || !GameplayRunStarted || startObservation?.Pending == true) return;
            if (startObservation != null) {
                var origin = startObservation.Snapshot;
                if (origin == null) { startedWithoutIdentity = true; identity = null; }
                else SelectIdentity(origin, startHardcore);
            }
            if (identity == null) { startedWithoutIdentity = true; validation = "开局时玩法尚未识别，本局保留为未归类记录。"; }
            else { frozen = identity; frozenHardcore = requested; }
        }
        private GameplaySnapshot CurrentGameplay()
        {
            var start = ReferenceEquals(startProcess, CompetitionGameProcess) ? startObservation : null;
            return gameplayReader.CurrentOrCapturedStart(start) ?? ResumedGameplay;
        }
        protected override void OnTick()
        {
            RefreshHardcoreRuntime();
            ObserveGameplay();
            bool ready; lock (timelineSync) { ready = referenceReady; referenceReady = false; }
            if (ready) {
                try { base.RefreshBestReference(); }
                catch (Exception e) when (e is IOException || e is ArgumentException || e is InvalidOperationException) { lock (timelineSync) referenceJson = null; }
            }
            base.OnTick();
            FreezeStartIdentity();
            ArchiveCompletedResult();
        }
        private void ArchiveCompletedResult()
        {
            if (archiveCompletedRun) {
                archiveCompletedRun = false;
                string path = GetExportPath(DateTime.Now), contents = GetRStr();
                archiveTask = Task.Run(async () => {
                    for (int retry = 0; retry < 3; ++retry) {
                        try { BestTimelineStorage.Write(path, contents, false); archiveError = ""; return; }
                        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { archiveError = "本地跑次留档失败：" + e.Message; }
                        await Task.Delay(100).ConfigureAwait(false);
                    }
                });
            }
        }
        protected override void OnCheckPointEnd() { base.OnCheckPointEnd(); completed = true; archiveCompletedRun = true; }
        private void ObserveGameplay()
        {
            // Closing PAL after the final node cannot invalidate an already
            // completed run; Reset is the only entry to a new classification.
            if (completed && CurrentStep >= CheckPoints.Count) return;
            if (completed) completed = false; // A manual Jump resumed this run.
            // Keep the frozen timeline in ordinary and hardcore restart gaps.
            // The shared observer rechecks the new process's own evidence;
            // pending exports remain unverified, with no sticky gap failure.
            if (GameplayRestartPending) return;
            if (GameplayContinuationError.Length != 0) validation = GameplayContinuationError;
            FreezeStartIdentity();
            if (startObservation?.Pending == true) return;
            gameplayReader.Observe(CompetitionGameProcess);
            var snapshot = CurrentGameplay();
            if (frozen != null && snapshot == null) validation = "本局玩法证据中断，保留未归类记录。";
            if (route != null && snapshot != null) {
                var evidence = CaptureHardcoreEvidence();
                SelectIdentity(snapshot, evidence.requested);
                if (frozen != null && frozenHardcore && !evidence.run_verified)
                    validation = "本局硬核未验证，成绩只保留在未归类时间线。";
            }
            // Existing speed/content checks remain authoritative. Missing new facts
            // never cause a known mode to be guessed or an official line to be overwritten.

        }
        protected override void ValidateBestReference(HObj data)
        {
            if (!data.HasValue("TimelineIdentity") || data.GetValue<string>("TimelineIdentity") != identity)
                throw new InvalidDataException("最佳线不属于当前玩法组合。旧成绩请使用“导入参考线”。");
        }
        protected override void ValidateTimerImport(string json)
        {
            var data = new HObj(json);
            ValidateBestReference(data);
            if (selected == null || !selected.covered || validation.Length != 0 || GameplayRestartPending || GameplayContinuationError.Length != 0 || !data.HasValue("GameplayVerified") ||
                !data.GetValue<bool>("GameplayVerified") || data.HasValue("ReferenceTimeline") && data.GetValue<bool>("ReferenceTimeline"))
                throw new InvalidDataException("此记录不具备当前玩法的完整证据，未导入游戏或计时状态。");
            base.ValidateTimerImport(json);
        }
        protected override void FillMoreTimerData(HObj data)
        {
            base.FillMoreTimerData(data);
            data["TimelineIdentity"] = frozen ?? identity ?? "";
            data["GameplayVerified"] = selected != null && selected.covered && validation.Length == 0 &&
                !startedWithoutIdentity && !GameplayRestartPending && GameplayContinuationError.Length == 0 && (!requested || CaptureHardcoreEvidence().run_verified);
            data["GameplayValidation"] = GameplayRestartPending ? "正在核对重启后的玩法证据。" :
                GameplayContinuationError.Length != 0 ? GameplayContinuationError : validation;
            data["GameplayRulesSha256"] = selected?.rules_sha256 ?? "";
            data["GameplayDetails"] = selected == null ? new HObj() : new HObj(CompetitionProtocol.Json().Serialize(selected));
            // An automatic mod line must not impersonate an old cloud leaderboard.
            data["LeaderboardCategory"] = "";
        }
        internal override void CaptureCompetitionGameplay(CompetitionObservation observation)
        {
            observation.Hardcore = CaptureHardcoreEvidence();
            // Local best-line selection is frozen, but online eligibility must
            // keep observing current rules. Added ranking-only facts must not
            // be hidden by the unchanged legacy TimelineIdentity.
            var current = CurrentGameplay();
            RememberGameplayForContinuation(current);
            observation.Gameplay = current?.Identity;
            observation.Ranking = current?.ranking;
            observation.TimelineId = frozen ?? identity;
            if (current == null || !current.covered || current.ranking?.covered != true || validation.Length != 0) observation.ValidationError = "本局玩法未登记或证据无效";
            if (GameplayContinuationError.Length != 0) observation.ValidationError = GameplayContinuationError;
        }
        protected override string GetExportPath(DateTime now) => Path.Combine(Path.GetDirectoryName(GetScoreSavePath(now)), now.ToString("yyyyMMddHHmmssfff") + ".json");
        protected override void FillReferenceIdentity(HObj data) { data["TimelineIdentity"] = identity ?? ""; data["GameplayVerified"] = false; }
        public override void Unload()
        {
            // Only shutdown may briefly wait for this local write; timing never waits.
            try { archiveTask?.Wait(1000); } catch (AggregateException) { }
            base.Unload();
        }
        internal void ImportReference(string filename)
        {
            if (identity == null) throw new InvalidDataException("请先连接游戏，确认当前玩法。");
            var data = new HObj(File.ReadAllText(filename, GetFileEncodeType(filename)));
            var names = data.GetValue<HObj>("CheckPoints").ToList().Cast<HObj>().Select(p => p.GetValue<string>("name"));
            if (CompetitionProtocol.RouteHash(names.Select(CompetitionProtocol.CheckpointId)) != route) throw new InvalidDataException("参考线节点或顺序不同。");
            data["TimelineIdentity"] = identity; data["ReferenceTimeline"] = true; data["GameplayVerified"] = false;
            BestTimelineStorage.Write(ActiveBestPath, data.ToJson()); RefreshBestReference();
        }
        public override void Reset()
        {
            base.Reset(); frozen = null; validation = ""; frozenHardcore = false; startedWithoutIdentity = false; archiveCompletedRun = false; completed = false;
            identity = null; selected = null; ++epoch;
            startObservation = null; startProcess = null; startHardcore = false;
            lock (timelineSync) { referenceJson = null; referenceReady = false; }
        }
        protected override string FormatGameTitle(string version)
        {
            var snapshot = selected;
            return snapshot == null ? base.FormatGameTitle(version) + "&未归类" :
                version + "－" + snapshot.Label(requested);
        }
        public override void InitUI()
        {
            base.InitUI();
            var details = form.NewMenuItem(); details.Text = "当前玩法时间线…";
            details.Click += delegate {
                string summary = selected == null ? "尚未收到配套运行时的玩法快照。" : selected.Label(requested) + "\r\n规则：" + selected.rules_sha256;
                using (var dialog = new Form { Text = "自动玩法时间线", Width = 760, Height = 560, StartPosition = FormStartPosition.CenterParent }) {
                    dialog.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                        Text = summary + "\r\n" + archiveError + "\r\n" + validation + "\r\n" + ActiveBestPath + "\r\n\r\n" +
                        string.Join("\r\n", selected?.rules.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + " = " + p.Value) ?? Enumerable.Empty<string>()) });
                    dialog.ShowDialog(form);
                }
            };
            var import = form.NewMenuItem(); import.Text = "导入旧最佳线为当前玩法参考…";
            import.Click += delegate {
                if (identity == null) { form.Error("请先连接游戏，确认当前玩法。"); return; }
                using (var picker = new OpenFileDialog { Filter = "时间线|*.txt;*.json" }) {
                    if (picker.ShowDialog(form) != DialogResult.OK) return;
                    try {
                        ImportReference(picker.FileName);
                    } catch (Exception e) { form.Error("参考线未导入：" + e.Message); }
                }
            };
        }
    }
}
