using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;

namespace Pal98Timer
{
    internal sealed class CloudPayload
    {
        internal string Lite, Big;
        internal bool IsC;
        internal readonly Dictionary<string, string> Plugins = new Dictionary<string, string>();
    }
    internal sealed class CloudStepResult
    {
        internal bool Initialized;
        internal int Id, DelaySeconds;
        internal string Error;
    }
    internal interface ICloudStepClient
    {
        bool Initialized { get; }
        int NextDataKind { get; }
        CloudStepResult Step(CloudPayload payload, bool finish);
        void Upload(string local, string remote);
        void Download(string remote, string local);
        void Stop();
    }
    internal sealed class CloudStatus
    {
        internal readonly string State, Message;
        internal readonly int Id;
        internal CloudStatus(string state, string message, int id = -1)
        { State = state; Message = message; Id = id; }
    }
    internal sealed class CloudSession
    {
        internal readonly string CoreName;
        internal readonly Func<bool> Valid;
        internal readonly Func<int, CloudPayload> Prepare;
        internal readonly Action<CloudSession, CloudStatus> Changed;
        internal volatile bool Cancelled;
        internal volatile CloudStatus Status = new CloudStatus("waiting", "等待游戏身份");
        internal int FinishRequested;
        internal string LiteOverride, BigOverride;
        internal readonly object DataLock = new object();
        internal CloudSession(string name, Func<bool> valid, Func<int, CloudPayload> prepare,
            Action<CloudSession, CloudStatus> changed)
        { CoreName = name; Valid = valid; Prepare = prepare; Changed = changed; }
        internal int CloudID { get { return Status.State == "online" ? Status.Id : -1; } }
        internal void PutLiteData(string data) { lock (DataLock) LiteOverride = data; }
        internal void PutBigData(string data) { lock (DataLock) BigOverride = data; }
    }

