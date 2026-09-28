using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Pal98Timer
{
    internal sealed class CompetitionAuthIdentity
    {
        public string protocol { get; set; }
        public string key_id { get; set; }
        public string timer_exe_sha256 { get; set; }
        public string timer_version { get; set; }
        public string component_sha256 { get; set; }
        internal bool Valid { get { return protocol == CompetitionAuthProtocol.Name && key_id != null &&
            System.Text.RegularExpressions.Regex.IsMatch(key_id, "^[0-9a-f]{32}$") &&
            CompetitionProtocol.Digest(timer_exe_sha256) && CompetitionProtocol.Digest(component_sha256); } }
    }
    internal sealed class CompetitionAuthSeal
    {
        public string protocol { get; set; }
        public string key_id { get; set; }
        public string timer_exe_sha256 { get; set; }
        public string timer_version { get; set; }
        public string server_origin { get; set; }
        public string scope { get; set; }
        public string hwid { get; set; }
        public string run_id { get; set; }
        public string payload_base64 { get; set; }
    }
    internal sealed class CompetitionAuthStatus
    {
        public string protocol { get; set; }
        public string scope { get; set; }
        public string server_origin { get; set; }
        public string timer_exe_sha256 { get; set; }
        public string key_id { get; set; }
        public bool? approved { get; set; }
    }
    internal interface ICompetitionAuth
    {
        CompetitionAuthIdentity Identity();
        string Seal(string origin, string eventId, string payload);
        string Prove(string sealedJson, string challengeJson);
    }
    internal static class CompetitionAuthProtocol
    {
        internal const string Name = "PAL98.TimerUploadAuth.v2";
        internal static string Encode(string value) { return Convert.ToBase64String(new UTF8Encoding(false, true).GetBytes(value)); }
        internal static string Decode(string value)
        {
            if (value == null || value.Length > 131072) throw new InvalidDataException("签封容量无效");
            var bytes = Convert.FromBase64String(value);
            if (Convert.ToBase64String(bytes) != value || bytes.Length > 98304) throw new InvalidDataException("签封编码无效");
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        internal static CompetitionAuthSeal ReadSeal(string value, CompetitionSettings settings, CompetitionRun run, string payload)
        {
            var seal = CompetitionProtocol.Json().Deserialize<CompetitionAuthSeal>(Decode(value));
            if (seal == null || seal.protocol != Name || seal.key_id == null || !System.Text.RegularExpressions.Regex.IsMatch(seal.key_id, "^[0-9a-f]{32}$") ||
                !CompetitionProtocol.Digest(seal.timer_exe_sha256) || seal.server_origin != settings.Server || seal.scope != CompetitionProtocol.Scope ||
                seal.hwid != run.hwid || seal.run_id != run.run_id || seal.timer_version != run.timer_version || Decode(seal.payload_base64) != payload)
                throw new InvalidDataException("签封与原始成绩不一致");
            return seal;
        }
    }
    // Native component owns the private release material and checks the actual
    // host process. This public adapter contains no key or activation fallback.
    // Every method is called exclusively on the competition background worker.
    internal sealed class NativeCompetitionAuth : ICompetitionAuth
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string name, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint IdentityFn([Out] byte[] output, uint capacity);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint SealFn(byte[] origin, byte[] eventId, byte[] payload, uint count, [Out] byte[] output, uint capacity);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ProveFn(byte[] sealedRun, uint sealCount, byte[] challenge, uint challengeCount, [Out] byte[] output, uint capacity);
        private IntPtr module;
        private bool attempted;
        private IdentityFn getIdentity;
        private SealFn seal;
        private ProveFn prove;
        private CompetitionAuthIdentity identity;
        private static byte[] Utf8(string text) { return new UTF8Encoding(false, true).GetBytes(text); }
        private static string Result(uint code, byte[] buffer)
        {
            if (code != 0) return null;
            int count = Array.IndexOf(buffer, (byte)0);
            if (count <= 0) return null;
            return new UTF8Encoding(false, true).GetString(buffer, 0, count);
        }
        public CompetitionAuthIdentity Identity()
        {
            if (attempted) return identity;
            attempted = true;
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PalCompetitionAuth.dll");
                // Restricted dependency search where supported. Win7 without the
                // loader update still uses an absolute DLL path; the component has
                // a static CRT and only system-library dependencies.
                module = LoadLibraryEx(path, IntPtr.Zero, 0x100 | 0x800);
                if (module == IntPtr.Zero && Marshal.GetLastWin32Error() == 87) module = LoadLibraryEx(path, IntPtr.Zero, 8);
                if (module == IntPtr.Zero) return null;
                getIdentity = (IdentityFn)Marshal.GetDelegateForFunctionPointer(GetProcAddress(module, "PCA_GetIdentity"), typeof(IdentityFn));
                seal = (SealFn)Marshal.GetDelegateForFunctionPointer(GetProcAddress(module, "PCA_SealRun"), typeof(SealFn));
                prove = (ProveFn)Marshal.GetDelegateForFunctionPointer(GetProcAddress(module, "PCA_ProveUpload"), typeof(ProveFn));
                var output = new byte[4096]; string value = Result(getIdentity(output, (uint)output.Length), output);
                if (value == null) return null;
                var candidate = CompetitionProtocol.Json().Deserialize<CompetitionAuthIdentity>(value);
                if (candidate != null && candidate.Valid && candidate.timer_version == typeof(CompetitionClient).Assembly.GetName().Version.ToString(4)) identity = candidate;
            }
            catch { identity = null; }
            return identity;
        }
        public string Seal(string origin, string eventId, string payload)
        {
            if (Identity() == null) return null;
            try {
                var bytes = Utf8(payload); var output = new byte[98305];
                return Result(seal(Utf8(origin + "\0"), Utf8(eventId + "\0"), bytes, (uint)bytes.Length, output, (uint)output.Length), output);
            } catch { return null; }
        }
        public string Prove(string sealedJson, string challengeJson)
        {
            if (Identity() == null) return null;
            try {
                var bytes = Utf8(sealedJson); var challenge = Utf8(challengeJson); var output = new byte[8192];
                return Result(prove(bytes, (uint)bytes.Length, challenge, (uint)challenge.Length, output, (uint)output.Length), output);
            } catch { return null; }
        }
    }
}
