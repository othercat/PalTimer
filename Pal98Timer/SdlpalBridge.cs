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
        public string resourceHash { get; set; }
        public Dictionary<string, string> resourceHashes { get; set; }
        public string build { get; set; }
        public string exeHash { get; set; }
        public string gamePath { get; set; }
        public string savePath { get; set; }
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
        // Keep IdentityKey below strict for in-flight observation/relay safety.
        internal Dictionary<string, string> RankingFacts()
        {
            var facts = new Dictionary<string, string>(StringComparer.Ordinal) {
                ["family"] = "sdlpal-geshi-easy", ["profile"] = profile,
                ["content"] = CompetitionProtocol.Hash(string.Join("\n", resourceHashes.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value))),
                ["fade_ms"] = fadeCycleMs.ToString(System.Globalization.CultureInfo.InvariantCulture), ["map_speed_ticks"] = "0",
                ["walk_frame_ms"] = walkFrameMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["speed"] = speed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["battle_speed"] = battleSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            foreach (var pair in resourceHashes) facts["resource." + pair.Key] = pair.Value;
            return facts;
        }
        internal string IdentityKey => profile + "|" + exeHash + "|" + build + "|" +
            string.Join("|", resourceHashes.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + x.Value)) +
            "|" + walkFrameMs + "|" + fadeCycleMs + "|" + speed + "|" + battleSpeed;
    }

    // No OpenProcess/RPM/WPM, injection or PALDLL addresses. A mapping may only
    // identify its actual live owner; callers cannot use a PID from its JSON.
    internal sealed class SdlpalBridge : IDisposable
    {
        internal const int Capacity = 4194304, HeaderSize = 64;
        internal const string Prefix = "Local\\SDLPAL.LostEdit.v1.";
        private readonly object gate = new object();
        private MemoryMappedFile map;
        private MemoryMappedViewAccessor view;
        internal Process Process { get; private set; }
        internal ulong Created { get; private set; }
        internal string ExeHash { get; private set; }
        internal string Error { get; private set; } = "等待 SDLPAL 隔世追凶简单版";
        [DllImport("kernel32.dll")] internal static extern ulong GetTickCount64();
        internal static bool Digest(string text) => text != null && text.Length == 64 && text.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        internal static JavaScriptSerializer Json() => new JavaScriptSerializer { MaxJsonLength = Capacity, RecursionLimit = 32 };
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
                throw new InvalidDataException("SDLPAL 快照头或进程身份无效");
            ulong published = BitConverter.ToUInt64(header, 56);
            if (published > now || now - published > 2000) throw new InvalidDataException("SDLPAL 快照已过期");
            string json = new UTF8Encoding(false, true).GetString(payload);
            var serializer = Json();
            var fields = serializer.Deserialize<Dictionary<string, object>>(json);
            string[] required = { "schema", "profile", "verified", "diagnostic", "resourceHash", "resourceHashes", "build", "exeHash",
                "gamePath", "savePath", "started", "focused", "busy", "canControl", "completed", "epoch", "frame", "loadedFrom",
                "speed", "battleSpeed", "walkFrameMs", "fadeCycleMs", "scene", "map", "battle", "battleEvents" };
            if (fields == null || required.Any(k => !fields.ContainsKey(k))) throw new InvalidDataException("SDLPAL 快照缺少必要字段");
            var value = serializer.Deserialize<SdlpalSnapshot>(json);
            if (value.schema != 1 || value.profile != "geshi-easy-v1" || !value.verified || !Digest(value.resourceHash) || !Digest(value.exeHash) ||
                value.resourceHashes == null || new[] { "SSS.MKF", "DATA.MKF", "WORD.DAT", "M.MSG" }.Any(k => !value.resourceHashes.ContainsKey(k) || !Digest(value.resourceHashes[k])) ||
                value.resourceHashes["SSS.MKF"] != value.resourceHash || string.IsNullOrWhiteSpace(value.build) || value.build.Length > 128 ||
                value.build.Any(char.IsControl) || !AbsolutePath(value.gamePath) || !AbsolutePath(value.savePath) ||
                value.epoch != BitConverter.ToUInt64(header, 40) || value.battle == null || value.battleEvents == null || value.battleEvents.Length > 64 ||
                value.walkFrameMs < 0 || value.walkFrameMs > 1000000 || value.fadeCycleMs < 0 || value.fadeCycleMs > 86400000 ||
                value.speed < 0 || value.speed > 100 || value.battleSpeed < 0 || value.battleSpeed > 100 ||
                value.battleEvents.Any(e => e == null || e.epoch != value.epoch || e.enemies == null || e.enemies.Length > 16))
                throw new InvalidDataException("SDLPAL 资源或状态身份无效");
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
            error = "等待 SDLPAL 隔世追凶简单版";
            SdlpalBridge found = null;
            foreach (var process in System.Diagnostics.Process.GetProcesses())
            {
                var candidate = new SdlpalBridge();
                try
                {
                    candidate.Attach(process);
                    if (found != null) { found.Dispose(); candidate.Dispose(); error = "检测到多个 SDLPAL 游戏，请只保留一个"; return null; }
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
