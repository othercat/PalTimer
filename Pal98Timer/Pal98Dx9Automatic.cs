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
        private readonly LocalConfigurationObserver configurationReader = new LocalConfigurationObserver();
        private readonly object configurationSync = new object();
        private LocalTimelineConfiguration localConfiguration;
        private bool configurationFrozen, manualConfiguration;
        private Task<LocalTimelineConfiguration> startConfiguration;
        private string configurationDiagnostic = "";
        private string metadataDiagnostic = "";
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
        protected override bool PauseOnTimingValidationError => false;
        public override string ActiveBestPath => localConfiguration?.BestPath ?? LegacyBestPath;
        private string LegacyBestPath => TimerUserSettings.TimelinePath(selected == null || identity == null ?
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Timelines", "Unclassified", unknown, "best.json") :
            TimelineIdentity.PathFor(AppDomain.CurrentDomain.BaseDirectory, identity, selected.covered && validation.Length == 0, "best.json"));
        protected override string RelayFileName => Path.Combine(Path.GetDirectoryName(ActiveBestPath), "SRPG.bin");
        protected override string GetScoreSavePath(DateTime now) => localConfiguration?.BestPath ?? (identity != null &&
            (GameplayRestartPending || GameplayContinuationError.Length != 0 || requested && !CaptureHardcoreEvidence().run_verified) ?
            TimerUserSettings.TimelinePath(TimelineIdentity.PathFor(AppDomain.CurrentDomain.BaseDirectory, identity, false, "best.json")) : ActiveBestPath);
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
            string path, legacy; long observed;
            lock (configurationSync) lock (timelineSync) {
                path = ActiveBestPath; legacy = localConfiguration != null && identity != null ? LegacyBestPath : null;
                observed = ++epoch;
            }
            string json = ReadReference(path, legacy);
            lock (timelineSync) {
                if (observed != epoch) return;
                referenceJson = json; referenceReady = false;
                base.RefreshBestReference();
            }
        }
        private static string ReadReference(string path, string legacy)
        {
            path = BestTimelineStorage.IoPath(path);
            if (legacy != null) legacy = BestTimelineStorage.IoPath(legacy);
            if (File.Exists(path)) return File.ReadAllText(path, GetFileEncodeType(path));
            return legacy != null && File.Exists(legacy) ? File.ReadAllText(legacy, GetFileEncodeType(legacy)) : null;
        }
        private void QueueReference()
        {
            string path, legacy; long observed;
            lock (configurationSync) lock (timelineSync) {
                path = ActiveBestPath; legacy = localConfiguration != null && identity != null ? LegacyBestPath : null;
                observed = ++epoch;
                referenceJson = null; referenceReady = false;
                foreach (var point in CheckPoints) point.SetBestReference(new CheckPointNewer { Name = point.Name, NickName = "", BestTS = TimeSpan.Zero });
                RefreshClearPrediction();
            }
            Task.Run(() => {
                string json = null, error = "";
                try { json = ReadReference(path, legacy); }
                catch (Exception e) when (OptionalMetadataFailure(e)) { error = "参考线暂不可读（" + e.GetType().Name + "），计时继续。"; }
                lock (timelineSync) { if (epoch == observed) { referenceJson = json; referenceReady = true; if (error.Length != 0) metadataDiagnostic = error; } }
            });
        }
        private void ApplyQueuedReference()
        {
            lock (timelineSync) {
                if (!referenceReady) return;
                referenceReady = false;
                try { base.RefreshBestReference(); }
                catch (Exception e) when (OptionalMetadataFailure(e)) {
                    referenceJson = null; Best = null;
                    foreach (var point in CheckPoints) point.SetBestReference(new CheckPointNewer { Name = point.Name, BestTS = TimeSpan.Zero });
                    RefreshClearPrediction();
                    metadataDiagnostic = "参考线无法应用（" + e.GetType().Name + "），计时继续。";
                }
            }
        }
        internal void SelectLocalConfiguration(LocalTimelineConfiguration value, bool manuallySelected)
        {
            lock (configurationSync) {
            if (value?.Valid != true || value.route_sha256 != route) throw new InvalidDataException("所选配置与当前节点路线不一致。");
            if (configurationFrozen && localConfiguration != null && localConfiguration.key != value.key)
                throw new InvalidOperationException("本局配置已经固定。请保存本局后重置，再选择其他配置。");
            bool changed = localConfiguration?.key != value.key;
            if (manuallySelected) value.Remember();
            localConfiguration = value;
            manualConfiguration = manuallySelected;
            if (manuallySelected) configurationDiagnostic = "本局参考线归属由玩家明确选择。";
            if (changed) QueueReference();
            }
        }
        private void ObserveLocalConfiguration()
        {
            lock (configurationSync) {
            if (configurationFrozen) { CompleteStartConfiguration(); return; }
            if (manualConfiguration) return;
            if (GameplayRunStarted) { FreezeLocalConfiguration(); return; }
            configurationReader.Observe(CompetitionGameProcess, route);
            var value = configurationReader.Current;
            configurationDiagnostic = configurationReader.Error;
            if (value != null) SelectLocalConfiguration(value, false);
            }
        }
        private void FreezeLocalConfiguration()
        {
            lock (configurationSync) {
            if (configurationFrozen) return;
            if (!manualConfiguration) {
                startConfiguration = configurationReader.CaptureStart(CompetitionGameProcess, route);
                bool hadReference = localConfiguration != null;
                localConfiguration = null;
                if (hadReference) QueueReference();
            }
            configurationFrozen = true;
            CompleteStartConfiguration();
            }
        }
        private void CompleteStartConfiguration()
        {
            lock (configurationSync) {
            if (manualConfiguration || startConfiguration == null) return;
            if (!startConfiguration.IsCompleted) { configurationDiagnostic = "正在读取开局配置，计时继续。"; return; }
            var completed = startConfiguration;
            startConfiguration = null;
            var captured = LocalConfigurationObserver.CompletedValue(completed);
            if (captured != null) { SelectLocalConfiguration(captured, false); configurationDiagnostic = "本局配置已固定。"; }
            else configurationDiagnostic = "开局时未能读取配置；保存前请选择本局配置归属。";
            }
        }
        private bool ChooseLocalConfiguration()
        {
            if (form == null) return false;
            using (var picker = new LocalTimelineConfigurationForm(route, configurationReader.Current ?? localConfiguration)) {
                if (picker.ShowDialog(form) != DialogResult.OK) return false;
                SelectLocalConfiguration(picker.Selected, true);
                RefreshBestReference();
                return true;
            }
        }
        protected override void ValidateBestSave()
        {
            CompleteStartConfiguration();
            if (localConfiguration != null) return;
            if (identity != null && GetScoreValidationError().Length == 0) { base.ValidateBestSave(); return; }
            if (!ChooseLocalConfiguration()) throw new OperationCanceledException("请先选择本局本地配置归属；当前成绩已保留。");
        }
        private void SetLocalReferenceIdentity(HObj data)
        {
            data["LocalReferenceConfiguration"] = new HObj(LocalTimelineConfiguration.Json().Serialize(localConfiguration));
            data["LocalReferenceBinding"] = manualConfiguration ? "manual" : "captured-at-start";
            data["ReferenceTimeline"] = true;
            data["GameplayVerified"] = false;
            data["TimingRulesVerified"] = false;
            data["HardcoreRunVerified"] = false;
        }
        protected override string PrepareBestSave(string contents)
        {
            if (localConfiguration == null) return contents;
            localConfiguration.Remember();
            var data = new HObj(contents); SetLocalReferenceIdentity(data);
            return data.ToJson();
        }
        public override void CreateBestReferenceIfMissing()
        {
            if (localConfiguration == null && identity == null && !ChooseLocalConfiguration())
                throw new OperationCanceledException("请先选择本地配置归属。");
            base.CreateBestReferenceIfMissing();
        }
        private void SelectIdentity(GameplaySnapshot snapshot, bool hardcore)
        {
            lock (configurationSync) {
            string candidate = hardcore ? snapshot.HardcoreTimelineId : snapshot.OrdinaryTimelineId;
            if (candidate == null || startedWithoutIdentity) return;
            if (identity == candidate) {
                if (!snapshot.covered) validation = "本局玩法覆盖证据无效，保留未归类记录。";
                if (frozen == null) selected = snapshot; // Seeds belong to the run, not its timeline.
                return;
            }
            if (frozen != null) { validation = "本局玩法规则或硬核分类已改变，保留本地记录；请重置后开始新时间线。"; return; }
            selected = snapshot; identity = candidate; requested = hardcore;
            QueueReference();
            }
        }
        private bool referenceReady;
        protected override void OnGameplayStarting()
        {
            UpdateOptionalMetadata(FreezeLocalConfiguration);
            startProcess = CompetitionGameProcess;
            startHardcore = CaptureHardcoreEvidence().requested;
            UpdateOptionalMetadata(() => startObservation = gameplayReader.CaptureStart(startProcess), true);
        }

        private static bool OptionalMetadataFailure(Exception error) => !(error is OutOfMemoryException) &&
            !(error is StackOverflowException) && !(error is AccessViolationException) && !(error is System.Threading.ThreadAbortException);

        private void UpdateOptionalMetadata(Action update, bool gameplay = false)
        {
            // Configuration, classification and best-line parsing are optional
            // inputs. An unexpected parser/schema failure must not skip the
            // base tick, including its normal pause and checkpoint handling.
            try { update(); }
            catch (Exception error) when (OptionalMetadataFailure(error)) {
                metadataDiagnostic = "配置或参考线暂不可用（" + error.GetType().Name + "），计时继续。";
                if (gameplay) validation = "本局玩法识别异常，仅保留本地记录。";
            }
        }
        private void FreezeStartIdentity()
        {
            if (GameplayRunStarted && !configurationFrozen) FreezeLocalConfiguration();
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
            UpdateOptionalMetadata(ObserveLocalConfiguration);
            UpdateOptionalMetadata(() => { RefreshHardcoreRuntime(); ObserveGameplay(); }, true);
            UpdateOptionalMetadata(ApplyQueuedReference);
            base.OnTick();
            UpdateOptionalMetadata(ObserveLocalConfiguration);
            UpdateOptionalMetadata(FreezeStartIdentity, true);
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
            if (data.HasValue("LocalReferenceConfiguration")) {
                var saved = LocalTimelineConfiguration.Json().Deserialize<LocalTimelineConfiguration>(data.GetValue<HObj>("LocalReferenceConfiguration").ToJson());
                if (saved?.Valid != true || localConfiguration == null || saved.key != localConfiguration.key || saved.route_sha256 != route ||
                    !data.HasValue("ReferenceTimeline") || !data.GetValue<bool>("ReferenceTimeline")) throw new InvalidDataException("本地参考线不属于当前配置或节点路线。");
                var names = data.GetValue<HObj>("CheckPoints").ToList().Cast<HObj>().Select(p => CompetitionProtocol.CheckpointId(p.GetValue<string>("name")));
                if (CompetitionProtocol.RouteHash(names) != route) throw new InvalidDataException("本地参考线节点或顺序不同。");
                return;
            }
            if (!data.HasValue("TimelineIdentity") || data.GetValue<string>("TimelineIdentity") != identity)
                throw new InvalidDataException("最佳线不属于当前玩法组合。旧成绩请使用“导入参考线”。");
        }
        protected override void ValidateTimerImport(string json)
        {
            base.ValidateTimerImport(json);
        }
        protected override void FillMoreTimerData(HObj data)
        {
            base.FillMoreTimerData(data);
            data["TimelineIdentity"] = frozen ?? identity ?? "";
            data["GameplayVerified"] = !competitionImportedRun && selected != null && selected.covered && validation.Length == 0 &&
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
        protected override void FillReferenceIdentity(HObj data) {
            data["TimelineIdentity"] = identity ?? ""; data["GameplayVerified"] = false;
            if (localConfiguration != null) { localConfiguration.Remember(); SetLocalReferenceIdentity(data); }
        }
        public override void Unload()
        {
            // Only shutdown may briefly wait for this local write; timing never waits.
            try { archiveTask?.Wait(1000); } catch (AggregateException) { }
            base.Unload();
        }
        internal void ImportReference(string filename)
        {
            if (identity == null && localConfiguration == null) throw new InvalidDataException("请先连接游戏或选择本地配置归属。");
            var data = new HObj(File.ReadAllText(filename, GetFileEncodeType(filename)));
            var names = data.GetValue<HObj>("CheckPoints").ToList().Cast<HObj>().Select(p => p.GetValue<string>("name"));
            if (CompetitionProtocol.RouteHash(names.Select(CompetitionProtocol.CheckpointId)) != route) throw new InvalidDataException("参考线节点或顺序不同。");
            data["TimelineIdentity"] = identity; data["ReferenceTimeline"] = true; data["GameplayVerified"] = false;
            if (localConfiguration != null) { localConfiguration.Remember(); SetLocalReferenceIdentity(data); }
            BestTimelineStorage.Write(ActiveBestPath, data.ToJson()); RefreshBestReference();
        }
        public override void Reset()
        {
            base.Reset(); frozen = null; validation = ""; frozenHardcore = false; startedWithoutIdentity = false; archiveCompletedRun = false; completed = false;
            lock (configurationSync) {
                identity = null; selected = null;
                localConfiguration = null; configurationFrozen = manualConfiguration = false; configurationReader.Reset(); configurationDiagnostic = metadataDiagnostic = "";
                startConfiguration = null;
                lock (timelineSync) { ++epoch; referenceJson = null; referenceReady = false; }
            }
            startObservation = null; startProcess = null; startHardcore = false;
        }
        protected override string FormatGameTitle(string version)
        {
            var snapshot = selected;
            return version + "－" + GameplayPresentation.Label(snapshot, RecordedTimingMode, requested);
        }
        public override void InitUI()
        {
            base.InitUI();
            var details = form.NewMenuItem(); details.Text = "当前玩法时间线…";
            details.Click += delegate {
                string summary = selected == null ? "尚未收到配套运行时的玩法快照。" : selected.Label(requested) + "\r\n规则：" + selected.rules_sha256;
                using (var dialog = new Form { Text = "自动玩法时间线", Width = 760, Height = 560, StartPosition = FormStartPosition.CenterParent }) {
                    dialog.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                        Text = summary + "\r\n" + (localConfiguration == null ? "未选择本地配置" : "本地参考配置：" + localConfiguration) +
                        "\r\n" + configurationDiagnostic + "\r\n" + metadataDiagnostic + "\r\n" + archiveError + "\r\n" + validation + "\r\n" + ActiveBestPath + "\r\n\r\n" +
                        string.Join("\r\n", selected?.rules.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + " = " + p.Value) ?? Enumerable.Empty<string>()) });
                    dialog.ShowDialog(form);
                }
            };
            var choose = form.NewMenuItem(); choose.Text = "选择本地最佳线配置…";
            choose.Click += delegate {
                try { ChooseLocalConfiguration(); }
                catch (Exception error) when (LocalConfigurationReader.ReadError(error)) { form.Error(error.Message); }
            };
            var import = form.NewMenuItem(); import.Text = "导入旧最佳线为当前玩法参考…";
            import.Click += delegate {
                if (identity == null && localConfiguration == null) { form.Error("请先连接游戏或选择本地配置归属。"); return; }
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
