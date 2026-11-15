using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using HFrame.EX;

namespace Pal98Timer
{
    public partial class Upload : FormEx
    {
        private ToolStripMenuItem btnCS;
        public Upload(ToolStripMenuItem b)
        {
            this.btnCS = b;
            InitializeComponent();
        }

        // Refs #61: SDL uses the original selectable result field, with an
        // explicit copy action. Traditional upload windows retain their layout.
        internal void SetCompletedCode(string code)
        {
            if (string.IsNullOrEmpty(code)) throw new ArgumentException("云存档编码为空", nameof(code));
            label1.Text = "云存档已保存";
            txtStatus.Text = code;
            txtStatus.ReadOnly = true;
            txtStatus.ShortcutsEnabled = true;
            var copy = new Button {
                Name = "btnCopy", Text = "复制", Location = new Point(5, 64),
                Size = new Size(66, 25), TabIndex = 2,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            copy.Click += delegate {
                try { Clipboard.SetText(txtStatus.Text); }
                catch (System.Runtime.InteropServices.ExternalException) {
                    Alert("剪贴板暂不可用，请稍后再试或选中编码按 Ctrl+C。");
                }
            };
            panel1.Controls.Add(copy);
            Shown += delegate { txtStatus.Focus(); txtStatus.SelectAll(); };
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            if (btnCS != null)
            {
                btnCS.Enabled = true;
            }
            this.Dispose();
        }

        private void Upload_Load(object sender, EventArgs e)
        {
            CenterToParent();
        }
    }
}
