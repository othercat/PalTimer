using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Pal98Timer
{
    // A single background loop owns disk/network state. Timing publishes values
    // through a short scalar-only lock, never waiting on disk, HTTP or the UI.
    internal sealed class CompetitionClient : IDisposable
    {
        private readonly CompetitionStorage storage;
        private readonly ICompetitionTransport transport;
        private readonly ICompetitionAuth authentication;
        private sealed class Approval { internal bool? Approved; internal DateTime NextCheck; }
        private readonly Dictionary<string, Approval> approvals = new Dictionary<string, Approval>(StringComparer.Ordinal);
        private volatile string activationText = "仅本地保存";
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly ConcurrentQueue<CompetitionObservation> completions = new ConcurrentQueue<CompetitionObservation>();
        private readonly ConcurrentQueue<Action> commands = new ConcurrentQueue<Action>();
        private readonly List<CompetitionPending> pending = new List<CompetitionPending>();
        private readonly HashSet<string> completed = new HashSet<string>(StringComparer.Ordinal);
        private readonly object publication = new object(); // scalar publication only, never IO/HTTP
        private CompetitionRunContext lease;
        private volatile CompetitionSettings settings = new CompetitionSettings();
        private volatile bool enabled;
        private bool closing; // guarded by publication
        private volatile CompetitionView view = new CompetitionView();
        private CompetitionObservation latest, current;
        private CompetitionCredential credential;
        private string activeToken = "", publishedStart = "", registeredKey = "", contextToken = "", contextRunId, route;
        private CompetitionSettings contextSettings;
        private DateTimeOffset started;
        private DateTime nextRegister, nextQuery;
        private bool registrationRejected, beganHere;
        private long generation, querySerial;
        private int publishedStep = int.MinValue, publishedFade, publishedSpeed;
        private readonly Task worker;
        private readonly bool gameManaged;
        private readonly Func<Process, CompetitionSettings> readGameSettings;
        private volatile Process observedGame;
        private long gameGeneration;
        private bool observedGameOnce;
        internal CompetitionClient(CompetitionStorage storage = null, ICompetitionTransport transport = null, ICompetitionAuth authentication = null, bool gameManaged = false, Func<Process, CompetitionSettings> readGameSettings = null)
        {
            this.storage = storage ?? new CompetitionStorage();
            this.transport = transport ?? new CompetitionHttpTransport();
            this.authentication = authentication ?? new NativeCompetitionAuth();
            this.gameManaged = gameManaged;
            this.readGameSettings = readGameSettings ?? CompetitionGameSettings.Load;
            worker = Task.Run((Func<Task>)Loop);
        }
        internal CompetitionSettings Settings { get { return settings.Copy(); } }
        internal CompetitionView View { get { return view; } }
        internal string DataDirectory { get { return storage.Root; } }
        internal bool Enabled { get { return enabled && (!gameManaged || observedGame != null); } }
        internal string ActivationText { get { return enabled ? activationText : "仅本地保存"; } }
        internal void ObserveGame(Process process)
        {
            if (!gameManaged || observedGameOnce && ReferenceEquals(observedGame, process)) return;
            observedGameOnce = true; observedGame = process;
            long epoch = Interlocked.Increment(ref gameGeneration);
            if (process == null) {
                // Completed records retain their authorized destination and may
                // finish/retry after PAL closes. No new run can publish meanwhile.
                Invalidate(activeToken); Interlocked.Increment(ref generation);
                commands.Enqueue(() => { if (epoch == Interlocked.Read(ref gameGeneration)) current = null; });
                return;
            }
            enabled = false; Interlocked.Increment(ref generation);
            commands.Enqueue(() => {
                if (epoch != Interlocked.Read(ref gameGeneration)) return;
                CompetitionSettings next;
                try { next = readGameSettings(process); }
                catch { next = new CompetitionSettings(); storage.LogNetwork("game-upload-settings-unavailable", 0); }
                if (epoch != Interlocked.Read(ref gameGeneration)) return;
                next.CopyAppearance(settings);
                ApplyConfiguration(next, null, epoch);
            });
        }
        internal void ConfigureAppearance(CompetitionSettings requested, string replacementSecret = null)
        {
            var copy = requested.Copy(); string error = copy.ValidateAppearance();
            if (error.Length != 0) throw new ArgumentException(error);
            if (replacementSecret != null && !CompetitionProtocol.Digest(replacementSecret)) throw new ArgumentException("设备凭据应为 64 位小写十六进制。");
            commands.Enqueue(() => {
                var next = settings.Copy(); next.CopyAppearance(copy);
                ApplyConfiguration(next, replacementSecret);
            });
        }
        internal void Invalidate(string token)
        {
            lock (publication)
            {
                Volatile.Write(ref activeToken, token ?? ""); lease = null;
                publishedStep = int.MinValue;
                Interlocked.Increment(ref querySerial);
                Interlocked.Exchange(ref latest, null);
                view = new CompetitionView { Status = settings.Enabled ? "等待本轮节点" : "比赛联机未开启", Hwid = view.Hwid, Pending = view.Pending };
            }
        }
        internal void Publish(CompetitionObservation observation)
        {
            if (observation == null || !Enabled) return;
            lock (publication)
            {
                if (!Enabled) return;
                // Only Reset/core binding advances the token. A late tick can never resurrect the old run.
                if (activeToken != "" && activeToken != observation.Token) return;
                if (activeToken == "") activeToken = observation.Token;
                if (lease == null)
                    lease = new CompetitionRunContext { Token = observation.Token, RunId = Guid.NewGuid().ToString("D"), Settings = settings.Copy(),
                        StartedAt = observation.ObservedAt, BeganHere = observation.BeganHere && observation.Step <= 0 && !observation.Finished,
                        Fade = observation.FadeMilliseconds, Speed = observation.MapSpeedTicks };
                if (observation.Step == 0 && !lease.Started)
                { lease = lease.Copy(); lease.Started = true; lease.StartedAt = observation.ObservedAt; }
                if (observation.Step <= 0 && lease.Fade == 0)
                { lease = lease.Copy(); lease.Fade = observation.FadeMilliseconds; lease.Speed = observation.MapSpeedTicks; }
                if (observation.Finished && lease.FinishedAt == default(DateTimeOffset))
                { lease = lease.Copy(); lease.FinishedAt = observation.ObservedAt; }
                observation.Context = lease;
                if (publishedStep != observation.Step || publishedFade != observation.FadeMilliseconds || publishedSpeed != observation.MapSpeedTicks)
                {
                    publishedStep = observation.Step; publishedFade = observation.FadeMilliseconds; publishedSpeed = observation.MapSpeedTicks;
                    var previous = view;
                    view = new CompetitionView { Hwid = previous.Hwid, Pending = previous.Pending, Status = "比赛实时参考（含预热）",
                        NodeName = observation.Step <= 0 ? "" : observation.Splits[Math.Min(observation.Step, observation.Splits.Length) - 1].checkpoint_id,
                        Reply = new CompetitionReply { track_id = CompetitionProtocol.TrackFor(publishedFade, publishedSpeed), player = previous.Reply == null ? null : previous.Reply.player } };
                }
                Interlocked.Increment(ref querySerial);
                Interlocked.Exchange(ref latest, observation);
                if (observation.Finished || observation.Step == 0 && Interlocked.Exchange(ref publishedStart, observation.Token) != observation.Token) completions.Enqueue(observation);
            }
        }
        internal void Configure(CompetitionSettings requested, string replacementSecret = null)
        {
            var copy = requested.Copy(); string error = copy.Validate();
            if (error.Length != 0) throw new ArgumentException(error);
            if (replacementSecret != null && !CompetitionProtocol.Digest(replacementSecret)) throw new ArgumentException("设备凭据应为 64 位小写十六进制。");
            // Pause publication while changing endpoint/credentials. Previously
            // accepted completions retain their immutable original destination.
            bool changingNetwork = settings.Server != copy.Server || settings.Event != copy.Event || settings.Ruleset != copy.Ruleset || settings.Enabled != copy.Enabled || replacementSecret != null;
            if (changingNetwork) enabled = false;
            Interlocked.Increment(ref generation);
            commands.Enqueue(() => ApplyConfiguration(copy, replacementSecret));
        }
        private void ApplyConfiguration(CompetitionSettings copy, string replacementSecret, long? gameEpoch = null)
        {
                bool networkChanged = settings.Server != copy.Server || settings.Event != copy.Event || settings.Ruleset != copy.Ruleset || settings.Enabled != copy.Enabled || replacementSecret != null;
                CompetitionCredential preparedCredential;
                List<CompetitionPending> preparedPending;
                try
                {
                    preparedCredential = networkChanged ? (copy.Enabled ? storage.Credential(copy.Server, replacementSecret) : null) : credential;
                    preparedPending = networkChanged ? storage.LoadPending(copy).ToList() : null;
                    var stored = copy.Copy();
                    if (gameManaged) { stored.Enabled = false; stored.Server = new CompetitionSettings().Server; stored.Event = "wuqiang"; stored.Ruleset = "wuqiang-2026-v1"; }
                    storage.SaveSettings(stored);
                }
                catch { enabled = false; SetStatus("比赛设置未能保存，联机已停止；请检查本机记录目录权限", true); return; }
                // Publish endpoint and credential together, only after all preparation succeeded.
                lock (publication)
                {
                    if (gameEpoch.HasValue && gameEpoch != Interlocked.Read(ref gameGeneration)) return;
                    settings = copy; credential = preparedCredential;
                    // A queued appearance save must not re-enable the previous
                    // game's endpoint while a new target is still being read.
                    enabled = copy.Enabled && !closing && (!gameManaged || gameEpoch.HasValue || enabled);
                    if (networkChanged)
                    {
                        lease = null; publishedStep = int.MinValue; Interlocked.Exchange(ref latest, null); Interlocked.Increment(ref querySerial);
                        view = new CompetitionView { Hwid = credential == null ? "" : credential.Hwid };
                    }
                }
                if (!networkChanged) return;
                pending.Clear(); pending.AddRange(preparedPending);
                registeredKey = ""; registrationRejected = false; nextRegister = DateTime.MinValue;
                approvals.Clear(); activationText = "仅本地保存";
                contextToken = ""; current = null; contextSettings = null; route = null; contextRunId = null; nextQuery = DateTime.MinValue;
                SetStatus(copy.Enabled ? "已启用；从下一轮完整计时开始自动上传" : "比赛联机未开启");
        }
        internal void Retry()
        {
            commands.Enqueue(() => {
                registrationRejected = false; nextRegister = DateTime.MinValue; registeredKey = ""; nextQuery = DateTime.MinValue;
                approvals.Clear();
                foreach (var record in pending.Where(p => !p.LocalRejected)) { record.Rejected = false; record.NextAttemptUtc = DateTime.MinValue; storage.SavePending(record); }
                storage.LogNetwork("manual-retry", 0);
            });
        }
        private void SetStatus(string status, bool stale = false, CompetitionReply reply = null, string node = null)
        {
            lock (publication)
            {
                var previous = view;
                view = new CompetitionView { Status = status, Hwid = credential == null ? previous.Hwid : credential.Hwid,
                    Pending = pending.Count, Stale = stale, Reply = reply ?? previous.Reply,
                    NodeName = node ?? previous.NodeName, ReceivedAt = reply == null ? previous.ReceivedAt : DateTimeOffset.Now };
            }
        }
        private void PublishResponse(long epoch, long serial, string token, string status, bool stale = false, CompetitionReply reply = null, string node = null)
        {
            // Validation and publication are atomic with Reset/core switching.
            lock (publication)
                if (epoch == Interlocked.Read(ref generation) && serial == Interlocked.Read(ref querySerial) && token == activeToken)
                    SetStatus(status, stale, reply, node);
        }
        private async Task Loop()
        {
            try
            {
                try
                {
                    settings = storage.LoadSettings();
                    if (gameManaged) { var local = settings; settings = new CompetitionSettings(); settings.CopyAppearance(local); }
                    lock (publication) enabled = settings.Enabled && !closing;
                    if (settings.Enabled) credential = storage.Credential(settings.Server);
                    pending.AddRange(storage.LoadPending(settings));
                    SetStatus(settings.Enabled ? "比赛实时参考（含预热）" : "比赛联机未开启");
                }
                catch { SetStatus("本机比赛设置或凭据不可读取；本地计时不受影响"); }
                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        CompetitionObservation finish;
                        while (completions.TryPeek(out finish)) { ProcessObservation(finish); completions.TryDequeue(out finish); }
                        Action command; while (commands.TryDequeue(out command)) command();
                        var update = Interlocked.Exchange(ref latest, null);
                        if (update != null) ProcessObservation(update);
                        if (enabled && settings.Enabled)
                        {
                            if (credential == null) credential = storage.Credential(settings.Server);
                            if (await EnsureRegistered().ConfigureAwait(false))
                            {
                                var identity = authentication.Identity();
                                if (identity != null) await Approved(settings, identity.key_id, identity.timer_exe_sha256, true).ConfigureAwait(false);
                                await UploadOne().ConfigureAwait(false);
                                await QueryLatest().ConfigureAwait(false);
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch { storage.LogNetwork("background-retry", 0); }
                    await Task.Delay(250, stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                // Cancellation aborts HTTP first; only local queued evidence is drained.
                DrainCompletions();
                transport.Dispose();
            }
        }
        private void ProcessObservation(CompetitionObservation observation)
        {
            string track = CompetitionProtocol.TrackFor(observation.FadeMilliseconds, observation.MapSpeedTicks);
            if (contextToken != observation.Token)
            {
                contextToken = observation.Token;
                route = CompetitionProtocol.RouteHash(observation.Splits.Select(s => CompetitionProtocol.CheckpointId(s.checkpoint_id)));
            }
            var bound = observation.Context;
            if (bound == null) return;
            contextRunId = bound.RunId; contextSettings = bound.Settings.Copy();
            contextSettings.Track = CompetitionProtocol.TrackFor(bound.Fade, bound.Speed) ?? "wuqiang";
            contextSettings.FadeMilliseconds = bound.Fade; contextSettings.MapSpeedTicks = bound.Speed;
            started = bound.StartedAt; beganHere = bound.BeganHere;
            current = observation;
            nextQuery = DateTime.MinValue;
            if (!observation.Finished || completed.Contains(observation.Token)) return;
            bool missingIdentity = !CompetitionProtocol.Digest(observation.DllHash) || string.IsNullOrEmpty(observation.GameVersion);
            var boundCredential = storage.Credential(contextSettings.Server);
            var run = new CompetitionRun {
                run_id = contextRunId, hwid = boundCredential.Hwid, track_id = contextSettings.Track, ruleset_id = contextSettings.Ruleset,
                route_sha256 = route, started_at = started.ToString("o"), finished_at = bound.FinishedAt.ToString("o"),
                total_ms = observation.TotalMilliseconds, timer_version = typeof(CompetitionClient).Assembly.GetName().Version.ToString(4),
                game_version = observation.GameVersion, pal_dll_sha256 = observation.DllHash,
                splits = observation.Splits.Select(s => new CompetitionSplit { checkpoint_id = CompetitionProtocol.CheckpointId(s.checkpoint_id), elapsed_ms = s.elapsed_ms, status = s.status }).ToArray()
            };
            string error = missingIdentity ? "通关记录已保存，等待本轮 DLL 身份核验后上传" : !beganHere ? "本轮在联机开启前已开始或为导入成绩，仅保留本地记录" :
                track == null || track != contextSettings.Track ? "本轮比赛速度发生变化，仅保留本地记录" :
                !string.IsNullOrEmpty(observation.ValidationError) ? "本轮不符合既有计时规则，仅保留本地记录" : CompetitionProtocol.Validate(run);
            var item = new CompetitionPending { Settings = contextSettings.Copy(), Run = run,
                Payload = CompetitionProtocol.Json().Serialize(run), Rejected = error.Length != 0, LocalRejected = error.Length != 0, LastStatus = error, Token = observation.Token };
            if (error.Length == 0)
            {
                // Seal only when this process first produces the completed record.
                // Loading an old outbox can never acquire a new producer identity.
                item.SealAttempted = true;
                string sealedJson = authentication.Seal(item.Settings.Server, item.Settings.Event, item.Payload);
                if (sealedJson != null)
                {
                    item.SealedRun = CompetitionAuthProtocol.Encode(sealedJson);
                    try { CompetitionAuthProtocol.ReadSeal(item.SealedRun, item.Settings, item.Run, item.Payload); }
                    catch { item.SealedRun = null; }
                }
                if (item.SealedRun == null) storage.LogNetwork("run-local-no-seal", 0);
            }
            storage.SavePending(item); // Commit the original bytes/run_id before first POST.
            pending.RemoveAll(p => p.Run.run_id == item.Run.run_id); pending.Add(item);
            if (!missingIdentity) completed.Add(observation.Token);
            SetStatus(error.Length == 0 ? "比赛实时参考（含预热）" : error);
        }
        private CompetitionReply Parse(CompetitionHttpResult response, CompetitionSettings expected)
        {
            if (!response.Success) return null;
            try
            {
                var result = CompetitionProtocol.Json().Deserialize<CompetitionReply>(response.Body);
                if (result != null && result.protocol == CompetitionProtocol.Name && result.@event == expected.Event &&
                    result.ruleset_id == expected.Ruleset && result.reference_only && result.includes_warmup) return result;
            }
            catch { }
            return null;
        }
        private async Task<bool> EnsureRegistered()
        {
            var cfg = settings; long epoch = Interlocked.Read(ref generation), serial = Interlocked.Read(ref querySerial);
            string token = Volatile.Read(ref activeToken);
            if (registeredKey == cfg.Server + "|" + cfg.Event) return true;
            if (registrationRejected || DateTime.UtcNow < nextRegister) return false;
            var response = await transport.Send("POST", cfg.Endpoint + "/devices", credential.Hwid, credential.Secret,
                CompetitionProtocol.Json().Serialize(new { protocol = CompetitionProtocol.Name, hwid = credential.Hwid }), 5000, stop.Token).ConfigureAwait(false);
            if (epoch != Interlocked.Read(ref generation)) return false;
            var reply = Parse(response, cfg);
            if (reply != null)
            { registeredKey = cfg.Server + "|" + cfg.Event; PublishResponse(epoch, serial, token, reply.player != null && reply.player.bound ? "比赛实时参考（含预热）" : "等待主办方绑定玩家名字", false, reply); return true; }
            registrationRejected = !response.Retryable && !response.Success;
            nextRegister = DateTime.UtcNow.AddSeconds(Math.Max(30, response.RetryAfterSeconds));
            storage.LogNetwork("device-register-retry", response.Status);
            return false;
        }
        private async Task<bool> Approved(CompetitionSettings cfg, string key, string exe, bool currentBuild)
        {
            string cacheKey = cfg.Server + "|" + cfg.Event + "|" + key + "|" + exe;
            Approval approval;
            if (!approvals.TryGetValue(cacheKey, out approval)) approvals[cacheKey] = approval = new Approval();
            if (DateTime.UtcNow >= approval.NextCheck)
            {
                long epoch = Interlocked.Read(ref generation);
                var response = await transport.Send("GET", cfg.Endpoint + "/auth/status?timer_exe_sha256=" + exe + "&key_id=" + key,
                    credential.Hwid, credential.Secret, null, 3000, stop.Token).ConfigureAwait(false);
                if (epoch != Interlocked.Read(ref generation)) return false;
                approval.NextCheck = DateTime.UtcNow.AddSeconds(Math.Max(30, response.RetryAfterSeconds));
                bool accepted = false;
                if (response.Success)
                {
                    try {
                        var status = CompetitionProtocol.Json().Deserialize<CompetitionAuthStatus>(response.Body);
                        if (status != null && status.protocol == CompetitionAuthProtocol.Name && status.@event == cfg.Event && status.server_origin == cfg.Server &&
                            status.key_id == key && status.timer_exe_sha256 == exe && status.approved.HasValue)
                        { approval.Approved = status.approved; approval.NextCheck = DateTime.UtcNow.AddMinutes(5); accepted = true; }
                    } catch { }
                }
                if (!accepted) storage.LogNetwork("activation-check-failed", response.Status);
            }
            if (currentBuild && approval.Approved.HasValue)
                activationText = approval.Approved.Value ? "比赛上传：已激活" : "未激活，仅本地保存";
            return approval.Approved == true;
        }
        private static bool AwaitingApproval(CompetitionHttpResult response)
        {
            if (response.Status != 403) return false;
            try {
                var data = CompetitionProtocol.Json().Deserialize<System.Collections.Generic.Dictionary<string, object>>(response.Body);
                object value;
                if (data != null && data.TryGetValue("code", out value) && Convert.ToString(value) == "build_not_approved") return true;
                object detail;
                var nested = data != null && data.TryGetValue("detail", out detail) ? detail as System.Collections.Generic.Dictionary<string, object> : null;
                return nested != null && nested.TryGetValue("code", out value) && Convert.ToString(value) == "build_not_approved";
            } catch { return false; }
        }
        private async Task UploadOne()
        {
            var cfg = settings;
            var item = pending.FirstOrDefault(p => !p.Rejected && p.Settings.Server == cfg.Server && p.Settings.Event == cfg.Event && p.Settings.Ruleset == cfg.Ruleset && p.NextAttemptUtc <= DateTime.UtcNow);
            if (item == null) return;
            if (string.IsNullOrEmpty(item.SealedRun))
            {
                item.LocalRejected = item.Rejected = true;
                item.LastStatus = "原记录没有签封，仅保留本地；升级或激活不能补签历史成绩";
                storage.SavePending(item); return;
            }
            string validation = CompetitionProtocol.Validate(item.Run);
            if (validation.Length != 0) { item.Rejected = true; item.LastStatus = validation; storage.SavePending(item); return; }
            CompetitionAuthSeal producer;
            try { producer = CompetitionAuthProtocol.ReadSeal(item.SealedRun, item.Settings, item.Run, item.Payload); }
            catch { item.LocalRejected = item.Rejected = true; item.LastStatus = "原始签封损坏，仅保留本地"; storage.SavePending(item); return; }
            var uploader = authentication.Identity();
            if (uploader == null) return;
            if (!await Approved(cfg, uploader.key_id, uploader.timer_exe_sha256, true).ConfigureAwait(false) ||
                !await Approved(cfg, producer.key_id, producer.timer_exe_sha256, false).ConfigureAwait(false))
            {
                // An unapproved older producer must not starve later records from
                // an approved build. No score body is sent while awaiting approval.
                item.NextAttemptUtc = DateTime.UtcNow.AddMinutes(5); storage.SavePending(item); return;
            }
            long epoch = Interlocked.Read(ref generation), serial = Interlocked.Read(ref querySerial);
            string sealedJson = CompetitionAuthProtocol.Decode(item.SealedRun);
            var challenge = await transport.Send("POST", item.Settings.Endpoint + "/auth/challenges", credential.Hwid, credential.Secret,
                CompetitionProtocol.Json().Serialize(new { protocol = CompetitionAuthProtocol.Name, hwid = credential.Hwid, key_id = uploader.key_id,
                    timer_exe_sha256 = uploader.timer_exe_sha256, request_sha256 = CompetitionProtocol.Hash(sealedJson) }), 5000, stop.Token).ConfigureAwait(false);
            if (epoch != Interlocked.Read(ref generation)) return;
            CompetitionHttpResult response = challenge;
            if (challenge.Success)
            {
                string proof = authentication.Prove(sealedJson, challenge.Body);
                if (proof == null)
                {
                    item.NextAttemptUtc = DateTime.UtcNow.AddMinutes(5); storage.SavePending(item); storage.LogNetwork("upload-proof-unavailable", 0); return;
                }
                string envelope = CompetitionProtocol.Json().Serialize(new { protocol = CompetitionAuthProtocol.Name, sealed_run = item.SealedRun, proof = CompetitionAuthProtocol.Encode(proof) });
                if (System.Text.Encoding.UTF8.GetByteCount(envelope) > 131072)
                { item.LocalRejected = item.Rejected = true; item.LastStatus = "签封成绩超过服务器容量，仅保留本地"; storage.SavePending(item); return; }
                response = await transport.Send("POST", item.Settings.Endpoint + "/runs", credential.Hwid, credential.Secret, envelope, 5000, stop.Token).ConfigureAwait(false);
            }
            var reply = Parse(response, item.Settings);
            if (reply != null && reply.receipt != null && reply.receipt.run_id == item.Run.run_id && reply.track_id == item.Run.track_id && reply.route_sha256 == item.Run.route_sha256)
            {
                storage.Receipt(item, reply); pending.Remove(item);
                PublishResponse(epoch, serial, item.Token, "比赛实时参考（含预热）", false, reply);
                return;
            }
            item.Attempts++;
            if (AwaitingApproval(response))
            {
                approvals.Clear(); item.NextAttemptUtc = DateTime.UtcNow.AddMinutes(5);
                item.LastStatus = "等待原生产构建和当前上传构建批准"; storage.SavePending(item);
                storage.LogNetwork("upload-awaiting-approval", response.Status, item.Attempts); return;
            }
            item.Rejected = !response.Success && !response.Retryable;
            int[] seconds = { 10, 30, 120, 300 };
            item.NextAttemptUtc = DateTime.UtcNow.AddSeconds(Math.Max(seconds[Math.Min(item.Attempts - 1, 3)], response.RetryAfterSeconds));
            item.LastStatus = response.Success ? "服务器响应无法核对，将使用原记录重试" : response.Description;
            storage.SavePending(item);
            storage.LogNetwork("run-upload-retry", response.Status, item.Attempts);
        }
        private async Task QueryLatest()
        {
            if (DateTime.UtcNow < nextQuery) return;
            var observation = current; var cfg = contextSettings ?? settings;
            // Never send the active endpoint's credential to an older run's server.
            if (cfg.Server != settings.Server || cfg.Event != settings.Event || cfg.Ruleset != settings.Ruleset) return;
            string token = Volatile.Read(ref activeToken);
            long epoch = Interlocked.Read(ref generation), serial = Interlocked.Read(ref querySerial);
            if (observation != null && observation.Token != token) return;
            var track = observation == null ? "wuqiang" : CompetitionProtocol.TrackFor(observation.FadeMilliseconds, observation.MapSpeedTicks);
            if (track == null) { SetStatus("当前速度不属于本次比赛的三条赛道；本地计时保持原规则"); return; }
            string url = cfg.Endpoint + "/standings?track_id=" + Uri.EscapeDataString(track);
            CompetitionSplit split = null;
            if (observation != null && observation.Step > 0 && route != null)
            {
                url += "&route_sha256=" + route;
                split = observation.Splits[Math.Min(observation.Step, observation.Splits.Length) - 1];
                if (split.status == "completed" && split.elapsed_ms.HasValue)
                    url += "&checkpoint_id=" + Uri.EscapeDataString(CompetitionProtocol.CheckpointId(split.checkpoint_id)) + "&elapsed_ms=" + split.elapsed_ms.Value.ToString(CultureInfo.InvariantCulture);
                if (observation.Finished && completed.Contains(observation.Token) && !pending.Any(p => p.Run.run_id == contextRunId)) url += "&run_id=" + contextRunId;
            }
            var response = await transport.Send("GET", url, credential.Hwid, credential.Secret, null, 3000, stop.Token).ConfigureAwait(false);
            nextQuery = DateTime.UtcNow.AddSeconds(Math.Max(30, response.RetryAfterSeconds));
            if (epoch != Interlocked.Read(ref generation) || serial != Interlocked.Read(ref querySerial) || token != Volatile.Read(ref activeToken)) return;
            var reply = Parse(response, cfg);
            if (reply == null || reply.track_id != track || split != null && reply.route_sha256 != route ||
                split != null && split.status == "completed" && (reply.node == null || reply.node.checkpoint_id != CompetitionProtocol.CheckpointId(split.checkpoint_id) || reply.node.elapsed_ms != split.elapsed_ms))
            { storage.LogNetwork("standings-query-failed", response.Status); return; }
            PublishResponse(epoch, serial, token, reply.player != null && reply.player.bound ? "实时参考（含预热） · " + CompetitionProtocol.TrackLabel(track) : "等待主办方绑定玩家名字", false, reply, split == null ? "" : split.checkpoint_id);
        }
        // Close never waits on HTTP. Let the already accepted disk work finish;
        // production UI closes only after this task drains local completions.
        private bool DrainCompletions()
        {
            CompetitionObservation finish;
            while (completions.TryPeek(out finish))
            {
                try { ProcessObservation(finish); completions.TryDequeue(out finish); }
                catch { SetStatus("比赛记录未能落盘，窗口已保留；修复本机目录权限后再退出", true); return false; }
            }
            return true;
        }
        internal async Task<bool> CloseAsync()
        {
            lock (publication) { closing = true; enabled = false; Interlocked.Increment(ref generation); }
            stop.Cancel();
            await worker.ConfigureAwait(false);
            return await Task.Run((Func<bool>)DrainCompletions).ConfigureAwait(false);
        }
        public void Dispose() { stop.Cancel(); }
    }
}
