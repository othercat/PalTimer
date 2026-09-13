using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using Pal98Timer;

internal static class TimingModeBehaviorTest
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static byte[] Bytes(int pid, long creation, int ms, int speed)
    {
        byte[] b = new byte[32];
        Array.Copy(BitConverter.GetBytes(0x314D5450u), 0, b, 0, 4);
        Array.Copy(BitConverter.GetBytes((ushort)1), 0, b, 4, 2);
        Array.Copy(BitConverter.GetBytes((ushort)32), 0, b, 6, 2);
        Array.Copy(BitConverter.GetBytes(pid), 0, b, 8, 4);
        Array.Copy(BitConverter.GetBytes(ms), 0, b, 12, 4);
        Array.Copy(BitConverter.GetBytes(creation), 0, b, 16, 8);
        Array.Copy(BitConverter.GetBytes(0x01060300u), 0, b, 24, 4);
        Array.Copy(BitConverter.GetBytes(speed), 0, b, 28, 4);
        return b;
    }
    static int Main()
    {
        using (var process = Process.GetCurrentProcess())
        {
            int pid = process.Id;
            long creation = process.StartTime.ToUniversalTime().ToFileTimeUtc();
            foreach (var values in new[] { new[] { 1200, 10 }, new[] { 800, 10 }, new[] { 800, 9 } })
            {
                byte[] bytes = Bytes(pid, creation, values[0], values[1]);
                var mode = TimingModeReader.Decode(bytes, pid, creation);
                Assert(mode != null && mode.FadeMilliseconds == values[0] && mode.MapSpeedTicks == values[1], "Valid mode rejected");
                Assert(TimingModeReader.Decode(bytes, pid + 1, creation) == null, "Foreign PID accepted");
                Assert(TimingModeReader.Decode(bytes, pid, creation + 1) == null, "Reused PID accepted");
                foreach (int offset in new[] { 0, 4, 6, 24 })
                {
                    byte[] bad = (byte[])bytes.Clone();
                    Array.Clear(bad, offset, offset == 4 || offset == 6 ? 2 : 4);
                    Assert(TimingModeReader.Decode(bad, pid, creation) == null, "Invalid header accepted");
                }
            }
            foreach (var values in new[] { new[] { 1200, 8 }, new[] { 1200, 9 }, new[] { 800, 8 }, new[] { 800, 7 }, new[] { 800, 0 }, new[] { 1230, 10 } })
                Assert(TimingModeReader.Decode(Bytes(pid, creation, values[0], values[1]), pid, creation) == null, "Unsupported mode accepted");
            Assert(TimingModeReader.Decode(new byte[31], pid, creation) == null, "Truncated snapshot accepted");
            Assert(new TimingModeReader().Read(process) == null, "Absent timing interface guessed");
            using (var mapping = MemoryMappedFile.CreateNew("Local\\PAL98.TimingMode.v1." + pid, 32))
            using (var view = mapping.CreateViewAccessor())
            {
                view.WriteArray(0, Bytes(pid, creation, 800, 9), 0, 32);
                var reader = new TimingModeReader();
                Assert(reader.Read(process).MapSpeedTicks == 9, "Live mode read failed");
                view.WriteArray(0, Bytes(pid, creation, 1200, 10), 0, 32);
                Assert(reader.Read(process).MapSpeedTicks == 9, "Snapshot was not immutable");
                Assert(reader.Read(null) == null, "Detach kept snapshot");
                Assert(reader.Read(process).FadeMilliseconds == 1200, "Reattach did not refresh");
            }
        }
        Console.WriteLine("PASS: three runtime modes, old 8-tick/header/PID rejection, read-only mapping, immutable snapshot and reattach");
        return 0;
    }
}
