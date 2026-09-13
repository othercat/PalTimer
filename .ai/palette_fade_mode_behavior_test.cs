using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using Pal98Timer;

internal static class PaletteFadeModeBehaviorTest
{
    private static void Assert(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static byte[] Snapshot(int pid, long creation, int mode)
    {
        byte[] data = new byte[32];
        Array.Copy(BitConverter.GetBytes(0x31464C50u), 0, data, 0, 4);
        Array.Copy(BitConverter.GetBytes((ushort)1), 0, data, 4, 2);
        Array.Copy(BitConverter.GetBytes((ushort)32), 0, data, 6, 2);
        Array.Copy(BitConverter.GetBytes(pid), 0, data, 8, 4);
        Array.Copy(BitConverter.GetBytes(mode), 0, data, 12, 4);
        Array.Copy(BitConverter.GetBytes(creation), 0, data, 16, 8);
        Array.Copy(BitConverter.GetBytes(0x01060300u), 0, data, 24, 4);
        return data;
    }
    private static int Main()
    {
        using (Process process = Process.GetCurrentProcess())
        {
            int pid = process.Id;
            long creation = process.StartTime.ToUniversalTime().ToFileTimeUtc();
            byte[] data = Snapshot(pid, creation, 800);
            Assert(PaletteFadeModeReader.Decode(data, pid, creation) == 800, "800ms decode");
            Assert(PaletteFadeModeReader.Decode(Snapshot(pid, creation, 1200), pid, creation) == 1200, "1200ms decode");
            Assert(!PaletteFadeModeReader.Decode(data, pid + 1, creation).HasValue, "Foreign PID accepted");
            Assert(!PaletteFadeModeReader.Decode(data, pid, creation + 1).HasValue, "Reused PID accepted");
            Assert(!PaletteFadeModeReader.Decode(Snapshot(pid, creation, 999), pid, creation).HasValue, "Unknown mode accepted");
            Assert(!PaletteFadeModeReader.Decode(new byte[31], pid, creation).HasValue, "Truncated data accepted");
            for (int i = 0; i < 3; i++)
            {
                byte[] invalid = (byte[])data.Clone();
                invalid[new[] { 0, 4, 6 }[i]] = 0;
                Assert(!PaletteFadeModeReader.Decode(invalid, pid, creation).HasValue, "Invalid header accepted");
            }
            foreach (string title in new[] { "仙剑98原版 新补丁 1.63", "秋季杯比赛专用", "魂牵 / 非人", "梦幻22显血" })
            {
                Assert(PaletteFadeModeReader.FormatVersion(title, 1200) == title, "1.2s title changed");
                Assert(PaletteFadeModeReader.FormatVersion(title, 800) == title + "-0.8s", "Fast suffix missing");
                Assert(PaletteFadeModeReader.FormatVersion(title, null).Contains("未知"), "Unknown presented as slow");
            }
            Assert(!new PaletteFadeModeReader().Read(process).HasValue, "Missing publication accepted");
            using (MemoryMappedFile mapping = MemoryMappedFile.CreateNew(
                "Local\\PAL98.PaletteFadeMode.v1." + pid, 32))
            using (MemoryMappedViewAccessor view = mapping.CreateViewAccessor())
            {
                view.WriteArray(0, data, 0, data.Length);
                PaletteFadeModeReader reader = new PaletteFadeModeReader();
                Assert(reader.Read(process) == 800, "Live mapping read failed");
                Assert(reader.Read(process) == 800, "Immutable mode cache changed");
                Assert(!reader.Read(null).HasValue, "Detached process retained mode");
                byte[] slow = Snapshot(pid, creation, 1200);
                view.WriteArray(0, slow, 0, slow.Length);
                Assert(reader.Read(process) == 1200, "Reattach did not refresh mode");
            }
        }
        Console.WriteLine("PASS: 800/1200 labels, tournament/profile titles, live read-only mapping, detach/reattach, stale PID/header rejection");
        return 0;
    }
}
