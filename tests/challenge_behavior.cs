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

internal static class ChallengeBehavior
{
    static int checks;
    static readonly string author = "11111111-1111-4111-8111-111111111111", challengeId = "22222222-2222-4222-8222-222222222222";
    static readonly string playerId = "33333333-3333-4333-8333-333333333333", deviceId = "44444444-4444-4444-8444-444444444444";
    static void Check(bool value, string message) { if (!value) throw new Exception(message); ++checks; Console.WriteLine("PASS " + message); }
    static void Reject(Action call, string message) { try { call(); } catch (Exception) { Check(true, message); return; } throw new Exception(message); }
    static ChallengeItem Item() => new ChallengeItem {id = challengeId, author_id = author, author_name = "作者", title = "坐牢三剑客挑战1", revision = 1, latest_revision = 1,
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
            } else if (path.EndsWith("/enrollments")) value["items"] = Enrolled ? new[] {Item()} : new ChallengeItem[0];
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
        string root = Path.Combine(output, "game"), personal = Path.Combine(output, "personal");
        Directory.CreateDirectory(root); Directory.CreateDirectory(Path.Combine(root, "patches")); File.WriteAllText(Path.Combine(root, "config.ini"), "[Game]\nPatch=pal98-classic-v6\n");
        var clock = DateTimeOffset.Parse("2026-10-29T00:00:00Z"); var fake = new Fake();
        var storage = new CompetitionStorage(personal, () => "challenge-fixture-device");
        Func<string, Task<ChallengeBridgeReply>> request = json => new ChallengeService(root, storage, fake, () => clock).Execute(json, CancellationToken.None);
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
        Check(!result.ok && File.ReadAllText(cacheFile) == before, "string boolean cannot become active rule");
        fake.InvalidBoolean = false; clock = clock.AddSeconds(61); fake.FailingIdentity = true;
        result = await request("{\"action\":\"refresh_identity\"}");
        Check(!result.ok && result.state.RefreshSeconds(clock) == 60, "failed refresh still has explicit cooldown");
        fake.FailingIdentity = false; result = await request("{\"action\":\"refresh_identity\"}");
        Check(!result.ok && result.code == "refresh_wait", "failure cooldown survives reopening bridge");
        Reject(() => ChallengeContract.Object("{\"action\":\"refresh\",\"action\":\"activate\"}"), "duplicate request fields rejected");
        Reject(() => ChallengeContract.Read<ChallengeRules>("{\"schema_version\":1,\"solo_art\":1}"), "numeric booleans rejected");
        Reject(() => ChallengeContract.ReadState(cache.Replace("\"bound\":false", "\"bound\":\"false\"")), "cache boolean coercion rejected");
        Reject(() => ChallengeContract.Object("{} trailing"), "trailing JSON rejected");
        Check(new ChallengeRules {chosen_hero = true, hero = "wuhou"}.Ini().EndsWith("Hero=wuhou\n"), "hero selection has fixed INI spelling");
    }
    static async Task Integration(string output, string server)
    {
        string root = Path.Combine(output, "http-game"); Directory.CreateDirectory(Path.Combine(root, "palmod")); Directory.CreateDirectory(Path.Combine(root, "patches"));
        File.WriteAllText(Path.Combine(root, "config.ini"), "[Patch]\nDefaultPatch=pal98-classic-v6\n");
        File.WriteAllText(Path.Combine(root, "palmod/common-tools.v1.json"), ChallengeContract.Json().Serialize(new {schema = "PAL98.ToolLaunchSettings.v1", competition_upload = new {schema = "PAL98.TimerOnlineSettings.v1", xiaorou = new {enabled = false, server = server}}}));
        var storage = new CompetitionStorage(Path.Combine(output, "http-personal"), () => "http-challenge-fixture");
        var clock = DateTimeOffset.UtcNow;
        using (var transport = new CompetitionHttpTransport())
        using (var admin = new HttpClient(new HttpClientHandler {AllowAutoRedirect = false}) {BaseAddress = new Uri(server)}) {
            Func<object, Task<ChallengeBridgeReply>> invoke = value => new ChallengeService(root, storage, transport, () => clock).Execute(ChallengeContract.Json().Serialize(value), CancellationToken.None);
            var reply = await invoke(new {action = "refresh"});
            Check(reply.ok && reply.state.items.Length == 1 && reply.state.identity_status == "unbound", "HTTP cold catalog and device registration");
            var item = reply.state.items[0];
            reply = await invoke(new {action = "submit_identity", display_name = "跨组件玩家"});
            Check(reply.ok && reply.state.identity_status == "pending", "HTTP name-only application reaches pending approval");
            admin.DefaultRequestHeaders.Add("X-SGS-Admin", "1"); admin.DefaultRequestHeaders.Add("X-PAL-Challenge-Client", "1");
            Func<string, object, Task<HttpResponseMessage>> post = (path, value) => admin.PostAsync(path, new StringContent(ChallengeContract.Json().Serialize(value), Encoding.UTF8, "application/json"));
            Check((await post("/api/v1/competitions/admin/login", new {password = "fixture-only-password"})).IsSuccessStatusCode, "HTTP existing admin session login");
            var pending = ChallengeContract.Object(await admin.GetStringAsync("/api/v1/timer/admin/binding-requests"));
            var requests = (object[])pending["items"]; var request = (Dictionary<string, object>)requests[0];
            Check((await post("/api/v1/timer/admin/binding-requests/" + request["id"], new {approved = true, display_name = "跨组件玩家", reason = "fixture approval"})).IsSuccessStatusCode, "HTTP existing approval workflow accepts name");
            clock = clock.AddSeconds(61); reply = await invoke(new {action = "refresh_identity"});
            Check(reply.ok && reply.state.player.bound && reply.state.player.display_name == "跨组件玩家", "HTTP refreshed identity observes approval");
            var player = reply.state.player;
            var choice = new Dictionary<string, object> {{"action", "enroll"}, {"challenge_id", item.id}, {"revision", item.revision}, {"player_id", player.id}, {"display_name", player.display_name}};
            reply = await invoke(choice); Check(reply.ok && reply.state.enrollments.Length == 1, "HTTP enrollment persists per device");
            choice["action"] = "activate"; choice["content_id"] = "pal98-classic-v6";
            reply = await invoke(choice); Check(reply.ok && reply.activation.locked && reply.activation.ini == item.rules.Ini(), "HTTP activation returns exact cross-language INI");
            string device = reply.activation.device_id;
            Check((await post("/api/v1/timer/challenge-workshop/login", new {username = "fixture-author", password = "fixture-author-password"})).IsSuccessStatusCode, "HTTP author login separate from admin");
            using (var edit = new HttpRequestMessage(HttpMethod.Put, "/api/v1/timer/challenge-workshop/challenges/" + item.id)) {
                edit.Content = new StringContent(ChallengeContract.Json().Serialize(new {title = "第二版挑战", expected_revision = 1, rules = new {true_duel = false}}), Encoding.UTF8, "application/json");
                Check((await admin.SendAsync(edit)).IsSuccessStatusCode, "HTTP owner publishes revision 2");
            }
            reply = await invoke(choice); Check(reply.ok && reply.activation.challenge.revision == 1 && reply.activation.challenge.rules.true_duel, "HTTP registered player retains revision 1 after edit");
            Check((await post("/api/v1/timer/admin/devices/" + device + "/binding", new {unbind = true})).IsSuccessStatusCode, "HTTP administrator unbinds fixture device");
            reply = await invoke(choice); Check(!reply.ok && reply.code == "http_409" && reply.activation == null, "HTTP changed binding rejects old challenge activation");
        }
    }
    static int Main(string[] args)
    {
        try { if (args.Length > 1) Integration(args[0], args[1]).GetAwaiter().GetResult(); else Cases(args[0]).GetAwaiter().GetResult(); Console.WriteLine("PASS total=" + checks); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
