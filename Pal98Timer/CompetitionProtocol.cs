using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Pal98Timer
{
    // PalServer PR #11, PAL98.TimerCompetition.v1. Never called to determine local timing.
    internal static class CompetitionProtocol
    {
        internal const string Name = "PAL98.TimerCompetition.v1";
        internal const string NameV2 = "PAL98.TimerCompetition.v2";
        internal const string Online = "PAL98.TimerOnline.v1";
        internal const string Scope = "timer-online";
        internal static string SerializeRun(CompetitionRun run) {
            var serializer = Json();
            var fields = serializer.Deserialize<Dictionary<string, object>>(serializer.Serialize(run));
            if (run.protocol == Name) { fields.Remove("timeline_id"); fields.Remove("gameplay"); fields.Remove("hardcore"); }
            if (run.protocol == Online) { fields.Remove("track_id"); fields.Remove("ruleset_id"); }
            else { fields.Remove("ranking"); fields.Remove("custom_competition_id"); }
            return run.protocol == Online ? CanonicalJson(fields) : serializer.Serialize(fields);
        }
        // Preserve a deterministic body across the legacy serializer's different
        // dictionary/property traversal orders. The sealed original bytes are
        // retained; this is only the producer encoding and local equality check.
        private static string CanonicalJson(object value)
        {
            if (value is IDictionary<string, object> fields)
                return "{" + string.Join(",", fields.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => GameplayModeReader.Quote(p.Key) + ":" + CanonicalJson(p.Value))) + "}";
            if (value is System.Collections.IEnumerable array && !(value is string))
                return "[" + string.Join(",", array.Cast<object>().Select(CanonicalJson)) + "]";
            return Json().Serialize(value);
        }
        internal const long MaxMilliseconds = 86400000;
        internal static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 1048576, RecursionLimit = 32 }; }
        internal static string Hash(string text)
        {
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(new UTF8Encoding(false).GetBytes(text)));
        }
        internal static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
        internal static bool Digest(string value) { return value != null && Regex.IsMatch(value, "^[0-9a-f]{64}$"); }
        internal static bool Identifier(string value) { return value != null && Regex.IsMatch(value, @"^[^\s\x00-\x1f\x7f]{1,80}$"); }
        internal static string CheckpointId(string name)
        {
            // Keep the ordinary PAL98 names byte-for-byte. Unusual names have an
            // explicit injective namespace; display nicknames never enter identity.
            if (Identifier(name) && !name.StartsWith("id-sha256-", StringComparison.Ordinal)) return name;
            return "id-sha256-" + Hash(name ?? "");
        }
        internal static string RouteHash(IEnumerable<string> ids) { return Hash("PAL98.TimerRoute.v1\n" + string.Join("\n", ids) + "\n"); }
        internal static string TrackFor(int fade, int speed)
        { return fade == 1200 && speed == 10 ? "wuqiang" : fade == 800 && speed == 10 ? "wuqiang-800" : fade == 800 && speed == 9 ? "wuqiang-800-speed" : null; }
        internal static string TrackLabel(string track)
        { return track == "wuqiang" ? "1.2秒＋普通走速" : track == "wuqiang-800" ? "0.8秒＋普通走速" : track == "wuqiang-800-speed" ? "0.8秒＋快走速" : "等待游戏速度"; }
        internal static string Validate(CompetitionRun run)
        {
            Guid id; Version version;
            if (run == null || (run.protocol != Name && run.protocol != NameV2 && run.protocol != Online) || !Guid.TryParse(run.run_id, out id) ||
                run.hwid == null || !Regex.IsMatch(run.hwid, "^[A-Za-z0-9._:-]{8,128}$") ||
                (run.protocol != Online && (!Identifier(run.track_id) || !Identifier(run.ruleset_id))) || !Digest(run.pal_dll_sha256) ||
                !Version.TryParse(run.timer_version, out version) || version.Revision < 0 ||
                !Version.TryParse(run.game_version, out version) || version.Revision < 0 ||
                run.total_ms <= 0 || run.total_ms > MaxMilliseconds) return "成绩身份或总时间不完整，已保留本地记录。";
            if ((run.protocol == NameV2 || run.protocol == Online) && (run.gameplay == null || !run.gameplay.Valid || run.hardcore == null ||
                !run.hardcore.Valid || (run.protocol != Online && run.track_id != TrackFor(run.gameplay.fade_ms, run.gameplay.map_speed_ticks)) ||
                run.timeline_id != TimelineIdentity.Create(run.gameplay.rules_sha256, run.route_sha256, run.hardcore.requested)))
                return "玩法或硬核证据不完整，已保留本地记录。";
            if (run.protocol == Online && (run.ranking == null || !run.ranking.Valid(run.gameplay) ||
                run.custom_competition_id != null && !Regex.IsMatch(run.custom_competition_id, "\\A[a-z0-9-]{1,60}\\z")))
                return "联机配置身份不完整，仅保留本地记录。";
            DateTimeOffset start, finish;
            if (!DateTimeOffset.TryParse(run.started_at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out start) ||
                !DateTimeOffset.TryParse(run.finished_at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out finish) || finish < start)
                return "成绩起止时间不完整，已保留本地记录。";
            if (run.splits == null || run.splits.Length < 1 || run.splits.Length > 256 || run.splits.Any(s => s == null || !Identifier(s.checkpoint_id)) ||
                run.splits.Select(s => s.checkpoint_id).Distinct(StringComparer.Ordinal).Count() != run.splits.Length ||
                run.route_sha256 != RouteHash(run.splits.Select(s => s.checkpoint_id))) return "节点身份或路线不完整，已保留本地记录。";
            long last = -1;
            foreach (var split in run.splits)
            {
                if (split.status == "completed")
                {
                    if (!split.elapsed_ms.HasValue || split.elapsed_ms.Value < 0 || split.elapsed_ms.Value < last || split.elapsed_ms.Value > MaxMilliseconds) return "节点时间不递增，未上传。";
                    last = split.elapsed_ms.Value;
                }
                else if ((split.status != "auto_skipped" && split.status != "manual_skipped") || split.elapsed_ms.HasValue) return "节点状态未完成，未上传。";
            }
            if (run.splits[run.splits.Length - 1].status != "completed") return "尚未通关，未上传。";
            return Encoding.UTF8.GetByteCount(SerializeRun(run)) > 65536 ? "成绩超过服务器容量，已保留本地记录。" : "";
        }
    }

    internal sealed class CompetitionSettings
    {
        public bool Enabled { get; set; }
        public bool Overlay { get; set; }
        public bool Transparent { get; set; }
        public string OverlayFont { get; set; } = "Microsoft YaHei UI";
        public float OverlayFontSize { get; set; } = 12F;
        public string OverlayColor { get; set; } = "#FFFFFF";
        public string OverlayAlignment { get; set; } = "left";
        public string ReferenceBoard { get; set; } = "overall";
        public string ReferenceScope { get; set; } = "automatic";
        public string CustomCompetitionId { get; set; }
        public string ConfigurationId { get; set; }
        public bool OverlayEditable { get; set; } = true;
        public int OverlayWidth { get; set; } = 555;
        public int OverlayHeight { get; set; } = 330;
        public int? OverlayLeft { get; set; }
        public int? OverlayTop { get; set; }
        public string Server { get; set; } = "https://www.pallab.top";
        public string Event { get; set; } = "wuqiang";
        public string Track { get; set; } = "wuqiang";
        public string Ruleset { get; set; } = "wuqiang-2026-v1";
        public int FadeMilliseconds { get; set; } = 800;
        public int MapSpeedTicks { get; set; } = 9;
        internal CompetitionSettings Copy() { return (CompetitionSettings)MemberwiseClone(); }
        internal void CopyAppearance(CompetitionSettings source)
        {
            Overlay = source.Overlay; Transparent = source.Transparent; OverlayFont = source.OverlayFont;
            OverlayFontSize = source.OverlayFontSize; OverlayColor = source.OverlayColor; OverlayAlignment = source.OverlayAlignment;
            OverlayEditable = source.OverlayEditable; OverlayWidth = source.OverlayWidth; OverlayHeight = source.OverlayHeight;
            OverlayLeft = source.OverlayLeft; OverlayTop = source.OverlayTop; ReferenceBoard = source.ReferenceBoard; ReferenceScope = source.ReferenceScope;
        }
        internal string ValidateAppearance()
        {
            if ((ReferenceScope != "automatic" && ReferenceScope != "daily") || (ReferenceBoard != "overall" && ReferenceBoard != "hardcore") || string.IsNullOrWhiteSpace(OverlayFont) || OverlayFont.Length > 128 || OverlayFont.Any(char.IsControl) ||
                float.IsNaN(OverlayFontSize) || float.IsInfinity(OverlayFontSize) || OverlayFontSize < 8 || OverlayFontSize > 72 ||
                !Regex.IsMatch(OverlayColor ?? "", "\\A#[0-9a-fA-F]{6}\\z") ||
                (OverlayAlignment != "left" && OverlayAlignment != "center" && OverlayAlignment != "right") ||
                OverlayWidth < 320 || OverlayWidth > 3840 || OverlayHeight < 140 || OverlayHeight > 2160 ||
                OverlayLeft.HasValue != OverlayTop.HasValue || (OverlayLeft.HasValue && (Math.Abs((long)OverlayLeft.Value) > 100000 || Math.Abs((long)OverlayTop.Value) > 100000)))
                return "排名窗口设置无效：字号允许 8–72，颜色为 #RRGGBB。";
            return "";
        }
        internal string Validate()
        {
            Uri uri;
            if (!Uri.TryCreate(Server, UriKind.Absolute, out uri) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/" ||
                (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))) return "服务器须为 HTTPS 域名；仅本机测试允许 HTTP。";
            if (CustomCompetitionId != null && !Regex.IsMatch(CustomCompetitionId, "\\A[a-z0-9-]{1,60}\\z")) return "自定义比赛ID无效。";
            if (FadeMilliseconds != 800 && FadeMilliseconds != 1200 || MapSpeedTicks != 9 && MapSpeedTicks != 10) return "比赛速度设置无效。";
            Server = uri.GetLeftPart(UriPartial.Authority);
            return "";
        }
        internal string Key { get { return Server + "|" + CustomCompetitionId + "|" + ConfigurationId + "|" + FadeMilliseconds + "|" + MapSpeedTicks; } }
        internal string Endpoint { get { return Server.TrimEnd('/') + "/api/v1/timer"; } }
    }

    internal sealed class CompetitionSplit
    {
        public string checkpoint_id { get; set; }
        public long? elapsed_ms { get; set; }
        public string status { get; set; }
    }
    internal sealed class CompetitionRun
    {
        public string protocol { get; set; } = CompetitionProtocol.Online;
        public string run_id { get; set; }
        public string hwid { get; set; }
        public string track_id { get; set; }
        public string ruleset_id { get; set; }
        public string route_sha256 { get; set; }
        public string started_at { get; set; }
        public string finished_at { get; set; }
        public long total_ms { get; set; }
        public string timer_version { get; set; }
        public string game_version { get; set; }
        public string pal_dll_sha256 { get; set; }
        public CompetitionSplit[] splits { get; set; }
        public string timeline_id { get; set; }
        public GameplayIdentity gameplay { get; set; }
        public CompetitionHardcore hardcore { get; set; }
        public RankingConfiguration ranking { get; set; }
        public string custom_competition_id { get; set; }
    }
    // Immutable after publication; only scalar copies/arrays of values cross the timing boundary.
    internal sealed class CompetitionObservation
    {
        internal string Token, Core, DllHash, GameVersion, ValidationError, TimelineId;
        internal GameplayIdentity Gameplay;
        internal RankingConfiguration Ranking;
        internal CompetitionHardcore Hardcore;
        internal int Step, FadeMilliseconds, MapSpeedTicks;
        internal long TotalMilliseconds;
        internal DateTimeOffset ObservedAt;
        internal bool Finished, BeganHere;
        internal CompetitionSplit[] Splits;
        internal CompetitionRunContext Context;
    }
    internal sealed class CompetitionRunContext
    {
        internal string Token, RunId;
        internal CompetitionSettings Settings;
        internal DateTimeOffset StartedAt, FinishedAt;
        internal bool BeganHere, Started;
        internal int Fade, Speed;
        internal string TimelineId, ConfigurationId;
        internal CompetitionRunContext Copy() { return (CompetitionRunContext)MemberwiseClone(); }
    }
    internal sealed class CompetitionPlayer { public string id { get; set; } public string display_name { get; set; } public bool bound { get; set; } }
    internal sealed class CompetitionComparison { public int? rank { get; set; } public int comparison_players { get; set; } public long? own_reference_ms { get; set; } public long? delta_ms { get; set; } }
    internal sealed class CompetitionNode { public string checkpoint_id { get; set; } public long? elapsed_ms { get; set; } public CompetitionComparison best_complete_line { get; set; } public CompetitionComparison personal_checkpoint_best { get; set; } }
    internal sealed class CompetitionBest { public string run_id { get; set; } public long total_ms { get; set; } public int? rank { get; set; } public string phase { get; set; } public bool ranked { get; set; } }
    internal sealed class CompetitionOverall { public CompetitionBest personal_best { get; set; } public CompetitionBest submitted_run { get; set; } }
    internal sealed class CompetitionReceipt {
        public long id { get; set; }
        public string run_id { get; set; }
        public string phase { get; set; }
        public string received_at { get; set; }
        public bool replayed { get; set; }
        public bool complete_line { get; set; }
        public string daily_status { get; set; }
        public string custom_status { get; set; }
        public string custom_reason { get; set; }
        public int? custom_rules_revision { get; set; }
    }
    internal sealed class CompetitionHardcoreRank { public int rank { get; set; } public string display_name { get; set; } public long total_ms { get; set; } }
    internal sealed class CompetitionReply
    {
        public string protocol { get; set; }
        public string @event { get; set; }
        public string scope { get; set; }
        public string configuration_id { get; set; }
        public string custom_competition_id { get; set; }
        public string title { get; set; }
        public bool published { get; set; }
        public string event_title { get; set; }
        public string board { get; set; }
        public string[] supported_run_protocols { get; set; }
        public CompetitionHardcoreRank[] hardcore_top { get; set; }
        public string track_id { get; set; }
        public string ruleset_id { get; set; }
        public string route_sha256 { get; set; }
        public string phase { get; set; }
        public string as_of { get; set; }
        public bool reference_only { get; set; }
        public bool includes_warmup { get; set; }
        public CompetitionPlayer player { get; set; }
        public CompetitionNode node { get; set; }
        public CompetitionOverall overall { get; set; }
        public CompetitionReceipt receipt { get; set; }
    }
    internal sealed class CompetitionView
    {
        internal string Status = "比赛联机未开启", Hwid = "", NodeName = "";
        internal bool Stale;
        internal int Pending;
        internal CompetitionReply Reply;
        internal DateTimeOffset ReceivedAt;
    }
}
