using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Pal98Timer
{
    internal sealed class CompetitionCredential
    {
        public string Hwid { get; set; }
        public string Secret { get; set; }
    }
    internal sealed class CompetitionPending
    {
        public CompetitionSettings Settings { get; set; }
        public CompetitionRun Run { get; set; }
        public string Payload { get; set; }
        public string SealedRun { get; set; }
        public bool SealAttempted { get; set; }
        public int Attempts { get; set; }
        public DateTime NextAttemptUtc { get; set; }
        public bool Rejected { get; set; }
        public bool LocalRejected { get; set; }
        public string LastStatus { get; set; }
        internal string Path;
        internal string Token;
    }
    // All methods execute on background workers; this directory is never distributed.
    internal sealed class CompetitionStorage
    {
        internal readonly string Root;
        private readonly Lazy<string> hardwareId;
        internal CompetitionStorage(string root = null, Func<string> hardwareId = null)
        {
            Root = root ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PalTimer", "Competition-v1");
            this.hardwareId = new Lazy<string>(hardwareId ?? ReadWindowsIdentity);
        }
        // Local display and credential creation share one identity. Reading it
        // neither creates credentials nor requires a server or a running game.
        internal string ReadDeviceIdentity()
        {
            string result = hardwareId.Value;
            if (string.IsNullOrWhiteSpace(result)) throw new InvalidDataException("本机设备身份暂不可读取");
            return result;
        }
        internal static string ReadWindowsIdentity()
        {
            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", false))
            {
                string guid = key == null ? "" : Convert.ToString(key.GetValue("MachineGuid"));
                Guid parsed;
                if (!Guid.TryParse(guid, out parsed)) throw new InvalidDataException("本机设备身份暂不可读取");
                // First PAL timer protocol HWID. No hardware inventory, host name or path leaves the PC.
                return "win-" + CompetitionProtocol.Hash("PAL98.TimerDevice.v1\n" + parsed.ToString("D"));
            }
        }
        internal CompetitionSettings LoadSettings()
        {
            string path = System.IO.Path.Combine(Root, "settings.json");
            if (!File.Exists(path)) return new CompetitionSettings();
            var result = CompetitionProtocol.Json().Deserialize<CompetitionSettings>(ReadBounded(path, 16384));
            if (result == null || result.Validate().Length != 0) throw new InvalidDataException("比赛设置损坏，联机保持关闭");
            if (result.ValidateAppearance().Length != 0) result.CopyAppearance(new CompetitionSettings());
            return result;
        }
        internal void SaveSettings(CompetitionSettings settings) { WriteAtomic(System.IO.Path.Combine(Root, "settings.json"), CompetitionProtocol.Json().Serialize(settings)); }
        internal CompetitionCredential Credential(string server, string replacement = null)
        {
            Directory.CreateDirectory(Root);
            string key = CompetitionProtocol.Hash(server);
            string path = System.IO.Path.Combine(Root, key + ".device");
            using (var gate = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                CompetitionCredential result;
                if (File.Exists(path))
                {
                    if (new FileInfo(path).Length > 16384) throw new InvalidDataException("本机比赛凭据损坏");
                    result = CompetitionProtocol.Json().Deserialize<CompetitionCredential>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Encoding.UTF8.GetBytes(server), DataProtectionScope.CurrentUser)));
                }
                else
                {
                    byte[] bytes = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
                    result = new CompetitionCredential { Hwid = ReadDeviceIdentity(), Secret = CompetitionProtocol.Hex(bytes) };
                }
                if (replacement != null && result != null) result.Secret = replacement;
                if (result == null || !CompetitionProtocol.Digest(result.Secret) || string.IsNullOrEmpty(result.Hwid)) throw new InvalidDataException("本机比赛凭据损坏");
                if (!File.Exists(path) || replacement != null)
                    WriteAtomicBytes(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(CompetitionProtocol.Json().Serialize(result)), Encoding.UTF8.GetBytes(server), DataProtectionScope.CurrentUser));
                return result;
            }
        }
        internal void SavePending(CompetitionPending pending)
        {
            if (pending.Path == null) pending.Path = System.IO.Path.Combine(Root, "outbox", CompetitionProtocol.Hash(pending.Settings.Server + "|" + CompetitionProtocol.Scope).Substring(0, 24), pending.Run.run_id + ".json");
            WriteAtomic(pending.Path, CompetitionProtocol.Json().Serialize(pending));
        }
        internal IEnumerable<CompetitionPending> LoadPending(CompetitionSettings settings)
        {
            string directory = System.IO.Path.Combine(Root, "outbox", CompetitionProtocol.Hash(settings.Server + "|" + CompetitionProtocol.Scope).Substring(0, 24));
            if (!Directory.Exists(directory)) yield break;
            foreach (string path in Directory.EnumerateFiles(directory, "*.json").Take(512))
            {
                CompetitionPending pending = null;
                try
                {
                    pending = CompetitionProtocol.Json().Deserialize<CompetitionPending>(ReadBounded(path, 262144));
                    if (pending == null || pending.Settings == null || pending.Settings.Validate().Length != 0 ||
                        pending.Settings.Server != settings.Server || pending.Run == null || pending.Run.protocol != CompetitionProtocol.Online ||
                        pending.Run.run_id + ".json" != System.IO.Path.GetFileName(path) ||
                        CompetitionProtocol.SerializeRun(CompetitionProtocol.Json().Deserialize<CompetitionRun>(pending.Payload)) != CompetitionProtocol.SerializeRun(pending.Run)) pending = null;
                    if (pending != null) pending.Path = path;
                }
                catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is InvalidOperationException) { }
                if (pending != null) yield return pending;
            }
        }
        internal void Receipt(CompetitionPending pending, CompetitionReply reply)
        {
            // Retain local immutable result and receipt; no game/tool configuration is replaced.
            string directory = System.IO.Path.Combine(Root, "receipts", CompetitionProtocol.Hash(pending.Settings.Server + "|" + CompetitionProtocol.Scope).Substring(0, 24));
            WriteAtomic(System.IO.Path.Combine(directory, pending.Run.run_id + ".json"), CompetitionProtocol.Json().Serialize(new { result = pending, receipt = reply }));
            if (File.Exists(pending.Path)) File.Delete(pending.Path);
        }
        internal void LogNetwork(string operation, int status, int attempts = 0)
        {
            // Background only. Never log credentials, HWID, body, URL or exception text.
            try
            {
                Directory.CreateDirectory(Root);
                string path = System.IO.Path.Combine(Root, "network.log");
                if (File.Exists(path) && new FileInfo(path).Length > 524288)
                {
                    string previous = path + ".previous";
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(path, previous);
                }
                File.AppendAllText(path, DateTimeOffset.UtcNow.ToString("o") + " " + operation + " status=" + status + " attempts=" + attempts + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { } // Diagnostic IO cannot change local timing or network retry state.
        }
        internal static string ReadBounded(string path, int max)
        {
            if (new FileInfo(path).Length > max) throw new InvalidDataException("本机比赛记录超过容量");
            return File.ReadAllText(path, Encoding.UTF8);
        }
        internal static void WriteAtomic(string path, string text) { WriteAtomicBytes(path, new UTF8Encoding(false).GetBytes(text)); }
        private static void WriteAtomicBytes(string path, byte[] data)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(data, 0, data.Length); file.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".previous", true);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
