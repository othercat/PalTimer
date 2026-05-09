using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

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
        TotalSlowerSegmentFaster
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
                default: return type.ToString();
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

        private string _configPath = "sound_config.txt";
        private int _mciAliasCounter = 0;

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
                                SoundTriggerType type = (SoundTriggerType)Enum.Parse(typeof(SoundTriggerType), key);
                                ParseSoundEntry(type, value);
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
        /// 异步播放音效（非阻塞，支持 wav 和 mp3）
        /// </summary>
        public void PlaySound(SoundTriggerType type)
        {
            if (!GlobalEnabled) return;
            if (!IsSoundEnabled(type)) return;

            string path = GetSoundPath(type);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            Thread playThread = new Thread(() =>
            {
                try
                {
                    string alias = "palSound" + Interlocked.Increment(ref _mciAliasCounter);
                    string ext = Path.GetExtension(path).ToLower();

                    if (ext == ".mp3")
                    {
                        mciSendString("open \"" + path + "\" type mpegvideo alias " + alias, null, 0, IntPtr.Zero);
                        mciSendString("play " + alias, null, 0, IntPtr.Zero);
                        // 等待播放完成后关闭
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
                    else
                    {
                        // wav 格式使用 mciSendString 也兼容
                        mciSendString("open \"" + path + "\" type waveaudio alias " + alias, null, 0, IntPtr.Zero);
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
                }
                catch
                {
                    // 静默忽略播放失败
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
                try
                {
                    string alias = "palTest" + Interlocked.Increment(ref _mciAliasCounter);
                    string ext = Path.GetExtension(filePath).ToLower();

                    if (ext == ".mp3")
                    {
                        mciSendString("open \"" + filePath + "\" type mpegvideo alias " + alias, null, 0, IntPtr.Zero);
                    }
                    else
                    {
                        mciSendString("open \"" + filePath + "\" type waveaudio alias " + alias, null, 0, IntPtr.Zero);
                    }
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
                catch { }
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
    }
}
