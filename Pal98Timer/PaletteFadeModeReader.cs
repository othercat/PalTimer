using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace Pal98Timer
{
    // PALDLL's immutable startup mode, bound to the attached process lifetime.
    // Never infer timing rules from a config.ini that may have changed since launch.
    internal sealed class PaletteFadeModeReader
    {
        private Process observedProcess;
        private int? cachedMode;
        private long nextRetry;

        internal int? Read(Process process)
        {
            if (!ReferenceEquals(process, observedProcess))
            {
                observedProcess = process;
                cachedMode = null;
                nextRetry = 0;
            }
            if (process == null || cachedMode.HasValue) return cachedMode;
            long now = Stopwatch.GetTimestamp();
            if (now < nextRetry) return null;
            nextRetry = now + Stopwatch.Frequency;
            try
            {
                using (MemoryMappedFile mapping = MemoryMappedFile.OpenExisting(
                    "Local\\PAL98.PaletteFadeMode.v1." + process.Id,
                    MemoryMappedFileRights.Read))
                using (MemoryMappedViewAccessor view = mapping.CreateViewAccessor(0, 32,
                    MemoryMappedFileAccess.Read))
                {
                    byte[] data = new byte[32];
                    view.ReadArray(0, data, 0, data.Length);
                    cachedMode = Decode(data, process.Id,
                        process.StartTime.ToUniversalTime().ToFileTimeUtc());
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException ||
                ex is UnauthorizedAccessException || ex is InvalidOperationException ||
                ex is System.ComponentModel.Win32Exception || ex is ArgumentException)
            {
                // A legacy runtime or unavailable publication is unknown, not 1.2s.
            }
            return cachedMode;
        }

        internal static int? Decode(byte[] data, int processId, long creationTime)
        {
            if (data == null || data.Length != 32 ||
                BitConverter.ToUInt32(data, 0) != 0x31464C50u ||
                BitConverter.ToUInt16(data, 4) != 1 ||
                BitConverter.ToUInt16(data, 6) != 32 ||
                BitConverter.ToUInt32(data, 8) != (uint)processId ||
                BitConverter.ToInt64(data, 16) != creationTime ||
                BitConverter.ToUInt32(data, 28) != 0) return null;
            uint mode = BitConverter.ToUInt32(data, 12);
            return mode == 800 || mode == 1200 ? (int?)mode : null;
        }

        internal static string FormatVersion(string version, int? mode)
        {
            if (mode == 800) return version + "-0.8s";
            if (mode == 1200) return version;
            return version + " [黑屏模式未知]";
        }
    }
}
