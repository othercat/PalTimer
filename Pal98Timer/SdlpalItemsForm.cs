// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Pal98Timer
{
    internal sealed class SdlpalItemsForm : Form
    {
        private readonly Func<SdlpalItemView> read;
        private readonly Func<SdlpalItemSettings> settings;
        private readonly Action<SdlpalItemSettings> save;
        private readonly Action rebase;
        private readonly Func<string, string> translate;
        private readonly Timer refresh = new Timer { Interval = 250 };
        private readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, BackgroundColor = SystemColors.Window };
        private readonly TextBox search = new TextBox { Width = 140 };
        private readonly CheckBox onlyOwned = new CheckBox { AutoSize = true }, onlyWatched = new CheckBox { AutoSize = true },
            fujia = new CheckBox { AutoSize = true }, watch = new CheckBox { AutoSize = true };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(8, 4, 8, 4) };
        private readonly Label help = new Label { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(8, 4, 8, 4) };
        private readonly Label searchLabel = new Label { AutoSize = true, Padding = new Padding(0, 5, 0, 0) };
        private readonly Button baseline = new Button { AutoSize = true }, clear = new Button { AutoSize = true };
        private SdlpalItemView shown;
        private SdlpalItemSettings shownSettings;
        private bool changing;
        internal SdlpalItemsForm(Func<SdlpalItemView> read, Func<SdlpalItemSettings> settings, Action<SdlpalItemSettings> save,
            Action rebase, Func<string, string> translate)
        {
            this.read = read; this.settings = settings; this.save = save; this.rebase = rebase; this.translate = translate;
            AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1030, 560); MinimumSize = new Size(800, 420); StartPosition = FormStartPosition.CenterParent;
            var controls = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(8), AutoScroll = true, WrapContents = true };
            controls.Controls.AddRange(new Control[] { searchLabel, search, onlyOwned, onlyWatched, fujia, watch, baseline, clear });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Watched", Width = 52, SortMode = DataGridViewColumnSortMode.NotSortable });
            foreach (var column in new[] { new { Name="Id", Width=48 }, new { Name="Name", Width=140 }, new { Name="Backpack", Width=60 },
                new { Name="InUse", Width=70 }, new { Name="Equipped", Width=70 }, new { Name="Total", Width=60 },
                new { Name="Change", Width=90 }, new { Name="Price", Width=80 }, new { Name="Owners", Width=210 } })
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = column.Name, Width = column.Width, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns["Owners"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            Controls.Add(grid); Controls.Add(help); Controls.Add(status); Controls.Add(controls);
            grid.CurrentCellDirtyStateChanged += (sender, args) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValueChanged += (sender, args) => {
                if (changing || args.RowIndex < 0 || args.ColumnIndex != 0) return;
                int id = (int)grid.Rows[args.RowIndex].Tag;
                bool enabled = Convert.ToBoolean(grid.Rows[args.RowIndex].Cells[0].Value);
                Commit(value => { var ids = new HashSet<int>(value.WatchedIds); if (enabled) ids.Add(id); else ids.Remove(id); value.WatchedIds = ids.OrderBy(i => i).ToArray(); });
            };
            onlyOwned.CheckedChanged += (sender, args) => { if (!changing) Commit(value => value.OnlyOwned = onlyOwned.Checked); };
            onlyWatched.CheckedChanged += (sender, args) => { if (!changing) Commit(value => value.OnlyWatched = onlyWatched.Checked); };
            fujia.CheckedChanged += (sender, args) => { if (!changing) Commit(value => value.FujiaEnabled = fujia.Checked); };
            watch.CheckedChanged += (sender, args) => { if (!changing) Commit(value => value.WatchEnabled = watch.Checked); };
            search.TextChanged += (sender, args) => RefreshRows(true);
            baseline.Click += (sender, args) => { rebase(); RefreshRows(true); };
            clear.Click += (sender, args) => Commit(value => value.WatchedIds = new int[0]);
            refresh.Tick += (sender, args) => RefreshRows(false);
            FormClosed += (sender, args) => refresh.Stop();
            RefreshLanguage(); RefreshRows(true); refresh.Start();
        }
        internal void RefreshLanguage()
        {
            Text = SdlpalGeshi.GameplayName + " - " + translate("实时物品检测");
            searchLabel.Text = translate("名称 / 编号"); onlyOwned.Text = translate("仅持有"); onlyWatched.Text = translate("仅监测");
            fujia.Text = translate("启用富甲（金钱与道具）"); watch.Text = translate("输出监测物品到主窗口 / OBS / 云");
            baseline.Text = translate("重建观察基线"); clear.Text = translate("清空监测选择");
            string[] titles = { "监测", "编号", "物品", "背包", "占用", "队伍装备", "合计", "本段净变化", "资源价格", "装备角色" };
            for (int i = 0; i < titles.Length; i++) grid.Columns[i].HeaderText = translate(titles[i]);
            help.Text = translate("道具种类 = 背包与当前队伍全部装备的去重种类；数量包含战斗占用，未入队角色装备不计入。\n资源价格来自当前物品目录，不折算成现金。净变化只表示持有变化；读档、重连和重置会重建基线。选择和开关自动保存。");
            shown = null;
        }
        private void Commit(Action<SdlpalItemSettings> change)
        {
            try { var value = settings(); change(value); save(value); }
            catch (Exception ex) { MessageBox.Show(this, translate("物品配置保存失败：") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            // A watch removal can also remove the edited row in "only watched"
            // mode. Wait until the DataGridView commit has unwound before filtering.
            if (IsHandleCreated) BeginInvoke(new Action(() => { if (!IsDisposed) RefreshRows(true); }));
            else RefreshRows(true);
        }
        internal void RefreshRows(bool force)
        {
            var view = read(); var config = settings();
            bool settingsChanged = shownSettings == null || SdlpalBridge.Json().Serialize(config) != SdlpalBridge.Json().Serialize(shownSettings);
            if (!force && ReferenceEquals(view, shown) && !settingsChanged) return;
            shown = view; shownSettings = config;
            var watched = new HashSet<int>(config.WatchedIds);
            string query = search.Text.Trim();
            var rows = view.Rows.Where(row => (!config.OnlyOwned || row.Total > 0 || watched.Contains(row.Id)) &&
                (!config.OnlyWatched || watched.Contains(row.Id)) && (query.Length == 0 || row.Id.ToString() == query ||
                translate(row.Name).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)).ToArray();
            changing = true;
            try
            {
                onlyOwned.Checked = config.OnlyOwned; onlyWatched.Checked = config.OnlyWatched; fujia.Checked = config.FujiaEnabled; watch.Checked = config.WatchEnabled;
                // Preserve the selected row and scroll position while numbers change.
                bool rebuild = grid.Rows.Count != rows.Length || grid.Rows.Cast<DataGridViewRow>().Where((row, index) => (int)row.Tag != rows[index].Id).Any();
                int first = grid.FirstDisplayedScrollingRowIndex; int? selected = grid.CurrentRow?.Tag as int?;
                if (rebuild) grid.Rows.Clear();
                for (int i = 0; i < rows.Length; i++)
                {
                    var row = rows[i];
                    if (rebuild) { int index = grid.Rows.Add(); grid.Rows[index].Tag = row.Id; }
                    object[] values = { watched.Contains(row.Id), row.Id, translate(row.Name), view.Available ? (object)row.Backpack : "—",
                        view.Available ? (object)row.InUse : "—", view.Available ? (object)row.Equipped : "—", view.Available ? (object)row.Total : "—",
                        view.Available ? row.NetChange.ToString("+0;-0;0", CultureInfo.InvariantCulture) : "—", row.ResourcePrice, translate(row.Owners) };
                    grid.Rows[i].SetValues(values);
                    grid.Rows[i].DefaultCellStyle.ForeColor = !view.Available ? Color.Gray : row.NetChange > 0 ? Color.DarkGreen : row.NetChange < 0 ? Color.Firebrick : SystemColors.ControlText;
                    if (rebuild && selected == row.Id) grid.CurrentCell = grid.Rows[i].Cells[2];
                }
                if (rebuild && first >= 0 && first < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = first;
                status.Text = translate(view.Status + (view.Available ? "  金钱 " + view.Cash + "  道具 " + view.Kinds + " 种 / " + view.Quantity + " 件  读档代数 " + view.Epoch : "") +
                    "\n监测 " + config.WatchedIds.Length + " 项；当前显示 " + rows.Length + " 项。未持有的监测项保留显示。");
            }
            finally { changing = false; }
        }
        protected override void Dispose(bool disposing) { if (disposing) refresh.Dispose(); base.Dispose(disposing); }
    }
}
