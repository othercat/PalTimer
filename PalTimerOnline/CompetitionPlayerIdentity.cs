using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Pal98.LocalDisplay;

namespace Pal98Timer
{
    internal sealed partial class CompetitionClient
    {
        private sealed class IdentityTarget
        {
            internal string Root, Executable;
            internal int ProcessId;
            internal long CreatedAt, GameEpoch, TargetRequest, InstalledTick;
            internal bool SyncStarted, SyncCompleted;
            internal DateTime NextAttempt;
        }
        private sealed class IdentityRequest
        {
            internal IdentityTarget Target;
            internal long Generation;
            internal string Server;
        }
        private volatile IdentityTarget identityTarget;
        private bool identityBehaviorCapability;

        private void InstallIdentityTarget(CompetitionGameResolution resolved, long epoch, long request)
        {
            if (resolved == null || resolved.IdentityRoot == null || epoch != Interlocked.Read(ref gameGeneration) ||
                request != Interlocked.Read(ref requestedTarget)) return;
            identityTarget = new IdentityTarget { Root = resolved.IdentityRoot, Executable = resolved.Executable,
                ProcessId = resolved.ProcessId, CreatedAt = resolved.CreatedAt, GameEpoch = epoch, TargetRequest = request,
                InstalledTick = Stopwatch.GetTimestamp() };
            // A different directory on the same endpoint still needs a fresh reply.
            registeredKey = ""; registrationRejected = false; nextRegister = DateTime.MinValue;
        }

        private IdentityRequest CaptureIdentityRequest(CompetitionSettings cfg)
        {
            var target = identityTarget;
            if (target == null || !target.SyncStarted || target.SyncCompleted) return null;
            var request = new IdentityRequest { Target = target, Generation = Interlocked.Read(ref generation), Server = cfg.Server };
            return IdentityCurrent(request) ? request : null;
        }

        private void PrepareLocalIdentitySync()
        {
            var target = identityTarget;
            if (target == null || target.SyncCompleted || target.TargetRequest != Interlocked.Read(ref requestedTarget)) return;
            if (!target.SyncStarted) {
                var clock = Volatile.Read(ref liveClock);
                if (clock == null || clock.ObservedTick < target.InstalledTick || CaptureLiveObservation().state != "running") return;
                target.SyncStarted = true;
            }
            // Identity refresh cannot bypass rejected credentials or the
            // endpoint's retry deadline (including Retry-After).
            if (registrationRejected || DateTime.UtcNow < nextRegister || DateTime.UtcNow < target.NextAttempt) return;
            // One successful sync per detected game process, armed only by an
            // actual running clock. Registration in the title menu is not reused.
            target.NextAttempt = DateTime.UtcNow.AddSeconds(30);
            registeredKey = "";
        }

        private bool IdentityCurrent(IdentityRequest request)
        {
            if (request == null || request.Target == null) return false;
            // Only scalar state is read under the timing-publication lock.
            lock (publication) return !closing && ReferenceEquals(identityTarget, request.Target) &&
                request.Generation == Interlocked.Read(ref generation) && request.Server == settings.Server &&
                request.Target.GameEpoch == Interlocked.Read(ref gameGeneration) &&
                request.Target.TargetRequest == Interlocked.Read(ref requestedTarget);
        }

        private bool IdentityProcessCurrent(IdentityTarget target)
        {
            try {
                using (var process = Process.GetProcessById(target.ProcessId))
                    return !process.HasExited && process.StartTime.ToUniversalTime().ToFileTimeUtc() == target.CreatedAt &&
                        String.Equals(process.MainModule.FileName, target.Executable, StringComparison.OrdinalIgnoreCase);
            } catch { return false; }
        }

        private bool SaveIdentity(IdentityRequest request, PlayerIdentity value, byte[] behavior)
        {
            if (!IdentityCurrent(request) || !IdentityProcessCurrent(request.Target)) return false;
            return LocalPlayerIdentity.Save(request.Target.Root, value, null, false, replace => {
                // Background loop only. No filesystem operation holds publication.
                if (!IdentityProcessCurrent(request.Target) || !IdentityCurrent(request)) return false;
                replace(); return true;
            }, behavior);
        }

        private void PublishLocalIdentity(IdentityRequest request, string body, bool live = false)
        {
            if (!IdentityCurrent(request)) return;
            try {
                // Inspect presence/types before the DTO defaults can turn malformed
                // or absent player data into a false unbinding response.
                var reply = CompetitionProtocol.Json().Deserialize<Dictionary<string, object>>(body);
                object raw;
                if (live) {
                    if (reply == null || !reply.TryGetValue("standings", out raw)) return;
                    reply = raw as Dictionary<string, object>;
                }
                if (reply == null || !reply.TryGetValue("player", out raw)) return;
                var player = raw as Dictionary<string, object>;
                object bound, name, id;
                if (player == null || !player.TryGetValue("bound", out bound) || !(bound is bool) ||
                    !player.TryGetValue("display_name", out name) || name != null && !(name is string) ||
                    !player.TryGetValue("id", out id) || id != null && !(id is string)) return;
                var identity = LocalPlayerIdentity.Server(request.Server, id as string, name as string, (bool)bound);
                byte[] behavior = IdentityBehaviorSnapshot.Build(reply, identity, id as string, identityBehaviorCapability);
                if (SaveIdentity(request, identity, behavior))
                    request.Target.SyncCompleted = true;
            } catch { storage.LogNetwork("local-player-identity-unavailable", 0); }
        }
    }
}
