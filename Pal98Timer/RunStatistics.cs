// Refs othercat/PalTimer#63. Shared wire model; never decides timing/ranking.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace Pal98Timer
{
    internal sealed class PlayerDeathStatistic
    {
        public int role_id { get; set; }
        public long deaths { get; set; }
    }
    internal sealed class FinalRunStatistics
    {
        public string schema { get; set; } = "PAL.RunStatistics.v1";
        public long? final_money { get; set; }
        public PlayerDeathStatistic[] player_deaths { get; set; } = new PlayerDeathStatistic[0];
        public long? battle_defeats { get; set; }
        public string counter_source { get; set; } = "unavailable";
        public bool complete { get; set; }
        internal bool Valid => schema == "PAL.RunStatistics.v1" &&
            (!final_money.HasValue || final_money >= -4294967295L && final_money <= 4294967295L) &&
            (!battle_defeats.HasValue || battle_defeats >= 0) &&
            new[] { "unavailable", "sdlpal-events-v1", "pal98-events-v1", "mixed-events-v1" }.Contains(counter_source) &&
            player_deaths != null && player_deaths.Length <= 16 &&
            player_deaths.All(p => p != null && p.role_id >= 0 && p.role_id < 16 && p.deaths >= 0) &&
            player_deaths.Select(p => p.role_id).Distinct().Count() == player_deaths.Length &&
            (counter_source != "unavailable" || player_deaths.Length == 0 && !battle_defeats.HasValue) &&
            (!complete || final_money.HasValue && battle_defeats.HasValue && counter_source != "unavailable");
        internal FinalRunStatistics Copy() => Parse(Json().Serialize(this));
        internal static JavaScriptSerializer Json() => new JavaScriptSerializer { MaxJsonLength = 32768, RecursionLimit = 16 };
        internal static FinalRunStatistics Parse(string text)
        {
            if (text == null || text.Length > 16384) throw new ArgumentException("Invalid run statistics size.");
            var result = Json().Deserialize<FinalRunStatistics>(text);
            if (result == null || !result.Valid) throw new ArgumentException("Invalid run statistics.");
            return result;
        }
    }
    internal sealed class RunStatisticsSample
    {
        public string key { get; set; }
        public string source { get; set; }
        public long[] deaths { get; set; }
        public long defeats { get; set; }
        public uint controlled { get; set; }
        public uint gaps { get; set; }
        public long? money { get; set; }
        public bool settled { get; set; } = true;
        internal bool Valid => !string.IsNullOrEmpty(key) && key.Length <= 160 &&
            (source == "sdlpal-events-v1" || source == "pal98-events-v1") &&
            deaths != null && deaths.Length > 0 && deaths.Length <= 16 && deaths.All(v => v >= 0) &&
            defeats >= 0 && (controlled >> deaths.Length) == 0 &&
            (!money.HasValue || money >= -4294967295L && money <= 4294967295L);
    }
    internal sealed class RunStatisticsState
    {
        public int version { get; set; } = 1;
        public long[] deaths { get; set; } = new long[16];
        public long defeats { get; set; }
        public uint controlled { get; set; }
        public bool begun { get; set; }
        public bool complete { get; set; } = true;
        public string source { get; set; } = "unavailable";
        public string lineage { get; set; } = Guid.NewGuid().ToString("N");
        public RunStatisticsSample last { get; set; }
    }
    internal sealed class RunStatisticsAccumulator
    {
        private readonly object gate = new object();
        private RunStatisticsState state = new RunStatisticsState();
        private FinalRunStatistics frozen;
        private long? missingFinalSince;
        private readonly Func<long> ticks;
        internal RunStatisticsAccumulator() : this(Stopwatch.GetTimestamp) { }
        internal RunStatisticsAccumulator(Func<long> clock) { ticks = clock; }
        internal bool FinalReady { get { lock (gate) return frozen != null; } }
        internal void Reset() { lock (gate) { state = new RunStatisticsState(); frozen = null; missingFinalSince = null; } }
        internal void Imported() { lock (gate) { state.complete = false; frozen = null; missingFinalSince = null; } }
        private static long Add(long a, long b) => b > long.MaxValue - a ? long.MaxValue : a + b;
        private void MissingFinal(bool finished, bool sourceEnded)
        {
            if (!finished) return;
            if (!missingFinalSince.HasValue) missingFinalSince = ticks();
            if (state.last != null && !sourceEnded && ticks() - missingFinalSince.Value < 5 * Stopwatch.Frequency) return;
            state.complete = false; frozen = Result(null);
        }
        internal void Observe(RunStatisticsSample sample, bool started, bool finished, bool sourceEnded = false)
        {
            lock (gate)
            {
                if (frozen != null) {
                    if (finished) return;
                    // A resumed/imported timeline is a new observation segment;
                    // its already copied, signed completion remains immutable.
                    frozen = null; state.complete = false;
                }
                if (sample == null || !sample.Valid)
                {
                    MissingFinal(finished, sourceEnded);
                    return; // cumulative counters recover an ordinary read interruption
                }
                if (!started && !state.begun) { state.last = sample; missingFinalSince = null; return; }
                var previous = state.last;
                bool same = previous != null && previous.key == sample.key && previous.deaths.Length == sample.deaths.Length;
                if (!state.begun)
                {
                    state.begun = true;
                    // Attaching late cannot certify deaths that preceded this run.
                    if (!same && (sample.gaps != 0 || sample.defeats != 0 || sample.deaths.Any(n => n != 0))) state.complete = false;
                    if (!same) previous = sample;
                }
                else if (!same)
                {
                    // A restarted process has its own zero origin. Preserve the
                    // previous run totals, include its new events, flag the gap.
                    state.complete = false;
                    previous = null;
                }
                if (previous != null && (sample.defeats < previous.defeats ||
                    sample.deaths.Where((n, i) => n < previous.deaths[i]).Any()))
                {
                    // Capture/export threads may deliver an older snapshot.
                    // Never rewind a processed monotonic cursor and recount it.
                    state.complete = false; MissingFinal(finished, sourceEnded); return;
                }
                for (int role = 0; role < sample.deaths.Length; ++role)
                    state.deaths[role] = Add(state.deaths[role], sample.deaths[role] - (previous?.deaths[role] ?? 0));
                state.defeats = Add(state.defeats, sample.defeats - (previous?.defeats ?? 0));
                if (sample.gaps != (previous?.gaps ?? 0)) state.complete = false;
                state.controlled |= sample.controlled;
                state.source = state.source == "unavailable" || state.source == sample.source ? sample.source : "mixed-events-v1";
                state.last = sample;
                missingFinalSince = null;
                if (finished && sample.settled) frozen = Result(sample.money);
            }
        }
        private FinalRunStatistics Result(long? money) => new FinalRunStatistics {
            final_money = money,
            player_deaths = Enumerable.Range(0, 16).Where(i => (state.controlled & (1u << i)) != 0 || state.deaths[i] != 0)
                .Select(i => new PlayerDeathStatistic { role_id = i, deaths = state.deaths[i] }).ToArray(),
            battle_defeats = state.source == "unavailable" ? (long?)null : state.defeats,
            counter_source = state.source,
            complete = state.complete && state.begun && money.HasValue && state.source != "unavailable" && state.last?.settled != false
        };
        internal FinalRunStatistics Snapshot() { lock (gate) return (frozen ?? Result(state.last?.money)).Copy(); }
        internal string Save() { lock (gate) return FinalRunStatistics.Json().Serialize(state); }
        internal void Restore(string text)
        {
            lock (gate)
            {
                state.complete = false; frozen = null; missingFinalSince = null;
                if (string.IsNullOrEmpty(text)) return;
                if (text.Length > 16384) throw new ArgumentException("Invalid saved run statistics size.");
                var saved = FinalRunStatistics.Json().Deserialize<RunStatisticsState>(text);
                if (saved == null || saved.version != 1 || saved.deaths == null || saved.deaths.Length != 16 ||
                    saved.deaths.Any(v => v < 0) || saved.defeats < 0 || (saved.controlled >> 16) != 0 ||
                    saved.last != null && !saved.last.Valid ||
                    saved.source == "unavailable" && (saved.defeats != 0 || saved.controlled != 0 || saved.deaths.Any(v => v != 0)) ||
                    !new[] { "unavailable", "sdlpal-events-v1", "pal98-events-v1", "mixed-events-v1" }.Contains(saved.source) ||
                    !Guid.TryParseExact(saved.lineage, "N", out var lineage))
                    throw new ArgumentException("Invalid saved run statistics.");
                var current = state.last;
                bool same = current != null && saved.last != null && current.key == saved.last.key && current.deaths.Length == saved.last.deaths.Length;
                bool newProcessHistory = current != null && saved.last != null && !same && state.lineage != saved.lineage;
                for (int i = 0; i < 16; ++i) {
                    long delta = same && i < current.deaths.Length ? Math.Max(0, current.deaths[i] - saved.last.deaths[i]) :
                        newProcessHistory && i < current.deaths.Length ? current.deaths[i] : 0;
                    state.deaths[i] = Math.Max(state.deaths[i], Add(saved.deaths[i], delta));
                }
                long losses = same ? Math.Max(0, current.defeats - saved.last.defeats) : newProcessHistory ? current.defeats : 0;
                state.defeats = Math.Max(state.defeats, Add(saved.defeats, losses));
                state.controlled |= saved.controlled;
                state.begun |= saved.begun;
                if (same) state.last = new RunStatisticsSample {
                    key=current.key, source=current.source, money=current.money, settled=current.settled,
                    controlled=current.controlled|saved.last.controlled, gaps=Math.Max(current.gaps,saved.last.gaps),
                    defeats=Math.Max(current.defeats,saved.last.defeats),
                    deaths=current.deaths.Select((v,i)=>Math.Max(v,saved.last.deaths[i])).ToArray()
                };
                else if (current == null) state.last = saved.last;
                if (state.source == "unavailable") state.source = saved.source;
                else if (saved.source != "unavailable" && saved.source != state.source) state.source = "mixed-events-v1";
                state.lineage = saved.lineage;
            }
        }
    }
    internal static class NativeRunStatisticsReader
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] value, UIntPtr count, out UIntPtr read);
        internal static RunStatisticsSample Read(Process process)
        {
            if (process == null) return null;
            try
            {
                int pid = process.Id;
                ulong created = (ulong)process.StartTime.ToUniversalTime().ToFileTimeUtc();
                using (var mapping = MemoryMappedFile.OpenExisting("Local\\PAL98.RunStatistics.v1." + pid, MemoryMappedFileRights.Read))
                using (var view = mapping.CreateViewAccessor(0, 256, MemoryMappedFileAccess.Read))
                {
                    for (int attempt = 0; attempt < 3; ++attempt)
                    {
                        int sequence = view.ReadInt32(16);
                        if ((sequence & 1) != 0) continue;
                        byte[] bytes = new byte[256]; view.ReadArray(0, bytes, 0, bytes.Length);
                        System.Threading.Thread.MemoryBarrier();
                        if (sequence != view.ReadInt32(16) || sequence != BitConverter.ToInt32(bytes, 16)) continue;
                        if (Encoding.ASCII.GetString(bytes, 0, 8) != "PALSTAT1" || BitConverter.ToUInt32(bytes, 8) != 1 ||
                            BitConverter.ToUInt32(bytes, 12) != 256 || BitConverter.ToInt32(bytes, 20) != pid ||
                            BitConverter.ToUInt64(bytes, 24) != created || (BitConverter.ToUInt32(bytes, 48) & 7) != 7) return null;
                        int roles = BitConverter.ToInt32(bytes, 52);
                        if (roles < 6 || roles > 16) return null;
                        uint runtime = BitConverter.ToUInt32(bytes, 212);
                        byte[] money = new byte[4]; UIntPtr read;
                        long? cash = runtime != 0 && ReadProcessMemory(process.Handle, new IntPtr((long)runtime + 0x2B4), money,
                            new UIntPtr(4), out read) && read.ToUInt64() == 4 ? (long?)BitConverter.ToUInt32(money, 0) : null;
                        var sample = new RunStatisticsSample {
                            key = "pal98:" + pid + ":" + created + ":" + BitConverter.ToUInt64(bytes, 32), source = "pal98-events-v1",
                            controlled = BitConverter.ToUInt32(bytes, 56), gaps = BitConverter.ToUInt32(bytes, 60),
                            defeats = BitConverter.ToInt64(bytes, 192), money = cash,
                            settled = BitConverter.ToUInt32(bytes, 216) == 0,
                            deaths = Enumerable.Range(0, roles).Select(i => BitConverter.ToInt64(bytes, 64 + 8 * i)).ToArray()
                        };
                        return sample.Valid ? sample : null;
                    }
                }
            }
            catch { } // missing/older producer never prevents local timing
            return null;
        }
    }
}
