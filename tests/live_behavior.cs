using Pal98Timer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

[assembly: AssemblyVersion("3.37.7.11")]
internal static class LiveBehavior
{
    static int checks;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    static async Task Until(Func<bool> condition, string name) {
        var clock = Stopwatch.StartNew(); while (!condition() && clock.ElapsedMilliseconds < 9000) await Task.Delay(20);
        Check(condition(), name);
    }
    sealed class Auth : ICompetitionAuth, ICompetitionLiveAuth
    {
        internal int Proofs;
        internal int ProofDelayMilliseconds;
        internal bool Available = true;
        public CompetitionAuthIdentity Identity() => !Available ? null : new CompetitionAuthIdentity { protocol = CompetitionAuthProtocol.Name,
            key_id = new string('a',32), timer_exe_sha256 = new string('b',64), timer_version = "3.37.7.11", component_sha256 = new string('c',64) };
        public string ProveLive(string challenge) { Proofs++; if (ProofDelayMilliseconds > 0) Thread.Sleep(ProofDelayMilliseconds); return challenge; }
        public string Seal(string origin, string scope, string payload) => null;
        public string Prove(string seal, string challenge) => null;
    }
    sealed class Request
    {
        public long sequence { get; set; }
        public string session_id { get; set; }
        public CompetitionLiveObservation observation { get; set; }
        public CompetitionLiveCompare compare { get; set; }
    }
    sealed class Transport : ICompetitionTransport
    {
        internal bool Supported = true, Approved = true, WrongAck, WrongRank, WrongChallenge, MissingDate;
        internal int ServerClockOffset, ExpiryAdjustment;
        internal int Active, MaxActive, Updates, Gets, Sessions, Challenges, UpdateStatus = 200;
        internal volatile Request Last;
        internal TaskCompletionSource<bool> Gate, SessionGate;
        internal string Session = Guid.NewGuid().ToString("D"), Instance;
        static CompetitionHttpResult Reply(object value) => new CompetitionHttpResult { Status = 200, Body = CompetitionProtocol.Json().Serialize(value) };
        static CompetitionReply Board(string config = null, string route = null, string checkpoint = null, long? elapsed = null, string custom = null, string board = "overall") => new CompetitionReply {
            protocol = CompetitionProtocol.Online, scope = CompetitionProtocol.Scope, supported_run_protocols = new[] { CompetitionProtocol.Online },
            published = true, configuration_id = config, route_sha256 = route, custom_competition_id = custom, board = board,
            player = new CompetitionPlayer { bound = true, display_name = "实时测试玩家" }, title = "测试参考榜",
            node = checkpoint == null ? null : new CompetitionNode { checkpoint_id = checkpoint, elapsed_ms = elapsed,
                best_complete_line = new CompetitionComparison { rank = 2 }, personal_checkpoint_best = new CompetitionComparison { rank = 3 } },
            overall = new CompetitionOverall { personal_best = new CompetitionBest { rank = 5 } }
        };
        public async Task<CompetitionHttpResult> Send(string method, string url, string hwid, string secret, string body, int timeout, CancellationToken stop)
        {
            int active = Interlocked.Increment(ref Active); MaxActive = Math.Max(MaxActive, active);
            try {
                var json = CompetitionProtocol.Json(); string origin = new Uri(url).GetLeftPart(UriPartial.Authority);
                if (url.EndsWith("/devices")) return Reply(Board());
                if (url.Contains("/auth/status?")) return Reply(new { protocol = CompetitionAuthProtocol.Name, scope = CompetitionProtocol.Scope,
                    server_origin = origin, key_id = new string('a',32), timer_exe_sha256 = new string('b',64), approved = Approved,
                    live_protocol = Supported ? CompetitionClient.LiveProtocol : null });
                if (url.EndsWith("/live/challenges")) {
                    Challenges++; var input = json.Deserialize<CompetitionLiveChallenge>(body); Instance = input.instance_id;
                    var received = DateTimeOffset.UtcNow;
                    var reply = Reply(new CompetitionLiveChallenge { protocol = CompetitionClient.LiveProtocol, scope = CompetitionProtocol.Scope,
                        server_origin = origin, key_id = input.key_id, timer_exe_sha256 = input.timer_exe_sha256, hwid = hwid,
                        instance_id = WrongChallenge ? Guid.NewGuid().ToString("D") : input.instance_id, nonce = new string('d',64),
                        challenge_id = Guid.NewGuid().ToString("D"), expires_at = received.ToUnixTimeSeconds() + ServerClockOffset + 120 + ExpiryAdjustment });
                    reply.ReceivedAt = received;
                    reply.ServerDate = MissingDate ? (DateTimeOffset?)null : received.AddSeconds(ServerClockOffset);
                    return reply;
                }
                if (url.EndsWith("/live/sessions")) {
                    Sessions++; var sessionGate = SessionGate; if (sessionGate != null) await sessionGate.Task;
                    return Reply(new CompetitionLiveSession { protocol = CompetitionClient.LiveProtocol, scope = CompetitionProtocol.Scope,
                        server_origin = origin, public_id = "1042", session_id = Session, session_token = new string('e',64), heartbeat_seconds = 5, offline_seconds = 20 });
                }
                if (url.EndsWith("/live/updates")) {
                    var request = json.Deserialize<Request>(body); Last = request; Interlocked.Increment(ref Updates);
                    var gate = Gate; if (gate != null) await gate.Task;
                    if (UpdateStatus != 200) return new CompetitionHttpResult { Status = UpdateStatus };
                    var compare = request.compare;
                    return Reply(new CompetitionLiveAck { protocol = CompetitionClient.LiveProtocol, scope = CompetitionProtocol.Scope,
                        server_origin = origin, session_id = Session, public_id = "1042", sequence = request.sequence + (WrongAck ? 1 : 0), received_at = DateTimeOffset.UtcNow.ToString("o"),
                        standings = compare == null ? null : Board(compare.configuration_id, compare.route_sha256, WrongRank ? "previous-node" : compare.checkpoint_id, compare.elapsed_ms, compare.custom_competition_id, compare.board) });
                }
                if (method == "GET") {
                    Gets++; var query = HttpUtility.ParseQueryString(new Uri(url).Query);
                    long value; long? elapsed = long.TryParse(query["elapsed_ms"], out value) ? (long?)value : null;
                    return Reply(Board(query["configuration_id"],query["route_sha256"],query["checkpoint_id"],elapsed,query["custom_competition_id"],query["board"] ?? "overall"));
                }
                throw new Exception("unexpected endpoint");
            } finally { Interlocked.Decrement(ref Active); }
        }
        public void Dispose() { }
    }
    static CompetitionObservation Observe(string token, int step)
    {
        var game = new GameplayIdentity { rules_sha256 = new string('1',64), content_id = "fixture", content_sha256 = new string('2',64), family = "standard", fade_ms = 800, map_speed_ticks = 9 };
        var rules = new Dictionary<string,string> { ["content"] = game.content_sha256, ["family"] = game.family, ["fade_ms"] = "800", ["map_speed_ticks"] = "9" };
        return new CompetitionObservation { Token = token, Core = "PAL98DX9_AUTO", GameTitle = "98柔情原版 1.68－0.8秒&快走速&吴强&普通模式", Step = step,
            BeganHere = true, Finished = step == 2, TotalMilliseconds = step == 2 ? 8000 : 0, DllHash = new string('3',64), GameVersion = "1.6.8.17",
            FadeMilliseconds = 800, MapSpeedTicks = 9, ObservedAt = DateTimeOffset.UtcNow, Gameplay = game, Hardcore = new CompetitionHardcore(),
            Ranking = new RankingConfiguration { covered = true, rules = rules, configuration_id = RankingConfiguration.Digest(rules) }, ValidationError = "",
            Splits = new[] { new CompetitionSplit { checkpoint_id = "见石碑", elapsed_ms = step > 0 ? (long?)3000 : null, status = step > 0 ? "completed" : "in_progress" },
                new CompetitionSplit { checkpoint_id = "通关", elapsed_ms = step == 2 ? (long?)8000 : null, status = step == 2 ? "completed" : "in_progress" } } };
    }
    static CompetitionClient Client(string root, string name, Transport transport, Auth auth)
    {
        var client = new CompetitionClient(new CompetitionStorage(Path.Combine(root,name),() => "fixture-live-device-" + name), transport, auth);
        client.Configure(new CompetitionSettings { Enabled = true, Server = "https://fixture.invalid" });
        return client;
    }
    static async Task Run(string root)
    {
        long now = Stopwatch.GetTimestamp();
        var status = new CompetitionLiveConnection();
        Check(status.Caption(false,now) == "" && status.Caption(true,now) == " [连接中]", "disabled/first connection title");
        status = new CompetitionLiveConnection { Id = "1042", AckTick = now };
        Check(status.Caption(true,now) == " [已连接:1042]", "short ID in title only after ACK");
        Check(status.Caption(true,now+20*Stopwatch.Frequency) == " [连接失败]", "lease expires without worker/UI intervention");

        foreach (int offset in new[] { 182, -182 }) {
            var clockWire = new Transport { ServerClockOffset = offset };
            var clockAuth = new Auth(); var clockClient = Client(root,"clock-" + offset,clockWire,clockAuth);
            await Until(() => clockClient.ClockWarning != null, "clock mismatch has explicit warning: " + offset);
            var issue = clockClient.ClockWarning;
            Check(clockClient.LiveCaption == " [系统时间不同步]" && issue.Message.Contains((offset > 0 ? "慢" : "快") + "约 3分2秒") &&
                issue.Message.Contains("立即同步") && issue.Message.Contains("本地计时继续"), "clock direction, amount and recovery guidance: " + offset);
            Check(clockAuth.Proofs == 0 && clockWire.Sessions == 0, "clock diagnosis never bypasses expiry authentication: " + offset);
            int challenges = clockWire.Challenges;
            clockClient.Retry();
            await Until(() => clockWire.Challenges > challenges && clockClient.ClockWarning != null, "clock retry completes: " + offset);
            Check(clockClient.ClockWarning.Id == issue.Id, "retry keeps one warning episode: " + offset);
            clockWire.ServerClockOffset = 0;
            await Until(() => clockClient.LiveCaption == " [已连接:1042]", "clock correction automatically reconnects: " + offset);
            Check(clockClient.ClockWarning == null && clockAuth.Proofs == 1, "clock recovery clears warning after valid challenge: " + offset);
            await clockClient.CloseAsync();
        }
        var wrongClockAuth = new Auth();
        var wrongClockClient = Client(root, "clock-wrong-identity", new Transport { ServerClockOffset = 182, WrongChallenge = true }, wrongClockAuth);
        await Until(() => wrongClockClient.LiveCaption == " [连接失败]", "untrusted challenge remains an identity failure");
        Check(wrongClockClient.ClockWarning == null && wrongClockAuth.Proofs == 0, "untrusted challenge cannot trigger time diagnosis or proof");
        await wrongClockClient.CloseAsync();
        foreach (var fault in new[] {
            new Transport { ServerClockOffset = 182, MissingDate = true },
            new Transport { ExpiryAdjustment = 182 },
            new Transport { ExpiryAdjustment = -121 },
            new Transport { ServerClockOffset = 182, ExpiryAdjustment = 600 } }) {
            var faultAuth = new Auth(); var faultClient = Client(root,"clock-invalid-" + Guid.NewGuid().ToString("N"),fault,faultAuth);
            await Until(() => faultClient.LiveCaption == " [时间校验异常]", "invalid challenge time has an explicit warning even without Date");
            Check(faultClient.ClockWarning != null && !faultClient.ClockWarning.ConfirmedClockSkew && faultAuth.Proofs == 0 &&
                faultClient.ClockWarning.Message.Contains("暂时无法确认") && faultClient.ClockWarning.Message.Contains("本地计时继续"),
                "generic time warning does not invent a local clock diagnosis or bypass expiry");
            await faultClient.CloseAsync();
        }

        var boundaryWire = new Transport { ExpiryAdjustment = -119 };
        var boundaryAuth = new Auth { ProofDelayMilliseconds = 1300 };
        var boundaryClient = Client(root, "clock-proof-boundary", boundaryWire, boundaryAuth);
        await Until(() => boundaryClient.ClockWarning != null, "challenge expiring during native proof has a visible time warning");
        Check(boundaryAuth.Proofs == 1 && boundaryWire.Sessions == 0, "expired proof is not sent to the server");
        await boundaryClient.CloseAsync();

        var wire = new Transport(); var auth = new Auth(); var client = Client(root,"normal",wire,auth);
        await Until(() => client.LiveCaption == " [已连接:1042]", "real background session and heartbeat acknowledged");
        Check(wire.Last.observation.state == "waiting" && wire.Last.observation.run_id == null, "connected idle is not an invented run");
        int gets = wire.Gets;
        client.Invalidate("first"); client.Publish(Observe("first",0));
        client.PublishClock("first",1200,true,Stopwatch.GetTimestamp());
        await Until(() => wire.Last.observation.state == "running" && wire.Last.observation.elapsed_ms == 1200, "live elapsed advances between nodes");
        string firstRun = wire.Last.observation.run_id; long firstGeneration = wire.Last.observation.run_generation;
        client.Publish(Observe("first",1)); client.PublishClock("first",4500,true,Stopwatch.GetTimestamp());
        await Until(() => client.View.Reply?.node?.best_complete_line?.rank == 2, "node POST returns best-line and node ranks");
        Check(wire.Gets == gets && client.View.Reply.overall.personal_best.rank == 5, "same response refreshes overall PB without redundant GET");
        int proofs = auth.Proofs; long sequence = wire.Last.sequence;
        client.PublishClock("first",4500,false,Stopwatch.GetTimestamp()); client.Retry();
        await Until(() => wire.Last.sequence > sequence && wire.Last.observation.state == "paused", "pause reflected with frozen local time");
        Check(auth.Proofs == proofs && wire.Last.observation.run_id == firstRun, "heartbeat does not hash/sign host or create a new run");
        sequence = wire.Last.sequence;
        client.PublishClock("first",4600,true,Stopwatch.GetTimestamp()-5*Stopwatch.Frequency); client.Retry();
        await Until(() => wire.Last.sequence > sequence && wire.Last.observation.state == "invalid", "stale local observation is not advertised as running");
        Check(wire.Last.observation.elapsed_ms == 4600, "stale sample retains observed time instead of inventing elapsed");

        client.PublishClock("first",4700,true,Stopwatch.GetTimestamp());client.Retry();
        await Until(() => wire.Last.observation.state == "running" && wire.Last.observation.elapsed_ms == 4700, "fresh timing heals transient invalid state under the same run");
        Check(wire.Last.observation.run_id == firstRun,"invalid-to-running recovery does not invent a new run");

        wire.Gate = new TaskCompletionSource<bool>(); client.Retry();
        int updates = wire.Updates;
        await Until(() => wire.Updates > updates, "slow live POST held in background");
        var watch = Stopwatch.StartNew();
        for (int i=0;i<10000;i++) client.PublishClock("first",4500+i,true,Stopwatch.GetTimestamp());
        watch.Stop(); Console.WriteLine("PERF 10000 scalar clock publications=" + watch.ElapsedMilliseconds + "ms");
        Check(watch.ElapsedMilliseconds < 1000 && wire.MaxActive == 1, "network delay does not block timing or overlap requests");
        client.Invalidate("second"); client.Publish(Observe("second",0));
        var gate = wire.Gate; wire.Gate = null; gate.SetResult(true);
        await Until(() => wire.Last.observation.run_id != null && wire.Last.observation.run_id != firstRun, "Reset publishes a new run under the same connection");
        Check(wire.Last.observation.run_generation > firstGeneration && client.LiveCaption == " [已连接:1042]", "Reset generation increases and short ID remains");
        Check(client.View.NodeName != "见石碑", "previous run response cannot resurrect previous node");

        wire.Approved = false; wire.UpdateStatus = 403; client.Retry();
        await Until(() => client.LiveCaption == " [连接失败]", "revocation invalidates connection without waiting lease");
        Check(client.Enabled && client.ActivationText.Contains("未激活"), "revocation leaves local run and queries available");
        wire.Approved = true; wire.UpdateStatus = 200; client.Retry();
        await Until(() => client.LiveCaption == " [已连接:1042]", "approval/reconnect recovers same short ID");
        Check(wire.MaxActive == 1, "all HTTP stays serialized");
        await client.CloseAsync();

        wire = new Transport { SessionGate = new TaskCompletionSource<bool>() };
        client = Client(root,"early-start",wire,new Auth());
        await Until(() => wire.Sessions > 0,"connection handshake is held in the background");
        Check(client.Enabled && client.LiveCaption != " [已连接:1042]","local publication is available before connected confirmation");
        client.Invalidate("early-start");client.Publish(Observe("early-start",0));
        client.PublishClock("early-start",1800,true,Stopwatch.GetTimestamp());
        client.Publish(Observe("early-start",1));
        client.PublishClock("early-start",4500,true,Stopwatch.GetTimestamp());
        wire.SessionGate.SetResult(true);
        await Until(() => wire.Last?.observation.state == "running" && wire.Last.observation.elapsed_ms == 4500,"delayed connection receives the latest already-running state");
        Check(wire.Last.observation.current_checkpoint == "通关" && wire.Last.observation.splits[0].status == "completed",
            "starting and crossing a checkpoint before connected retains current progress");
        await client.CloseAsync();

        wire = new Transport { Supported = false }; client = Client(root,"old-server",wire,new Auth());
        await Until(() => client.LiveCaption == " [连接失败]", "old server has explicit connection feedback");
        Check(wire.Challenges == 0 && client.Enabled, "old server receives no unsupported calls and remains usable locally");
        await client.CloseAsync();
        wire = new Transport { WrongAck = true }; client = Client(root,"bad-ack",wire,new Auth());
        await Until(() => wire.Updates > 0 && client.LiveCaption == " [连接失败]", "wrong sequence ACK never claims connection");
        await client.CloseAsync();
        wire = new Transport { Approved = false }; client = Client(root,"unapproved",wire,new Auth());
        await Until(() => client.LiveCaption == " [连接失败]", "unapproved build gets clear feedback");
        Check(wire.Sessions == 0 && wire.Updates == 0, "unapproved build cannot open judge telemetry session");
        await client.CloseAsync();

        wire = new Transport();
        client = new CompetitionClient(new CompetitionStorage(Path.Combine(root,"restart"),()=>"fixture-live-restart"),wire,new Auth(),true,
            process => new CompetitionSettings { Enabled=true,Server="https://fixture.invalid" });
        client.ObserveGame(Process.GetCurrentProcess());
        await Until(() => client.LiveCaption == " [已连接:1042]", "game-managed live session connects");
        client.Publish(Observe("restart",0));client.PublishClock("restart",2400,true,Stopwatch.GetTimestamp());
        await Until(() => wire.Last.observation.state=="running", "game-managed run visible");
        int sessionCount=wire.Sessions; string restartRun=wire.Last.observation.run_id;
        client.ObserveGame(null);client.Retry();
        await Until(() => wire.Last.observation.state=="no_game", "PAL exit stops current gameplay display");
        client.ObserveGame(Process.GetCurrentProcess());
        await Until(() => client.Enabled,"PAL restart rereads settings");
        client.Publish(Observe("restart",0));client.PublishClock("restart",2500,true,Stopwatch.GetTimestamp());
        await Until(() => wire.Last.observation.state=="running", "PAL restart resumes live state");
        Check(wire.Last.observation.run_id==restartRun,"PAL reconnect preserves this run until an explicit Reset");
        Check(wire.Sessions==sessionCount&&client.LiveCaption==" [已连接:1042]","PAL restart keeps timer connection and short ID");
        await client.CloseAsync();

        // Reproduce the exact worker interleaving: PAL has closed, while the
        // previous finished observation has not yet been cleared by its command.
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var finished = Observe("closed-after-finish",2);
        typeof(CompetitionClient).GetField("current",flags).SetValue(client,finished);
        typeof(CompetitionClient).GetField("nextQuery",flags).SetValue(client,DateTime.MinValue);
        ((HashSet<string>)typeof(CompetitionClient).GetField("completed",flags).GetValue(client)).Add(finished.Token);
        var closingSnapshot = new CompetitionLiveObservation { state="no_game",run_id=Guid.NewGuid().ToString("D"),
            configuration_id=finished.Ranking.configuration_id,route_sha256=new string('f',64),splits=finished.Splits };
        var comparison = (CompetitionLiveCompare)typeof(CompetitionClient).GetMethod("LiveComparison",flags).Invoke(client,new object[]{closingSnapshot});
        Check(comparison != null && comparison.run_id == null,"closing PAL cannot send finished-run comparison with no_game snapshot");
        closingSnapshot.state="finished";
        comparison = (CompetitionLiveCompare)typeof(CompetitionClient).GetMethod("LiveComparison",flags).Invoke(client,new object[]{closingSnapshot});
        Check(comparison.run_id==closingSnapshot.run_id,"finished live snapshot retains uploaded-run overall comparison");
    }
    static int Main(string[] args) {
        try { Directory.CreateDirectory(args[0]); Run(args[0]).GetAwaiter().GetResult(); Console.WriteLine("PASS live total="+checks); return 0; }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
