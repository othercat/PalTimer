using HFrame.ENT;
using HFrame.EX;
using HFrame.OS;
using PalCloudLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Pal98Timer
{

    public class 仙剑98DX9 : TimerCore
    {
        public override bool IsShowC()
        {
            Process[] res = Process.GetProcessesByName("Pal98Robot");
            if (res.Length > 0)
            {
                return true;
            }
            return false;
        }
        private string GMD5 = "none";
        private string DX9Version = "未知";
        public IntPtr PalHandle;
        public IntPtr GameWindowHandle = IntPtr.Zero;
        private int PID = -1;
        private Process PalProcess;
        private bool _HasGameStart = false;
        private bool _IsFirstStarted = false;

        private PTimer ST = new PTimer();
        private PTimer LT = new PTimer();
        private DateTime InBattleTime;
        private DateTime OutBattleTime;
        public TimeSpan BattleLong = new TimeSpan(0);
        private bool HasUnCheated = false;
        private bool IsInUnCheat = false;

        private bool IsPause = false;

        private short MaxFC = 0;
        private short MaxFM = 0;
        private short MaxHCG = 0;
        private short MaxXLL = 0;
        private short MaxLQJ = 0;
        private short MaxYXY = 0;
        private short MaxTLF = 0;
        private short MaxQTJ = 0;
        private bool IsInBattle = false;

        private bool IsDoMoreEndBattle = true;
        private string WillCopyRPG = "";

        private string cryerror = "";
        
        private GameObject GameObj = new GameObject();

        private List<string> NamedBattleRes = new List<string>();

        private bool IsShowSpeed = false;
        private bool HasAlertMutiPal = false;

        public 仙剑98DX9(GForm form) : base(form)
        {
            CoreName = "PAL98DX9";
        }

        protected override void InitCheckPoints()
        {
            LoadBest();
            _CurrentStep = -1;
            Data = new HObj();
            Data["caiyi"] = false;
            CheckPoints = new List<CheckPoint>();
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest("见石碑", new TimeSpan(0, 6, 5)))
            {
                Check = delegate ()
                {
                    if (PositionCheck(new int[3] { 19, 1696, 384 }, new int[3] { 19, 1680, 376 })
                        || PositionAroundCheck(19, 1696, 384))
                    {
                        return true;
                    }
                    return false;
                }
            });
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest("学功夫", new TimeSpan(0, 11, 13)))
            {
                Check = delegate ()
                {
                    if (GameObj.AreaBGM == 86)
                    {
                        return true;
                    }
                    return false;
                }
            });
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest("上船", new TimeSpan(0, 18, 37)))
            {
                Check = delegate ()
                {
                    //if (PositionCheck(new int[3] { 6, 1072, 1080 }, new int[3] { 6, 1088, 1088 }))
                    if (PositionAroundCheck(6, 1072, 1080, 2))
                    {
                        return true;
                    }
                    return false;
                }
            });
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest("出林家堡", new TimeSpan(0, 24, 53)))
            {
                Check = delegate ()
                {
                    if (PositionAroundCheck(40, 1456, 872, 5))
                    {
                        return true;
                    }
                    return false;
                }
            });
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest("出隐龙窟", new TimeSpan(0, 30, 46)))
            {
                Check = delegate ()
                {
                    if (PositionAroundCheck(49, 304, 1560, 5))
                    {
                        return true;
                    }
                    return false;
                }
            });
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest("生化危机", new TimeSpan(0, 37, 56)))
            {
                Check = delegate ()
                {
                    //if (PositionCheck(new int[3] { 62, 1152, 1264 }))
                    if (PositionAroundCheck(62, 1152, 1264, 2))
                    {
                        return true;
                    }
                    return false;
                }
            });
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest("过鬼将军", new TimeSpan(0, 43, 25)))
            {
                Check = delegate ()
                {
                    if (GameObj.BossID == 75 && GameObj.BattleTotalBlood <= 0)
                    {
                        return true;
                    }
                    return false;
                }
            });
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest("过赤鬼王", new TimeSpan(0, 47, 45)))
            {
                Check = delegate ()
                {
                    if (GameObj.BossID == 76 && GameObj.BattleTotalBlood <= 0)
                    {
                        return true;
                    }
                    return false;
                }
            });
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest("进扬州", new TimeSpan(0, 54, 0)))
            {
                Check = delegate ()
                {
                    //if (PositionCheck(new int[3] { 83, 320, 1056 }))
                    if (PositionAroundCheck(80, 256, 1344, 5))
                    {
                        return true;
                    }
                    return false;
                }
            });
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest("出扬州", new TimeSpan(1, 1, 53)))
            {
                Check = delegate ()
                {
                    //if (PositionCheck(new int[3] { 85, 1136, 536 }))
                    if (PositionAroundCheck(106, 64, 960, 5))
                    {
                        return true;
                    }
                    return false;
                }
            });
            CheckPoints.Add(new CheckPoint(CheckPoints.Count, GetBest("出麻烦洞", new TimeSpan(1, 7, 26)))
            {
                Check = delegate ()
                {
                    //if (PositionCheck(new int[3] { 107, 1520, 408 }))
                    if (PositionAroundCheck(107, 1520, 408, 5))
                    {
                        return true;
                    }
                    return false;
                }
            });
        }

        public override string GetGameVersion()
        {
            if (PID != -1)
            {
                return "新补丁 " + DX9Version;
            }
            else
            {
                return "等待游戏运行";
            }
        }

        public override void Reset()
        {
            base.Reset();
            MoveSpeed = 0;
            HasAlertMutiPal = false;
            HasUnCheated = false;
            IsInUnCheat = false;
            ST.Stop();
            _IsFirstStarted = false;
            MaxFC = 0;
            MaxFM = 0;
            MaxHCG = 0;
            MaxLQJ = 0;
            MaxXLL = 0;
            MaxYXY = 0;
            MaxQTJ = 0;
            MaxTLF = 0;
            BattleLong = new TimeSpan(0);
            ST.Reset();
            NamedBattleRes = new List<string>();
        }

        protected override void Checking()
        {
            if (GetPalHandle())
            {
                if (cryerror != "")
                {
                    Error(cryerror);
                    cryerror = "";
                }
            }
            else
            {
                if (cryerror != "")
                {
                    Error(cryerror);
                    cryerror = "";
                }
                return;
            }

            if (PID == -1) return;

            base.Checking();
        }

        private bool GetPalHandle()
        {
            Process[] res = Process.GetProcessesByName("Pal");
            
            // 功能2: 过滤已退出的进程
            if (res.Length > 1)
            {
                var aliveProcesses = res.Where(p => {
                    try { return !p.HasExited; }
                    catch { return false; }
                }).ToArray();
                
                if (aliveProcesses.Length > 1)
                {
                    if (!HasAlertMutiPal)
                    {
                        cryerror = "检测到多个Pal.exe进程，请关闭其他的，只保留一个！";
                        HasAlertMutiPal = true;
                    }
                    return false;
                }
                res = aliveProcesses;
            }

            HasAlertMutiPal = false;
            if (res.Length > 0)
            {
                if (PID == -1)
                {
                    IntPtr tempHandle = res[0].MainWindowHandle;
                    StringBuilder sb = new StringBuilder(256);
                    User32.GetWindowText(tempHandle, sb, sb.Capacity);
                    string windowTitle = sb.ToString();
                    
                    // 功能1: 窗口标题识别
                    if (windowTitle.Contains("仙剑奇侠传") && windowTitle.Contains("DX9移植版"))
                    {
                        // 提取版本号
                        int versionStartIndex = windowTitle.IndexOf("(v");
                        if (versionStartIndex != -1)
                        {
                            int versionEndIndex = windowTitle.IndexOf(")", versionStartIndex);
                            if (versionEndIndex != -1)
                            {
                                DX9Version = windowTitle.Substring(versionStartIndex + 2, versionEndIndex - versionStartIndex - 2);
                            }
                        }
                        
                        PalProcess = res[0];
                        GameWindowHandle = res[0].MainWindowHandle;
                        PID = PalProcess.Id;
                        PalHandle = new IntPtr(Kernel32.OpenProcess(0x1F0FFF, false, PID));
                        CalcPalMD5();
                        return true;
                    }
                    else
                    {
                        cryerror = "请使用仙剑98 DX9移植版！";
                        return false;
                    }
                }
                else
                {
                    if (PID == res[0].Id)
                    {
                        if (GMD5 == "none")
                        {
                            CalcPalMD5();
                        }
                        return true;
                    }
                    else
                    {
                        PalHandle = IntPtr.Zero;
                        GameWindowHandle = IntPtr.Zero;
                        PalProcess = null;
                        PID = -1;
                        GMD5 = "none";
                        DX9Version = "未知";
                        return false;
                    }
                }
            }
            else
            {
                PalHandle = IntPtr.Zero;
                GameWindowHandle = IntPtr.Zero;
                PalProcess = null;
                PID = -1;
                GMD5 = "none";
                DX9Version = "未知";
                return false;
            }
        }

        private void CalcPalMD5()
        {
            try
            {
                string dllmd5 = GetFileMD5(GetGameFilePath("Pal.dll"));
                string datamd5 = GetFileMD5(GetGameFilePath("DATA.MKF"));
                string sssmd5 = GetFileMD5(GetGameFilePath("SSS.MKF"));
                string vb40032md5 = GetFileMD5(GetGameFilePath("VB40032.dll"));
                GMD5 = dllmd5 + "_" + datamd5 + "_" + sssmd5 + "_" + vb40032md5;
            }
            catch
            {
                GMD5 = "none";
            }
        }

        private string GetGameFilePath(string fn)
        {
            string palpath = PalProcess.MainModule.FileName;
            string[] spli = palpath.Split('\\');
            spli[spli.Length - 1] = fn;
            palpath = "";
            foreach (string s in spli)
            {
                palpath += s + "\\";
            }
            if (palpath != "")
            {
                palpath = palpath.Substring(0, palpath.Length - 1);
            }
            return palpath;
        }
    }
}
