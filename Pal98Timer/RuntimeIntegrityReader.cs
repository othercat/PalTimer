using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace Pal98Timer
{
    // Independent, read-only PAL98.RuntimeIntegrity.v1 contract. This is diagnostic
    // evidence, not authentication; the timer verifies code separately.
    internal sealed class RuntimeIntegritySnapshot
    {
        internal int Protection;
        internal uint Alerts, EventSequence, Generation, Sequence, ProducerVersion;
        internal ulong RandomCalls, ProcessedCalls, DroppedCalls;
        internal long Heartbeat, Frequency;
        internal long LastEvent;
        internal uint Role, Field;
        internal int Observed, Expected;
        internal string Build, Detail;
    }

    internal static class RuntimeIntegrityReader
    {
        internal const int Size = 320;
        private static bool SupportedProducer(uint version)
        {
            // r10 renamed the existing v1.69 candidate; its v1 IPC layout did
            // not change. Decode only these known producers. File/build/code
            // checks still independently reject an old or mismatched release.
            return version == 0x0106080Au || version == 0x01060900u;
        }
        internal static RuntimeIntegritySnapshot Read(PalLiveProcessIdentity identity, long frequency, RuntimeIntegritySnapshot previous)
        {
            try
            {
                using (var mapping = MemoryMappedFile.OpenExisting("Local\\PAL98.RuntimeIntegrity.v1." + identity.Pid, MemoryMappedFileRights.Read))
                using (var view = mapping.CreateViewAccessor(0, Size, MemoryMappedFileAccess.Read))
                {
                    var bytes = new byte[Size];
                    for (int attempt = 0; attempt < 3; ++attempt)
                    {
                        uint before = view.ReadUInt32(12);
                        if ((before & 1) != 0) { Thread.SpinWait(16); continue; }
                        view.ReadArray(0, bytes, 0, bytes.Length);
                        uint after = view.ReadUInt32(12);
                        if (before != after || before != BitConverter.ToUInt32(bytes, 12)) { Thread.SpinWait(16); continue; }
                        // Capture time after copying: a legitimately newer native
                        // heartbeat must not be rejected as coming from the future.
                        return Decode(bytes, identity.Pid, identity.CreationTime, Stopwatch.GetTimestamp(), frequency);
                    }
                    // Only seqlock contention may use the same session's previous
                    // snapshot, with its ORIGINAL heartbeat/expiry. Missing,
                    // invalid or expired mappings never receive this fallback.
                    long now = Stopwatch.GetTimestamp();
                    return previous != null && previous.Frequency == frequency && frequency > 0 &&
                        previous.Heartbeat > 0 && previous.Heartbeat <= now && now - previous.Heartbeat <= frequency * 3 ? previous : null;
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            { return null; }
        }

        internal static RuntimeIntegritySnapshot Decode(byte[] bytes, int pid, long creation, long now, long frequency)
        {
            if (bytes == null || bytes.Length != Size || BitConverter.ToUInt32(bytes, 0) != 0x31495250u ||
                BitConverter.ToUInt16(bytes, 4) != 1 || BitConverter.ToUInt16(bytes, 6) != Size ||
                BitConverter.ToUInt32(bytes, 8) != (uint)pid || (BitConverter.ToUInt32(bytes, 12) & 1) != 0 ||
                BitConverter.ToInt64(bytes, 16) != creation || !SupportedProducer(BitConverter.ToUInt32(bytes, 24)) ||
                BitConverter.ToUInt32(bytes, 28) > 4 || BitConverter.ToUInt32(bytes, 84) != 0 ||
                (BitConverter.ToUInt32(bytes, 72) & ~127u) != 0) return null;
            long heartbeat = BitConverter.ToInt64(bytes, 32), sourceFrequency = BitConverter.ToInt64(bytes, 40);
            if (frequency <= 0 || sourceFrequency != frequency || heartbeat <= 0 || heartbeat > now ||
                now - heartbeat > frequency * 3) return null;
            try
            {
                string build = Text(bytes, 96, 48);
                if (!System.Text.RegularExpressions.Regex.IsMatch(build, "^[A-Za-z0-9_.-]{1,47}$")) return null;
                return new RuntimeIntegritySnapshot {
                    Sequence = BitConverter.ToUInt32(bytes, 12), ProducerVersion = BitConverter.ToUInt32(bytes, 24),
                    Protection = (int)BitConverter.ToUInt32(bytes, 28), Heartbeat = heartbeat, Frequency = sourceFrequency,
                    RandomCalls = BitConverter.ToUInt64(bytes, 48), ProcessedCalls = BitConverter.ToUInt64(bytes, 56),
                    DroppedCalls = BitConverter.ToUInt64(bytes, 64), Alerts = BitConverter.ToUInt32(bytes, 72),
                    EventSequence = BitConverter.ToUInt32(bytes, 76), Generation = BitConverter.ToUInt32(bytes, 80),
                    LastEvent = BitConverter.ToInt64(bytes, 88), Role = BitConverter.ToUInt32(bytes, 304),
                    Field = BitConverter.ToUInt32(bytes, 308), Observed = BitConverter.ToInt32(bytes, 312), Expected = BitConverter.ToInt32(bytes, 316),
                    Build = build, Detail = Text(bytes, 144, 160)
                };
            }
            catch (ArgumentException) { return null; }
        }
        private static string Text(byte[] bytes, int offset, int count)
        {
            int end = Array.IndexOf(bytes, (byte)0, offset, count);
            if (end < offset) throw new ArgumentException("Unterminated integrity text");
            return new UTF8Encoding(false, true).GetString(bytes, offset, end - offset);
        }
    }
}
