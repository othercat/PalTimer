using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace Pal98Timer
{
    internal enum HardcoreState { Off, Waiting, Active, Disconnected, Rejected, Unfocused }
    internal enum HardcoreKeyboardTransport { Usb = 0, Ps2 = 2, Any = 3 }

    // Immutable facts from PAL98.HardcoreMode.v1, never from config.ini or a best file.
    internal sealed class HardcoreSnapshot
    {
        internal readonly uint Pid, ProducerVersion, RulesVersion, BlacklistVersion, Flags, Reason;
        internal readonly uint DisconnectCount, ReconnectCount;
        internal readonly long ProcessCreation;
        internal readonly ulong LastChangeQpc;
        internal readonly ushort VendorId, ProductId;
        internal readonly HardcoreState State;
        internal readonly HardcoreKeyboardTransport KeyboardTransport;
        internal readonly string ConfigurationHash, BindingHash, DeviceName, ReasonText;
        internal bool Requested { get { return (Flags & 1) != 0; } }
        // The wire contract and the gameplay rule revision are independent.
        // Unknown rules still carry a request that must disable key changing,
        // but cannot certify a run until this consumer supports those rules.
        internal bool AnyKeyboard => KeyboardTransport == HardcoreKeyboardTransport.Any;
        internal bool RulesSupported => AnyKeyboard
            ? RulesVersion == 5 && BlacklistVersion == 0 && ProducerVersion >= HardcoreModeReader.NostalgiaProducerVersion
            : RulesVersion >= 1 && RulesVersion <= 4 && BlacklistVersion == 1;

        internal HardcoreSnapshot(byte[] bytes, string config, string binding, string name, string reason)
        {
            Pid = BitConverter.ToUInt32(bytes, 8); ProducerVersion = BitConverter.ToUInt32(bytes, 12);
            ProcessCreation = BitConverter.ToInt64(bytes, 16); State = (HardcoreState)BitConverter.ToUInt32(bytes, 28);
            RulesVersion = BitConverter.ToUInt32(bytes, 32); BlacklistVersion = BitConverter.ToUInt32(bytes, 36);
            Flags = BitConverter.ToUInt32(bytes, 40); Reason = BitConverter.ToUInt32(bytes, 44);
            DisconnectCount = BitConverter.ToUInt32(bytes, 48); ReconnectCount = BitConverter.ToUInt32(bytes, 52);
            LastChangeQpc = BitConverter.ToUInt64(bytes, 56);
            VendorId = BitConverter.ToUInt16(bytes, 64); ProductId = BitConverter.ToUInt16(bytes, 66);
            KeyboardTransport = (HardcoreKeyboardTransport)BitConverter.ToUInt32(bytes, 68);
            ConfigurationHash = config; BindingHash = binding; DeviceName = name; ReasonText = reason;
        }

        internal bool SameRun(HardcoreSnapshot other)
        {
            return other != null && Pid == other.Pid && ProcessCreation == other.ProcessCreation &&
                SamePolicy(other);
        }
        internal bool SamePolicy(HardcoreSnapshot other)
        {
            return other != null && ProducerVersion == other.ProducerVersion && RulesVersion == other.RulesVersion &&
                BlacklistVersion == other.BlacklistVersion && ConfigurationHash == other.ConfigurationHash &&
                BindingHash == other.BindingHash && VendorId == other.VendorId && ProductId == other.ProductId &&
                KeyboardTransport == other.KeyboardTransport;
        }

        internal string StateLabel
        {
            get
            {
                string mode = AnyKeyboard ? "怀旧" : "硬核";
                if (Requested && !RulesSupported) return mode + "规则未支持，请更新计时器";
                if (Reason == 12) return mode + "工具版本不合格";
                if (Reason == 13) return mode + "正在检查工具版本";
                switch (State)
                {
                    case HardcoreState.Off: return "普通模式";
                    case HardcoreState.Waiting: return Reason == 11 ? mode + "待本地按键确认" : mode + "等待键盘";
                    case HardcoreState.Active: return mode + "生效";
                    case HardcoreState.Disconnected: return mode + "键盘断开";
                    case HardcoreState.Rejected: return mode + "已拒绝";
                    default: return mode + "失焦";
                }
            }
        }

        internal string DeviceLabel
        {
            get
            {
                if (!Requested || AnyKeyboard) return "";
                string name = DeviceName.Length == 0 ? "键盘未知" : SingleLine(DeviceName);
                if (KeyboardTransport == HardcoreKeyboardTransport.Ps2) return "PS/2 " + name;
                return "VID:" + VendorId.ToString("X4") + " PID:" + ProductId.ToString("X4") + " " + name;
            }
        }

        internal static string SingleLine(string text)
        {
            var result = new StringBuilder(text.Length);
            foreach (char value in text) result.Append(char.IsControl(value) ? ' ' : value);
            return result.ToString();
        }
    }

    internal interface IHardcoreModeReader
    {
        HardcoreSnapshot Read(Process process);
    }

    internal sealed class HardcoreModeReader : IHardcoreModeReader
    {
        internal const int SnapshotSize = 1024;
        internal const uint NostalgiaProducerVersion = 0x01070203u;
        internal const uint LocalConfirmationProducerVersion = 0x01060701u;
        internal const string MappingPrefix = "Local\\PAL98.HardcoreMode.v1.";

        internal static bool HasPublishedSnapshot(int pid)
        {
            try
            {
                using (MemoryMappedFile.OpenExisting(MappingPrefix + pid, MemoryMappedFileRights.Read)) return true;
            }
            catch (FileNotFoundException) { return false; }
            // A published but unreadable/incompatible state must not reopen remapping.
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }

        public HardcoreSnapshot Read(Process process)
        {
            if (process == null) return null;
            try
            {
                if (process.HasExited) return null;
                int pid = process.Id;
                long creation = process.StartTime.ToUniversalTime().ToFileTimeUtc();
                using (var mapping = MemoryMappedFile.OpenExisting(MappingPrefix + pid, MemoryMappedFileRights.Read))
                using (var view = mapping.CreateViewAccessor(0, SnapshotSize, MemoryMappedFileAccess.Read))
                {
                    // This state changes on disconnect/focus. Never cache a former Active snapshot.
                    for (int attempt = 0; attempt != 3; ++attempt)
                    {
                        int before = view.ReadInt32(24);
                        if ((before & 1) != 0) continue;
                        Thread.MemoryBarrier();
                        var bytes = new byte[SnapshotSize];
                        if (view.ReadArray(0, bytes, 0, bytes.Length) != bytes.Length) return null;
                        Thread.MemoryBarrier();
                        int after = view.ReadInt32(24);
                        var snapshot = Decode(bytes, pid, creation, before, after);
                        if (snapshot != null) return process.HasExited ? null : snapshot;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception || ex is ArgumentException)
            { }
            return null;
        }

        internal static HardcoreSnapshot Decode(byte[] bytes, int pid, long creation, int before, int after)
        {
            if (bytes == null || bytes.Length != SnapshotSize || pid <= 0 || creation <= 0 ||
                before != after || (before & 1) != 0 || BitConverter.ToInt32(bytes, 24) != before ||
                BitConverter.ToUInt32(bytes, 0) != 0x31484350u || BitConverter.ToUInt16(bytes, 4) != 1 ||
                BitConverter.ToUInt16(bytes, 6) != SnapshotSize || BitConverter.ToUInt32(bytes, 8) != (uint)pid ||
                BitConverter.ToUInt32(bytes, 12) < 0x01060700u || BitConverter.ToInt64(bytes, 16) != creation ||
                BitConverter.ToUInt32(bytes, 32) == 0) return null;
            for (int i = 714; i < SnapshotSize; ++i) if (bytes[i] != 0) return null;
            uint state = BitConverter.ToUInt32(bytes, 28), flags = BitConverter.ToUInt32(bytes, 40),
                reason = BitConverter.ToUInt32(bytes, 44), disconnected = BitConverter.ToUInt32(bytes, 48),
                reconnected = BitConverter.ToUInt32(bytes, 52), transport = BitConverter.ToUInt32(bytes, 68),
                producer = BitConverter.ToUInt32(bytes, 12);
            uint rules = BitConverter.ToUInt32(bytes, 32), blacklist = BitConverter.ToUInt32(bytes, 36);
            bool anyKeyboard = transport == 3 && producer >= NostalgiaProducerVersion && rules >= 5 && blacklist == 0;
            if (transport == 3 && !anyKeyboard || !anyKeyboard && blacklist == 0) return null;
            if (anyKeyboard && (disconnected != 0 || reconnected != 0 || state == 3 || reason == 2 || reason == 3 ||
                reason == 6 || reason == 7 || reason == 11)) return null;
            if (state > 5 || flags > 15 || reason > 13 || reconnected > disconnected ||
                transport != 0 && transport != 2 && !anyKeyboard ||
                reason >= 12 && producer < 0x01060705u ||
                (transport == 2 || reason == 11) && producer < LocalConfirmationProducerVersion) return null;
            bool validState = state == 0 ? flags == 0 && reason == 0 && disconnected == 0 && reconnected == 0 :
                state == 1 ? ((flags == 3 || flags == 11) && reason == 7 && disconnected == 0 && reconnected == 0 ||
                    (flags == 7 || flags == 15) && reason == 11 ||
                    (flags == 3 || flags == 7 || flags == 11 || flags == 15) && reason == 13) :
                state == 2 ? flags == 15 && reason == 0 :
                state == 3 ? (flags == 3 || flags == 11) && reason == 7 && disconnected >= 1 :
                state == 4 ? (flags == 1 || flags == 3 || flags == 9 || flags == 11) &&
                    (reason >= 1 && reason <= 6 || reason == 9 || reason == 10 || reason == 12) :
                flags == 7 && reason == 8;
            if (!validState) return null;
            try
            {
                string config = ReadText(bytes, 72, 65), binding = ReadText(bytes, 137, 65),
                    name = ReadText(bytes, 202, 256), text = ReadText(bytes, 458, 256);
                ushort vid = BitConverter.ToUInt16(bytes, 64), product = BitConverter.ToUInt16(bytes, 66);
                if (anyKeyboard && (vid != 0 || product != 0 || binding.Length != 0 || name.Length != 0)) return null;
                if (transport == 2 && (vid != 0 || product != 0) || reason >= 11 && text.Length == 0) return null;
                if (state == 0)
                {
                    if (config.Length != 0 || binding.Length != 0 || name.Length != 0 || vid != 0 || product != 0)
                        return null;
                }
                else if (state == 4)
                {
                    if (text.Length == 0 || config.Length != 0 && !IsHash(config) || binding.Length != 0 && !IsHash(binding))
                        return null;
                }
                else if (!IsHash(config) || !anyKeyboard && (!IsHash(binding) || name.Length == 0) ||
                    transport == 0 && (vid == 0 || product == 0))
                    return null;
                return new HardcoreSnapshot(bytes, config, binding, name, text);
            }
            catch (ArgumentException) { return null; }
        }

        private static bool IsHash(string value)
        {
            if (value.Length != 64) return false;
            foreach (char ch in value) if (!(ch >= '0' && ch <= '9' || ch >= 'a' && ch <= 'f')) return false;
            return true;
        }

        private static string ReadText(byte[] bytes, int offset, int count)
        {
            int end = Array.IndexOf(bytes, (byte)0, offset, count);
            if (end < offset) throw new ArgumentException("Unterminated hardcore text");
            for (int i = end; i < offset + count; ++i)
                if (bytes[i] != 0) throw new ArgumentException("Nonzero hardcore text padding");
            return new UTF8Encoding(false, true).GetString(bytes, offset, end - offset);
        }
    }
}
