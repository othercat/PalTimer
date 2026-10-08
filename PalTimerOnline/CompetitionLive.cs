using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Pal98Timer
{
    internal interface ICompetitionLiveAuth { string ProveLive(string challengeJson); }

    internal sealed class CompetitionClockWarning
    {
        internal long Id;
        internal string Message;
    }

    internal sealed class CompetitionLiveClock
    {
        internal string Token;
        internal long Elapsed, ObservedTick;
        internal bool Running;
    }

    internal sealed class CompetitionLiveConnection
    {
        internal string Id = "", Detail = "正在连接联机服务器";
        internal long AckTick;
        internal bool Failed;
        internal string Caption(bool enabled, long now)
        {
            if (!enabled) return "";
            if (AckTick > 0 && now >= AckTick && now - AckTick < Stopwatch.Frequency * 20)
                return " [已连接:" + Id + "]";
            return Failed || AckTick > 0 ? " [连接失败]" : " [连接中]";
        }
    }

    internal sealed class CompetitionLiveChallenge
    {
        public string protocol { get; set; }
        public string scope { get; set; }
        public string server_origin { get; set; }
        public string challenge_id { get; set; }
        public string nonce { get; set; }
        public long expires_at { get; set; }
        public string key_id { get; set; }
        public string timer_exe_sha256 { get; set; }
        public string hwid { get; set; }
        public string instance_id { get; set; }
    }

    internal sealed class CompetitionLiveSession
    {
        public string protocol { get; set; }
        public string scope { get; set; }
        public string server_origin { get; set; }
        public string session_id { get; set; }
        public string public_id { get; set; }
        public string session_token { get; set; }
        public int heartbeat_seconds { get; set; }
        public int offline_seconds { get; set; }
    }

    internal sealed class CompetitionLiveObservation
    {
        public string run_id { get; set; }
        public long run_generation { get; set; }
        public string state { get; set; }
        public string configuration_id { get; set; }
        public string custom_competition_id { get; set; }
        public string route_sha256 { get; set; }
        public string game_title { get; set; }
        public long elapsed_ms { get; set; }
        public string current_checkpoint { get; set; }
        public bool hardcore { get; set; }
        public CompetitionSplit[] splits { get; set; }
    }

    internal sealed class CompetitionLiveCompare
    {
        public string configuration_id { get; set; }
        public string custom_competition_id { get; set; }
        public string route_sha256 { get; set; }
        public string checkpoint_id { get; set; }
        public long? elapsed_ms { get; set; }
        public string run_id { get; set; }
        public string board { get; set; }
    }

    internal sealed class CompetitionLiveAck
    {
        public string protocol { get; set; }
        public string scope { get; set; }
        public string server_origin { get; set; }
        public string session_id { get; set; }
        public string public_id { get; set; }
        public long sequence { get; set; }
        public string received_at { get; set; }
        public CompetitionReply standings { get; set; }
    }

    internal sealed partial class CompetitionClient
    {
        internal const string LiveProtocol = "PAL98.TimerLive.v1";
        private readonly string liveInstanceId = Guid.NewGuid().ToString("D");
        private volatile CompetitionLiveConnection liveConnection = new CompetitionLiveConnection();
        private CompetitionLiveClock liveClock;
        private CompetitionLiveSession liveSession;
        private string serverLiveProtocol, liveObservedRunId;
        private long nextLiveDueTick, nextLiveAttemptTick;
        private volatile CompetitionClockWarning clockWarning;
        private long clockWarningSequence;
        private long liveSequence, liveGeneration, liveRevision, liveObservedRevision;
        private int liveFailures;

        internal CompetitionClockWarning ClockWarning { get { return enabled && settings.Enabled ? clockWarning : null; } }
        internal string LiveCaption { get { return ClockWarning != null ? " [系统时间不同步]" : liveConnection.Caption(enabled && settings.Enabled, Stopwatch.GetTimestamp()); } }
        internal string LiveDetail
        {
            get {
                if (!enabled || !settings.Enabled) return "联机未开启";
                var warning = ClockWarning;
                if (warning != null) return "[系统时间不同步] · " + warning.Message;
                var state = liveConnection;
                string caption = state.Caption(true, Stopwatch.GetTimestamp()).Trim();
                if (caption.StartsWith("[已连接:", StringComparison.Ordinal)) return caption + " · 实时进度已连接；通关成绩按原规则提交";
                return caption + (state.Id.Length == 0 ? "" : " · 联机 ID：" + state.Id) + " · " +
                    (state.AckTick > 0 && !state.Failed ? "心跳已过期，正在后台重连" : state.Detail);
            }
        }
        // No locks, file access, serialization or native calls on the timing thread.
        internal void PublishClock(string token, long elapsed, bool running, long observedTick)
        {
            if (!Enabled) return;
            Interlocked.Exchange(ref liveClock, new CompetitionLiveClock { Token = token, Elapsed = elapsed, Running = running, ObservedTick = observedTick });
        }
        private void InvalidateLive()
        { Interlocked.Increment(ref liveRevision); Interlocked.Exchange(ref liveClock, null); }
        private void ResetLiveConnection()
        {
            liveSession = null; serverLiveProtocol = null; liveFailures = 0;
            nextLiveDueTick = nextLiveAttemptTick = 0;
            clockWarning = null;
            liveConnection = new CompetitionLiveConnection();
        }
        private void RetryLiveConnection()
        {
            nextLiveDueTick = nextLiveAttemptTick = 0; liveFailures = 0;
            if (liveSession == null) liveConnection = new CompetitionLiveConnection { Id = liveConnection.Id };
        }
        private void LiveFailure(string detail, int status, bool force = false)
        {
            var old = liveConnection;
            liveConnection = new CompetitionLiveConnection { Id = old.Id, AckTick = force ? 0 : old.AckTick, Failed = true, Detail = detail };
        }
        private void LiveRetry(CompetitionHttpResult response, string detail)
        {
            liveFailures = Math.Min(8, liveFailures + 1);
            int delay = liveFailures == 1 ? 5 : liveFailures == 2 ? 10 : liveFailures == 3 ? 20 : liveFailures == 4 ? 30 : 60;
            int seconds = Math.Max(delay, response.RetryAfterSeconds);
            // System time can change while the player fixes a clock warning.
            // Retry and heartbeat delays must continue on a monotonic clock.
            nextLiveAttemptTick = Stopwatch.GetTimestamp() + Stopwatch.Frequency * Math.Min(86400, seconds);
            if (response.Status == 401 || response.Status == 403) {
                liveSession = null;
                // Revocation is authoritative; recheck approval instead of waiting five minutes.
                approvals.Clear();
            }
            LiveFailure(detail, response.Status, response.Status == 401 || response.Status == 403);
            storage.LogNetwork("live-retry", response.Status);
        }
        private static bool LiveUuid(string value)
        { Guid parsed; return value != null && Guid.TryParseExact(value, "D", out parsed) && parsed.ToString("D") == value; }
        private static string LiveText(string value, int limit)
        { return new string((value ?? "").Where(c => !char.IsControl(c)).Take(limit).ToArray()); }
        private bool LiveCurrent(long epoch, string server)
        { return enabled && settings.Enabled && epoch == Interlocked.Read(ref generation) && settings.Server == server && !stop.IsCancellationRequested; }
        private bool ExplainLiveClockFailure(CompetitionHttpResult response, CompetitionLiveChallenge challenge)
        {
            // HTTP Date is diagnostic only. Never use it to relax the local
            // expiry check or to generate an otherwise rejected proof.
            if (!response.ServerDate.HasValue || !response.ReceivedAt.HasValue) return false;
            long serverNow = response.ServerDate.Value.ToUnixTimeSeconds();
            if (challenge.expires_at <= serverNow || challenge.expires_at > serverNow + 180) return false;
            long offset = (long)Math.Round((response.ServerDate.Value - response.ReceivedAt.Value).TotalSeconds);
            if (Math.Abs(offset) < 30) return false;
            var duration = TimeSpan.FromSeconds(Math.Abs(offset));
            string amount = (duration.Days > 0 ? duration.Days + "天" : "") +
                (duration.Hours > 0 ? duration.Hours + "小时" : "") +
                (duration.Minutes > 0 ? duration.Minutes + "分" : "") + duration.Seconds + "秒";
            var previous = clockWarning;
            clockWarning = new CompetitionClockWarning { Id = previous == null ? ++clockWarningSequence : previous.Id,
                Message = "本机时间比服务器" + (offset > 0 ? "慢" : "快") + "约 " + amount + "，实时联机认证无法通过。\r\n" +
                    "请打开 Windows“日期和时间”，开启“自动设置时间”，点击“立即同步”。校时后会自动重连；本地计时继续。" };
            return true;
        }
        private async Task<bool> OpenLiveSession(CompetitionSettings cfg, CompetitionAuthIdentity identity, long epoch)
        {
            var auth = authentication as ICompetitionLiveAuth;
            if (auth == null) { LiveFailure("配套认证组件不支持实时联机，请一起更新计时器和认证组件", 0, true); return false; }
            var body = CompetitionProtocol.Json().Serialize(new { protocol = LiveProtocol, hwid = credential.Hwid,
                instance_id = liveInstanceId, key_id = identity.key_id, timer_exe_sha256 = identity.timer_exe_sha256 });
            var response = await transport.Send("POST", cfg.Endpoint + "/live/challenges", credential.Hwid, credential.Secret, body, 3000, stop.Token).ConfigureAwait(false);
            if (!LiveCurrent(epoch, cfg.Server)) return false;
            CompetitionLiveChallenge challenge = null;
            try { if (response.Success) challenge = CompetitionProtocol.Json().Deserialize<CompetitionLiveChallenge>(response.Body); } catch { }
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (challenge == null || challenge.protocol != LiveProtocol || challenge.scope != CompetitionProtocol.Scope || challenge.server_origin != cfg.Server ||
                challenge.hwid != credential.Hwid || challenge.key_id != identity.key_id || challenge.timer_exe_sha256 != identity.timer_exe_sha256 ||
                challenge.instance_id != liveInstanceId || !LiveUuid(challenge.challenge_id) || !CompetitionProtocol.Digest(challenge.nonce))
            { LiveRetry(response, "实时联机握手失败，正在后台重试"); return false; }
            if (challenge.expires_at <= now || challenge.expires_at > now + 180) {
                bool explained = ExplainLiveClockFailure(response, challenge);
                LiveRetry(response, explained ? clockWarning.Message : "实时联机握手已过期或时间无效，正在后台重试");
                return false;
            }
            clockWarning = null;
            string proof = auth.ProveLive(response.Body);
            if (proof == null) { LiveRetry(new CompetitionHttpResult(), "认证组件未通过实时联机校验，请确认配套文件完整"); return false; }
            body = CompetitionProtocol.Json().Serialize(new { protocol = LiveProtocol, proof_base64 = CompetitionAuthProtocol.Encode(proof) });
            response = await transport.Send("POST", cfg.Endpoint + "/live/sessions", credential.Hwid, credential.Secret, body, 3000, stop.Token).ConfigureAwait(false);
            if (!LiveCurrent(epoch, cfg.Server)) return false;
            CompetitionLiveSession session = null;
            try { if (response.Success) session = CompetitionProtocol.Json().Deserialize<CompetitionLiveSession>(response.Body); } catch { }
            if (session == null || session.protocol != LiveProtocol || session.scope != CompetitionProtocol.Scope || session.server_origin != cfg.Server ||
                !LiveUuid(session.session_id) || !CompetitionProtocol.Digest(session.session_token) || session.public_id == null ||
                !Regex.IsMatch(session.public_id, "^[1-9][0-9]{0,17}$") || session.heartbeat_seconds != 5 || session.offline_seconds != 20)
            { LiveRetry(response, "实时联机会话未建立，正在后台重试"); return false; }
            liveSession = session;
            liveConnection = new CompetitionLiveConnection { Id = session.public_id, Detail = "等待服务器确认本次心跳" };
            return true;
        }
        private CompetitionLiveObservation CaptureLiveObservation()
        {
            long revision = Interlocked.Read(ref liveRevision);
            var observation = current;
            string token = Volatile.Read(ref activeToken);
            if (observation != null && observation.Token != token) observation = null;
            string runId = observation?.Context?.RunId;
            if (revision != liveObservedRevision || runId != null && runId != liveObservedRunId) {
                liveObservedRevision = revision; liveObservedRunId = runId; liveGeneration++;
            }
            var clock = Volatile.Read(ref liveClock);
            bool hasClock = clock != null && observation != null && clock.Token == token;
            bool fresh = hasClock && Stopwatch.GetTimestamp() - clock.ObservedTick < Stopwatch.Frequency * 4;
            bool hasGame = !gameManaged || observedGame != null;
            string state = !hasGame ? "no_game" : observation == null ? "waiting" : observation.Finished ? "finished" :
                !string.IsNullOrEmpty(observation.ValidationError) || hasClock && !fresh ? "invalid" : fresh && clock.Running ? "running" :
                (hasClock ? clock.Elapsed : observation.TotalMilliseconds) > 0 ? "paused" : "waiting";
            return new CompetitionLiveObservation {
                run_id = runId, run_generation = liveGeneration, state = state,
                configuration_id = observation?.Ranking?.covered == true ? observation.Ranking.configuration_id : null,
                custom_competition_id = observation?.Context?.Settings.CustomCompetitionId,
                route_sha256 = observation == null ? null : route,
                game_title = LiveText(observation?.GameTitle ?? observation?.GameVersion, 240),
                elapsed_ms = Math.Max(0, Math.Min(86400000, hasClock ? clock.Elapsed : observation?.TotalMilliseconds ?? 0)),
                current_checkpoint = observation == null || observation.Step < 0 || observation.Splits.Length == 0 ? "" :
                    CompetitionProtocol.CheckpointId(observation.Splits[Math.Min(observation.Step, observation.Splits.Length - 1)].checkpoint_id),
                hardcore = observation?.Hardcore?.requested == true,
                splits = observation == null ? new CompetitionSplit[0] : observation.Splits.Select(s => new CompetitionSplit {
                    checkpoint_id = CompetitionProtocol.CheckpointId(s.checkpoint_id), elapsed_ms = s.elapsed_ms, status = s.status }).ToArray()
            };
        }
        private CompetitionLiveCompare LiveComparison(CompetitionLiveObservation snapshot)
        {
            if (DateTime.UtcNow < nextQuery || snapshot.configuration_id == null || snapshot.route_sha256 == null || current == null ||
                current.Ranking == null || !current.Ranking.Valid(current.Gameplay)) return null;
            CompetitionSplit split = current.Step <= 0 ? null : snapshot.splits[Math.Min(current.Step, snapshot.splits.Length) - 1];
            bool hasNode = split != null && split.status == "completed" && split.elapsed_ms.HasValue;
            return new CompetitionLiveCompare { configuration_id = snapshot.configuration_id, route_sha256 = snapshot.route_sha256,
                custom_competition_id = settings.ReferenceScope == "daily" ? null : snapshot.custom_competition_id,
                checkpoint_id = hasNode ? split.checkpoint_id : null, elapsed_ms = hasNode ? split.elapsed_ms : null,
                run_id = snapshot.state == "finished" && snapshot.run_id != null && current.Finished && completed.Contains(current.Token) &&
                    !pending.Any(p => p.Run.run_id == snapshot.run_id) ? snapshot.run_id : null,
                board = settings.ReferenceBoard };
        }
        private void AcceptLiveStandings(CompetitionReply reply, CompetitionLiveCompare compare, long epoch, long serial, string token)
        {
            if (reply == null || compare == null || reply.protocol != CompetitionProtocol.Online || reply.scope != CompetitionProtocol.Scope ||
                reply.configuration_id != compare.configuration_id || reply.route_sha256 != compare.route_sha256 ||
                reply.custom_competition_id != compare.custom_competition_id || (reply.board ?? "overall") != compare.board ||
                compare.checkpoint_id != null && (reply.node == null || reply.node.checkpoint_id != compare.checkpoint_id || reply.node.elapsed_ms != compare.elapsed_ms)) return;
            if (!reply.published) { reply.node = null; reply.overall = null; reply.hardcore_top = null; }
            PublishResponse(epoch, serial, token, reply.player?.bound == true ? "联机实时参考" : "尚未绑定玩家", false, reply, compare.checkpoint_id ?? "");
            nextQuery = DateTime.UtcNow.AddSeconds(30);
        }
        private async Task PumpLive(CompetitionAuthIdentity identity, bool approved)
        {
            if (Stopwatch.GetTimestamp() < nextLiveAttemptTick) return;
            if (identity == null) { LiveFailure("配套认证组件不可用；本地计时照常", 0, true); return; }
            if (!approved) { LiveFailure("当前构建尚未获服务器批准；本地计时和参考查询照常", 0, true); return; }
            if (serverLiveProtocol != LiveProtocol) { LiveFailure("服务器尚未支持实时联机，请管理员更新服务器", 0, true); return; }
            long epoch = Interlocked.Read(ref generation);
            var cfg = settings;
            if (liveSession == null && !await OpenLiveSession(cfg, identity, epoch).ConfigureAwait(false)) return;
            if (!LiveCurrent(epoch, cfg.Server) || Stopwatch.GetTimestamp() < nextLiveDueTick) return;
            long revision = Interlocked.Read(ref liveRevision), serial = Interlocked.Read(ref querySerial);
            string token = Volatile.Read(ref activeToken);
            var snapshot = CaptureLiveObservation(); var compare = LiveComparison(snapshot);
            var session = liveSession; long sequence = ++liveSequence;
            string body = CompetitionProtocol.Json().Serialize(new { protocol = LiveProtocol, hwid = credential.Hwid,
                session_id = session.session_id, session_token = session.session_token, sequence, observation = snapshot, compare });
            var response = await transport.Send("POST", cfg.Endpoint + "/live/updates", credential.Hwid, credential.Secret, body, 3000, stop.Token).ConfigureAwait(false);
            if (!LiveCurrent(epoch, cfg.Server)) return;
            CompetitionLiveAck ack = null;
            try { if (response.Success) ack = CompetitionProtocol.Json().Deserialize<CompetitionLiveAck>(response.Body); } catch { }
            if (ack == null || ack.protocol != LiveProtocol || ack.scope != CompetitionProtocol.Scope || ack.server_origin != cfg.Server ||
                ack.session_id != session.session_id || ack.public_id != session.public_id || ack.sequence != sequence)
            { LiveRetry(response, "实时心跳未获确认，正在后台重连"); return; }
            liveFailures = 0; nextLiveAttemptTick = 0;
            nextLiveDueTick = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
            liveConnection = new CompetitionLiveConnection { Id = session.public_id, AckTick = Stopwatch.GetTimestamp(), Detail = "已连接" };
            if (revision != Interlocked.Read(ref liveRevision)) { nextLiveDueTick = 0; return; }
            if (serial == Interlocked.Read(ref querySerial) && token == Volatile.Read(ref activeToken))
                AcceptLiveStandings(ack.standings, compare, epoch, serial, token);
        }
    }
}
