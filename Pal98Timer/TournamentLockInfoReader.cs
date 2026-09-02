using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Pal98Timer
{
    internal enum TournamentLockReadState
    {
        Unlocked,
        Locked,
        Invalid
    }

    internal sealed class TournamentLockInfo
    {
        public TournamentLockReadState State { get; set; }
        public string CompetitionDisplayName { get; set; }
        public string Diagnostic { get; set; }
    }

    internal sealed class TournamentTimerLockedFile
    {
        public string name { get; set; }
        public string snapshot { get; set; }
        public long size { get; set; }
        public string sha256 { get; set; }
    }

    internal sealed class TournamentTimerManifest
    {
        public string schema { get; set; }
        public int version { get; set; }
        public bool locked { get; set; }
        public string locker_name { get; set; }
        public string competition_name { get; set; }
        public string competition_display_name { get; set; }
        public string[] display_lines { get; set; }
        public bool[] display_line_overrides { get; set; }
        public string configuration_code_marker { get; set; }
        public string locked_footer_line { get; set; }
        public TournamentTimerLockedFile[] files { get; set; }
    }

    internal static class TournamentLockInfoReader
    {
        private const string Schema = "PAL98.TournamentLock.v1";
        private const string IntegrityKeyResourceName =
            "Pal98Timer.TournamentIntegrityKey.txt";
        private const string RelativeDirectory = "palmod\\TournamentLock\\v1";
        private const int MaximumManifestBytes = 128 * 1024;
        private const int MaximumSnapshotBytes = 4 * 1024 * 1024;
        private static readonly Regex LockerName = new Regex(
            "\\A[A-Za-z0-9\\u3400-\\u9FFF]{1,4}\\z",
            RegexOptions.CultureInvariant);
        private static readonly string[] ExpectedFiles =
        {
            "config.ini",
            "mod.ini",
            "dxwrapper.ini"
        };

        public static TournamentLockInfo LoadForProcessExecutable(string executablePath)
        {
            string directory = string.IsNullOrWhiteSpace(executablePath)
                ? null
                : Path.GetDirectoryName(Path.GetFullPath(executablePath));
            return Load(directory);
        }

        public static TournamentLockInfo Load(string gameDirectory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(gameDirectory))
                    return Invalid("game directory is empty");
                string active = Path.Combine(gameDirectory, RelativeDirectory);
                string manifestPath = Path.Combine(active, "manifest.json");
                string signaturePath = Path.Combine(active, "manifest.sig");
                bool activeDirectoryExists = Directory.Exists(active);
                bool activeExists = activeDirectoryExists || File.Exists(active);
                bool manifestExists = File.Exists(manifestPath);
                bool signatureExists = File.Exists(signaturePath);
                if (!activeExists && !manifestExists && !signatureExists)
                {
                    return new TournamentLockInfo
                    {
                        State = TournamentLockReadState.Unlocked,
                        CompetitionDisplayName = string.Empty,
                        Diagnostic = "unlocked"
                    };
                }
                if (!activeDirectoryExists || !manifestExists || !signatureExists)
                    return Invalid("tournament lock files are incomplete");
                string snapshots = Path.Combine(active, "snapshots");
                if (IsReparsePoint(active) || IsReparsePoint(snapshots) ||
                    IsReparsePoint(manifestPath) || IsReparsePoint(signaturePath))
                    return Invalid("tournament lock reparse point is forbidden");

                byte[] bytes = ReadBounded(manifestPath, MaximumManifestBytes);
                string expected = Encoding.ASCII.GetString(ReadBounded(signaturePath, 256)).Trim();
                string actual;
                using (var hmac = new HMACSHA256(Encoding.ASCII.GetBytes(GetIntegrityKey())))
                {
                    actual = ToHex(hmac.ComputeHash(bytes));
                }
                if (!Regex.IsMatch(expected ?? string.Empty, "^[0-9a-f]{64}$") ||
                    !FixedTimeEquals(expected, actual))
                    return Invalid("tournament lock signature mismatch");

                var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumManifestBytes };
                TournamentTimerManifest manifest = serializer.Deserialize<TournamentTimerManifest>(
                    new UTF8Encoding(false, true).GetString(bytes));
                string error;
                if (!Validate(active, manifest, out error)) return Invalid(error);
                return new TournamentLockInfo
                {
                    State = TournamentLockReadState.Locked,
                    CompetitionDisplayName = manifest.competition_display_name,
                    Diagnostic = "locked"
                };
            }
            catch (Exception ex)
            {
                return Invalid(ex.Message);
            }
        }

        private static string GetIntegrityKey()
        {
            using (Stream stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(IntegrityKeyResourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException(
                        "trusted tournament integrity key is unavailable");
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 256, false))
                {
                    string value = (reader.ReadToEnd() ?? string.Empty).Trim();
                    if (value.Length < 32)
                        throw new InvalidOperationException(
                            "trusted tournament integrity key is invalid");
                    return value;
                }
            }
        }

        private static bool Validate(
            string activeDirectory,
            TournamentTimerManifest manifest,
            out string error)
        {
            error = string.Empty;
            if (manifest == null || manifest.schema != Schema || manifest.version != 1 || !manifest.locked)
            {
                error = "tournament lock schema is invalid";
                return false;
            }
            string competition = (manifest.competition_name ?? string.Empty).Trim();
            string expectedCompetitionDisplay = competition.EndsWith("比赛专用", StringComparison.Ordinal)
                ? competition
                : competition + "比赛专用";
            bool legacyDisplayContract =
                manifest.display_line_overrides == null &&
                manifest.configuration_code_marker == null;
            bool currentDisplayContract =
                manifest.display_line_overrides != null &&
                manifest.display_line_overrides.Length == 4 &&
                Regex.IsMatch(
                    manifest.configuration_code_marker ?? string.Empty,
                    "\\A[A-Za-z0-9_-]{6}\\z");
            string expectedFooter = legacyDisplayContract
                ? "本次游戏内容不可更改 锁定者 " + manifest.locker_name
                : currentDisplayContract
                    ? "锁定者" + manifest.locker_name + " : " +
                        manifest.configuration_code_marker
                    : string.Empty;
            if (!LockerName.IsMatch(manifest.locker_name ?? string.Empty) ||
                !string.Equals(manifest.competition_name, competition, StringComparison.Ordinal) ||
                competition.Length == 0 || competition.Length > 20 ||
                competition.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0 ||
                !string.Equals(
                    manifest.competition_display_name,
                    expectedCompetitionDisplay,
                    StringComparison.Ordinal) ||
                manifest.display_lines == null || manifest.display_lines.Length != 4 ||
                manifest.display_lines.Any(line => line == null || line.Length > 80 ||
                    line.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0) ||
                (!legacyDisplayContract && !currentDisplayContract) ||
                !string.Equals(
                    manifest.locked_footer_line,
                    expectedFooter,
                    StringComparison.Ordinal))
            {
                error = "tournament lock display identity is invalid";
                return false;
            }
            if (manifest.files == null || manifest.files.Length != ExpectedFiles.Length)
            {
                error = "tournament lock file set is invalid";
                return false;
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string activeFull = Path.GetFullPath(activeDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            foreach (TournamentTimerLockedFile file in manifest.files)
            {
                if (file == null ||
                    !ExpectedFiles.Contains(file.name, StringComparer.OrdinalIgnoreCase) ||
                    !seen.Add(file.name) ||
                    !string.Equals(
                        file.snapshot,
                        "snapshots/" + file.name,
                        StringComparison.OrdinalIgnoreCase) ||
                    file.size < 0 || file.size > MaximumSnapshotBytes ||
                    !Regex.IsMatch(file.sha256 ?? string.Empty, "^[0-9a-f]{64}$"))
                {
                    error = "tournament lock file identity is invalid";
                    return false;
                }
                string relative = file.snapshot.Replace('/', Path.DirectorySeparatorChar);
                string snapshot = Path.GetFullPath(Path.Combine(activeDirectory, relative));
                if (!snapshot.StartsWith(activeFull, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(snapshot) || IsReparsePoint(snapshot))
                {
                    error = "tournament lock snapshot is missing or unsafe";
                    return false;
                }
                var snapshotInfo = new FileInfo(snapshot);
                if (snapshotInfo.Length != file.size || snapshotInfo.Length > MaximumSnapshotBytes)
                {
                    error = "tournament lock snapshot size mismatch";
                    return false;
                }
                byte[] snapshotBytes = File.ReadAllBytes(snapshot);
                if (snapshotBytes.LongLength != file.size ||
                    !string.Equals(Sha256(snapshotBytes), file.sha256, StringComparison.Ordinal))
                {
                    error = "tournament lock snapshot hash mismatch";
                    return false;
                }
            }
            return seen.Count == ExpectedFiles.Length;
        }

        private static byte[] ReadBounded(string path, int maximumBytes)
        {
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > maximumBytes)
                throw new InvalidDataException("tournament lock manifest size is invalid");
            return File.ReadAllBytes(path);
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.Exists(path) || Directory.Exists(path)) &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return ToHex(sha.ComputeHash(bytes));
        }

        private static TournamentLockInfo Invalid(string diagnostic)
        {
            return new TournamentLockInfo
            {
                State = TournamentLockReadState.Invalid,
                CompetitionDisplayName = string.Empty,
                Diagnostic = diagnostic ?? "invalid"
            };
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) builder.Append(value.ToString("x2"));
            return builder.ToString();
        }
    }
}
