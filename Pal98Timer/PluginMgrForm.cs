using HFrame.EX;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Pal98Timer
{
    public partial class PluginMgrForm : FormEx
    {
        private bool HasChanged = false;
        private readonly Func<TimerCore> currentCore;
        private SdlpalGeshi sdlpal;
        public PluginMgrForm() : this(null) { }
        public PluginMgrForm(Func<TimerCore> currentCore)
        {
            this.currentCore = currentCore;
            InitializeComponent();
            this.FormClosing += PluginMgrForm_FormClosing;
            RefreshPlugins();
        }

        private void PluginMgrForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (HasChanged)
            {
                Info("启用/禁用插件后需要重置计时器才能应用！", "别忘了~");
            }
        }

        public void RefreshPlugins()
        {
            dvPlugins.Rows.Clear();
            sdlpal = currentCore?.Invoke() as SdlpalGeshi;
            if (sdlpal != null)
            {
                var settings = sdlpal.ItemSettings;
                ShowBundledPlugin("富甲（金钱与道具）", true, settings.FujiaEnabled);
                ShowBundledPlugin("实时物品监测输出", false, settings.WatchEnabled);
            }
            string pluginPath = TimerPluginPackageInfo.GetPluginDir();
            if (!Directory.Exists(pluginPath)) return;
            DirectoryInfo root = new DirectoryInfo(pluginPath);
            FileInfo[] files = root.GetFiles();
            foreach (var f in files)
            {
                if (f.Name.EndsWith(".tpg"))
                {
                    string tpsfile = f.FullName;
                    if (File.Exists(tpsfile))
                    {
                        ShowOnePlugin(tpsfile);
                        try
                        {
                        }
                        catch
                        { }
                    }
                }
            }

        }
        private void ShowBundledPlugin(string name, bool fujia, bool enabled)
        {
            var row = new DataGridViewRow(); row.CreateCells(dvPlugins);
            row.Cells[0].Value = enabled; row.Cells[0].Tag = fujia;
            row.Cells[1].Value = name; row.Cells[2].Value = SdlpalGeshi.GameplayName;
            row.Cells[3].Value = "1（内置）"; row.Cells[4].Value = "随计时器签发";
            row.DefaultCellStyle.ForeColor = enabled ? Color.Green : Color.Black;
            row.Cells[1].ToolTipText = "使用 SDLPAL 只读快照；立即生效。监测清单在“实时物品检测 / 富甲设置”中配置。";
            dvPlugins.Rows.Add(row);
        }
        private void ShowOnePlugin(string tpgPath)
        {
            TimerPluginPackageInfo ti = new TimerPluginPackageInfo(tpgPath);
            DataGridViewRow dr = new DataGridViewRow();
            dr.CreateCells(dvPlugins);
            dr.Cells[0].Value = ti.Enable;
            dr.Cells[0].Tag = ti;
            dr.Cells[1].Value = ti.Des;
            dr.Cells[2].Value = ti.Core;
            dr.Cells[3].Value = ti.Version;
            dr.Cells[4].Value = (ti.IsOK ? "是" : (TimerPluginPackageInfo.IsPluginAuthorizationDisabled() ? "调试放行" : "否"));

            SetRowColor(dr, ti);

            dvPlugins.Rows.Add(dr);
        }

        private void dvPlugins_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.RowIndex < dvPlugins.RowCount && e.ColumnIndex == 0)
            {
                var row = dvPlugins.Rows[e.RowIndex];
                var cell = row.Cells[e.ColumnIndex];
                bool enable = (bool)cell.EditedFormattedValue;
                if (cell.Tag is bool)
                {
                    if (sdlpal == null || !ReferenceEquals(currentCore?.Invoke(), sdlpal)) { RefreshPlugins(); return; }
                    try
                    {
                        var settings = sdlpal.ItemSettings;
                        if ((bool)cell.Tag) settings.FujiaEnabled = enable; else settings.WatchEnabled = enable;
                        sdlpal.ApplyItemSettings(settings);
                        cell.Value = enable; row.DefaultCellStyle.ForeColor = enable ? Color.Green : Color.Black;
                    }
                    catch (Exception ex) { Error("物品插件设置保存失败：" + ex.Message); RefreshPlugins(); }
                    return;
                }
                TimerPluginPackageInfo ti = cell.Tag as TimerPluginPackageInfo;
                if (enable != ti.Enable)
                {
                    ti.Enable = enable;
                    cell.Value = ti.Enable;
                    SetRowColor(row, ti);
                    HasChanged = true;
                }
            }
        }

        private void SetRowColor(DataGridViewRow dr, TimerPluginPackageInfo ti)
        {
            Color fc = Color.Black;
            if (ti.Version != TimerPluginBase.TimerPlugin.Version.ToString())
            {
                fc = Color.Gray;
            }
            else if (!ti.IsOK)
            {
                fc = TimerPluginPackageInfo.IsPluginAuthorizationDisabled() ? Color.Green : Color.Red;
            }
            else if (ti.Enable)
            {
                fc = Color.Green;
            }
            dr.DefaultCellStyle.ForeColor = fc;
        }
    }
}
