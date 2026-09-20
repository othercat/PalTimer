using System;
using System.Collections.Concurrent;
using System.Threading;
using Pal98Timer;

internal static class CloudSessionBehavior
{
    private static int checks;
    private static void Check(bool condition, string message)
    { ++checks; if (!condition) throw new Exception(message); }
    private static void Until(Func<bool> condition, string message)
    { Check(SpinWait.SpinUntil(condition, 3000), message); }
    private sealed class Fake : ICloudStepClient
    {
        internal int Calls, Stops, Finishes, Uploads, Downloads;
        internal bool Ready;
        internal Func<int, CloudStepResult> Action;
        internal Action TransferAction;
        internal CloudPayload LastPayload;
        public bool Initialized { get { return Ready; } }
        public int NextDataKind { get { return 2; } }
        public CloudStepResult Step(CloudPayload payload, bool finish)
        {
            LastPayload = payload;
            if (finish) ++Finishes;
            var result = Action == null ? new CloudStepResult { Initialized = true, Id = 42 } : Action(++Calls);
            Ready = result.Initialized;
            return result;
        }
        public void Stop() { ++Stops; }
        public void Upload(string local, string remote) { ++Uploads; TransferAction?.Invoke(); }
        public void Download(string remote, string local) { ++Downloads; TransferAction?.Invoke(); }
    }
    private static CloudSession Begin(CloudSessionRunner runner, Func<bool> valid = null,
        Action<CloudSession, CloudStatus> changed = null)
    {
        return runner.Start("offline-test", valid ?? (() => true),
            next => new CloudPayload { Lite = "fixture-lite", Big = "fixture-big", IsC = true },
            changed ?? ((session, status) => { }));
    }
    private static CloudSessionRunner Runner(Func<string, ICloudStepClient> factory)
    { return new CloudSessionRunner(factory, 10, (attempt, minimum) => 5); }
    private static void Close(CloudSessionRunner runner)
    { runner.Dispose(); Check(runner.WaitForExit(2000), "worker did not stop"); }

