using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace KeyChanger
{
    static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            System.IO.Directory.SetCurrentDirectory(Application.StartupPath);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(Array.IndexOf(args, "--show-keyboard") >= 0));
        }
    }
}
