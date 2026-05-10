using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Pal98Timer
{
    /// <summary>
    /// 音效触发类型
    /// </summary>
    public enum SoundTriggerType
    {
        /// <summary>
        /// 分段快了
        /// </summary>
        SegmentFaster,
        /// <summary>
        /// 分段慢了
        /// </summary>
        SegmentSlower,
        /// <summary>
        /// 总时间快了但分段慢了
        /// </summary>
        TotalFasterSegmentSlower,
        /// <summary>
        /// 总时间慢了但分段快了
        /// </summary>
        TotalSlowerSegmentFaster,
        /// <summary>
        /// 最终通关
        /// </summary>
        GameComplete
    }

    /// <summary>
    /// 获取触发类型的中文描述
    /// </summary>
    public static class SoundTriggerTypeExtensions
    {
        public static string ToChineseString(this SoundTriggerType type)
        {
            switch (type)
            {
                case SoundTriggerType.SegmentFaster: return "分段快了";
                case SoundTriggerType.SegmentSlower: return "分段慢了";
                case SoundTriggerType.TotalFasterSegmentSlower: return "总时间快但分段慢";
                case SoundTriggerType.TotalSlowerSegmentFaster: return "总时间慢但分段快";
                case SoundTriggerType.GameComplete: return "最终通关";
                default: return type.ToString();
            }
        }

        /// <summary>
        /// 获取音效优先级（数值越大优先级越高）
        /// </summary>
        public static int GetPriority(this SoundTriggerType type)
        {
            switch (type)
            {
                case SoundTriggerType.GameComplete: return 30;
                case SoundTriggerType.TotalFasterSegmentSlower:
                case SoundTriggerType.TotalSlowerSegmentFaster: return 20;
                case SoundTriggerType.SegmentFaster:
                case SoundTriggerType.SegmentSlower: return 10;
                default: return 0;
            }
        }
    }

    /// <summary>
    /// 音效配置管理（单例）
    /// </summary>
    public class SoundConfig
    {
        private static SoundConfig _ins;
        public static SoundConfig ins
        {
            get
            {
                if (_ins == null)
                {
                    _ins = new SoundConfig();
                }
                return _ins;
            }
        }

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int mciSendString(string command, StringBuilder retstring, int returnlength, IntPtr callback);

        private Dictionary<SoundTriggerType, string> _soundPaths = new Dictionary<SoundTriggerType, string>();
        private Dictionary<SoundTriggerType, bool> _soundEnabled = new Dictionary<SoundTriggerType, bool>();

        public bool GlobalEnabled { get; set; } = true;

        // 音效开关快捷键
        public Keys ToggleHotkey { get; set; } = Keys.None;
        // 音效打开提示音路径
        public string SoundEnabledOnPath { get; set; } = "";
        // 音效关闭提示音路径
        public string SoundEnabledOffPath { get; set; } = "";
        // 是否播放打开提示音
        public bool SoundEnabledOnEnabled { get; set; } = false;
        // 是否播放关闭提示音
        public bool SoundEnabledOffEnabled { get; set; } = false;

        private string _configPath = "sound_config.txt";
        private int _mciAliasCounter = 0;

        // 优先级跟踪
        private readonly object _playLock = new object();
        private int _currentPlayingPriority = 0;
        private string _currentPlayingAlias = null;

        /// <summary>
        /// 尝试用多种方式打开音频文件，返回成功的 alias，失败返回 null
        /// </summary>
        private string MciOpenFile(string filePath)
        {
            string alias = "palMci" + Interlocked.Increment(ref _mciAliasCounter);
            string ext = Path.GetExtension(filePath).ToLower();
            string quoted = "\"" + filePath + "\"";

            int err;

            if (ext == ".mp3")
            {
                // 尝试1: 不指定 type，让 MCI 根据扩展名自动检测
                err = mciSendString("open " + quoted + " alias " + alias, null, 0, IntPtr.Zero);
                if (err == 0) return alias;

                // 尝试2: 指定 MPEGVideo
                err = mciSendString("open " + quoted + " type MPEGVideo alias " + alias, null, 0, IntPtr.Zero);
                if (err == 0) return alias;

                // 尝试3: 指定 mpegvideo
                err = mciSendString("open " + quoted + " type mpegvideo alias " + alias, null, 0, IntPtr.Zero);
                if (err == 0) return alias;
            }
            else
            {
                // WAV: 先尝试自动检测，再尝试 waveaudio
                err = mciSendString("open " + quoted + " alias " + alias, null, 0, IntPtr.Zero);
                if (err == 0) return alias;

                err = mciSendString("open " + quoted + " type waveaudio alias " + alias, null, 0, IntPtr.Zero);
                if (err == 0) return alias;
            }

            System.Diagnostics.Debug.WriteLine("MCI: all open attempts failed for " + filePath);
            return null;
        }

        /// <summary>
        /// 使用 Windows Media Player COM 播放 MP3（MCI 不可用时的备用方案）
        /// </summary>
        private bool PlayMp3WithWmp(string filePath)
        {
            try
            {
                Type wmpType = Type.GetTypeFromProgID("WMPlayer.OCX");
                if (wmpType == null)
                {
                    System.Diagnostics.Debug.WriteLine("WMP: WMPlayer.OCX not registered");
                    return false;
                }

                object wmp = Activator.CreateInstance(wmpType);
                try
                {
                    // 设置 URL 属性
                    wmpType.InvokeMember("url",
                        System.Reflection.BindingFlags.SetProperty, null, wmp,
                        new object[] { filePath });

                    // 获取 controls 对象并调用 play
                    object controls = wmpType.InvokeMember("controls",
                        System.Reflection.BindingFlags.GetProperty, null, wmp, null);

                    controls.GetType().InvokeMember("play",
                        System.Reflection.BindingFlags.InvokeMethod, null, controls, null);

                    // 等待播放完成
                    Thread.Sleep(300);
                    object playState;
                    int maxWait = 300; // 最多等 30 秒
                    while (maxWait-- > 0)
                    {
                        playState = wmpType.InvokeMember("playState",
                            System.Reflection.BindingFlags.GetProperty, null, wmp, null);
                        int state = (int)playState;
                        // 1=Stopped, 8=MediaEnded
                        if (state == 1 || state == 8) break;
                        Thread.Sleep(100);
                    }

                    Marshal.ReleaseComObject(controls);
                    return true;
                }
                finally
                {
                    Marshal.ReleaseComObject(wmp);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("WMP playback failed: " + ex.Message);
                return false;
            }
        }

        private SoundConfig()
        {
            foreach (SoundTriggerType type in Enum.GetValues(typeof(SoundTriggerType)))
            {
                _soundPaths[type] = "";
                _soundEnabled[type] = false;
            }
            LoadConfig();
        }

        public void LoadConfig()
        {
            if (!File.Exists(_configPath))
            {
                SaveConfig();
                return;
            }

            try
            {
                Encoding charset = TimerCore.GetFileEncodeType(_configPath);
                using (FileStream fs = new FileStream(_configPath, FileMode.Open))
                using (StreamReader sr = new StreamReader(fs, charset))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                            continue;

                        int eqIdx = line.IndexOf('=');
                        if (eqIdx < 0) continue;

                        string key = line.Substring(0, eqIdx).Trim();
                        string value = line.Substring(eqIdx + 1).Trim();

                        switch (key)
                        {
                            case "GlobalEnabled":
                                GlobalEnabled = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                                break;
                            case "SegmentFaster":
                            case "SegmentSlower":
                            case "TotalFasterSegmentSlower":
                            case "TotalSlowerSegmentFaster":
                            case "GameComplete":
                                SoundTriggerType type = (SoundTriggerType)Enum.Parse(typeof(SoundTriggerType), key);
                                ParseSoundEntry(type, value);
                                break;
                            case "ToggleHotkey":
                                try { ToggleHotkey = (Keys)int.Parse(value); } catch { }
                                break;
                            case "SoundEnabledOn":
                                ParseToggleSoundEntry(value, true);
                                break;
                            case "SoundEnabledOff":
                                ParseToggleSoundEntry(value, false);
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Load sound config failed: " + ex.Message);
            }
        }

        private void ParseSoundEntry(SoundTriggerType type, string value)
        {
            if (value.Contains("|"))
            {
                string[] parts = value.Split(new char[] { '|' }, 2);
                _soundEnabled[type] = parts[0].Equals("true", StringComparison.OrdinalIgnoreCase);
                _soundPaths[type] = parts.Length > 1 ? parts[1] : "";
            }
            else
            {
                _soundEnabled[type] = !string.IsNullOrEmpty(value);
                _soundPaths[type] = value;
            }
        }

        private void ParseToggleSoundEntry(string value, bool isOn)
        {
            if (value.Contains("|"))
            {
                string[] parts = value.Split(new char[] { '|' }, 2);
                if (isOn)
                {
                    SoundEnabledOnEnabled = parts[0].Equals("true", StringComparison.OrdinalIgnoreCase);
                    SoundEnabledOnPath = parts.Length > 1 ? parts[1] : "";
                }
                else
                {
                    SoundEnabledOffEnabled = parts[0].Equals("true", StringComparison.OrdinalIgnoreCase);
                    SoundEnabledOffPath = parts.Length > 1 ? parts[1] : "";
                }
            }
            else
            {
                if (isOn)
                {
                    SoundEnabledOnEnabled = !string.IsNullOrEmpty(value);
                    SoundEnabledOnPath = value;
                }
                else
                {
                    SoundEnabledOffEnabled = !string.IsNullOrEmpty(value);
                    SoundEnabledOffPath = value;
                }
            }
        }

        public void SaveConfig()
        {
            try
            {
                using (FileStream fs = new FileStream(_configPath, FileMode.Create))
                using (StreamWriter sw = new StreamWriter(fs, Encoding.UTF8))
                {
                    sw.WriteLine("# PalTimer 音效配置");
                    sw.WriteLine("# 格式: 触发类型=启用|文件路径");
                    sw.WriteLine("# 支持格式: wav, mp3");
                    sw.WriteLine("# 启用: true/false");
                    sw.WriteLine();
                    sw.WriteLine("GlobalEnabled=" + (GlobalEnabled ? "true" : "false"));
                    sw.WriteLine("ToggleHotkey=" + ((int)ToggleHotkey));
                    sw.WriteLine("SoundEnabledOn=" + (SoundEnabledOnEnabled ? "true" : "false") + "|" + (SoundEnabledOnPath ?? ""));
                    sw.WriteLine("SoundEnabledOff=" + (SoundEnabledOffEnabled ? "true" : "false") + "|" + (SoundEnabledOffPath ?? ""));
                    sw.WriteLine();
                    foreach (SoundTriggerType type in Enum.GetValues(typeof(SoundTriggerType)))
                    {
                        string enabled = _soundEnabled[type] ? "true" : "false";
                        string path = _soundPaths[type] ?? "";
                        sw.WriteLine(type.ToString() + "=" + enabled + "|" + path);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Save sound config failed: " + ex.Message);
            }
        }

        public void SetSoundPath(SoundTriggerType type, string path)
        {
            _soundPaths[type] = path;
            if (!string.IsNullOrEmpty(path))
                _soundEnabled[type] = true;
            SaveConfig();
        }

        public void SetSoundEnabled(SoundTriggerType type, bool enabled)
        {
            _soundEnabled[type] = enabled;
            SaveConfig();
        }

        public string GetSoundPath(SoundTriggerType type)
        {
            return _soundPaths.ContainsKey(type) ? _soundPaths[type] : "";
        }

        public bool IsSoundEnabled(SoundTriggerType type)
        {
            return _soundEnabled.ContainsKey(type) && _soundEnabled[type];
        }

        /// <summary>
        /// 停止当前正在播放的低优先级音效
        /// </summary>
        private void StopCurrentSound()
        {
            string aliasToStop = null;
            lock (_playLock)
            {
                aliasToStop = _currentPlayingAlias;
                _currentPlayingAlias = null;
                _currentPlayingPriority = 0;
            }
            if (aliasToStop != null)
            {
                try { mciSendString("stop " + aliasToStop, null, 0, IntPtr.Zero); } catch { }
                try { mciSendString("close " + aliasToStop, null, 0, IntPtr.Zero); } catch { }
            }
        }

        /// <summary>
        /// 异步播放音效（非阻塞，支持优先级中断，支持 wav 和 mp3）
        /// </summary>
        public void PlaySound(SoundTriggerType type)
        {
            PlaySound(type, type.GetPriority());
        }

        private void PlaySound(SoundTriggerType type, int priority)
        {
            if (!GlobalEnabled) return;
            if (!IsSoundEnabled(type)) return;

            string path = GetSoundPath(type);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            // 优先级检查：中断低优先级音效
            lock (_playLock)
            {
                if (priority < _currentPlayingPriority) return;
            }
            StopCurrentSound();

            Thread playThread = new Thread(() =>
            {
                string alias = null;
                try
                {
                    alias = MciOpenFile(path);
                    if (alias != null)
                    {
                        lock (_playLock)
                        {
                            _currentPlayingAlias = alias;
                            _currentPlayingPriority = priority;
                        }

                        int err = mciSendString("play " + alias, null, 0, IntPtr.Zero);
                        if (err != 0)
                        {
                            System.Diagnostics.Debug.WriteLine("MCI play failed: " + err);
                            string failedAlias = alias;
                            mciSendString("close " + alias, null, 0, IntPtr.Zero);
                            alias = null;
                            lock (_playLock)
                            {
                                if (_currentPlayingAlias == failedAlias)
                                {
                                    _currentPlayingAlias = null;
                                    _currentPlayingPriority = 0;
                                }
                            }
                        }
                    }

                    if (alias != null)
                    {
                        Thread.Sleep(500);
                        StringBuilder status = new StringBuilder(128);
                        while (true)
                        {
                            // 检查是否被更高优先级中断
                            lock (_playLock)
                            {
                                if (_currentPlayingAlias != alias) break;
                            }
                            mciSendString("status " + alias + " mode", status, 128, IntPtr.Zero);
                            if (status.ToString().Trim() != "playing") break;
                            Thread.Sleep(100);
                        }
                        mciSendString("close " + alias, null, 0, IntPtr.Zero);
                        lock (_playLock)
                        {
                            if (_currentPlayingAlias == alias)
                            {
                                _currentPlayingAlias = null;
                                _currentPlayingPriority = 0;
                            }
                        }
                    }
                    else if (Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
                    {
                        // MCI 不可用时，尝试 WMP COM 播放 MP3
                        PlayMp3WithWmp(path);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("PlaySound exception: " + ex.Message);
                    if (alias != null) try { mciSendString("close " + alias, null, 0, IntPtr.Zero); } catch { }
                    lock (_playLock)
                    {
                        if (_currentPlayingAlias == alias)
                        {
                            _currentPlayingAlias = null;
                            _currentPlayingPriority = 0;
                        }
                    }
                }
            });
            playThread.IsBackground = true;
            playThread.Start();
        }

        /// <summary>
        /// 测试播放指定文件（用于配置窗口预览）
        /// </summary>
        public void TestPlay(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

            Thread playThread = new Thread(() =>
            {
                string alias = null;
                try
                {
                    alias = MciOpenFile(filePath);
                    if (alias != null)
                    {
                        int err = mciSendString("play " + alias, null, 0, IntPtr.Zero);
                        if (err != 0)
                        {
                            System.Diagnostics.Debug.WriteLine("MCI play failed: " + err);
                            mciSendString("close " + alias, null, 0, IntPtr.Zero);
                            alias = null;
                        }
                    }

                    if (alias != null)
                    {
                        Thread.Sleep(500);
                        StringBuilder status = new StringBuilder(128);
                        while (true)
                        {
                            mciSendString("status " + alias + " mode", status, 128, IntPtr.Zero);
                            if (status.ToString().Trim() != "playing") break;
                            Thread.Sleep(100);
                        }
                        mciSendString("close " + alias, null, 0, IntPtr.Zero);
                    }
                    else if (Path.GetExtension(filePath).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
                    {
                        // MCI 不可用时，尝试 WMP COM 播放 MP3
                        if (!PlayMp3WithWmp(filePath))
                        {
                            MessageBox.Show("无法播放音频，请确认系统已安装 Windows Media Player 或相关解码器。\n路径: " + filePath,
                                "播放失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    else
                    {
                        MessageBox.Show("无法打开音频文件，请确认文件格式正确。\n路径: " + filePath,
                            "播放失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("TestPlay exception: " + ex.Message);
                    if (alias != null) try { mciSendString("close " + alias, null, 0, IntPtr.Zero); } catch { }
                }
            });
            playThread.IsBackground = true;
            playThread.Start();
        }

        /// <summary>
        /// 根据分段和总时间快慢判断触发类型并播放
        /// </summary>
        public void PlayBySpeed(bool segmentFaster, bool totalFaster)
        {
            SoundTriggerType? type = GetTriggerType(segmentFaster, totalFaster);
            if (type.HasValue)
            {
                PlaySound(type.Value);
            }
        }

        private SoundTriggerType? GetTriggerType(bool segmentFaster, bool totalFaster)
        {
            if (segmentFaster && totalFaster)
                return SoundTriggerType.SegmentFaster;
            else if (!segmentFaster && !totalFaster)
                return SoundTriggerType.SegmentSlower;
            else if (!segmentFaster && totalFaster)
                return SoundTriggerType.TotalFasterSegmentSlower;
            else
                return SoundTriggerType.TotalSlowerSegmentFaster;
        }

        /// <summary>
        /// 播放音效开关提示音（不受 GlobalEnabled 阻挡）
        /// </summary>
        public void PlayToggleSound(bool enabled)
        {
            try
            {
                string path;
                bool shouldPlay;
                if (enabled)
                {
                    path = SoundEnabledOnPath;
                    shouldPlay = SoundEnabledOnEnabled;
                }
                else
                {
                    path = SoundEnabledOffPath;
                    shouldPlay = SoundEnabledOffEnabled;
                }

                if (!shouldPlay || string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                Thread playThread = new Thread(() =>
                {
                    string alias = null;
                    try
                    {
                        alias = MciOpenFile(path);
                        if (alias != null)
                        {
                            mciSendString("play " + alias, null, 0, IntPtr.Zero);
                            Thread.Sleep(500);
                            StringBuilder status = new StringBuilder(128);
                            while (true)
                            {
                                mciSendString("status " + alias + " mode", status, 128, IntPtr.Zero);
                                if (status.ToString().Trim() != "playing") break;
                                Thread.Sleep(100);
                            }
                            mciSendString("close " + alias, null, 0, IntPtr.Zero);
                        }
                        else if (Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
                        {
                            PlayMp3WithWmp(path);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("PlayToggleSound exception: " + ex.Message);
                        if (alias != null) try { mciSendString("close " + alias, null, 0, IntPtr.Zero); } catch { }
                    }
                });
                playThread.IsBackground = true;
                playThread.Start();
            }
            catch
            {
                // 静默忽略
            }
        }

        /// <summary>
        /// 获取快捷键的显示文本
        /// </summary>
        public string GetToggleHotkeyText()
        {
            if (ToggleHotkey == Keys.None) return "未设置";
            return ToggleHotkey.ToString();
        }
    }
}
