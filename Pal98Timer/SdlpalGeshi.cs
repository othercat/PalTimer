// SPDX-License-Identifier: GPL-2.0-only
using HFrame.ENT;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pal98Timer
{
    internal sealed class SdlpalLocationRule
    {
        public int Scene { get; set; }
        public int? X { get; set; } public int? Y { get; set; } public int? Radius { get; set; }
        internal bool Valid => Scene > 0 && Scene < 300 &&
            (!X.HasValue && !Y.HasValue && !Radius.HasValue || X >= 0 && X <= 65535 && Y >= 0 && Y <= 65535 && Radius >= 0 && Radius <= 100);
        internal bool Matches(SdlpalSnapshot s) => s.scene == Scene && (!X.HasValue ||
            Math.Abs((long)s.viewportX - X.Value) <= Radius.Value * 16 && Math.Abs((long)s.viewportY - Y.Value) <= Radius.Value * 8);
        internal string Identity => Scene.ToString(CultureInfo.InvariantCulture) + "," + X + "," + Y + "," + Radius;
    }
    internal sealed class SdlpalSplitRule
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public int? Team { get; set; }
        public int? Scene { get; set; }
        public int? Enemy { get; set; }
        public int[] Scenes { get; set; }
        public int? X { get; set; } public int? Y { get; set; } public int? Radius { get; set; }
        public int? Music { get; set; } public int? Item { get; set; }
        public SdlpalLocationRule[] Alternatives { get; set; }
        public int? ReturnScene { get; set; }
        internal bool MatchesState(SdlpalSnapshot s) => s.started && (!Scene.HasValue || s.scene == Scene) &&
            (Kind == "position" && s.scene == Scene && Math.Abs((long)s.viewportX - X.Value) <= Radius.Value * 16 && Math.Abs((long)s.viewportY - Y.Value) <= Radius.Value * 8 ||
             Kind == "music" && s.music == Music || Kind == "item" && s.inventory != null && s.inventory.Any(i => i.id == Item && i.amount > 0) ||
             Kind == "any-location" && Alternatives.Any(p => p.Matches(s)));
        internal static SdlpalSplitRule[] Defaults(string profile) => profile == "geshi-easy-v1" ? Defaults() : SdlpalDreamRoutes.ForProfile(profile);
        internal bool Matches(SdlpalBattleEvent battle) => ((Kind == "battle" || Kind == "completion" && Team.HasValue) && battle.result == 3 || Kind == "battle-end") &&
            (!Team.HasValue || Team == battle.team) && (!Scene.HasValue || Scene == battle.scene) &&
            (Scenes == null || Scenes.Contains(battle.scene)) &&
            (!Enemy.HasValue || battle.enemies.Contains(Enemy.Value));
        internal static SdlpalSplitRule[] Defaults() => new[] {
            new SdlpalSplitRule { Id = "geshi-eight-headed-serpent", Name = "八头蛇胜利", Kind = "battle", Team = 315, Enemy = 547 },
            new SdlpalSplitRule { Id = "geshi-ending", Name = "通关", Kind = "completion" }
        };
        internal static void Validate(SdlpalSplitRule[] rules)
        {
            if (rules == null || rules.Length < 1 || rules.Length > 64 || rules.Any(r => r == null || !CompetitionProtocol.Identifier(r.Id) ||
                string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 80 || r.Name.Any(char.IsControl) ||
                (r.Kind != "battle" && r.Kind != "battle-end" && r.Kind != "scene" && r.Kind != "completion" && r.Kind != "position" && r.Kind != "music" && r.Kind != "item" && r.Kind != "any-location" && r.Kind != "water-pearl") ||
                r.Kind == "any-location" && (r.Alternatives == null || r.Scene.HasValue) ||
                r.Alternatives != null && (r.Kind != "any-location" || r.Alternatives.Length < 1 || r.Alternatives.Length > 8 || r.Alternatives.Any(p => p == null || !p.Valid)) ||
                r.Kind == "water-pearl" && (!r.Scene.HasValue || r.Scene < 1 || r.Scene >= 300 || !r.ReturnScene.HasValue || r.ReturnScene < 1 || r.ReturnScene >= 300 || r.Scene == r.ReturnScene) ||
                r.ReturnScene.HasValue && r.Kind != "water-pearl" ||
                (r.Kind == "battle" || r.Kind == "battle-end") && !r.Team.HasValue && !r.Enemy.HasValue || r.Kind == "scene" && !r.Scene.HasValue ||
                r.Scenes != null && (r.Scenes.Length == 0 || r.Scenes.Length > 16 || r.Scenes.Any(v => v < 1 || v >= 300) || r.Scenes.Distinct().Count() != r.Scenes.Length) ||
                r.Kind == "completion" && (r.Team.HasValue || r.Enemy.HasValue || r.Scene.HasValue) && (!r.Team.HasValue || !r.Enemy.HasValue || !r.Scene.HasValue) ||
                r.Kind == "position" && (!r.Scene.HasValue || !r.X.HasValue || !r.Y.HasValue || !r.Radius.HasValue || r.Radius < 0 || r.Radius > 100 || r.X < 0 || r.X > 65535 || r.Y < 0 || r.Y > 65535) ||
                r.Kind == "music" && (!r.Music.HasValue || r.Music < 1 || r.Music > 999) ||
                r.Kind == "item" && (!r.Item.HasValue || r.Item < 1 || r.Item >= 700) ||
                r.Team < 0 || r.Team > 65535 || r.Scene < 0 || r.Scene > 65535 || r.Enemy < 0 || r.Enemy > 65535) ||
                rules.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != rules.Length ||
                rules.Count(r => r.Kind == "completion") != 1 || rules[rules.Length - 1].Kind != "completion")
                throw new InvalidDataException("节点须有唯一 ID；战斗填队伍或敌人 ID，场景填 scene，最后且仅一个节点为 completion");
        }
        internal static string Identity(SdlpalSplitRule[] rules)
        {
            // Display names are editable without splitting a route/category.
            string text = string.Join("\n", rules.Select(r => GameplayModeReader.Quote(r.Id) + ":" + r.Kind + ":" +
                (r.Team.HasValue ? r.Team.Value.ToString(CultureInfo.InvariantCulture) : "-") + ":" +
                (r.Scene.HasValue ? r.Scene.Value.ToString(CultureInfo.InvariantCulture) : "-") + ":" +
                (r.Enemy.HasValue ? r.Enemy.Value.ToString(CultureInfo.InvariantCulture) : "-") +
                (r.Kind == "position" ? ":" + r.X + ":" + r.Y + ":" + r.Radius : r.Kind == "music" ? ":" + r.Music : r.Kind == "item" ? ":" + r.Item : "") +
                (r.Kind == "any-location" ? ":any=" + string.Join(";", r.Alternatives.Select(p => p.Identity).OrderBy(v => v, StringComparer.Ordinal)) :
                    r.Kind == "water-pearl" ? ":return=" + r.ReturnScene + ":gate=pal98-water-pearl-v1" : "") +
                (r.Scenes == null ? "" : ":scenes=" + string.Join(",", r.Scenes.OrderBy(v => v)))));
            return CompetitionProtocol.Hash("SDLPAL.GeshiSplits.v1\n" + text + "\n");
        }
    }

    // This observer is independent of clock/UI/network. Only newly observed
    // victories qualify; load epochs and event-ring gaps cannot fabricate wins.
    internal sealed class SdlpalRunObserver
    {
        internal SdlpalSnapshot Snapshot;
        internal readonly HashSet<string> Reached = new HashSet<string>(StringComparer.Ordinal);
        internal string Issue = "";
        internal bool BeganHere, Started, EpochChanged;
        internal bool WuQiangChanged;
        internal bool BestConfigurationChanged;
        private ulong lastBattle;
        private string initialIdentity;
        private bool initialWuQiang;
        private string initialBestConfiguration;
        private readonly Dictionary<string, Pal98WaterSpiritPearlGate> waterPearlGates = new Dictionary<string, Pal98WaterSpiritPearlGate>();
        internal SdlpalSnapshot ConfigurationSnapshot => Snapshot;
        internal void Observe(SdlpalSnapshot next, SdlpalSplitRule[] rules)
        {
            EpochChanged = Snapshot != null && (Snapshot.Pid != next.Pid || Snapshot.Created != next.Created || Snapshot.epoch != next.epoch);
            bool first = Snapshot == null;
            bool freshStart = !Started && (!first && !Snapshot.started || EpochChanged) && next.started && next.loadedFrom == 0;
            if (first || EpochChanged)
            {
                waterPearlGates.Clear();
                lastBattle = next.battleEvents.Length == 0 ? (next.battle.active && next.battle.sequence > 0 ? next.battle.sequence - 1 : next.battle.sequence) : next.battleEvents.Last().seq;
                bool ordinaryLoad = Started && Snapshot != null && Snapshot.Pid == next.Pid &&
                    Snapshot.Created == next.Created && next.loadedFrom > 0 && initialIdentity == next.ResourceEngineIdentity;
                // Loading an ordinary game save keeps this run's time and reached
                // checkpoints. Rebase the battle event cursor without replaying it.
                if (Started) { if (!ordinaryLoad) { Issue = "游戏已重开或切换进程"; Reached.Clear(); } }
                else if (freshStart) { BeganHere = true; initialIdentity = next.ResourceEngineIdentity; }
                else if (next.started) { Issue = "中途连接或载入存档"; BeganHere = false; initialIdentity = next.ResourceEngineIdentity; }
            }
            if (next.started && !Started)
            {
                Started = true;
                initialWuQiang = next.wuqiang;
                initialBestConfiguration = RankingConfiguration.Digest(next.RankingFacts());
                if (!first && !EpochChanged && !Snapshot.started) { BeganHere = true; initialIdentity = next.ResourceEngineIdentity; }
            }
            if (Started && initialIdentity != next.ResourceEngineIdentity) Issue = "计时期间资源或引擎配置变化";
            if (Started && initialWuQiang != next.wuqiang) WuQiangChanged = true;
            if (Started && initialBestConfiguration != RankingConfiguration.Digest(next.RankingFacts())) BestConfigurationChanged = true;
            if (!first && !EpochChanged)
            {
                var pending = next.battleEvents.Where(e => e.seq > lastBattle).ToArray();
                if (pending.Length != 0 && lastBattle != 0 && pending[0].seq != lastBattle + 1)
                { Issue = "战斗事件记录缺口，请检查节点"; }
                else foreach (var battle in pending)
                    foreach (var rule in rules)
                        if (!Reached.Contains(rule.Id) && rule.Matches(battle)) { Reached.Add(rule.Id); break; }
                if (pending.Length != 0) lastBattle = pending.Last().seq;
            }
            if (next.started && (Snapshot == null || !Snapshot.started || next.scene != Snapshot.scene))
                foreach (var rule in rules)
                    if (!Reached.Contains(rule.Id) && rule.Kind == "scene" && next.scene == rule.Scene) { Reached.Add(rule.Id); break; }
            foreach (var rule in rules) if (rule.MatchesState(next)) Reached.Add(rule.Id);
            foreach (var rule in rules.Where(r => r.Kind == "water-pearl"))
            {
                string key = rule.Id + ":" + rule.Scene + ":" + rule.ReturnScene;
                if (!waterPearlGates.TryGetValue(key, out Pal98WaterSpiritPearlGate pearl))
                    waterPearlGates[key] = pearl = new Pal98WaterSpiritPearlGate();
                if (!next.started || !next.partyOffsetX.HasValue || !next.partyOffsetY.HasValue || !next.partyDirection.HasValue ||
                    next.ItemDataError != null || next.inventory == null) { pearl.Reset(); continue; }
                // Map only the two audited scene identities; reuse the native
                // trigger-range/direction and item-increase gate unchanged.
                int area = next.scene == rule.Scene ? Pal98WaterSpiritPearlGate.NormalExchangeArea :
                    next.scene == rule.ReturnScene ? Pal98WaterSpiritPearlGate.DaliReturnArea : 0;
                pearl.ObserveGameState(area, next.viewportX + next.partyOffsetX.Value, next.viewportY + next.partyOffsetY.Value,
                    next.partyDirection.Value, next.inventory.Where(i => i.id == 265).Sum(i => i.amount));
                if (pearl.CanComplete()) Reached.Add(rule.Id);
            }
            foreach (var rule in rules)
                if (next.started && rule.Kind == "completion" && next.completed) Reached.Add(rule.Id);
            Snapshot = next;
        }
        internal void MarkImported(SdlpalSnapshot snapshot, bool wuqiangChanged = false, bool bestConfigurationChanged = false)
        {
            waterPearlGates.Clear();
            Snapshot = snapshot; Started = snapshot.started; BeganHere = false; EpochChanged = false;
            WuQiangChanged |= wuqiangChanged;
            BestConfigurationChanged |= bestConfigurationChanged;
            initialWuQiang = snapshot.wuqiang;
            initialBestConfiguration = RankingConfiguration.Digest(snapshot.RankingFacts());
            initialIdentity = snapshot.ResourceEngineIdentity;
            lastBattle = snapshot.battleEvents.Length == 0 ? 0 : snapshot.battleEvents.Last().seq;
            Reached.Clear(); Issue = "接力或时间线导入";
        }
    }

    [TimerCoreDisplayName("SDLPAL 维护版（自动识别内容）")]
    public sealed class SdlpalGeshi : TimerCore
    {
        internal const string Family = "sdlpal-geshi-easy";
        internal const string GameplayName = "隔世追凶-简单";
        private string activeProfile = "geshi-easy-v1";
        internal string ActiveProfile => activeProfile;
        private string RulesFile => activeProfile == "geshi-easy-v1" ? "sdlpal_geshi_splits.json" : "sdlpal_" + activeProfile + "_splits.json";
        private string OverlayLayout => activeProfile == "geshi-easy-v1" ? "sdlpal_geshi_overlay_layout" : "sdlpal_" + activeProfile + "_overlay_layout";
        private readonly object gate = new object();
        private SdlpalBridge bridge;
        private SdlpalRunObserver observer = new SdlpalRunObserver();
        private SdlpalSplitRule[] rules = SdlpalSplitRule.Defaults();
        private readonly PTimer battleTimer = new PTimer(), idleTimer = new PTimer();
        private ulong battleSequence;
        private ulong nextDiscovery;
        private string status = "等待受支持的 SDLPAL 游戏", warning = "";
        private bool busyRelay, unloaded;
        private long relayRun;
        private SdlpalBridge relayBridge;
        private bool freshSnapshot;
        private string bestCategory = "unbound";
        private Dx9OverlayForm overlay;
        private SdlpalItemsForm itemsForm;
        private readonly SdlpalItemTracker items = new SdlpalItemTracker();
        private SdlpalItemSettings itemSettings = new SdlpalItemSettings();
        private ToolStripMenuItem cloudSave, cloudLoad, overlayMenu;
        public SdlpalGeshi(GForm form) : base(form)
        {
            CoreName = "SDLPAL_GESHI_EASY"; CheckInterval = 70;
            try { itemSettings = SdlpalItemSettings.Load(); }
            catch (Exception ex) { warning = "物品监测配置读取失败，使用默认值；原文件保留：" + ex.Message; }
        }
        public override void LoadPlugins()
        {
            lock (gate)
            {
                // The two bundled SDL plugins own these slots even when disabled,
                // so clearing an output is observable by the renderer/cloud cache.
                foreach (var position in new[] { TimerPluginBase.TimerPlugin.EPluginPosition.BL, TimerPluginBase.TimerPlugin.EPluginPosition.BR })
                    if (!Plugins.ContainsKey(position))
                    { var plugin = new SdlpalItemPlugin(position); plugin.OnLoad(); Plugins.Add(position, plugin); }
                base.LoadPlugins(); PublishItemPlugins();
            }
        }
        internal SdlpalItemSettings ItemSettings { get { lock (gate) return itemSettings.Copy(); } }
        internal SdlpalItemView ItemView { get { lock (gate) return items.View; } }
        internal void ApplyItemSettings(SdlpalItemSettings value)
        { ApplyProfileItemSettings(value, null); }
        internal void ApplyProfileItemSettings(SdlpalItemSettings value, string profile)
        {
            lock (gate)
            {
                if (unloaded) throw new InvalidOperationException("核心已切换，请重新打开物品配置");
                if (profile != null && profile != activeProfile) throw new InvalidOperationException("游戏内容已切换，请重新打开物品配置");
                value = value.Copy(); value.Save(activeProfile); itemSettings = value; PublishItemPlugins();
            }
        }
        internal void RebaseItems()
        {
            lock (gate)
            {
                items.Reset();
                if (freshSnapshot && observer.Snapshot != null) items.Observe(observer.Snapshot);
                PublishItemPlugins();
            }
        }
        private void PublishItemPlugins() => SendPluginsEvent(SdlpalItemPlugin.EventName, new SdlpalItemPluginText(items.View, itemSettings, T));
        private void ShowItems()
        {
            lock (gate) {
            if (itemsForm == null || itemsForm.IsDisposed)
            {
                string profile = activeProfile;
                itemsForm = new SdlpalItemsForm(() => ItemView, () => ItemSettings, value => ApplyProfileItemSettings(value, profile),
                    () => { lock (gate) { if (activeProfile == profile && !unloaded) RebaseItems(); } }, T);
                itemsForm.Show(form);
            }
            else { itemsForm.Show(); itemsForm.Activate(); }
            }
        }
        internal override Process CompetitionGameProcess => bridge?.Process;
        internal override bool CompetitionPublicationAllowed => observer.Snapshot != null && !observer.Snapshot.diagnostic;
        internal SdlpalSnapshot CurrentSnapshot => observer.Snapshot;
        private string RouteIdentity => SdlpalSplitRule.Identity(rules);
        private string BestCategory => observer.Snapshot == null ? "unbound" : RankingConfiguration.Digest(observer.Snapshot.RankingFacts()).Substring(0, 16);
        public override string ActiveBestPath => TimerUserSettings.BestPath("best" + CoreName + "-" + BestCategory + "-" + RouteIdentity.Substring(0, 16) + ".txt");
        // This edition always displays simplified names, including legacy Big5
        // snapshots and traditional Windows locales. Other cores keep their choice.
        private string T(string text) => Microsoft.VisualBasic.Strings.StrConv(text,
            Microsoft.VisualBasic.VbStrConv.SimplifiedChinese, 0x0804);
        protected override void InitCheckPoints()
        {
            lock (gate)
            {
                rules = SdlpalSplitRule.Defaults(activeProfile);
                string path = TimerUserSettings.GetPath(RulesFile);
                try { if (File.Exists(path)) { var parsed = SdlpalBridge.Json().Deserialize<SdlpalSplitRule[]>(File.ReadAllText(path)); SdlpalSplitRule.Validate(parsed); rules = parsed; } }
                catch (Exception ex) { warning = "节点配置无效，保留默认节点：" + ex.Message; rules = SdlpalSplitRule.Defaults(activeProfile); }
                LoadBest(); _CurrentStep = -1; CheckPoints = new List<CheckPoint>();
                foreach (var rule in rules)
                {
                    string id = rule.Id; var best = GetBest(id);
                    if (string.IsNullOrEmpty(best.NickName)) best.NickName = T(rule.Name);
                    CheckPoints.Add(new CheckPoint(CheckPoints.Count, best) { Check = () => observer.Reached.Contains(id) });
                }
            }
        }
        public override void RefreshBestReference()
        {
            lock (gate)
            {
                // A category without a reference must not inherit the previous
                // category's values, including when parsing fails partway through.
                Best = null;
                try { LoadBest(); }
                catch { Best = null; throw; }
                finally
                {
                    if (CheckPoints != null)
                        foreach (var point in CheckPoints)
                        {
                            var reference = GetBest(point.Name);
                            if (string.IsNullOrEmpty(reference.NickName))
                                reference.NickName = T(rules.First(rule => rule.Id == point.Name).Name);
                            point.SetBestReference(reference);
                        }
                    RefreshClearPrediction();
                }
            }
        }
        protected override void ValidateBestSave()
        {
            if (observer.WuQiangChanged)
                throw new InvalidOperationException("本跑次曾切换吴强无墙模式；请重置后再保存该模式最佳线。时间线仍可导出留作参考。");
            if (observer.BestConfigurationChanged)
                throw new InvalidOperationException("本跑次曾切换资源或玩法配置；请重置后再保存该配置最佳线。时间线仍可导出留作参考。");
        }
        protected override void FillReferenceIdentity(HObj data) { data["SdlpalRoute"] = RouteIdentity; data["SdlpalProfile"] = activeProfile; data["SdlpalCategory"] = BestCategory; }
        protected override void ValidateBestReference(HObj data)
        {
            if (data.GetValue<string>("SdlpalRoute") != RouteIdentity || data.GetValue<string>("SdlpalProfile") != activeProfile || data.GetValue<string>("SdlpalCategory") != BestCategory)
                throw new InvalidDataException("最佳时间线不属于当前内容节点规则");
        }
        protected override void FillMoreTimerData(HObj data)
        {
            FillReferenceIdentity(data); data["TimerCore"] = CoreName;
            data["SdlpalIdentity"] = observer.Snapshot?.IdentityKey ?? "";
            if (observer.Snapshot != null)
            {
                var s = observer.Snapshot;
                data["SdlpalProfileData"] = new HObj(new { s.profile, s.resourceHash, s.resourceHashes, s.build, s.exeHash, s.wuqiang, s.monsterRush, s.invisibleMonsters, s.respawnRule,
                    s.gameplayRuleId, s.gameplayRuleRevision, s.gameplayRuleSha256, s.walkFrameMs, s.fadeCycleMs, s.speed, s.battleSpeed });
            }
            data["SdlpalWuQiangChanged"] = observer.WuQiangChanged;
            data["SdlpalBestConfigurationChanged"] = observer.BestConfigurationChanged;
            data["SdlpalIssue"] = GetScoreValidationError();
            data["SdlpalStatuses"] = new HObj(CheckPoints.Select(p => (object)p.Status.ToString()).ToArray());
            data["SdlpalIdle"] = idleTimer.ToFullString();
        }
        public override string GetScoreValidationError()
        {
            var s = observer.Snapshot;
            if (s == null) return "尚未连接已验证的 SDLPAL 资源";
            if (s.diagnostic) return "诊断模式禁止榜单和成绩上传";
            if (!freshSnapshot) return "游戏快照暂不可用";
            if (s.fadeCycleMs <= 0 || s.walkFrameMs <= 0) return "运行时尚未发布有效速度配置";
            if (!string.IsNullOrEmpty(observer.Issue)) return observer.Issue;
            if (competitionImportedRun || CheckPoints?.Any(p => p.Status == CheckPointStatus.ManualSkipped || p.Status == CheckPointStatus.AutoSkipped) == true)
                return "导入或手动跳过节点，保留实际观察供服务器判定";
            return "";
        }
        internal override void CaptureCompetitionIdentity(out string hash, out string version, out int fade, out int speed, out string error)
        {
            var s = observer.ConfigurationSnapshot; hash = s?.exeHash ?? ""; version = "1.0.0.0"; fade = s?.fadeCycleMs ?? 0;
            speed = 0; error = GetScoreValidationError();
        }
        internal override void CaptureCompetitionGameplay(CompetitionObservation observation)
        {
            var s = observer.ConfigurationSnapshot; if (s == null) return;
            var facts = s.RankingFacts();
            string digest = RankingConfiguration.Digest(facts);
            observation.Gameplay = new GameplayIdentity { content_id = s.profile, content_sha256 = facts["content"], rules_sha256 = digest,
                family = SdlpalBridge.ProfileFamily(s.profile), fade_ms = s.fadeCycleMs, map_speed_ticks = 0, walk_frame_ms = s.walkFrameMs,
                speed = s.speed, battle_speed = s.battleSpeed };
            observation.Ranking = new RankingConfiguration { configuration_id = digest, covered = true, rules = facts };
            observation.Hardcore = new CompetitionHardcore();
            observation.TimelineId = TimelineIdentity.Create(digest, CompetitionProtocol.RouteHash(CheckPoints.Select(p => p.Name)), false);
            observation.ValidationError = GetScoreValidationError();
            var current = observer.Snapshot;
            observation.ReleaseRevision = current.releaseRevision;
            observation.LiveTimingValid = freshSnapshot && !current.diagnostic && current.fadeCycleMs > 0 && current.walkFrameMs > 0;
        }
        protected override void OnTick()
        {
            lock (gate)
            {
                if (unloaded) return;
                if (bridge == null && SdlpalBridge.GetTickCount64() >= nextDiscovery)
                { bridge = SdlpalBridge.Discover(out status); nextDiscovery = SdlpalBridge.GetTickCount64() + 2000; }
                SdlpalSnapshot snapshot;
                if (bridge == null || !bridge.TryRead(out snapshot))
                {
                    freshSnapshot = false;
                    items.Unavailable("游戏未连接或快照不可用"); PublishItemPlugins();
                    status = bridge?.Error ?? status; MT.Stop(); battleTimer.Stop();
                    if (observer.Started) idleTimer.Start();
                    if (bridge != null && (bridge.Process == null || bridge.Process.HasExited)) { bridge.Dispose(); bridge = null; }
                    return;
                }
                if (snapshot.profile != activeProfile)
                {
                    // Save the old run before changing any route or clock state.
                    if (observer.Started)
                        TimerUserSettings.WriteText("sdlpal-previous-" + activeProfile + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + ".json", GetRStr(), new UTF8Encoding(false));
                    activeProfile = snapshot.profile;
                    Reset(); InitCheckPoints();
                    try { itemSettings = SdlpalItemSettings.Load(activeProfile); }
                    catch (Exception ex) { itemSettings = new SdlpalItemSettings(); warning = "当前内容物品配置无效：" + ex.Message; }
                    var oldOverlay = overlay; overlay = null;
                    var oldItems = itemsForm; itemsForm = null;
                    string selectedProfile = activeProfile;
                    if (form != null && form.IsHandleCreated) form.BeginInvoke(new Action(() => {
                        oldOverlay?.Close(); oldItems?.Close();
                        lock (gate) {
                            if (unloaded || activeProfile != selectedProfile) return;
                            var renderer = form.rr;
                            if (renderer == null) return;
                            renderer.ClearAllItem();
                            // A tick may already have advanced the new route before
                            // this UI callback runs. Rebind its values and state,
                            // including when the old and new indices are identical.
                            renderer.ItemIdx = -1;
                            foreach (var point in CheckPoints) {
                                var item = renderer.AddItem(point.GetNickName(), point.Best);
                                point.SetUIItem(item);
                                item.Cur = point.Current;
                                item.Status = point.Status;
                            }
                            renderer.ItemIdx = CurrentStep;
                        }
                    }));
                }
                freshSnapshot = true;
                observer.Observe(snapshot, rules);
                items.Observe(snapshot); PublishItemPlugins();
                if (bestCategory != BestCategory)
                {
                    bestCategory = BestCategory;
                    try { RefreshBestReference(); }
                    catch (Exception ex) { warning = "最佳时间线读取失败：" + ex.Message; }
                }
                if (!observer.BeganHere) competitionImportedRun = observer.Started;
                // Ordinary loads, including the death script's automatic load,
                // continue the existing clock. Epoch changes retain audit evidence
                // and reset the battle clock; they must not synthesize a manual
                // pause that then persists across subsequent process restarts.
                // F8/F9 and explicit relay/timeline imports still own IsUIPause.
                if (observer.EpochChanged || snapshot.battle.sequence != battleSequence) { battleSequence = snapshot.battle.sequence; battleTimer.Reset(); }
                bool running = snapshot.started && snapshot.focused && !IsUIPause && !busyRelay;
                status = !snapshot.started ? "等待新游戏" : busyRelay ? "接力存读档中" : IsUIPause ? "已暂停（F9继续）" : !snapshot.focused ? "游戏失去焦点" : "计时中";
                bool countBattle = snapshot.started && snapshot.battle.active && !snapshot.completed && !busyRelay &&
                    CurrentStep < CheckPoints.Count && (!form.IsBattlePauseNoTimer || running);
                if (countBattle) battleTimer.Start(); else battleTimer.Stop();
                if (running)
                {
                    idleTimer.Stop(); StartAndCheckMainTimer();
                    if (CurrentStep >= CheckPoints.Count) status = "已通关";
                }
                else { MT.Stop(); if (observer.Started && CurrentStep < CheckPoints.Count) idleTimer.Start(); }
            }
        }
        private void PauseForLoad()
        {
            IsUIPause = true; MT.Stop();
            long sequence = ScoreRunSequence;
            if (form != null && form.IsHandleCreated) form.BeginInvoke(new Action(() => {
                if (!unloaded && ScoreRunSequence == sequence && IsUIPause) form.SetUIPause(true);
            }));
        }
        public override void Reset()
        {
            lock (gate) { base.Reset(); observer = new SdlpalRunObserver(); freshSnapshot = false; bestCategory = "unbound"; battleTimer.Reset(); idleTimer.Reset(); battleSequence = 0; items.Reset(); PublishItemPlugins(); }
        }
        public override void Jump(int index)
        {
            lock (gate)
            {
                base.Jump(index);
                if (observer.Started) { AdvanceScoreRunSequence(imported: true); observer.Issue = "手动节点跳转或接力导入"; }
            }
        }
        public override void SetTS(TimeSpan time)
        {
            lock (gate)
            {
                base.SetTS(time); AdvanceScoreRunSequence(imported: true); observer.Issue = "手动修改计时";
            }
        }
        public override bool IsShowC() => false;
        public override bool NeedBlockCtrlEnter() => false;
        public override bool IsMainWatchStar() => IsUIPause;
        public override string GetSmallWatch() => battleTimer.CurrentTS.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + "s";
        public override string GetSecondWatch() => idleTimer.ToFullString();
        internal string GameplayDisplayName
        {
            get { lock (gate) return SdlpalBridge.ProfileName(activeProfile) + (freshSnapshot && observer.Snapshot?.wuqiang == true ? "（吴强）" : "") +
                (freshSnapshot && observer.Snapshot?.monsterRush == true ? "（突脸）" : "") +
                (freshSnapshot && observer.Snapshot?.invisibleMonsters == true ? "（隐形）" : ""); }
        }
        internal string ReleaseSuffix
        {
            get { lock (gate) return freshSnapshot && !string.IsNullOrEmpty(observer.Snapshot?.releaseRevision) ? " " + observer.Snapshot.releaseRevision : ""; }
        }
        public override string GetGameVersion() => GameplayDisplayName + ReleaseSuffix;
        internal string GetWindowTitle(string integrityTitle, string onlineSuffix)
        {
            lock (gate) return "自动计时器 " + GameplayDisplayName +
                (string.IsNullOrEmpty(integrityTitle) ? "" : " " + integrityTitle) + onlineSuffix + ReleaseSuffix;
        }
        public override string GetMoreInfo() => T(status + (!freshSnapshot || observer.Snapshot == null ? "" : "  场景 " + observer.Snapshot.scene +
            (items.View.Available ? "  金钱 " + items.View.Cash : "  物品数据不可用")));
        public override string GetAAction() => "";
        public override string GetCriticalError() { string value = warning; warning = ""; return value; }
        public override string GetRuntimeIntegrityStatus() => GetScoreValidationError();
        public override void OnFunctionKey(int number)
        {
            if (number == 8) form.UIPause(); // F8 aliases the existing F9 release action for this core.
            else if (number == 11) OpenKeyConfig();
        }
        internal ProcessStartInfo CreateKeyConfigStartInfo()
        {
            lock (gate)
            {
                SdlpalSnapshot snapshot;
                if (bridge == null || !bridge.TryRead(out snapshot))
                    throw new InvalidOperationException("请先启动隔世追凶游戏");
                // gamePath is the resource directory (usually ./PAL), not the executable directory.
                string executable = bridge.Process.MainModule.FileName;
                string directory = Path.GetDirectoryName(executable);
                return new ProcessStartInfo(Pal98.Maintenance.MaintenanceExecutable.FindConfiguration(directory))
                    { WorkingDirectory = directory, UseShellExecute = false,
                      Arguments = "--game-dir " + Pal98.Maintenance.MaintenanceExecutable.Quote(directory) +
                          " --game-exe " + Pal98.Maintenance.MaintenanceExecutable.Quote(executable) };
            }
        }
        internal void OpenKeyConfig()
        {
            try
            {
                var start = CreateKeyConfigStartInfo();
                Process.Start(start);
            }
            catch (Exception ex) { form.Error(ex.Message); }
        }
        private ToolStripMenuItem Menu(string text, Action action, bool cloud = false)
        {
            var item = cloud ? form.NewCloudMenuItem() : form.NewMenuItem();
            item.Text = T(text); item.Tag = text; item.Click += (sender, args) => { try { action(); } catch (Exception ex) { form.Error(ex.Message); } };
            return item;
        }
        public override void InitUI()
        {
            Menu("导出当前时间线", ExportTimeline);
            Menu("导入时间线…", ImportTimeline);
            Menu("设置为最佳", () => SaveBest(GetRStr()));
            Menu("编辑自动节点 / 捕获当前战斗…", EditRules);
            Menu("接力－保存…", SaveRelay);
            Menu("接力－载入…", LoadRelay);
            Menu("按键配置（F11）", OpenKeyConfig);
            Menu("实时物品检测 / 富甲设置…", ShowItems);
            cloudSave = Menu("云存档（隔世追凶）", CloudSave, true); cloudSave.Enabled = false;
            cloudLoad = Menu("云读档（隔世追凶）", CloudLoad, true); cloudLoad.Enabled = false;
            overlayMenu = Menu("OBS 独立遮罩", ToggleOverlay);
            Menu("OBS 调整位置与大小", () => { EnsureOverlay(); if (overlay.IsInEditMode) overlay.EndEditMode(); else overlay.BeginEditMode(); });
            Menu("OBS 字体…", () => { EnsureOverlay(); using (var dialog = new FontDialog { Font = new Font(overlay.ConfiguredFontFamily, overlay.ConfiguredFontSize, overlay.ConfiguredFontStyle) }) if (dialog.ShowDialog(form) == DialogResult.OK) overlay.ApplyFont(dialog.Font.FontFamily.Name, dialog.Font.Size, dialog.Font.Style); });
            Menu("OBS 文字颜色…", () => { EnsureOverlay(); using (var dialog = new ColorDialog { Color = Color.FromArgb(overlay.ConfiguredFontColorArgb) }) if (dialog.ShowDialog(form) == DialogResult.OK) overlay.ApplyFontColor(dialog.Color); });
            Menu("OBS 快捷键…", ConfigureOverlayHotkey);
            Menu("OBS 恢复默认位置", () => { EnsureOverlay(); overlay.ResetLayout(); });
            string enabled = TimerUserSettings.GetPath("sdlpal_geshi_overlay");
            if (File.Exists(enabled) && File.ReadAllText(enabled).Trim() == "1") EnsureOverlay();
        }
        public override void OnCloudOK() { if (cloudSave != null) cloudSave.Enabled = !busyRelay; if (cloudLoad != null) cloudLoad.Enabled = !busyRelay; }
        public override void OnCloudFail() { if (cloudSave != null) cloudSave.Enabled = false; if (cloudLoad != null) cloudLoad.Enabled = false; }
        public override void OnCloudPending() => OnCloudFail();
        private void EnsureOverlay()
        {
            if (overlay != null && !overlay.IsDisposed) return;
            overlay = new Dx9OverlayForm(OverlaySnapshot, Dx9OverlaySettings.LoadLayout(OverlayLayout), layout => Dx9OverlaySettings.SaveLayout(layout, OverlayLayout));
            overlay.Text = GameplayDisplayName + " - OBS 独立遮罩" + ReleaseSuffix;
            overlay.LayoutSaveFailed += error => form.Error("OBS 设置保存失败：" + error.Message);
            overlay.FormClosed += (s, e) => { if (overlayMenu != null) overlayMenu.Checked = false; };
            overlay.Start(); overlayMenu.Checked = true;
        }
        private void ToggleOverlay()
        {
            bool enable = overlay == null || overlay.IsDisposed;
            TimerUserSettings.WriteText("sdlpal_geshi_overlay", enable ? "1" : "0", Encoding.UTF8);
            if (enable) EnsureOverlay(); else { overlay.Stop(); overlay.Dispose(); overlay = null; overlayMenu.Checked = false; }
        }
        private void ConfigureOverlayHotkey()
        {
            var layout = Dx9OverlaySettings.LoadLayout(OverlayLayout);
            using (var dialog = new Dx9OverlayHotkeyForm(layout.ToggleHotkey, key => {
                string error = Dx9OverlaySettings.ValidateToggleHotkey(key, SoundConfig.ins.ToggleHotkey);
                return error.Length != 0 ? error : key != Keys.None && key == form.ObsWindowStyleToggleHotkey ? "该组合已用于主窗口 OBS 模式" : "";
            }))
                if (dialog.ShowDialog(form) == DialogResult.OK)
                { layout.ToggleHotkey = dialog.SelectedHotkey; Dx9OverlaySettings.SaveLayout(layout, OverlayLayout); overlay?.ApplyToggleHotkey(dialog.SelectedHotkey); }
        }
        public override Keys GetCustomToggleHotkey() => overlay?.ConfiguredToggleHotkey ?? Dx9OverlaySettings.LoadLayout(OverlayLayout).ToggleHotkey;
        public override bool TryHandleCustomHotkey(Keys hotkey)
        { if (hotkey == Keys.None || hotkey != GetCustomToggleHotkey()) return false; ToggleOverlay(); return true; }
        internal static string OverlayIdentity(int cloudId, string caption)
        {
            string cloud = cloudId >= 0 ? cloudId.ToString(System.Globalization.CultureInfo.InvariantCulture) : "未激活";
            string state = (caption ?? "").Trim().Trim('[', ']');
            string id = "--";
            foreach (string prefix in new[] { "已连接:", "不完全连接:" })
                if (state.StartsWith(prefix, StringComparison.Ordinal))
                {
                    string value = state.Substring(prefix.Length);
                    if (value.Length > 0 && value.Length <= 18 && value[0] != '0' && value.All(c => c >= '0' && c <= '9'))
                    { id = value; state = prefix.TrimEnd(':'); }
                    else state = "未连接";
                    break;
                }
            if (state.Length == 0) state = "未连接";
            return "云ID:" + cloud + "  连接ID:" + id + "（" + state + "）";
        }
        private Dx9OverlaySnapshot OverlaySnapshot()
        {
            lock (gate)
            {
                if (overlay != null && !overlay.IsDisposed)
                    overlay.Text = GameplayDisplayName + " - OBS 独立遮罩" + ReleaseSuffix;
                var entries = new Dx9OverlayTimelineEntry[2];
                int start = Math.Max(0, Math.Min(CurrentStep - 1, CheckPoints.Count - 2));
                for (int i = 0; i < 2 && start + i < CheckPoints.Count; i++)
                { var p = CheckPoints[start + i]; entries[i] = new Dx9OverlayTimelineEntry(T(p.GetNickName()), TItem.TimeSpanToString(p.Best), TItem.TimeSpanToString(p.Current), p.GetCHA(), p.Index == CurrentStep, p.Status == CheckPointStatus.Completed); }
                IntPtr handle = IntPtr.Zero; try { handle = bridge?.Process?.MainWindowHandle ?? IntPtr.Zero; } catch { }
                string plugin = string.Join("  ", new[] { GetPluginResult(TimerPluginBase.TimerPlugin.EPluginPosition.BR),
                    GetPluginResult(TimerPluginBase.TimerPlugin.EPluginPosition.BL) }.Where(text => !string.IsNullOrEmpty(text)));
                return new Dx9OverlaySnapshot(handle, overlay?.ConfiguredFontFamily, TItem.TimeSpanToString(GetMainWatch()), GetSmallWatch(), GetSecondWatch(),
                    GetMoreInfo(), form.ManualPauseCount, entries[0], entries[1], new Dx9OverlayTimelineEntry(), T(status), false, !MT.IsRunning,
                    GetGameVersion(), "", "", T(GetScoreValidationError()), form.CloudID(), plugin,
                    OverlayIdentity(form.CloudID(), form.CompetitionOverlayCaption));
            }
        }
        public override void UnloadUI() { if (itemsForm != null) { itemsForm.Dispose(); itemsForm = null; } if (overlay != null) { overlay.Stop(); overlay.Dispose(); overlay = null; } base.UnloadUI(); }
        public override void Unload() { lock (gate) { unloaded = true; base.Unload(); bridge?.Dispose(); bridge = null; MT.Stop(); battleTimer.Stop(); idleTimer.Stop(); } }

        private sealed class ImportedTimer
        {
            internal TimeSpan Current, Idle;
            internal int Step;
            internal TimeSpan[] Times;
            internal CheckPointStatus[] Statuses;
            internal bool WuQiangChanged;
            internal bool BestConfigurationChanged;
        }
        private ImportedTimer ValidateImport(string json, string identity)
        {
            if (json == null || json.Length > 262144) throw new InvalidDataException("时间线容量无效");
            var data = new HObj(json);
            if (data.GetValue<string>("SdlpalRoute") != RouteIdentity || data.GetValue<string>("SdlpalProfile") != activeProfile)
                throw new InvalidDataException("时间线不属于当前内容节点规则");
            // Speed changes share a PB category and may also occur in a relay;
            // engine/resource/route identities still have to match.
            if (data.GetValue<string>("TimerCore") != CoreName || !SdlpalSnapshot.SameResourceEngineIdentity(data.GetValue<string>("SdlpalIdentity"), identity))
                throw new InvalidDataException("时间线资源或引擎身份不匹配");
            object changed;
            bool hasChanged = data.ToDic().TryGetValue("SdlpalWuQiangChanged", out changed);
            if (hasChanged && !(changed is bool)) throw new InvalidDataException("时间线无墙模式变更记录无效");
            object configurationChanged;
            bool hasConfigurationChanged = data.ToDic().TryGetValue("SdlpalBestConfigurationChanged", out configurationChanged);
            if (hasConfigurationChanged && !(configurationChanged is bool)) throw new InvalidDataException("时间线配置变更记录无效");
            var points = data.GetValue<HObj>("CheckPoints")?.ToList(); var states = data.GetValue<HObj>("SdlpalStatuses")?.ToList();
            var result = new ImportedTimer { Current = ParseTime(data.GetValue<string>("Current")), Idle = ParseTime(data.GetValue<string>("SdlpalIdle")),
                Step = data.GetValue<int>("Step"), Times = new TimeSpan[rules.Length], Statuses = new CheckPointStatus[rules.Length],
                WuQiangChanged = hasChanged && (bool)changed,
                BestConfigurationChanged = hasConfigurationChanged && (bool)configurationChanged };
            if (points == null || states == null || points.Count != rules.Length || states.Count != rules.Length || result.Step < -1 || result.Step > rules.Length) throw new InvalidDataException("时间线节点数量或进度无效");
            TimeSpan last = TimeSpan.Zero;
            for (int i = 0; i < rules.Length; i++)
            {
                var point = points[i] as HObj;
                CheckPointStatus state;
                if (point == null || point.GetValue<string>("name") != rules[i].Id || !Enum.TryParse(Convert.ToString(states[i]), out state) || !Enum.IsDefined(typeof(CheckPointStatus), state)) throw new InvalidDataException("时间线节点身份无效");
                result.Times[i] = ParseTime(point.GetValue<string>("time")); result.Statuses[i] = state;
                if (result.Times[i] > result.Current || i < result.Step && (state == CheckPointStatus.NotStarted || state == CheckPointStatus.InProgress) ||
                    i == result.Step && state != CheckPointStatus.InProgress || i > result.Step && result.Step >= 0 && state != CheckPointStatus.NotStarted)
                    throw new InvalidDataException("时间线时间或状态与进度不一致");
                if (state == CheckPointStatus.Completed) { if (result.Times[i] < last) throw new InvalidDataException("节点时间不递增"); last = result.Times[i]; }
            }
            return result;
        }
        private static TimeSpan ParseTime(string text)
        {
            TimeSpan result;
            if (!TimeSpan.TryParseExact(text, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out result) || result < TimeSpan.Zero || result.TotalDays >= 1)
                throw new InvalidDataException("时间必须为 HH:mm:ss.fff，且少于24小时");
            return result;
        }
        private void Restore(ImportedTimer timer, SdlpalSnapshot snapshot)
        {
            lock (gate)
            {
                MT.Stop(); MT.SetTS(timer.Current); idleTimer.Stop(); idleTimer.SetTS(timer.Idle); battleTimer.Reset();
                AdvanceScoreRunSequence(imported: true); observer.MarkImported(snapshot, timer.WuQiangChanged, timer.BestConfigurationChanged); freshSnapshot = true;
                // Jump derives the interval and prediction from checkpoint times.
                for (int i = 0; i < rules.Length; i++) CheckPoints[i].SetCurrentTSForLoad(timer.Times[i]);
                Jump(timer.Step);
                items.Reset(); items.Observe(snapshot); PublishItemPlugins();
                for (int i = 0; i < rules.Length; i++)
                {
                    // Replace Jump's manual-skip status in both data and renderer.
                    CheckPoints[i].Status = timer.Statuses[i];
                    form?.rr?.UpdateItemStatus(i, timer.Statuses[i]);
                }
                PauseForLoad();
            }
        }
        private SdlpalSnapshot RequireSnapshot()
        {
            SdlpalSnapshot snapshot;
            if (bridge == null || !bridge.TryRead(out snapshot)) throw new InvalidOperationException("请先连接正在运行的隔世追凶游戏");
            return snapshot;
        }
        private void ImportTimeline()
        {
            using (var dialog = new OpenFileDialog { Filter = "PalTimer 时间线|*.txt;*.json" })
                if (dialog.ShowDialog(form) == DialogResult.OK)
                { var snapshot = RequireSnapshot(); var timer = ValidateImport(SdlpalRelayPackage.ReadBounded(dialog.FileName), snapshot.IdentityKey); Restore(timer, snapshot); }
        }
        private void ExportTimeline()
        {
            using (var dialog = new SaveFileDialog { Filter = "PalTimer 时间线|*.json", FileName = "隔世追凶-时间线.json" })
                if (dialog.ShowDialog(form) == DialogResult.OK)
                    SdlpalRelayPackage.AtomicWrite(dialog.FileName, new UTF8Encoding(false).GetBytes(GetRStr()));
        }
        private async Task RunRelay(Func<SdlpalSnapshot, Task> operation)
        {
            if (busyRelay) throw new InvalidOperationException("接力操作尚未完成");
            var snapshot = RequireSnapshot(); PauseForLoad(); busyRelay = true; relayRun = ScoreRunSequence; relayBridge = bridge; OnCloudFail();
            try { await operation(snapshot); }
            finally { busyRelay = false; relayBridge = null; if (!unloaded && form.CloudID() >= 0) OnCloudOK(); }
        }
        private void CheckRelayOwner()
        {
            if (unloaded || ScoreRunSequence != relayRun || !ReferenceEquals(bridge, relayBridge))
                throw new OperationCanceledException("计时器已重置、切换或重新连接，接力操作取消；请核对游戏状态");
        }
        private async Task SavePackage(string path, SdlpalSnapshot snapshot)
        {
            string timer; lock (gate) { CheckRelayOwner(); timer = GetRStr(); }
            var saved = await Task.Run(() => SdlpalControl.Execute(relayBridge, 1, snapshot));
            CheckRelayOwner();
            var package = new SdlpalRelayPackage { identity = snapshot.IdentityKey, timer = timer, saveBase64 = Convert.ToBase64String(saved.SavedBytes), saveSha256 = SdlpalRelayPackage.Hash(saved.SavedBytes) };
            await Task.Run(() => SdlpalRelayPackage.AtomicWrite(path, new UTF8Encoding(false).GetBytes(SdlpalBridge.Json().Serialize(package))));
            CheckRelayOwner();
        }
        private async Task LoadPackage(string path, SdlpalSnapshot snapshot)
        {
            CheckRelayOwner();
            var package = SdlpalBridge.Json().Deserialize<SdlpalRelayPackage>(SdlpalRelayPackage.ReadBounded(path));
            if (package == null) throw new InvalidDataException("接力包为空");
            byte[] bytes = package.Validate(snapshot.IdentityKey); var timer = ValidateImport(package.timer, snapshot.IdentityKey);
            var loaded = await Task.Run(() => SdlpalControl.Execute(relayBridge, 2, snapshot, bytes));
            CheckRelayOwner();
            Restore(timer, loaded.Snapshot);
        }
        private async void SaveRelay()
        {
            using (var dialog = new SaveFileDialog { Filter = "SDLPAL 接力包|*.sdlrelay", FileName = "隔世追凶-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".sdlrelay" })
                if (dialog.ShowDialog(form) == DialogResult.OK)
                    try { await RunRelay(snapshot => SavePackage(dialog.FileName, snapshot)); form.Success("接力包已保存；F9 继续计时"); } catch (Exception ex) { form.Error(ex.Message); }
        }
        private async void LoadRelay()
        {
            using (var dialog = new OpenFileDialog { Filter = "SDLPAL 接力包|*.sdlrelay;*.bin" })
                if (dialog.ShowDialog(form) == DialogResult.OK)
                    try { await RunRelay(snapshot => LoadPackage(dialog.FileName, snapshot)); form.Success("游戏和计时已载入；F9 继续计时"); } catch (Exception ex) { form.Error(ex.Message); }
        }
        private async void CloudSave()
        {
            string code = "geshi-" + form.CloudID() + "-" + Guid.NewGuid().ToString("N");
            string path = Path.Combine(TimerUserSettings.Store.DirectoryPath, code + ".bin");
            try
            {
                await RunRelay(async snapshot => { await SavePackage(path, snapshot); CheckRelayOwner(); await Task.Run(() => form.OUpload(path, code + ".bin")); CheckRelayOwner(); });
                form.Success("云存档编码（可复制）：\r\n" + code + "\r\nF9 继续计时");
            }
            catch (Exception ex) { form.Error(ex.Message); }
            finally { TryDeleteTemporary(path); }
        }
        private void CloudLoad()
        {
            var dialog = new Download(async (code, window) => {
                string path = Path.Combine(TimerUserSettings.Store.DirectoryPath, "geshi-download-" + Guid.NewGuid().ToString("N") + ".bin");
                try
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(code, "\\Ageshi-[0-9]+-[a-f0-9]{32}\\z")) throw new InvalidDataException("这不是隔世追凶云存档编码");
                    await RunRelay(async snapshot => { await Task.Run(() => form.ODownload(code + ".bin", path)); await LoadPackage(path, snapshot); });
                    window.Close(); form.Success("云存档已载入；F9 继续计时");
                }
                catch (Exception ex) { window.Error(ex.Message); window.txtCode.Enabled = window.btnOK.Enabled = true; }
                finally { TryDeleteTemporary(path); }
            });
            dialog.Show(form);
        }
        private static void TryDeleteTemporary(string path)
        { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        private void EditRules()
        {
            string profile; SdlpalSplitRule[] initialRules; SdlpalSnapshot snapshot;
            lock (gate) { profile = activeProfile; initialRules = rules; snapshot = observer.Snapshot; }
            using (var dialog = new SdlpalRulesForm(initialRules, snapshot))
                if (dialog.ShowDialog(form) == DialogResult.OK && form.Confirm("保存新节点并重置计时器？"))
                {
                    try { lock (gate) { SaveProfileRules(dialog.Rules, profile); form._ResetAll(); } }
                    catch (Exception ex) { form.Error(ex.Message); }
                }
        }
        internal void SaveProfileRules(SdlpalSplitRule[] value, string profile)
        {
            lock (gate) {
                if (unloaded || profile != activeProfile) throw new InvalidOperationException("游戏内容已切换，请重新打开节点编辑器；旧节点未保存。");
                string json = SdlpalBridge.Json().Serialize(value); string path = TimerUserSettings.GetPath(RulesFile);
                if (File.Exists(path)) File.Copy(path, path + ".backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
                TimerUserSettings.WriteText(RulesFile, json, new UTF8Encoding(false));
            }
        }
    }

    internal sealed class SdlpalRulesForm : Form
    {
        internal SdlpalSplitRule[] Rules;
        private readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true };
        internal SdlpalRulesForm(SdlpalSplitRule[] rules, SdlpalSnapshot snapshot)
        {
            Text = "SDLPAL 自动节点（当前内容，按表格顺序）"; Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi; Width = 1040; Height = 560; MinimumSize = new Size(850, 420); StartPosition = FormStartPosition.CenterParent;
            var help = new Label { Text = "battle 匹配胜利；position 使用视口坐标；any-location 满足任一入口；water-pearl 为水灵珠交换或返回大理；最后为 completion。每种内容独立保存。", Dock = DockStyle.Top, Height = 46, Padding = new Padding(8) };
            string[] names = { "Id", "Name", "Kind", "Team", "Scene", "Enemy", "X", "Y", "Radius", "Music", "Item", "Scenes", "Alternatives", "ReturnScene" }, captions = { "稳定 ID", "显示名称", "规则类型", "敌队 ID", "场景 ID", "敌人物件 ID", "视口 X", "视口 Y", "范围格数", "音乐 ID", "物品 ID", "场景集合", "可选入口 JSON", "返回场景" };
            for (int i = 0; i < names.Length; i++) grid.Columns.Add(names[i], captions[i]);
            grid.Columns[0].FillWeight = 190; grid.Columns[1].FillWeight = 120; grid.RowTemplate.Height = 28;
            foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            foreach (var rule in rules) grid.Rows.Add(rule.Id, rule.Name, rule.Kind, rule.Team, rule.Scene, rule.Enemy, rule.X, rule.Y, rule.Radius, rule.Music, rule.Item, rule.Scenes == null ? "" : string.Join(",", rule.Scenes),
                rule.Alternatives == null ? "" : SdlpalBridge.Json().Serialize(rule.Alternatives), rule.ReturnScene);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, Padding = new Padding(5) };
            var capture = new Button { Text = "捕获战斗", AutoSize = true };
            capture.Click += (s, e) => {
                var last = snapshot?.battleEvents.LastOrDefault();
                int? team = snapshot?.battle.active == true ? snapshot.battle.team : last?.team;
                if (!team.HasValue) { MessageBox.Show(this, "尚未观察到战斗"); return; }
                int index = Math.Max(0, grid.Rows.Count - 2);
                grid.Rows.Insert(index, "battle-" + team + "-" + Guid.NewGuid().ToString("N").Substring(0, 6), "战斗 " + team, "battle", team, null, null);
            };
            bar.Controls.Add(capture);
            foreach (int direction in new[] { -1, 1 })
            {
                int offset = direction; var move = new Button { Text = direction < 0 ? "上移" : "下移", AutoSize = true };
                move.Click += (s, e) => { int i = grid.CurrentCell?.RowIndex ?? -1, j = i + offset; if (i < 0 || j < 0 || i >= grid.Rows.Count - 1 || j >= grid.Rows.Count - 1) return;
                    object[] values = grid.Rows[i].Cells.Cast<DataGridViewCell>().Select(c => c.Value).ToArray(); grid.Rows.RemoveAt(i); grid.Rows.Insert(j, values); grid.CurrentCell = grid.Rows[j].Cells[0]; };
                bar.Controls.Add(move);
            }
            var save = new Button { Text = "验证并保存", AutoSize = true }; bar.Controls.Add(save);
            save.Click += (s, e) => {
                try
                {
                    grid.EndEdit(); var result = new List<SdlpalSplitRule>();
                    foreach (DataGridViewRow row in grid.Rows) if (!row.IsNewRow)
                        result.Add(new SdlpalSplitRule { Id = Convert.ToString(row.Cells[0].Value).Trim(), Name = Convert.ToString(row.Cells[1].Value).Trim(), Kind = Convert.ToString(row.Cells[2].Value).Trim(),
                            Team = Number(row.Cells[3].Value), Scene = Number(row.Cells[4].Value), Enemy = Number(row.Cells[5].Value), X = Number(row.Cells[6].Value), Y = Number(row.Cells[7].Value), Radius = Number(row.Cells[8].Value), Music = Number(row.Cells[9].Value), Item = Number(row.Cells[10].Value), Scenes = Numbers(row.Cells[11].Value),
                            Alternatives = Locations(row.Cells[12].Value), ReturnScene = Number(row.Cells[13].Value) });
                    Rules = result.ToArray(); SdlpalSplitRule.Validate(Rules); DialogResult = DialogResult.OK;
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "节点无效", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            Controls.Add(grid); Controls.Add(help); Controls.Add(bar);
        }
        private static int[] Numbers(object value) { string text = Convert.ToString(value).Trim(); return text.Length == 0 ? null : text.Split(',').Select(v => int.Parse(v.Trim(), CultureInfo.InvariantCulture)).ToArray(); }
        private static SdlpalLocationRule[] Locations(object value) { string text = Convert.ToString(value).Trim(); return text.Length == 0 ? null : SdlpalBridge.Json().Deserialize<SdlpalLocationRule[]>(text); }
        private static int? Number(object value) { string text = Convert.ToString(value).Trim(); return text.Length == 0 ? (int?)null : int.Parse(text, CultureInfo.InvariantCulture); }
    }
}
