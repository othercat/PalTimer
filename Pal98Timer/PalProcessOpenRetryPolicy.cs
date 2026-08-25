using System.Diagnostics;

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
        internal const int AccessDeniedGraceMilliseconds = 1500;

        private bool HasPendingAccessDenied;
        private int PendingProcessId = -1;
        private long FirstAccessDeniedTimestamp;

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
