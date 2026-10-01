using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Pal98Timer;

// Runs in an isolated copy. The native host binding is tested separately by
// the private native probe; this exercises the real frozen host's loader.
internal static class OnlineModuleLoaderBehavior
{
    [STAThread]
    static int Main(string[] args)
    {
        IDisposable lease = null;
        try {
            bool accepted = false;
            var loader = typeof(IPalTimerOnlineV1).Assembly.GetType("Pal98Timer.OnlineModuleLoader", true);
            try {
                lease = (IDisposable)loader.GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { AppDomain.CurrentDomain.BaseDirectory });
                accepted = true;
            } catch (TargetInvocationException e) { Console.WriteLine("LOAD rejected: " + e.InnerException.GetType().Name); }
            if (accepted != (args[0] == "valid")) throw new Exception("Unexpected component acceptance.");
            if (accepted) {
                var module = (IPalTimerOnlineV1)lease.GetType().GetField("Module", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(lease);
                if (module.ApiVersion != 1) throw new Exception("Wrong API.");
                bool writeRejected = false;
                try { using (File.Open("PalTimerOnline.dll", FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { } }
                catch (IOException) { writeRejected = true; }
                if (!writeRejected) throw new Exception("Verified composition was replaceable during use.");
                module.CloseAsync().GetAwaiter().GetResult();
            }
            // Optional component failure does not prevent local stopwatch use.
            var local = new PTimer(); local.Start(); Thread.Sleep(35); local.Stop();
            if (local.CurrentTSOnly.TotalMilliseconds < 20) throw new Exception("Local stopwatch unavailable.");
            Console.WriteLine("PASS loader=" + accepted + "; local stopwatch remains available");
            return 0;
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { lease?.Dispose(); }
    }
}
