// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TimerPluginBase;

namespace Pal98Timer
{
    internal sealed class SdlpalItemSettings
    {
        internal const string FileName = "sdlpal_geshi_items.json";
        public int Version { get; set; } = 1;
        public bool FujiaEnabled { get; set; } = true;
        public bool WatchEnabled { get; set; } = true;
        public bool OnlyOwned { get; set; } = true;
        public bool OnlyWatched { get; set; }
        public int[] WatchedIds { get; set; } = new int[0];
        internal void Validate()
        {
            if (Version != 1 || WatchedIds == null || WatchedIds.Length > 700 ||
                WatchedIds.Any(id => id <= 0 || id >= 700) || WatchedIds.Distinct().Count() != WatchedIds.Length)
                throw new InvalidDataException("物品监测配置版本或物品编号无效");
        }
        internal SdlpalItemSettings Copy() => new SdlpalItemSettings { Version = Version, FujiaEnabled = FujiaEnabled,
            WatchEnabled = WatchEnabled, OnlyOwned = OnlyOwned, OnlyWatched = OnlyWatched, WatchedIds = WatchedIds.ToArray() };
        internal static SdlpalItemSettings Load()
        {
            string path = TimerUserSettings.GetPath(FileName);
            if (!File.Exists(path)) return new SdlpalItemSettings();
            if (new FileInfo(path).Length > 32768) throw new InvalidDataException("物品监测配置过大");
            var value = SdlpalBridge.Json().Deserialize<SdlpalItemSettings>(File.ReadAllText(path, Encoding.UTF8));
            if (value == null) throw new InvalidDataException("物品监测配置为空");
            value.Validate(); return value;
        }
        internal void Save()
        {
            Validate();
            TimerUserSettings.WriteText(FileName, SdlpalBridge.Json().Serialize(this), new UTF8Encoding(false));
        }
    }

    internal sealed class SdlpalItemRow
    {
        internal readonly int Id, Backpack, InUse, Equipped, NetChange, ResourcePrice;
        internal readonly string Name, Owners;
        internal int Total => Backpack + Equipped;
        internal SdlpalItemRow(int id, string name, int backpack, int inUse, int equipped, int change, string owners, int price)
        { Id = id; Name = name; Backpack = backpack; InUse = inUse; Equipped = equipped; NetChange = change; Owners = owners; ResourcePrice = price; }
    }

    internal sealed class SdlpalItemView
    {
        internal readonly bool Available;
        internal readonly long Cash;
        internal readonly ulong Epoch;
        internal readonly SdlpalItemRow[] Rows;
        internal readonly string Status;
        internal int Kinds => Rows.Count(row => row.Total > 0);
        internal long Quantity => Rows.Sum(row => (long)row.Total);
        internal SdlpalItemView(bool available, long cash, ulong epoch, SdlpalItemRow[] rows, string status)
        { Available = available; Cash = cash; Epoch = epoch; Rows = rows; Status = status; }
        internal static SdlpalItemView Waiting(string message, SdlpalItemRow[] rows = null) =>
            new SdlpalItemView(false, 0, 0, rows ?? new SdlpalItemRow[0], message);
    }

