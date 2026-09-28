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
                Value("fade_ms") == game.fade_ms.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                Value("map_speed_ticks") == game.map_speed_ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        internal string Value(string key) => rules != null && rules.TryGetValue(key, out var value) ? value : null;
        internal RankingConfiguration Copy() => new RankingConfiguration { schema = schema, configuration_id = configuration_id,
            covered = covered, rules = rules == null ? null : new Dictionary<string, string>(rules, StringComparer.Ordinal) };
    }
}
