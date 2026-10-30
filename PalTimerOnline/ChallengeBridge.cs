using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pal98.Challenges;
using Pal98.LocalDisplay;

namespace Pal98Timer
{
    internal sealed class ChallengeFailure : Exception
    {
        internal readonly string Code;
        internal readonly int Retry;
        internal ChallengeFailure(string code, string message, int retry = 0) : base(message) { Code = code; Retry = retry; }
    }
    internal sealed class ChallengeService
    {
        private readonly string root;
        private readonly CompetitionStorage storage;
        private readonly ICompetitionTransport transport;
        private readonly Func<DateTimeOffset> now;
        private CompetitionSettings settings;
        private CompetitionCredential credential;
        private ChallengeState state;
        private string deviceId, stampPath;
        internal ChallengeService(string root, CompetitionStorage storage, ICompetitionTransport transport, Func<DateTimeOffset> now = null)
        { this.root = Path.GetFullPath(root); this.storage = storage; this.transport = transport; this.now = now ?? (() => DateTimeOffset.UtcNow); }

        internal async Task<ChallengeBridgeReply> Execute(string requestText, CancellationToken cancellation)
        {
            try {
                var request = ChallengeContract.Object(requestText);
                string action = Required(request, "action");
                if (!new[] {"refresh", "refresh_identity", "submit_identity", "enroll", "activate"}.Contains(action)) throw new InvalidDataException("挑战操作无效。");
                if (!File.Exists(Path.Combine(root, "config.ini")) || !Directory.Exists(Path.Combine(root, "patches"))) throw new InvalidDataException("请选择维护版游戏目录。");
                string settingsPath = Path.Combine(root, CompetitionGameSettings.PathInGame);
                settings = File.Exists(settingsPath) ? CompetitionGameSettings.Parse(CompetitionStorage.ReadBounded(settingsPath, 1048576)) : new CompetitionSettings();
                string error = settings.Validate(); if (error.Length != 0) throw new InvalidDataException(error);
                string key = CompetitionProtocol.Hash(settings.Server);
                string shared = Path.Combine(storage.Root, "challenges", key);
                Directory.CreateDirectory(shared);
                using (var gate = await Gate(Path.Combine(shared, "request.lock"), cancellation).ConfigureAwait(false)) {
                    stampPath = Path.Combine(shared, "identity-refresh.json");
                    string cachePath = Path.Combine(root, ChallengeContract.CacheFile);
                    if (File.Exists(cachePath)) {
                        state = ChallengeContract.ReadState(CompetitionStorage.ReadBounded(cachePath, 8388608));
                        if (state.server_origin != settings.Server) state = null;
                    }
                    state = state ?? new ChallengeState {server_origin = settings.Server};
                    if (File.Exists(stampPath)) state.next_identity_refresh_utc = ReadStamp();
                    if (action == "refresh_identity" && state.RefreshSeconds(now()) > 0)
                        throw new ChallengeFailure("refresh_wait", "请等待倒计时结束后再刷新身份状态。", state.RefreshSeconds(now()));
                    credential = storage.Credential(settings.Server);
                    var registered = await Send("POST", "/devices", new {protocol = CompetitionProtocol.Online, hwid = credential.Hwid}, cancellation, CompetitionProtocol.Online).ConfigureAwait(false);
                    deviceId = Required(registered, "device_id"); ChallengeContract.Id(deviceId);
                    var registeredPlayer = Convert<ChallengePlayer>(Field(registered, "player")); registeredPlayer.Validate();
                    state.player = registeredPlayer;
                    if (registeredPlayer.bound) state.identity_status = "bound";
                    else if (state.identity_status == "bound") state.identity_status = "unbound";
                    ChallengeActivation activation = null;
                    if (action == "refresh") {
                        await Catalog(cancellation).ConfigureAwait(false);
                        if (state.RefreshSeconds(now()) == 0 || state.updated_at_utc == null) await Identity(cancellation).ConfigureAwait(false);
                        await Enrollments(cancellation).ConfigureAwait(false);
                    } else if (action == "refresh_identity") {
                        await Identity(cancellation).ConfigureAwait(false);
                        await Catalog(cancellation).ConfigureAwait(false);
                        await Enrollments(cancellation).ConfigureAwait(false);
                    }
                    else if (action == "submit_identity") {
                        string name = Required(request, "display_name").Trim(); ChallengeContract.Text(name, 60);
                        ApplyIdentity(await Send("POST", "/challenges/identity", new {display_name = name}, cancellation).ConfigureAwait(false));
                    } else {
                        string id = Required(request, "challenge_id"), player = Required(request, "player_id"), name = Required(request, "display_name");
                        ChallengeContract.Id(id); ChallengeContract.Id(player); ChallengeContract.Text(name, 60);
                        int revision = Integer(request, "revision"); if (revision < 1) throw new InvalidDataException("挑战修订无效。");
                        var choice = new Dictionary<string, object> {{"player_id", player}, {"display_name", name}, {"revision", revision}};
                        if (action == "activate") { string content = Required(request, "content_id"); ChallengeContract.ContentId(content); choice["content_id"] = content; choice["cover_version"] = 1; }
                        var reply = await Send("POST", "/challenges/" + id + "/" + action, choice, cancellation).ConfigureAwait(false);
                        var confirmed = Convert<ChallengePlayer>(Field(reply, "player")); confirmed.Validate();
                        var challenge = Convert<ChallengeItem>(Field(reply, "challenge")); challenge.Validate();
                        if (!confirmed.bound || confirmed.id != player || confirmed.display_name != name || challenge.id != id || challenge.revision != revision)
                            throw new InvalidDataException("服务器确认身份或规则与选择不一致。");
                        state.player = confirmed; state.identity_status = "bound"; state.requested_name = null;
                        if (action == "activate") {
                            activation = Convert<ChallengeActivation>(reply); activation.Validate();
                            if (activation.device_id != deviceId || activation.content_id != (string)choice["content_id"] ||
                                !activation.challenge.Current || !activation.challenge.Approved) throw new InvalidDataException("挑战需要最新版本和管理员批准的身份。");
                        } else await Enrollments(cancellation).ConfigureAwait(false);
                    }
                    state.updated_at_utc = now().ToString("o"); state.Validate();
                    CompetitionStorage.WriteAtomic(cachePath, ChallengeContract.Json().Serialize(state));
                    // Refresh the same display identity contract used by the live
                    // timer, with matching behavior data from registration.
                    if (state.player.id == registeredPlayer.id && state.player.display_name == registeredPlayer.display_name) {
                        var identity = LocalPlayerIdentity.Server(settings.Server, state.player.id, state.player.display_name, state.player.bound);
                        object features;
                        bool required = registered.TryGetValue("supported_features", out features) && features is object[] list && list.Contains(IdentityBehaviorSnapshot.Feature);
                        byte[] behavior = IdentityBehaviorSnapshot.Build(registered, identity, state.player.id, required);
                        LocalPlayerIdentity.Save(root, identity, null, false, behavior: behavior);
                    }
                    return new ChallengeBridgeReply {ok = true, code = "ok", detail = action == "enroll" ? "选择挑战成功" : "挑战信息已更新。", state = state, activation = activation};
                }
            } catch (ChallengeFailure error) {
                return new ChallengeBridgeReply {ok = false, code = error.Code, detail = error.Message, retry_after_seconds = error.Retry, state = state};
            } catch (OperationCanceledException) {
                return new ChallengeBridgeReply {ok = false, code = "cancelled", detail = "挑战请求已超时，请刷新后重试。", state = state};
            } catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is ArgumentException || error is InvalidOperationException || error is System.Security.Cryptography.CryptographicException) {
                storage.LogNetwork("challenge-invalid-or-unavailable", 0);
                return new ChallengeBridgeReply {ok = false, code = "invalid_or_unavailable", detail = "挑战信息或本机配置无法读取，请检查配套版本后重试。", state = state};
            }
        }

