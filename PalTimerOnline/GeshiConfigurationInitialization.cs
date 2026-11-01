using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Pal98Timer
{
    // Explicit operator action, never called by ordinary timing, diagnostics or
    // live updates. The zero-time unfinished record can only initialize rules.
    internal static class GeshiConfigurationInitialization
    {
        internal const string Marker = "人工合成：仅用于隔世追凶-简单标准配置初始化；非真实通关；禁止加入任何成绩榜";
        internal const string Origin = "https://www.pallab.top";
        internal static CompetitionRun CreateMaintenance(SdlpalSnapshot snapshot, string hwid, string version,
            string[] checkpointIds, string id = null, string stamp = null)
        {
            if (snapshot == null || snapshot.schema != 1 || !snapshot.verified || snapshot.diagnostic ||
                !SdlpalBridge.SupportedProfile(snapshot.profile) || !CompetitionProtocol.Digest(snapshot.exeHash) ||
                snapshot.resourceHashes == null || !CompetitionProtocol.ReleaseRevision(snapshot.releaseRevision) ||
                checkpointIds == null || checkpointIds.Length == 0 || checkpointIds.Length > 64 ||
                checkpointIds.Any(p => !CompetitionProtocol.Identifier(p)) || checkpointIds.Distinct(StringComparer.Ordinal).Count() != checkpointIds.Length)
                throw new InvalidDataException("维护内容初始化需要有效的发行身份与默认节点");
            var facts = snapshot.RankingFacts();
            string digest = RankingConfiguration.Digest(facts);
            var game = new GameplayIdentity { family = SdlpalBridge.ProfileFamily(snapshot.profile), content_id = snapshot.profile,
                content_sha256 = facts["content"], rules_sha256 = digest, fade_ms = snapshot.fadeCycleMs,
                map_speed_ticks = 0, walk_frame_ms = snapshot.walkFrameMs, speed = snapshot.speed, battle_speed = snapshot.battleSpeed };
            var ranking = new RankingConfiguration { configuration_id = digest, covered = true, rules = facts };
            if (!ranking.Valid(game)) throw new InvalidDataException("初始化资源或加速配置不完整");
            string route = CompetitionProtocol.RouteHash(checkpointIds);
            stamp = stamp ?? DateTimeOffset.UtcNow.ToString("o");
            return new CompetitionRun {
                protocol = CompetitionProtocol.OnlineV2, run_id = id ?? Guid.NewGuid().ToString("D"), hwid = hwid,
                started_at = stamp, finished_at = stamp, total_ms = 0, restart_ms = 0, timer_version = version,
                game_version = "1.0.0.0", pal_dll_sha256 = snapshot.exeHash, release_revision = snapshot.releaseRevision,
                gameplay = game, ranking = ranking, hardcore = new CompetitionHardcore(),
                splits = checkpointIds.Select(p => new CompetitionSplit { checkpoint_id = p, status = "in_progress" }).ToArray(),
                route_sha256 = route, timeline_id = TimelineIdentity.Create(digest, route, false),
                observations = new CompetitionRunObservations { began_here = false, finished = false,
                    issues = new[] { "synthetic_configuration_initialization", "人工合成：" + SdlpalBridge.ProfileName(snapshot.profile) +
                        (snapshot.wuqiang ? "／吴强" : "／普通玩法") + "；仅配置初始化，非真实通关，禁止加入任何成绩榜" },
                    initial_gameplay = game.Copy(), initial_ranking = ranking.Copy(), initial_hardcore = new CompetitionHardcore() }
            };
        }
        internal static CompetitionRun Create(SdlpalSnapshot snapshot, string hwid, string version, string id = null, string stamp = null)
        {
            if (snapshot == null || snapshot.schema != 1 || !snapshot.verified || snapshot.profile != "geshi-easy-v1" ||
                !CompetitionProtocol.Digest(snapshot.exeHash) || snapshot.resourceHashes == null)
                throw new InvalidDataException("初始化需要真实资源身份快照");
            var facts = snapshot.RankingFacts(true);
            string digest = RankingConfiguration.Digest(facts);
            var game = new GameplayIdentity { family = "sdlpal-geshi-easy", content_id = snapshot.profile,
                content_sha256 = facts["content"], rules_sha256 = digest, fade_ms = snapshot.fadeCycleMs,
                map_speed_ticks = 0, walk_frame_ms = snapshot.walkFrameMs };
            var ranking = new RankingConfiguration { configuration_id = digest, covered = true, rules = facts };
            if (!ranking.Valid(game)) throw new InvalidDataException("初始化资源或加速配置不完整");
            var splits = new[] {
                new CompetitionSplit { checkpoint_id = "geshi-eight-headed-serpent", status = "in_progress" },
                new CompetitionSplit { checkpoint_id = "geshi-ending", status = "in_progress" }
            };
            string route = CompetitionProtocol.RouteHash(splits.Select(s => s.checkpoint_id));
            stamp = stamp ?? DateTimeOffset.UtcNow.ToString("o");
            return new CompetitionRun {
                protocol = CompetitionProtocol.OnlineV2, run_id = id ?? Guid.NewGuid().ToString("D"), hwid = hwid,
                started_at = stamp, finished_at = stamp, total_ms = 0, restart_ms = 0, timer_version = version,
                game_version = "1.0.0.0", pal_dll_sha256 = snapshot.exeHash,
                gameplay = game, ranking = ranking, hardcore = new CompetitionHardcore(), splits = splits,
                route_sha256 = route, timeline_id = TimelineIdentity.Create(digest, route, false),
                observations = new CompetitionRunObservations { began_here = false, finished = false, issues = new[] { Marker },
                    initial_gameplay = game.Copy(), initial_ranking = ranking.Copy(), initial_hardcore = new CompetitionHardcore() }
            };
        }

        public static int Execute(string snapshotFile, string outputDirectory, string[] checkpointIds = null)
        {
            try
            {
                var snapshot = SdlpalBridge.ReadSnapshotJson(CompetitionStorage.ReadBounded(snapshotFile, SdlpalBridge.Capacity));
                var storage = new CompetitionStorage();
                // This administrative action never creates/replaces credentials.
                if (!File.Exists(Path.Combine(storage.Root, CompetitionProtocol.Hash(Origin) + ".device")))
                    throw new InvalidDataException("没有现有设备凭据");
                var credential = storage.Credential(Origin);
                try
                {
                    using (var transport = new CompetitionHttpTransport())
                        return Upload(snapshot, outputDirectory, Origin, credential, new NativeCompetitionAuth(), transport, checkpointIds).GetAwaiter().GetResult();
                }
                finally { credential.Secret = null; }
            }
            catch (Exception ex)
            {
                // Exception messages can contain paths or echoed request data.
                WriteReport(outputDirectory, new { status = "failed", error_type = ex.GetType().Name, ranked = false });
                return 1;
            }
        }

        internal static async Task<int> Upload(SdlpalSnapshot snapshot, string directory, string origin,
            CompetitionCredential credential, ICompetitionAuth auth, ICompetitionTransport transport, string[] checkpointIds = null)
        {
            Directory.CreateDirectory(directory);
            using (var gate = new FileStream(Path.Combine(directory, "initialization.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                var identity = auth.Identity();
                if (identity == null || !identity.Valid) throw new InvalidDataException("签封组件与当前计时器不匹配");
                var cfg = new CompetitionSettings { Server = origin };
                if (cfg.Validate().Length != 0) throw new InvalidDataException("服务器地址无效");
                Func<string, string, CompetitionRun> create = (id, stamp) => checkpointIds == null
                    ? Create(snapshot, credential.Hwid, identity.timer_version, id, stamp)
                    : CreateMaintenance(snapshot, credential.Hwid, identity.timer_version, checkpointIds, id, stamp);
                string pendingPath = Path.Combine(directory, "initialization.pending.json");
                CompetitionPending item;
                if (File.Exists(pendingPath))
                {
                    item = CompetitionProtocol.Json().Deserialize<CompetitionPending>(CompetitionStorage.ReadBounded(pendingPath, 1048576));
                    if (item?.Run == null || item.Settings?.Server != origin) throw new InvalidDataException("原初始化记录无效");
                    // Retry is byte-for-byte, including timestamp and run id. No
                    // command can turn this pending record into a real finish.
                    var expected = create(item.Run.run_id, item.Run.started_at);
                    if (item.Payload != CompetitionProtocol.SerializeRun(expected) ||
                        item.Payload != CompetitionProtocol.SerializeRun(item.Run)) throw new InvalidDataException("初始化输入已经变化，请使用新的输出目录");
                    var seal = CompetitionAuthProtocol.ReadSeal(item.SealedRun, cfg, item.Run, item.Payload);
                    if (seal.key_id != identity.key_id || seal.timer_exe_sha256 != identity.timer_exe_sha256)
                        throw new InvalidDataException("请使用原签封计时器重试");
                }
                else
                {
                    var run = create(null, null);
                    if (CompetitionProtocol.Validate(run).Length != 0) throw new InvalidDataException("初始化记录格式无效");
                    string payload = CompetitionProtocol.SerializeRun(run);
                    string sealedJson = auth.Seal(origin, CompetitionProtocol.Scope, payload);
                    if (sealedJson == null) throw new InvalidDataException("初始化签封失败");
                    item = new CompetitionPending { Settings = cfg, Run = run, Payload = payload,
                        SealedRun = CompetitionAuthProtocol.Encode(sealedJson), SealAttempted = true };
                    CompetitionAuthProtocol.ReadSeal(item.SealedRun, cfg, run, payload);
                    CompetitionStorage.WriteAtomic(pendingPath, CompetitionProtocol.Json().Serialize(item));
                }
                string receiptPath = Path.Combine(directory, "initialization.receipt.json");
                if (File.Exists(receiptPath))
                {
                    var receipt = CompetitionProtocol.Json().Deserialize<InitializationReceipt>(CompetitionStorage.ReadBounded(receiptPath, 16384));
                    if (receipt?.run_id != item.Run.run_id || receipt.configuration_id != item.Run.ranking.configuration_id ||
                        receipt.daily_status != "pending_review" || receipt.board_eligible || !receipt.auth_verified)
                        throw new InvalidDataException("初始化回执不匹配");
                    WriteReport(directory, receipt); return 0;
                }
                Func<string, string, object, Task<CompetitionHttpResult>> send = (method, path, body) => transport.Send(method,
                    cfg.Endpoint + path, credential.Hwid, credential.Secret, body == null ? null : CompetitionProtocol.Json().Serialize(body), 10000, CancellationToken.None);
                var statusResponse = await send("GET", "/auth/status?timer_exe_sha256=" + identity.timer_exe_sha256 + "&key_id=" + identity.key_id, null).ConfigureAwait(false);
                var status = statusResponse.Success ? CompetitionProtocol.Json().Deserialize<CompetitionAuthStatus>(statusResponse.Body) : null;
                if (status?.protocol != CompetitionAuthProtocol.Name || status.scope != CompetitionProtocol.Scope || status.server_origin != origin ||
                    status.timer_exe_sha256 != identity.timer_exe_sha256 || status.key_id != identity.key_id || !status.approved.HasValue)
                {
                    WriteReport(directory, new { status = "approval_query_failed", http_status = statusResponse.Status, ranked = false }); return 2;
                }
                if (status.approved != true)
                {
                    var registration = (auth as ICompetitionBuildRegistration)?.Registration();
                    int candidateStatus = 0;
                    if (registration != null && registration.Matches(identity))
                        candidateStatus = (await send("POST", "/auth/candidates", new { hwid = credential.Hwid, release = registration }).ConfigureAwait(false)).Status;
                    WriteReport(directory, new { status = "awaiting_build_approval", candidate_http_status = candidateStatus,
                        timer_version = identity.timer_version, timer_exe_sha256 = identity.timer_exe_sha256, key_id = identity.key_id,
                        configuration_id = item.Run.ranking.configuration_id, run_id = item.Run.run_id, score_uploaded = false, ranked = false });
                    return 2;
                }
                if (item.Run.ranking.SpeedIndependent && status.supported_features?.Contains(CompetitionProtocol.ContentEffectsFeature) != true)
                {
                    WriteReport(directory, new { status = "awaiting_server_support", required_feature = CompetitionProtocol.ContentEffectsFeature,
                        configuration_id = item.Run.ranking.configuration_id, run_id = item.Run.run_id, score_uploaded = false, ranked = false });
                    return 2;
                }
                string original = CompetitionAuthProtocol.Decode(item.SealedRun);
                var challenge = await send("POST", "/auth/challenges", new { protocol = CompetitionAuthProtocol.Name,
                    hwid = credential.Hwid, key_id = identity.key_id, timer_exe_sha256 = identity.timer_exe_sha256,
                    request_sha256 = CompetitionProtocol.Hash(original) }).ConfigureAwait(false);
                string proof = challenge.Success ? auth.Prove(original, challenge.Body) : null;
                if (proof == null) { WriteReport(directory, new { status = "challenge_failed", http_status = challenge.Status, ranked = false }); return 2; }
                var response = await send("POST", "/runs", new { protocol = CompetitionAuthProtocol.Name,
                    sealed_run = item.SealedRun, proof = CompetitionAuthProtocol.Encode(proof) }).ConfigureAwait(false);
                var bodyFields = response.Success ? CompetitionProtocol.Json().Deserialize<Dictionary<string, object>>(response.Body) : null;
                var reply = response.Success ? CompetitionProtocol.Json().Deserialize<CompetitionReply>(response.Body) : null;
                Dictionary<string, object> receiptFields = null;
                if (bodyFields != null && bodyFields.TryGetValue("receipt", out var raw)) receiptFields = raw as Dictionary<string, object>;
                if (reply?.protocol != CompetitionProtocol.Online || reply.scope != CompetitionProtocol.Scope ||
                    reply.configuration_id != item.Run.ranking.configuration_id || reply.route_sha256 != item.Run.route_sha256 ||
                    reply.receipt?.run_id != item.Run.run_id || reply.receipt.daily_status != "pending_review" || receiptFields == null ||
                    !receiptFields.TryGetValue("auth_verified", out var verified) || !Equals(verified, true) ||
                    !receiptFields.TryGetValue("board_eligible", out var eligible) || !Equals(eligible, false))
                {
                    WriteReport(directory, new { status = "receipt_not_confirmed", http_status = response.Status,
                        run_id = item.Run.run_id, configuration_id = item.Run.ranking.configuration_id, ranked = false }); return 2;
                }
                var result = new InitializationReceipt { status = "configuration_initialized", http_status = response.Status,
                    run_id = item.Run.run_id, configuration_id = reply.configuration_id, receipt_id = reply.receipt.id,
                    daily_status = reply.receipt.daily_status, auth_verified = true, board_eligible = false,
                    timer_exe_sha256 = identity.timer_exe_sha256, timer_version = identity.timer_version };
                CompetitionStorage.WriteAtomic(receiptPath, CompetitionProtocol.Json().Serialize(result));
                WriteReport(directory, result); return 0;
            }
        }
        private static void WriteReport(string directory, object value) =>
            CompetitionStorage.WriteAtomic(Path.Combine(Path.GetFullPath(directory), "initialization.result.json"), CompetitionProtocol.Json().Serialize(value));
        private sealed class InitializationReceipt
        {
            public string status { get; set; }
            public int http_status { get; set; }
            public string run_id { get; set; }
            public string configuration_id { get; set; }
            public long receipt_id { get; set; }
            public string daily_status { get; set; }
            public bool auth_verified { get; set; }
            public bool board_eligible { get; set; }
            public string timer_exe_sha256 { get; set; }
            public string timer_version { get; set; }
        }
    }
}
