using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Pal98Timer
{
    internal sealed class CompetitionClockWarningForm : Form
    {
        private readonly Label message;
        internal CompetitionClockWarningForm(string text)
        {
            Text = "联机连接失败：时间校验异常";
            TopMost = true; ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 10F);
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            var layout = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1 };
            message = new Label { AutoSize = true, MaximumSize = new Size(540, 0), ForeColor = Color.Firebrick,
                Margin = new Padding(3, 3, 3, 16), Text = text };
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var settings = new Button { Name = "btnClockSettings", Text = "打开日期和时间设置", AutoSize = true };
            var dismiss = new Button { Text = "知道了", AutoSize = true };
            settings.Click += delegate {
                try { Process.Start(new ProcessStartInfo("ms-settings:dateandtime") { UseShellExecute = true }); }
                catch {
                    try { Process.Start(new ProcessStartInfo("control.exe", "timedate.cpl") { UseShellExecute = true }); }
                    catch { message.Text = text + "\r\n无法直接打开设置，请手动进入 Windows 日期和时间设置。"; }
                }
            };
            dismiss.Click += delegate { Close(); };
            buttons.Controls.AddRange(new Control[] { settings, dismiss });
            layout.Controls.Add(message, 0, 0); layout.Controls.Add(buttons, 0, 1); Controls.Add(layout);
            AcceptButton = CancelButton = dismiss;
        }
        internal void UpdateMessage(string text) { if (message.Text != text) message.Text = text; }
    }

    internal sealed class CompetitionSettingsForm : Form
    {
        private readonly CompetitionClient client;
        private readonly CheckBox overlay, transparent, editable;
        private readonly TextBox secret;
        private readonly Label status, hwid, activation, source, fontName;
        private readonly NumericUpDown fontSize;
        private readonly ComboBox alignment, board, rankingScope;
        private readonly Button color, copyDeviceId;
        private readonly Timer refresh;
        private string selectedFont;
        private Color selectedColor;
        internal CompetitionSettingsForm(CompetitionClient client)
        {
            this.client = client;
            Text = "联机与排名"; StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(690, 610); MinimumSize = new Size(670, 550);
            Font = new Font("Microsoft YaHei UI", 9F);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, AutoScroll = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Controls.Add(layout);
            var cfg = client.Settings;
            source = AddNote(layout, 0, "服务器、配置编号和自定义比赛由游戏配置工具的“服务器上传配置”页面管理。", 645);
            AddNote(layout, 1, "按游戏实际玩法参与日常榜；有效锁定的自定义比赛另按举办方规则归榜。\n下面的显示偏好仅保存在本机，不受比赛锁限制。", 645);
            overlay = AddCheck(layout, 2, "显示独立联机排名遮罩（OBS 可单独捕获）", cfg.Overlay);
            transparent = AddCheck(layout, 3, "透明背景", cfg.Transparent);
            editable = AddCheck(layout, 4, "允许拖动及边缘缩放（显示编辑边框；录制时可关闭）", cfg.OverlayEditable);
            layout.Controls.Add(new Label { Text = "字体", AutoSize = true }, 0, 5);
            var fontRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            selectedFont = cfg.OverlayFont; fontName = new Label { Text = selectedFont, AutoSize = true, Padding = new Padding(0, 5, 0, 0) };
            var chooseFont = new Button { Text = "选择字体…", AutoSize = true };
            fontRow.Controls.AddRange(new Control[] { fontName, chooseFont }); layout.Controls.Add(fontRow, 1, 5);
            fontSize = new NumericUpDown { Minimum = 8, Maximum = 72, DecimalPlaces = 1, Value = (decimal)cfg.OverlayFontSize, Width = 90 };
            layout.Controls.Add(new Label { Text = "字号（8–72）", AutoSize = true }, 0, 6); layout.Controls.Add(fontSize, 1, 6);
            selectedColor = ColorTranslator.FromHtml(cfg.OverlayColor);
            color = new Button { Text = "选择文字颜色…", AutoSize = true, BackColor = selectedColor, ForeColor = selectedColor.GetBrightness() > 0.5 ? Color.Black : Color.White, UseVisualStyleBackColor = false };
            layout.Controls.Add(new Label { Text = "颜色", AutoSize = true }, 0, 7); layout.Controls.Add(color, 1, 7);
            alignment = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
            alignment.Items.AddRange(new object[] { "居左", "居中", "居右" }); alignment.SelectedIndex = cfg.OverlayAlignment == "center" ? 1 : cfg.OverlayAlignment == "right" ? 2 : 0;
            layout.Controls.Add(new Label { Text = "文字对齐", AutoSize = true }, 0, 8); layout.Controls.Add(alignment, 1, 8);
            chooseFont.Click += delegate {
                using (var initial = CompetitionOverlayForm.CreateFont(selectedFont, (float)fontSize.Value))
                using (var dialog = new FontDialog { Font = initial, MinSize = 8, MaxSize = 72, ShowEffects = false, FontMustExist = true }) {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    selectedFont = dialog.Font.FontFamily.Name; fontName.Text = selectedFont;
                    fontSize.Value = Math.Max(fontSize.Minimum, Math.Min(fontSize.Maximum, (decimal)dialog.Font.SizeInPoints));
                }
            };
            color.Click += delegate { using (var dialog = new ColorDialog { Color = selectedColor, FullOpen = true }) if (dialog.ShowDialog(this) == DialogResult.OK) { selectedColor = dialog.Color; color.BackColor = selectedColor; color.ForeColor = selectedColor.GetBrightness() > 0.5 ? Color.Black : Color.White; } };
            hwid = AddNote(layout, 9, "", 645);
            hwid.Name = "lblOnlineDeviceId";
            secret = new TextBox { Dock = DockStyle.Fill, MaxLength = 64, UseSystemPasswordChar = true };
            layout.Controls.Add(new Label { Text = "更换设备凭据", AutoSize = true }, 0, 10); layout.Controls.Add(secret, 1, 10);
            AddNote(layout, 11, "通常留空；仅主办方重置凭据后填写。凭据保存在本机，不随游戏包分发。", 645);
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var save = new Button { Text = "保存显示设置", AutoSize = true }; var retry = new Button { Text = "后台重试", AutoSize = true };
            var copy = copyDeviceId = new Button { Name = "btnCopyOnlineDeviceId", Text = "复制设备标识", AutoSize = true, Enabled = false }; var folder = new Button { Text = "打开本机记录", AutoSize = true };
            var reset = new Button { Text = "重置遮罩位置", AutoSize = true };
            buttons.Controls.AddRange(new Control[] { save, retry, copy, folder, reset }); layout.Controls.Add(buttons, 0, 12); layout.SetColumnSpan(buttons, 2);
            status = AddNote(layout, 13, "", 645); activation = AddNote(layout, 14, "", 645);
            board = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
            board.Items.AddRange(new object[] { "综合榜节点参考", "硬核榜节点参考" }); board.SelectedIndex = cfg.ReferenceBoard == "hardcore" ? 1 : 0;
            layout.Controls.Add(new Label { Text = "节点参考榜", AutoSize = true }, 0, 15); layout.Controls.Add(board, 1, 15);
            rankingScope = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
            rankingScope.Items.AddRange(new object[] { "自动跟随（日常／自定义比赛）", "同配置日常榜" });
            rankingScope.SelectedIndex = cfg.ReferenceScope == "daily" ? 1 : 0;
            layout.Controls.Add(new Label { Text = "排名范围", AutoSize = true }, 0, 16); layout.Controls.Add(rankingScope, 1, 16);
            save.Click += delegate {
                try {
                    var next = client.Settings; next.Overlay = overlay.Checked; next.Transparent = transparent.Checked; next.OverlayEditable = editable.Checked;
                    next.OverlayFont = selectedFont; next.OverlayFontSize = (float)fontSize.Value;
                    next.OverlayColor = "#" + selectedColor.R.ToString("X2") + selectedColor.G.ToString("X2") + selectedColor.B.ToString("X2");
                    next.ReferenceScope = rankingScope.SelectedIndex == 1 ? "daily" : "automatic";
                    next.ReferenceBoard = board.SelectedIndex == 1 ? "hardcore" : "overall";
                    next.OverlayAlignment = alignment.SelectedIndex == 1 ? "center" : alignment.SelectedIndex == 2 ? "right" : "left";
                    client.ConfigureAppearance(next, secret.Text.Length == 0 ? null : secret.Text.Trim()); secret.Clear(); status.Text = "已交给后台保存。";
                } catch (Exception ex) { status.Text = ex.Message; }
            };
            reset.Click += delegate { var next = client.Settings; next.OverlayLeft = next.OverlayTop = null; next.OverlayWidth = 555; next.OverlayHeight = 330; client.ConfigureAppearance(next); };
            retry.Click += delegate { client.Retry(); };
            copy.Click += delegate { try { if (client.View.Hwid.Length > 0) Clipboard.SetText(client.View.Hwid); } catch { status.Text = "剪贴板暂不可用。"; } };
            folder.Click += delegate { try { Process.Start(new ProcessStartInfo(client.DataDirectory) { UseShellExecute = true }); } catch { status.Text = "本机记录目录尚未创建。"; } };
            refresh = new Timer { Interval = 1000 }; refresh.Tick += delegate { UpdateStatus(); }; refresh.Start(); UpdateStatus();
        }
        protected override void Dispose(bool disposing) { if (disposing) refresh?.Dispose(); base.Dispose(disposing); }
        private static Label AddNote(TableLayoutPanel layout, int row, string text, int width)
        {
            var note = new Label { Text = text, AutoSize = true, MaximumSize = new Size(width, 0), Margin = new Padding(3, 7, 3, 7) };
            layout.Controls.Add(note, 0, row); layout.SetColumnSpan(note, 2); return note;
        }
        private static CheckBox AddCheck(TableLayoutPanel layout, int row, string text, bool value)
        {
            var check = new CheckBox { Text = text, AutoSize = true, Checked = value };
            layout.Controls.Add(check, 0, row); layout.SetColumnSpan(check, 2); return check;
        }
        private void UpdateStatus()
        {
            var cfg = client.Settings; var value = client.View;
            source.Text = "联机设置由游戏配置工具的“服务器上传配置”页面管理。\n" + (client.GameSettingsError.Length != 0 ? client.GameSettingsError : cfg.Enabled
                ? cfg.Server + "  " + (cfg.CustomCompetitionId == null ? "日常排名" : "自定义比赛：" + cfg.CustomCompetitionId) : "当前游戏未开启联机，或尚未连接游戏。");
            hwid.Text = value.Hwid.Length != 0 ? "设备标识：" + value.Hwid : client.DeviceIdentityReady
                ? "设备标识：本机身份暂不可读取，请重开计时器后重试。" : "设备标识：正在本机读取…";
            copyDeviceId.Enabled = value.Hwid.Length != 0;
            activation.Text = client.ActivationText;
            status.Text = client.LiveDetail;
            if (client.GameSettingsError.Length != 0) status.Text = client.GameSettingsError;
            if (value.Status.StartsWith("比赛设置未能保存", StringComparison.Ordinal) || value.Status.StartsWith("本机比赛设置或凭据不可读取", StringComparison.Ordinal) || value.Status.StartsWith("比赛记录未能落盘", StringComparison.Ordinal)) status.Text = value.Status;
            // Concise connection feedback only; no modal dialog or timing gate.
        }
    }

    internal sealed class CompetitionOverlayForm : Form
    {
        internal const string ObsWindowTitle = "仙剑98自动计时器 - 联机排名";
        private readonly CompetitionClient client;
        private readonly Timer refresh;
        private CompetitionSettings style;
        private string content = "";
        private Font displayFont;
        private bool moving, applyingBounds;
        private Rectangle appliedBounds;
        internal CompetitionOverlayForm(CompetitionClient client)
        {
            this.client = client;
            Text = ObsWindowTitle; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = true; TopMost = false;
            AutoScaleMode = AutoScaleMode.None; ClientSize = new Size(555, 330); MinimumSize = new Size(320, 140); MaximumSize = new Size(3840, 2160);
            DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true);
            StartPosition = FormStartPosition.Manual;
            Resize += delegate { UpdateFont(); };
            ResizeBegin += delegate { moving = true; };
            ResizeEnd += delegate { moving = false; SaveBounds(); };
            var menu = new ContextMenuStrip(); var hide = new ToolStripMenuItem("隐藏联机排名遮罩");
            hide.Click += delegate { Close(); }; menu.Items.Add(hide); ContextMenuStrip = menu;
            refresh = new Timer { Interval = 500 }; refresh.Tick += delegate { RefreshView(); }; refresh.Start(); RefreshView();
        }
        protected override void Dispose(bool disposing) { if (disposing) { refresh?.Dispose(); displayFont?.Dispose(); ContextMenuStrip?.Dispose(); } base.Dispose(disposing); }
        internal static Font CreateFont(string family, float points)
        {
            try { return new Font(family, points, FontStyle.Regular, GraphicsUnit.Point); }
            catch (ArgumentException) { return new Font(FontFamily.GenericSansSerif, points, FontStyle.Regular, GraphicsUnit.Point); }
        }
        internal static Color TransparencyColor(Color foreground) => foreground.ToArgb() == Color.Magenta.ToArgb() ? Color.Lime : Color.Magenta;
        private void UpdateFont()
        {
            if (style == null) return;
            displayFont?.Dispose();
            displayFont = CreateFont(style.OverlayFont, Math.Max(4, Math.Min(192, style.OverlayFontSize * ClientSize.Width / 555F)));
            Invalidate();
        }
        private static string Rank(int? value) => value.HasValue && value.Value > 0 ? "第 " + value.Value + " 名" : "未知";
        internal static string TextFor(CompetitionView value)
        {
            var reply = value.Reply; var node = reply == null ? null : reply.node; var overall = reply == null ? null : reply.overall;
            string player = reply != null && reply.player != null && reply.player.bound ? reply.player.display_name : "尚未绑定玩家";
            return "玩家：" + player + "\n" + (string.IsNullOrWhiteSpace(reply?.title) ? "联机排名" : reply.title) + "\n" +
                "当前节点：" + (string.IsNullOrEmpty(value.NodeName) ? "尚未完成节点" : value.NodeName) + "\n" +
                "完整最佳线：" + Rank(node == null || node.best_complete_line == null ? null : node.best_complete_line.rank) + "    单节点最佳：" + Rank(node == null || node.personal_checkpoint_best == null ? null : node.personal_checkpoint_best.rank) + "\n" +
                (reply?.board == "hardcore" ? "硬核总榜：" : "综合总榜：") + Rank(overall == null || overall.personal_best == null ? null : overall.personal_best.rank) +
                "    本次通关：" + Rank(overall == null || overall.submitted_run == null || !overall.submitted_run.ranked ? null : overall.submitted_run.rank) + "\n" +
                (string.IsNullOrEmpty(reply?.custom_competition_id) ? "硬核领先：" : "硬核前三：") + (reply?.hardcore_top == null || reply.hardcore_top.Length == 0 ? "未知" :
                    string.Join("；", reply.hardcore_top.Select(p => p.rank + ". " + p.display_name + " " + TimeSpan.FromMilliseconds(p.total_ms).ToString(@"hh\:mm\:ss\.fff")))) + "\n" +
                "排名以服务器最近返回的结果为准";
        }
        private void RefreshView()
        {
            var next = client.Settings;
            bool changed = style == null || style.OverlayFont != next.OverlayFont || style.OverlayFontSize != next.OverlayFontSize ||
                style.OverlayColor != next.OverlayColor || style.OverlayAlignment != next.OverlayAlignment || style.Transparent != next.Transparent || style.OverlayEditable != next.OverlayEditable;
            style = next;
            Color key = TransparencyColor(ColorTranslator.FromHtml(style.OverlayColor));
            BackColor = style.Transparent ? key : Color.FromArgb(25, 28, 34); TransparencyKey = style.Transparent ? key : Color.Empty;
            var location = style.OverlayLeft.HasValue ? new Point(style.OverlayLeft.Value, style.OverlayTop.Value) : new Point(Screen.PrimaryScreen.WorkingArea.Left + 60, Screen.PrimaryScreen.WorkingArea.Top + 60);
            var desired = new Rectangle(location, new Size(style.OverlayWidth, style.OverlayHeight));
            if (!moving && desired != appliedBounds) {
                if (Array.TrueForAll(Screen.AllScreens, screen => !Rectangle.Intersect(screen.WorkingArea, desired).Contains(new Point(desired.Left + 16, desired.Top + 16)))) desired.Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 60, Screen.PrimaryScreen.WorkingArea.Top + 60);
                applyingBounds = true; Bounds = desired; appliedBounds = desired; applyingBounds = false;
            }
            string text = TextFor(client.View); if (text != content) { content = text; changed = true; }
            if (changed || displayFont == null) { UpdateFont(); Invalidate(); }
        }
        private void SaveBounds()
        {
            if (applyingBounds) return;
            appliedBounds = Bounds;
            var next = client.Settings; next.OverlayWidth = Width; next.OverlayHeight = Height; next.OverlayLeft = Left; next.OverlayTop = Top;
            client.ConfigureAppearance(next);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (style == null || displayFont == null) return;
            using (var brush = new SolidBrush(ColorTranslator.FromHtml(style.OverlayColor)))
            using (var format = new StringFormat { Alignment = style.OverlayAlignment == "center" ? StringAlignment.Center : style.OverlayAlignment == "right" ? StringAlignment.Far : StringAlignment.Near, LineAlignment = StringAlignment.Near })
                e.Graphics.DrawString(content, displayFont, brush, new RectangleF(14, 12, Math.Max(1, ClientSize.Width - 28), Math.Max(1, ClientSize.Height - 24)), format);
            if (style.OverlayEditable) { using (var pen = new Pen(Color.Gray)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); ControlPaint.DrawSizeGrip(e.Graphics, Color.Gray, Width - 18, Height - 18, 16, 16); }
        }
        internal static int HitTest(Point point, Size size, bool editable)
        {
            if (!editable) return 1; // HTCLIENT: capture remains an independent top-level window.
            const int edge = 10;
            bool l = point.X < edge, r = point.X >= size.Width - edge, t = point.Y < edge, b = point.Y >= size.Height - edge;
            return t ? (l ? 13 : r ? 14 : 12) : b ? (l ? 16 : r ? 17 : 15) : l ? 10 : r ? 11 : 2;
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84 && style != null) { var point = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16)))); m.Result = (IntPtr)HitTest(point, ClientSize, style.OverlayEditable); return; }
            base.WndProc(ref m);
        }
    }
}
