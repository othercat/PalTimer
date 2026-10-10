using System;
using System.Collections.Generic;
using System.IO;

namespace Pal98Timer
{
    // Keep this bootstrap free of types from companion assemblies. It must be
    // runnable before the CLR resolves the main window and its dependencies.
    internal static class StartupDependencies
    {
        internal static readonly string[] Required = {
            "PalCloudLib.dll", "System.Web.Script.Serialization.dll", "TimerPluginBase.dll"
        };
        internal static readonly string[] Online = { "PalTimerOnline.dll", "PalCompetitionAuth.dll" };
        internal static string DiagnosticDirectory;

        internal static string WriteLog(string message)
        {
            string line = DateTimeOffset.UtcNow.ToString("o") + " " + message + Environment.NewLine;
            foreach (string directory in new[] {
                DiagnosticDirectory ?? Path.Combine(new Pal98.Storage.UserDataStore(Pal98.Storage.UserDataStore.FindInstallation(AppDomain.CurrentDomain.BaseDirectory)).DirectoryPath, "logs", "timer"),
                Path.Combine(Path.GetTempPath(), "PalTimer-Diagnostics") }) {
                try { Directory.CreateDirectory(directory); string path = Path.Combine(directory, "startup.log"); File.AppendAllText(path, line); return path; }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return "日志未能写入磁盘";
        }

        internal static bool Check(string directory, Action<string, bool> notify)
        {
            var required = Missing(directory, Required);
            var online = Missing(directory, Online);
            if (required.Count == 0 && online.Count == 0) return true;
            string message = "计时器目录缺少以下 DLL：\n" + string.Join("\n", required) +
                (required.Count > 0 && online.Count > 0 ? "\n" : "") + string.Join("\n", online) +
                "\n\n目录：" + directory + "\n请从同一版本的完整计时器包恢复这些文件。" +
                (required.Count > 0 ? "\n核心依赖缺失，计时器无法启动。" : "\n联机功能暂不可用，本地计时仍可使用。");
            notify(message, required.Count > 0);
            return required.Count == 0;
        }
        private static List<string> Missing(string directory, IEnumerable<string> names)
        {
            var missing = new List<string>();
            foreach (string name in names) if (!File.Exists(Path.Combine(directory, name))) missing.Add(name);
            return missing;
        }
    }
}
