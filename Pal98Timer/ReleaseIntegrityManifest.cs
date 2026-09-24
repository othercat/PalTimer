using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Pal98Timer
{
    internal sealed class ReleaseIntegrityFile
    {
        public string path { get; set; }
        public long size { get; set; }
        public string sha256 { get; set; }
        public string module { get; set; }
        public string normalization { get; set; }
        public string[] enabled_by { get; set; }
    }
    internal sealed class ReleaseIntegritySetting
    {
        public string file { get; set; }
        public string section { get; set; }
        public string key { get; set; }
        public string value { get; set; }
        public string[] allowed_values { get; set; }
        public string default_value { get; set; }
        public long? min_value { get; set; }
        public long? max_value { get; set; }
        public bool ignore_case { get; set; }
    }
    internal sealed class ReleaseIntegrityProfile
    {
        public string content_id { get; set; }
        public string content_version { get; set; }
        public string content_sha256 { get; set; }
        public ReleaseIntegrityFile[] files { get; set; }
        public ReleaseIntegritySetting[] settings { get; set; }
    }
    internal sealed class ReleaseIntegrityGraphics
    {
        public string id { get; set; }
        public ReleaseIntegrityFile[] files { get; set; }
        public string[] absent_files { get; set; }
    }
    internal sealed class ReleaseIntegrityDirectory
    {
        public string path { get; set; }
        public string[] extensions { get; set; }
        public string[] entries { get; set; }
    }
    internal sealed class ReleaseIntegrityFixup
    {
        public int offset { get; set; }
        public string module { get; set; }
        public uint rva { get; set; }
        public string kind { get; set; }
    }
    internal sealed class ReleaseIntegrityRegion
    {
        public string id { get; set; }
        public string module { get; set; }
        public uint? rva { get; set; }
        public uint? pointer_rva { get; set; }
        public string expected { get; set; }
        public ReleaseIntegrityFixup[] fixups { get; set; }
        public int[] protection_states { get; set; }
    }
    internal sealed class ReleaseIntegrityManifest
    {
        internal const string ResourceName = "Pal98Timer.release_integrity.v1.json";
        public string schema { get; set; }
        public string release_id { get; set; }
        public string build { get; set; }
        public bool frozen { get; set; }
        public ReleaseIntegrityFile[] files { get; set; }
        public ReleaseIntegrityGraphics[] graphics_chains { get; set; }
        public ReleaseIntegrityProfile[] profiles { get; set; }
        public ReleaseIntegritySetting[] settings { get; set; }
        public ReleaseIntegrityRegion[] memory_regions { get; set; }
        public ReleaseIntegrityDirectory[] exact_directories { get; set; }

        // No external JSON lookup, override, or fallback is permitted here.
        internal static ReleaseIntegrityManifest LoadEmbedded()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (stream == null) throw new InvalidDataException("Missing embedded release manifest");
                using (var reader = new StreamReader(stream)) return Parse(reader.ReadToEnd());
            }
        }
        internal static ReleaseIntegrityManifest Parse(string json)
        {
            if (json == null || json.Length > 4 * 1024 * 1024) throw new InvalidDataException("Invalid release manifest size");
            var manifest = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Deserialize<ReleaseIntegrityManifest>(json);
            if (manifest == null || manifest.schema != "PAL98.ReleaseIntegrity.v1" || string.IsNullOrWhiteSpace(manifest.release_id) ||
                manifest.release_id.Length > 128 || manifest.files == null || manifest.profiles == null || manifest.graphics_chains == null ||
                manifest.memory_regions == null || manifest.settings == null || manifest.memory_regions.Length > 128 ||
                manifest.profiles.Length > 128 || manifest.graphics_chains.Length > 16) throw new InvalidDataException("Invalid release manifest");
            if (manifest.frozen && (string.IsNullOrWhiteSpace(manifest.build) || manifest.files.Length == 0 || manifest.profiles.Length == 0 ||
                manifest.graphics_chains.Length == 0 || manifest.memory_regions.Length == 0)) throw new InvalidDataException("Incomplete frozen manifest");
            ValidateFiles(manifest.files); ValidateSettings(manifest.settings);
            foreach (var file in manifest.files.Where(f => f.enabled_by != null))
                if (file.enabled_by.Any(path => !manifest.files.Any(m => m.path == path && m.normalization == "copymen-enabled-v1")))
                    throw new InvalidDataException("Unknown enabling module");
            var directories = manifest.exact_directories ?? new ReleaseIntegrityDirectory[0];
            if (directories.Length > 1) throw new InvalidDataException("Only the Copymen script directory can be enumerated");
            foreach (var directory in directories)
            {
                if (directory == null || directory.path != "copymen_scripts" || directory.extensions == null || directory.extensions.Length != 3 ||
                    !new HashSet<string>(directory.extensions, StringComparer.OrdinalIgnoreCase).SetEquals(new[] { ".json", ".txt", ".rule" }) ||
                    directory.entries == null || directory.entries.Length > 256 ||
                    directory.entries.Any(p => !SafePath(p) || p.Length > 128 || p.IndexOf('/') >= 0 || p.TrimEnd(' ', '.') != p ||
                        Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase) ||
                        !directory.extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)) ||
                    directory.entries.Distinct(StringComparer.OrdinalIgnoreCase).Count() != directory.entries.Length)
                    throw new InvalidDataException("Invalid exact Copymen directory contract");
            }
            var profiles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var profile in manifest.profiles)
            {
                if (profile == null || string.IsNullOrEmpty(profile.content_id) || profile.content_version == null || !Hash(profile.content_sha256) ||
                    !profiles.Add(profile.content_id + "\n" + profile.content_version + "\n" + profile.content_sha256)) throw new InvalidDataException("Invalid release profile");
                ValidateFiles(profile.files); ValidateSettings(profile.settings ?? new ReleaseIntegritySetting[0]);
            }
            var graphics = new HashSet<string>(StringComparer.Ordinal);
            foreach (var chain in manifest.graphics_chains)
            {
                if (chain == null || string.IsNullOrEmpty(chain.id) || !graphics.Add(chain.id) || chain.files == null || chain.files.Length == 0)
                    throw new InvalidDataException("Invalid graphics chain");
                ValidateFiles(chain.files);
                if (chain.absent_files != null && chain.absent_files.Any(p => !SafePath(p))) throw new InvalidDataException("Invalid absent file");
            }
            var regions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var region in manifest.memory_regions)
            {
                if (region == null || string.IsNullOrEmpty(region.id) || !regions.Add(region.id) || !ModuleName(region.module) ||
                    region.rva.HasValue == region.pointer_rva.HasValue ||
                    region.expected == null || !Regex.IsMatch(region.expected, "^(?:[0-9a-fA-F]{2}){1,4096}$") ||
                    region.protection_states != null && (region.protection_states.Length == 0 || region.protection_states.Any(v => v < 0 || v > 4)))
                    throw new InvalidDataException("Invalid memory region");
                if ((region.rva.HasValue ? (ulong)region.rva.Value + (uint)(region.expected.Length / 2) : (ulong)region.pointer_rva.Value + 4) > 0x100000000UL)
                    throw new InvalidDataException("Memory region overflows 32-bit module range");
                var occupied = new HashSet<int>();
                foreach (var fixup in region.fixups ?? new ReleaseIntegrityFixup[0])
                    if (fixup == null || !ModuleName(fixup.module) || (fixup.kind != "abs32" && fixup.kind != "rel32") ||
                        fixup.offset < 0 || (long)fixup.offset + 4 > region.expected.Length / 2 ||
                        Enumerable.Range(fixup.offset, 4).Any(i => !occupied.Add(i))) throw new InvalidDataException("Invalid memory fixup");
            }
            return manifest;
        }
        private static void ValidateFiles(ReleaseIntegrityFile[] files)
        {
            if (files == null || files.Length > 4096) throw new InvalidDataException("Invalid release files");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
                if (file == null || !SafePath(file.path) || !paths.Add(file.path) || file.size < 0 || !Hash(file.sha256) ||
                    file.module != null && !ModuleName(file.module) ||
                    file.normalization != null && (file.normalization != "copymen-enabled-v1" ||
                        !file.path.StartsWith("copymen_scripts/", StringComparison.Ordinal) || !file.path.EndsWith(".module.json", StringComparison.Ordinal) || file.size > 65536) ||
                    file.enabled_by != null && (file.normalization != null || file.enabled_by.Length == 0 || file.enabled_by.Length > 256 ||
                        file.enabled_by.Any(p => !SafePath(p)) || file.enabled_by.Distinct(StringComparer.Ordinal).Count() != file.enabled_by.Length))
                    throw new InvalidDataException("Invalid release file");
        }
        private static void ValidateSettings(ReleaseIntegritySetting[] settings)
        {
            if (settings == null || settings.Length > 4096) throw new InvalidDataException("Invalid release settings");
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var setting in settings)
                if (setting == null || !SafePath(setting.file) || string.IsNullOrWhiteSpace(setting.section) || string.IsNullOrWhiteSpace(setting.key) ||
                    (setting.value != null ? 1 : 0) + (setting.allowed_values != null ? 1 : 0) + (setting.min_value.HasValue || setting.max_value.HasValue ? 1 : 0) != 1 ||
                    (setting.min_value.HasValue || setting.max_value.HasValue) && (!setting.min_value.HasValue || !setting.max_value.HasValue || setting.min_value > setting.max_value) ||
                    setting.ignore_case && setting.allowed_values == null ||
                    setting.allowed_values != null && (setting.allowed_values.Length == 0 || setting.allowed_values.Length > 64 ||
                        setting.allowed_values.Any(v => v == null) || setting.allowed_values.Select(ReleaseIntegrityVerifier.Normalize).Distinct(setting.ignore_case ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Count() != setting.allowed_values.Length) ||
                    !keys.Add(setting.file + "\n" + setting.section + "\n" + setting.key)) throw new InvalidDataException("Invalid release setting");
        }
        internal static bool Hash(string text) { return text != null && Regex.IsMatch(text, "^[0-9a-fA-F]{64}$"); }
        // The current DLL comes from the compiled release manifest. These two
        // historical identities were read from the owner's exact release ZIPs;
        // version strings, an adjacent JSON file, and arbitrary old DLLs do not
        // qualify. This classification does not waive the other file checks.
        internal string OfficialPalDllVersion(long size, string hash)
        {
            var current = files.SingleOrDefault(f => string.Equals(f.path, "PAL.dll", StringComparison.OrdinalIgnoreCase));
            if (current != null && size == current.size && string.Equals(hash, current.sha256, StringComparison.OrdinalIgnoreCase)) return "current";
            if (size == 526336 && string.Equals(hash, "b3bc8a7b53cb92a8e7910c3b6e3176cdfeb888ca50cba79c8e26c4f8e9b634e6", StringComparison.OrdinalIgnoreCase)) return "1.14";
            if (size == 477184 && string.Equals(hash, "cb47b9e66119de098c3d4d9bc6a1fe2d9c0672d1afc2a13d8110f3a98a8ac8b0", StringComparison.OrdinalIgnoreCase)) return "1.02";
            return null;
        }
        internal static bool SafePath(string path)
        {
            return !string.IsNullOrEmpty(path) && path.Length < 1024 && !Path.IsPathRooted(path) && path.IndexOfAny(new[] { ':', '\\', '\0', '\r', '\n' }) < 0 &&
                path.Split('/').All(p => p.Length != 0 && p != "." && p != ".." && p.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
        }
        private static bool ModuleName(string name) { return SafePath(name) && name.IndexOf('/') < 0; }
        internal ReleaseIntegrityProfile FindProfile(RuntimeTimingMode mode)
        {
            return mode == null ? null : profiles.SingleOrDefault(p => p.content_id == mode.ContentId && p.content_version == mode.ContentVersion &&
                string.Equals(p.content_sha256, mode.ContentHash, StringComparison.OrdinalIgnoreCase));
        }
    }
}