    private static void IdentityAndPayload()
    {
        bool ready = false; int creates = 0;
        var fake = new Fake();
        var runner = Runner(name => { Interlocked.Increment(ref creates); return fake; });
        var session = Begin(runner, () => Volatile.Read(ref ready));
        Thread.Sleep(60); Check(creates == 0, "cloud requested before game identity");
        Volatile.Write(ref ready, true);
        Until(() => session.CloudID == 42, "identity readiness did not resume automatically");
        Until(() => fake.LastPayload != null, "data not prepared after initialization");
        Check(fake.LastPayload.Lite == "fixture-lite" && fake.LastPayload.Big == "fixture-big", "payload changed");
        session.PutLiteData("custom-lite");
        Until(() => fake.LastPayload.Lite == "custom-lite", "custom payload ignored");
        Volatile.Write(ref ready, false);
        Until(() => session.Status.State == "waiting", "lost identity not suspended");
        Check(fake.Stops == 1 && session.CloudID < 0, "invalid identity retained online state");
        Close(runner);
    }
    private static void RetryBoundaries()
    {
        foreach (int delay in new[] { 0, 10, 30 })
        {
            var fake = new Fake { Action = call => new CloudStepResult {
                Initialized = delay == 30, Id = -3, DelaySeconds = delay, Error = "server reason" } };
            var runner = Runner(name => fake);
            var session = Begin(runner);
            Until(() => session.Status.State == "failed", "failure retries not bounded: " + delay);
            Check(fake.Calls == 4, "wrong maximum attempts");
            Thread.Sleep(60);
            Check(fake.Calls == 4 && session.Status.Message.Contains("server reason"), "retry continued or reason lost");
            Check(session.CloudID < 0, "failure looks online");
            Close(runner);
        }
        var recovered = new Fake { Action = call => new CloudStepResult {
            Initialized = call > 2, Id = 55, DelaySeconds = call > 2 ? 0 : 10, Error = "stale error" } };
        var goodRunner = Runner(name => recovered);
        var good = Begin(goodRunner);
        Until(() => good.CloudID == 55, "transient failure did not recover");
        Check(good.Status.Message == "", "successful state retained old error");
        Close(goodRunner);
        var alternating = new Fake { Action = call => new CloudStepResult {
            Initialized = call % 2 == 1, Id = 66, DelaySeconds = call % 2 == 1 ? 0 : 30, Error = "send rejected" } };
        var alternatingRunner = Runner(name => alternating);
        var alternatingSession = Begin(alternatingRunner);
        Until(() => alternatingSession.Status.State == "failed", "re-auth success reset send failure budget");
        Check(alternating.Calls == 8, "alternating init/send attempts not bounded");
        Thread.Sleep(60); Check(alternating.Calls == 8, "alternating retry continued after terminal failure");
        Close(alternatingRunner);
    }
    private static void BlockedReplacement()
    {
        var entered = new ManualResetEvent(false); var release = new ManualResetEvent(false);
        int creates = 0, staleOnline = 0;
        var first = new Fake { Action = call => {
            entered.Set(); release.WaitOne(); return new CloudStepResult { Initialized = true, Id = 88 }; } };
        var second = new Fake();
        var runner = Runner(name => Interlocked.Increment(ref creates) == 1 ? first : second);
        var old = Begin(runner, changed: (s, status) => { if (status.State == "online") ++staleOnline; });
        Check(entered.WaitOne(2000), "initial request did not enter");
        var next = Begin(runner);
        Thread.Sleep(60);
        Check(creates == 1 && first.Stops == 0, "overlapping request or stop before completion");
        release.Set();
        Until(() => next.CloudID == 42, "replacement did not resume after completion");
        Check(creates == 2 && first.Stops == 1 && staleOnline == 0 && old.Cancelled, "stale callback or lifecycle");
        Close(runner);
    }
    private static void FinishAndTransfers()
    {
        int creates = 0; var first = new Fake(); var second = new Fake();
        var runner = Runner(name => Interlocked.Increment(ref creates) == 1 ? first : second);
        var session = Begin(runner);
        Until(() => session.CloudID == 42, "initial session offline");
        runner.TransferFile(session, true, "fixture-local", "fixture-remote");
        runner.TransferFile(session, false, "fixture-local", "fixture-remote");
        Check(first.Uploads == 1 && first.Downloads == 1, "transfer dispatch changed");
        runner.FinishOne(session);
        Until(() => creates == 2, "finish did not recreate cloud ID");
        Check(first.Finishes == 1 && first.Stops == 1, "finish omitted or duplicated");
        Close(runner);

        var entered = new ManualResetEvent(false); var release = new ManualResetEvent(false);
        var upload = new Fake { TransferAction = () => { entered.Set(); release.WaitOne(); } };
        int transferCreates = 0;
        var transferRunner = Runner(name => { ++transferCreates; return upload; });
        var transferSession = Begin(transferRunner);
        Until(() => transferSession.CloudID == 42, "transfer session offline");
        Exception error = null;
        var caller = new Thread(() => { try { transferRunner.TransferFile(transferSession, true, "local", "remote"); }
            catch (Exception ex) { error = ex; } });
        caller.Start(); Check(entered.WaitOne(2000), "transfer did not enter");
        Begin(transferRunner); Thread.Sleep(60);
        Check(transferCreates == 1, "session overlapped active transfer");
        release.Set(); Check(caller.Join(2000) && error != null, "stale transfer reported success");
        Close(transferRunner);
    }
    public static int Main()
    {
        try
        {
            // Hash/signatures only. Never construct/start the network library.
            LegacyCloudStepClient.ValidateContract(); Check(true, "bundled binary contract");
            IdentityAndPayload(); RetryBoundaries(); BlockedReplacement(); FinishAndTransfers();
            Console.WriteLine("PASS: " + checks + " cloud scheduling / identity / retry / transfer checks; network=none");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
