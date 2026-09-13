using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace Pal98Timer
{
    internal sealed class RuntimeTimingMode
    {
        internal readonly int FadeMilliseconds;
        internal readonly int MapSpeedTicks;
        internal RuntimeTimingMode(int fadeMilliseconds, int mapSpeedTicks)
        { FadeMilliseconds = fadeMilliseconds; MapSpeedTicks = mapSpeedTicks; }
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
                if (process == null || cached != null) return cached;
                long now = Stopwatch.GetTimestamp();
                if (now < nextRetry) return null;
                nextRetry = now + Stopwatch.Frequency;
                try
                {
                    using (var mapping = MemoryMappedFile.OpenExisting(
                        "Local\\PAL98.TimingMode.v1." + process.Id, MemoryMappedFileRights.Read))
                    using (var view = mapping.CreateViewAccessor(0, 32, MemoryMappedFileAccess.Read))
                    {
                        var bytes = new byte[32];
                        view.ReadArray(0, bytes, 0, bytes.Length);
                        cached = Decode(bytes, process.Id, process.StartTime.ToUniversalTime().ToFileTimeUtc());
                    }
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException ||
                    ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception || ex is ArgumentException)
                { /* Old runtimes cannot prove map speed, even when their version says 1.63. */ }
                return cached;
            }
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
            if ((fade != 800 && fade != 1200) || (speed != 8 && speed != 10) ||
                (fade == 1200 && speed != 10)) return null;
            return new RuntimeTimingMode((int)fade, (int)speed);
        }
    }
}