        private string ReadStamp()
        {
            string text = CompetitionStorage.ReadBounded(stampPath, 1024);
            string value = Required(ChallengeContract.Object(text), "next_identity_refresh_utc");
            // Clock adjustments cannot lock the refresh button indefinitely.
            var next = ChallengeContract.Time(value);
            return (next > now().AddSeconds(60) ? now().AddSeconds(60) : next).ToString("o");
        }
        private async Task Identity(CancellationToken cancellation)
        {
            state.next_identity_refresh_utc = now().AddSeconds(60).ToString("o");
            CompetitionStorage.WriteAtomic(stampPath, ChallengeContract.Json().Serialize(new {next_identity_refresh_utc = state.next_identity_refresh_utc}));
            ApplyIdentity(await Send("GET", "/challenges/identity", null, cancellation).ConfigureAwait(false));
        }
        private void ApplyIdentity(Dictionary<string, object> reply)
        {
            var player = Convert<ChallengePlayer>(Field(reply, "player")); player.Validate();
            string status = Required(reply, "status");
            if (!new[] {"bound", "unbound", "pending", "rejected", "approved"}.Contains(status) || player.bound != (status == "bound")) throw new InvalidDataException("服务器身份状态无效。");
            state.player = player; state.identity_status = status;
            object name; state.requested_name = reply.TryGetValue("requested_name", out name) ? name as string : null;
            if (state.requested_name != null) ChallengeContract.Text(state.requested_name, 60);
        }
        private async Task Catalog(CancellationToken cancellation)
        {
            var collected = new List<ChallengeItem>(); int offset = 0;
            do {
                var reply = await Send("GET", "/challenges/catalog?limit=100&offset=" + offset, null, cancellation).ConfigureAwait(false);
                var page = Convert<ChallengeItem[]>(Field(reply, "items"));
                if (page == null || page.Length > 100) throw new InvalidDataException("挑战目录分页无效。");
                foreach (var item in page) item.Validate(); collected.AddRange(page);
                if (collected.Count > 5000) throw new InvalidDataException("挑战目录超过本地容量。");
                state.contents = Convert<Dictionary<string, string>>(Field(reply, "contents"));
                state.heroes = Convert<Dictionary<string, string>>(Field(reply, "heroes")); state.rules = Convert<ChallengeRuleInfo[]>(Field(reply, "rules"));
                object rulesRevision; state.rules_revision = reply.TryGetValue("rules_revision", out rulesRevision) && rulesRevision is int ? (int)rulesRevision : 0;
                object next = Field(reply, "next_offset");
                if (next == null) break;
                if (!(next is int) || (int)next != offset + page.Length || page.Length == 0) throw new InvalidDataException("挑战目录分页未前进。");
                offset = (int)next;
            } while (true);
            state.items = collected.ToArray();
        }
        private async Task Enrollments(CancellationToken cancellation)
        {
            var reply = await Send("GET", "/challenges/enrollments", null, cancellation).ConfigureAwait(false);
            var items = Convert<ChallengeItem[]>(Field(reply, "items"));
            if (items == null || items.Length > 5000) throw new InvalidDataException("已报名挑战无效。");
            foreach (var item in items) item.Validate(); state.enrollments = items;
        }
        private async Task<Dictionary<string, object>> Send(string method, string path, object value, CancellationToken cancellation, string protocol = ChallengeContract.ServerProtocol)
        {
            var reply = await transport.Send(method, settings.Endpoint + path, credential.Hwid, credential.Secret,
                value == null ? null : ChallengeContract.Json().Serialize(value), 5000, cancellation).ConfigureAwait(false);
            if (!reply.Success) {
                if (reply.Status == 0 && !reply.ReceivedResponse)
                    throw new ChallengeFailure("network_unavailable", "当前无法连接挑战服务器，已保存的挑战配置可以离线游玩。");
                string message = reply.Status == 401 ? "本机计时器设备身份未通过服务器验证，请检查身份凭据。" : reply.Status == 409 ? "身份、HWID或挑战版本不匹配，请刷新身份状态并重新选择挑战。"
                    : reply.Status == 404 ? "服务器尚未提供此挑战，请更新或刷新挑战信息。" : reply.Status == 429 ? "操作过于频繁，请等待后重试。" : "服务器暂时不可用，已有本地配置保留。";
                throw new ChallengeFailure("http_" + reply.Status, message, reply.RetryAfterSeconds);
            }
            var result = ChallengeContract.Object(reply.Body);
            if (Required(result, "protocol") != protocol) throw new InvalidDataException("服务器挑战协议不匹配。");
            return result;
        }
        private static T Convert<T>(object value) => ChallengeContract.Read<T>(ChallengeContract.Json().Serialize(value));
        private static object Field(Dictionary<string, object> value, string key) { object item; if (!value.TryGetValue(key, out item)) throw new InvalidDataException("挑战响应字段缺失。"); return item; }
        private static string Required(Dictionary<string, object> value, string key) { var item = Field(value, key) as string; if (string.IsNullOrEmpty(item)) throw new InvalidDataException("挑战文字字段缺失。"); return item; }
        private static int Integer(Dictionary<string, object> value, string key) { var item = Field(value, key); if (!(item is int)) throw new InvalidDataException("挑战整数字段无效。"); return (int)item; }
        private static async Task<FileStream> Gate(string path, CancellationToken token)
        {
            var elapsed = Stopwatch.StartNew();
            while (true) {
                token.ThrowIfCancellationRequested();
                try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { if (elapsed.ElapsedMilliseconds > 10000) throw new ChallengeFailure("busy", "计时器正在更新挑战，请稍后重试。"); }
                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }
    }
    internal static class ChallengeBridge
    {
        internal static int Execute(string root, string requestPath, string responsePath)
        {
            try {
                string request = CompetitionStorage.ReadBounded(requestPath, 16384);
                using (var transport = new CompetitionHttpTransport())
                using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40))) {
                    var response = new ChallengeService(root, new CompetitionStorage(), transport).Execute(request, deadline.Token).GetAwaiter().GetResult();
                    CompetitionStorage.WriteAtomic(responsePath, ChallengeContract.Json().Serialize(response));
                    return response.ok ? 0 : 2;
                }
            } catch { return 1; } // The config UI reports the missing reply; never put secrets in startup logs.
        }
        internal static Task RefreshInstallation(string timerDirectory, CancellationToken token)
        {
            string root = Path.GetFullPath(Path.Combine(timerDirectory, "..", ".."));
            if (!File.Exists(Path.Combine(root, "config.ini")) || !Directory.Exists(Path.Combine(root, "patches"))) return Task.CompletedTask;
            return Task.Run(async () => {
                try {
                    using (var transport = new CompetitionHttpTransport())
                    using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                        deadline.CancelAfter(40000);
                        var result = await new ChallengeService(root, new CompetitionStorage(), transport).Execute("{\"action\":\"refresh\"}", deadline.Token).ConfigureAwait(false);
                        if (!result.ok) new CompetitionStorage().LogNetwork("challenge-startup-unavailable", 0);
                    }
                } catch { new CompetitionStorage().LogNetwork("challenge-startup-unavailable", 0); }
            }, token);
        }
    }
}
