using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Pal98Timer
{
    // A local reference identity is deliberately not a GameplayIdentity or a
    // RankingConfiguration. Reading a setting cannot certify runtime behavior.
    internal sealed class LocalTimelineConfiguration
    {
        public string schema { get; set; } = "PAL98.LocalTimelineConfiguration.v1";
        public string configuration_id { get; set; }
        public string name { get; set; }
        public string settings_sha256 { get; set; }
        public string route_sha256 { get; set; }
        public bool hardcore { get; set; }
        public string source { get; set; }
        public string key { get; set; }
        internal bool Valid => schema == "PAL98.LocalTimelineConfiguration.v1" &&
            Guid.TryParseExact(configuration_id, "D", out _) && !string.IsNullOrWhiteSpace(name) && name.Length <= 160 &&
            CompetitionProtocol.Digest(settings_sha256) && CompetitionProtocol.Digest(route_sha256) &&
            (source == "configuration" || source == "manual") && key == MakeKey(configuration_id, settings_sha256, route_sha256, hardcore);
        private static string MakeKey(string id, string settings, string route, bool hardcore) =>
            CompetitionProtocol.Hash("PAL98.LocalTimelineConfiguration.v1\n" + id.ToLowerInvariant() + "\n" + settings + "\n" + route + "\n" + (hardcore ? "hardcore" : "ordinary") + "\n");
        internal LocalTimelineConfiguration ForRoute(string route)
        {
            var result = (LocalTimelineConfiguration)MemberwiseClone();
            result.route_sha256 = route;
            result.key = MakeKey(configuration_id, settings_sha256, route, hardcore);
            return result;
        }
        internal static LocalTimelineConfiguration Create(string id, string name, string settings, string route, bool hardcore, string source)
        {
            var result = new LocalTimelineConfiguration { configuration_id = id.ToLowerInvariant(), name = name,
                settings_sha256 = settings, route_sha256 = route, hardcore = hardcore, source = source,
                key = MakeKey(id, settings, route, hardcore) };
            if (!result.Valid) throw new InvalidDataException("本地配置身份不完整。");
            return result;
        }
        internal static LocalTimelineConfiguration Manual(string name, string route, bool hardcore)
        {
            string id = Guid.NewGuid().ToString("D");
            return Create(id, name.Trim(), CompetitionProtocol.Hash("manual\n" + id), route, hardcore, "manual");
        }
        internal string BestPath => BestTimelineStorage.IoPath(TimerUserSettings.TimelinePath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "Timelines", "Configurations", key, "best.json")));
        internal static string CatalogDirectory => Path.GetDirectoryName(TimerUserSettings.TimelinePath(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Timelines", "Configurations", "profiles", "catalog.json")));
        internal void Remember()
        {
            string path = Path.Combine(CatalogDirectory, key + ".json"), text = Json().Serialize(this);
            if (!File.Exists(BestTimelineStorage.IoPath(path)) || LocalConfigurationReader.ReadText(path, 16384) != text) BestTimelineStorage.Write(path, text);
        }
        internal static List<LocalTimelineConfiguration> Known(string route)
        {
            var result = new List<LocalTimelineConfiguration>();
            string directory = BestTimelineStorage.IoPath(CatalogDirectory);
            if (!Directory.Exists(directory)) return result;
            foreach (string path in Directory.EnumerateFiles(directory, "*.json").Take(1000)) {
                if (!CompetitionProtocol.Digest(Path.GetFileNameWithoutExtension(path))) continue;
                try {
                    var value = Json().Deserialize<LocalTimelineConfiguration>(LocalConfigurationReader.ReadText(path, 16384));
                    if (value?.Valid == true && value.key == Path.GetFileNameWithoutExtension(path)) result.Add(value.ForRoute(route));
                } catch (Exception e) when (LocalConfigurationReader.ReadError(e)) { }
            }
            return result.GroupBy(p => p.key).Select(p => p.First()).OrderBy(p => p.name).ToList();
        }
        internal static JavaScriptSerializer Json() => new JavaScriptSerializer { MaxJsonLength = 24 * 1024 * 1024, RecursionLimit = 48 };
        public override string ToString() => name + (hardcore ? " [硬核]" : " [普通]") + " · " + key.Substring(0, 8);
    }

    internal static class LocalConfigurationReader
    {
        internal const string Pointer = "palmod/Configurations/current.json";
        internal sealed class Dependency { public string path { get; set; } public long size { get; set; } public string sha256 { get; set; } }
        internal sealed class Preset
        {
            public string schema { get; set; }
            public string id { get; set; }
            public string name { get; set; }
            public string producer_version { get; set; }
            public string settings_contract { get; set; }
            public string minimum_runtime { get; set; }
            public string minimum_timer { get; set; }
            public SortedDictionary<string, string> files { get; set; }
            public SortedDictionary<string, string> tournament { get; set; }
            public Dependency[] dependencies { get; set; }
            public string content_sha256 { get; set; }
        }
        private sealed class Selection { public string id { get; set; } public string content_sha256 { get; set; } public string storage_key { get; set; } }
        private sealed class ProfilePointer {
            public string schema { get; set; } public string profile_id { get; set; } public string profile_version { get; set; }
            public string descriptor_sha256 { get; set; } public string staging_relative_path { get; set; }
        }
        private sealed class ProfileResource { public string kind { get; set; } public string relative_path { get; set; } public string sha256 { get; set; } public long size_bytes { get; set; } }
        private sealed class ProfileDescriptor { public string schema { get; set; } public string profile_id { get; set; } public string profile_version { get; set; } public ProfileResource[] resource_set { get; set; } }
        internal static bool ReadError(Exception e) => e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is FormatException ||
            e is InvalidOperationException || e is System.ComponentModel.Win32Exception || e is System.Security.SecurityException;
        internal static string ReadText(string path, int max)
        {
            byte[] bytes = ReadOptionalBytes(path, max);
            if (bytes == null) throw new FileNotFoundException("配置文件不存在。", path);
            using (var stream = new MemoryStream(bytes)) {
                using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true)) return reader.ReadToEnd();
            }
        }
        private static string HashBytes(byte[] bytes)
        { using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        internal static string PresetDigest(Preset p) => HashBytes(Encoding.UTF8.GetBytes(LocalTimelineConfiguration.Json().Serialize(new {
            p.schema, p.id, p.name, p.producer_version, p.settings_contract, p.minimum_runtime, p.minimum_timer, p.files, p.tournament, p.dependencies })));
        internal static Preset Parse(string text)
        {
            var p = LocalTimelineConfiguration.Json().Deserialize<Preset>(text);
            // ConfigTool signs/compares an ordinal projection, not the host
            // culture's dictionary order (notably Graphics/... vs config.ini).
            if (p?.files != null) p.files = new SortedDictionary<string, string>(p.files, StringComparer.Ordinal);
            if (p?.tournament != null) p.tournament = new SortedDictionary<string, string>(p.tournament, StringComparer.Ordinal);
            if (p == null || p.schema != "PAL98.ConfigurationPreset.v1" || p.settings_contract != "PAL98.Settings.v1" ||
                !Guid.TryParseExact(p.id, "D", out _) || string.IsNullOrWhiteSpace(p.name) || p.name.Length > 160 ||
                p.files == null || p.tournament == null || p.dependencies == null || p.files.Count == 0 || p.files.Count > 1024 || !p.files.ContainsKey("config.ini") ||
                p.content_sha256 != PresetDigest(p)) throw new InvalidDataException("配置内容校验失败，未用于本地最佳线。");
            return p;
        }
        private static bool SafePath(string path) => !string.IsNullOrEmpty(path) && path.Length < 500 &&
            !path.Contains('\\') && !path.Contains(':') && path.Split('/').All(p => p.Length > 0 && p != "." && p != ".." &&
                p.TrimEnd(' ', '.') == p && p.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
        internal static LocalTimelineConfiguration ReadCurrent(string root, string route, string localAppData = null)
        {
            string transaction = Pal98.Storage.UserDataStore.ReadGamePath(root, "hardcore-transaction.pending", localAppData);
            if (File.Exists(transaction)) throw new InvalidDataException("配置事务尚未完成，请先在配置工具恢复配置。");
            string pointerPath = Pal98.Storage.UserDataStore.ReadGamePath(root, Pointer, localAppData);
            string before = ReadText(pointerPath, 16384);
            var selection = LocalTimelineConfiguration.Json().Deserialize<Selection>(before);
            if (selection == null || !Guid.TryParseExact(selection.id, "D", out _) || !CompetitionProtocol.Digest(selection.content_sha256))
                throw new InvalidDataException("当前配置指针无效，请明确选择本地配置。");
            string directory = Path.GetDirectoryName(pointerPath);
            IEnumerable<string> files;
            if (!string.IsNullOrEmpty(selection.storage_key)) {
                if (Path.GetFileName(selection.storage_key) != selection.storage_key || !SafePath(selection.storage_key) ||
                    !selection.storage_key.EndsWith(".pal98config", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("当前配置文件名无效。");
                files = new[] { Path.Combine(directory, selection.storage_key) };
            } else files = Directory.EnumerateFiles(directory, "*.pal98config").Take(1001).ToArray();
            Preset match = null;
            foreach (string path in files) {
                Preset candidate;
                try { candidate = Parse(ReadText(path, 24 * 1024 * 1024)); }
                catch (Exception e) when (string.IsNullOrEmpty(selection.storage_key) && ReadError(e)) { continue; }
                if (candidate.id != selection.id || candidate.content_sha256 != selection.content_sha256) continue;
                if (match != null) throw new InvalidDataException("当前配置有重复身份，请明确选择本地配置。");
                match = candidate;
            }
            if (match == null) throw new InvalidDataException("当前配置缺失或已被替换，请明确选择本地配置。");
            var live = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var extra = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var observed = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var paths = new HashSet<string>(match.files.Keys.Where(p => !PresentationFile(p.ToLowerInvariant())), StringComparer.OrdinalIgnoreCase);
            paths.UnionWith(new[] { "config.ini", "mod.ini", "dxwrapper.ini", "palmod/random-skill-selection.v1.json" });
            string scripts = Pal98.Storage.UserDataStore.ReadGamePath(root, "copymen_scripts", localAppData);
            if (Directory.Exists(scripts)) foreach (string script in Directory.EnumerateFiles(scripts).Take(1025)) {
                string leaf = Path.GetFileName(script);
                if (new[] { ".json", ".rule", ".txt" }.Contains(Path.GetExtension(leaf).ToLowerInvariant())) paths.Add("copymen_scripts/" + leaf);
            }
            if (paths.Count > 1024) throw new InvalidDataException("配置文件数量过多。");
            foreach (string relative in paths) {
                if (!SafePath(relative)) throw new InvalidDataException("配置路径无效。");
                string physical = Pal98.Storage.UserDataStore.ReadGamePath(root, relative, localAppData);
                byte[] bytes = ReadOptionalBytes(physical, 16 * 1024 * 1024);
                observed[physical] = bytes;
                if (bytes == null) extra["missing:" + relative.ToLowerInvariant()] = "1";
                else live[relative] = Convert.ToBase64String(bytes);
            }
            if (!live.ContainsKey("config.ini")) throw new InvalidDataException("当前生效 config.ini 不可读，请明确选择本地配置。");
            // The container intentionally strips per-machine Hardcore settings;
            // grouping must use the effective files, including local overlays.
            match.files = live;
            var replacements = ReadProfile(root, localAppData, Decode(Convert.FromBase64String(live["config.ini"])), extra, observed);
            foreach (string resource in new[] { "DATA.MKF", "SSS.MKF", "MAP.MKF", "WORD.DAT", "M.MSG", "ABC.MKF", "FIRE.MKF" }) {
                string hash = replacements.TryGetValue(resource, out string replacement) ? replacement : HashOptionalFile(Path.Combine(root, resource));
                extra["resource:" + resource.ToLowerInvariant()] = hash ?? "missing";
            }
            foreach (var dependency in match.dependencies ?? new Dependency[0]) {
                if (dependency == null || !SafePath(dependency.path)) throw new InvalidDataException("配置依赖路径无效。");
                string path = dependency.path.ToLowerInvariant();
                if (PresentationFile(path) || ProfilePointerFile(path) || path.StartsWith("tools/", StringComparison.Ordinal)) continue;
                if (new[] { ".mkf", ".zip", ".json", ".rule", ".txt" }.Contains(Path.GetExtension(path)))
                    extra["effective-dependency:" + path] = HashOptionalFile(Pal98.Storage.UserDataStore.ReadGamePath(root, dependency.path, localAppData)) ?? "missing";
            }
            var value = FromPreset(match, route, extra);
            foreach (var file in observed) {
                byte[] after = ReadOptionalBytes(file.Key, 16 * 1024 * 1024);
                if (file.Value == null ? after != null : after == null || !file.Value.SequenceEqual(after)) throw new InvalidDataException("读取期间配置已改变。");
            }
            if (before != ReadText(pointerPath, 16384)) throw new InvalidDataException("读取期间配置已切换，等待重新读取。");
            if (File.Exists(transaction)) throw new InvalidDataException("读取期间配置事务尚未完成。");
            return value;
        }
        private static Dictionary<string, string> ReadProfile(string root, string localAppData, string ini,
            IDictionary<string, string> facts, IDictionary<string, byte[]> observed)
        {
            var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var settings = NormalizeIni("config.ini", ini);
            if (!settings.TryGetValue("patch/defaultpatch", out string patch) || string.IsNullOrWhiteSpace(patch)) return replacements;
            patch = patch.Split('\n')[0];
            if (!SafePath(patch) || patch.Contains('/')) throw new InvalidDataException("内容包名称无效。");
            if (patch.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) patch = patch.Substring(0, patch.Length - 4);
            Func<string, int, byte[]> read = (relative, max) => {
                string path = Pal98.Storage.UserDataStore.ReadGamePath(root, relative, localAppData);
                byte[] bytes = ReadOptionalBytes(path, max); observed[path] = bytes; return bytes;
            };
            byte[] manifest = read("patches/" + patch + ".json", 4 * 1024 * 1024);
            facts["selected-patch"] = manifest == null ? "missing" : HashBytes(manifest);
            facts["selected-patch-archive"] = HashOptionalFile(Pal98.Storage.UserDataStore.ReadGamePath(root, "patches/" + patch + ".zip", localAppData)) ?? "missing";
            if (manifest == null) return replacements;
            var package = LocalTimelineConfiguration.Json().Deserialize<Dictionary<string, object>>(Decode(manifest));
            if (package != null && package.TryGetValue("patches", out object entries) && entries is IEnumerable collection) {
                var selected = collection.Cast<object>().OfType<Dictionary<string, object>>().Where(entry =>
                    entry.TryGetValue("zip_file", out object zip) && string.Equals(zip as string, patch + ".zip", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (selected.Length != 1) throw new InvalidDataException("内容包清单中无法唯一确定当前内容。");
                package = selected[0];
            }
            if (package == null || !package.TryGetValue("game_profile", out object envelopeObject)) return replacements;
            var json = LocalTimelineConfiguration.Json();
            var envelope = json.Deserialize<ProfilePointer>(json.Serialize(envelopeObject));
            if (envelope == null || envelope.schema != "PAL98.GameProfileEnvelope.v1" || !CompetitionProtocol.Digest(envelope.descriptor_sha256) || !SafePath(envelope.profile_id) || !SafePath(envelope.profile_version))
                throw new InvalidDataException("内容包 Profile 身份不完整。");
            byte[] current = read("palmod/Profiles/current.json", 16384);
            if (current == null) throw new InvalidDataException("当前内容 Profile 尚未激活。");
            var pointer = json.Deserialize<ProfilePointer>(Decode(current));
            ValidateProfilePointer(pointer);
            if (pointer.profile_id != envelope.profile_id) throw new InvalidDataException("当前内容 Profile 与所选内容包不符。");
            if (pointer.descriptor_sha256 != envelope.descriptor_sha256) {
                byte[] update = read("palmod/Profiles/updates/" + envelope.descriptor_sha256 + ".json", 16384);
                if (update == null) throw new InvalidDataException("当前内容 Profile 更新指针缺失。");
                pointer = json.Deserialize<ProfilePointer>(Decode(update)); ValidateProfilePointer(pointer);
            }
            if (pointer.profile_id != envelope.profile_id || pointer.profile_version != envelope.profile_version || pointer.descriptor_sha256 != envelope.descriptor_sha256)
                throw new InvalidDataException("当前内容 Profile 与所选内容包不符。");
            string stage = "palmod/Profiles/" + pointer.staging_relative_path + "/";
            byte[] descriptorBytes = read(stage + "manifest/game-profile.json", 4 * 1024 * 1024);
            if (descriptorBytes == null || HashBytes(descriptorBytes) != pointer.descriptor_sha256) throw new InvalidDataException("内容 Profile 描述文件不完整。");
            var descriptor = json.Deserialize<ProfileDescriptor>(Decode(descriptorBytes));
            if (descriptor == null || descriptor.schema != "PAL98.GameProfile.v1" || descriptor.profile_id != pointer.profile_id || descriptor.profile_version != pointer.profile_version ||
                descriptor.resource_set == null || descriptor.resource_set.Length > 1024) throw new InvalidDataException("内容 Profile 描述格式无效。");
            // This is a local grouping checksum, not a runtime coverage claim.
            facts["profile-descriptor"] = pointer.descriptor_sha256;
            foreach (var resource in descriptor.resource_set) {
                if (resource == null || string.IsNullOrWhiteSpace(resource.kind) || !SafePath(resource.relative_path) || !CompetitionProtocol.Digest(resource.sha256) || resource.size_bytes < 0)
                    throw new InvalidDataException("内容 Profile 资源路径无效。");
                string relative = resource.relative_path;
                if (PresentationFile(relative.ToLowerInvariant()) || resource.kind.Equals("MUS.MKF", StringComparison.OrdinalIgnoreCase)) continue;
                string preferred = resource.kind == "EXTERNAL.MAGIC.RLE.MANIFEST" ? "x/m" :
                    resource.kind.StartsWith("EXTERNAL.MAGIC.RLE.CHUNK.", StringComparison.Ordinal) ? "x/c/" + resource.sha256 : relative;
                string physical = Pal98.Storage.UserDataStore.ReadGamePath(root, stage + preferred, localAppData);
                if (preferred != relative && !File.Exists(physical)) physical = Pal98.Storage.UserDataStore.ReadGamePath(root, stage + relative, localAppData);
                string hash = HashOptionalFile(physical) ?? "missing";
                if (facts.ContainsKey("profile-resource:" + resource.kind)) throw new InvalidDataException("内容 Profile 资源重复。");
                facts["profile-resource:" + resource.kind] = hash;
                replacements[resource.kind] = hash;
            }
            return replacements;
        }
        private static void ValidateProfilePointer(ProfilePointer p)
        {
            if (p == null || p.schema != "PAL98.EffectiveGameProfilePointer.v1" || !CompetitionProtocol.Digest(p.descriptor_sha256) ||
                !SafePath(p.profile_id) || !SafePath(p.profile_version) || !SafePath(p.staging_relative_path) ||
                p.staging_relative_path != "p/" + p.descriptor_sha256.Substring(0, 32) && p.staging_relative_path != p.profile_id + "/" + p.profile_version)
                throw new InvalidDataException("内容 Profile 指针无效。");
        }
        private static byte[] ReadOptionalBytes(string path, int max)
        {
            try {
                using (var stream = new FileStream(BestTimelineStorage.IoPath(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                    if (stream.Length > max) throw new InvalidDataException("配置文件过大。");
                    using (var output = new MemoryStream()) {
                        byte[] buffer = new byte[16384]; int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0) {
                            if (output.Length + read > max) throw new InvalidDataException("配置文件过大。");
                            output.Write(buffer, 0, read);
                        }
                        return output.ToArray();
                    }
                }
            } catch (FileNotFoundException) { return null; } catch (DirectoryNotFoundException) { return null; }
        }
        private static string HashOptionalFile(string path)
        {
            try {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = System.Security.Cryptography.SHA256.Create()) {
                    if (stream.Length > 256 * 1024 * 1024) throw new InvalidDataException("玩法资源过大，请手动选择本地参考配置。");
                    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                }
            } catch (FileNotFoundException) { return null; } catch (DirectoryNotFoundException) { return null; }
        }
        internal static LocalTimelineConfiguration FromFile(string path, string route)
        {
            var preset = Parse(ReadText(path, 24 * 1024 * 1024));
            string parent = Path.GetDirectoryName(Path.GetFullPath(path));
            string game = Directory.GetParent(parent)?.Parent?.FullName;
            if (game != null && parent.Equals(Path.Combine(game, "palmod", "Configurations"), StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(game, "PAL.exe"))) {
                try {
                    var pointer = LocalTimelineConfiguration.Json().Deserialize<Selection>(ReadText(Pal98.Storage.UserDataStore.ReadGamePath(game, Pointer), 16384));
                    if (pointer?.id == preset.id && pointer.content_sha256 == preset.content_sha256) return ReadCurrent(game, route);
                } catch (Exception e) when (ReadError(e)) { }
            }
            var reference = FromPreset(preset, route);
            reference.source = "manual"; reference.name += "（文件参考）";
            if (reference.name.Length > 160) reference.name = reference.name.Substring(0, 154) + "（文件参考）";
            return reference;
        }
        internal static LocalTimelineConfiguration FromPreset(Preset preset, string route, IDictionary<string, string> additionalFacts = null)
        {
            var facts = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (additionalFacts != null) foreach (var fact in additionalFacts) facts[fact.Key] = fact.Value;
            bool hardcore = false;
            int total = 0;
            foreach (var file in preset.files) {
                if (!SafePath(file.Key)) throw new InvalidDataException("配置包含无效文件路径。");
                string path = file.Key.ToLowerInvariant();
                if (PresentationFile(path)) continue;
                if (ProfilePointerFile(path) && additionalFacts != null) continue;
                if (file.Value == null) { facts["missing:" + path] = "1"; continue; }
                byte[] bytes = Convert.FromBase64String(file.Value);
                total += bytes.Length; if (total > 16 * 1024 * 1024) throw new InvalidDataException("配置内容过大。");
                if (path.EndsWith(".ini", StringComparison.Ordinal)) {
                    var ini = NormalizeIni(path, Decode(bytes));
                    foreach (var pair in ini) facts[path + ":" + pair.Key] = pair.Value;
                    if (path == "config.ini" && ini.TryGetValue("hardcore/enabled", out string enabled)) hardcore = enabled.Split('\n')[0] == "1";
                } else if (path.EndsWith(".json", StringComparison.Ordinal)) facts[path] = CanonicalJson(LocalTimelineConfiguration.Json().DeserializeObject(Decode(bytes)));
                else facts[path] = HashBytes(bytes);
            }
            foreach (var dependency in preset.dependencies ?? new Dependency[0]) {
                if (dependency == null || !SafePath(dependency.path) || !CompetitionProtocol.Digest(dependency.sha256)) throw new InvalidDataException("配置依赖身份无效。");
                string path = dependency.path.ToLowerInvariant();
                if (!PresentationFile(path) && !ProfilePointerFile(path) && !path.StartsWith("tools/", StringComparison.Ordinal) &&
                    (path.EndsWith(".mkf") || path.EndsWith(".zip") || path.EndsWith(".json") || path.EndsWith(".rule") || path.EndsWith(".txt"))) facts["dependency:" + path] = dependency.sha256;
            }
            string canonical = "PAL98.LocalConfigurationSettings.v1\n" + string.Concat(facts.Select(p => GameplayModeReader.Quote(p.Key) + ":" + GameplayModeReader.Quote(p.Value) + "\n"));
            return LocalTimelineConfiguration.Create(preset.id, preset.name, CompetitionProtocol.Hash(canonical), route, hardcore, "configuration");
        }
        private static string Decode(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true)) {
                try { return reader.ReadToEnd(); }
                catch (DecoderFallbackException) { return Encoding.GetEncoding(936).GetString(bytes); }
            }
        }
        // Deliberately narrow exclusions. Unknown files/options contribute to the
        // identity, even if an older timer does not understand their semantics.
        private static bool ProfilePointerFile(string path) => path == "palmod/profiles/current.json" || path == "palmod/profiles/previous.json";
        private static bool PresentationFile(string path) => path == "music-new.ini" || path == "mus.mkf" || path.StartsWith("midi/", StringComparison.Ordinal) || path == "key.ini" || path == "reshade.ini" ||
            path.StartsWith("musics/", StringComparison.Ordinal) || path.StartsWith("voices/", StringComparison.Ordinal) ||
            path.StartsWith("reshade-shaders/", StringComparison.Ordinal) || path == "palmod/common-tools.v1.json" ||
            path == "palmod/title-presentation.v1.json" || path == "graphics/renderer-state.v1.json" || path.StartsWith("graphics/presets/", StringComparison.Ordinal);
        internal static SortedDictionary<string, string> NormalizeIni(string path, string text)
        {
            var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
            string section = "";
            foreach (string raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
                string line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant(); continue; }
                int split = line.IndexOf('=');
                if (split < 1) { result[section + "/raw/" + result.Count] = line; continue; }
                string key = line.Substring(0, split).Trim().ToLowerInvariant(), value = IniValue(line.Substring(split + 1));
                if (PresentationOption(path, section, key)) continue;
                string full = section + "/" + key;
                // Preserve duplicate occurrences, whose precedence may differ
                // between consumers; never silently merge distinct configs.
                if (result.ContainsKey(full)) result[full] += "\n" + value;
                else result.Add(full, value);
            }
            return result;
        }
        private static string IniValue(string text)
        {
            bool single = false, quoted = false;
            for (int i = 0; i < text.Length; ++i) {
                if (text[i] == '"' && !single) quoted = !quoted;
                else if (text[i] == '\'' && !quoted) single = !single;
                else if ((text[i] == ';' || text[i] == '#') && !single && !quoted && (i == 0 || char.IsWhiteSpace(text[i - 1]))) { text = text.Substring(0, i); break; }
            }
            text = text.Trim();
            if (text.Length >= 2 && (text[0] == '"' && text[text.Length - 1] == '"' || text[0] == '\'' && text[text.Length - 1] == '\'')) text = text.Substring(1, text.Length - 2);
            return text;
        }
        private static bool PresentationOption(string path, string section, string key)
        {
            if (path == "config.ini") {
                // The runtime persists a new seed each run; the pool/mode and
                // enable flags remain part of the configuration identity.
                if (section == "extra" && key == "randomlevelupskillsseed") return true;
                if (section == "language" && new[] { "configtoollanguage", "gametextmode" }.Contains(key)) return true;
                if (section == "graphics" && new[] { "renderer", "filter" }.Contains(key)) return true;
                if (section == "sounds" && new[] { "useexternalsounds", "extradefaultsound", "extrastealsound" }.Contains(key)) return true;
                if (section == "voice" && key == "enableplaylistopcode") return true;
            }
            if (path == "dxwrapper.ini") {
                if (section == "dd7to9" && new[] { "ddrawoverridewidth", "ddrawoverrideheight", "ddrawcustomwidth", "ddrawcustomheight", "ddrawusenativeresolution", "ddrawintegerscalingclamp", "ddrawmaintainaspectratio" }.Contains(key)) return true;
                if (section == "d3d9" && new[] { "initialwindowpositionleft", "initialwindowpositiontop", "setinitialwindowposition", "enablewindowmode", "windowmodeborder", "fullscreenwindowmode", "forceexclusivefullscreen", "disablemaxwindowedmode" }.Contains(key)) return true;
                if (section == "fullscreen" && new[] { "fullscreen", "forcewindowresize" }.Contains(key)) return true;
            }
            return false;
        }
        private static string CanonicalJson(object value)
        {
            if (value is IDictionary<string, object> map) return "{" + string.Join(",", map.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p =>
                GameplayModeReader.Quote(p.Key) + ":" + CanonicalJson(p.Value))) + "}";
            if (value is IEnumerable list && !(value is string)) return "[" + string.Join(",", list.Cast<object>().Select(CanonicalJson)) + "]";
            return LocalTimelineConfiguration.Json().Serialize(value);
        }
    }

    // Only pre-start background work reads settings. Once the run begins the
    // core freezes the last complete snapshot, never save-time disk contents.
    internal sealed class LocalConfigurationObserver
    {
        private readonly object sync = new object();
        private Process target;
        private long next;
        private Task<LocalTimelineConfiguration> reading;
        private readonly Func<Process, string, LocalTimelineConfiguration> read;
        private LocalTimelineConfiguration current;
        private string error = "尚未读取开局配置。";
        internal LocalConfigurationObserver(Func<Process, string, LocalTimelineConfiguration> reader = null) { read = reader ?? Read; }
        internal LocalTimelineConfiguration Current { get { lock (sync) return current; } }
        internal Task<LocalTimelineConfiguration> CaptureStart(Process process, string route)
        {
            Observe(process, route);
            lock (sync) return current != null ? Task.FromResult(current) : reading ?? Task.FromResult<LocalTimelineConfiguration>(null);
        }
        internal string Error { get { lock (sync) return error; } }
        internal void Observe(Process process, string route)
        {
            lock (sync) {
                if (!ReferenceEquals(target, process)) { target = process; current = null; reading = null; next = 0; error = "尚未读取开局配置。"; }
                long now = Stopwatch.GetTimestamp();
                if (process == null || route == null || reading != null && !reading.IsCompleted || now < next) return;
                next = now + Stopwatch.Frequency;
                reading = Task.Run(() => read(process, route));
                var observed = reading;
                observed.ContinueWith(finished => {
                    lock (sync) if (ReferenceEquals(reading, observed)) {
                        current = finished.Result;
                        error = current == null ? "开局配置暂不可读，请选择本地配置归属。" : "";
                    }
                }, TaskScheduler.Default);
            }
        }
        private static LocalTimelineConfiguration Read(Process process, string route)
        {
                    LocalTimelineConfiguration value = null; string diagnostic = "";
                    try {
                        long created = process.StartTime.ToUniversalTime().ToFileTimeUtc();
                        string exe = process.MainModule.FileName;
                        if (!Path.GetFileName(exe).Equals("PAL.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("目标不是 PAL.exe。");
                        value = LocalConfigurationReader.ReadCurrent(Path.GetDirectoryName(exe), route);
                        if (process.HasExited || created != process.StartTime.ToUniversalTime().ToFileTimeUtc()) { value = null; diagnostic = "读取配置时游戏已退出。"; }
                    } catch (Exception e) when (LocalConfigurationReader.ReadError(e)) { diagnostic = "开局配置暂不可读，请选择本地配置归属。"; }
                    return diagnostic.Length == 0 ? value : null;
        }
        internal void Reset() { lock (sync) { target = null; current = null; reading = null; next = 0; error = "尚未读取开局配置。"; } }
    }
}
