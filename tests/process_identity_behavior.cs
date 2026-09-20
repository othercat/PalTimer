using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Pal98Timer;

internal static class ProcessIdentityBehavior
{
    static int checks;
    static void Check(bool condition, string name) { ++checks; if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static Process Start(string executable, string ready, string stop)
    {
        return Process.Start(new ProcessStartInfo(executable, "--child " + ready + " " + stop) {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
    }
    static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--child")
        {
            using (var ready = EventWaitHandle.OpenExisting(args[1]))
            using (var stop = EventWaitHandle.OpenExisting(args[2])) { ready.Set(); return stop.WaitOne(20000) ? 0 : 2; }
        }
        try
        {
            using (var current = Process.GetCurrentProcess())
            {
                var identity = PalLiveProcessIdentity.Read(current);
                Check(identity != null && identity.Pid == current.Id, "real live handle PID");
                Check(identity.CreationTime == current.StartTime.ToUniversalTime().ToFileTimeUtc(), "kernel creation time");
                Check(identity.SamePath(current.MainModule.FileName.ToUpperInvariant()), "full image path case insensitive");
                Check(!identity.SameInstance(new PalLiveProcessIdentity(identity.Pid, identity.CreationTime + 1, identity.ExecutablePath)), "reused PID rejected");
                Check(!identity.SameInstance(new PalLiveProcessIdentity(identity.Pid, identity.CreationTime, identity.ExecutablePath + ".other")), "different path rejected");
                Check(PalLiveProcessIdentity.ReadHandle(IntPtr.Zero, current.Id) == null, "invalid handle rejected");
            }
            string fixture = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "process-fixture");
            string a = Path.Combine(fixture, "a", "Pal.exe"), b = Path.Combine(fixture, "b", "Pal.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(a)); Directory.CreateDirectory(Path.GetDirectoryName(b));
            File.Copy(Assembly.GetExecutingAssembly().Location, a); File.Copy(Assembly.GetExecutingAssembly().Location, b);
            string key = "Local\\PalIdentityTest-" + Guid.NewGuid().ToString("N");
            using (var readyA = new EventWaitHandle(false, EventResetMode.AutoReset, key + "-ra"))
            using (var readyB = new EventWaitHandle(false, EventResetMode.AutoReset, key + "-rb"))
            using (var stopA = new EventWaitHandle(false, EventResetMode.ManualReset, key + "-sa"))
            using (var stopB = new EventWaitHandle(false, EventResetMode.ManualReset, key + "-sb"))
            using (var first = Start(a, key + "-ra", key + "-sa"))
            using (var bystander = Start(b, key + "-rb", key + "-sb"))
            {
                try
                {
                    Check(readyA.WaitOne(5000) && readyB.WaitOne(5000), "two real same-name children ready");
                    var original = PalLiveProcessIdentity.Read(first); var other = PalLiveProcessIdentity.Read(bystander);
                    Check(original != null && other != null, "both live identities readable");
                    Check(new[] { original, other }.Count(item => item.SamePath(original.ExecutablePath)) == 1, "attached path excludes bystander");
                    stopA.Set(); Check(first.WaitForExit(5000), "old instance terminates");
                    Check(PalLiveProcessIdentity.Read(first) == null, "retained exited process is not live");
                    stopA.Reset();
                    using (var restarted = Start(a, key + "-ra", key + "-sa"))
                    {
                        try
                        {
                            Check(readyA.WaitOne(5000), "replacement ready"); var next = PalLiveProcessIdentity.Read(restarted);
                            Check(next != null && next.SamePath(original.ExecutablePath) && !next.SameInstance(original), "same installation new instance");
                            Check(PalLiveProcessIdentity.Read(bystander).SameInstance(other), "bystander unchanged across restart");
                        }
                        finally { stopA.Set(); if (!restarted.WaitForExit(5000)) restarted.Kill(); }
                    }
                }
                finally { stopA.Set(); stopB.Set(); if (!first.WaitForExit(5000)) first.Kill(); if (!bystander.WaitForExit(5000)) bystander.Kill(); }
            }
            Console.WriteLine("PASS checks=" + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
