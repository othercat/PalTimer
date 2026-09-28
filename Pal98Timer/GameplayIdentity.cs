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
        public string schema { get; set; } = "PAL98.GameplayIdentity.v1";
        public string rules_sha256 { get; set; }
        public string content_id { get; set; }
        public string content_sha256 { get; set; }
        public string family { get; set; }
        public int fade_ms { get; set; }
        public int map_speed_ticks { get; set; }
        internal bool Valid => schema == "PAL98.GameplayIdentity.v1" && CompetitionProtocol.Digest(rules_sha256) &&
            CompetitionProtocol.Digest(content_sha256) && CompetitionProtocol.Identifier(content_id) &&
            (family == "standard" || family == "drawcard") && (fade_ms == 800 || fade_ms == 1200) && (map_speed_ticks == 9 || map_speed_ticks == 10);
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
    internal sealed class GameplaySnapshot
    {
        public string schema { get; set; }
        public bool covered { get; set; }
        public string rules_sha256 { get; set; }
        public string content_id { get; set; }
        public string content_sha256 { get; set; }
        public string family { get; set; }
        public int fade_ms { get; set; }
        public int map_speed_ticks { get; set; }
        public RankingConfiguration ranking { get; set; }
        public uint initial_random_skill_seed { get; set; }
        public Dictionary<string, string> rules { get; set; }
        internal uint Generation;
        internal string OrdinaryTimelineId, HardcoreTimelineId;
        internal GameplayIdentity Identity => new GameplayIdentity { rules_sha256 = rules_sha256, content_id = content_id,
            content_sha256 = content_sha256, family = family, fade_ms = fade_ms, map_speed_ticks = map_speed_ticks };
        internal string Label(bool hardcore)
        {
            var parts = new List<string> { fade_ms == 800 ? "0.8秒" : "1.2秒", map_speed_ticks == 9 ? "快走速" : "普通走速" };
            if (family == "drawcard") parts.Add("抽卡");
            foreach (var pair in new[] { new[] { "random_items", "随机物品" }, new[] { "wuqiang", "吴强" },
                new[] { "random_skills.enabled", "随机技能" }, new[] { "love.enabled", "爱无限" }, new[] { "village", "村村通" } })
                if (rules != null && rules.TryGetValue(pair[0], out string value) && value == "1") parts.Add(pair[1]);
            parts.Add(hardcore ? "硬核模式" : "普通模式");
            if (!covered) parts.Add("未归类");
            return string.Join("&", parts);
        }
    }
    internal static class TimelineIdentity
    {
        internal static string Create(string rules, string route, bool hardcore)
        {
            if (!CompetitionProtocol.Digest(rules) || !CompetitionProtocol.Digest(route)) throw new InvalidDataException("时间线身份不完整。");
            return CompetitionProtocol.Hash("PAL98.TimelineIdentity.v1\n" + rules + "\n" + route + "\n" + (hardcore ? "hardcore" : "ordinary") + "\n");
        }
        internal static string PathFor(string root, string identity, bool covered, string name)
        {
            if (!CompetitionProtocol.Digest(identity) || Path.GetFileName(name) != name) throw new InvalidDataException("时间线路径无效。");
            return covered ? Path.Combine(root, "Timelines", identity, name) : Path.Combine(root, "Timelines", "Unclassified", identity, name);
        }
    }
    // One bounded background reader. No hashing, IO or waits in checkpoint processing.
    internal sealed class GameplayModeReader
    {
        private readonly object sync = new object();
        private Process target;
        internal string Route;
        private long next;
        private bool busy;
        private GameplaySnapshot current;
        internal GameplaySnapshot Current { get { lock (sync) return current; } }
        internal void Observe(Process process)
        {
            lock (sync)
            {
                if (!ReferenceEquals(target, process)) { target = process; current = null; next = 0; }
                if (process == null || busy || Stopwatch.GetTimestamp() < next) return;
                busy = true; next = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
                Task.Run(() => {
                    GameplaySnapshot value = null;
                    try
                    {
                        using (var mapping = MemoryMappedFile.OpenExisting("Local\\PAL98.GameplayMode.v1." + process.Id, MemoryMappedFileRights.Read))
                        using (var view = mapping.CreateViewAccessor(0, 32808, MemoryMappedFileAccess.Read))
                        {
                            var bytes = new byte[32808];
                            long creation = process.StartTime.ToUniversalTime().ToFileTimeUtc();
                            // Retry only torn reads, on this background worker. A coherent
                            // but invalid snapshot must never reuse a previous good value.
                            for (int attempt = 0; attempt < 4; ++attempt) {
                                int sequence = view.ReadInt32(24);
                                view.ReadArray(0, bytes, 0, bytes.Length); Thread.MemoryBarrier();
                                int after = view.ReadInt32(24);
                                if (sequence != after || (sequence & 1) != 0) { Thread.Yield(); continue; }
                                value = Decode(bytes, process.Id, creation, sequence, after); break;
                            }
                            if (process.HasExited) value = null;
                            string route = Route;
                            if (value != null && CompetitionProtocol.Digest(route)) {
                                value.OrdinaryTimelineId = TimelineIdentity.Create(value.rules_sha256, route, false);
                                value.HardcoreTimelineId = TimelineIdentity.Create(value.rules_sha256, route, true);
                            }
                        }
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException || e is System.ComponentModel.Win32Exception) { }
                    finally { lock (sync) { if (ReferenceEquals(target, process)) current = value; busy = false; } }
                });
            }
        }
        internal static GameplaySnapshot Decode(byte[] bytes, int pid, long creation, int before, int after)
        {
            if (bytes == null || bytes.Length != 32808 || before != after || (before & 1) != 0 || before == 0 ||
                BitConverter.ToInt32(bytes, 24) != before || BitConverter.ToUInt32(bytes, 0) != 0x314D5047 ||
                BitConverter.ToUInt16(bytes, 4) != 1 || BitConverter.ToUInt16(bytes, 6) != 40 ||
                BitConverter.ToInt32(bytes, 8) != pid || BitConverter.ToInt64(bytes, 16) != creation ||
                (BitConverter.ToUInt32(bytes, 12) != 0x0106080D && BitConverter.ToUInt32(bytes, 12) != 0x0106080E && BitConverter.ToUInt32(bytes, 12) != 0x0106080F) || BitConverter.ToUInt32(bytes, 36) != 0) return null;
            uint count = BitConverter.ToUInt32(bytes, 32);
            if (count == 0 || count >= 32768) return null;
            try
            {
                var result = CompetitionProtocol.Json().Deserialize<GameplaySnapshot>(new UTF8Encoding(false, true).GetString(bytes, 40, (int)count));
                if (result == null || result.schema != "PAL98.GameplayMode.v1" || !result.Identity.Valid || result.rules == null || result.rules.Count > 512) return null;
                var canonical = new StringBuilder("PAL98.GameplayRules.v1\n");
                foreach (var rule in result.rules.OrderBy(p => p.Key, StringComparer.Ordinal)) canonical.Append(Quote(rule.Key)).Append(':').Append(Quote(rule.Value)).Append('\n');
                if (CompetitionProtocol.Hash(canonical.ToString()) != result.rules_sha256) return null;
                if (result.ranking != null && !result.ranking.Valid(result.Identity)) result.ranking.covered = false;
                result.Generation = BitConverter.ToUInt32(bytes, 28); return result.Generation == 0 ? null : result;
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException) { return null; }
        }
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
