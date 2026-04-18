using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Pal98Timer
{
    public static class VoicePrompt
    {
        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern int mciSendString(string command, StringBuilder buffer, int bufferSize, IntPtr hwndCallback);

        private static readonly object _configLock = new object();
        private static readonly object _playLock = new object();
        private static readonly string _playerAlias = "paltimer_voice";
        private static readonly string[] _soundExts = new string[] { ".mp3", ".wav" };
        private static bool _isLoaded = false;
        private static bool _isEnable = true;
        private static string _fasterPath = @"sounds\faster.mp3";
        private static string _slowerPath = @"sounds\slower.mp3";
        private static Dictionary<string, string> _checkpointSounds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string AppDir
        {
            get
            {
                try
                {
                    string p = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                    if (!string.IsNullOrEmpty(p))
                    {
                        return p;
                    }
                }
                catch
                { }
                try
                {
                    string p = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    if (!string.IsNullOrEmpty(p))
                    {
                        return p;
                    }
                }
                catch
                { }
                return Environment.CurrentDirectory;
            }
        }

        public static void ReloadConfig()
        {
            lock (_configLock)
            {
                _isLoaded = false;
            }
            EnsureConfigLoaded();
        }

        private static void EnsureConfigLoaded()
        {
            if (_isLoaded)
            {
                return;
            }
            lock (_configLock)
            {
                if (_isLoaded)
                {
                    return;
                }
                _isEnable = true;
                _fasterPath = @"sounds\faster.mp3";
                _slowerPath = @"sounds\slower.mp3";
                _checkpointSounds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                string cfgPath = Path.Combine(AppDir, "voice_config.txt");
                if (File.Exists(cfgPath))
                {
                    try
                    {
                        Encoding charset = TimerCore.GetFileEncodeType(cfgPath);
                        if (charset == null)
                        {
                            charset = Encoding.UTF8;
                        }
                        using (FileStream fs = new FileStream(cfgPath, FileMode.Open, FileAccess.Read))
                        {
                            using (StreamReader sr = new StreamReader(fs, charset))
                            {
                                while (!sr.EndOfStream)
                                {
                                    string line = sr.ReadLine();
                                    if (line == null)
                                    {
                                        continue;
                                    }
                                    line = line.Trim();
                                    if (line == "" || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("//"))
                                    {
                                        continue;
                                    }
                                    int idx = line.IndexOf('=');
                                    if (idx <= 0)
                                    {
                                        continue;
                                    }
                                    string key = line.Substring(0, idx).Trim();
                                    string val = line.Substring(idx + 1).Trim();
                                    if (key == "" || val == "")
                                    {
                                        continue;
                                    }
                                    string lowKey = key.ToLowerInvariant();
                                    if (lowKey == "enable" || lowKey == "enabled" || lowKey == "voice_enable")
                                    {
                                        _isEnable = ParseBool(val, true);
                                    }
                                    else if (lowKey == "faster" || lowKey == "faster_sound")
                                    {
                                        _fasterPath = val;
                                    }
                                    else if (lowKey == "slower" || lowKey == "slower_sound")
                                    {
                                        _slowerPath = val;
                                    }
                                    else if (lowKey.StartsWith("checkpoint."))
                                    {
                                        string checkpointName = key.Substring("checkpoint.".Length).Trim();
                                        if (checkpointName != "")
                                        {
                                            _checkpointSounds[checkpointName] = val;
                                        }
                                    }
                                    else if (lowKey.StartsWith("cp."))
                                    {
                                        string checkpointName = key.Substring("cp.".Length).Trim();
                                        if (checkpointName != "")
                                        {
                                            _checkpointSounds[checkpointName] = val;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("VoicePrompt config load failed: " + ex.Message);
                    }
                }
                _isLoaded = true;
            }
        }

        private static bool ParseBool(string str, bool defaultValue)
        {
            if (string.IsNullOrEmpty(str))
            {
                return defaultValue;
            }
            string s = str.Trim().ToLowerInvariant();
            if (s == "1" || s == "true" || s == "yes" || s == "on")
            {
                return true;
            }
            if (s == "0" || s == "false" || s == "no" || s == "off")
            {
                return false;
            }
            return defaultValue;
        }

        public static void PlaySound(string filePath)
        {
            EnsureConfigLoaded();
            if (!_isEnable)
            {
                return;
            }
            string fullPath = ResolvePath(filePath);
            if (fullPath == "")
            {
                return;
            }
            if (!File.Exists(fullPath))
            {
                return;
            }
            string normalizedPath;
            try
            {
                normalizedPath = Path.GetFullPath(fullPath);
            }
            catch
            {
                return;
            }
            if (!IsAllowedAudioFile(normalizedPath))
            {
                return;
            }
            if (normalizedPath.IndexOf('\r') >= 0 || normalizedPath.IndexOf('\n') >= 0 || normalizedPath.IndexOf('"') >= 0)
            {
                return;
            }

            lock (_playLock)
            {
                try
                {
                    mciSendString("close " + _playerAlias, null, 0, IntPtr.Zero);
                    int openRes = mciSendString("open \"" + normalizedPath + "\" alias " + _playerAlias, null, 0, IntPtr.Zero);
                    if (openRes == 0)
                    {
                        mciSendString("play " + _playerAlias + " from 0", null, 0, IntPtr.Zero);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("VoicePrompt.PlaySound error: " + ex.Message);
                }
            }
        }

        public static void PlayCheckpointSound(string checkpointName)
        {
            EnsureConfigLoaded();
            if (string.IsNullOrWhiteSpace(checkpointName))
            {
                return;
            }
            string mapping;
            if (_checkpointSounds.TryGetValue(checkpointName.Trim(), out mapping))
            {
                PlaySound(mapping);
                return;
            }

            string autoPath = FindNamedSound(checkpointName.Trim());
            if (autoPath != "")
            {
                PlaySound(autoPath);
            }
        }

        public static void PlayFasterSound()
        {
            EnsureConfigLoaded();
            PlaySound(_fasterPath);
        }

        public static void PlaySlowerSound()
        {
            EnsureConfigLoaded();
            PlaySound(_slowerPath);
        }

        private static string ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }
            string p = path.Trim().Trim('"');
            if (Path.IsPathRooted(p))
            {
                return p;
            }
            return Path.Combine(AppDir, p);
        }

        private static string FindNamedSound(string checkpointName)
        {
            string[] bases = new string[]
            {
                Path.Combine(AppDir, "sounds"),
                Path.Combine(AppDir, "voice")
            };

            foreach (string baseDir in bases)
            {
                string matched = FindNamedSoundInDir(baseDir, checkpointName);
                if (matched != "")
                {
                    return matched;
                }
            }
            return "";
        }

        private static string FindNamedSoundInDir(string baseDir, string checkpointName)
        {
            if (!Directory.Exists(baseDir))
            {
                return "";
            }
            string safeName = MakeSafeFileName(checkpointName);
            foreach (string ext in _soundExts)
            {
                string p1 = Path.Combine(baseDir, checkpointName + ext);
                if (File.Exists(p1))
                {
                    return p1;
                }
                if (safeName != checkpointName)
                {
                    string p2 = Path.Combine(baseDir, safeName + ext);
                    if (File.Exists(p2))
                    {
                        return p2;
                    }
                }
            }
            return "";
        }

        private static string MakeSafeFileName(string fileName)
        {
            string res = fileName;
            char[] invs = Path.GetInvalidFileNameChars();
            foreach (char c in invs)
            {
                res = res.Replace(c, '_');
            }
            return res;
        }

        private static bool IsAllowedAudioFile(string path)
        {
            string ext = Path.GetExtension(path);
            foreach (string item in _soundExts)
            {
                if (string.Equals(ext, item, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
