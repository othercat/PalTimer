using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using PalCloudLib;

namespace Pal98Timer
{
    public partial class GForm : NoneBoardFormEx
    {
        public const string CurrentVersion = "3.37.1";
        public const string bgpath = @"bg.png";
        private TimerCore core;
        private bool IsAutoLuck = false;
        private Dictionary<string, ToolStripMenuItem> CoreBtns;
        private int transparencyValue = 100; // 背景图透明度 0-100, 100为不透明
        private ObsWindowStyleSettings obsWindowStyleSettings;
        private bool obsPresentationFailureHandled;

        internal Keys ObsWindowStyleToggleHotkey
        {
            get
            {
                return obsWindowStyleSettings == null ? Keys.None : obsWindowStyleSettings.ToggleHotkey;
            }
        }

        public GRender rr;
        public GRender.GBtn btnPause;
        private GRender.GBtn btnReset;
        private GRender.GBtn btnData;
        private GRender.GBtn btnCloud;
        private ContextMenuStrip cmCloud;
        private ToolStripMenuItem btnCloudInit;
        private PCloud cloud;
        private KeyboardLib _keyboardHook = null;
        private Keys ActiveCustomHotkey = Keys.None;
        private int locx = 0;
        private int locy = 0;
        private bool IsCriticalExitRequested = false;
        public GForm():base(true)
        {
            _keyboardHook = new KeyboardLib();
            _keyboardHook.InstallHook(this.OnKeyPress);
            InitializeComponent();
            obsWindowStyleSettings = ObsWindowStyleStore.Load();
            UpdateObsWindowStyleMenu();
            this.FormClosing += GForm_FormClosing;
            this.FormClosed += GForm_FormClosed;
            this.Shown += GForm_Shown;

            string filepath = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName) + "\\size";
            try
            {
                if (File.Exists(filepath))
                {
                    using (FileStream fs = new FileStream(filepath, FileMode.Open, FileAccess.Read))
                    {
                        using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                        {
                            string[] sizestr = sr.ReadToEnd().Split('*');
                            this.Width = int.Parse(sizestr[0]);
                            this.Height = int.Parse(sizestr[1]);
                            if (sizestr.Length > 2)
                            {
                                locx = int.Parse(sizestr[2]);
                            }
                            if (sizestr.Length > 3)
                            {
                                locy = int.Parse(sizestr[3]);
                            }
                        }
                    }
                }
            }
            catch
            { }

            GBoard bb = new GBoard();
            bb.Load();
            rr = new GRender(this);
            rr.OnDBClickItem = OnDBClickItem;
            rr.OnMainTimerDBClicked = OnMainTimerDBClicked;
            rr.SetGBoard(bb);
            rr.SetBG(bgpath);
            rr.OnConfigClicked = delegate (int x, int y) {
                mnMain.Show(this, x, y);
            };
            rr.OnCloseClicked = delegate () {
                this.Close();
            };

            cmCloud= new ContextMenuStrip(this.components);
            btnCloudInit = new ToolStripMenuItem();
            btnCloudInit.Text = "重新验证";
            btnCloudInit.Enabled = false;
            cmCloud.Items.Add(btnCloudInit);

            btnPause = rr.AddBtn("暂停", delegate (int x, int y, GRender.GBtn btn) { UIPause(); }, 9);
            btnReset = rr.AddBtn("重置", delegate (int x, int y, GRender.GBtn btn) { btnReset_Click(null, null); }, 10);
            btnData = rr.AddBtn("功能", delegate (int x, int y, GRender.GBtn btn) { mnData.Show(this,x,y); }, 20);
            btnCloud = rr.AddBtn("云", delegate (int x, int y, GRender.GBtn btn) { cmCloud.Show(this,x,y); }, 30);
            btnCloudInit.Click += delegate (object sender, EventArgs e) {
                InitCloud();
            };

            LoadNonSequentialCheck();
            ApplyAutomationOptions();

            CoreBtns = new Dictionary<string, ToolStripMenuItem>();
            List<string> cores = TimerCore.GetAllCores();
            foreach (string cn in cores)
            {
                ToolStripMenuItem ti = new ToolStripMenuItem();
                ti.Text = TimerCore.GetCoreDisplayName(cn);
                ti.Click += delegate (object sender, EventArgs e) {
                    if (Confirm("确定更换内核么？这将重置计时器"))
                    {
                        LoadCore(TimerCore.GetCoreIns(cn,this));
                    }
                };
                mnMain.Items.Add(ti);
                CoreBtns.Add(cn, ti);
            }
            try
            {
                if (File.Exists("LastCore"))
                {
                    string lc = "";
                    Encoding charset = TimerCore.GetFileEncodeType("LastCore");
                    using (FileStream fileStream = new FileStream("LastCore", FileMode.Open))
                    {
                        using (StreamReader sr = new StreamReader(fileStream, charset))
                        {
                            lc = sr.ReadToEnd();
                        }
                    }
                    LoadCore(TimerCore.GetCoreIns(lc,this));
                }
                else
                {
                    throw new Exception("LoadDefaultCore");
                }
            }
            catch (Exception ex)
            {
                LoadCore(new 仙剑98柔情DX9(this));
            }
            
            rr.SetVersion(CurrentVersion);

            ShowKCEnable();
            LoadTransparency();
        }

        private void GForm_Shown(object sender, EventArgs e)
        {
            this.SetDesktopBounds(locx, locy, this.Width, this.Height);
            try
            {
                UpdateTransparency();
            }
            catch (Exception ex)
            {
                DisableObsWindowStyleAfterFailure(ex);
            }
        }

