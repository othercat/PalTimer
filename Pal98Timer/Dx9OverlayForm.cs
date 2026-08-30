using HFrame.OS;
using System;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Pal98Timer
{
    internal struct Dx9OverlayTimelineEntry
    {
        public readonly bool HasValue;
        public readonly bool IsCurrent;
        public readonly bool IsCompleted;
        public readonly string Name;
        public readonly string Best;
        public readonly string Current;
        public readonly long ComparisonSeconds;

        public Dx9OverlayTimelineEntry(
            string name,
            string best,
            string current,
            long comparisonSeconds,
            bool isCurrent,
            bool isCompleted)
        {
            HasValue = true;
            IsCurrent = isCurrent;
            IsCompleted = isCompleted;
            Name = name ?? "";
            Best = best ?? "";
            Current = current ?? "";
            ComparisonSeconds = comparisonSeconds;
        }
    }

    internal sealed class Dx9OverlaySnapshot
    {
        public readonly IntPtr GameWindowHandle;
        public readonly string FontFamily;
        public readonly string MainTimer;
        public readonly string BattleTimer;
        public readonly string IdleTimer;
        public readonly string Resources;
        public readonly int ManualPauseCount;
        public readonly Dx9OverlayTimelineEntry TimelineFirst;
        public readonly Dx9OverlayTimelineEntry TimelineSecond;
        public readonly Dx9OverlayTimelineEntry TimelineThird;
        public readonly string State;
        public readonly bool IsAntiCheatPaused;
        public readonly bool IsPaused;

        public Dx9OverlaySnapshot(
            IntPtr gameWindowHandle,
            string fontFamily,
            string mainTimer,
            string battleTimer,
            string idleTimer,
            string resources,
            int manualPauseCount,
            Dx9OverlayTimelineEntry timelineFirst,
            Dx9OverlayTimelineEntry timelineSecond,
            Dx9OverlayTimelineEntry timelineThird,
            string state,
            bool isAntiCheatPaused,
            bool isPaused)
        {
            GameWindowHandle = gameWindowHandle;
            FontFamily = fontFamily ?? "SimSun";
            MainTimer = mainTimer ?? "";
            BattleTimer = battleTimer ?? "";
            IdleTimer = idleTimer ?? "";
            Resources = resources ?? "";
            ManualPauseCount = Math.Max(0, manualPauseCount);
            TimelineFirst = timelineFirst;
            TimelineSecond = timelineSecond;
            TimelineThird = timelineThird;
            State = state ?? "";
            IsAntiCheatPaused = isAntiCheatPaused;
            IsPaused = isPaused;
        }
    }

    internal static class Dx9OverlaySettings
    {
        internal const string ConfigFileName = "dx9_overlay";
        internal const string LayoutConfigFileName = "dx9_overlay_layout";

        public static bool LoadEnabled()
        {
            try
            {
                if (!File.Exists(ConfigFileName))
                {
                    return false;
                }

                return File.ReadAllText(ConfigFileName, Encoding.UTF8).Trim() == "1";
            }
            catch
            {
                return false;
            }
        }

        public static void SaveEnabled(bool enabled)
        {
            File.WriteAllText(ConfigFileName, enabled ? "1" : "0", new UTF8Encoding(false));
        }

        public static Dx9OverlayLayoutSettings LoadLayout()
        {
            Dx9OverlayLayoutSettings result = Dx9OverlayLayoutSettings.CreateDefault();
            try
            {
                if (!File.Exists(LayoutConfigFileName))
                {
                    return result;
                }

                string[] lines = File.ReadAllLines(LayoutConfigFileName, Encoding.UTF8);
                foreach (string rawLine in lines)
                {
                    int separator = rawLine.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string key = rawLine.Substring(0, separator).Trim();
                    string value = rawLine.Substring(separator + 1).Trim();
                    float number;
                    int integer;
                    switch (key)
                    {
                        case "position_x":
                            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) result.PositionX = number;
                            break;
                        case "position_y":
                            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) result.PositionY = number;
                            break;
                        case "scale":
                            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) result.Scale = number;
                            break;
                        case "font_family":
                            result.FontFamily = value;
                            break;
                        case "font_size":
                            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) result.FontSize = number;
                            break;
                        case "font_style":
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) result.FontStyle = (FontStyle)integer;
                            break;
                        case "font_color":
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) result.FontColorArgb = integer;
                            break;
                        case "toggle_hotkey":
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) result.ToggleHotkey = (Keys)integer;
                            break;
                    }
                }
            }
            catch
            {
                return Dx9OverlayLayoutSettings.CreateDefault();
            }

            result.Normalize();
            return result;
        }

        public static void SaveLayout(Dx9OverlayLayoutSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            Dx9OverlayLayoutSettings value = settings.Clone();
            value.Normalize();
            string family = (value.FontFamily ?? "").Replace("\r", "").Replace("\n", "");
            StringBuilder text = new StringBuilder();
            text.AppendLine("version=1");
            text.AppendLine("position_x=" + value.PositionX.ToString("0.0000", CultureInfo.InvariantCulture));
            text.AppendLine("position_y=" + value.PositionY.ToString("0.0000", CultureInfo.InvariantCulture));
            text.AppendLine("scale=" + value.Scale.ToString("0.0000", CultureInfo.InvariantCulture));
            text.AppendLine("font_family=" + family);
            text.AppendLine("font_size=" + value.FontSize.ToString("0.00", CultureInfo.InvariantCulture));
            text.AppendLine("font_style=" + ((int)value.FontStyle).ToString(CultureInfo.InvariantCulture));
            text.AppendLine("font_color=" + value.FontColorArgb.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("toggle_hotkey=" + ((int)value.ToggleHotkey).ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(LayoutConfigFileName, text.ToString(), new UTF8Encoding(false));
        }

        public static string ValidateToggleHotkey(Keys hotkey, Keys soundToggleHotkey)
        {
            if (hotkey == Keys.None)
            {
                return "";
            }

            Keys keyCode = hotkey & Keys.KeyCode;
            Keys modifiers = hotkey & Keys.Modifiers;
            if (keyCode >= Keys.F1 && keyCode <= Keys.F12)
            {
                return "F1-F12 已由常规计时器功能保留。";
            }
            if (keyCode == Keys.Enter && (modifiers & Keys.Control) == Keys.Control)
            {
                return "Ctrl+Enter 已由计时器功能保留。";
            }
            if (modifiers == Keys.None)
            {
                return "请至少配合 Ctrl、Shift 或 Alt 使用，避免占用游戏按键。";
            }
            if (soundToggleHotkey != Keys.None && hotkey == soundToggleHotkey)
            {
                return "该组合已用于节点音效开关。";
            }
            return "";
        }

        public static string FormatToggleHotkey(Keys hotkey)
        {
            if (hotkey == Keys.None)
            {
                return "未设置";
            }

            StringBuilder text = new StringBuilder();
            Keys modifiers = hotkey & Keys.Modifiers;
            if ((modifiers & Keys.Control) == Keys.Control) text.Append("Ctrl+");
            if ((modifiers & Keys.Shift) == Keys.Shift) text.Append("Shift+");
            if ((modifiers & Keys.Alt) == Keys.Alt) text.Append("Alt+");
            text.Append((hotkey & Keys.KeyCode).ToString());
            return text.ToString();
        }
    }

    internal sealed class Dx9OverlayLayoutSettings
    {
        internal const float MinimumScale = 0.50F;
        internal const float MaximumScale = 2.00F;
        internal const float MinimumFontSize = 6.00F;
        internal const float MaximumFontSize = 18.00F;
        internal const float DefaultFontSize = 9.00F;

        public float PositionX;
        public float PositionY;
        public float Scale;
        public string FontFamily;
        public float FontSize;
        public FontStyle FontStyle;
        public int FontColorArgb;
        public Keys ToggleHotkey;

        public static Dx9OverlayLayoutSettings CreateDefault()
        {
            return new Dx9OverlayLayoutSettings
            {
                PositionX = 1.0F,
                PositionY = 1.0F,
                Scale = 1.0F,
                FontFamily = "",
                FontSize = DefaultFontSize,
                FontStyle = FontStyle.Regular,
                FontColorArgb = 0,
                ToggleHotkey = Keys.None,
            };
        }

        public Dx9OverlayLayoutSettings Clone()
        {
            return new Dx9OverlayLayoutSettings
            {
                PositionX = PositionX,
                PositionY = PositionY,
                Scale = Scale,
                FontFamily = FontFamily,
                FontSize = FontSize,
                FontStyle = FontStyle,
                FontColorArgb = FontColorArgb,
                ToggleHotkey = ToggleHotkey,
            };
        }

        public void Normalize()
        {
            PositionX = Clamp(PositionX, 0.0F, 1.0F);
            PositionY = Clamp(PositionY, 0.0F, 1.0F);
            Scale = Clamp(Scale, MinimumScale, MaximumScale);
            FontSize = Clamp(FontSize, MinimumFontSize, MaximumFontSize);
            FontFamily = (FontFamily ?? "").Trim();
            FontStyle &= FontStyle.Bold | FontStyle.Italic;
            if (FontColorArgb != 0)
            {
                Color color = Color.FromArgb(FontColorArgb);
                FontColorArgb = Color.FromArgb(255, color.R, color.G, color.B).ToArgb();
            }
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return minimum;
            }
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }

    internal sealed class Dx9OverlayHotkeyForm : Form
    {
        private readonly TextBox HotkeyTextBox;
        private readonly Label ValidationLabel;
        private readonly Func<Keys, string> Validator;

        public Keys SelectedHotkey { get; private set; }

        public Dx9OverlayHotkeyForm(Keys currentHotkey, Func<Keys, string> validator)
        {
            Validator = validator ?? throw new ArgumentNullException("validator");
            SelectedHotkey = currentHotkey;

            Text = "配置游戏内信息叠加快捷键";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(430, 162);

            Label instructionLabel = new Label();
            instructionLabel.AutoSize = true;
            instructionLabel.Location = new Point(14, 14);
            instructionLabel.Text = "在下方按下组合键。必须包含 Ctrl、Shift 或 Alt，且不能使用 F1-F12。";

            HotkeyTextBox = new TextBox();
            HotkeyTextBox.Location = new Point(17, 42);
            HotkeyTextBox.Size = new Size(280, 23);
            HotkeyTextBox.ReadOnly = true;
            HotkeyTextBox.Text = Dx9OverlaySettings.FormatToggleHotkey(SelectedHotkey);
            HotkeyTextBox.KeyDown += HotkeyTextBox_KeyDown;

            Button clearButton = new Button();
            clearButton.Location = new Point(307, 40);
            clearButton.Size = new Size(105, 27);
            clearButton.Text = "清除快捷键";
            clearButton.Click += delegate {
                SelectedHotkey = Keys.None;
                HotkeyTextBox.Text = Dx9OverlaySettings.FormatToggleHotkey(SelectedHotkey);
                ValidationLabel.Text = "";
                HotkeyTextBox.Focus();
            };

            ValidationLabel = new Label();
            ValidationLabel.AutoEllipsis = true;
            ValidationLabel.ForeColor = Color.Firebrick;
            ValidationLabel.Location = new Point(17, 73);
            ValidationLabel.Size = new Size(395, 32);

            Button okButton = new Button();
            okButton.Location = new Point(245, 121);
            okButton.Size = new Size(80, 28);
            okButton.Text = "确定";
            okButton.Click += OkButton_Click;

            Button cancelButton = new Button();
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.Location = new Point(332, 121);
            cancelButton.Size = new Size(80, 28);
            cancelButton.Text = "取消";

            Controls.Add(instructionLabel);
            Controls.Add(HotkeyTextBox);
            Controls.Add(clearButton);
            Controls.Add(ValidationLabel);
            Controls.Add(okButton);
            Controls.Add(cancelButton);
            AcceptButton = okButton;
            CancelButton = cancelButton;
            Shown += delegate { HotkeyTextBox.Focus(); };
        }

        private void HotkeyTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            if (IsModifierKey(e.KeyCode))
            {
                return;
            }

            SelectedHotkey = e.KeyData;
            HotkeyTextBox.Text = Dx9OverlaySettings.FormatToggleHotkey(SelectedHotkey);
            ValidationLabel.Text = Validator(SelectedHotkey);
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            string error = Validator(SelectedHotkey);
            if (!string.IsNullOrEmpty(error))
            {
                ValidationLabel.Text = error;
                HotkeyTextBox.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private static bool IsModifierKey(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey ||
                key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey ||
                key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu;
        }
    }

    internal sealed class Dx9OverlayForm : Form
    {
        private const int RefreshIntervalMilliseconds = 100;
        private const float OverlayWidthLogicalPixels = 340.0F;
        // Removing the estimate row shortens the bottom-anchored panel by one row,
        // which moves the complete overlay down without changing row spacing.
        private const float OverlayHeightLogicalPixels = 148.0F;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const float DefaultSmallFontLogicalPoints = 9.0F;
        private const float DefaultTimerFontLogicalPoints = 20.0F;
        private const float ResizeHandleLogicalPixels = 16.0F;
        private const float EditHeaderLogicalPixels = 22.0F;

        private static readonly Color TransparentColor = Color.Magenta;
        private readonly Func<Dx9OverlaySnapshot> SnapshotProvider;
        private readonly Timer RefreshTimer;
        private Dx9OverlaySnapshot CurrentSnapshot;
        private Dx9OverlayLayoutSettings LayoutSettings;
        private Rectangle CurrentGameClientBounds = Rectangle.Empty;
        private float CurrentAutomaticScale = 1.0F;
        private float CurrentScale = 1.0F;
        private bool EditMode;
        private bool Dragging;
        private bool Resizing;
        private Point DragStartScreen;
        private Rectangle DragStartBounds;

        public event EventHandler EditModeChanged;
        public event Action<Exception> LayoutSaveFailed;

        public Dx9OverlayForm(Func<Dx9OverlaySnapshot> snapshotProvider)
            : this(snapshotProvider, Dx9OverlaySettings.LoadLayout())
        {
        }

        internal Dx9OverlayForm(
            Func<Dx9OverlaySnapshot> snapshotProvider,
            Dx9OverlayLayoutSettings layoutSettings)
        {
            if (snapshotProvider == null)
            {
                throw new ArgumentNullException("snapshotProvider");
            }

            SnapshotProvider = snapshotProvider;
            LayoutSettings = layoutSettings == null ? Dx9OverlayLayoutSettings.CreateDefault() : layoutSettings.Clone();
            LayoutSettings.Normalize();
            AutoScaleMode = AutoScaleMode.None;
            BackColor = TransparentColor;
            TransparencyKey = TransparentColor;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            RefreshTimer = new Timer();
            RefreshTimer.Interval = RefreshIntervalMilliseconds;
            RefreshTimer.Tick += RefreshTimer_Tick;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                if (!EditMode)
                {
                    cp.ExStyle |= WS_EX_TRANSPARENT;
                }
                return cp;
            }
        }

        public bool IsInEditMode
        {
            get { return EditMode; }
        }

        public string ConfiguredFontFamily
        {
            get { return LayoutSettings.FontFamily; }
        }

        public float ConfiguredFontSize
        {
            get { return LayoutSettings.FontSize; }
        }

        public FontStyle ConfiguredFontStyle
        {
            get { return LayoutSettings.FontStyle; }
        }

        public int ConfiguredFontColorArgb
        {
            get { return LayoutSettings.FontColorArgb; }
        }

        public Keys ConfiguredToggleHotkey
        {
            get { return LayoutSettings.ToggleHotkey; }
        }

        public void Start()
        {
            RefreshTimer.Start();
            RefreshOverlay();
        }

        public void Stop()
        {
            RefreshTimer.Stop();
            if (Visible)
            {
                Hide();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                RefreshTimer.Stop();
                RefreshTimer.Tick -= RefreshTimer_Tick;
                RefreshTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void WndProc(ref Message m)
        {
            if (!EditMode && m.Msg == WM_NCHITTEST)
            {
                m.Result = new IntPtr(HTTRANSPARENT);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Dx9OverlaySnapshot snapshot = CurrentSnapshot;
            if (snapshot == null)
            {
                return;
            }

            float scale = GetScaleFactor();
            float margin = 7.0F * scale;
            float rowGap = 2.0F * scale;
            float editHeaderHeight = EditMode ? EditHeaderLogicalPixels * scale : 0.0F;
            float timerHeight = GetTimerHeightLogicalPixels() * scale;
            float infoHeight = GetInfoHeightLogicalPixels() * scale;
            float contentWidth = Math.Max(1.0F, ClientSize.Width - margin * 2);
            RectangleF timerRect = new RectangleF(margin, editHeaderHeight + margin, contentWidth, timerHeight);

            if (EditMode)
            {
                using (SolidBrush editBackground = new SolidBrush(Color.FromArgb(48, 50, 48)))
                using (Pen editBorder = new Pen(Color.FromArgb(184, 169, 122), Math.Max(1.0F, scale)))
                using (SolidBrush resizeHandle = new SolidBrush(Color.FromArgb(184, 169, 122)))
                {
                    e.Graphics.FillRectangle(editBackground, ClientRectangle);
                    e.Graphics.DrawRectangle(editBorder, 0, 0, Math.Max(0, ClientSize.Width - 1), Math.Max(0, ClientSize.Height - 1));
                    Rectangle handle = GetResizeHandleRectangle();
                    e.Graphics.FillRectangle(resizeHandle, handle);
                }
            }

            // Color-key transparency and antialiasing create magenta fringes around text.
            // Single-bit glyph edges keep the transparent overlay neutral and readable.
            e.Graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            string fontFamily = string.IsNullOrEmpty(LayoutSettings.FontFamily) ? snapshot.FontFamily : LayoutSettings.FontFamily;
            bool hasCustomFontColor = LayoutSettings.FontColorArgb != 0;
            Color primaryColor = hasCustomFontColor ? Color.FromArgb(LayoutSettings.FontColorArgb) : Color.FromArgb(226, 226, 216);
            Color secondaryColor = hasCustomFontColor ? DimColor(primaryColor, 0.72F) : Color.FromArgb(166, 176, 172);
            Color runningColor = hasCustomFontColor ? primaryColor : Color.FromArgb(190, 207, 187);
            Color currentColor = hasCustomFontColor ? primaryColor : Color.FromArgb(205, 185, 128);
            using (Font smallFont = new Font(fontFamily, LayoutSettings.FontSize * scale, LayoutSettings.FontStyle, GraphicsUnit.Point))
            using (Font timerFont = new Font(fontFamily, GetTimerFontLogicalPoints() * scale, LayoutSettings.FontStyle, GraphicsUnit.Point))
            using (SolidBrush primaryBrush = new SolidBrush(primaryColor))
            using (SolidBrush secondaryBrush = new SolidBrush(secondaryColor))
            using (SolidBrush runningBrush = new SolidBrush(runningColor))
            using (SolidBrush currentBrush = new SolidBrush(currentColor))
            using (SolidBrush fasterBrush = new SolidBrush(Color.FromArgb(125, 164, 137)))
            using (SolidBrush slowerBrush = new SolidBrush(Color.FromArgb(180, 121, 116)))
            using (SolidBrush shadowBrush = new SolidBrush(Color.Black))
            using (StringFormat rightFormat = new StringFormat())
            {
                rightFormat.Alignment = StringAlignment.Far;
                rightFormat.LineAlignment = StringAlignment.Near;
                rightFormat.Trimming = StringTrimming.EllipsisCharacter;
                rightFormat.FormatFlags = StringFormatFlags.NoWrap;

                SolidBrush timerBrush = snapshot.IsAntiCheatPaused ? slowerBrush : (snapshot.IsPaused ? currentBrush : runningBrush);
                DrawOutlinedText(e.Graphics, snapshot.MainTimer, timerFont, timerBrush, shadowBrush, timerRect, rightFormat, scale);

                float y = editHeaderHeight + margin + timerHeight + rowGap;
                RectangleF row = new RectangleF(margin, y, contentWidth, infoHeight);
                DrawOutlinedText(e.Graphics, snapshot.Resources, smallFont, primaryBrush, shadowBrush, row, rightFormat, scale);

                y += infoHeight + rowGap;
                row = new RectangleF(margin, y, contentWidth, infoHeight);
                string timing = "暂停" + snapshot.ManualPauseCount.ToString() + "  战斗 " + snapshot.BattleTimer;
                if (snapshot.IdleTimer != "")
                {
                    timing += "  空闲 " + snapshot.IdleTimer;
                }
                if (snapshot.State != "")
                {
                    timing += "  " + snapshot.State;
                }
                DrawOutlinedText(e.Graphics, timing, smallFont, snapshot.IsPaused ? currentBrush : primaryBrush, shadowBrush, row, rightFormat, scale);

                y += infoHeight + rowGap;
                DrawTimelineEntry(e.Graphics, snapshot.TimelineFirst, smallFont, primaryBrush, secondaryBrush, currentBrush, fasterBrush, slowerBrush, shadowBrush, rightFormat, margin, y, contentWidth, infoHeight, scale);
                y += infoHeight + rowGap;
                DrawTimelineEntry(e.Graphics, snapshot.TimelineSecond, smallFont, primaryBrush, secondaryBrush, currentBrush, fasterBrush, slowerBrush, shadowBrush, rightFormat, margin, y, contentWidth, infoHeight, scale);
                y += infoHeight + rowGap;
                DrawTimelineEntry(e.Graphics, snapshot.TimelineThird, smallFont, primaryBrush, secondaryBrush, currentBrush, fasterBrush, slowerBrush, shadowBrush, rightFormat, margin, y, contentWidth, infoHeight, scale);

                if (EditMode)
                {
                    using (StringFormat leftFormat = new StringFormat())
                    {
                        leftFormat.Alignment = StringAlignment.Near;
                        leftFormat.LineAlignment = StringAlignment.Near;
                        leftFormat.FormatFlags = StringFormatFlags.NoWrap;
                        RectangleF editHint = new RectangleF(margin, 2.0F * scale, contentWidth, EditHeaderLogicalPixels * scale);
                        DrawOutlinedText(e.Graphics, "拖动移动  右下角缩放  右键完成", smallFont, currentBrush, shadowBrush, editHint, leftFormat, scale);
                    }
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!EditMode)
            {
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                EndEditMode();
                return;
            }
            if (e.Button != MouseButtons.Left || CurrentGameClientBounds.IsEmpty)
            {
                return;
            }

            Dragging = true;
            Resizing = GetResizeHandleRectangle().Contains(e.Location);
            DragStartScreen = MousePosition;
            DragStartBounds = Bounds;
            Capture = true;
            Cursor = Resizing ? Cursors.SizeNWSE : Cursors.SizeAll;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!EditMode)
            {
                return;
            }
            if (!Dragging)
            {
                Cursor = GetResizeHandleRectangle().Contains(e.Location) ? Cursors.SizeNWSE : Cursors.SizeAll;
                return;
            }

            Point mouse = MousePosition;
            int deltaX = mouse.X - DragStartScreen.X;
            int deltaY = mouse.Y - DragStartScreen.Y;
            if (Resizing)
            {
                ResizeFromDrag(deltaX, deltaY);
            }
            else
            {
                MoveFromDrag(deltaX, deltaY);
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!Dragging || e.Button != MouseButtons.Left)
            {
                return;
            }

            Dragging = false;
            Resizing = false;
            Capture = false;
            SaveLayoutAfterInteraction();
        }

        public bool BeginEditMode()
        {
            if (EditMode)
            {
                return Visible;
            }

            EditMode = true;
            RecreateHandle();
            RefreshOverlay();
            if (!Visible)
            {
                EditMode = false;
                RecreateHandle();
                OnEditModeChanged();
                return false;
            }

            OnEditModeChanged();
            Invalidate();
            return true;
        }

        public void EndEditMode()
        {
            if (!EditMode)
            {
                return;
            }

            Dragging = false;
            Resizing = false;
            Capture = false;
            SaveLayoutAfterInteraction();
            EditMode = false;
            RecreateHandle();
            RefreshOverlay();
            OnEditModeChanged();
        }

        public void ApplyFont(string family, float size, FontStyle style)
        {
            Dx9OverlayLayoutSettings previous = LayoutSettings.Clone();
            LayoutSettings.FontFamily = family ?? "";
            LayoutSettings.FontSize = size;
            LayoutSettings.FontStyle = style;
            LayoutSettings.Normalize();
            try
            {
                Dx9OverlaySettings.SaveLayout(LayoutSettings);
            }
            catch
            {
                LayoutSettings = previous;
                throw;
            }
            RefreshOverlay();
        }

        public void ApplyFontColor(Color color)
        {
            if (color.R == TransparentColor.R && color.G == TransparentColor.G && color.B == TransparentColor.B)
            {
                throw new ArgumentException("洋红色用于叠加窗口透明色键，不能作为字体颜色。", "color");
            }

            Dx9OverlayLayoutSettings previous = LayoutSettings.Clone();
            LayoutSettings.FontColorArgb = Color.FromArgb(255, color.R, color.G, color.B).ToArgb();
            LayoutSettings.Normalize();
            try
            {
                Dx9OverlaySettings.SaveLayout(LayoutSettings);
            }
            catch
            {
                LayoutSettings = previous;
                throw;
            }
            RefreshOverlay();
        }

        public void ApplyToggleHotkey(Keys hotkey)
        {
            Dx9OverlayLayoutSettings previous = LayoutSettings.Clone();
            LayoutSettings.ToggleHotkey = hotkey;
            try
            {
                Dx9OverlaySettings.SaveLayout(LayoutSettings);
            }
            catch
            {
                LayoutSettings = previous;
                throw;
            }
        }

        public void ResetLayout()
        {
            Dx9OverlayLayoutSettings previous = LayoutSettings;
            LayoutSettings = Dx9OverlayLayoutSettings.CreateDefault();
            try
            {
                Dx9OverlaySettings.SaveLayout(LayoutSettings);
            }
            catch
            {
                LayoutSettings = previous;
                throw;
            }
            RefreshOverlay();
        }

        private static void DrawTimelineEntry(
            Graphics graphics,
            Dx9OverlayTimelineEntry entry,
            Font font,
            SolidBrush primaryBrush,
            SolidBrush secondaryBrush,
            SolidBrush currentBrush,
            SolidBrush fasterBrush,
            SolidBrush slowerBrush,
            SolidBrush shadowBrush,
            StringFormat rightFormat,
            float left,
            float top,
            float width,
            float height,
            float scale)
        {
            if (!entry.HasValue)
            {
                return;
            }

            float bestWidth = 76.0F * scale;
            float currentWidth = 76.0F * scale;
            float nameWidth = Math.Max(1.0F, width - bestWidth - currentWidth);
            RectangleF nameRect = new RectangleF(left, top, nameWidth, height);
            RectangleF bestRect = new RectangleF(nameRect.Right, top, bestWidth, height);
            RectangleF currentRect = new RectangleF(bestRect.Right, top, currentWidth, height);
            SolidBrush nameBrush = entry.IsCurrent ? currentBrush : (entry.IsCompleted ? primaryBrush : secondaryBrush);

            string name = entry.IsCurrent ? "> " + entry.Name : entry.Name;
            DrawOutlinedText(graphics, name, font, nameBrush, shadowBrush, nameRect, rightFormat, scale);
            DrawOutlinedText(graphics, entry.Best, font, currentBrush, shadowBrush, bestRect, rightFormat, scale);
            if (entry.Current != "")
            {
                SolidBrush currentTimeBrush = entry.ComparisonSeconds < 0
                    ? fasterBrush
                    : (entry.ComparisonSeconds > 0 ? slowerBrush : currentBrush);
                DrawOutlinedText(graphics, entry.Current, font, currentTimeBrush, shadowBrush, currentRect, rightFormat, scale);
            }
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            RefreshOverlay();
        }

        private void RefreshOverlay()
        {
            Dx9OverlaySnapshot snapshot;
            try
            {
                snapshot = SnapshotProvider();
            }
            catch
            {
                HideOverlay();
                return;
            }

            if (snapshot == null || snapshot.GameWindowHandle == IntPtr.Zero ||
                !IsWindowVisible(snapshot.GameWindowHandle) || IsIconic(snapshot.GameWindowHandle) ||
                (!EditMode && User32.GetForegroundWindow() != snapshot.GameWindowHandle))
            {
                HideOverlay();
                return;
            }

            RECT clientRect = new RECT();
            POINT clientOrigin = new POINT();
            if (Win32API.GetClientRect(snapshot.GameWindowHandle, ref clientRect) == 0 ||
                !Win32API.ClientToScreen(snapshot.GameWindowHandle, ref clientOrigin))
            {
                HideOverlay();
                return;
            }

            int width = clientRect.right - clientRect.left;
            int height = clientRect.bottom - clientRect.top;
            if (width <= 0 || height <= 0)
            {
                HideOverlay();
                return;
            }

            CurrentSnapshot = snapshot;
            CurrentGameClientBounds = new Rectangle(clientOrigin.x, clientOrigin.y, width, height);
            float widthScale = width / 640.0F;
            float heightScale = height / 480.0F;
            CurrentAutomaticScale = Math.Max(0.75F, Math.Min(2.0F, Math.Min(widthScale, heightScale)));
            float logicalHeight = GetOverlayHeightLogicalPixels();
            float maximumFittingScale = Math.Min(
                width / (OverlayWidthLogicalPixels * CurrentAutomaticScale),
                height / (logicalHeight * CurrentAutomaticScale));
            float effectiveUserScale = Math.Min(LayoutSettings.Scale, Math.Max(0.10F, maximumFittingScale));
            CurrentScale = CurrentAutomaticScale * effectiveUserScale;
            int overlayWidth = Math.Min(width, (int)Math.Ceiling(OverlayWidthLogicalPixels * CurrentScale));
            int overlayHeight = Math.Min(height, (int)Math.Ceiling(logicalHeight * CurrentScale));
            int availableWidth = Math.Max(0, width - overlayWidth);
            int availableHeight = Math.Max(0, height - overlayHeight);
            int overlayLeft = clientOrigin.x + (int)Math.Round(availableWidth * LayoutSettings.PositionX);
            int overlayTop = clientOrigin.y + (int)Math.Round(availableHeight * LayoutSettings.PositionY);
            if (Left != overlayLeft || Top != overlayTop || Width != overlayWidth || Height != overlayHeight)
            {
                SetBounds(overlayLeft, overlayTop, overlayWidth, overlayHeight);
            }
            if (!Visible)
            {
                Show();
            }
            Invalidate();
        }

        private void HideOverlay()
        {
            CurrentSnapshot = null;
            CurrentGameClientBounds = Rectangle.Empty;
            if (Visible)
            {
                Hide();
            }
        }

        private float GetScaleFactor()
        {
            return CurrentScale;
        }

        private float GetTimerFontLogicalPoints()
        {
            return LayoutSettings.FontSize * DefaultTimerFontLogicalPoints / DefaultSmallFontLogicalPoints;
        }

        private float GetTimerHeightLogicalPixels()
        {
            return Math.Max(32.0F, GetTimerFontLogicalPoints() * 1.6F);
        }

        private float GetInfoHeightLogicalPixels()
        {
            return Math.Max(18.0F, LayoutSettings.FontSize * 2.0F);
        }

        private float GetOverlayHeightLogicalPixels()
        {
            float timerHeight = GetTimerHeightLogicalPixels();
            float infoHeight = GetInfoHeightLogicalPixels();
            float contentHeight = 7.0F + timerHeight + 2.0F + infoHeight + 4.0F * (infoHeight + 2.0F) + 9.0F;
            return contentHeight + (EditMode ? EditHeaderLogicalPixels : 0.0F);
        }

        private Rectangle GetResizeHandleRectangle()
        {
            int size = Math.Max(10, (int)Math.Ceiling(ResizeHandleLogicalPixels * Math.Max(0.5F, CurrentScale)));
            return new Rectangle(Math.Max(0, ClientSize.Width - size), Math.Max(0, ClientSize.Height - size), size, size);
        }

        private void MoveFromDrag(int deltaX, int deltaY)
        {
            int maximumLeft = Math.Max(CurrentGameClientBounds.Left, CurrentGameClientBounds.Right - Width);
            int maximumTop = Math.Max(CurrentGameClientBounds.Top, CurrentGameClientBounds.Bottom - Height);
            int left = Math.Max(CurrentGameClientBounds.Left, Math.Min(maximumLeft, DragStartBounds.Left + deltaX));
            int top = Math.Max(CurrentGameClientBounds.Top, Math.Min(maximumTop, DragStartBounds.Top + deltaY));
            SetBounds(left, top, Width, Height);
            UpdateNormalizedPosition();
        }

        private void ResizeFromDrag(int deltaX, int deltaY)
        {
            float logicalHeight = GetOverlayHeightLogicalPixels();
            float widthScale = (DragStartBounds.Width + deltaX) / (OverlayWidthLogicalPixels * CurrentAutomaticScale);
            float heightScale = (DragStartBounds.Height + deltaY) / (logicalHeight * CurrentAutomaticScale);
            float requestedScale = Math.Abs(deltaX) >= Math.Abs(deltaY) ? widthScale : heightScale;
            float fitScale = Math.Min(
                (CurrentGameClientBounds.Right - DragStartBounds.Left) / (OverlayWidthLogicalPixels * CurrentAutomaticScale),
                (CurrentGameClientBounds.Bottom - DragStartBounds.Top) / (logicalHeight * CurrentAutomaticScale));
            LayoutSettings.Scale = Math.Max(
                Dx9OverlayLayoutSettings.MinimumScale,
                Math.Min(Dx9OverlayLayoutSettings.MaximumScale, Math.Min(requestedScale, fitScale)));
            LayoutSettings.Normalize();

            CurrentScale = CurrentAutomaticScale * LayoutSettings.Scale;
            int width = Math.Min(CurrentGameClientBounds.Width, (int)Math.Ceiling(OverlayWidthLogicalPixels * CurrentScale));
            int height = Math.Min(CurrentGameClientBounds.Height, (int)Math.Ceiling(logicalHeight * CurrentScale));
            SetBounds(DragStartBounds.Left, DragStartBounds.Top, width, height);
            UpdateNormalizedPosition();
        }

        private void UpdateNormalizedPosition()
        {
            int availableWidth = Math.Max(0, CurrentGameClientBounds.Width - Width);
            int availableHeight = Math.Max(0, CurrentGameClientBounds.Height - Height);
            LayoutSettings.PositionX = availableWidth == 0 ? 0.0F : (Left - CurrentGameClientBounds.Left) / (float)availableWidth;
            LayoutSettings.PositionY = availableHeight == 0 ? 0.0F : (Top - CurrentGameClientBounds.Top) / (float)availableHeight;
            LayoutSettings.Normalize();
        }

        private void SaveLayoutAfterInteraction()
        {
            try
            {
                Dx9OverlaySettings.SaveLayout(LayoutSettings);
            }
            catch (Exception ex)
            {
                Action<Exception> handler = LayoutSaveFailed;
                if (handler != null)
                {
                    handler(ex);
                }
            }
        }

        private void OnEditModeChanged()
        {
            EventHandler handler = EditModeChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private static Color DimColor(Color color, float factor)
        {
            return Color.FromArgb(
                255,
                Math.Max(0, Math.Min(255, (int)Math.Round(color.R * factor))),
                Math.Max(0, Math.Min(255, (int)Math.Round(color.G * factor))),
                Math.Max(0, Math.Min(255, (int)Math.Round(color.B * factor))));
        }

        private static void DrawOutlinedText(
            Graphics graphics,
            string text,
            Font font,
            Brush foreground,
            Brush shadow,
            RectangleF bounds,
            StringFormat format,
            float scale)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            RectangleF shadowBounds = bounds;
            shadowBounds.Offset(Math.Max(1.0F, scale), Math.Max(1.0F, scale));
            graphics.DrawString(text, font, shadow, shadowBounds, format);
            graphics.DrawString(text, font, foreground, bounds, format);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
    }
}
