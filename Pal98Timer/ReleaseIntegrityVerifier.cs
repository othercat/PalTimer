using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Pal98Timer
{
    internal enum IntegrityCheckState { Incomplete, Match, Mismatch }
    internal sealed class IntegrityFileResult
    {
        internal long Size;
        internal string Hash, Error;
        internal bool Missing;
        internal bool? ModuleEnabled;
    }
    // A single caller advances this cursor. Hashing is capped by bytes and QPC;
    // unfinished work resumes next observation, never on the UI/timing thread.
    internal sealed class ReleaseIntegrityVerifier : IDisposable
    {
        internal const int SliceByteBudget = 512 * 1024;
        internal const int BackgroundByteBudget = 256 * 1024;
        internal const int SliceMilliseconds = 2;
        private readonly string root;
        private readonly ReleaseIntegrityManifest manifest;
        private readonly ReleaseIntegrityProfile profile;
        private readonly Queue<string> pending;
        private readonly Queue<ReleaseIntegritySetting> pendingSettings;
        private readonly Dictionary<string, ReleaseIntegrityFile> fileRules;
        private readonly Dictionary<string, IntegrityFileResult> results = new Dictionary<string, IntegrityFileResult>(StringComparer.OrdinalIgnoreCase);
        private readonly byte[] buffer = new byte[65536];
        private FileStream stream;
        private SHA256 sha;
        private MemoryStream moduleBytes;
        private string activePath;
        private long activeSize, activeWriteTime;
        private bool evaluatedComplete;
        private bool directoryComplete;
        private int directoryVisited;
        private IEnumerator<string> directoryCursor;
        private readonly HashSet<string> directorySeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private IntegrityCheckState directoryState = IntegrityCheckState.Match;
        private string directoryDetail;
        private sealed class IniRead { internal string[] Lines; internal long Size, WriteTime; }
        private readonly Dictionary<string, IniRead> iniReads = new Dictionary<string, IniRead>(StringComparer.OrdinalIgnoreCase);
        private Queue<KeyValuePair<string, IniRead>> iniRechecks;
        private long iniCacheBytes;
        private bool settingsComplete;
        private IntegrityCheckState settingsState = IntegrityCheckState.Match;
        private string settingsDetail;
        internal bool Complete { get { return pending.Count == 0 && stream == null && directoryComplete && settingsComplete; } }
        internal bool HasReadFailure { get { return results.Values.Any(r => r.Error != null) ||
            directoryComplete && directoryState == IntegrityCheckState.Incomplete ||
            settingsComplete && settingsState == IntegrityCheckState.Incomplete; } }
        internal string Detail { get; private set; }
        internal IntegrityCheckState State { get; private set; }
        internal string GraphicsChain { get; private set; }
        internal IntegrityCheckState PalDllState { get; private set; }
        internal string PalDllVersion { get; private set; }

        internal ReleaseIntegrityVerifier(string root, ReleaseIntegrityManifest manifest, RuntimeTimingMode mode)
        {
            this.root = Path.GetFullPath(root); this.manifest = manifest; profile = manifest.FindProfile(mode);
            directoryComplete = manifest.exact_directories == null || manifest.exact_directories.Length == 0;
            pendingSettings = new Queue<ReleaseIntegritySetting>(manifest.settings.Concat(profile == null ? new ReleaseIntegritySetting[0] : profile.settings ?? new ReleaseIntegritySetting[0]));
            settingsComplete = pendingSettings.Count == 0;
            fileRules = manifest.files.Concat(profile == null ? new ReleaseIntegrityFile[0] : profile.files)
                .Concat(manifest.graphics_chains.SelectMany(c => c.files)).GroupBy(f => f.path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            // Resolve module switches before visiting conditional payloads.
            pending = new Queue<string>(fileRules.Values.OrderBy(f => f.normalization != null ? 0 : f.enabled_by == null ? 1 : 2).Select(f => f.path));
            Detail = "文件核验尚未完成";
        }
        internal int Advance()
        { return AdvanceCore(SliceByteBudget); }
        internal int AdvanceBackground()
        { return AdvanceCore(BackgroundByteBudget); }
        private int AdvanceCore(int byteBudget)
        {
            if (evaluatedComplete) return 0;
            long deadline = Stopwatch.GetTimestamp() + Math.Max(1, Stopwatch.Frequency * SliceMilliseconds / 1000);
            int consumed = 0;
            do
            {
                if (stream == null)
                {
                    if (pending.Count == 0) break;
                    activePath = pending.Dequeue();
                    if (!Required(fileRules[activePath])) continue;
                    try
                    {
                        string full = Resolve(activePath);
                        stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, buffer.Length, FileOptions.SequentialScan);
                        var info = new FileInfo(full);
                        activeSize = info.Length; activeWriteTime = info.LastWriteTimeUtc.Ticks;
                        sha = SHA256.Create();
                        if (fileRules[activePath].normalization != null) moduleBytes = new MemoryStream();
                    }
                    catch (FileNotFoundException) { results[activePath] = new IntegrityFileResult { Missing = true }; CloseActive(); continue; }
                    catch (DirectoryNotFoundException) { results[activePath] = new IntegrityFileResult { Missing = true }; CloseActive(); continue; }
                    catch (Exception ex) when (ReadFailure(ex)) { results[activePath] = new IntegrityFileResult { Error = ex.Message }; CloseActive(); continue; }
                }
                try
                {
                    int read = stream.Read(buffer, 0, Math.Min(buffer.Length, byteBudget - consumed));
                    if (read == 0)
                    {
                        sha.TransformFinalBlock(buffer, 0, 0);
                        var info = new FileInfo(Resolve(activePath));
                        if (!info.Exists || info.Length != activeSize || info.LastWriteTimeUtc.Ticks != activeWriteTime || stream.Length != activeSize)
                            results[activePath] = new IntegrityFileResult { Error = "核验期间文件发生变化" };
                        else if (moduleBytes != null)
                        {
                            bool enabled;
                            byte[] normalized = NormalizeModuleBytes(moduleBytes.ToArray(), out enabled);
                            using (var normalizedHash = SHA256.Create())
                                results[activePath] = new IntegrityFileResult { Size = normalized.Length,
                                    Hash = BitConverter.ToString(normalizedHash.ComputeHash(normalized)).Replace("-", "").ToLowerInvariant(), ModuleEnabled = enabled };
                        }
                        else results[activePath] = new IntegrityFileResult { Size = activeSize, Hash = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant() };
                        CloseActive();
                        break; // Yield after one file; never hash the next file in this slice.
                    }
                    else {
                        consumed += read;
                        if (moduleBytes != null) {
                            if (moduleBytes.Length + read > 65536) throw new InvalidDataException("Module exceeds normalization budget");
                            moduleBytes.Write(buffer, 0, read);
                        }
                        sha.TransformBlock(buffer, 0, read, buffer, 0);
                    }
                }
                catch (InvalidDataException) { results[activePath] = new IntegrityFileResult { Missing = true }; CloseActive(); break; }
                catch (Exception ex) when (ReadFailure(ex)) { results[activePath] = new IntegrityFileResult { Error = ex.Message }; CloseActive(); }
            } while (consumed < byteBudget && Stopwatch.GetTimestamp() < deadline);
            if (pending.Count == 0 && stream == null && !directoryComplete && Stopwatch.GetTimestamp() < deadline)
                AdvanceDirectory(deadline);
            if (pending.Count == 0 && stream == null && directoryComplete && !settingsComplete && Stopwatch.GetTimestamp() < deadline)
                AdvanceSettings(deadline);
            Evaluate();
            evaluatedComplete = Complete;
            return consumed;
        }
        private void AdvanceDirectory(long deadline)
        {
            var expected = manifest.exact_directories[0];
            try
            {
                if (directoryCursor == null)
                {
                    string path = Resolve(expected.path);
                    if ((File.GetAttributes(path) & FileAttributes.Directory) == 0)
                    { FinishDirectory(IntegrityCheckState.Mismatch, "缺少脚本目录：" + expected.path); return; }
                    directoryCursor = Directory.EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly).GetEnumerator();
                }
                while (Stopwatch.GetTimestamp() < deadline)
                {
                    if (!directoryCursor.MoveNext())
                    {
                        bool match = directorySeen.SetEquals(expected.entries);
                        FinishDirectory(match ? IntegrityCheckState.Match : IntegrityCheckState.Mismatch, match ? "" : "脚本目录存在缺失项目：" + expected.path);
                        return;
                    }
                    if (++directoryVisited > 1024) throw new IOException("脚本目录项目超过单轮核验预算");
                    string entry = directoryCursor.Current;
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("脚本目录项目包含链接");
                    if ((attributes & FileAttributes.Directory) != 0 || !expected.extensions.Contains(Path.GetExtension(entry), StringComparer.OrdinalIgnoreCase)) continue;
                    string name = Path.GetFileName(entry);
                    if (!expected.entries.Contains(name, StringComparer.OrdinalIgnoreCase))
                    { FinishDirectory(IntegrityCheckState.Mismatch, "存在未登记脚本：" + expected.path + "/" + name); return; }
                    directorySeen.Add(name);
                }
            }
            catch (FileNotFoundException) { FinishDirectory(IntegrityCheckState.Mismatch, "缺少脚本目录或项目：" + expected.path); }
            catch (DirectoryNotFoundException) { FinishDirectory(IntegrityCheckState.Mismatch, "缺少脚本目录：" + expected.path); }
            catch (Exception ex) when (ReadFailure(ex)) { FinishDirectory(IntegrityCheckState.Incomplete, "脚本目录尚未核验：" + ex.Message); }
        }
        private void FinishDirectory(IntegrityCheckState state, string detail)
        {
            directoryState = state; directoryDetail = detail; directoryComplete = true;
            if (directoryCursor != null) directoryCursor.Dispose(); directoryCursor = null;
        }
        private void AdvanceSettings(long deadline)
        {
            try
            {
                while (Stopwatch.GetTimestamp() < deadline)
                {
                    if (pendingSettings.Count > 0)
                    {
                        if (!VerifySettingCore(pendingSettings.Dequeue(), true))
                        { settingsComplete = true; settingsState = IntegrityCheckState.Mismatch; settingsDetail = "玩法设置与登记配置不符"; return; }
                    }
                    else
                    {
                        if (iniRechecks == null) iniRechecks = new Queue<KeyValuePair<string, IniRead>>(iniReads);
                        if (iniRechecks.Count == 0) { settingsComplete = true; return; }
                        var entry = iniRechecks.Dequeue(); var info = new FileInfo(Resolve(entry.Key));
                        if (!info.Exists || info.Length != entry.Value.Size || info.LastWriteTimeUtc.Ticks != entry.Value.WriteTime)
                            throw new IOException("核验期间玩法设置发生变化");
                    }
                }
            }
            catch (Exception ex) when (ReadFailure(ex))
            { settingsComplete = true; settingsState = IntegrityCheckState.Incomplete; settingsDetail = "玩法设置尚未完成核验"; }
        }
        private string Resolve(string path)
        {
            if (!ReleaseIntegrityManifest.SafePath(path)) throw new IOException("无效的相对路径");
            string full = Path.GetFullPath(Path.Combine(root, path));
            for (string current = full; current != null && !current.Equals(root, StringComparison.OrdinalIgnoreCase); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("资源路径包含链接，尚未核验：" + path);
            return full;
        }
        private IntegrityCheckState Check(ReleaseIntegrityFile file)
        {
            if (!Required(file)) return IntegrityCheckState.Match;
            IntegrityFileResult result;
            if (!results.TryGetValue(file.path, out result)) return IntegrityCheckState.Incomplete;
            if (result.Error != null) { Detail = file.path + "：" + result.Error; return IntegrityCheckState.Incomplete; }
            if (result.Missing || result.Size != file.size || !string.Equals(result.Hash, file.sha256, StringComparison.OrdinalIgnoreCase))
            { Detail = file.path; return IntegrityCheckState.Mismatch; }
            return IntegrityCheckState.Match;
        }
        private void Evaluate()
        {
            EvaluatePalDll();
            if (!manifest.frozen) { State = IntegrityCheckState.Incomplete; Detail = "定版清单尚未冻结"; return; }
            State = IntegrityCheckState.Match;
            foreach (var file in manifest.files.Concat(profile == null ? new ReleaseIntegrityFile[0] : profile.files))
            {
                var state = Check(file);
                if (state == IntegrityCheckState.Mismatch) { State = state; return; }
                if (state == IntegrityCheckState.Incomplete) State = state;
            }
            bool pendingChain = false; string matching = null;
            foreach (var chain in manifest.graphics_chains)
            {
                var states = chain.files.Select(Check).ToArray();
                bool absent = (chain.absent_files ?? new string[0]).All(p => !File.Exists(Resolve(p)) && !Directory.Exists(Resolve(p)));
                if (absent && states.All(s => s == IntegrityCheckState.Match)) { matching = chain.id; break; }
                if (absent && !states.Contains(IntegrityCheckState.Mismatch)) pendingChain = true;
            }
            GraphicsChain = matching;
            if (matching == null)
            {
                State = pendingChain ? IntegrityCheckState.Incomplete : IntegrityCheckState.Mismatch;
                Detail = pendingChain ? "图形链核验尚未完成" : "图形链与登记组合不匹配";
                return;
            }
            if (!Complete) { State = IntegrityCheckState.Incomplete; return; }
            if (directoryState != IntegrityCheckState.Match) { State = directoryState; Detail = directoryDetail; return; }
            if (settingsState != IntegrityCheckState.Match) { State = settingsState; Detail = settingsDetail; return; }
            if (profile == null) { State = IntegrityCheckState.Incomplete; Detail = "当前内容配置尚未覆盖"; return; }
            if (State == IntegrityCheckState.Match) Detail = "";
        }
        private void EvaluatePalDll()
        {
            PalDllState = IntegrityCheckState.Incomplete; PalDllVersion = "";
            IntegrityFileResult result;
            if (!manifest.frozen || !results.TryGetValue("PAL.dll", out result) || result.Error != null) return;
            // Reuse the existing budgeted scan; no additional file IO or hashing.
            PalDllVersion = result.Missing ? null : manifest.OfficialPalDllVersion(result.Size, result.Hash);
            PalDllState = PalDllVersion == null ? IntegrityCheckState.Mismatch : IntegrityCheckState.Match;
            PalDllVersion = PalDllVersion ?? "";
        }
        private bool VerifySetting(ReleaseIntegritySetting setting)
        { return VerifySettingCore(setting, false); }
        private bool VerifySettingCore(ReleaseIntegritySetting setting, bool cache)
        {
            string path = Resolve(setting.file);
            IniRead input;
            if (!cache || !iniReads.TryGetValue(setting.file, out input))
            {
                long size, writeTime;
                try { var info = new FileInfo(path); size = info.Length; writeTime = info.LastWriteTimeUtc.Ticks; if (size > 1024 * 1024) return false; }
                catch (FileNotFoundException) { return false; }
                catch (DirectoryNotFoundException) { return false; }
                if (cache && iniCacheBytes + size > 4 * 1024 * 1024) throw new IOException("玩法设置核验达到本轮缓存上限");
                input = new IniRead { Lines = File.ReadAllLines(path), Size = size, WriteTime = writeTime };
                var after = new FileInfo(path);
                if (!after.Exists || after.Length != size || after.LastWriteTimeUtc.Ticks != writeTime) throw new IOException("核验期间玩法设置发生变化");
                if (cache) { iniReads.Add(setting.file, input); iniCacheBytes += size; }
            }
            string section = "", found = null;
            foreach (string source in input.Lines)
            {
                string line = source.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                int separator = line.IndexOf('=');
                if (separator < 1 || !section.Equals(setting.section, StringComparison.OrdinalIgnoreCase) ||
                    !line.Substring(0, separator).Trim().Equals(setting.key, StringComparison.OrdinalIgnoreCase)) continue;
                if (found != null) return false; // Do not guess the runtime's duplicate-key precedence.
                found = line.Substring(separator + 1).Trim();
            }
            string[] allowed = setting.allowed_values ?? new[] { setting.value };
            bool numeric = setting.min_value.HasValue || allowed.All(value => IsInteger(Normalize(value)));
            string actual = Normalize(found == null ? setting.default_value : ParseIniValue(found, numeric));
            if (setting.min_value.HasValue) {
                long number;
                return long.TryParse(actual, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out number) &&
                    number >= setting.min_value.Value && number <= setting.max_value.Value;
            }
            return allowed.Any(value => string.Equals(Normalize(value), actual,
                setting.ignore_case ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        }
        private bool Required(ReleaseIntegrityFile file)
        {
            return file.enabled_by == null || file.enabled_by.Any(path => {
                IntegrityFileResult result;
                return !results.TryGetValue(path, out result) || result.ModuleEnabled != false;
            });
        }
        // ConfigTool only replaces the top-level boolean token. Pin all other
        // bytes, including payload identity and unknown fields; never bless a
        // startup file as a new expected baseline. BOM is transport-only.
        internal static byte[] NormalizeModuleBytes(byte[] bytes, out bool enabled)
        {
            enabled = false;
            if (bytes == null || bytes.Length > 65536) throw new InvalidDataException("Invalid module size");
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
            catch (DecoderFallbackException ex) { throw new InvalidDataException("Invalid module encoding", ex); }
            Dictionary<string, object> data;
            try { data = new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 32 }.DeserializeObject(text) as Dictionary<string, object>; }
            catch (ArgumentException ex) { throw new InvalidDataException("Invalid module JSON", ex); }
            object schema, value;
            if (data == null || !data.TryGetValue("schema", out schema) || !Equals(schema, "PAL98.CopymenScriptModule.v1") ||
                !data.TryGetValue("enabled", out value) || !(value is bool)) throw new InvalidDataException("Invalid module switch");
            enabled = (bool)value;
            int depth = 0, found = -1, length = 0;
            for (int index = 0; index < text.Length; index++) {
                if (text[index] == '"') {
                    int start = ++index;
                    bool escaped = false;
                    while (index < text.Length) {
                        if (!escaped && text[index] == '"') break;
                        escaped = !escaped && text[index] == '\\';
                        if (text[index] != '\\') escaped = false;
                        index++;
                    }
                    if (depth != 1 || index - start != 7 || string.CompareOrdinal(text, start, "enabled", 0, 7) != 0) continue;
                    int cursor = index + 1;
                    while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
                    if (cursor >= text.Length || text[cursor++] != ':') continue;
                    while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
                    int count = text.IndexOf("true", cursor, StringComparison.Ordinal) == cursor ? 4 :
                        text.IndexOf("false", cursor, StringComparison.Ordinal) == cursor ? 5 : 0;
                    if (count == 0 || found >= 0) throw new InvalidDataException("Ambiguous module switch");
                    found = cursor; length = count;
                } else if (text[index] == '{') depth++;
                else if (text[index] == '}') depth--;
            }
            if (found < 0) throw new InvalidDataException("Missing module switch");
            return new UTF8Encoding(false).GetBytes(text.Substring(0, found) + "false" + text.Substring(found + length));
        }
        // Match ConfigTool's quoted values / whitespace-prefixed inline comments.
        // Numeric gameplay owners additionally accept a semicolon/hash directly
        // after a complete integer; never apply that shortcut to string paths.
        internal static string ParseIniValue(string value, bool numeric)
        {
            bool single = false, quoted = false;
            for (int i = 0; i < value.Length; ++i)
            {
                char c = value[i];
                if (c == '"' && !single) { quoted = !quoted; continue; }
                if (c == '\'' && !quoted) { single = !single; continue; }
                if ((c == ';' || c == '#') && !single && !quoted &&
                    (i == 0 || char.IsWhiteSpace(value[i - 1]) || numeric && IsInteger(Normalize(value.Substring(0, i)))))
                { value = value.Substring(0, i); break; }
            }
            value = value.Trim();
            if (value.Length >= 2 && (value[0] == '"' && value[value.Length - 1] == '"' || value[0] == '\'' && value[value.Length - 1] == '\''))
                value = value.Substring(1, value.Length - 2);
            return value;
        }
        private static bool IsInteger(string value)
        {
            long number;
            return value != null && long.TryParse(value.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out number);
        }
        internal static string Normalize(string value)
        {
            if (value == null) return null;
            long number; value = value.Trim();
            if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) return "1";
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) return "0";
            return long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out number)
                ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : value;
        }
        internal static bool ReadFailure(Exception ex)
        { return ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is ArgumentException; }
        private void CloseActive() { if (stream != null) stream.Dispose(); if (sha != null) sha.Dispose(); if (moduleBytes != null) moduleBytes.Dispose(); stream = null; sha = null; moduleBytes = null; }
        public void Dispose() { CloseActive(); if (directoryCursor != null) directoryCursor.Dispose(); directoryCursor = null; }
    }
}