    // Exactly one synchronous library step may be in flight, including across
    // reset/core changes. Stop never pretends to cancel an already sent request.
    internal sealed class CloudSessionRunner : IDisposable
    {
        private readonly Func<string, ICloudStepClient> factory;
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Thread worker;
        private readonly int normalDelayMs;
        private readonly Func<int, int, int> retryDelayMs;
        private volatile CloudSession desired;
        private volatile bool closing;
        private sealed class Transfer
        {
            internal CloudSession Session;
            internal bool Upload;
            internal string Local, Remote;
            internal Exception Error;
            internal readonly ManualResetEvent Done = new ManualResetEvent(false);
        }
        private readonly ConcurrentQueue<Transfer> transfers = new ConcurrentQueue<Transfer>();
        private readonly object submissionLock = new object();
        internal CloudSessionRunner(Func<string, ICloudStepClient> factory,
            int normalDelayMs = 1000, Func<int, int, int> retryDelayMs = null)
        {
            this.factory = factory;
            this.normalDelayMs = normalDelayMs;
            this.retryDelayMs = retryDelayMs ?? ((attempt, minimumSeconds) =>
                Math.Max(minimumSeconds * 1000, Math.Min(60000, 10000 << (attempt - 1))));
            worker = new Thread(Run) { IsBackground = true, Name = "PalTimer cloud session" };
            worker.Start();
        }
        internal CloudSession Start(string name, Func<bool> valid, Func<int, CloudPayload> prepare,
            Action<CloudSession, CloudStatus> changed)
        {
            var session = new CloudSession(name, valid, prepare, changed);
            var previous = desired;
            if (previous != null) previous.Cancelled = true;
            desired = session;
            wake.Set();
            return session;
        }
        internal void Stop(CloudSession session)
        {
            if (session == null) return;
            session.Cancelled = true;
            lock (session.DataLock) { session.LiteOverride = ""; session.BigOverride = ""; }
            wake.Set();
        }
        internal void FinishOne(CloudSession session)
        {
            if (session == null || session.Cancelled) return;
            Interlocked.Exchange(ref session.FinishRequested, 1);
            wake.Set();
        }
        internal void TransferFile(CloudSession session, bool upload, string local, string remote)
        {
            if (!Current(session) || session.CloudID < 0) throw new InvalidOperationException("云功能没有初始化");
            var request = new Transfer { Session = session, Upload = upload, Local = local, Remote = remote };
            lock (submissionLock)
            {
                if (!Current(session)) throw new OperationCanceledException("云会话已关闭");
                transfers.Enqueue(request);
                wake.Set();
            }
            request.Done.WaitOne();
            request.Done.Dispose();
            if (request.Error != null) throw request.Error;
        }
        private bool Current(CloudSession session)
        { return !closing && session != null && !session.Cancelled && ReferenceEquals(desired, session); }
        private void Publish(CloudSession session, CloudStatus status)
        {
            if (!Current(session)) return;
            var old = session.Status;
            session.Status = status;
            if (old.State == status.State && old.Message == status.Message && old.Id == status.Id) return;
            try { session.Changed(session, status); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { if (!closing) throw; }
        }
        private void Run()
        {
            CloudSession active = null;
            ICloudStepClient client = null;
            int failures = 0;
            bool terminal = false;
            try
            {
                while (!closing)
                {
                    wake.WaitOne(0);
                    var next = desired;
                    if (!ReferenceEquals(next, active) || !Current(active))
                    {
                        if (client != null) { client.Stop(); client = null; }
                        active = next; failures = 0; terminal = false;
                    }
                    Transfer request;
                    while (transfers.TryDequeue(out request))
                    {
                        try
                        {
                            if (!Current(request.Session) || !ReferenceEquals(active, request.Session) ||
                                client == null || !client.Initialized || !active.Valid())
                                throw new InvalidOperationException("游戏或跑次已改变，请重新执行云存档操作。");
                            if (request.Upload) client.Upload(request.Local, request.Remote);
                            else client.Download(request.Remote, request.Local);
                            if (!Current(request.Session) || !active.Valid())
                                throw new InvalidOperationException("请求完成时游戏或跑次已改变，未应用云存档。");
                        }
                        catch (Exception ex) { request.Error = ex; }
                        finally { request.Done.Set(); }
                    }
                    if (!Current(active)) { wake.WaitOne(normalDelayMs); continue; }
                    if (terminal) { wake.WaitOne(normalDelayMs); continue; }
                    int delay = normalDelayMs;
                    try
                    {
                        if (!active.Valid())
                        {
                            if (client != null) { client.Stop(); client = null; }
                            failures = 0;
                            Publish(active, new CloudStatus("waiting", "等待有效游戏身份"));
                            wake.WaitOne(normalDelayMs); continue;
                        }
                        if (client == null)
                        {
                            Publish(active, new CloudStatus("pending", "正在初始化"));
                            client = factory(active.CoreName);
                        }
                        bool wasInitialized = client.Initialized;
                        bool finish = wasInitialized && Volatile.Read(ref active.FinishRequested) != 0;
                        int dataKind = finish ? 2 : client.NextDataKind;
                        CloudPayload payload = wasInitialized ? active.Prepare(dataKind) : null;
                        if (!Current(active) || !active.Valid() || (client.Initialized && payload == null)) continue;
                        if (payload != null)
                        {
                            lock (active.DataLock)
                            {
                                if (active.LiteOverride != null) payload.Lite = active.LiteOverride;
                                if (active.BigOverride != null) payload.Big = active.BigOverride;
                            }
                        }
                        CloudStepResult result = client.Step(payload, finish);
                        if (!Current(active) || !active.Valid()) continue;
                        if (result.Initialized && result.DelaySeconds == 0)
                        {
                            // A successful re-authentication is not a successful
                            // send. Otherwise alternating init/send failures retry forever.
                            if (wasInitialized && dataKind >= 0 && dataKind <= 2) failures = 0;
                            Publish(active, new CloudStatus("online", "", result.Id));
                            if (finish)
                            {
                                Interlocked.Exchange(ref active.FinishRequested, 0);
                                client.Stop(); client = null;
                            }
                        }
                        else
                        {
                            ++failures;
                            terminal = failures >= 4;
                            string reason = string.IsNullOrWhiteSpace(result.Error) ? "云服务未完成验证" : result.Error;
                            Publish(active, new CloudStatus(terminal ? "failed" : "retry",
                                reason + (terminal ? "；自动重试已停止，可重新验证" : "；等待重试 " + failures + "/3")));
                            delay = retryDelayMs(failures, result.DelaySeconds);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Unsupported dependency/serialization/adapter failures
                        // must remain visible; do not create an unbounded loop.
                        terminal = true;
                        Publish(active, new CloudStatus("failed", ex.Message));
                    }
                    wake.WaitOne(delay);
                }
            }
            finally
            {
                if (client != null) client.Stop();
                lock (submissionLock)
                {
                    closing = true;
                    Transfer request;
                    while (transfers.TryDequeue(out request))
                    { request.Error = new OperationCanceledException("云会话已关闭"); request.Done.Set(); }
                }
            }
        }
        public void Dispose()
        {
            lock (submissionLock) { closing = true; if (desired != null) desired.Cancelled = true; wake.Set(); }
        }
        internal bool WaitForExit(int milliseconds) { return worker.Join(milliseconds); }
    }
}
