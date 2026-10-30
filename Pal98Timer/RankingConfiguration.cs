using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Pal98Timer
{
    // Independent of TimelineIdentity: online grouping must not move local PB files.
    internal sealed class RankingConfiguration
    {
        public string schema { get; set; } = "PAL98.RankingConfiguration.v1";
        public string configuration_id { get; set; }
        public bool covered { get; set; }
        public Dictionary<string, string> rules { get; set; }
        internal bool SpeedIndependent => SdlpalBridge.SupportedProfile(Value("profile")) &&
            Value("family") == SdlpalBridge.ProfileFamily(Value("profile")) &&
            !new[] { "fade_ms", "map_speed_ticks", "walk_frame_ms", "speed", "battle_speed" }.Any(k => rules.ContainsKey(k));
        internal static string Digest(IDictionary<string, string> facts)
        {
            var text = new StringBuilder("PAL98.RankingConfiguration.v1\n");
            foreach (var pair in facts.OrderBy(p => p.Key, StringComparer.Ordinal))
                text.Append(GameplayModeReader.Quote(pair.Key)).Append(':').Append(GameplayModeReader.Quote(pair.Value)).Append('\n');
            return CompetitionProtocol.Hash(text.ToString());
        }
        internal bool Valid(GameplayIdentity game)
        {
            if (schema != "PAL98.RankingConfiguration.v1" || !covered || game == null || !game.Valid ||
                rules == null || rules.Count == 0 || rules.Count > 512 || rules.Any(p => p.Value == null || p.Key == null || p.Key.Length > 160 || !Regex.IsMatch(p.Key, "^[A-Za-z0-9_.-]+$") || p.Value.Length > 2048) ||
                configuration_id != Digest(rules)) return false;
            return Value("content") == game.content_sha256 && Value("family") == game.family &&
                (SpeedIndependent || Value("fade_ms") == game.fade_ms.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                Value("map_speed_ticks") == game.map_speed_ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)) &&
                (!game.family.StartsWith("sdlpal-", StringComparison.Ordinal) || ValidSdlRules(game));
        }
        private bool ValidSdlRules(GameplayIdentity game)
        {
            var resources = new[] { "DATA.MKF", "M.MSG", "SSS.MKF", "WORD.DAT" };
            var keys = new HashSet<string>(new[] { "family", "content", "profile" }, StringComparer.Ordinal);
            if (!SpeedIndependent) keys.UnionWith(new[] { "fade_ms", "map_speed_ticks", "walk_frame_ms", "speed", "battle_speed" });
            foreach (var resource in resources) keys.Add("resource." + resource);
            var gameplay = new[] { "gameplay.rule", "gameplay.revision", "gameplay.sha256" };
            if (gameplay.Any(rules.ContainsKey)) {
                if (!gameplay.All(rules.ContainsKey) || !SdlpalBridge.ValidGameplayRule(Value("profile"),
                    Value("gameplay.rule"), Value("gameplay.revision"), Value("gameplay.sha256"))) return false;
                keys.UnionWith(gameplay);
            }
            if (rules.ContainsKey("wuqiang"))
            {
                if (Value("wuqiang") != "1") return false;
                keys.Add("wuqiang");
            }
            bool legacy = rules.ContainsKey("engine_sha256") || rules.ContainsKey("engine_build") || rules.ContainsKey("route_rules_sha256");
            if (rules.ContainsKey("respawn_rule"))
            {
                if (Value("respawn_rule") != "pal98-current-scene-v1") return false;
                keys.Add("respawn_rule");
            }
            foreach (string key in new[] { "monster_rush", "invisible_monsters" })
                if (rules.ContainsKey(key))
                {
                    if (Value(key) != "1" || Value("respawn_rule") != "pal98-current-scene-v1") return false;
                    keys.Add(key);
                }
            if (legacy)
            {
                if (SpeedIndependent) return false;
                keys.UnionWith(new[] { "engine_sha256", "engine_build", "route_rules_sha256" });
                if (!CompetitionProtocol.Digest(Value("engine_sha256")) || !CompetitionProtocol.Digest(Value("route_rules_sha256")) ||
                    string.IsNullOrWhiteSpace(Value("engine_build")) || Value("engine_build").Length > 128 || Value("engine_build").Any(char.IsControl)) return false;
            }
            int speed, battle;
            return keys.SetEquals(rules.Keys) && SdlpalBridge.SupportedProfile(Value("profile")) && SdlpalBridge.ProfileFamily(Value("profile")) == game.family && Value("profile") == game.content_id &&
                (SpeedIndependent ? game.speed.HasValue && game.battle_speed.HasValue : Value("walk_frame_ms") == game.walk_frame_ms?.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                int.TryParse(Value("speed"), out speed) && speed >= 0 && speed <= 100 && Value("speed") == speed.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                int.TryParse(Value("battle_speed"), out battle) && battle >= 0 && battle <= 100 && Value("battle_speed") == battle.ToString(System.Globalization.CultureInfo.InvariantCulture)) &&
                resources.All(r => CompetitionProtocol.Digest(Value("resource." + r))) &&
                Value("content") == CompetitionProtocol.Hash(string.Join("\n", resources.Select(r => r + "=" + Value("resource." + r))));
        }
        internal string Value(string key) => rules != null && rules.TryGetValue(key, out var value) ? value : null;
        internal RankingConfiguration Copy() => new RankingConfiguration { schema = schema, configuration_id = configuration_id,
            covered = covered, rules = rules == null ? null : new Dictionary<string, string>(rules, StringComparer.Ordinal) };
    }
}
