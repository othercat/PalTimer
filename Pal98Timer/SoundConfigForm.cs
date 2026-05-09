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
        private Button[] btnBrowses;
        private Button[] btnTests;
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
            this.Size = new Size(600, 320);
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
            lblHint.Text = "支持格式: wav, mp3。未配置时自动跳过。";
            lblHint.Location = new Point(220, 15);
            lblHint.Size = new Size(350, 18);
            lblHint.ForeColor = SystemColors.GrayText;
            this.Controls.Add(lblHint);

            // 分隔线
            Label line = new Label();
            line.BorderStyle = BorderStyle.Fixed3D;
            line.Location = new Point(15, 38);
            line.Size = new Size(555, 2);
            this.Controls.Add(line);

            // 每行配置
            chkEnabled = new CheckBox[rowCount];
            txtPaths = new TextBox[rowCount];
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
                txtPaths[i].Size = new Size(280, 22);
                txtPaths[i].ReadOnly = true;
                this.Controls.Add(txtPaths[i]);

                // 浏览按钮
                btnBrowses[i] = new Button();
                btnBrowses[i].Text = "浏览";
                btnBrowses[i].Location = new Point(445, y - 1);
                btnBrowses[i].Size = new Size(55, 24);
                int idx = i;
                btnBrowses[i].Click += (s, e) => BrowseSoundFile(idx);
                this.Controls.Add(btnBrowses[i]);

                // 试听按钮
                btnTests[i] = new Button();
                btnTests[i].Text = "试听";
                btnTests[i].Location = new Point(505, y - 1);
                btnTests[i].Size = new Size(55, 24);
                btnTests[i].Click += (s, e) => TestPlaySound(idx);
                this.Controls.Add(btnTests[i]);

                y += 32;
            }

            // 底部按钮
            y += 10;
            btnOK = new Button();
            btnOK.Text = "确定";
            btnOK.Location = new Point(380, y);
            btnOK.Size = new Size(75, 26);
            btnOK.Click += BtnOK_Click;
            this.Controls.Add(btnOK);

            btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Location = new Point(465, y);
            btnCancel.Size = new Size(75, 26);
            btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            // 调整窗口高度
            this.ClientSize = new Size(580, y + 40);
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
            }
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
            SoundConfig.ins.TestPlay(path);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            SoundConfig sc = SoundConfig.ins;
            sc.GlobalEnabled = chkGlobalEnabled.Checked;

            SoundTriggerType[] types = (SoundTriggerType[])Enum.GetValues(typeof(SoundTriggerType));
            for (int i = 0; i < types.Length; i++)
            {
                sc.SetSoundEnabled(types[i], chkEnabled[i].Checked);
                sc.SetSoundPath(types[i], txtPaths[i].Text);
            }

            sc.SaveConfig();
            this.DialogResult = DialogResult.OK;
        }
    }
}
