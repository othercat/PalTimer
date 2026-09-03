using System;
using System.Diagnostics;
using System.Threading;

namespace Pal98Timer
{
    internal static class TournamentTimerCapability
    {
        public const string EventName = @"Local\PAL98.PalTimer.TournamentLock.v1";

        public static EventWaitHandle Publish(string eventName = null)
        {
            try
            {
                bool createdNew;
                return new EventWaitHandle(
                    true,
                    EventResetMode.ManualReset,
                    string.IsNullOrWhiteSpace(eventName) ? EventName : eventName,
                    out createdNew);
            }
            catch (Exception ex)
            {
                // Do not break timer startup if the OS refuses a named object.
                // PALDLL will show the same actionable compatibility warning
                // as it does for a missing or outdated timer.
                Debug.WriteLine("Tournament timer capability unavailable: " + ex.Message);
                return null;
            }
        }
    }
}
