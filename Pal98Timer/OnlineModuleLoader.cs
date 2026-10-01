using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Pal98Timer
{
    internal sealed class OnlineModuleLease : IDisposable
    {
        internal IPalTimerOnlineV1 Module;
        internal readonly List<FileStream> Files = new List<FileStream>();
        public void Dispose() { try { Module?.Dispose(); } finally { foreach (var file in Files) file.Dispose(); Files.Clear(); } }
    }
    internal static class OnlineModuleLoader
    {
        internal const int ApiVersion = 1;
        private const string PublisherId = "7e251fc559f27aa7e7493b461025d4302a75d6abb2040dfdd0cdd7aebc88e08e";
        private const string PublisherXml = "<RSAKeyValue><Modulus>zWNXxoHhTv8tL2Wkhw8n42s0jv2J6W6SrSvp7UO0UB7XBuTmQtZW2lp4uaJs06pA1g1oZLDkDIffDK6xbpVNmIDbhkDWNndWyB4CBYoQRjHC77QPl6EXqLesjccN9g2LwRaQg8coJKHIAYjcdps/ghqM0HgvLq6DoWV+Azxrbv3NPpwKZXEpY6+Qvtmd+LMMrMj8ZjWC/L9iHmAWpSFdhT7Np5y7Sd3upzXP4CxMLE4Y2Lpopx8MWNY5O3Pj0xs54/e3GQ3O39e3GZssFkRDXuvumkH6WOjs30MKBLxSrJZP0h8rWrNvbQVPukyPa/n6oI40qcK5wOnI4butM5QPxQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
        private sealed class Registration
        {
            public string protocol { get; set; }
            public string publisher_id { get; set; }
            public Build build { get; set; }
            public string signature_base64 { get; set; }
        }
        private sealed class Build
        {
            public string protocol { get; set; }
            public string key_id { get; set; }
            public string exe_sha256 { get; set; }
            public string timer_version { get; set; }
            public string component_sha256 { get; set; }
            public string public_key_pem { get; set; }
            public string online_component_sha256 { get; set; }
            public string online_component_version { get; set; }
            public int online_api_version { get; set; }
        }
        internal static OnlineModuleLease Load(string directory)
        {
            var lease = new OnlineModuleLease();
            try {
                string root = Path.GetFullPath(directory);
                var regFile = new FileStream(Path.Combine(root, "PalCompetitionRegistration.public.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
                lease.Files.Add(regFile);
                if (regFile.Length > 32768) throw new InvalidDataException("Registration too large.");
                Registration r;
                using (var reader = new StreamReader(regFile, new UTF8Encoding(false, true), true, 1024, true))
                    r = new JavaScriptSerializer { MaxJsonLength = 32768, RecursionLimit = 12 }.Deserialize<Registration>(reader.ReadToEnd());
                var b = r?.build; Version timer, onlineVersion;
                if (r == null || r.protocol != "PAL98.TimerBuildCandidate.v2" || r.publisher_id != PublisherId || b == null ||
                    b.protocol != "PAL98.TimerUploadAuth.v2" || b.online_api_version != ApiVersion ||
                    !Regex.IsMatch(b.key_id ?? "", "\\A[0-9a-f]{32}\\z") || !CompetitionProtocol.Digest(b.exe_sha256) ||
                    !CompetitionProtocol.Digest(b.component_sha256) || !CompetitionProtocol.Digest(b.online_component_sha256) ||
                    b.public_key_pem == null || b.public_key_pem.Length > 4096 ||
                    !Version.TryParse(b.timer_version, out timer) || timer.Revision < 0 ||
                    !Version.TryParse(b.online_component_version, out onlineVersion) || onlineVersion.Revision < 0)
                    throw new InvalidDataException("Registration identity is invalid.");
                string pem = b.public_key_pem.Replace("\r\n", "\n").Trim() + "\n";
                string signed = string.Join("\n", r.protocol, b.protocol, r.publisher_id, b.key_id, b.exe_sha256,
                    b.timer_version, b.component_sha256, CompetitionProtocol.Hash(pem), b.online_component_sha256,
                    b.online_component_version, b.online_api_version.ToString(CultureInfo.InvariantCulture)) + "\n";
                using (var rsa = new RSACryptoServiceProvider()) {
                    rsa.PersistKeyInCsp = false; rsa.FromXmlString(PublisherXml);
                    if (!rsa.VerifyData(Encoding.ASCII.GetBytes(signed), CryptoConfig.MapNameToOID("SHA256"), Convert.FromBase64String(r.signature_base64)))
                        throw new InvalidDataException("Publisher signature failed.");
                }
                string exe = typeof(OnlineModuleLoader).Assembly.Location;
                if (!string.Equals(Path.GetDirectoryName(exe), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
                    typeof(OnlineModuleLoader).Assembly.GetName().Version.ToString(4) != b.timer_version)
                    throw new InvalidDataException("Host identity failed.");
                VerifyFile(lease, exe, b.exe_sha256);
                VerifyFile(lease, Path.Combine(root, "PalCompetitionAuth.dll"), b.component_sha256);
                string online = Path.Combine(root, "PalTimerOnline.dll");
                VerifyFile(lease, online, b.online_component_sha256);
                var assembly = Assembly.LoadFrom(online);
                if (assembly.GetName().Name != "PalTimerOnline" || assembly.GetName().Version.ToString(4) != b.online_component_version)
                    throw new InvalidDataException("Online component version failed.");
                var factory = assembly.GetType("Pal98Timer.OnlineEntryPoint", true).GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
                lease.Module = factory?.Invoke(null, new object[] { ApiVersion, b.timer_version }) as IPalTimerOnlineV1;
                if (lease.Module == null || lease.Module.ApiVersion != ApiVersion) throw new InvalidDataException("Online API unavailable.");
                return lease;
            } catch { lease.Dispose(); throw; }
        }
        private static void VerifyFile(OnlineModuleLease lease, string path, string expected)
        {
            // Hold read-only sharing until shutdown to prevent replacement
            // between verification and managed/native loading. No hot updates.
            var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            lease.Files.Add(file);
            using (var sha = SHA256.Create())
                if (CompetitionProtocol.Hex(sha.ComputeHash(file)) != expected) throw new InvalidDataException("Online component file mismatch.");
        }
    }
}
