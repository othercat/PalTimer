using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Pal98Timer
{
    // Optional extension; existing native authentication and old approved builds
    // work without a registration file. This file contains PUBLIC material only.
    internal interface ICompetitionBuildRegistration
    {
        CompetitionBuildRegistration Registration();
    }

    internal sealed class CompetitionBuildDescriptor
    {
        public string protocol { get; set; }
        public string key_id { get; set; }
        public string exe_sha256 { get; set; }
        public string timer_version { get; set; }
        public string component_sha256 { get; set; }
        public string public_key_pem { get; set; }
        public string online_component_sha256 { get; set; }
        public string online_component_version { get; set; }
        public int? online_api_version { get; set; }
    }

    internal sealed class CompetitionBuildRegistration
    {
        internal const string Protocol = "PAL98.TimerBuildCandidate.v2";
        internal const string LegacyProtocol = "PAL98.TimerBuildCandidate.v1";
        internal const string FileName = "PalCompetitionRegistration.public.json";
        public string protocol { get; set; }
        public string publisher_id { get; set; }
        public CompetitionBuildDescriptor build { get; set; }
        public string signature_base64 { get; set; }

        internal bool Matches(CompetitionAuthIdentity identity)
        {
            if (identity == null || !identity.Valid || (protocol != Protocol && protocol != LegacyProtocol) || !CompetitionProtocol.Digest(publisher_id) ||
                build == null || build.protocol != CompetitionAuthProtocol.Name ||
                build.key_id != identity.key_id || build.exe_sha256 != identity.timer_exe_sha256 ||
                build.timer_version != identity.timer_version || build.component_sha256 != identity.component_sha256 ||
                build.public_key_pem == null || build.public_key_pem.Length > 4096 ||
                !build.public_key_pem.StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal) ||
                signature_base64 == null || !Regex.IsMatch(signature_base64, "^[A-Za-z0-9+/]{342}==$")) return false;
            if (protocol == Protocol && (!CompetitionProtocol.Digest(build.online_component_sha256) ||
                build.online_component_version == null || !Version.TryParse(build.online_component_version, out var version) ||
                version.Revision < 0 || build.online_api_version != 1)) return false;
            try { return Convert.FromBase64String(signature_base64).Length == 256; }
            catch { return false; }
        }

        internal static CompetitionBuildRegistration Load(string directory, CompetitionAuthIdentity identity)
        {
            // Called once by the background-owned native adapter. Bound the SAME
            // open file read; never reread the EXE or walk resource directories.
            try
            {
                using (var stream = new FileStream(Path.Combine(directory, FileName), FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length <= 0 || stream.Length > 16384) return null;
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
                    {
                        var candidate = CompetitionProtocol.Json().Deserialize<CompetitionBuildRegistration>(reader.ReadToEnd());
                        return candidate != null && candidate.Matches(identity) ? candidate : null;
                    }
                }
            }
            catch { return null; }
        }
    }

    internal sealed class CompetitionBuildReceipt
    {
        public string protocol { get; set; }
        public string server_origin { get; set; }
        public string key_id { get; set; }
        public string timer_exe_sha256 { get; set; }
        public string status { get; set; }
    }
}
