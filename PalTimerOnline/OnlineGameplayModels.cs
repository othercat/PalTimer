using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pal98Timer
{
    internal sealed class GameplayIdentity
    {
        internal static bool IsSdlFamily(string value) => value == "sdlpal-geshi-easy" || value == "sdlpal-dream22" || value == "sdlpal-dream222" || value == "sdlpal-dreamspace39x";
        public string schema { get; set; } = "PAL98.GameplayIdentity.v1";
        public string rules_sha256 { get; set; }
        public string content_id { get; set; }
        public string content_sha256 { get; set; }
        public string family { get; set; }
        public int fade_ms { get; set; }
        public int map_speed_ticks { get; set; }
        public int? walk_frame_ms { get; set; }
        public int? speed { get; set; }
        public int? battle_speed { get; set; }
        internal bool Valid => schema == "PAL98.GameplayIdentity.v1" && CompetitionProtocol.Digest(rules_sha256) &&
            CompetitionProtocol.Digest(content_sha256) && CompetitionProtocol.Identifier(content_id) &&
            ((family == "standard" || family == "drawcard") && (fade_ms == 800 || fade_ms == 1200) && (map_speed_ticks == 9 || map_speed_ticks == 10) && !walk_frame_ms.HasValue && !speed.HasValue && !battle_speed.HasValue ||
             IsSdlFamily(family) && fade_ms > 0 && fade_ms <= 86400000 && map_speed_ticks == 0 && walk_frame_ms > 0 && walk_frame_ms <= 1000000 &&
             (!speed.HasValue || speed >= 0 && speed <= 100) && (!battle_speed.HasValue || battle_speed >= 0 && battle_speed <= 100));
        internal GameplayIdentity Copy() => (GameplayIdentity)MemberwiseClone();
    }
    internal sealed class CompetitionHardcore
    {
        public bool requested { get; set; }
        public bool run_verified { get; set; }
        public int rules_version { get; set; }
        public string evidence_status { get; set; } = "ordinary";
        internal bool Valid => !requested ? !run_verified && rules_version == 0 && evidence_status == "ordinary" :
            rules_version >= 0 && rules_version <= 4 && (run_verified ? rules_version >= 1 && evidence_status == "runtime_observed" : evidence_status == "unverified");
    }
    internal static class TimelineIdentity
    {
        internal static string Create(string rules, string route, bool hardcore) =>
            CompetitionProtocol.Hash("PAL98.TimelineIdentity.v1\n" + rules + "\n" + route + "\n" + (hardcore ? "hardcore" : "ordinary") + "\n");
    }
    internal static class GameplayModeReader
    {
        internal static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            foreach (char c in value ?? "")
                if (c == '\\' || c == '"') result.Append('\\').Append(c);
                else if (c < 32) result.Append("\\u").Append(((int)c).ToString("x4")); else result.Append(c);
            return result.Append('"').ToString();
        }
    }
}
