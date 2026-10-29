// Shared wire contract for the timer-owned bridge and the maintenance config UI.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Pal98.Challenges
{
    internal sealed class ChallengeCoverLine
    {
        public int x { get; set; }
        public int y { get; set; }
        public int font_size { get; set; }
        public string color { get; set; }
        internal void Validate()
        {
            if (x < 0 || x > 319 || y < 0 || y > 199 || font_size < 8 || font_size > 32 ||
                !Regex.IsMatch(color ?? "", @"\A#[0-9a-fA-F]{6}\z")) throw new InvalidDataException("封面坐标、字号或颜色无效。");
        }
    }
    internal sealed class ChallengeCover
    {
        public int schema_version { get; set; }
        public string background_id { get; set; }
        public string planner { get; set; }
        public string line3 { get; set; }
        public string line4 { get; set; }
        public ChallengeCoverLine[] lines { get; set; }
        internal const string Footer = "版本制作人： othercat";
        internal void Validate()
        {
            if (schema_version != 1 || background_id == null || lines == null || lines.Length != 5 || lines.Any(l => l == null))
                throw new InvalidDataException("挑战封面格式不完整。");
            if (background_id.Length != 0 && (!Regex.IsMatch(background_id, @"\A[A-Za-z0-9][A-Za-z0-9_-]{0,63}\z") ||
                Regex.IsMatch(background_id, @"\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])\z", RegexOptions.IgnoreCase)))
                throw new InvalidDataException("封面图片ID无效。");
            ChallengeContract.Text(planner, 60);
            foreach (string text in new[] {line3, line4}) {
                if (text == null || text.Length > 80) throw new InvalidDataException("封面文字无效。");
                if (text.Length != 0) ChallengeContract.Text("X" + text, 81);
            }
            if (new[] {planner, line3, line4}.Any(s => s.Contains('\u2028') || s.Contains('\u2029'))) throw new InvalidDataException("封面文字不能换行。");
            foreach (var line in lines) line.Validate();
        }
        internal string[] Texts(string title, string configuration = "0000", string hardware = "0000") =>
            new[] {title, "策划人：" + planner, line3, line4, Footer + " " + configuration + " " + hardware};
    }
    internal sealed class ChallengeRules
    {
        public int schema_version { get; set; } = 1;
        public bool solo_art { get; set; }
        public bool helpless_husband { get; set; }
        public bool true_duel { get; set; }
        public bool level_one_solo { get; set; }
        public bool individual_flee { get; set; }
        public bool chosen_hero { get; set; }
        public string hero { get; set; } = "li-xiaoyao";
        internal void Validate() { if (schema_version != 1 || !ChallengeContract.HeroIds.Contains(hero)) throw new InvalidDataException("牢局规则版本或人物无效。"); }
        internal string Ini()
        {
            Validate();
            bool[] flags = {solo_art, helpless_husband, true_duel, level_one_solo, individual_flee, chosen_hero};
            var text = new StringBuilder("[Challenge]\nSchemaVersion=1\n");
            for (int i = 0; i < flags.Length; ++i) text.Append(ChallengeContract.IniKeys[i]).Append('=').Append(flags[i] ? '1' : '0').Append('\n');
            return text.Append("Hero=").Append(hero).Append('\n').ToString();
        }
    }
    internal sealed class ChallengeItem
    {
        public string id { get; set; }
        public string author_id { get; set; }
        public string author_name { get; set; }
        public string title { get; set; }
        public int revision { get; set; }
        public int latest_revision { get; set; }
        public string[] contents { get; set; }
        public ChallengeRules rules { get; set; }
        public ChallengeCover cover { get; set; }
        internal void Validate()
        {
            ChallengeContract.Id(id); ChallengeContract.Id(author_id); ChallengeContract.Text(title, 80); ChallengeContract.Text(author_name, 60);
            if (revision < 1 || latest_revision < revision || contents == null || contents.Length < 1 || contents.Length > 32 || contents.Distinct().Count() != contents.Length || rules == null)
                throw new InvalidDataException("挑战内容或规则不完整。");
            foreach (string content in contents) ChallengeContract.ContentId(content);
            rules.Validate();
            cover?.Validate();
        }
        public override string ToString() => title;
    }
    internal sealed class ChallengePlayer
    {
        public bool bound { get; set; }
        public string id { get; set; }
        public string display_name { get; set; }
        internal void Validate()
        {
            if (bound) { ChallengeContract.Id(id); ChallengeContract.Text(display_name, 60); }
            else if (!string.IsNullOrEmpty(id) || !string.IsNullOrEmpty(display_name)) throw new InvalidDataException("未绑定身份字段不一致。");
        }
    }
    internal sealed class ChallengeRuleInfo
    {
        public string key { get; set; }
        public string name { get; set; }
        public string description { get; set; }
    }
    internal sealed class ChallengeState
    {
        public string protocol { get; set; } = ChallengeContract.CacheProtocol;
        public string server_origin { get; set; }
        public string updated_at_utc { get; set; }
        public string next_identity_refresh_utc { get; set; }
        public string identity_status { get; set; } = "unbound";
        public string requested_name { get; set; }
        public ChallengePlayer player { get; set; } = new ChallengePlayer();
        public ChallengeItem[] items { get; set; } = new ChallengeItem[0];
        public ChallengeItem[] enrollments { get; set; } = new ChallengeItem[0];
        public Dictionary<string, string> contents { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> heroes { get; set; } = new Dictionary<string, string>();
        public ChallengeRuleInfo[] rules { get; set; } = new ChallengeRuleInfo[0];
        internal void Validate()
        {
            if (protocol != ChallengeContract.CacheProtocol || items == null || enrollments == null || items.Length > 5000 || enrollments.Length > 5000 || contents == null || heroes == null || rules == null)
                throw new InvalidDataException("挑战缓存格式无效。");
            ChallengeContract.Origin(server_origin);
            if (!new[] {"bound", "unbound", "pending", "rejected", "approved"}.Contains(identity_status) || player == null || player.bound != (identity_status == "bound"))
                throw new InvalidDataException("在线身份状态不一致。");
            player.Validate();
            foreach (var item in items.Concat(enrollments)) item.Validate();
            if (items.Select(i => i.id).Distinct().Count() != items.Length || enrollments.Select(i => i.id).Distinct().Count() != enrollments.Length)
                throw new InvalidDataException("挑战列表有重复条目。");
            foreach (var pair in contents) { ChallengeContract.ContentId(pair.Key); ChallengeContract.Text(pair.Value, 120); }
            foreach (var pair in heroes) { if (!ChallengeContract.HeroIds.Contains(pair.Key)) throw new InvalidDataException("人物列表无效。"); ChallengeContract.Text(pair.Value, 60); }
            if (rules.Length != 0 && (rules.Length != 6 || rules.Select(r => r.key).Distinct().Count() != 6)) throw new InvalidDataException("规则目录不完整。");
            foreach (var rule in rules) {
                if (!ChallengeContract.RuleKeys.Contains(rule.key)) throw new InvalidDataException("规则目录无效。");
                ChallengeContract.Text(rule.name, 80); ChallengeContract.Text(rule.description, 1000);
            }
            if (updated_at_utc != null) ChallengeContract.Time(updated_at_utc);
            if (next_identity_refresh_utc != null) ChallengeContract.Time(next_identity_refresh_utc);
        }
        internal int RefreshSeconds(DateTimeOffset now) => next_identity_refresh_utc == null ? 0 : (int)Math.Max(0, Math.Min(60, Math.Ceiling((ChallengeContract.Time(next_identity_refresh_utc) - now).TotalSeconds)));
    }
    internal sealed class ChallengeActivation
    {
        public ChallengeItem challenge { get; set; }
        public ChallengePlayer player { get; set; }
        public string device_id { get; set; }
        public string content_id { get; set; }
        public bool locked { get; set; }
        public string ini_filename { get; set; }
        public string ini { get; set; }
        internal void Validate()
        {
            if (challenge == null || player == null || !player.bound || !locked || ini_filename != ChallengeContract.IniFile) throw new InvalidDataException("挑战下发不完整。");
            challenge.Validate(); player.Validate(); ChallengeContract.Id(device_id); ChallengeContract.ContentId(content_id);
            if (!challenge.contents.Contains(content_id) || ini != challenge.rules.Ini()) throw new InvalidDataException("挑战下发配置与规则不一致。");
        }
    }
    internal sealed class ChallengeBridgeReply
    {
        public string protocol { get; set; } = ChallengeContract.BridgeProtocol;
        public bool ok { get; set; }
        public string code { get; set; }
        public string detail { get; set; }
        public int retry_after_seconds { get; set; }
        public ChallengeState state { get; set; }
        public ChallengeActivation activation { get; set; }
    }
    internal static class ChallengeContract
    {
        internal const string ServerProtocol = "PAL.Challenges.v1", CacheProtocol = "PAL.ChallengeCache.v1", BridgeProtocol = "PAL.ChallengeBridge.v2";
        internal const string CoverFile = "palmod/challenge-cover.v1.txt";
        internal const string CacheFile = "palmod/challenges.v1.json", IniFile = "challenge-rules.ini", SelectionFile = "palmod/challenge-selection.v1.json";
        internal static readonly string[] RuleKeys = {"solo_art", "helpless_husband", "true_duel", "level_one_solo", "individual_flee", "chosen_hero"};
        internal static readonly string[] IniKeys = {"SoloArt", "HelplessHusband", "TrueDuel", "LevelOneSolo", "IndividualFlee", "ChosenHero"};
        internal static readonly string[] HeroIds = {"li-xiaoyao", "zhao-linger", "lin-yueru", "anu", "wuhou"};
        internal static JavaScriptSerializer Json() => new JavaScriptSerializer {MaxJsonLength = 8388608, RecursionLimit = 32};
        internal static Dictionary<string, object> Object(string text)
        {
            var result = Value(text) as Dictionary<string, object>;
            if (result == null) throw new InvalidDataException("挑战数据根节点无效。");
            return result;
        }
        private static object Value(string text)
        {
            if (text == null || Encoding.UTF8.GetByteCount(text) > 8388608) throw new InvalidDataException("挑战数据超过容量。");
            StrictJsonSyntaxValidator.Validate(text, 32, 500000);
            var result = Json().DeserializeObject(text);
            StrictTypes(result); return result;
        }
        private static void StrictTypes(object value)
        {
            var dict = value as Dictionary<string, object>;
            if (dict != null) foreach (var pair in dict) {
                if ((RuleKeys.Contains(pair.Key) || new[] {"bound", "locked", "ok"}.Contains(pair.Key)) && !(pair.Value is bool)) throw new InvalidDataException("规则布尔字段无效。");
                if (new[] {"revision", "latest_revision", "schema_version", "retry_after_seconds"}.Contains(pair.Key) && !(pair.Value is int)) throw new InvalidDataException("规则版本字段无效。");
                if (pair.Key == "rules" && pair.Value is Dictionary<string, object> fields &&
                    (fields.Count != 8 || !RuleKeys.Concat(new[] {"schema_version", "hero"}).All(fields.ContainsKey))) throw new InvalidDataException("牢局规则字段不完整。");
                if (pair.Key == "player") {
                    var player = pair.Value as Dictionary<string, object>; object bound, id, name;
                    if (player == null || !player.TryGetValue("bound", out bound) || !(bound is bool) || !player.TryGetValue("id", out id) || id != null && !(id is string) ||
                        !player.TryGetValue("display_name", out name) || name != null && !(name is string)) throw new InvalidDataException("玩家身份字段不完整。");
                }
                if (pair.Key == "cover" && pair.Value != null) {
                    var cover = pair.Value as Dictionary<string, object>;
                    string[] keys = {"schema_version", "background_id", "planner", "line3", "line4", "lines"};
                    if (cover == null || cover.Count != keys.Length || !keys.All(cover.ContainsKey) ||
                        !(cover["schema_version"] is int) || !keys.Skip(1).Take(4).All(k => cover[k] is string) || !(cover["lines"] is object[]))
                        throw new InvalidDataException("封面字段不完整。");
                    foreach (object row in (object[])cover["lines"]) {
                        var line = row as Dictionary<string, object>;
                        if (line == null || line.Count != 4 || !new[] {"x", "y", "font_size", "color"}.All(line.ContainsKey) ||
                            !(line["x"] is int) || !(line["y"] is int) || !(line["font_size"] is int) || !(line["color"] is string))
                            throw new InvalidDataException("封面行字段无效。");
                    }
                }
                StrictTypes(pair.Value);
            } else if (value is IEnumerable array && !(value is string)) foreach (var item in array) StrictTypes(item);
        }
        internal static T Read<T>(string text) { Value(text); return Json().Deserialize<T>(text); }
        internal static ChallengeState ReadState(string text) { var state = Read<ChallengeState>(text); if (state == null) throw new InvalidDataException("挑战缓存为空。"); state.Validate(); return state; }
        internal static void Id(string value) { Guid guid; if (!Guid.TryParseExact(value, "D", out guid) || guid == Guid.Empty) throw new InvalidDataException("挑战身份ID无效。"); }
        internal static void ContentId(string value) { if (!Regex.IsMatch(value ?? "", @"\A[a-z0-9][a-z0-9-]{0,63}\z")) throw new InvalidDataException("游戏内容ID无效。"); }
        internal static void Text(string value, int max) { if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(char.IsControl)) throw new InvalidDataException("挑战名称或说明无效。"); new UTF8Encoding(false, true).GetByteCount(value); }
        internal static string Origin(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/" || uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) throw new InvalidDataException("挑战服务器来源无效。");
            return uri.GetLeftPart(UriPartial.Authority);
        }
        internal static DateTimeOffset Time(string value) { DateTimeOffset time; if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out time)) throw new InvalidDataException("挑战时间无效。"); return time; }
    }
}
