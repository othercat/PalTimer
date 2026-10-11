using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Pal98Timer
{
    internal sealed class LocalTimelineConfigurationForm : Form
    {
        internal LocalTimelineConfiguration Selected { get; private set; }
        private readonly ComboBox choices = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly string route;
        internal LocalTimelineConfigurationForm(string route, LocalTimelineConfiguration available)
        {
            this.route = route;
            Text = "选择本地最佳线配置"; ClientSize = new Size(610, 278);
            StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "选择本局实际使用的配置，保存已有节点和时间作为本地参考。\r\n选择不会重置计时，也不会取得联网排名或比赛资格。玩法不同请新建配置归属。\r\n无法关联到当前游戏的配置文件会标记为“文件参考”，重开后可在此再次选用。" });
            layout.Controls.Add(choices);
            var sources = new FlowLayoutPanel { Dock = DockStyle.Fill };
            var browse = new Button { Text = "选择配置文件…", AutoSize = true };
            sources.Controls.Add(browse); layout.Controls.Add(sources);
            var create = new FlowLayoutPanel { Dock = DockStyle.Fill };
            var name = new TextBox { Width = 238, MaxLength = 160 };
            var hardcore = new CheckBox { Text = "硬核", AutoSize = true };
            var add = new Button { Text = "新建本地配置归属", AutoSize = true };
            create.Controls.Add(name); create.Controls.Add(hardcore); create.Controls.Add(add); layout.Controls.Add(create);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
            var select = new Button { Text = "使用所选配置", AutoSize = true };
            actions.Controls.Add(cancel); actions.Controls.Add(select); layout.Controls.Add(actions); Controls.Add(layout);
            CancelButton = cancel; AcceptButton = select;
            if (available != null) Add(available);
            foreach (var choice in LocalTimelineConfiguration.Known(route)) Add(choice);
            if (choices.Items.Count > 0) choices.SelectedIndex = 0;
            add.Click += (sender, args) => {
                if (string.IsNullOrWhiteSpace(name.Text)) { MessageBox.Show(this, "请输入能区分玩法的配置名称或版本。"); return; }
                Add(LocalTimelineConfiguration.Manual(name.Text, route, hardcore.Checked));
            };
            browse.Click += (sender, args) => {
                using (var dialog = new OpenFileDialog { Filter = "仙剑98配置|*.pal98config", Title = "选择本局使用的配置" }) {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    try { Add(LocalConfigurationReader.FromFile(dialog.FileName, route)); }
                    catch (Exception error) when (LocalConfigurationReader.ReadError(error)) { MessageBox.Show(this, "配置未读取：" + error.Message); }
                }
            };
            select.Click += (sender, args) => {
                if (!(choices.SelectedItem is LocalTimelineConfiguration selected)) { MessageBox.Show(this, "请先选择或新建本地配置归属。"); return; }
                Selected = selected; DialogResult = DialogResult.OK; Close();
            };
        }
        private void Add(LocalTimelineConfiguration value)
        {
            var existing = choices.Items.Cast<LocalTimelineConfiguration>().FirstOrDefault(p => p.key == value.key);
            if (existing == null) { choices.Items.Add(value); choices.SelectedItem = value; }
            else choices.SelectedItem = existing;
        }
    }
}
