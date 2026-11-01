using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pal98.LocalDisplay;
using Wire = Pal98.Challenges.ChallengeRelayContract;

namespace Pal98Timer
{
    internal sealed class ChallengeFailure : Exception
    {
        internal readonly string Code; internal readonly int Retry;
        internal ChallengeFailure(string code, string message, int retry = 0) : base(message) {Code = code; Retry = retry;}
    }
    internal sealed class ChallengeService
    {
        private readonly string root, localAppData;
        private readonly CompetitionStorage storage;
        private readonly ICompetitionTransport transport;
        private readonly Func<DateTimeOffset> now;
        private CompetitionSettings settings;
        private CompetitionCredential credential;
        private Dictionary<string, object> state;
        private Dictionary<string, object> stateBeforeRequest;
        private string deviceId, stampPath;
        private bool OriginalEdition => File.Exists(Path.Combine(root,"PALOLD.dll")) && File.Exists(Path.Combine(root,"PAL.dll"));
        internal ChallengeService(string root, CompetitionStorage storage, ICompetitionTransport transport, Func<DateTimeOffset> now = null, string localAppData = null)
        {this.root = Path.GetFullPath(root); this.localAppData = localAppData; this.storage = storage; this.transport = transport; this.now = now ?? (() => DateTimeOffset.UtcNow);}
        private Dictionary<string, object> Reply(bool ok, string code, string detail, int retry = 0, Dictionary<string, object> activation = null)
        {
            // The identity cooldown is persisted independently even when its
            // request fails; retain it without publishing a partial catalog.
            object next;
            if (!ok && stateBeforeRequest != null && state != null && state.TryGetValue("next_identity_refresh_utc", out next))
                stateBeforeRequest["next_identity_refresh_utc"] = next;
            return new Dictionary<string, object> {{"protocol", Wire.BridgeProtocol}, {"ok", ok}, {"code", code}, {"detail", detail}, {"retry_after_seconds", retry}, {"state", ok ? state : stateBeforeRequest}, {"activation", activation}};
        }
        internal async Task<Dictionary<string, object>> Execute(string requestText, CancellationToken cancellation)
        {
            try {
                var request = Wire.Object(requestText); string action = Wire.String(request, "action");
                if (!new[] {"refresh", "refresh_identity", "submit_identity", "enroll", "activate"}.Contains(action)) throw new InvalidDataException("挑战操作无效。");
                if (!File.Exists(Path.Combine(root, "config.ini")) || !Directory.Exists(Path.Combine(root, "patches"))) throw new InvalidDataException("请选择完整游戏目录。");
                string settingsPath = Path.Combine(root, CompetitionGameSettings.PathInGame);
                settings = OriginalEdition ? CompetitionGameSettings.LoadDirectory(root,localAppData) :
                    File.Exists(settingsPath) ? CompetitionGameSettings.Parse(CompetitionStorage.ReadBounded(settingsPath, 1048576)) : new CompetitionSettings();
                string error = settings.Validate(); if (error.Length != 0) throw new InvalidDataException(error);
                string shared = Path.Combine(storage.Root, "challenges", CompetitionProtocol.Hash(settings.Server)); Directory.CreateDirectory(shared);
                using (var gate = await Gate(Path.Combine(shared, "request.lock"), cancellation).ConfigureAwait(false)) {
                    stampPath = Path.Combine(shared, "identity-refresh.json"); string cachePath = OriginalEdition ? Pal98.Storage.UserDataStore.ReadGamePath(root, Wire.CacheFile,localAppData) : Path.Combine(root, Wire.CacheFile);
                    if (File.Exists(cachePath)) {state = Wire.Object(CompetitionStorage.ReadBounded(cachePath, 8388608)); Wire.State(state); if (Wire.String(state, "server_origin") != settings.Server) state = null;}
                    state = state ?? Wire.NewState(settings.Server);
                    string before = Wire.Serialize(state);
                    stateBeforeRequest = System.Text.Encoding.UTF8.GetByteCount(before) <= 6 * 1048576 ? Wire.Object(before) : null;
                    if (File.Exists(stampPath)) state["next_identity_refresh_utc"] = ReadStamp();
                    if (action == "refresh_identity" && Wire.RefreshSeconds(state, now()) > 0) throw new ChallengeFailure("refresh_wait", "请等待倒计时结束后再刷新身份状态。", Wire.RefreshSeconds(state, now()));
                    credential = storage.Credential(settings.Server);
                    var registered = await Send("POST", "/devices", new {protocol = CompetitionProtocol.Online, hwid = credential.Hwid}, cancellation, CompetitionProtocol.Online).ConfigureAwait(false);
                    deviceId = Wire.String(registered, "device_id"); Wire.Id(deviceId);
                    var registeredPlayer = Wire.Player(Wire.Field(registered, "player")); state["player"] = registeredPlayer;
                    if (Wire.Boolean(registeredPlayer, "bound")) state["identity_status"] = "bound";
                    else if (Wire.String(state, "identity_status") == "bound") state["identity_status"] = "unbound";
                    Dictionary<string, object> activation = null;
                    if (action == "refresh" || action == "refresh_identity") {
                        if (action == "refresh_identity") await Identity(cancellation).ConfigureAwait(false);
                        await Catalog(cancellation).ConfigureAwait(false);
                        if (action == "refresh" && (Wire.RefreshSeconds(state, now()) == 0 || !state.ContainsKey("updated_at_utc") || state["updated_at_utc"] == null)) await Identity(cancellation).ConfigureAwait(false);
                        await Enrollments(cancellation).ConfigureAwait(false);
                    } else if (action == "submit_identity") {
                        string name = Wire.String(request, "display_name").Trim(); Wire.Text(name, 60);
                        ApplyIdentity(await Send("POST", "/challenges/identity", new {display_name = name}, cancellation).ConfigureAwait(false));
                    } else {
                        string id = Wire.String(request, "challenge_id"), player = Wire.String(request, "player_id"), name = Wire.String(request, "display_name");
                        Wire.Id(id); Wire.Id(player); Wire.Text(name, 60); int revision = Wire.Integer(request, "revision"); if (revision < 1) throw new InvalidDataException("挑战修订无效。");
                        // Forward interpreter-owned capability/version options
                        // without needing another timer release for each field.
                        // Routing remains local; credentials only enter headers.
                        var choice = request.Where(p => p.Key != "action" && p.Key != "challenge_id" &&
                            (action == "activate" || p.Key != "content_id" && p.Key != "cover_version")).ToDictionary(p => p.Key, p => p.Value);
                        if (action == "activate") {string content = Wire.String(request, "content_id"); Wire.ContentId(content); if (!choice.ContainsKey("cover_version")) choice["cover_version"] = 1;}
                        var reply = await Send("POST", "/challenges/" + id + "/" + action, choice, cancellation).ConfigureAwait(false);
                        var confirmed = Wire.Player(Wire.Field(reply, "player")); var challenge = Wire.Item(Wire.Field(reply, "challenge"));
                        if (!Wire.Boolean(confirmed, "bound") || Wire.String(confirmed, "id") != player || Wire.String(confirmed, "display_name") != name || Wire.String(challenge, "id") != id || Wire.Integer(challenge, "revision") != revision) throw new InvalidDataException("服务器确认身份或挑战与选择不一致。");
                        state["player"] = confirmed; state["identity_status"] = "bound"; state["requested_name"] = null;
                        if (action == "activate") {Wire.Activation(reply, deviceId, (string)choice["content_id"]); activation = reply;}
                        else await Enrollments(cancellation).ConfigureAwait(false);
                    }
                    state["updated_at_utc"] = now().ToString("o"); Wire.State(state);
                    string identityRoot = SaveCache(Wire.Serialize(state));
                    var currentPlayer = Wire.Player(Wire.Field(state, "player"));
                    if (Equals(Wire.Field(currentPlayer, "id"), Wire.Field(registeredPlayer, "id")) && Equals(Wire.Field(currentPlayer, "display_name"), Wire.Field(registeredPlayer, "display_name"))) {
                        var identity = LocalPlayerIdentity.Server(settings.Server, Wire.Field(currentPlayer, "id") as string, Wire.Field(currentPlayer, "display_name") as string, Wire.Boolean(currentPlayer, "bound"));
                        object features; bool required = registered.TryGetValue("supported_features", out features) && features is object[] list && list.Contains(IdentityBehaviorSnapshot.Feature);
                        byte[] behavior = IdentityBehaviorSnapshot.Build(registered, identity, Wire.Field(currentPlayer, "id") as string, required);
                        LocalPlayerIdentity.Save(identityRoot, identity, null, false, behavior: behavior);
                    }
                    return Reply(true, "ok", action == "enroll" ? "选择挑战成功" : "挑战信息已更新。", activation: activation);
                }
            } catch (ChallengeFailure error) {return Reply(false, error.Code, error.Message, error.Retry);}
            catch (OperationCanceledException) {return Reply(false, "cancelled", "挑战请求已超时，请刷新后重试。");}
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is ArgumentException || error is InvalidOperationException || error is System.Security.Cryptography.CryptographicException) {
                storage.LogNetwork("challenge-invalid-or-unavailable", 0); return Reply(false, "invalid_or_unavailable", "挑战信息或本机配置无法读取：" + BoundedDetail(error.Message));
            }
        }
        private string ReadStamp()
        {string value = Wire.String(Wire.Object(CompetitionStorage.ReadBounded(stampPath, 1024)), "next_identity_refresh_utc"); var next = Wire.Time(value); return (next > now().AddSeconds(60) ? now().AddSeconds(60) : next).ToString("o");}
        private async Task Identity(CancellationToken cancellation)
        {
            state["next_identity_refresh_utc"] = now().AddSeconds(60).ToString("o"); CompetitionStorage.WriteAtomic(stampPath, Wire.Json().Serialize(new {next_identity_refresh_utc = state["next_identity_refresh_utc"]}));
            ApplyIdentity(await Send("GET", "/challenges/identity", null, cancellation).ConfigureAwait(false));
        }
        private void ApplyIdentity(Dictionary<string, object> reply)
        {
            var player = Wire.Player(Wire.Field(reply, "player")); string status = Wire.String(reply, "status");
            if (!new[] {"bound", "unbound", "pending", "rejected", "approved"}.Contains(status) || Wire.Boolean(player, "bound") != (status == "bound")) throw new InvalidDataException("服务器身份状态无效。");
            state["player"] = player; state["identity_status"] = status; object name; state["requested_name"] = reply.TryGetValue("requested_name", out name) ? name : null;
            if (state["requested_name"] != null) {if (!(state["requested_name"] is string)) throw new InvalidDataException("身份名称字段无效。"); Wire.Text((string)state["requested_name"], 60);}
        }
        private async Task Catalog(CancellationToken cancellation)
        {
            var collected = new List<object>(); int offset = 0; Dictionary<string, object> metadata = null;
            do {
                var reply = await Send("GET", "/challenges/catalog?limit=100&offset=" + offset, null, cancellation).ConfigureAwait(false); var page = Wire.Items(Wire.Field(reply, "items"));
                if (page.Length > 100) throw new InvalidDataException("挑战目录分页无效。"); collected.AddRange(page); if (collected.Count > 5000) throw new InvalidDataException("挑战目录超过本地容量。");
                state["contents"] = Wire.Field(reply, "contents"); state["heroes"] = Wire.Field(reply, "heroes"); state["rules"] = Wire.Field(reply, "rules");
                // Keep future package/schema metadata without flattening it
                // into the cache's own identity or lifecycle fields.
                var pageMetadata = reply.Where(p => !new[] {"items", "next_offset", "offset", "limit", "protocol", "as_of"}.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
                if (metadata != null && !Wire.Same(metadata, pageMetadata)) throw new InvalidDataException("分页期间规则目录已改变，请重新刷新。");
                metadata = pageMetadata; state["catalog_metadata"] = metadata;
                object revision; state["rules_revision"] = reply.TryGetValue("rules_revision", out revision) ? revision : 0;
                object next = Wire.Field(reply, "next_offset"); if (next == null) break;
                if (!(next is int) || (int)next != offset + page.Length || page.Length == 0) throw new InvalidDataException("挑战目录分页未前进。"); offset = (int)next;
            } while (true);
            state["items"] = collected.ToArray(); CheckCacheCapacity();
        }
        private async Task Enrollments(CancellationToken cancellation)
        {
            var collected = new List<object>(); int offset = 0; Dictionary<string, object> metadata = null;
            do {
                var reply = await Send("GET", "/challenges/enrollments?paging=1&limit=100&offset=" + offset, null, cancellation).ConfigureAwait(false);
                var page = Wire.Items(Wire.Field(reply, "items"));
                collected.AddRange(page); if (collected.Count > 5000) throw new ChallengeFailure("challenge_capacity", "已报名挑战超过本地容量，原有下载配置保留。");
                var pageMetadata = reply.Where(p => !new[] {"items", "next_offset", "offset", "limit", "protocol", "as_of"}.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
                if (metadata != null && !Wire.Same(metadata, pageMetadata)) throw new InvalidDataException("分页期间已报名挑战已改变，请重新刷新。");
                metadata = pageMetadata;
                object next; if (!reply.TryGetValue("next_offset", out next) || next == null) break; // Explicit old-server adapter.
                if (!(next is int) || (int)next != offset + page.Length || page.Length == 0 || page.Length > 100) throw new InvalidDataException("已报名挑战分页未前进。");
                offset = (int)next;
            } while (true);
            state["enrollments"] = collected.ToArray(); CheckCacheCapacity();
        }
        private void CheckCacheCapacity()
        {
            try {
                // Reserve IPC space for an activation and envelope inside the
                // unchanged 8MiB transport limit. No per-rule interpretation.
                if (System.Text.Encoding.UTF8.GetByteCount(Wire.Serialize(state)) <= 6 * 1048576) return;
            } catch (InvalidDataException) { }
            throw new ChallengeFailure("challenge_capacity", "挑战目录超过本地容量，原有下载配置保留；请联系管理员减少目录内容。");
        }
        private async Task<Dictionary<string, object>> Send(string method, string path, object value, CancellationToken cancellation, string protocol = Wire.ServerProtocol)
        {
            var reply = await transport.Send(method, settings.Endpoint + path, credential.Hwid, credential.Secret, value == null ? null : Wire.Json().Serialize(value), 5000, cancellation).ConfigureAwait(false);
            if (!reply.Success) {
                if (reply.Status == 0 && !reply.ReceivedResponse) throw new ChallengeFailure("network_unavailable", "当前无法连接挑战服务器，已保存的挑战配置可以离线游玩。");
                string message = reply.Status == 0 ? "服务器响应无法读取或超过容量，请稍后重试。" : reply.Status == 401 ? "本机计时器设备身份未通过服务器验证，请检查身份凭据。" : reply.Status == 409 ? "身份、HWID或挑战版本不匹配，请刷新身份状态并重新选择挑战。" : reply.Status == 404 ? "服务器尚未提供此挑战，请更新或刷新挑战信息。" : reply.Status == 429 ? "操作过于频繁，请等待后重试。" : "服务器暂时不可用，已有本地配置保留。";
                string serverDetail = reply.ErrorDetail ?? "";
                throw new ChallengeFailure("http_" + reply.Status, message + "（HTTP " + reply.Status + "）" +
                    (serverDetail.Length == 0 ? "" : "\n服务器：" + serverDetail), reply.RetryAfterSeconds);
            }
            var result = Wire.Object(reply.Body); if (Wire.String(result, "protocol") != protocol) throw new InvalidDataException("服务器挑战协议不匹配。"); return result;
        }
        private static string BoundedDetail(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "未提供详细原因。";
            text = new string(text.Where(c => !char.IsControl(c) || c == '\n').ToArray());
            return text.Length <= 1024 ? text : text.Substring(0,1024) + "…";
        }
        private string SaveCache(string text)
        {
            if (OriginalEdition) {
                var store = new Pal98.Storage.UserDataStore(root,localAppData);
                if (store.PreferUserDirectory("game",root,new[] {"config.ini",Wire.CacheFile})) {
                    store.WriteText("game",Wire.CacheFile,text,Path.Combine(root,Wire.CacheFile));
                    return store.ComponentDirectory("game");
                }
            }
            CompetitionStorage.WriteAtomic(Path.Combine(root,Wire.CacheFile),text);
            return root;
        }
        private static async Task<FileStream> Gate(string path, CancellationToken token)
        {
            var elapsed = Stopwatch.StartNew();
            while (true) {token.ThrowIfCancellationRequested(); try {return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);} catch (IOException) {if (elapsed.ElapsedMilliseconds > 10000) throw new ChallengeFailure("busy", "计时器正在更新挑战，请稍后重试。");} await Task.Delay(100, token).ConfigureAwait(false);}
        }
    }
    internal static class ChallengeBridge
    {
        internal static int Execute(string root, string requestPath, string responsePath)
        {
            try {
                string request = CompetitionStorage.ReadBounded(requestPath, 16384);
                using (var transport = new CompetitionHttpTransport()) using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40))) {
                    var response = new ChallengeService(root, new CompetitionStorage(), transport).Execute(request, deadline.Token).GetAwaiter().GetResult(); CompetitionStorage.WriteAtomic(responsePath, Wire.Serialize(response)); return Wire.Boolean(response, "ok") ? 0 : 2;
                }
            } catch {return 1;}
        }
        internal static Task RefreshInstallation(string timerDirectory, CancellationToken token)
        {
            string root = Pal98.Storage.UserDataStore.FindInstallation(timerDirectory);
            if (!File.Exists(Path.Combine(root,"config.ini"))) root = Path.GetFullPath(Path.Combine(timerDirectory,"..",".."));
            if (!File.Exists(Path.Combine(root, "config.ini")) || !Directory.Exists(Path.Combine(root, "patches"))) return Task.CompletedTask;
            return Task.Run(async () => {
                try {
                    using (var transport = new CompetitionHttpTransport()) using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                        deadline.CancelAfter(40000); var result = await new ChallengeService(root, new CompetitionStorage(), transport).Execute("{\"action\":\"refresh\"}", deadline.Token).ConfigureAwait(false); if (!Wire.Boolean(result, "ok")) new CompetitionStorage().LogNetwork("challenge-startup-unavailable", 0);
                    }
                } catch {new CompetitionStorage().LogNetwork("challenge-startup-unavailable", 0);}
            }, token);
        }
    }
}
