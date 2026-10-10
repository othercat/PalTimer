using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Pal98Timer
{
    internal sealed class TimerSettingsException : IOException
    {
        internal TimerSettingsException(string message, Exception inner) : base(message, inner) { }
    }

    // Shared with KeyChanger. Resources, scores and authentication keep their
    // existing owners. Only personal settings are redirected here.
    internal sealed class TimerSettingsStore
    {
        internal static readonly string[] LegacyFiles = {
            "config.txt", "LastCore", "size", "transparency", "skip_node",
            "GDisplay.cnf", "sound_config.txt", "obs_window_style",
            "dx9_overlay", "dx9_overlay_layout", "bg.png", "keychange.txt",
            "keyboard/keys.meta", "keyboard/normal.png", "keyboard/act.png", "keyboard/arr.png"
        };
        private readonly string installation, legacy;
        private readonly Pal98.Storage.UserDataStore shared;
        private readonly Action<string> report;
        private bool initialized, userMode;
        internal string DirectoryPath;
        internal string LogDirectory => shared.PathFor("logs", "timer");
        internal TimerSettingsStore(string installation, string localAppData) : this(installation, localAppData, null) { }
        internal TimerSettingsStore(string installation, string localAppData, Action<string> reportError)
        {
            this.installation = Path.GetFullPath(installation);
            report = reportError;
            shared = new Pal98.Storage.UserDataStore(Pal98.Storage.UserDataStore.FindInstallation(installation), localAppData, reportError);
            DirectoryPath = Path.Combine(shared.DirectoryPath, "timer");
            using (var sha = SHA256.Create()) {
                string identity = Path.GetFullPath(installation).TrimEnd('\\', '/').ToUpperInvariant();
                string id = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "").ToLowerInvariant().Substring(0, 24);
                legacy = Path.Combine(localAppData, "PalTimer", "Settings", id);
            }
        }
        internal static string GetDirectory(string installation, string localAppData)
        {
            return Path.Combine(new Pal98.Storage.UserDataStore(Pal98.Storage.UserDataStore.FindInstallation(installation), localAppData).DirectoryPath, "timer");
        }
        private T Access<T>(Func<T> action)
        {
            try { return action(); }
            catch (Pal98.Storage.UserDataException error) { throw new TimerSettingsException(error.Message, error); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException) {
                string message = "无法读取或保存计时器配置：\n" + DirectoryPath + "\n" + error.Message;
                report?.Invoke(message); throw new TimerSettingsException(message, error);
            }
        }
        private string Source(string name)
        {
            if (Path.IsPathRooted(name) || name.IndexOf(':') >= 0 || name.Replace('\\', '/').Split('/').Length == 0)
                throw new ArgumentException("Unsafe configuration path.");
            foreach (string part in name.Replace('\\', '/').Split('/'))
                if (part.Length == 0 || part == "." || part == ".." || part.TrimEnd(' ', '.') != part ||
                    System.Text.RegularExpressions.Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) throw new ArgumentException("Unsafe configuration path.");
            return Path.Combine(installation, name.StartsWith("timelines/", StringComparison.Ordinal) ? name.Substring(10) : name);
        }
        internal void Initialize()
        {
            if (initialized) return;
            Access(() => {
                userMode = shared.PreferUserDirectory("timer", installation, LegacyFiles);
                if (userMode) InitializeUser();
                else DirectoryPath = installation;
                initialized = true; return true;
            });
        }
        private void InitializeUser()
        {
            userMode = true;
            shared.Initialize();
            DirectoryPath = shared.ComponentDirectory("timer");
            foreach (string name in LegacyFiles) {
                shared.ImportLegacyOnce("timer", name, Path.Combine(legacy, name), Source(name));
                shared.Resolve("timer", name, Source(name));
            }
            TimerUserSettings.ClearPathCache();
        }
        internal string GetPath(string name)
        {
            Initialize();
            return Access(() => {
                if (!userMode) {
                    string source = Source(name), old = Path.Combine(legacy, name);
                    return !Pal98.Storage.UserDataStore.FileExists(source) && Array.IndexOf(LegacyFiles, name) >= 0 &&
                        Pal98.Storage.UserDataStore.FileExists(old) ? old : source;
                }
                string path = shared.Resolve("timer", name, Source(name));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                return path;
            });
        }
        internal void WriteBytes(string name, byte[] bytes)
        {
            Initialize(); string source = Source(name);
            Access(() => {
                if (!userMode) {
                    try { WriteLocal(source, bytes); return true; }
                    catch (Exception error) when (Pal98.Storage.UserDataStore.IsPermissionFailure(error)) { InitializeUser(); }
                }
                shared.Write("timer", name, bytes, source); return true;
            });
        }
        private static void WriteLocal(string target, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string temporary = target + ".new-" + Guid.NewGuid().ToString("N");
            try {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
                if (File.Exists(target)) File.Replace(temporary, target, target + ".previous");
                else File.Move(temporary, target);
            } finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (UnauthorizedAccessException) { } }
        }
        internal void WriteText(string name, string text, Encoding encoding)
        {
            byte[] prefix = encoding.GetPreamble(), body = encoding.GetBytes(text);
            byte[] bytes = new byte[prefix.Length + body.Length];
            Buffer.BlockCopy(prefix, 0, bytes, 0, prefix.Length); Buffer.BlockCopy(body, 0, bytes, prefix.Length, body.Length);
            WriteBytes(name, bytes);
        }
        internal void Remove(string name)
        {
            Initialize(); string source = Source(name);
            Access(() => {
                if (!userMode) {
                    try { File.Delete(source); return true; }
                    catch (Exception error) when (Pal98.Storage.UserDataStore.IsPermissionFailure(error)) { InitializeUser(); }
                }
                shared.Remove("timer", name, source); return true;
            });
        }
    }

    internal static class TimerUserSettings
    {
        private static TimerSettingsStore store;
        private static readonly Dictionary<string, string> timelinePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal static void ClearPathCache() { lock (timelinePaths) timelinePaths.Clear(); }
        internal static TimerSettingsStore Store { get { return store ?? (store = new TimerSettingsStore(AppDomain.CurrentDomain.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), message => ShowError(message, "计时器配置访问失败"))); } }
        internal static string GetPath(string name) { return Store.GetPath(name); }
        internal static string BestPath(string name) { return Store.GetPath("timelines/" + Path.GetFileName(name)); }
        internal static string TimelinePath(string source)
        {
            string prefix = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\', '/') + "\\";
            string full = Path.GetFullPath(source);
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return source;
            string relative = full.Substring(prefix.Length);
            lock (timelinePaths) {
                string path;
                if (!timelinePaths.TryGetValue(relative, out path)) timelinePaths[relative] = path = Store.GetPath(relative);
                return path;
            }
        }
        internal static void WriteText(string name, string text, Encoding encoding) { Store.WriteText(name, text, encoding); }
        internal static void ShowError(string message, string title)
        {
            // A modal dialog on a half-constructed main window's thread pumps
            // its timers and can start KeyChanger before startup has succeeded.
            var thread = new Thread(() => MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error));
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        }
    }
}
