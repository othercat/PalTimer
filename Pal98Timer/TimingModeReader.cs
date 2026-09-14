using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace Pal98Timer
{
    internal sealed class RuntimeTimingMode
    {
        internal readonly int FadeMilliseconds;
        internal readonly int MapSpeedTicks;
        internal readonly bool OfficialSpeedrun;
        internal readonly string ContentId, ContentVersion, ContentHash, DisplayName;
        internal bool HasContentIdentity { get { return !string.IsNullOrEmpty(ContentId); } }
        internal RuntimeTimingMode(int fadeMilliseconds, int mapSpeedTicks)
        { FadeMilliseconds = fadeMilliseconds; MapSpeedTicks = mapSpeedTicks; }
        internal RuntimeTimingMode(int fade, int speed, bool official, string id, string version, string hash, string name)
            : this(fade, speed)
        { OfficialSpeedrun = official; ContentId = id; ContentVersion = version; ContentHash = hash; DisplayName = name; }
        internal bool SameRun(RuntimeTimingMode other)
        {
            return other != null && FadeMilliseconds == other.FadeMilliseconds && MapSpeedTicks == other.MapSpeedTicks &&
                OfficialSpeedrun == other.OfficialSpeedrun && ContentId == other.ContentId &&
                ContentVersion == other.ContentVersion && ContentHash == other.ContentHash;
        }
    }

    // Read-only startup facts, bound to the actual attached process, never INI.
    internal sealed class TimingModeReader
    {
        private readonly object sync = new object();
        private Process observedProcess;
        private RuntimeTimingMode cached;
        private long nextRetry;

        internal RuntimeTimingMode Read(Process process)
        {
            lock (sync)
            {
                if (!ReferenceEquals(process, observedProcess))
                { observedProcess = process; cached = null; nextRetry = 0; }
                if (process == null) return null;
                long now = Stopwatch.GetTimestamp();
                if (now < nextRetry) return cached;
                nextRetry = now + Stopwatch.Frequency;
                try
                {
                    using (var mapping = MemoryMappedFile.OpenExisting(
                        "Local\\PAL98.TimingMode.v2." + process.Id, MemoryMappedFileRights.Read))
                    using (var view = mapping.CreateViewAccessor(0, 592, MemoryMappedFileAccess.Read))
                    {
                        if (process.HasExited) { cached = null; return null; }
                        var bytes = new byte[592];
                        view.ReadArray(0, bytes, 0, bytes.Length);
                        cached = DecodeV2(bytes, process.Id, process.StartTime.ToUniversalTime().ToFileTimeUtc());
                    }
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException ||
                    ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception || ex is ArgumentException)
                { cached = null; /* Older interfaces cannot prove content identity. */ }
                return cached;
            }
        }

        internal static RuntimeTimingMode DecodeV2(byte[] bytes, int pid, long creation)
        {
            if (bytes == null || bytes.Length != 592 || BitConverter.ToUInt32(bytes, 0) != 0x324D5450u ||
                BitConverter.ToUInt16(bytes, 4) != 2 || BitConverter.ToUInt16(bytes, 6) != 592 ||
                BitConverter.ToUInt32(bytes, 8) != (uint)pid || BitConverter.ToInt64(bytes, 16) != creation ||
                BitConverter.ToUInt32(bytes, 24) < 0x01060500u) return null;
            uint fade = BitConverter.ToUInt32(bytes, 12), speed = BitConverter.ToUInt32(bytes, 28), kind = BitConverter.ToUInt32(bytes, 32);
            if ((fade != 800 && fade != 1200) || (speed != 9 && speed != 10) || (kind != 1 && kind != 2) ||
                (kind == 1 && fade == 1200 && speed != 10) || BitConverter.ToUInt32(bytes, 36) != 0) return null;
            try
            {
                string id = ReadText(bytes, 40, 160), version = ReadText(bytes, 200, 64),
                    hash = ReadText(bytes, 264, 65), name = ReadText(bytes, 329, 256);
                if (id.Length == 0 || name.Length == 0 || hash.Length != 64 ||
                    !System.Text.RegularExpressions.Regex.IsMatch(hash, "^[0-9a-fA-F]{64}$")) return null;
                for (int i = 585; i < 592; ++i) if (bytes[i] != 0) return null;
                return new RuntimeTimingMode((int)fade, (int)speed, kind == 1, id, version, hash.ToLowerInvariant(), name);
            }
            catch (ArgumentException) { return null; }
        }
        private static string ReadText(byte[] bytes, int offset, int size)
        {
            int end = Array.IndexOf(bytes, (byte)0, offset, size);
            if (end < offset) throw new ArgumentException("Unterminated timing identity");
            for (int i = end; i < offset + size; ++i) if (bytes[i] != 0) throw new ArgumentException("Nonzero timing padding");
            return new UTF8Encoding(false, true).GetString(bytes, offset, end - offset);
        }

        internal static RuntimeTimingMode Decode(byte[] bytes, int pid, long creation)
        {
            if (bytes == null || bytes.Length != 32 ||
                BitConverter.ToUInt32(bytes, 0) != 0x314D5450u ||
                BitConverter.ToUInt16(bytes, 4) != 1 || BitConverter.ToUInt16(bytes, 6) != 32 ||
                BitConverter.ToUInt32(bytes, 8) != (uint)pid ||
                BitConverter.ToInt64(bytes, 16) != creation ||
                BitConverter.ToUInt32(bytes, 24) < 0x01060300u) return null;
            uint fade = BitConverter.ToUInt32(bytes, 12), speed = BitConverter.ToUInt32(bytes, 28);
            // Reject the unreleased 8-tick runtime; it cannot verify 9-tick scores.
            if ((fade != 800 && fade != 1200) || (speed != 9 && speed != 10) ||
                (fade == 1200 && speed != 10)) return null;
            return new RuntimeTimingMode((int)fade, (int)speed);
        }
    }
}
