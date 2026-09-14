using System.Diagnostics;
using System;
using System.Runtime.InteropServices;

namespace Pal98Timer
{
    /// <summary>
    /// Debounces a transient access-denied result while PAL.exe is replacing its
    /// process during an in-game restart. Persistent denial still reaches the
    /// existing elevation-mismatch warning.
    /// </summary>
    internal sealed class PalProcessOpenRetryPolicy
    {
        internal const int AccessDeniedErrorCode = 5;
        internal const int AccessDeniedGraceMilliseconds = 9500;

        private bool HasPendingAccessDenied;
        private int PendingProcessId = -1;
        private long FirstAccessDeniedTimestamp;
        private string publishedMessage;

        internal void ClearPublishedMessage(ref string message)
        {
            if (message == publishedMessage) message = "";
            publishedMessage = null;
        }

        internal string DescribeFailure(int processId, int errorCode, string elevatedMessage)
        {
            bool exited;
            bool? targetElevated, timerElevated;
            Probe(processId, out exited, out targetElevated, out timerElevated);
            publishedMessage = DescribeFailure(errorCode, exited, targetElevated, timerElevated, elevatedMessage);
            return publishedMessage;
        }

        internal static string DescribeFailure(int errorCode, bool exited, bool? targetElevated,
            bool? timerElevated, string elevatedMessage)
        {
            if (exited) return "";
            if (errorCode == AccessDeniedErrorCode && targetElevated == true && timerElevated == false)
                return elevatedMessage;
            return "暂时无法访问 PAL.exe（Windows 错误码 " + errorCode + "），计时器将继续尝试连接。";
        }

        // Query-only handles remain usable when the full-access attach fails.
        // An error code alone never proves a UAC elevation mismatch.
        private static void Probe(int processId, out bool exited, out bool? targetElevated, out bool? timerElevated)
        {
            exited = false; targetElevated = null; timerElevated = null;
            IntPtr handle = OpenProcess(0x1000, false, processId); // QUERY_LIMITED_INFORMATION, Win7+
            if (handle == IntPtr.Zero)
            {
                exited = Marshal.GetLastWin32Error() == 87; // PID no longer exists
                return;
            }
            try
            {
                uint exitCode;
                if (GetExitCodeProcess(handle, out exitCode) && exitCode != 259) { exited = true; return; }
                targetElevated = ReadElevation(handle);
                timerElevated = ReadElevation(GetCurrentProcess());
                // The process can exit while querying the tokens.
                if (GetExitCodeProcess(handle, out exitCode) && exitCode != 259) exited = true;
            }
            finally { CloseHandle(handle); }
        }

        private static bool? ReadElevation(IntPtr process)
        {
            IntPtr token;
            if (!OpenProcessToken(process, 8, out token)) return null; // TOKEN_QUERY
            try
            {
                int elevated, length;
                return GetTokenInformation(token, 20, out elevated, 4, out length) && length == 4
                    ? (bool?)(elevated != 0) : null; // TokenElevation
            }
            finally { CloseHandle(token); }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inherit, int processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr token, int informationClass, out int information,
            int size, out int length);

        internal bool ShouldPublish(int processId, int errorCode)
        {
            return ShouldPublish(processId, errorCode, Stopwatch.GetTimestamp(), Stopwatch.Frequency);
        }

        internal bool ShouldPublish(int processId, int errorCode, long nowTimestamp, long timestampFrequency)
        {
            if (errorCode != AccessDeniedErrorCode)
            {
                Reset();
                return true;
            }

            // Invalid timing input cannot safely suppress a real permission error.
            if (processId <= 0 || timestampFrequency <= 0)
            {
                Reset();
                return true;
            }

            if (!HasPendingAccessDenied ||
                PendingProcessId != processId ||
                nowTimestamp < FirstAccessDeniedTimestamp)
            {
                HasPendingAccessDenied = true;
                PendingProcessId = processId;
                FirstAccessDeniedTimestamp = nowTimestamp;
                return false;
            }

            double elapsedMilliseconds =
                (nowTimestamp - FirstAccessDeniedTimestamp) * 1000.0 / timestampFrequency;
            return elapsedMilliseconds >= AccessDeniedGraceMilliseconds;
        }

        internal void Reset()
        {
            HasPendingAccessDenied = false;
            PendingProcessId = -1;
            FirstAccessDeniedTimestamp = 0;
        }
    }
}