        private void InitCloud()
        {
            if (cloud != null)
            {
                cloud.Stop();
            }
            cloud = new PCloud(this.core.CoreName, delegate (int cid)
            {
                if (cid < 0)
                {
                    switch (cid)
                    {
                        case -2:
                            btnCloud.Text = "正在初始化";
                            UISetBtnCloudInitEnable(false);
                            UI(delegate () {
                                try
                                {
                                    core?.OnCloudPending();
                                }
                                catch { }
                            });
                            break;
                        case -3:
                            btnCloud.Text = "云";
                            UISetBtnCloudInitEnable(true);
                            UI(delegate () {
                                try
                                {
                                    core?.OnCloudFail();
                                }
                                catch { }
                                //Error(cloud.LastError);
                            });
                            break;
                        default:
                            btnCloud.Text = "云";
                            UISetBtnCloudInitEnable(true);
                            UI(delegate () {
                                try
                                {
                                    core?.OnCloudFail();
                                }
                                catch { }
                            });
                            break;
                    }
                }
                else
                {
                    btnCloud.Text = "云ID:" + cid;
                    UISetBtnCloudInitEnable(false);
                    UI(delegate () {
                        try
                        {
                            core?.OnCloudOK();
                        }
                        catch { }
                    });
                }
            });
            cloud.OnCloudTickBefore = delegate (int NextDo)
            {
                if (core.HasPlugin(TimerPluginBase.TimerPlugin.EPluginPosition.BL))
                {
                    cloud.PutPluginData("BL", core.GetPluginResult(TimerPluginBase.TimerPlugin.EPluginPosition.BL));
                }

                if (core.HasPlugin(TimerPluginBase.TimerPlugin.EPluginPosition.BR))
                {
                    cloud.PutPluginData("BR", core.GetPluginResult(TimerPluginBase.TimerPlugin.EPluginPosition.BR));
                }

                if (core.HasPlugin(TimerPluginBase.TimerPlugin.EPluginPosition.Title))
                {
                    cloud.PutPluginData("Title", core.GetPluginResult(TimerPluginBase.TimerPlugin.EPluginPosition.Title));
                }


                cloud.PutIsC(rr.IsC);

                switch (NextDo)
                {
                    case 0:
                        if (!core.CustomCloudLiteData())
                        {
                            cloud.PutLiteData(core.ForCloudLiteData());
                        }
                        break;
                    case 1:
                        if (!core.CustomCloudBigData())
                        {
                            cloud.PutBigData(core.ForCloudBigData());
                        }
                        break;
                    case 2:
                        if (!core.CustomCloudBigData())
                        {
                            cloud.PutBigData(core.ForCloudBigData());
                        }
                        if (!core.CustomCloudLiteData())
                        {
                            cloud.PutLiteData(core.ForCloudLiteData());
                        }
                        break;
                }
            };
            cloud.Start();
        }
        public int CloudID()
        {
            if (cloud == null) return int.MinValue;
            return cloud.CloudID;
        }
        public void PutLiteData(string data)
        {
            if (cloud == null) return;
            cloud.PutLiteData(data);
        }
        public void PutBigData(string data)
        {
            if (cloud == null) return;
            cloud.PutBigData(data);
        }

        private void UISetBtnCloudInitEnable(bool isEnable)
        {
            UI(delegate () {
                if (btnCloudInit == null) return;
                btnCloudInit.Enabled = isEnable;
            });
        }
        public void OUpload(string LocalFileName, string RemoteFileName = "")
        {
            if (cloud == null || cloud.CloudID < 0) throw new Exception("版本不匹配");
            cloud.OUpload(LocalFileName, RemoteFileName);
        }

        public void ODownload(string RemoteFileName, string LocalFileName)
        {
            if (cloud == null || cloud.CloudID < 0) throw new Exception("版本不匹配");
            cloud.ODownload(RemoteFileName, LocalFileName);
        }

        private int HandPauseCount = 0;
        public int ManualPauseCount
        {
            get { return HandPauseCount; }
        }

        public void UIPause()
        {
            if (core != null)
            {
                if (!core.IsUIPause)
                {
                    HandPauseCount++;
                }
                SetUIPause(!core.IsUIPause);
                if (HandPauseCount > 0)
                {
                    btnPause.Text = "暂停 " + HandPauseCount;
                }
                else
                {
                    btnPause.Text = "暂停 ";
                }
            }
        }
        public void SetUIPause(bool isp)
        {
            if (core != null)
            {
                core.IsUIPause = isp;
                if (core.IsUIPause)
                {
                    btnPause.Red();
                }
                else
                {
                    btnPause.White();
                }
            }
        }

        public void LoadCore(TimerCore core)
        {
            //Success(core.GetType().Name);
            if (this.core != null)
            {
                try
                {
                    CoreBtns[this.core.GetType().Name].Checked = false;
                }
                catch { }
                this.core.Unload();
                this.core.UnloadUI();
            }
            try
            {
                CoreBtns[core.GetType().Name].Checked = true;
            }
            catch { }
            try
            {
                if (File.Exists("LastCore"))
                {
                    File.Delete("LastCore");
                }
                using (FileStream fileStream = new FileStream("LastCore", FileMode.Create))
                {
                    using (StreamWriter streamWriter = new StreamWriter(fileStream, Encoding.UTF8))
                    {
                        streamWriter.Write(core.GetType().Name);
                        streamWriter.Flush();
                    }
                }
            }
            catch { }
            this.core = core;
            InitCloud();
            this.core.LoadCore = LoadCore;
            this.core.InitUI();
            this.core.OnCurrentStepChanged = delegate (int curidx)
            {
                if (rr != null)
                {
                    rr.ItemIdx = curidx;
                }
                if (IsAutoLuck)
                {
                    rr?.SetBL(MConfig.ins.Luck(true));
                }
                if (curidx > 0)
                {
                    WriteAutomationSnapshot("checkpoint");
                }
            };
            _ResetAll();
            if (rr != null)
            {
                rr.IsForceRefreshAll = true;
            }
            core.Start();
            WriteAutomationSnapshot("core_loaded");
        }

