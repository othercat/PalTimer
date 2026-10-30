// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace Pal98Timer
{
    internal sealed class SdlpalBattleEvent
    {
        public ulong seq { get; set; }
        public ulong epoch { get; set; }
        public int team { get; set; }
        public bool boss { get; set; }
        public int result { get; set; }
        public int scene { get; set; }
        public int[] enemies { get; set; }
    }
    internal sealed class SdlpalBattle
    {
        public bool active { get; set; }
        public ulong sequence { get; set; }
        public int team { get; set; }
        public int round { get; set; }
    }
    internal sealed class SdlpalInventory
    {
        public int slot { get; set; }
        public int id { get; set; }
        public int amount { get; set; }
        public int inUse { get; set; }
    }
    internal sealed class SdlpalItemRole
    {
        public int id { get; set; }
        public string name { get; set; }
        public int[] equipment { get; set; }
    }
    internal sealed class SdlpalCatalogEntry
    {
        public int id { get; set; }
        public string name { get; set; }
        public int[] data { get; set; }
    }
    internal sealed class SdlpalSnapshot
    {
        public int schema { get; set; }
        public string profile { get; set; }
        public bool verified { get; set; }
        public bool diagnostic { get; set; }
        public bool wuqiang { get; set; }
        public bool monsterRush { get; set; }
        public bool invisibleMonsters { get; set; }
        public bool dreamspaceThrowFormula { get; set; }
        public string gameplayRuleId { get; set; }
        public int? gameplayRuleRevision { get; set; }
        public string gameplayRuleSha256 { get; set; }
        public string respawnRule { get; set; }
        public string releaseRevision { get; set; }
        public string resourceHash { get; set; }
        public Dictionary<string, string> resourceHashes { get; set; }
        public string build { get; set; }
        public string exeHash { get; set; }
        public string gamePath { get; set; }
        public string savePath { get; set; }
        public string saveLayout { get; set; }
        public bool started { get; set; }
        public bool focused { get; set; }
        public bool busy { get; set; }
        public bool canControl { get; set; }
        public bool completed { get; set; }
        public ulong epoch { get; set; }
        public ulong frame { get; set; }
        public int loadedFrom { get; set; }
        public int speed { get; set; }
        public int battleSpeed { get; set; }
        public int walkFrameMs { get; set; }
        public int fadeCycleMs { get; set; }
        public int scene { get; set; }
        public int map { get; set; }
        public int viewportX { get; set; } public int viewportY { get; set; } public int music { get; set; }
        public int? partyOffsetX { get; set; } public int? partyOffsetY { get; set; } public int? partyDirection { get; set; }
        public long cash { get; set; }
        public SdlpalBattle battle { get; set; }
        public SdlpalBattleEvent[] battleEvents { get; set; }
        public SdlpalInventory[] inventory { get; set; }
        public int[] party { get; set; }
        public SdlpalItemRole[] roles { get; set; }
        public SdlpalCatalogEntry[] catalog { get; set; }
        public int[] itemIds { get; set; }
        internal string ItemDataError;
        internal int Pid;
        internal ulong Created, Published;
        // Online categories describe gameplay, not the executable or its UI.
        // IdentityKey retains the historical serialized/PB format. Speed is a
        // normal Geshi control, so runtime/relay checks use ResourceEngineIdentity.
        internal Dictionary<string, string> RankingFacts(bool includeHistoricalSpeed = false)
        {
            var facts = new Dictionary<string, string>(StringComparer.Ordinal) {
                ["family"] = SdlpalBridge.ProfileFamily(profile), ["profile"] = profile,
                ["content"] = CompetitionProtocol.Hash(string.Join("\n", resourceHashes.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value)))
            };
            // Historical initialization retries must retain their signed bytes.
            // New maintenance categories depend on resources and gameplay effects.
            if (includeHistoricalSpeed)
            {
                facts["fade_ms"] = fadeCycleMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
                facts["map_speed_ticks"] = "0";
                facts["walk_frame_ms"] = walkFrameMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
                facts["speed"] = speed.ToString(System.Globalization.CultureInfo.InvariantCulture);
                facts["battle_speed"] = battleSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            foreach (var pair in resourceHashes) facts["resource." + pair.Key] = pair.Value;
            // Preserve ordinary categories and historical records byte-for-byte.
            if (wuqiang) facts["wuqiang"] = "1";
            if (!string.IsNullOrEmpty(respawnRule)) facts["respawn_rule"] = respawnRule;
            if (monsterRush) facts["monster_rush"] = "1";
            if (invisibleMonsters) facts["invisible_monsters"] = "1";
            if (gameplayRuleId != null) {
                facts["gameplay.rule"] = gameplayRuleId;
                facts["gameplay.revision"] = gameplayRuleRevision?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                facts["gameplay.sha256"] = gameplayRuleSha256;
            }
            // The optional throw formula is informational, not a score category.
            return facts;
        }
        internal string ResourceEngineIdentity => profile + "|" + exeHash + "|" + build + "|" +
            string.Join("|", resourceHashes.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + x.Value)) +
            (wuqiang ? "|wuqiang=1" : "") +
            (!string.IsNullOrEmpty(respawnRule) ? "|respawn_rule=" + respawnRule : "") +
            (monsterRush ? "|monster_rush=1" : "") + (invisibleMonsters ? "|invisible_monsters=1" : "") +
            (gameplayRuleId == null ? "" : "|gameplay.rule=" + gameplayRuleId + "|gameplay.revision=" +
                gameplayRuleRevision?.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|gameplay.sha256=" + gameplayRuleSha256);
        internal string IdentityKey => ResourceEngineIdentity + "|" + walkFrameMs + "|" + fadeCycleMs + "|" + speed + "|" + battleSpeed;
        internal SdlpalSnapshot WithSpeedConfiguration(SdlpalSnapshot source)
        {
            var copy = (SdlpalSnapshot)MemberwiseClone();
            copy.walkFrameMs = source.walkFrameMs; copy.fadeCycleMs = source.fadeCycleMs;
            copy.speed = source.speed; copy.battleSpeed = source.battleSpeed;
            return copy;
        }
        internal static bool SameResourceEngineIdentity(string left, string right)
        {
            string first = WithoutSpeedConfiguration(left), second = WithoutSpeedConfiguration(right);
            return first != null && second != null && first == second;
        }
        private static string WithoutSpeedConfiguration(string identity)
        {
            if (string.IsNullOrEmpty(identity)) return null;
            string[] fields = identity.Split('|');
            if (fields.Length < 11) return null; // profile, EXE, build, four resources, four speeds
            int[] limits = { 1000000, 86400000, 100, 100 };
            for (int i = 0; i < limits.Length; i++)
            {
                string field = fields[fields.Length - 4 + i];
                if (!int.TryParse(field, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value) ||
                    value > limits[i] || field != value.ToString(System.Globalization.CultureInfo.InvariantCulture)) return null;
            }
            return string.Join("|", fields.Take(fields.Length - 4));
        }
    }

    // No OpenProcess/RPM/WPM, injection or PALDLL addresses. A mapping may only
    // identify its actual live owner; callers cannot use a PID from its JSON.
    internal sealed class SdlpalBridge : IDisposable
    {
        internal static bool SupportedProfile(string value) => value == "pal-dos-v1" || value == "pal98-classic-v6-v1" || value == "geshi-easy-v1" || value == "dream22-puppet100-v1" || value == "dream222-normal-v1" || value == "dream222-hard-v1" || value == "dreamspace39x-hard-v1" || value == "dream211-v1" || value == "hunqian-branch-v1" || value == "hunqian167-easy-v1" || value == "hunqian167-hard-v1" || value == "mogai10-v1" || value == "dream211-pudu-v1" || value == "dream211-yueying-v1" || value == "tianqi20-v1" || value == "dream211-yueying-rat-v1" || value == "geshi-hard-v1" || value == "geshi-nightwolf-v1" || value == "geshi-fanmade-v1" || value == "geshi-fanmade-entertainment-v1";
        internal static string ProfileFamily(string value) => value == "pal-dos-v1" ? "sdlpal-dos" : value == "pal98-classic-v6-v1" ? "sdlpal-classic98" : value == "geshi-hard-v1" ? "sdlpal-geshi" : value == "geshi-nightwolf-v1" ? "sdlpal-geshi" : value == "geshi-fanmade-v1" ? "sdlpal-geshi" : value == "geshi-fanmade-entertainment-v1" ? "sdlpal-geshi" : value == "dream211-v1" ? "sdlpal-dream211" : value == "hunqian-branch-v1" ? "sdlpal-hunqian-branch" : value == "hunqian167-easy-v1" ? "sdlpal-hunqian167" : value == "hunqian167-hard-v1" ? "sdlpal-hunqian167" : value == "mogai10-v1" ? "sdlpal-mogai10" : value == "dream211-pudu-v1" ? "sdlpal-dream211" : value == "dream211-yueying-v1" ? "sdlpal-dream211" : value == "tianqi20-v1" ? "sdlpal-tianqi20" : value == "dream211-yueying-rat-v1" ? "sdlpal-dream211" : value == "dream22-puppet100-v1" ? "sdlpal-dream22" : value == "geshi-easy-v1" ? "sdlpal-geshi-easy" : value == "dream222-normal-v1" || value == "dream222-hard-v1" ? "sdlpal-dream222" : value == "dreamspace39x-hard-v1" ? "sdlpal-dreamspace39x" : "";
        internal static string ProfileName(string value) => value == "pal-dos-v1" ? "仙剑DOS原版" : value == "pal98-classic-v6-v1" ? "98柔情经典速通v6" : value == "geshi-hard-v1" ? "隔世追凶·困难" : value == "geshi-nightwolf-v1" ? "隔世追凶·夜狼非人" : value == "geshi-fanmade-v1" ? "隔世追凶·非人饭制" : value == "geshi-fanmade-entertainment-v1" ? "隔世追凶·非人饭制娱乐" : value == "dream211-v1" ? "梦幻2.11" : value == "hunqian-branch-v1" ? "魂牵梦萦 初版分支" : value == "hunqian167-easy-v1" ? "魂牵梦萦1.67 简单" : value == "hunqian167-hard-v1" ? "魂牵梦萦1.67 困难" : value == "mogai10-v1" ? "魔改1.0" : value == "dream211-pudu-v1" ? "梦幻2.11 普渡" : value == "dream211-yueying-v1" ? "梦幻2.11 月影" : value == "tianqi20-v1" ? "天启神佑2.0" : value == "dream211-yueying-rat-v1" ? "梦幻2.11 月影鼠年祈福篇" : value == "dream22-puppet100-v1" ? "梦幻2.2-傀儡100%" : value == "dream222-normal-v1" ? "梦幻2.2.2-普通" : value == "dream222-hard-v1" ? "梦幻2.2.2-高难" : value == "dreamspace39x-hard-v1" ? "梦时空3.9x-高难" : "隔世追凶-简单";
        internal const int Capacity = 4194304, HeaderSize = 64;
        internal const string Prefix = "Local\\SDLPAL.LostEdit.v1.";
        private readonly object gate = new object();
        private MemoryMappedFile map;
        private MemoryMappedViewAccessor view;
        internal Process Process { get; private set; }
        internal ulong Created { get; private set; }
        internal string ExeHash { get; private set; }
        internal string Error { get; private set; } = "等待隔世追凶-简单";
        [DllImport("kernel32.dll")] internal static extern ulong GetTickCount64();
        internal static bool Digest(string text) => text != null && text.Length == 64 && text.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        internal static bool ValidGameplayRule(string profile, string id, string revision, string hash)
        {
            int number;
            return SupportedProfile(profile) && id != null && id.Length <= 96 &&
                Regex.IsMatch(id, "\\A" + Regex.Escape(profile.Substring(0, profile.Length - 3)) + "-rules-v[1-9][0-9]{0,5}\\z") &&
                int.TryParse(revision, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number) && number > 0 &&
                revision == number.ToString(System.Globalization.CultureInfo.InvariantCulture) && Digest(hash);
        }
        internal static void ValidateGameplayRuleFields(Dictionary<string, object> fields, SdlpalSnapshot value)
        {
            string[] names = { "gameplayRuleId", "gameplayRuleRevision", "gameplayRuleSha256" };
            object nested;
            var identity = fields.TryGetValue("identity", out nested) ? nested as Dictionary<string, object> : null;
            bool present = names.Any(fields.ContainsKey), inside = identity != null && names.Any(identity.ContainsKey);
            if (!present && !inside && value.gameplayRuleId == null && value.gameplayRuleRevision == null && value.gameplayRuleSha256 == null) return;
            if (!names.All(fields.ContainsKey) || identity == null || !names.All(identity.ContainsKey) ||
                !(fields[names[0]] is string) || !(fields[names[1]] is int) || !(fields[names[2]] is string) ||
                names.Any(n => !Equals(fields[n], identity[n])) ||
                !Equals(fields[names[0]], value.gameplayRuleId) || !Equals(fields[names[1]], value.gameplayRuleRevision) || !Equals(fields[names[2]], value.gameplayRuleSha256) ||
                !ValidGameplayRule(value.profile, value.gameplayRuleId,
                    value.gameplayRuleRevision?.ToString(System.Globalization.CultureInfo.InvariantCulture), value.gameplayRuleSha256))
                throw new InvalidDataException("公式身份字段不完整、内容不符或内外层不一致");
        }
        internal static void ValidateGameplayRuleJson(string json)
        {
            // JavaScriptSerializer maps CLR members without case sensitivity
            // and replaces repeated JSON keys. Check the wire before that
            // conversion so aliases or duplicate keys cannot hide a binding.
            var root = new HashSet<string>(StringComparer.Ordinal);
            var inner = new HashSet<string>(StringComparer.Ordinal);
            string parent = null;
            try {
            using (var reader = System.Runtime.Serialization.Json.JsonReaderWriterFactory.CreateJsonReader(
                Encoding.UTF8.GetBytes(json), System.Xml.XmlDictionaryReaderQuotas.Max))
            while (reader.Read())
            {
                if (reader.NodeType != System.Xml.XmlNodeType.Element) continue;
                string name = reader.GetAttribute("item") ?? reader.LocalName;
                if (reader.Depth == 1) parent = name;
                if (reader.Depth != 1 && !(reader.Depth == 2 && parent == "identity")) continue;
                foreach (string key in new[] { "gameplayRuleId", "gameplayRuleRevision", "gameplayRuleSha256", "identity" })
                    if (name.Equals(key, StringComparison.OrdinalIgnoreCase) && name != key)
                        throw new InvalidDataException("公式身份字段大小写不规范");
                if (!(reader.Depth == 1 ? root : inner).Add(name))
                    throw new InvalidDataException("游戏快照含重复身份字段");
            }
            } catch (Exception ex) when (ex is System.Xml.XmlException || ex is System.Runtime.Serialization.SerializationException) {
                throw new InvalidDataException("游戏快照 JSON 无效", ex);
            }
        }
        internal static JavaScriptSerializer Json() => new JavaScriptSerializer { MaxJsonLength = Capacity, RecursionLimit = 32 };
        internal static SdlpalSnapshot ReadSnapshotJson(string json)
        {
            ValidateGameplayRuleJson(json);
            var serializer = Json();
            var value = serializer.Deserialize<SdlpalSnapshot>(json);
            ValidateGameplayRuleFields(serializer.Deserialize<Dictionary<string, object>>(json), value);
            return value;
        }
        internal static string HashFile(string path)
        {
            using (var file = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        internal static bool AbsolutePath(string value) => !string.IsNullOrWhiteSpace(value) &&
            Path.IsPathRooted(value) && Path.GetFullPath(value).TrimEnd('\\', '/') == value.TrimEnd('\\', '/') && value.IndexOf('\0') < 0;
        internal static SdlpalSnapshot Decode(byte[] header, byte[] payload, int pid, ulong created, ulong now)
        {
            if (header.Length != HeaderSize || Encoding.ASCII.GetString(header, 0, 8) != "SDLPAL1\0" ||
                BitConverter.ToUInt32(header, 8) != 1 || BitConverter.ToUInt32(header, 12) != HeaderSize ||
                BitConverter.ToUInt32(header, 16) != Capacity || (BitConverter.ToUInt32(header, 20) & 1) != 0 ||
                BitConverter.ToUInt32(header, 24) != payload.Length || payload.Length < 2 || payload.Length > Capacity - HeaderSize ||
                BitConverter.ToUInt32(header, 28) != pid || BitConverter.ToUInt64(header, 32) != created)
                throw new InvalidDataException("游戏快照头或进程身份无效");
            ulong published = BitConverter.ToUInt64(header, 56);
            if (published > now || now - published > 2000) throw new InvalidDataException("游戏快照已过期");
            string json = new UTF8Encoding(false, true).GetString(payload);
            ValidateGameplayRuleJson(json);
            var serializer = Json();
            var fields = serializer.Deserialize<Dictionary<string, object>>(json);
            string[] required = { "schema", "profile", "verified", "diagnostic", "resourceHash", "resourceHashes", "build", "exeHash",
                "gamePath", "savePath", "started", "focused", "busy", "canControl", "completed", "epoch", "frame", "loadedFrom",
                "speed", "battleSpeed", "walkFrameMs", "fadeCycleMs", "scene", "map", "battle", "battleEvents" };
            if (fields == null || required.Any(k => !fields.ContainsKey(k))) throw new InvalidDataException("游戏快照缺少必要字段");
            // Older engines omit this additive field and have no runtime no-wall
            // switch. Do not let the JSON serializer coerce strings/numbers/null.
            object wuqiang;
            if (fields.TryGetValue("wuqiang", out wuqiang) && !(wuqiang is bool))
                throw new InvalidDataException("游戏无墙状态必须为布尔值");
            object nested, nestedWuqiang;
            if (fields.TryGetValue("identity", out nested) && nested is Dictionary<string, object> identityFields &&
                identityFields.TryGetValue("wuqiang", out nestedWuqiang) &&
                (!(nestedWuqiang is bool) || !fields.ContainsKey("wuqiang") || !Equals(nestedWuqiang, wuqiang)))
                throw new InvalidDataException("游戏无墙身份与状态不一致");
            // Optional display metadata never changes timing/relay identity.
            object release;
            string label = fields.TryGetValue("releaseRevision", out release) ? release as string : null;
            if (release != null && !(release is string))
                throw new InvalidDataException("游戏发行版本必须为文本");
            bool validLabel = label != null && label.Length >= 2 && label.Length <= 10 && label[0] == 'r' &&
                label[1] >= '1' && label[1] <= '9' && label.Skip(2).All(c => c >= '0' && c <= '9');
            object nestedRelease;
            if (fields.TryGetValue("identity", out nested) && nested is Dictionary<string, object> releaseIdentity &&
                releaseIdentity.TryGetValue("releaseRevision", out nestedRelease) && !Equals(nestedRelease, release))
                validLabel = false;
            var value = serializer.Deserialize<SdlpalSnapshot>(json);
            ValidateGameplayRuleFields(fields, value);
            foreach (string key in new[] { "partyOffsetX", "partyOffsetY", "partyDirection" })
                if (fields.TryGetValue(key, out object coordinate) && coordinate != null &&
                    (!(coordinate is int) || (int)coordinate < 0 || (int)coordinate > (key == "partyDirection" ? 3 : 65535)))
                    throw new InvalidDataException("队伍触发位置字段无效");
            foreach (string key in new[] { "monsterRush", "invisibleMonsters", "respawnRule", "dreamspaceThrowFormula" })
            {
                object field;
                bool present = fields.TryGetValue(key, out field);
                if (present && (key == "respawnRule" ? field != null && !(field is string) : !(field is bool)))
                    throw new InvalidDataException("玩法规则字段无效");
                object nestedField;
                if (fields.TryGetValue("identity", out nested) && nested is Dictionary<string, object> modeIdentity &&
                    modeIdentity.TryGetValue(key, out nestedField) && (!present || !Equals(nestedField, field)))
                    throw new InvalidDataException("玩法规则身份与状态不一致");
            }
            if (value.dreamspaceThrowFormula && value.profile != "dreamspace39x-hard-v1")
                throw new InvalidDataException("梦时空投掷公式不适用于当前内容");
            if (!string.IsNullOrEmpty(value.respawnRule) && value.respawnRule != "pal98-current-scene-v1" ||
                (value.monsterRush || value.invisibleMonsters) && value.respawnRule != "pal98-current-scene-v1")
                throw new InvalidDataException("未知怪物刷新规则");
            value.releaseRevision = validLabel ? label : null;
            if (value.schema != 1 || !SupportedProfile(value.profile) || !value.verified || !Digest(value.resourceHash) || !Digest(value.exeHash) ||
                value.resourceHashes == null || new[] { "SSS.MKF", "DATA.MKF", "WORD.DAT", "M.MSG" }.Any(k => !value.resourceHashes.ContainsKey(k) || !Digest(value.resourceHashes[k])) ||
                value.resourceHashes["SSS.MKF"] != value.resourceHash || string.IsNullOrWhiteSpace(value.build) || value.build.Length > 128 ||
                value.build.Any(char.IsControl) || !AbsolutePath(value.gamePath) || !AbsolutePath(value.savePath) ||
                value.epoch != BitConverter.ToUInt64(header, 40) || value.battle == null || value.battleEvents == null || value.battleEvents.Length > 64 ||
                value.walkFrameMs < 0 || value.walkFrameMs > 1000000 || value.fadeCycleMs < 0 || value.fadeCycleMs > 86400000 ||
                value.speed < 0 || value.speed > 100 || value.battleSpeed < 0 || value.battleSpeed > 100 ||
                value.battleEvents.Any(e => e == null || e.epoch != value.epoch || e.enemies == null || e.enemies.Length > 16))
                throw new InvalidDataException("游戏资源或状态身份无效");
            for (int i = 1; i < value.battleEvents.Length; i++)
                if (value.battleEvents[i].seq <= value.battleEvents[i - 1].seq) throw new InvalidDataException("战斗事件顺序无效");
            value.Pid = pid; value.Created = created; value.Published = published;
            // Item viewing is an optional capability. Missing/invalid item fields
            // must not turn an otherwise valid timing snapshot into zero stock.
            value.ItemDataError = new[] { "cash", "inventory", "party", "roles", "catalog", "itemIds" }.Any(k => !fields.ContainsKey(k) || fields[k] == null) ||
                !HasItemFields(fields, "inventory", "slot", "id", "amount", "inUse") ||
                !HasItemFields(fields, "roles", "id", "name", "equipment") || !HasItemFields(fields, "catalog", "id", "name", "data")
                ? "游戏未提供完整物品数据，请更新配套游戏" : null;
            return value;
        }
        private static bool HasItemFields(Dictionary<string, object> root, string name, params string[] required)
        {
            object value;
            if (!root.TryGetValue(name, out value) || value is string) return false;
            var entries = value as System.Collections.IEnumerable;
            if (entries == null) return false;
            foreach (object entry in entries)
            {
                var fields = entry as Dictionary<string, object>;
                if (fields == null || required.Any(k => !fields.ContainsKey(k) || fields[k] == null)) return false;
            }
            return true;
        }
        internal void Attach(Process process)
        {
            lock (gate)
            {
                Close();
                try
                {
                    ulong created = (ulong)process.StartTime.ToUniversalTime().ToFileTimeUtc();
                    map = MemoryMappedFile.OpenExisting(Prefix + process.Id, MemoryMappedFileRights.Read);
                    view = map.CreateViewAccessor(0, Capacity, MemoryMappedFileAccess.Read);
                    Process = process; Created = created;
                    ExeHash = HashFile(process.MainModule.FileName);
                    SdlpalSnapshot snapshot;
                    if (!TryRead(out snapshot)) throw new InvalidDataException(Error);
                }
                catch { Close(); throw; }
            }
        }
        internal bool TryRead(out SdlpalSnapshot snapshot)
        {
            lock (gate)
            {
                snapshot = null;
                try
                {
                    if (Process == null || view == null || Process.HasExited || (ulong)Process.StartTime.ToUniversalTime().ToFileTimeUtc() != Created)
                        throw new InvalidDataException("游戏未连接或已经退出");
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        uint sequence = view.ReadUInt32(20);
                        if ((sequence & 1) != 0) continue;
                        Thread.MemoryBarrier();
                        byte[] header = new byte[HeaderSize]; view.ReadArray(0, header, 0, header.Length);
                        uint length = BitConverter.ToUInt32(header, 24);
                        if (length < 2 || length > Capacity - HeaderSize) throw new InvalidDataException("快照长度无效");
                        byte[] payload = new byte[length]; view.ReadArray(HeaderSize, payload, 0, payload.Length);
                        Thread.MemoryBarrier();
                        if (sequence != view.ReadUInt32(20) || sequence != BitConverter.ToUInt32(header, 20)) continue;
                        var read = Decode(header, payload, Process.Id, Created, GetTickCount64());
                        if (Process.HasExited) throw new InvalidDataException("游戏已退出");
                        if (read.exeHash != ExeHash) throw new InvalidDataException("运行引擎文件与快照身份不一致");
                        snapshot = read; Error = ""; return true;
                    }
                    throw new InvalidDataException("游戏正在更新快照");
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException ||
                    ex is InvalidOperationException || ex is ArgumentException || ex is FormatException || ex is OverflowException ||
                    ex is System.ComponentModel.Win32Exception || ex is DecoderFallbackException)
                { Error = ex.Message; return false; }
            }
        }
        internal static SdlpalBridge Discover(out string error)
        {
            error = "等待隔世追凶-简单";
            SdlpalBridge found = null;
            foreach (var process in System.Diagnostics.Process.GetProcesses())
            {
                var candidate = new SdlpalBridge();
                try
                {
                    candidate.Attach(process);
                    if (found != null) { found.Dispose(); candidate.Dispose(); error = "检测到多个隔世游戏，请只保留一个"; return null; }
                    found = candidate;
                }
                catch { candidate.Dispose(); process.Dispose(); }
            }
            return found;
        }
        private void Close()
        {
            view?.Dispose(); map?.Dispose(); view = null; map = null;
            Process?.Dispose(); Process = null; Created = 0; ExeHash = null;
        }
        public void Dispose() { lock (gate) Close(); }
    }
}
