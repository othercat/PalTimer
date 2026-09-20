using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Pal98Timer
{
    public class KeyChangerDel
    {
        private static readonly HardcoreRequestedProcesses hardcoreProcesses = new HardcoreRequestedProcesses();
        private static readonly HardcoreKeyChangerGuard hardcoreGuard = new HardcoreKeyChangerGuard(hardcoreProcesses.Read);
        public static bool IsHardcoreBlocked { get { return hardcoreGuard.Refresh(); } }
        internal static long BeginEnableRequest() { return hardcoreGuard.BeginRequest(); }
        internal static bool IsEnableRequestCurrent(long request) { return hardcoreGuard.IsCurrent(request); }
        public static void RefreshHardcoreProtection()
        {
            // Disabling never changes a timer or automatically enables the helper again.
            if (hardcoreGuard.Refresh()) Disable();
        }
        public static void TryAutoOpen()
        {
            long request = hardcoreGuard.BeginAutoStart();
            if (request >= 0) Open(request);
        }
        [DllImport("User32.dll", EntryPoint = "SendMessage")]
        private static extern IntPtr SendMessage(int hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("User32.dll", EntryPoint = "FindWindow")]
        private static extern int FindWindow(string lpClassName, string lpWindowName);

        private const int TIMERCALL = 0x8822;
        private const int TSTAT = 1;
        private const int TEDIT = 2;
        private const int TEXIT = 3;
        private const int TENABLE = 4;
        private const int TDISABLE = 5;
        private const int TBLOCKCTRLENTER = 6;

        private const string tar = "KeyChanger";
        private static int call(int act,int data=0)
        {
            int hwnd = FindWindow(null, "改键器");
            if (hwnd == 0)
            {
                // 兼容旧版改键器窗口标题
                hwnd = FindWindow(null, "改建器");
            }
            if (hwnd != 0)
            {
                return SendMessage(hwnd, TIMERCALL, (IntPtr)act, (IntPtr)data).ToInt32();
            }
            return 0;
        }

        private static Process kcp = null;
        public static void Open()
        {
            hardcoreGuard.TryRun(OpenCore, revoke: Disable);
        }
        internal static void Open(long request) { hardcoreGuard.TryRun(OpenCore, request, Disable); }
        private static void OpenCore()
        {
            if (kcp == null)
            {
                Process[] pss = Process.GetProcessesByName(tar);
                if (pss.Length <= 0)
                {
                    string delpath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName) + "\\" + tar + ".exe";
                    if (File.Exists(delpath))
                    {
                        kcp = new Process();

                        kcp.StartInfo.FileName = delpath;
                        //kcp.StartInfo.UseShellExecute = false;
                        //kcp.StartInfo.RedirectStandardOutput = true;
                        //kcp.StartInfo.CreateNoWindow = true;
                        try
                        {
                            kcp.Start();
                        }
                        catch
                        {
                            try
                            {
                                kcp.Close();
                            }
                            catch { }
                            try
                            {
                                kcp.Dispose();
                            }
                            catch { }
                            kcp = null;
                        }
                    }
                }
            }
        }
        public static void Close()
        {
            try
            {
                call(TEXIT);
            }
            catch { }
            if (kcp != null)
            {
                try
                {
                    kcp.Close();
                }
                catch { }
                try
                {
                    kcp.Dispose();
                }
                catch { }
                kcp = null;
            }
        }
        public static bool IsEnable()
        {
            return call(TSTAT) == 1;
        }
        public static bool IsWindowOpen()
        {
            return FindWindow(null, "改键器") != 0 || FindWindow(null, "改建器") != 0;
        }
        public static void Edit()
        {
            hardcoreGuard.TryRun(() => call(TEDIT), revoke: Disable);
        }
        internal static void Edit(long request) { hardcoreGuard.TryRun(() => call(TEDIT), request, Disable); }
        public static void BlockCtrlEnter(bool isenable)
        {
            call(TBLOCKCTRLENTER, (isenable ? 1 : 0));
        }
        public static void Enable()
        {
            hardcoreGuard.TryRun(() => call(TENABLE), revoke: Disable);
        }
        internal static void Enable(long request) { hardcoreGuard.TryRun(() => call(TENABLE), request, Disable); }
        public static void Disable()
        {
            call(TDISABLE);
        }
    }
}