    // This observes ownership, not obtain/use events: transfers to/from absent
    // party members and scripts can change totals too. No PAL98 IDs or addresses.
    internal sealed class SdlpalItemTracker
    {
        private Dictionary<int, int> baseline;
        private int pid;
        private ulong created, epoch;
        private string identity;
        internal SdlpalItemView View { get; private set; } = SdlpalItemView.Waiting("等待游戏物品数据");
        internal static string Validate(SdlpalSnapshot s)
        {
            if (!s.started) return "";
            if (s.cash < 0 || s.cash > uint.MaxValue || s.inventory == null || s.inventory.Length > 256 ||
                s.catalog == null || s.catalog.Length != 700 || s.itemIds == null || s.itemIds.Length > 700 ||
                s.roles == null || s.roles.Length != 6 || s.party == null || s.party.Length > 5)
                return "游戏物品数据缺失或超出范围";
            if (s.catalog.Any(c => c == null || c.id < 0 || c.id >= 700 || c.name == null || c.name.Length > 256 ||
                    c.name.Any(char.IsControl) || c.data == null || c.data.Length != 7 || c.data.Any(w => w < 0 || w > ushort.MaxValue)) ||
                s.catalog.Select(c => c.id).Distinct().Count() != 700 ||
                s.itemIds.Any(id => id <= 0 || id >= 700) || s.itemIds.Distinct().Count() != s.itemIds.Length)
                return "游戏物品目录无效";
            if (s.roles.Any(r => r == null || r.id < 0 || r.id >= 6 || r.name == null || r.name.Length > 256 || r.name.Any(char.IsControl) ||
                    r.equipment == null || r.equipment.Length != 6 || r.equipment.Any(id => id < 0 || id >= 700)) ||
                s.roles.Select(r => r.id).Distinct().Count() != 6 || s.party.Any(id => id < 0 || id >= 6) ||
                s.party.Distinct().Count() != s.party.Length)
                return "游戏队伍或装备数据无效";
            if (s.inventory.Any(i => i == null || i.slot < 0 || i.slot >= 256 || i.id <= 0 || i.id >= 700 ||
                    i.amount < 0 || i.amount > ushort.MaxValue || i.inUse < 0 || i.inUse > i.amount) ||
                s.inventory.Select(i => i.slot).Distinct().Count() != s.inventory.Length)
                return "游戏背包数据无效";
            return "";
        }
        internal void Unavailable(string reason)
        {
            baseline = null;
            View = SdlpalItemView.Waiting(reason, View.Rows);
        }
        internal void Reset() { baseline = null; View = SdlpalItemView.Waiting("等待新的物品观察基线", View.Rows); }
        internal void Observe(SdlpalSnapshot s)
        {
            if (!s.started) { Unavailable("等待新游戏或读档"); return; }
            string error = s.ItemDataError ?? Validate(s);
            if (!string.IsNullOrEmpty(error)) { Unavailable(error); return; }
            var names = s.catalog.ToDictionary(c => c.id);
            var ids = new HashSet<int>(s.itemIds);
            var stock = new Dictionary<int, int>(); var reserved = new Dictionary<int, int>();
            var equipment = new Dictionary<int, List<string>>();
            foreach (var i in s.inventory)
            {
                ids.Add(i.id); stock[i.id] = Get(stock, i.id) + i.amount; reserved[i.id] = Get(reserved, i.id) + i.inUse;
            }
            foreach (var role in s.roles.Where(r => s.party.Contains(r.id)))
                foreach (int id in role.equipment.Where(id => id != 0))
                {
                    ids.Add(id);
                    if (!equipment.ContainsKey(id)) equipment[id] = new List<string>();
                    equipment[id].Add(string.IsNullOrEmpty(role.name) ? "角色 " + role.id : role.name);
                }
            var totals = ids.ToDictionary(id => id, id => Get(stock, id) + (equipment.ContainsKey(id) ? equipment[id].Count : 0));
            bool changed = baseline == null || pid != s.Pid || created != s.Created || epoch != s.epoch || identity != s.ResourceEngineIdentity;
            if (changed) baseline = new Dictionary<int, int>(totals);
            foreach (int id in ids) if (!baseline.ContainsKey(id)) baseline[id] = 0;
            pid = s.Pid; created = s.Created; epoch = s.epoch; identity = s.ResourceEngineIdentity;
            // Keep previously observed IDs visible when their last item is lost.
            ids.UnionWith(baseline.Keys);
            var rows = ids.OrderBy(id => id).Select(id => new SdlpalItemRow(id,
                string.IsNullOrWhiteSpace(names[id].name) ? "未命名物品 " + id : names[id].name,
                Get(stock, id), Get(reserved, id), equipment.ContainsKey(id) ? equipment[id].Count : 0,
                Get(totals, id) - Get(baseline, id), equipment.ContainsKey(id) ? string.Join("、", equipment[id]) : "",
                names[id].data[1])).ToArray();
            View = new SdlpalItemView(true, s.cash, s.epoch, rows, changed ? "已建立观察基线" : "实时物品数据");
        }
        private static int Get(Dictionary<int, int> values, int id) { int value; return values.TryGetValue(id, out value) ? value : 0; }
    }

    // Bundled with the host and therefore covered by the host's release hash.
    // This does not bypass the signature policy for external .tpg packages.
    internal sealed class SdlpalItemPlugin : TimerPlugin
    {
        internal const string EventName = "SDLPAL.GeshiItems.v1";
        private readonly EPluginPosition position;
        private volatile string result = "";
        internal SdlpalItemPlugin(EPluginPosition position) { this.position = position; }
        public override void OnLoad() { result = ""; }
        public override void OnUnload() { result = ""; }
        public override EPluginPosition GetPosition() => position;
        public override string GetResult() => result;
        public override void OnEvent(string name, object data)
        {
            if (name != EventName) return;
            var text = data as SdlpalItemPluginText;
            if (text != null) result = position == EPluginPosition.BR ? text.Fujia : text.Watch;
        }
    }

    internal sealed class SdlpalItemPluginText
    {
        internal readonly string Fujia, Watch;
        internal SdlpalItemPluginText(SdlpalItemView view, SdlpalItemSettings settings, Func<string, string> translate)
        {
            Fujia = !settings.FujiaEnabled ? "" : translate(!view.Available ? "富甲：游戏数据不可用" :
                "钱" + view.Cash.ToString(CultureInfo.InvariantCulture) + " 道具" + view.Kinds + "种 / " + view.Quantity + "件");
            if (!settings.WatchEnabled || settings.WatchedIds.Length == 0) { Watch = ""; return; }
            if (!view.Available) { Watch = translate("物品监测：游戏数据不可用"); return; }
            var rows = view.Rows.ToDictionary(row => row.Id);
            Watch = translate("监测 " + string.Join("  ", settings.WatchedIds.Select(id => {
                SdlpalItemRow row;
                return rows.TryGetValue(id, out row) ? row.Name + "×" + row.Total +
                    (row.NetChange == 0 ? "" : "(" + row.NetChange.ToString("+0;-0", CultureInfo.InvariantCulture) + ")") : "#" + id + " 未知物品";
            })));
        }
    }
}
