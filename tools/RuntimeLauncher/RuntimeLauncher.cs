// SPDX-License-Identifier: GPL-2.0-only
// Refs othercat/sdlpal-lostedit#223. Select once before starting the frozen timer.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PalTimer.RuntimeLauncher
{
    internal enum RuntimeKind { Unknown, Original, SDL }
    internal sealed class RuntimeCandidate
    {
        internal int Pid;
        internal string Path;
        internal RuntimeKind Kind;
    }
    internal sealed class Selection
    {
        public string core, reason, game;
        public int pid;
    }
    internal static class RuntimeSelection
    {
        internal const string OriginalCore = "Pal98Dx9Automatic", SdlCore = "SdlpalGeshi";
        // Unmodified PAL98 VB loader, also used by the current original DX9 package.
        internal const string OriginalImage = "75d612b9cbd9c1f0884f18c9a6d2ee522b227f2f6b3da92495bae8f73161c450";
        internal static string Core(RuntimeKind kind)
        {
            return kind == RuntimeKind.SDL ? SdlCore : kind == RuntimeKind.Original ? OriginalCore : null;
        }
        internal static Selection Select(IEnumerable<RuntimeCandidate> candidates, int foregroundPid, RuntimeCandidate installed)
        {
            var live = candidates.Where(c => c.Kind != RuntimeKind.Unknown).ToArray();
            var focused = live.FirstOrDefault(c => c.Pid == foregroundPid);
            if (focused != null) return Pick(focused, "foreground-game");
            if (live.Length > 0 && live.All(c => c.Kind == live[0].Kind))
                return Pick(live[0], "single-runtime");
            if (installed != null && installed.Kind != RuntimeKind.Unknown) {
                if (live.Length == 0) return Pick(installed, "installed-game");
                var local = live.FirstOrDefault(c => string.Equals(c.Path, installed.Path, StringComparison.OrdinalIgnoreCase));
                if (local != null) return Pick(local, "installed-running-game");
            }
            throw new InvalidOperationException(live.Length == 0
                ? "未找到支持的游戏。请将启动器放在计时器旁，或先打开要计时的游戏。"
                : "原版和 SDL 游戏同时运行，请先切到目标游戏窗口，再启动计时器。");
        }
        private static Selection Pick(RuntimeCandidate item, string reason)
        {
            return new Selection { core = Core(item.Kind), reason = reason, game = item.Path, pid = item.Pid };
        }
        internal static RuntimeKind ImageKind(string path)
        {
            try {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length < 256 || info.Length > 64 * 1024 * 1024) return RuntimeKind.Unknown;
                byte[] image = File.ReadAllBytes(path);
                if (image[0] != 'M' || image[1] != 'Z') return RuntimeKind.Unknown;
                int pe = BitConverter.ToInt32(image, 60);
                if (pe < 64 || pe > image.Length - 24 || BitConverter.ToUInt32(image, pe) != 0x4550) return RuntimeKind.Unknown;
                using (var hash = SHA256.Create())
                    if (BitConverter.ToString(hash.ComputeHash(image)).Replace("-", "").ToLowerInvariant() == OriginalImage)
                        return RuntimeKind.Original;
                // Both the runtime-specific bridge and SDL import are required;
                // an arbitrary SDL application or a filename is insufficient.
                string bytes = Encoding.ASCII.GetString(image);
                if (HasImport(image, pe, "SDL2.dll") &&
                    (bytes.Contains("SDLPAL.LostEdit.v1.") || Encoding.Unicode.GetString(image).Contains("SDLPAL.LostEdit.v1.")))
                    return RuntimeKind.SDL;
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return RuntimeKind.Unknown;
        }
        private static bool HasImport(byte[] image, int pe, string dll)
        {
            int sections = BitConverter.ToUInt16(image, pe + 6), optionalSize = BitConverter.ToUInt16(image, pe + 20);
            int optional = pe + 24, sectionTable = optional + optionalSize;
            if (sections == 0 || sections > 96 || optionalSize < 128 || sectionTable > image.Length - sections * 40) return false;
            ushort magic = BitConverter.ToUInt16(image, optional);
            if (magic != 0x10b && magic != 0x20b) return false;
            int directory = optional + (magic == 0x10b ? 104 : 120);
            int descriptor = Rva(image, sectionTable, sections, BitConverter.ToUInt32(image, directory));
            for (int i = 0; i < 256 && descriptor > 0 && descriptor <= image.Length - 20; i++, descriptor += 20) {
                uint nameRva = BitConverter.ToUInt32(image, descriptor + 12);
                if (nameRva == 0) break;
                int name = Rva(image, sectionTable, sections, nameRva);
                if (name < 0) return false;
                int end = name; while (end < image.Length && end - name < 128 && image[end] != 0) end++;
                if (end < image.Length && image[end] == 0 && string.Equals(Encoding.ASCII.GetString(image, name, end - name), dll, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        private static int Rva(byte[] image, int table, int count, uint rva)
        {
            if (rva == 0) return -1;
            for (int i = 0; i < count; i++) {
                int row = table + i * 40;
                uint start = BitConverter.ToUInt32(image, row + 12), size = BitConverter.ToUInt32(image, row + 16);
                if (rva >= start && (ulong)rva - start < size) {
                    ulong offset = (ulong)BitConverter.ToUInt32(image, row + 20) + rva - start;
                    return offset < (ulong)image.Length ? (int)offset : -1;
                }
            }
            return -1;
        }
        internal static RuntimeCandidate Installed(string launcherDirectory)
        {
            // Search the containing game only, never other installations on disk.
            for (var directory = new DirectoryInfo(launcherDirectory); directory != null; directory = directory.Parent) {
                string standard = System.IO.Path.Combine(directory.FullName, "pal.exe");
                var kind = ImageKind(standard);
                if (kind != RuntimeKind.Unknown) return new RuntimeCandidate { Path = standard, Kind = kind };
                // Maintenance launchers permit a renamed game EXE.
                if (File.Exists(System.IO.Path.Combine(directory.FullName, "geshi-game.v1.json"))) {
                    var found = directory.EnumerateFiles("*.exe").Select(f => new RuntimeCandidate { Path = f.FullName, Kind = ImageKind(f.FullName) })
                        .Where(c => c.Kind != RuntimeKind.Unknown).ToArray();
                    if (found.Length == 1) return found[0];
                    return null;
                }
            }
            return null;
        }
        // A separate, single argument for each incoming argument; no cmd.exe or shell.
        internal static string Quote(string value)
        {
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value) {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); slashes = 0; result.Append(c);
            }
            result.Append('\\', slashes * 2).Append('"'); return result.ToString();
        }
    }
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        private static string ProcessPath(int pid)
        {
            // QUERY_LIMITED_INFORMATION only. No process VM/read/write permission.
            IntPtr handle = OpenProcess(0x1000, false, pid);
            if (handle == IntPtr.Zero) return null;
            try { int size = 32768; var name = new StringBuilder(size); return QueryFullProcessImageName(handle, 0, name, ref size) ? name.ToString() : null; }
            finally { CloseHandle(handle); }
        }
        private static List<RuntimeCandidate> LiveGames(int foreground)
        {
            var result = new List<RuntimeCandidate>(); var kinds = new Dictionary<string, RuntimeKind>(StringComparer.OrdinalIgnoreCase);
            foreach (var process in Process.GetProcesses()) using (process) {
                try {
                    if (process.Id != foreground && process.MainWindowHandle == IntPtr.Zero) continue;
                    string path = ProcessPath(process.Id); if (path == null) continue;
                    RuntimeKind kind;
                    if (!kinds.TryGetValue(path, out kind)) kinds[path] = kind = RuntimeSelection.ImageKind(path);
                    if (kind != RuntimeKind.Unknown) result.Add(new RuntimeCandidate { Path = path, Pid = process.Id, Kind = kind });
                } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            }
            return result;
        }
        [STAThread]
        private static int Main(string[] args)
        {
            bool probe = args.Contains("--probe");
            try {
                string directory = AppDomain.CurrentDomain.BaseDirectory;
                string timer = System.IO.Path.Combine(directory, "Pal98Timer.exe");
                if (!File.Exists(timer)) throw new FileNotFoundException("请将自动启动器放在 Pal98Timer.exe 旁。");
                var forwarded = args.Where(a => a != "--probe").ToList();
                int explicitCore = forwarded.IndexOf("--core");
                Selection choice;
                if (explicitCore >= 0) {
                    if (explicitCore + 1 >= forwarded.Count || string.IsNullOrWhiteSpace(forwarded[explicitCore + 1])) throw new ArgumentException("--core 需要核心名称。");
                    choice = new Selection { core = forwarded[explicitCore + 1], reason = "explicit-core" };
                } else {
                    uint foreground; GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
                    choice = RuntimeSelection.Select(LiveGames((int)foreground), (int)foreground, RuntimeSelection.Installed(directory));
                    forwarded.InsertRange(0, new[] { "--core", choice.core });
                }
                string command = string.Join(" ", forwarded.Select(RuntimeSelection.Quote));
                if (probe) { Console.WriteLine(new JavaScriptSerializer().Serialize(new { selection = choice, timer = timer, arguments = command, started = false })); return 0; }
                using (var mutex = new System.Threading.Mutex(false, "Local\\PalTimer.RuntimeLauncher." + HashPath(timer))) {
                    if (!mutex.WaitOne(0)) return 0;
                    try {
                        foreach (var existing in Process.GetProcessesByName("Pal98Timer")) using (existing) {
                            string path = ProcessPath(existing.Id);
                            if (path == null || string.Equals(path, timer, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException("计时器已开启。请先关闭它再自动选核，以保留当前计时。");
                        }
                        Process.Start(new ProcessStartInfo(timer, command) { WorkingDirectory = directory, UseShellExecute = false });
                    }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
            } catch (Exception error) {
                if (probe) Console.Error.WriteLine(error.Message);
                else MessageBox.Show(error.Message, "自动计时器启动", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
        }
        private static string HashPath(string path)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(path.ToUpperInvariant()))).Replace("-", "");
        }
    }
}
