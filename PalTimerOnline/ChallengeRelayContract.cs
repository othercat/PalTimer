// Refs #52. Transport envelope only; game rule schemas belong to ConfigTool.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Pal98.Challenges
{
    internal static class ChallengeRelayContract
    {
        internal const string ServerProtocol = "PAL.Challenges.v1", CacheProtocol = "PAL.ChallengeCache.v1", BridgeProtocol = "PAL.ChallengeBridge.v2", CacheFile = "palmod/challenges.v1.json";
        internal static JavaScriptSerializer Json() => new JavaScriptSerializer {MaxJsonLength = 8388608, RecursionLimit = 32};
        internal static string Serialize(object value)
        {string text = Json().Serialize(value); if (Encoding.UTF8.GetByteCount(text) > 8388608) throw new InvalidDataException("挑战数据超过容量。"); return text;}
        internal static Dictionary<string, object> Object(string text)
        {
            if (text == null || Encoding.UTF8.GetByteCount(text) > 8388608) throw new InvalidDataException("挑战数据超过容量。");
            StrictJsonSyntaxValidator.Validate(text, 32, 500000);
            return Map(Json().DeserializeObject(text));
        }
        internal static Dictionary<string, object> Map(object value) => value as Dictionary<string, object> ?? throw new InvalidDataException("挑战对象字段无效。");
        internal static object Field(Dictionary<string, object> value, string key)
        {object result; if (!value.TryGetValue(key, out result)) throw new InvalidDataException("挑战响应字段缺失。"); return result;}
        internal static string String(Dictionary<string, object> value, string key)
        {var result = Field(value, key) as string; if (string.IsNullOrEmpty(result)) throw new InvalidDataException("挑战文字字段缺失。"); return result;}
        internal static int Integer(Dictionary<string, object> value, string key)
        {var result = Field(value, key); if (!(result is int)) throw new InvalidDataException("挑战整数字段无效。"); return (int)result;}
        internal static bool Boolean(Dictionary<string, object> value, string key)
        {var result = Field(value, key); if (!(result is bool)) throw new InvalidDataException("挑战布尔字段无效。"); return (bool)result;}
        internal static object[] Array(object value)
        {if (!(value is object[] result)) throw new InvalidDataException("挑战列表字段无效。"); return result;}
        internal static bool Same(object left, object right)
        {
            if (left is Dictionary<string, object> a && right is Dictionary<string, object> b) return a.Count == b.Count && a.All(p => b.ContainsKey(p.Key) && Same(p.Value, b[p.Key]));
            if (left is object[] x && right is object[] y) return x.Length == y.Length && x.Zip(y, Same).All(v => v);
            return Equals(left, right);
        }
        internal static void Id(string value)
        {Guid id; if (!Guid.TryParseExact(value, "D", out id) || id == Guid.Empty) throw new InvalidDataException("挑战身份ID无效。");}
        internal static void ContentId(string value)
        {if (!Regex.IsMatch(value ?? "", @"\A[a-z0-9][a-z0-9-]{0,63}\z")) throw new InvalidDataException("游戏内容ID无效。");}
        internal static void Text(string value, int max)
        {if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(char.IsControl)) throw new InvalidDataException("挑战名称或说明无效。"); new UTF8Encoding(false, true).GetByteCount(value);}
        internal static string Origin(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/" || uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) throw new InvalidDataException("挑战服务器来源无效。");
            return uri.GetLeftPart(UriPartial.Authority);
        }
        internal static DateTimeOffset Time(string value)
        {DateTimeOffset time; if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out time)) throw new InvalidDataException("挑战时间无效。"); return time;}
        internal static Dictionary<string, object> Player(object value)
        {
            var player = Map(value); bool bound = Boolean(player, "bound"); object id = Field(player, "id"), name = Field(player, "display_name");
            if (bound) {if (!(id is string) || !(name is string)) throw new InvalidDataException("在线身份字段无效。"); Id((string)id); Text((string)name, 60);}
            else if (id != null && (!(id is string) || ((string)id).Length != 0) || name != null && (!(name is string) || ((string)name).Length != 0)) throw new InvalidDataException("未绑定身份字段不一致。");
            return player;
        }
        internal static Dictionary<string, object> Item(object value)
        {
            var item = Map(value); Id(String(item, "id")); Id(String(item, "author_id")); Text(String(item, "author_name"), 60); Text(String(item, "title"), 80);
            int revision = Integer(item, "revision"), latest = Integer(item, "latest_revision"); var contents = Array(Field(item, "contents"));
            if (revision < 1 || latest < revision || contents.Length < 1 || contents.Length > 32 || contents.Any(c => !(c is string)) || contents.Distinct().Count() != contents.Length) throw new InvalidDataException("挑战内容或修订无效。");
            foreach (string content in contents) ContentId(content);
            // Preserve all rule versions and nested values. Do not infer engine
            // support, reconstruct INI, or discard fields absent from this build.
            Document(Field(item, "rules")); object status, rulesRevision;
            if (item.TryGetValue("approval_status", out status) && status != null && (!(status is string) || !new[] {"pending", "approved", "rejected"}.Contains((string)status)) ||
                item.TryGetValue("rules_revision", out rulesRevision) && (!(rulesRevision is int) || (int)rulesRevision < 0)) throw new InvalidDataException("挑战审批或规则修订无效。");
            return item;
        }
        internal static object[] Items(object value)
        {
            var items = Array(value); if (items.Length > 5000) throw new InvalidDataException("挑战目录超过容量。"); var ids = new HashSet<string>();
            foreach (var raw in items) {var item = Item(raw); if (!ids.Add(String(item, "id"))) throw new InvalidDataException("挑战列表有重复条目。");} return items;
        }
        internal static void Document(object value)
        {
            // Only frame a bounded JSON container. Entry IDs, labels, grouping,
            // parameter types and supported versions belong to the interpreter.
            if (value is object[] array) {if (array.Length > 4096) throw new InvalidDataException("规则文档超过容量。");}
            else if (value is Dictionary<string, object> map) {if (map.Count > 4096) throw new InvalidDataException("规则文档超过容量。");}
            else throw new InvalidDataException("规则文档必须是JSON对象或数组。");
        }
        internal static Dictionary<string, object> NewState(string origin) => new Dictionary<string, object> {
            {"protocol", CacheProtocol}, {"server_origin", origin}, {"identity_status", "unbound"},
            {"player", new Dictionary<string, object> {{"bound", false}, {"id", null}, {"display_name", null}}},
            {"items", new object[0]}, {"enrollments", new object[0]}, {"rules", new object[0]},
            {"contents", new Dictionary<string, object>()}, {"heroes", new Dictionary<string, object>()}};
        internal static void State(Dictionary<string, object> state)
        {
            if (String(state, "protocol") != CacheProtocol) throw new InvalidDataException("挑战缓存协议无效。");
            Origin(String(state, "server_origin")); var player = Player(Field(state, "player")); string status = String(state, "identity_status");
            if (!new[] {"bound", "unbound", "pending", "rejected", "approved"}.Contains(status) || Boolean(player, "bound") != (status == "bound")) throw new InvalidDataException("在线身份状态不一致。");
            Items(Field(state, "items")); Items(Field(state, "enrollments")); Document(Field(state, "rules"));
            foreach (var pair in Map(Field(state, "contents"))) {ContentId(pair.Key); if (!(pair.Value is string)) throw new InvalidDataException("游戏内容名称无效。"); Text((string)pair.Value, 120);}
            foreach (var pair in Map(Field(state, "heroes"))) {ContentId(pair.Key); if (!(pair.Value is string)) throw new InvalidDataException("人物名称无效。"); Text((string)pair.Value, 60);}
            object revision; if (state.TryGetValue("rules_revision", out revision) && (!(revision is int) || (int)revision < 0)) throw new InvalidDataException("目录修订无效。");
            foreach (string key in new[] {"updated_at_utc", "next_identity_refresh_utc"}) {object value; if (state.TryGetValue(key, out value) && value != null) {if (!(value is string)) throw new InvalidDataException("挑战时间字段无效。"); Time((string)value);}}
        }
        internal static int RefreshSeconds(Dictionary<string, object> state, DateTimeOffset now)
        {object next; return state.TryGetValue("next_identity_refresh_utc", out next) && next != null ? (int)Math.Max(0, Math.Min(60, Math.Ceiling((Time((string)next) - now).TotalSeconds))) : 0;}
        internal static void Activation(Dictionary<string, object> value, string device, string content)
        {
            var item = Item(Field(value, "challenge")); var player = Player(Field(value, "player"));
            if (!Boolean(player, "bound") || !Boolean(value, "locked") || String(value, "device_id") != device || String(value, "content_id") != content ||
                !Array(Field(item, "contents")).Contains(content) || Integer(item, "revision") != Integer(item, "latest_revision") || String(item, "approval_status") != "approved")
                throw new InvalidDataException("挑战需要匹配设备、内容、最新版本和管理员批准的身份。");
            // ConfigTool validates schema, capabilities and exact files before
            // its atomic install. The timer never installs game configuration.
        }
    }
}
