using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Pal98Timer
{
    /// <summary>
    /// 节点音效配置窗口
    /// </summary>
    public class SoundConfigForm : Form
    {
        private CheckBox chkGlobalEnabled;
        private CheckBox[] chkEnabled;
        private TextBox[] txtPaths;
        private NumericUpDown[] numVolumes;
        private Button[] btnBrowses;
        private Button[] btnTests;
        private TextBox txtToggleHotkey;
        private Keys _hotkeyValue = Keys.None;
        private CheckBox chkSoundOnEnabled;
        private TextBox txtSoundOnPath;
        private NumericUpDown numSoundOnVolume;
        private Button btnSoundOnBrowse;
        private Button btnSoundOnTest;
        private CheckBox chkSoundOffEnabled;
        private TextBox txtSoundOffPath;
        private NumericUpDown numSoundOffVolume;
        private Button btnSoundOffBrowse;
        private Button btnSoundOffTest;
        private Button btnOK;
        private Button btnCancel;

        public SoundConfigForm()
        {
            InitializeComponent();
            LoadCurrentConfig();
        }

        private void InitializeComponent()
        {
            this.Text = "节点音效配置";
            this.Size = new Size(700, 492);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;

            SoundTriggerType[] types = (SoundTriggerType[])Enum.GetValues(typeof(SoundTriggerType));
            int rowCount = types.Length;

            // 全局启用
            chkGlobalEnabled = new CheckBox();
            chkGlobalEnabled.Text = "启用节点音效";
            chkGlobalEnabled.Location = new Point(15, 12);
            chkGlobalEnabled.Size = new Size(200, 22);
            this.Controls.Add(chkGlobalEnabled);

            // 说明标签
            Label lblHint = new Label();
            lblHint.Text = "支持格式: wav, mp3。音量 0-100，未配置时自动跳过。";
            lblHint.Location = new Point(220, 15);
            lblHint.Size = new Size(440, 18);
            lblHint.ForeColor = SystemColors.GrayText;
            this.Controls.Add(lblHint);

            // 分隔线
            Label line = new Label();
            line.BorderStyle = BorderStyle.Fixed3D;
            line.Location = new Point(15, 38);
            line.Size = new Size(655, 2);
            this.Controls.Add(line);

            // 每行配置
            chkEnabled = new CheckBox[rowCount];
            txtPaths = new TextBox[rowCount];
            numVolumes = new NumericUpDown[rowCount];
            btnBrowses = new Button[rowCount];
            btnTests = new Button[rowCount];

            int y = 48;
            for (int i = 0; i < rowCount; i++)
            {
                SoundTriggerType type = types[i];

                // 触发类型标签
                Label lbl = new Label();
                lbl.Text = type.ToChineseString();
                lbl.Location = new Point(15, y + 3);
                lbl.Size = new Size(120, 18);
                this.Controls.Add(lbl);

                // 启用复选框
                chkEnabled[i] = new CheckBox();
                chkEnabled[i].Text = "";
                chkEnabled[i].Location = new Point(135, y + 2);
                chkEnabled[i].Size = new Size(20, 20);
                this.Controls.Add(chkEnabled[i]);

                // 文件路径
                txtPaths[i] = new TextBox();
                txtPaths[i].Location = new Point(160, y);
                txtPaths[i].Size = new Size(300, 22);
                txtPaths[i].ReadOnly = true;
                this.Controls.Add(txtPaths[i]);

                // 音量
                numVolumes[i] = new NumericUpDown();
                numVolumes[i].Location = new Point(465, y);
                numVolumes[i].Size = new Size(55, 22);
                numVolumes[i].Minimum = 0;
                numVolumes[i].Maximum = 100;
                numVolumes[i].Increment = 5;
                numVolumes[i].Value = 100;
                numVolumes[i].TextAlign = HorizontalAlignment.Right;
                this.Controls.Add(numVolumes[i]);

                // 浏览按钮
                btnBrowses[i] = new Button();
                btnBrowses[i].Text = "浏览";
                btnBrowses[i].Location = new Point(525, y - 1);
                btnBrowses[i].Size = new Size(55, 24);
                int idx = i;
                btnBrowses[i].Click += (s, e) => BrowseSoundFile(idx);
                this.Controls.Add(btnBrowses[i]);

                // 试听按钮
                btnTests[i] = new Button();
                btnTests[i].Text = "试听";
                btnTests[i].Location = new Point(585, y - 1);
                btnTests[i].Size = new Size(55, 24);
                btnTests[i].Click += (s, e) => TestPlaySound(idx);
                this.Controls.Add(btnTests[i]);

                y += 32;
            }

            // 分隔线2 - 开关快捷键区域
            y += 5;
            Label line2 = new Label();
            line2.BorderStyle = BorderStyle.Fixed3D;
            line2.Location = new Point(15, y);
            line2.Size = new Size(655, 2);
            this.Controls.Add(line2);
            y += 10;

            // 快捷键标签
            Label lblHotkey = new Label();
            lblHotkey.Text = "音效开关快捷键";
            lblHotkey.Location = new Point(15, y + 3);
            lblHotkey.Size = new Size(120, 18);
            this.Controls.Add(lblHotkey);

            // 快捷键输入框
            txtToggleHotkey = new TextBox();
            txtToggleHotkey.Location = new Point(135, y);
            txtToggleHotkey.Size = new Size(150, 22);
            txtToggleHotkey.ReadOnly = true;
            txtToggleHotkey.Text = "未设置";
            txtToggleHotkey.KeyDown += TxtToggleHotkey_KeyDown;
            this.Controls.Add(txtToggleHotkey);

            // 清除快捷键按钮
            Button btnClearHotkey = new Button();
            btnClearHotkey.Text = "清除";
            btnClearHotkey.Location = new Point(290, y - 1);
            btnClearHotkey.Size = new Size(55, 24);
            btnClearHotkey.Click += (s, e) => { _hotkeyValue = Keys.None; txtToggleHotkey.Text = "未设置"; };
            this.Controls.Add(btnClearHotkey);

            y += 28;

            // 快捷键提示（独立一行，避免截断）
            Label lblHotkeyHint = new Label();
            lblHotkeyHint.Text = "点击输入框后按任意键设置，建议避开 F9/F10/F11";
            lblHotkeyHint.Location = new Point(15, y + 1);
            lblHotkeyHint.Size = new Size(540, 18);
            lblHotkeyHint.ForeColor = SystemColors.GrayText;
            this.Controls.Add(lblHotkeyHint);

            y += 24;

            // 打开提示音
            Label lblSoundOn = new Label();
            lblSoundOn.Text = "音效打开提示音";
            lblSoundOn.Location = new Point(15, y + 3);
            lblSoundOn.Size = new Size(120, 18);
            this.Controls.Add(lblSoundOn);

            chkSoundOnEnabled = new CheckBox();
            chkSoundOnEnabled.Text = "";
            chkSoundOnEnabled.Location = new Point(135, y + 2);
            chkSoundOnEnabled.Size = new Size(20, 20);
            this.Controls.Add(chkSoundOnEnabled);

            txtSoundOnPath = new TextBox();
            txtSoundOnPath.Location = new Point(160, y);
            txtSoundOnPath.Size = new Size(300, 22);
            txtSoundOnPath.ReadOnly = true;
            this.Controls.Add(txtSoundOnPath);

            numSoundOnVolume = new NumericUpDown();
            numSoundOnVolume.Location = new Point(465, y);
            numSoundOnVolume.Size = new Size(55, 22);
            numSoundOnVolume.Minimum = 0;
            numSoundOnVolume.Maximum = 100;
            numSoundOnVolume.Increment = 5;
            numSoundOnVolume.Value = 100;
            numSoundOnVolume.TextAlign = HorizontalAlignment.Right;
            this.Controls.Add(numSoundOnVolume);

            btnSoundOnBrowse = new Button();
            btnSoundOnBrowse.Text = "浏览";
            btnSoundOnBrowse.Location = new Point(525, y - 1);
            btnSoundOnBrowse.Size = new Size(55, 24);
            btnSoundOnBrowse.Click += (s, e) => BrowseToggleSoundFile(txtSoundOnPath, chkSoundOnEnabled);
            this.Controls.Add(btnSoundOnBrowse);

            btnSoundOnTest = new Button();
            btnSoundOnTest.Text = "试听";
            btnSoundOnTest.Location = new Point(585, y - 1);
            btnSoundOnTest.Size = new Size(55, 24);
            btnSoundOnTest.Click += (s, e) => TestPlayToggleSound(txtSoundOnPath);
            this.Controls.Add(btnSoundOnTest);

            y += 32;

            // 关闭提示音
            Label lblSoundOff = new Label();
            lblSoundOff.Text = "音效关闭提示音";
            lblSoundOff.Location = new Point(15, y + 3);
            lblSoundOff.Size = new Size(120, 18);
            this.Controls.Add(lblSoundOff);

            chkSoundOffEnabled = new CheckBox();
            chkSoundOffEnabled.Text = "";
            chkSoundOffEnabled.Location = new Point(135, y + 2);
            chkSoundOffEnabled.Size = new Size(20, 20);
            this.Controls.Add(chkSoundOffEnabled);

            txtSoundOffPath = new TextBox();
            txtSoundOffPath.Location = new Point(160, y);
            txtSoundOffPath.Size = new Size(300, 22);
            txtSoundOffPath.ReadOnly = true;
            this.Controls.Add(txtSoundOffPath);

            numSoundOffVolume = new NumericUpDown();
            numSoundOffVolume.Location = new Point(465, y);
            numSoundOffVolume.Size = new Size(55, 22);
            numSoundOffVolume.Minimum = 0;
            numSoundOffVolume.Maximum = 100;
            numSoundOffVolume.Increment = 5;
            numSoundOffVolume.Value = 100;
            numSoundOffVolume.TextAlign = HorizontalAlignment.Right;
            this.Controls.Add(numSoundOffVolume);

            btnSoundOffBrowse = new Button();
            btnSoundOffBrowse.Text = "浏览";
            btnSoundOffBrowse.Location = new Point(525, y - 1);
            btnSoundOffBrowse.Size = new Size(55, 24);
            btnSoundOffBrowse.Click += (s, e) => BrowseToggleSoundFile(txtSoundOffPath, chkSoundOffEnabled);
            this.Controls.Add(btnSoundOffBrowse);

            btnSoundOffTest = new Button();
            btnSoundOffTest.Text = "试听";
            btnSoundOffTest.Location = new Point(585, y - 1);
            btnSoundOffTest.Size = new Size(55, 24);
            btnSoundOffTest.Click += (s, e) => TestPlayToggleSound(txtSoundOffPath);
            this.Controls.Add(btnSoundOffTest);

            y += 32;

            // 底部按钮
            y += 10;
            btnOK = new Button();
            btnOK.Text = "确定";
            btnOK.Location = new Point(500, y);
            btnOK.Size = new Size(75, 26);
            btnOK.Click += BtnOK_Click;
            this.Controls.Add(btnOK);

            btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Location = new Point(585, y);
            btnCancel.Size = new Size(75, 26);
            btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            // 调整窗口高度
            this.ClientSize = new Size(680, y + 40);
        }

        private void LoadCurrentConfig()
        {
            SoundConfig sc = SoundConfig.ins;
            chkGlobalEnabled.Checked = sc.GlobalEnabled;

            SoundTriggerType[] types = (SoundTriggerType[])Enum.GetValues(typeof(SoundTriggerType));
            for (int i = 0; i < types.Length; i++)
            {
                chkEnabled[i].Checked = sc.IsSoundEnabled(types[i]);
                txtPaths[i].Text = sc.GetSoundPath(types[i]);
                numVolumes[i].Value = sc.GetSoundVolume(types[i]);
            }

            // 加载快捷键
            _hotkeyValue = sc.ToggleHotkey;
            txtToggleHotkey.Text = sc.GetToggleHotkeyText();

            // 加载开关提示音
            chkSoundOnEnabled.Checked = sc.SoundEnabledOnEnabled;
            txtSoundOnPath.Text = sc.SoundEnabledOnPath;
            numSoundOnVolume.Value = sc.SoundEnabledOnVolume;
            chkSoundOffEnabled.Checked = sc.SoundEnabledOffEnabled;
            txtSoundOffPath.Text = sc.SoundEnabledOffPath;
            numSoundOffVolume.Value = sc.SoundEnabledOffVolume;
        }

        private void TxtToggleHotkey_KeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;

            // 忽略单独的修饰键
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey ||
                e.KeyCode == Keys.Alt || e.KeyCode == Keys.Menu ||
                e.KeyCode == Keys.LControlKey || e.KeyCode == Keys.RControlKey ||
                e.KeyCode == Keys.LShiftKey || e.KeyCode == Keys.RShiftKey ||
                e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
            {
                return;
            }

            Keys key = e.KeyCode;
            if (e.Control) key |= Keys.Control;
            if (e.Shift) key |= Keys.Shift;
            if (e.Alt) key |= Keys.Alt;

            _hotkeyValue = key;
            txtToggleHotkey.Text = key.ToString();
        }

        private void BrowseSoundFile(int index)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "选择音效文件";
                ofd.Filter = "音频文件 (*.wav;*.mp3)|*.wav;*.mp3|WAV 文件 (*.wav)|*.wav|MP3 文件 (*.mp3)|*.mp3|所有文件 (*.*)|*.*";
                ofd.FilterIndex = 1;
                if (File.Exists(txtPaths[index].Text))
                {
                    ofd.InitialDirectory = Path.GetDirectoryName(txtPaths[index].Text);
                }
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtPaths[index].Text = ofd.FileName;
                    chkEnabled[index].Checked = true;
                }
            }
        }

        private void TestPlaySound(int index)
        {
            string path = txtPaths[index].Text;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                MessageBox.Show("请先选择有效的音频文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SoundConfig.ins.TestPlay(path, (int)numVolumes[index].Value);
        }

        private void BrowseToggleSoundFile(TextBox txtPath, CheckBox chkEnable)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "选择提示音文件";
                ofd.Filter = "音频文件 (*.wav;*.mp3)|*.wav;*.mp3|WAV 文件 (*.wav)|*.wav|MP3 文件 (*.mp3)|*.mp3|所有文件 (*.*)|*.*";
                ofd.FilterIndex = 1;
                if (File.Exists(txtPath.Text))
                {
                    ofd.InitialDirectory = Path.GetDirectoryName(txtPath.Text);
                }
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtPath.Text = ofd.FileName;
                    chkEnable.Checked = true;
                }
            }
        }

        private void TestPlayToggleSound(TextBox txtPath)
        {
            string path = txtPath.Text;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                MessageBox.Show("请先选择有效的音频文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int volume = txtPath == txtSoundOnPath ? (int)numSoundOnVolume.Value : (int)numSoundOffVolume.Value;
            SoundConfig.ins.TestPlay(path, volume);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            SoundConfig sc = SoundConfig.ins;
            sc.GlobalEnabled = chkGlobalEnabled.Checked;

            SoundTriggerType[] types = (SoundTriggerType[])Enum.GetValues(typeof(SoundTriggerType));
            for (int i = 0; i < types.Length; i++)
            {
                sc.SetSoundPath(types[i], txtPaths[i].Text);
                sc.SetSoundVolume(types[i], (int)numVolumes[i].Value);
                sc.SetSoundEnabled(types[i], chkEnabled[i].Checked);
            }

            // 保存快捷键和开关提示音
            sc.ToggleHotkey = _hotkeyValue;
            sc.SoundEnabledOnEnabled = chkSoundOnEnabled.Checked;
            sc.SoundEnabledOnPath = txtSoundOnPath.Text;
            sc.SoundEnabledOnVolume = (int)numSoundOnVolume.Value;
            sc.SoundEnabledOffEnabled = chkSoundOffEnabled.Checked;
            sc.SoundEnabledOffPath = txtSoundOffPath.Text;
            sc.SoundEnabledOffVolume = (int)numSoundOffVolume.Value;

            sc.SaveConfig();
            this.DialogResult = DialogResult.OK;
        }
    }
}
