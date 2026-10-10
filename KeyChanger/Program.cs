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
            try {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
            System.IO.Directory.SetCurrentDirectory(Application.StartupPath);
            Pal98Timer.TimerUserSettings.Store.Initialize();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(Array.IndexOf(args, "--show-keyboard") >= 0));
            } catch (Pal98Timer.TimerSettingsException) { Environment.Exit(1); }
        }
    }
}
