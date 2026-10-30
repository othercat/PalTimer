using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Pal98.Maintenance
{
    internal static class MaintenanceExecutable
    {
        // Discovery only: executable/bridge hashes still establish game identity.
        internal static string FindConfiguration(string directory)
        {
            var found = Directory.GetFiles(directory, "*.exe", SearchOption.TopDirectoryOnly).Where(path => {
                try {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
                    var value = FileVersionInfo.GetVersionInfo(path);
                    return value.ProductName == "Pal98ConfigTool Geshi" && value.OriginalFilename == "隔世追凶配置工具.exe";
                } catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.ComponentModel.Win32Exception) { return false; }
            }).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            if (found.Length == 0) throw new FileNotFoundException("游戏目录中未找到仙剑配置工具。请将配套程序放回同一目录；文件名可以修改。");
            if (found.Length > 1) throw new InvalidOperationException("游戏目录中有多份仙剑配置工具，请只保留一份：" + string.Join("、", found.Select(Path.GetFileName)));
            return found[0];
        }
        internal static string Quote(string value)
        {
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value) {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); result.Append(c); slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
    }
}
