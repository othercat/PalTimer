using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Pal98Timer
{
    // Bind one live kernel process object, not only a reusable PID/name.
    internal sealed class PalLiveProcessIdentity
    {
        internal readonly int Pid;
        internal readonly long CreationTime;
        internal readonly string ExecutablePath;
        internal PalLiveProcessIdentity(int pid, long creationTime, string path)
        { Pid = pid; CreationTime = creationTime; ExecutablePath = path; }
        internal bool SameInstance(PalLiveProcessIdentity other)
        { return other != null && Pid == other.Pid && CreationTime == other.CreationTime && SamePath(other.ExecutablePath); }
        internal bool SamePath(string path)
        { return string.Equals(ExecutablePath, path, StringComparison.OrdinalIgnoreCase); }
        internal static PalLiveProcessIdentity Read(Process process)
        {
            if (process == null) return null;
            IntPtr handle = OpenProcess(0x00101000, false, process.Id); // SYNCHRONIZE | QUERY_LIMITED_INFORMATION
            if (handle == IntPtr.Zero) return null;
            try { return ReadHandle(handle, process.Id); }
            finally { Close(handle); }
        }
        internal static PalLiveProcessIdentity ReadHandle(IntPtr handle, int pid)
        {
            if (handle == IntPtr.Zero || WaitForSingleObject(handle, 0) != 258 || GetProcessId(handle) != (uint)pid) return null;
            long creation, exit, kernel, user;
            if (!GetProcessTimes(handle, out creation, out exit, out kernel, out user) || creation <= 0) return null;
            var path = new StringBuilder(32768); int size = path.Capacity;
            if (!QueryFullProcessImageName(handle, 0, path, ref size) || size <= 0 || WaitForSingleObject(handle, 0) != 258) return null;
            try { return new PalLiveProcessIdentity(pid, creation, Path.GetFullPath(path.ToString())); }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
        }
        internal static void Close(IntPtr handle) { if (handle != IntPtr.Zero) CloseHandle(handle); }
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint timeout);
        [DllImport("kernel32.dll")] private static extern uint GetProcessId(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(IntPtr handle, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr handle, uint flags, StringBuilder path, ref int size);
    }
}
