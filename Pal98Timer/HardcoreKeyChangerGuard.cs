using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Pal98Timer
{
    internal sealed class HardcoreKeyChangerGuard
    {
        private readonly object sync = new object();
        private readonly Func<bool> readRequested;
        private bool blocked, autoStartAttempted;
        private long generation;
        internal HardcoreKeyChangerGuard(Func<bool> readRequested) { this.readRequested = readRequested; }
        internal bool Refresh()
        {
            lock (sync)
            {
                bool next = readRequested();
                if (next && !blocked) ++generation;
                blocked = next;
                if (blocked) autoStartAttempted = true;
                return blocked;
            }
        }
        internal long BeginAutoStart()
        {
            lock (sync)
            {
                if (Refresh() || autoStartAttempted) return -1;
                autoStartAttempted = true;
                return generation;
            }
        }
        internal long BeginRequest()
        {
            lock (sync) return Refresh() ? -1 : generation;
        }
        internal bool IsCurrent(long request)
        {
            lock (sync) return !Refresh() && request >= 0 && request == generation;
        }
        internal bool TryRun(Action action, long? request = null, Action revoke = null)
        {
            long accepted = request ?? BeginRequest();
            if (!IsCurrent(accepted)) return false;
            // SendMessage(TEDIT) can remain in a modal dialog indefinitely.
            // Never hold the guard lock across an external process/UI action.
            bool current = false;
            try { action(); }
            finally
            {
                current = IsCurrent(accepted);
                if (!current && revoke != null) revoke();
            }
            return current;
        }
    }

    // Also protects startup, an unattached/multiple PAL, and switching timer cores.
    // This does not attach to or read memory from a game process.
    internal sealed class HardcoreRequestedProcesses
    {
        private readonly HardcoreModeReader reader = new HardcoreModeReader();
        private readonly Dictionary<int, long> requested = new Dictionary<int, long>();
        internal bool Read()
        {
            var retained = new Dictionary<int, long>();
            try
            {
                foreach (Process process in Process.GetProcessesByName("Pal"))
                {
                    using (process)
                    {
                        int pid = process.Id;
                        long previousCreation;
                        bool known = requested.TryGetValue(pid, out previousCreation);
                        try
                        {
                            if (process.HasExited) continue;
                            long creation = process.StartTime.ToUniversalTime().ToFileTimeUtc();
                            HardcoreSnapshot snapshot = reader.Read(process);
                            if (snapshot != null ? snapshot.Requested : known && previousCreation == creation)
                                retained[pid] = creation;
                        }
                        catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
                        { if (known) retained[pid] = previousCreation; }
                    }
                }
            }
            catch (InvalidOperationException) { return requested.Count != 0; }
            requested.Clear();
            foreach (var item in retained) requested.Add(item.Key, item.Value);
            return requested.Count != 0;
        }
    }
}