        public void WriteAutomationSnapshot(string trigger)
        {
            if (!AutomationArgs.Current.Enabled || core == null)
            {
                return;
            }

            try
            {
                string exportPath = AutomationArgs.Current.SnapshotExportPath;
                string dir = Path.GetDirectoryName(exportPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                using (FileStream fileStream = new FileStream(exportPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    using (StreamWriter streamWriter = new StreamWriter(fileStream, new UTF8Encoding(false)))
                    {
                        streamWriter.Write(core.BuildAutomationSnapshotJson(trigger, AutomationArgs.Current.SnapshotRunId));
                        streamWriter.Flush();
                    }
                }
            }
            catch
            {
                // Automation export must not affect normal timer behavior.
            }
        }

        public void _ResetAll()
        {
            core.UnloadPlugins();
            core.LoadPlugins();
            HandPauseCount = 0;
            btnPause.Text = "暂停";
            btnPause.White();
            core.Reset();
            rr?.ClearAllDots();
            rr?.SetGameVersion("");
            rr?.SetSubTimer("");
            rr?.SetOutTimer("");
            rr?.SetMoreInfo("");
            rr?.SetMainTimer(new TimeSpan(0));
            rr.SetWillClear("");
            MConfig.ins.LoadConfig();
            ShowConfigs();
            InitCheckPoints();
        }
        public void ShowConfigs()
        {
            rr?.SetTitle(MConfig.ins.Title);
            
            // Only set BL to Luck if there's no active BL plugin
            if (!core.HasPlugin(TimerPluginBase.TimerPlugin.EPluginPosition.BL))
            {
                rr?.SetBL(MConfig.ins.Luck(true));
            }
            
            // Only set BR to ColorEgg if there's no active BR plugin
            if (!core.HasPlugin(TimerPluginBase.TimerPlugin.EPluginPosition.BR))
            {
                rr?.SetBR(MConfig.ins.ColorEgg);
            }
            
            /*lblMTFront.ForeColor = MConfig.ins.MainColor;
            lblMTBack.ForeColor = MConfig.ins.MainColor;
            lblST.ForeColor = MConfig.ins.MainColor;*/
        }
        public void InitCheckPoints()
        {
            core.InitCheckPointsEx();
            rr?.ClearAllItem();
            if (core.CheckPoints != null)
            {
                foreach (CheckPoint cp in core.CheckPoints)
                {
                    var item = rr?.AddItem(cp.GetNickName(), cp.Best);
                    cp.SetUIItem(item);
                    item.Cur = cp.Current;
                }
                rr.ItemIdx = -1;
                //core.CurrentStep = 0;
                core.Jump(0);
            }
        }
        public bool OnCtrlDown2 = false;
        public bool OnCtrlDown = false;
        public void OnKeyPress(KeyboardLib.HookStruct hookStruct, out bool handle)
        {
            handle = false; //预设不拦截任何键
            if (((Keys)(hookStruct.vkCode)) == Keys.Enter && (OnCtrlDown || OnCtrlDown2) && this.core != null && this.core.NeedBlockCtrlEnter())
            {
                handle = true;
            }
            switch ((Keys)(hookStruct.vkCode))
            {
                case Keys.RControlKey:
                    if (hookStruct.flags >= 128)
                    {
                        OnCtrlDown2 = false;
                    }
                    else
                    {
                        OnCtrlDown2 = true;
                    }
                    break;
                case Keys.LControlKey:
                    if (hookStruct.flags >= 128)
                    {
                        OnCtrlDown = false;
                    }
                    else
                    {
                        OnCtrlDown = true;
                    }
                    break;
            }
            // 全局自定义快捷键共用一个锁存状态，基准键抬起前不允许系统连发重复切换。
            Keys keyCode = (Keys)(hookStruct.vkCode);
            if (ActiveCustomHotkey != Keys.None && (ActiveCustomHotkey & Keys.KeyCode) == keyCode)
            {
                handle = true;
                if (hookStruct.flags >= 128)
                {
                    ActiveCustomHotkey = Keys.None;
                }
                return;
            }

            // OBS 主窗口样式快捷键优先用于恢复普通界面。配置入口会阻止它与其它开关冲突；
            // 即使用户后来手工制造冲突，也让恢复可见界面的快捷键保持优先。
            if (hookStruct.flags < 128 && ObsWindowStyleToggleHotkey != Keys.None &&
                string.IsNullOrEmpty(Dx9OverlaySettings.ValidateToggleHotkey(
                    ObsWindowStyleToggleHotkey,
                    Keys.None)))
            {
                Keys pressed = keyCode;
                if (OnCtrlDown || OnCtrlDown2) pressed |= Keys.Control;
                if (Control.ModifierKeys.HasFlag(Keys.Shift)) pressed |= Keys.Shift;
                if (Control.ModifierKeys.HasFlag(Keys.Alt)) pressed |= Keys.Alt;

                if (pressed == ObsWindowStyleToggleHotkey)
                {
                    ActiveCustomHotkey = pressed;
                    handle = true;
                    UI(delegate () { ToggleObsWindowStyleEnabled(); });
                    return;
                }
            }
            // 音效开关快捷键（在修饰键状态更新后、功能键处理前检查）
            if (hookStruct.flags < 128 && SoundConfig.ins.ToggleHotkey != Keys.None)
            {
                Keys pressed = (Keys)(hookStruct.vkCode);
                if (OnCtrlDown || OnCtrlDown2) pressed |= Keys.Control;
                if (Control.ModifierKeys.HasFlag(Keys.Shift)) pressed |= Keys.Shift;
                if (Control.ModifierKeys.HasFlag(Keys.Alt)) pressed |= Keys.Alt;

                if (pressed == SoundConfig.ins.ToggleHotkey)
                {
                    handle = true;
                    bool newState = !SoundConfig.ins.GlobalEnabled;
                    SoundConfig.ins.GlobalEnabled = newState;
                    SoundConfig.ins.SaveConfig();
                    SoundConfig.ins.PlayToggleSound(newState);
                    UI(delegate () { btnSoundConfig.Checked = newState; });
                    return;
                }
            }

            // 内核专用组合键复用现有全局钩子，不新增线程或键盘钩子。
            if (hookStruct.flags < 128 && core != null)
            {
                Keys pressed = keyCode;
                if (OnCtrlDown || OnCtrlDown2) pressed |= Keys.Control;
                if (Control.ModifierKeys.HasFlag(Keys.Shift)) pressed |= Keys.Shift;
                if (Control.ModifierKeys.HasFlag(Keys.Alt)) pressed |= Keys.Alt;
                if (core.TryHandleCustomHotkey(pressed))
                {
                    ActiveCustomHotkey = pressed;
                    handle = true;
                    return;
                }
            }
            switch ((Keys)(hookStruct.vkCode))
            {
                case Keys.F1:
                    if (hookStruct.flags >= 128)
                    {
                        core.OnFunctionKey(1);
                    }
                    handle = core.NeedBlockFunctionKey(1);
                    break;
                case Keys.F2:
                    if (hookStruct.flags >= 128)
                    {
                        core.OnFunctionKey(2);
                    }
                    handle = core.NeedBlockFunctionKey(2);
                    break;
                case Keys.F3:
                    if (hookStruct.flags >= 128)
                    {
                        core.OnFunctionKey(3);
                    }
                    handle = core.NeedBlockFunctionKey(3);
                    break;
                case Keys.F4:
                    if (hookStruct.flags >= 128)
                    {
                        core.OnFunctionKey(4);
                    }
                    handle = core.NeedBlockFunctionKey(4);
                    break;
                case Keys.F5:
                    if (hookStruct.flags >= 128)
                    {
                        core.OnFunctionKey(5);
                    }
                    handle = core.NeedBlockFunctionKey(5);
                    break;
                case Keys.F6:
                    if (hookStruct.flags >= 128)
                    {
                        core.OnFunctionKey(6);
                    }
                    handle = core.NeedBlockFunctionKey(6);
                    break;
                case Keys.F7:
                    if (hookStruct.flags >= 128)
                    {
                        core.OnFunctionKey(7);
                    }
                    handle = core.NeedBlockFunctionKey(7);
                    break;
                case Keys.F8:
                    if (hookStruct.flags >= 128)
                    {
                        core.OnFunctionKey(8);
                    }
                    handle = core.NeedBlockFunctionKey(8);
                    break;
                case Keys.F9:
                    if (hookStruct.flags >= 128)
                    {
                        UIPause();
                        core.OnFunctionKey(9);
                    }
                    handle = core.NeedBlockFunctionKey(9);
                    break;
                case Keys.F10:
                    if (hookStruct.flags >= 128)
                    {
                        btnReset_Click(null, null);
                        core.OnFunctionKey(10);
                    }
                    handle = core.NeedBlockFunctionKey(10);
                    break;
                case Keys.F11:
                    if (hookStruct.flags >= 128)
                    {
                        if (KeyChangerDel.IsEnable())
                        {
                            KeyChangerDel.Disable();
                            ShowKCEnable();
                            KeyChangerDel.Close();
                        }
                        else
                        {
                            Run(delegate() {
                                KeyChangerDel.Open();
                                // Wait for the KeyChanger window to be ready (up to 3 seconds)
                                for (int i = 0; i < 30 && !KeyChangerDel.IsWindowOpen(); i++)
                                {
                                    System.Threading.Thread.Sleep(100);
                                }
                                KeyChangerDel.Enable();
                                UI(delegate() { ShowKCEnable(); });
                            });
                        }
                        core.OnFunctionKey(11);
                    }
                    handle = core.NeedBlockFunctionKey(11);
                    break;
                case Keys.F12:
                    if (hookStruct.flags >= 128)
                    {
                        core.OnFunctionKey(12);
                    }
                    handle = core.NeedBlockFunctionKey(12);
                    break;
            }
        }
        private void btnReset_Click(object sender, EventArgs e)
        {
            if (Confirm("确定要重置计时器么？"))
            {
                _ResetAll();
            }
        }
        private void ShowKCEnable()
        {
            /*if (kc != null && kc.IsEnable)
            {
                btnData.Orange();
            }
            else
            {
                btnData.White();
            }*/
            if (KeyChangerDel.IsEnable())
            {
                btnData.Orange();
            }
            else
            {
                btnData.White();
            }
        }

        private void GForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            _keyboardHook.UninstallHook();
            KeyChangerDel.Close();
            Environment.Exit(0);
        }

        private void GForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (core != null && core.CoreName != "S")
            {
                string sizestr = this.Width + "*" + this.Height + "*" + this.DesktopBounds.X + "*" + this.DesktopBounds.Y;
                string filepath = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName) + "\\size";
                if (File.Exists(filepath)) File.Delete(filepath);
                using (FileStream fs = new FileStream(filepath, FileMode.Create, FileAccess.ReadWrite))
                {
                    using (StreamWriter sw = new StreamWriter(fs, Encoding.UTF8))
                    {
                        sw.Write(sizestr);
                        sw.Flush();
                    }
                }
            }
            if (IsCriticalExitRequested)
            {
                return;
            }
            if (!Confirm("确定退出计时器么？"))
            {
                e.Cancel = true;
                return;
            }
        }

        public void OnMainTimerDBClicked()
        {
            SetTime();
        }
        private void SetTime()
        {
            TSSet tss = new TSSet(delegate (TimeSpan ts) {
                try
                {
                    core.SetTS(ts);
                }
                catch { }
            });
            tss.ShowDialog(this);
            tss.Dispose();
        }

        public void OnDBClickItem(GRender.GItem item)
        {
            if (core != null)
            {
                if (Confirm("确定转到【"+item.Name+"】节点么？"))
                {
                    //core.CurrentStep = item.Index;
                    core.Jump(item.Index);
                }
            }
        }

        public void CallCloudFinishOne()
        {
            try
            {
                cloud?.FinishOne();
            }
            catch { }
            try
            {
                InitCloud();
            }
            catch { }
        }

        private void tmMain_Tick(object sender, EventArgs e)
        {
            if (core != null)
            {
                /*if (core.CurrentStep >= 0 && core.CurrentStep < core.CheckPoints.Count)
                {
                    core.CheckPoints[core.CurrentStep].Current = core.GetMainWatch();
                }*/
                rr.IsC= core.IsShowC();

                string cryerr = core.GetCriticalError();
                if (cryerr != "")
                {
                    Error(cryerr);
                    if (cryerr == TimerCore.ElevatedPalProcessErrorMessage)
                    {
                        IsCriticalExitRequested = true;
                        Close();
                        return;
                    }
                }
                rr.SetGameVersion(core.GetGameVersion());
                rr.SetWillClear(core.GetPointEnd());
                rr.SetPointSpan(core.GetPointSpan());
                rr.SetSubTimer(core.GetSmallWatch());
                rr.SetOutTimer(core.GetSecondWatch());
                rr.SetMainTimer(core.GetMainWatch());
                rr.IsInCheck = core.IsMainWatchStar();
                rr.SetMoreInfo(core.GetMoreInfo());
                string aaction = core.GetAAction() + core.AAction;
                core.AAction = "";
                if (aaction != "")
                {
                    string[] aaspli = aaction.Split('|');
                    foreach (string aas in aaspli)
                    {
                        if (aas.Trim() != "")
                        {
                            rr.AddDot(aas);
                        }
                    }
                }
                if (core.HasPlugin(TimerPluginBase.TimerPlugin.EPluginPosition.BL))
                {
                    rr.SetBL(core.GetPluginResult(TimerPluginBase.TimerPlugin.EPluginPosition.BL));
                }
                if (core.HasPlugin(TimerPluginBase.TimerPlugin.EPluginPosition.BR))
                {
                    rr.SetBR(core.GetPluginResult(TimerPluginBase.TimerPlugin.EPluginPosition.BR));
                }
                if (core.HasPlugin(TimerPluginBase.TimerPlugin.EPluginPosition.Title))
                {
                    rr.SetTitle(core.GetPluginResult(TimerPluginBase.TimerPlugin.EPluginPosition.Title));
                }
            }
            else
            {
                rr.IsC = false;
                rr.IsInCheck = false;
                try
                {
                    cloud?.PutIsC(false);
                }
                catch { }
            }
            if (rr != null && rr.Draw(delegate (Rectangle? rect) {
                if (obsWindowStyleSettings != null && obsWindowStyleSettings.Enabled)
                {
                    return;
                }
                if (rect == null)
                {
                    Invalidate();
                }
                else
                {
                    Invalidate(rect.Value);
                }
            }))
            {
                //Invalidate();
            }
            if (obsWindowStyleSettings != null && obsWindowStyleSettings.Enabled)
            {
                try
                {
                    PresentObsWindowFrame();
                }
                catch (Exception ex)
                {
                    DisableObsWindowStyleAfterFailure(ex);
                }
            }
        }
        
        private void btnKeyChange_Click(object sender, EventArgs e)
        {
            /*IsKeyInEdit = true;
            KeysForm kf = new KeysForm(this);
            kf.ShowDialog(this);
            ApplyKeyChange();
            ShowKCEnable();
            IsKeyInEdit = false;*/
            Run(delegate () {
                string ps = this.DesktopBounds.X + "," + this.DesktopBounds.Y + "," + this.DesktopBounds.Width + "," + this.DesktopBounds.Height;
                using (FileStream fs = new FileStream("trect",FileMode.Create,FileAccess.ReadWrite))
                {
                    using (StreamWriter sw = new StreamWriter(fs, Encoding.UTF8))
                    {
                        sw.Write(ps);
                    }
                }
                KeyChangerDel.Edit();
                UI(delegate () {
                    ShowKCEnable();
                });
            });
        }

        private void btnAutoLuck_Click(object sender, EventArgs e)
        {
            btnAutoLuck.Checked = !btnAutoLuck.Checked;
            IsAutoLuck = btnAutoLuck.Checked;
        }

        private void btnChangeStyle_Click(object sender, EventArgs e)
        {
            GEditForm ef = new GEditForm(this);
            ef.Show(this);
        }

        public GRender.GBtn NewMenuButton(int index)
        {
            /*Button tbt = new Button();
            tbt.AutoSize = true;
            tbt.FlatStyle = FlatStyle.Popup;
            tbt.Size = new Size(39, 22);
            pnMenu.Controls.Add(tbt);
            pnMenu.Controls.SetChildIndex(tbt, index);
            core.AddUIC(tbt);
            return tbt;*/
            GRender.GBtn b= rr.AddBtn(null, null, index);
            core.AddUIGB(b);
            return b;
        }

        public ContextMenuStrip NewMenu(GRender.GBtn btn)
        {
            /*ContextMenuStrip cm = new ContextMenuStrip(this.components);
            btn.ContextMenuStrip = cm;
            btn.Click += delegate (object sender, EventArgs e) {
                cm.Show(btn, 0, btn.Height);
            };
            core.AddUIC(cm);
            return cm;*/
            ContextMenuStrip cm = new ContextMenuStrip(this.components);
            btn.OnClicked = delegate (int x, int y, GRender.GBtn ctl) {
                cm.Show(x,y);
            };
            core.AddUIC(cm);
            return cm;
        }

        public ToolStripMenuItem NewMenuItem(ContextMenuStrip cm)
        {
            ToolStripMenuItem btn = new ToolStripMenuItem();
            cm.Items.Add(btn);
            core.AddUIT(btn);
            return btn;
        }
        public ToolStripMenuItem NewMenuItem()
        {
            ToolStripMenuItem btn = new ToolStripMenuItem();
            mnData.Items.Add(btn);
            core.AddUIT(btn);
            return btn;
        }
        public ToolStripMenuItem NewCloudMenuItem()
        {
            return NewMenuItem(cmCloud);
        }

        PluginMgrForm pmf = null;
        private void btnPluginManage_Click(object sender, EventArgs e)
        {
            if (pmf == null)
            {
                pmf = new PluginMgrForm();
                this.CenterChild(pmf);
                pmf.Show(this);
                pmf.FormClosed += delegate(object sender1, FormClosedEventArgs e1) {
                    pmf.Dispose();
                    pmf = null;
                };
            }
        }

        private void btnAbout_Click(object sender, EventArgs e)
        {
            AboutForm af = new AboutForm();
            this.CenterChild(af);
            af.ShowDialog(this);
            af.Dispose();
        }

        public void SetSCoreBtnVisible(bool isVisible)
        {
            btnEditBest.Visible = isVisible;
            btnShowPSInDots.Visible = isVisible;
        }

        private void btnEditBest_Click(object sender, EventArgs e)
        {
            if (core != null)
            {
                if (!File.Exists("best" + core.CoreName + ".txt"))
                {
                    try
                    {
                        core.SaveBest();
                    }
                    catch (Exception ex)
                    {
                        Error("无法生成最佳线文件");
                        return;
                    }
                }
                BestEditForm bef = new BestEditForm(core.CoreName);
                bef.ShowDialog(this);
            }
            else
            {
                Error("还没有准备好，请稍候再试");
            }
        }

        public bool IsShowPSInDots = true;
        private void btnShowPSInDots_Click(object sender, EventArgs e)
        {
            btnShowPSInDots.Checked = !btnShowPSInDots.Checked;
            IsShowPSInDots = btnShowPSInDots.Checked;
        }

        public bool IsBattlePauseNoTimer = false;
        private void btnBattlePauseNoTimer_Click(object sender, EventArgs e)
        {
            btnBattlePauseNoTimer.Checked = !btnBattlePauseNoTimer.Checked;
            IsBattlePauseNoTimer = btnBattlePauseNoTimer.Checked;
        }

        private void btnSoundConfig_Click(object sender, EventArgs e)
        {
            using (SoundConfigForm form = new SoundConfigForm())
            {
                form.ShowDialog(this);
            }
            btnSoundConfig.Checked = SoundConfig.ins.GlobalEnabled;
        }

        public bool IsNonSequentialCheck = false;
        private void btnNonSequentialCheck_Click(object sender, EventArgs e)
        {
            btnNonSequentialCheck.Checked = !btnNonSequentialCheck.Checked;
            IsNonSequentialCheck = btnNonSequentialCheck.Checked;
            SaveNonSequentialCheck();
        }

        private void LoadTransparency()
        {
            string transparencyFile = "transparency";
            try
            {
                if (File.Exists(transparencyFile))
                {
                    string content = File.ReadAllText(transparencyFile);
                    if (int.TryParse(content, out int value))
                    {
                        transparencyValue = Math.Max(0, Math.Min(100, value));
                    }
                }
            }
            catch { }
            UpdateTransparencyText();
        }

        private void SaveTransparency()
        {
            string transparencyFile = "transparency";
            try
            {
                File.WriteAllText(transparencyFile, transparencyValue.ToString());
            }
            catch { }
        }

        private void LoadNonSequentialCheck()
        {
            try
            {
                if (File.Exists("skip_node"))
                {
                    string content = File.ReadAllText("skip_node").Trim();
                    IsNonSequentialCheck = content == "1";
                }
            }
            catch { }
            btnNonSequentialCheck.Checked = IsNonSequentialCheck;
        }

        private void ApplyAutomationOptions()
        {
            if (AutomationArgs.Current.EnableNonSequentialSplits)
            {
                IsNonSequentialCheck = true;
                btnNonSequentialCheck.Checked = true;
            }
        }

        private void SaveNonSequentialCheck()
        {
            try
            {
                File.WriteAllText("skip_node", IsNonSequentialCheck ? "1" : "0");
            }
            catch { }
        }

        private void UpdateTransparency()
        {
            // 普通模式只调整背景图；OBS 模式再独立调整非文字框架层。
            // 两种模式都让文字、文字描边和计时数字保持不透明。
            this.Opacity = 1.0;
            rr?.SetBGOpacity(transparencyValue);
            int chromeOpacity = obsWindowStyleSettings != null && obsWindowStyleSettings.Enabled
                ? obsWindowStyleSettings.ChromeOpacity
                : 100;
            rr?.SetChromeOpacity(chromeOpacity);
            LayeredWindowPresenter.SetEnabled(this, obsWindowStyleSettings != null && obsWindowStyleSettings.Enabled);
            if (obsWindowStyleSettings != null && obsWindowStyleSettings.Enabled && Visible)
            {
                PresentObsWindowFrame();
            }
        }

        private void UpdateTransparencyText()
        {
            btnTransparency.Text = "背景透明度 (" + transparencyValue + "%)";
        }

        private void btnTransparency_Click(object sender, EventArgs e)
        {
            // 创建一个简单的输入对话框
            string input = Microsoft.VisualBasic.Interaction.InputBox(
                "请输入背景图透明度 (0-100):\n0 = 背景图完全透明\n100 = 背景图完全不透明\n文字、按钮和计时数字不会变透明",
                "设置背景图透明度",
                transparencyValue.ToString());
            
            if (!string.IsNullOrEmpty(input))
            {
                if (int.TryParse(input, out int value))
                {
                    transparencyValue = Math.Max(0, Math.Min(100, value));
                    UpdateTransparency();
                    UpdateTransparencyText();
                    SaveTransparency();
                }
                else
                {
                    MessageBox.Show("请输入有效的数字 (0-100)", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void btnObsWindowStyleEnabled_Click(object sender, EventArgs e)
        {
            ToggleObsWindowStyleEnabled();
        }

        private void ToggleObsWindowStyleEnabled()
        {
            ObsWindowStyleSettings previous = obsWindowStyleSettings.Clone();
            obsWindowStyleSettings.Enabled = !obsWindowStyleSettings.Enabled;
            obsWindowStyleSettings.Normalize();
            try
            {
                ObsWindowStyleStore.Save(obsWindowStyleSettings);
                obsPresentationFailureHandled = false;
                UpdateTransparency();
                UpdateObsWindowStyleMenu();
            }
            catch (Exception ex)
            {
                obsWindowStyleSettings = previous;
                try { ObsWindowStyleStore.Save(obsWindowStyleSettings); } catch { }
                try { UpdateTransparency(); } catch { }
                UpdateObsWindowStyleMenu();
                Error("无法应用 OBS 窗口采集样式：" + ex.Message);
            }
        }

        private void btnObsWindowStyleHotkey_Click(object sender, EventArgs e)
        {
            Func<Keys, string> validator = ValidateObsWindowStyleHotkey;
            using (Dx9OverlayHotkeyForm dialog = new Dx9OverlayHotkeyForm(
                ObsWindowStyleToggleHotkey,
                validator,
                "配置 OBS 窗口采集样式快捷键"))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                ObsWindowStyleSettings previous = obsWindowStyleSettings.Clone();
                obsWindowStyleSettings.ToggleHotkey = dialog.SelectedHotkey;
                try
                {
                    ObsWindowStyleStore.Save(obsWindowStyleSettings);
                    UpdateObsWindowStyleMenu();
                }
                catch (Exception ex)
                {
                    obsWindowStyleSettings = previous;
                    UpdateObsWindowStyleMenu();
                    Error("无法保存 OBS 窗口采集样式快捷键：" + ex.Message);
                }
            }
        }

        private string ValidateObsWindowStyleHotkey(Keys hotkey)
        {
            string error = Dx9OverlaySettings.ValidateToggleHotkey(hotkey, SoundConfig.ins.ToggleHotkey);
            if (!string.IsNullOrEmpty(error))
            {
                return error;
            }
            Keys coreHotkey = core == null ? Keys.None : core.GetCustomToggleHotkey();
            if (hotkey != Keys.None && hotkey == coreHotkey)
            {
                return "该组合已用于当前内核的独立遮罩开关。";
            }
            return "";
        }

        private void btnObsWindowChromeOpacity_Click(object sender, EventArgs e)
        {
            string input = Microsoft.VisualBasic.Interaction.InputBox(
                "请输入 OBS 窗口框架透明度 (0-100):\n0 = 只保留文字，背景和框架完全透明\n100 = 显示完整背景和框架\n文字、计时数字及文字描边始终保持不透明",
                "设置 OBS 窗口框架透明度",
                obsWindowStyleSettings.ChromeOpacity.ToString());
            if (string.IsNullOrEmpty(input))
            {
                return;
            }

            int value;
            if (!int.TryParse(input, out value))
            {
                MessageBox.Show("请输入有效的数字 (0-100)", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ObsWindowStyleSettings previous = obsWindowStyleSettings.Clone();
            obsWindowStyleSettings.ChromeOpacity = Math.Max(0, Math.Min(100, value));
            try
            {
                ObsWindowStyleStore.Save(obsWindowStyleSettings);
                UpdateTransparency();
                UpdateObsWindowStyleMenu();
            }
            catch (Exception ex)
            {
                obsWindowStyleSettings = previous;
                try { ObsWindowStyleStore.Save(obsWindowStyleSettings); } catch { }
                try { UpdateTransparency(); } catch { }
                UpdateObsWindowStyleMenu();
                Error("无法保存 OBS 窗口框架透明度：" + ex.Message);
            }
        }

        private void btnObsWindowHelp_Click(object sender, EventArgs e)
        {
            Info(
                "在 OBS 中添加“窗口采集”，选择标题为“自动计时器”的窗口。\n\n" +
                "启用本模式后，框架透明度为 0 时只保留文字；1-99 时保留半透明背景和框架；100 时显示完整界面。完整界面和简版使用同一设置。\n\n" +
                "建议先配置样式开关快捷键。即使按钮和框架已经完全透明，也能用该快捷键恢复普通界面。\n\n" +
                "如需采集右侧独立信息遮罩，请在 PAL98DX9 内核的菜单中启用“OBS 独立遮罩窗口”，然后在 OBS 中单独选择该窗口。",
                "OBS 窗口采集说明");
        }

        private void UpdateObsWindowStyleMenu()
        {
            if (obsWindowStyleSettings == null)
            {
                return;
            }
            if (btnObsWindowStyle != null)
            {
                btnObsWindowStyle.Checked = obsWindowStyleSettings.Enabled;
            }
            if (btnObsWindowStyleEnabled != null)
            {
                btnObsWindowStyleEnabled.Checked = obsWindowStyleSettings.Enabled;
                btnObsWindowStyleEnabled.Text = "启用 OBS 纯文字/透明框架模式";
            }
            if (btnObsWindowStyleHotkey != null)
            {
                btnObsWindowStyleHotkey.Text = "配置样式开关快捷键...（" +
                    Dx9OverlaySettings.FormatToggleHotkey(obsWindowStyleSettings.ToggleHotkey) + "）";
            }
            if (btnObsWindowChromeOpacity != null)
            {
                btnObsWindowChromeOpacity.Text = "框架透明度 (" + obsWindowStyleSettings.ChromeOpacity + "%)...";
            }
        }

        private void PresentObsWindowFrame()
        {
            Bitmap frame = rr == null ? null : rr.GetFrameBitmap();
            if (frame != null)
            {
                LayeredWindowPresenter.Present(this, frame);
            }
        }

        private void DisableObsWindowStyleAfterFailure(Exception ex)
        {
            if (obsPresentationFailureHandled)
            {
                return;
            }
            obsPresentationFailureHandled = true;
            if (obsWindowStyleSettings == null)
            {
                obsWindowStyleSettings = ObsWindowStyleSettings.CreateDefault();
            }
            obsWindowStyleSettings.Enabled = false;
            try { ObsWindowStyleStore.Save(obsWindowStyleSettings); } catch { }
            try
            {
                rr?.SetChromeOpacity(100);
                LayeredWindowPresenter.SetEnabled(this, false);
                Invalidate(true);
            }
            catch { }
            UpdateObsWindowStyleMenu();
            Alert("本机无法启用 OBS 逐像素透明窗口，已安全恢复普通界面：" + ex.Message);
        }
    }

    public class MConfig
    {
        private static MConfig _ins = null;
        private MConfig()
        {
            LoadConfig();
        }
        public static MConfig ins
        {
            get
            {
                if (_ins == null)
                {
                    _ins = new MConfig();
                }
                return _ins;
            }
        }
        public string Title = "";
        public Color MainColor = Color.Lime;
        public Color FasterColor = Color.Lime;
        public Color SlowerColor = Color.Red;
        public string ColorEgg = "";
        public string[] Lucks = new string[] { "" };
        public void LoadConfig(string cfgpath = "config.txt")
        {
            string cfgstr = "";
            if (File.Exists(cfgpath))
            {
                Encoding charset = TimerCore.GetFileEncodeType(cfgpath);
                using (FileStream fileStream = new FileStream(cfgpath, FileMode.Open))
                {
                    using (StreamReader streamReader = new StreamReader(fileStream, charset))
                    {
                        cfgstr = streamReader.ReadToEnd().Replace("\r", "");
                    }
                }
            }
            else
            {
                cfgstr = "自动计时器\r\n彩蛋\r\n大吉|小吉";
                using (FileStream fs = new FileStream(cfgpath, FileMode.Create))
                {
                    using (StreamWriter sw = new StreamWriter(fs, Encoding.UTF8))
                    {
                        sw.Write(cfgstr);
                    }
                }
            }

            if (cfgstr != "")
            {
                bool isOldCfg = false;
                string[] spli = cfgstr.Split('\n');
                if (spli.Length > 0)
                {
                    this.Title = spli[0];
                }
                if (spli.Length > 4)
                {
                    this.ColorEgg = spli[4];
                    isOldCfg = true;
                }
                if (spli.Length > 5)
                {
                    this.Lucks = spli[5].Split('|');
                    isOldCfg = true;
                }
                if (!isOldCfg)
                {
                    if (spli.Length > 1)
                    {
                        this.ColorEgg = spli[1];
                    }
                    if (spli.Length > 2)
                    {
                        this.Lucks = spli[2].Split('|');
                    }
                }
                else
                {
                    string updatecfgstr = this.Title + "\r\n" + this.ColorEgg + "\r\n";
                    for (int i = 0; i < this.Lucks.Length; ++i)
                    {
                        updatecfgstr += this.Lucks[i];
                        if (i < (this.Lucks.Length - 1))
                        {
                            updatecfgstr += "|";
                        }
                    }
                    File.Delete(cfgpath);
                    using (FileStream fs = new FileStream(cfgpath, FileMode.Create))
                    {
                        using (StreamWriter sw = new StreamWriter(fs, Encoding.UTF8))
                        {
                            sw.Write(updatecfgstr);
                        }
                    }
                }
            }
        }
        private int LuckIdx = -1;
        public string Luck(bool IsReset = false)
        {
            if (IsReset || LuckIdx < 0)
            {
                Random r = new Random(DateTime.Now.Millisecond);
                LuckIdx = r.Next(0, Lucks.Length);
            }
            return Lucks[LuckIdx];
        }
    }
}
