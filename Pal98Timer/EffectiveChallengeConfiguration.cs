using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Pal98.Challenges
{
    // Data only. The online snapshot is immutable; this describes the actual
    // merged configuration file and never claims to be a memory observation.
    internal static class EffectiveChallengeConfiguration
    {
        internal const string Schema = "PAL.ChallengeEffective.v1";
        internal static readonly string[] SdlSlots = { "SoloArt", "HelplessHusband", "TrueDuel", "LevelOneSolo", "IndividualFlee", "ChosenHero",
            "StartingSupplies", "NoCoopMagic", "Drop99", "StealAll", "NoInvisibilityItem", "InvisibilityMana1", "SilkwormStatsOnly", "AdvancedTrade",
            "AutoCollect", "SellStoryItems", "Steal99", "SpiritShop", "BanSilkworm", "BanInvisibilityItem", "BanQianKun", "BanJiuShen", "HelplessWife", "StartingHolyTalisman",
            "BanTenMileIncense", "BanExorcismIncense", "BanVajraTalisman", "YoumengIllusion1", "BloodBattle", "ErraticSpeed", "BattleAtWill", "UniversalTrouble", "YoumengIllusion2" };
        internal static bool CompleteSdl(IDictionary<string, string> values) => SdlSlots.All(slot => values.TryGetValue(slot, out string value) &&
            (value == "0" || value == "1") && values.ContainsKey("Priority" + slot)) && values.TryGetValue("Hero", out string hero) &&
            new[] { "li-xiaoyao", "zhao-linger", "lin-yueru", "anu", "wuhou" }.Contains(hero);
        internal static Dictionary<string, string> Parse(string ini, bool complete = true)
        {
            if (ini == null || Encoding.UTF8.GetByteCount(ini) > 65536) throw new InvalidDataException("有效挑战配置为空或超过容量。");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); string section = "";
            foreach (string raw in ini.TrimStart('\uFEFF').Replace("\r", "").Split('\n')) {
                string line = raw.Trim(); if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal)) {section = line.Substring(1, line.Length - 2); continue;}
                if (!section.Equals("Challenge", StringComparison.OrdinalIgnoreCase)) continue;
                int equal = line.IndexOf('='); if (equal <= 0) throw new InvalidDataException("挑战配置行无效。");
                string key = line.Substring(0, equal).Trim(), value = line.Substring(equal + 1).Trim();
                if (!Regex.IsMatch(key, @"\A[A-Z][A-Za-z0-9]{0,71}\z") || !names.Add(key) ||
                    !Regex.IsMatch(value, @"\A(?:0|[1-9][0-9]{0,9}|[a-z][a-z0-9-]{0,63})\z"))
                    throw new InvalidDataException("挑战配置字段无效或重复：" + key);
                values.Add(key, value);
            }
            if (!values.TryGetValue("SchemaVersion", out string schema) || schema != "1") throw new InvalidDataException("有效挑战配置版本无效。");
            values.Remove("SchemaVersion");
            var slots = new HashSet<string>(values.Keys.Where(k => !k.StartsWith("Priority", StringComparison.Ordinal)), StringComparer.Ordinal);
            if (slots.Count < 1 || slots.Count > 128 || values.Count > 256 || values.Any(p => p.Key.StartsWith("Priority", StringComparison.Ordinal) &&
                (complete && !slots.Contains(p.Key.Substring(8)) || !int.TryParse(p.Value, out int priority) || priority < 0 || priority > 999)))
                throw new InvalidDataException("有效挑战配置字段或优先级不完整。");
            return values;
        }
        internal static string Serialize(IDictionary<string, string> values) => "[Challenge]\nSchemaVersion=1\n" +
            string.Concat(values.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value + "\n"));
        internal static string Merge(string online, string local)
        {
            var values = Parse(local);
            foreach (var pair in Parse(online, false)) values[pair.Key] = pair.Value;
            string result = Serialize(values); Parse(result); return result;
        }
        internal static void RequireSource(string online, string effective)
        {
            var actual = Parse(effective);
            if (Parse(online, false).Any(p => !actual.TryGetValue(p.Key, out string value) || value != p.Value))
                throw new InvalidDataException("实际配置与在线已定义项目不一致。");
        }
        internal static void AddFacts(IDictionary<string, string> facts, string ini)
        {
            facts["config.schema"] = Schema; facts["config.evidence"] = "configuration-file";
            foreach (var pair in Parse(ini)) facts["config.slot." + pair.Key] = pair.Value;
        }
        internal static bool ValidFacts(IDictionary<string, string> facts)
        {
            try {
                var keys = facts.Keys.Where(k => k.StartsWith("config.", StringComparison.Ordinal)).ToArray();
                if (keys.Length == 0) return true;
                if (!facts.TryGetValue("config.schema", out string schema) || schema != Schema ||
                    !facts.TryGetValue("config.evidence", out string evidence) || evidence != "configuration-file") return false;
                var source = new[] { "config.source.id", "config.source.revision", "config.source.content_id", "config.source.rules_sha256" };
                if (source.Any(facts.ContainsKey) && (!source.All(facts.ContainsKey) || !Guid.TryParseExact(facts[source[0]], "D", out Guid id) || id == Guid.Empty ||
                    !Regex.IsMatch(facts[source[1]], @"\A[1-9][0-9]{0,8}\z") || !Regex.IsMatch(facts[source[2]], @"\A[a-z0-9][a-z0-9-]{0,63}\z") ||
                    !Regex.IsMatch(facts[source[3]], @"\A[0-9a-f]{64}\z"))) return false;
                var slots = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string key in keys.Except(new[] { "config.schema", "config.evidence" }).Except(source)) {
                    if (!key.StartsWith("config.slot.", StringComparison.Ordinal)) return false;
                    slots.Add(key.Substring(12), facts[key]);
                }
                Parse(Serialize(slots));
                return !facts.TryGetValue("family", out string family) || !family.StartsWith("sdlpal-", StringComparison.Ordinal) || CompleteSdl(slots);
            } catch (Exception error) when (error is InvalidDataException || error is ArgumentException) { return false; }
        }
    }
}
