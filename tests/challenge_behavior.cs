using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pal98Timer;
using Pal98.Challenges;

internal static partial class ChallengeBehavior
{
    static int checks;
    static readonly string author = "11111111-1111-4111-8111-111111111111", challengeId = "22222222-2222-4222-8222-222222222222";
    static readonly string playerId = "33333333-3333-4333-8333-333333333333", deviceId = "44444444-4444-4444-8444-444444444444";
    static void Check(bool value, string message) { if (!value) throw new Exception(message); ++checks; Console.WriteLine("PASS " + message); }
    static void Reject(Action call, string message) { try { call(); } catch (Exception) { Check(true, message); return; } throw new Exception(message); }
    // The real config tool is the v1 interpreter. Keep its frozen strict
    // contract as a consumer fixture, separate from the generic timer relay.
    static async Task<ChallengeBridgeReply> Consumer(Task<Dictionary<string, object>> pending)
    {
        var raw = await pending;
        try {
            var value = ChallengeContract.Read<ChallengeBridgeReply>(ChallengeContract.Json().Serialize(raw));
            if (value.ok) {value.state.Validate(); value.activation?.Validate();}
            return value;
        } catch (InvalidDataException) {return new ChallengeBridgeReply {ok = false, code = "invalid_or_unavailable", detail = "Consumer rejected unsupported game rules."};}
    }
    static ChallengeItem Item() => new ChallengeItem {id = challengeId, author_id = author, author_name = "作者", title = "坐牢三剑客挑战1", revision = 1, latest_revision = 1, approval_status = "approved",
        contents = new[] {"pal98-classic-v6"}, rules = new ChallengeRules {solo_art = true, true_duel = true}};
    sealed class Fake : ICompetitionTransport
    {
        internal readonly List<string> Calls = new List<string>();
        internal ChallengePlayer Player = new ChallengePlayer();
        internal string Status = "unbound", RequestedName;
        internal bool Enrolled, BadIni, InvalidBoolean, FailingIdentity;
        internal int RejectStatus;
        public void Dispose() { }
        public Task<CompetitionHttpResult> Send(string method, string url, string hwid, string secret, string body, int timeoutMilliseconds, CancellationToken stop)
        {
            Check(hwid == "challenge-fixture-device" && CompetitionProtocol.Digest(secret), "device credentials stay in transport");
            string path = new Uri(url).PathAndQuery; Calls.Add(method + " " + path);
            if (RejectStatus != 0) return Task.FromResult(new CompetitionHttpResult {Status = RejectStatus});
            var value = new Dictionary<string, object> {{"protocol", ChallengeContract.ServerProtocol}, {"as_of", "2026-10-29T00:00:00Z"}};
            if (path.EndsWith("/devices")) {
                value["protocol"] = CompetitionProtocol.Online; value["device_id"] = deviceId; value["player"] = Player;
            } else if (path.Contains("/catalog?")) {
                value["items"] = new[] {Item()}; value["next_offset"] = null; value["total"] = 1;
                value["contents"] = new Dictionary<string, string> {{"pal98-classic-v6", "98柔情经典速通v6"}};
                value["heroes"] = new Dictionary<string, string> {{"li-xiaoyao", "李逍遥"}};
                value["rules"] = ChallengeContract.RuleKeys.Select(key => new ChallengeRuleInfo {key = key, name = key, description = "规则说明"}).ToArray();
            } else if (path.EndsWith("/identity")) {
                if (FailingIdentity) return Task.FromResult(new CompetitionHttpResult {Status = 0});
                if (method == "POST") { RequestedName = (string)ChallengeContract.Object(body)["display_name"]; Status = "pending"; }
                value["status"] = Status; value["player"] = Player; value["requested_name"] = RequestedName;
                value["refresh_interval_seconds"] = 60;
            } else if (path.Split('?')[0].EndsWith("/enrollments")) value["items"] = Enrolled ? new[] {Item()} : new ChallengeItem[0];
            else if (path.EndsWith("/enroll") || path.EndsWith("/activate")) {
                if (!Player.bound) return Task.FromResult(new CompetitionHttpResult {Status = 409});
                var request = ChallengeContract.Object(body);
                if ((string)request["player_id"] != Player.id || (string)request["display_name"] != Player.display_name) return Task.FromResult(new CompetitionHttpResult {Status = 409});
                value["player"] = Player; value["challenge"] = Item();
                if (path.EndsWith("/enroll")) Enrolled = true;
                else {
                    value["device_id"] = deviceId; value["content_id"] = "pal98-classic-v6"; value["locked"] = true;
                    value["ini_filename"] = "challenge-rules.ini"; value["ini"] = BadIni ? "[Challenge]\nSoloArt=0\n" : Item().rules.Ini();
                }
            } else throw new Exception("Unexpected endpoint: " + path);
            string json = ChallengeContract.Json().Serialize(value);
            if (InvalidBoolean) json = json.Replace("\"solo_art\":true", "\"solo_art\":\"true\"");
            return Task.FromResult(new CompetitionHttpResult {Status = 200, Body = json});
        }
    }
    static async Task Cases(string output)
    {
        var advanced = new ChallengeRules {advanced_trade = true, rule_priorities = new Dictionary<string, int> {{"advanced_trade", 7}}};
        Check(ChallengeContract.RuleKeys.Length == 14 && !new ChallengeRules().advanced_trade &&
            advanced.Ini().Contains("AdvancedTrade=1\n") && advanced.Ini().Contains("PriorityAdvancedTrade=7\n"),
            "fourteenth advanced trade rule defaults off and roundtrips its priority");
        var activation = new ChallengeActivation {challenge = Item(), player = new ChallengePlayer {bound = true, id = playerId, display_name = "旧挑战玩家"},
            device_id = deviceId, content_id = "pal98-classic-v6", locked = true, ini_filename = ChallengeContract.IniFile};
        activation.challenge.rules.starting_supplies = true;
        activation.challenge.rules.rule_priorities["solo_art"] = 3;
        activation.ini = activation.challenge.rules.Ini(false, true);
        activation.Validate(); Check(true, "pre-advanced thirteen-rule downloads retain flags and priorities");
        activation.challenge.rules.advanced_trade = true;
        Reject(() => activation.Validate(), "missing advanced INI field cannot enable advanced trading");
        activation.challenge.rules = new ChallengeRules {solo_art = true};
        activation.ini = activation.challenge.rules.Ini(true);
        activation.Validate(); Check(true, "original six-rule downloads remain readable with advanced trading off");
        var oldCache = new ChallengeState {server_origin = "https://fixture.invalid", items = new[] {Item()},
            rules = ChallengeContract.RuleKeys.Take(13).Select(key => new ChallengeRuleInfo {key = key, name = key, description = "原规则"}).ToArray()};
        oldCache.Validate(); Check(true, "thirteen-rule cached directory remains valid while advanced trading is off");
        oldCache.items[0].rules.advanced_trade = true;
        Reject(() => oldCache.Validate(), "old cached directory cannot claim an unsupported advanced rule");
        Reject(() => ChallengeContract.Read<ChallengeRules>("{\"schema_version\":1,\"hero\":\"li-xiaoyao\",\"advanced_trade\":1}"),
            "advanced trading requires a strict boolean on the wire");
        string root = Path.Combine(output, "game"), personal = Path.Combine(output, "personal");
        Directory.CreateDirectory(root); Directory.CreateDirectory(Path.Combine(root, "patches")); File.WriteAllText(Path.Combine(root, "config.ini"), "[Game]\nPatch=pal98-classic-v6\n");
        var clock = DateTimeOffset.Parse("2026-10-29T00:00:00Z"); var fake = new Fake();
        var storage = new CompetitionStorage(personal, () => "challenge-fixture-device");
        Func<string, Task<ChallengeBridgeReply>> request = json => Consumer(new ChallengeService(root, storage, fake, () => clock).Execute(json, CancellationToken.None));
        var result = await request("{\"action\":\"refresh\"}");
        Check(result.ok && result.state.items.Length == 1 && result.state.identity_status == "unbound", "cold startup retrieves catalog and unbound identity");
        Check(result.state.RefreshSeconds(clock) == 60, "refresh starts visible 60 second cooldown");
        string cacheFile = Path.Combine(root, ChallengeContract.CacheFile), cache = File.ReadAllText(cacheFile);
        Check(!cache.Contains(storage.Credential("https://www.pallab.top").Secret) && !cache.Contains("challenge-fixture-device"), "cache contains no credential or HWID");
        int calls = fake.Calls.Count;
        result = await request("{\"action\":\"refresh_identity\"}");
        Check(!result.ok && result.code == "refresh_wait" && result.retry_after_seconds == 60 && fake.Calls.Count == calls, "new bridge process honors persisted refresh deadline without HTTP");
        clock = clock.AddSeconds(31);
        result = await request("{\"action\":\"refresh_identity\"}");
        Check(result.retry_after_seconds == 29, "countdown reports actual remaining seconds");
        result = await request("{\"action\":\"submit_identity\",\"display_name\":\"玩家申请\"}");
        Check(result.ok && result.state.identity_status == "pending" && result.state.requested_name == "玩家申请", "name-only request shows approval pending");
        fake.Player = new ChallengePlayer {bound = true, id = playerId, display_name = "玩家申请"}; fake.Status = "bound"; clock = clock.AddSeconds(30);
        result = await request("{\"action\":\"refresh_identity\"}");
        Check(result.ok && result.state.player.id == playerId, "approval refresh sees bound player");
        Check(Pal98.LocalDisplay.LocalPlayerIdentity.Load(root).Value.display_name == "玩家申请", "server identity updates existing timer display contract");
        var choice = new Dictionary<string, object> {{"action", "enroll"}, {"challenge_id", challengeId}, {"revision", 1}, {"player_id", playerId}, {"display_name", "玩家申请"}};
        result = await request(ChallengeContract.Json().Serialize(choice));
        Check(result.ok && result.state.enrollments.Length == 1 && result.detail == "选择挑战成功", "enrollment produces online patch catalog");
        choice["action"] = "activate"; choice["content_id"] = "pal98-classic-v6";
        result = await request(ChallengeContract.Json().Serialize(choice));
        Check(result.ok && result.activation.locked && result.activation.ini == Item().rules.Ini(), "activation checks exact rules and returns locked INI");
        Check(!File.Exists(Path.Combine(root, ChallengeContract.IniFile)), "timer never applies game configuration outside config transaction");
        fake.BadIni = true; string before = File.ReadAllText(cacheFile);
        result = await request(ChallengeContract.Json().Serialize(choice));
        Check(!result.ok && result.activation == null && File.ReadAllText(cacheFile) == before, "mismatched INI rejected without overwriting last good cache");
        fake.BadIni = false; choice["display_name"] = "另一个人";
        result = await request(ChallengeContract.Json().Serialize(choice));
        Check(!result.ok && result.code == "http_409", "identity mismatch never activates challenge");
        fake.RejectStatus = 401; result = await request("{\"action\":\"refresh\"}");
        Check(!result.ok && result.code == "http_401" && File.ReadAllText(cacheFile) == before, "invalid device credentials preserve cache");
        fake.RejectStatus = 0; fake.InvalidBoolean = true;
        result = await request("{\"action\":\"refresh\"}");
        Check(!result.ok && File.ReadAllText(cacheFile).Contains("\"solo_art\":\"true\"") && !File.Exists(Path.Combine(root, ChallengeContract.IniFile)),
            "relay preserves payload while the config consumer rejects a string boolean before activation");
        fake.InvalidBoolean = false;
        result = await request("{\"action\":\"refresh\"}");
        Check(result.ok, "valid server data restores the interpretable directory without changing game files");
        fake.InvalidBoolean = false; clock = clock.AddSeconds(61); fake.FailingIdentity = true;
        result = await request("{\"action\":\"refresh_identity\"}");
        Check(!result.ok && result.state.RefreshSeconds(clock) == 60, "failed refresh still has explicit cooldown");
        fake.FailingIdentity = false; result = await request("{\"action\":\"refresh_identity\"}");
        Check(!result.ok && result.code == "refresh_wait", "failure cooldown survives reopening bridge");
        Reject(() => ChallengeContract.Object("{\"action\":\"refresh\",\"action\":\"activate\"}"), "duplicate request fields rejected");
        Reject(() => ChallengeContract.Read<ChallengeRules>("{\"schema_version\":1,\"solo_art\":1}"), "numeric booleans rejected");
        Reject(() => ChallengeContract.ReadState(cache.Replace("\"bound\":false", "\"bound\":\"false\"")), "cache boolean coercion rejected");
        Reject(() => ChallengeContract.Object("{} trailing"), "trailing JSON rejected");
        Check(new ChallengeRules {chosen_hero = true, hero = "wuhou"}.Ini().Contains("Hero=wuhou\n"), "hero selection has fixed INI spelling");
    }
    static async Task<Dictionary<string, object>> HttpJson(HttpClient client, string method, string path, object body, int expected, string label)
    {
        using (var request = new HttpRequestMessage(new HttpMethod(method), path)) {
            if (body != null) request.Content = new StringContent(ChallengeContract.Json().Serialize(body), Encoding.UTF8, "application/json");
            using (var response = await client.SendAsync(request)) {
                Check((int)response.StatusCode == expected, label);
                return ChallengeContract.Object(await response.Content.ReadAsStringAsync());
            }
        }
    }
    static ChallengeItem HttpItem(Dictionary<string, object> response)
    {
        var item = ChallengeContract.Read<ChallengeItem>(ChallengeContract.Json().Serialize(response["challenge"])); item.Validate(); return item;
    }
    static void HttpRejected(ChallengeBridgeReply reply, string code, string cacheFile, string expectedCache, string label)
    {
        Check(!reply.ok && reply.code == code && reply.code != "network_unavailable" && reply.activation == null && File.ReadAllText(cacheFile) == expectedCache, label);
    }
    static async Task Integration(string output, string server)
    {
        var origin = new Uri(server);
        Check(origin.Scheme == "http" && origin.IsLoopback && origin.AbsolutePath == "/" && origin.Query.Length == 0 && origin.UserInfo.Length == 0, "HTTP integration accepts loopback fixture only");
        string root = Path.Combine(output, "http-game"); Directory.CreateDirectory(Path.Combine(root, "palmod")); Directory.CreateDirectory(Path.Combine(root, "patches"));
        File.WriteAllText(Path.Combine(root, "config.ini"), "[Patch]\nDefaultPatch=pal98-classic-v6\n");
        File.WriteAllText(Path.Combine(root, "palmod/common-tools.v1.json"), ChallengeContract.Json().Serialize(new {schema = "PAL98.ToolLaunchSettings.v1", competition_upload = new {schema = "PAL98.TimerOnlineSettings.v1", xiaorou = new {enabled = false, server = server}}}));
        var storage = new CompetitionStorage(Path.Combine(output, "http-personal"), () => "http-challenge-fixture");
        var clock = DateTimeOffset.UtcNow;
        string workshop = "/api/v1/timer/challenge-workshop", challenges = "/api/v1/timer/challenges", administration = "/api/v1/timer/admin";
        using (var transport = new CompetitionHttpTransport())
        using (var admin = new HttpClient(new HttpClientHandler {AllowAutoRedirect = false}) {BaseAddress = origin})
        using (var authorClient = new HttpClient(new HttpClientHandler {AllowAutoRedirect = false}) {BaseAddress = origin})
        using (var deviceClient = new HttpClient(new HttpClientHandler {AllowAutoRedirect = false, UseCookies = false}) {BaseAddress = origin}) {
            admin.DefaultRequestHeaders.Add("X-SGS-Admin", "1"); authorClient.DefaultRequestHeaders.Add("X-PAL-Challenge-Client", "1");
            await HttpJson(admin, "POST", "/api/v1/competitions/admin/login", new {password = "fixture-only-password"}, 200, "HTTP isolated admin session login");
            await HttpJson(admin, "POST", administration + "/challenge-authors", new {username = "fixture-author", password = "fixture-author-password"}, 201, "HTTP administrator creates disposable author");
            await HttpJson(authorClient, "POST", workshop + "/login", new {username = "fixture-author", password = "fixture-author-password"}, 200, "HTTP author has separate session");
            var rules = new ChallengeRules {solo_art = true, true_duel = true, starting_supplies = true, no_coop_magic = true, drop99 = true,
                steal_all = true, no_invisibility_item = true, invisibility_mana1 = true, silkworm_stats_only = true, advanced_trade = true,
                rule_priorities = new Dictionary<string, int> {{"steal_all", 9}, {"solo_art", 4}, {"advanced_trade", 7}}};
            var item = HttpItem(await HttpJson(authorClient, "POST", workshop + "/challenges", new {title = "HTTP第一版挑战", rules = rules}, 201, "HTTP owner creates revision 1 with fourteen-rule contract"));
            Func<object, Task<ChallengeBridgeReply>> invoke = value => Consumer(new ChallengeService(root, storage, transport, () => clock).Execute(ChallengeContract.Json().Serialize(value), CancellationToken.None));
            var reply = await invoke(new {action = "refresh"});
            Check(reply.ok && reply.state.items.Length == 1 && reply.state.rules.Length == 14 && reply.state.rules_revision == 1 && reply.state.identity_status == "unbound", "HTTP cold catalog and device registration");
            reply = await invoke(new {action = "submit_identity", display_name = "跨组件玩家"});
            Check(reply.ok && reply.state.identity_status == "pending", "HTTP global name application reaches pending");
            var pending = await HttpJson(admin, "GET", administration + "/binding-requests", null, 200, "HTTP admin reads global identity application");
            var request = (Dictionary<string, object>)((object[])pending["items"]).Single();
            await HttpJson(admin, "POST", administration + "/binding-requests/" + request["id"], new {approved = true, display_name = "跨组件玩家", reason = "fixture approval"}, 200, "HTTP global identity approval");
            clock = clock.AddSeconds(61); reply = await invoke(new {action = "refresh_identity"});
            Check(reply.ok && reply.state.player.bound && reply.state.player.display_name == "跨组件玩家", "HTTP identity refresh sees bound player");
            var player = reply.state.player;
            var credential = storage.Credential(server);
            deviceClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credential.Secret);
            deviceClient.DefaultRequestHeaders.Add("X-PAL-HWID", credential.Hwid);
            var choice = new Dictionary<string, object> {{"action", "enroll"}, {"challenge_id", item.id}, {"revision", 1}, {"player_id", player.id}, {"display_name", player.display_name}};
            Func<int, object> activation = revision => new {player_id = player.id, display_name = player.display_name, revision = revision, content_id = "pal98-classic-v6", cover_version = 1};
            reply = await invoke(choice);
            Check(reply.ok && reply.state.enrollments.Single().approval_status == "pending", "HTTP enrollment is pending despite globally bound identity");
            clock = clock.AddSeconds(61); reply = await invoke(new {action = "refresh_identity"});
            Check(reply.ok && !reply.state.enrollments.Single().Approved, "HTTP identity refresh does not approve challenge application");
            string cacheFile = Path.Combine(root, ChallengeContract.CacheFile), before = File.ReadAllText(cacheFile);
            choice["action"] = "activate"; choice["content_id"] = "pal98-classic-v6";
            HttpRejected(await invoke(choice), "http_409", cacheFile, before, "HTTP pending challenge cannot activate or become offline");
            var rejected = await HttpJson(deviceClient, "POST", challenges + "/" + item.id + "/activate", activation(1), 409, "HTTP pending activation status is 409");
            Check((string)rejected["code"] == "approval_pending", "HTTP pending rejection has explicit business code");
            var applicants = await HttpJson(admin, "GET", administration + "/challenge-approvals?challenge_id=" + item.id, null, 200, "HTTP admin selects challenge applicant");
            var applicant = (Dictionary<string, object>)((object[])applicants["items"]).Single();
            Check((string)applicant["player_id"] == player.id && (string)applicant["status"] == "pending", "HTTP application belongs to bound player identity");
            await HttpJson(admin, "POST", administration + "/challenge-approvals/" + item.id + "/" + player.id, new {status = "approved"}, 200, "HTTP administrator approves this challenge identity");
            Check(!ChallengeContract.ReadState(File.ReadAllText(cacheFile)).enrollments.Single().Approved, "HTTP approval does not silently replace client status");
            clock = clock.AddSeconds(61); reply = await invoke(new {action = "refresh_identity"});
            Check(reply.ok && reply.state.enrollments.Single().Approved, "HTTP explicit refresh observes challenge approval");
            reply = await invoke(choice);
            Check(reply.ok && reply.activation.locked && reply.activation.ini == item.rules.Ini() && reply.activation.challenge.Current && reply.activation.challenge.Approved, "HTTP approved latest revision activates with exact INI");
            string device = reply.activation.device_id;
            rules.true_duel = false;
            var second = HttpItem(await HttpJson(authorClient, "PUT", workshop + "/challenges/" + item.id, new {title = "HTTP第二版挑战", expected_revision = 1, rules = rules}, 200, "HTTP owner publishes revision 2"));
            before = File.ReadAllText(cacheFile);
            HttpRejected(await invoke(choice), "http_409", cacheFile, before, "HTTP edit immediately rejects revision 1 without changing local configuration");
            rejected = await HttpJson(deviceClient, "POST", challenges + "/" + item.id + "/activate", activation(1), 409, "HTTP stale revision status is 409");
            Check((string)rejected["code"] == "revision_outdated", "HTTP stale revision has explicit update-required code");
            reply = await invoke(new {action = "refresh"});
            var enrolled = reply.state.enrollments.Single();
            Check(reply.ok && enrolled.revision == 1 && enrolled.latest_revision == 2 && !enrolled.Current && enrolled.rules.true_duel && enrolled.Approved && enrolled.ToString().Contains("[有更新]"), "HTTP refresh exposes update while retaining old enrolled rules");
            Check(reply.state.items.Single().revision == 2 && !reply.state.items.Single().rules.true_duel, "HTTP catalog exposes explicit latest revision separately");
            choice["action"] = "enroll"; choice["revision"] = 2; reply = await invoke(choice);
            Check(reply.ok && reply.state.enrollments.Single().Current && reply.state.enrollments.Single().Approved, "HTTP confirmed update preserves same-player approval");
            choice["action"] = "activate"; reply = await invoke(choice);
            Check(reply.ok && reply.activation.challenge.revision == 2 && reply.activation.ini == second.rules.Ini() && !reply.activation.challenge.rules.true_duel, "HTTP explicitly updated revision 2 can activate");
            var other = HttpItem(await HttpJson(authorClient, "POST", workshop + "/challenges", new {title = "HTTP另一个旧挑战", rules = new ChallengeRules()}, 201, "HTTP second live challenge created before directory import"));
            var exported = await HttpJson(admin, "GET", administration + "/challenge-rules", null, 200, "HTTP administrator exports full rule directory");
            var directory = (Dictionary<string, object>)exported["catalog"];
            var firstRule = (Dictionary<string, object>)((object[])directory["rules"])[0]; firstRule["description"] = "HTTP上传后的规则说明"; firstRule["priority"] = 8;
            var imported = await HttpJson(admin, "PUT", administration + "/challenge-rules", directory, 200, "HTTP administrator uploads validated rule JSON");
            Check((int)imported["updated_challenges"] == 2 && (int)((Dictionary<string, object>)imported["catalog"])["revision"] == 2, "HTTP directory import revises every live challenge atomically");
            var latest = HttpItem(await HttpJson(admin, "GET", challenges + "/" + item.id, null, 200, "HTTP explicit latest challenge detail"));
            var otherLatest = HttpItem(await HttpJson(admin, "GET", challenges + "/" + other.id, null, 200, "HTTP other old challenge detail"));
            Check(latest.revision == 3 && otherLatest.revision == 2 && latest.rules_revision == 2 && otherLatest.rules_revision == 2, "HTTP all previous challenge revisions advanced after rule upload");
            before = File.ReadAllText(cacheFile);
            HttpRejected(await invoke(choice), "http_409", cacheFile, before, "HTTP rule upload immediately rejects previously current revision 2");
            rejected = await HttpJson(deviceClient, "POST", challenges + "/" + item.id + "/activate", activation(2), 409, "HTTP imported-rule stale activation status is 409");
            Check((string)rejected["code"] == "revision_outdated", "HTTP rule upload requires explicit revision update");
            clock = clock.AddSeconds(61); reply = await invoke(new {action = "refresh_identity"}); enrolled = reply.state.enrollments.Single();
            Check(reply.ok && reply.state.rules_revision == 2 && enrolled.revision == 2 && enrolled.latest_revision == 3 && !enrolled.Current && enrolled.Approved, "HTTP identity refresh shows pending version update without silent reenrollment");
            choice["action"] = "enroll"; choice["revision"] = 3; reply = await invoke(choice);
            Check(reply.ok && reply.state.enrollments.Single().Current && reply.state.enrollments.Single().Approved, "HTTP latest rules update keeps approved identity");
            choice["action"] = "activate"; reply = await invoke(choice);
            Check(reply.ok && reply.activation.challenge.revision == 3 && reply.activation.challenge.rules_revision == 2 && reply.activation.ini == latest.rules.Ini(), "HTTP latest revision activates after directory update");
            string verifiedIni = Path.Combine(output, "last-verified-rules.ini"); File.WriteAllText(verifiedIni, reply.activation.ini);
            string expectedIni = File.ReadAllText(verifiedIni); before = File.ReadAllText(cacheFile);
            Check(!File.Exists(Path.Combine(root, ChallengeContract.IniFile)), "HTTP timer does not bypass config tool transaction to apply rules");
            string fault = Path.Combine(output, "http-fault-mode.fixture");
            try {
                foreach (int status in new[] {401, 403, 404, 408, 409, 422, 429, 500, 503}) {
                    File.WriteAllText(fault, status.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    HttpRejected(await invoke(new {action = "refresh"}), "http_" + status, cacheFile, before, "HTTP " + status + " response never grants offline fallback");
                }
                foreach (string mode in new[] {"oversized", "invalid-utf8"}) {
                    File.WriteAllText(fault, mode);
                    HttpRejected(await invoke(new {action = "refresh"}), "http_0", cacheFile, before, "HTTP received " + mode + " response is not a network outage");
                }
                File.WriteAllText(fault, "invalid-json");
                HttpRejected(await invoke(new {action = "refresh"}), "invalid_or_unavailable", cacheFile, before, "HTTP malformed successful body is not a network outage");
            } finally { if (File.Exists(fault)) File.Delete(fault); }
            await HttpJson(admin, "POST", administration + "/devices/" + device + "/binding", new {unbind = true}, 200, "HTTP administrator unbinds fixture device");
            HttpRejected(await invoke(choice), "http_409", cacheFile, before, "HTTP changed global binding rejects prior challenge activation");
            File.WriteAllText(Path.Combine(output, "stop-http.fixture"), "stop isolated loopback service");
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!File.Exists(Path.Combine(output, "http-stopped.fixture")) && DateTime.UtcNow < deadline) await Task.Delay(25);
            Check(File.Exists(Path.Combine(output, "http-stopped.fixture")), "HTTP local service stopped and port closed before offline check");
            reply = await invoke(choice);
            Check(!reply.ok && reply.code == "network_unavailable" && reply.activation == null && reply.detail.Contains("离线") && File.ReadAllText(cacheFile) == before && File.ReadAllText(verifiedIni) == expectedIni,
                "HTTP no-response outage alone permits retaining verified offline configuration");
            var cached = ChallengeContract.ReadState(File.ReadAllText(cacheFile));
            Check(cached.enrollments.Single().Current && cached.enrollments.Single().Approved && cached.enrollments.Single().revision == 3, "HTTP outage preserves validated current challenge rather than inventing an activation");
        }
    }
    static int Main(string[] args)
    {
        try { if (args.Length > 1) Integration(args[0], args[1]).GetAwaiter().GetResult(); else { Cases(args[0]).GetAwaiter().GetResult(); OriginalCases(args[0]).GetAwaiter().GetResult(); } Console.WriteLine("PASS total=" + checks); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
