using Pal98Timer;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

[assembly: AssemblyVersion("3.37.7.6")]
internal static class CompetitionBehavior
{
    static int count;
    static string root;
    [ThreadStatic] static bool insideObserveGame;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
    static async Task Until(Func<bool> condition, string name, int ms = 7000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < ms) await Task.Delay(20);
        Check(condition(), name);
    }
    static CompetitionObservation Observation(string token, int step, int fade = 1200, int speed = 10)
    {
        var game=new GameplayIdentity { rules_sha256=new string('a',64),content_id="fixture",content_sha256=new string('b',64),family="standard",fade_ms=fade,map_speed_ticks=speed };
        var facts=new System.Collections.Generic.Dictionary<string,string> { ["content"]=game.content_sha256,["family"]=game.family,["fade_ms"]=fade.ToString(),["map_speed_ticks"]=speed.ToString() };
        return new CompetitionObservation { Gameplay=game,Hardcore=new CompetitionHardcore(),Ranking=new RankingConfiguration { covered=true,rules=facts,configuration_id=RankingConfiguration.Digest(facts) },Token = token, Core = "PAL98DX9", Step = step, BeganHere = true, Finished = step == 2,
            TotalMilliseconds = 1100, DllHash = new string('1', 64), GameVersion = "1.6.8.12", FadeMilliseconds = fade, MapSpeedTicks = speed,
            ObservedAt = DateTimeOffset.UtcNow, ValidationError = "", Splits = new[] {
                new CompetitionSplit { checkpoint_id = "鬼将军", elapsed_ms = step > 0 ? (long?)500 : null, status = step > 0 ? "completed" : "in_progress" },
                new CompetitionSplit { checkpoint_id = "拜月", elapsed_ms = step == 2 ? (long?)1200 : null, status = step == 2 ? "completed" : "in_progress" } } };
    }
    static CompetitionStorage Store(string name) { return new CompetitionStorage(Path.Combine(root, name), () => "fixture-device-" + name + "-" + Guid.NewGuid().ToString("N")); }
    static CompetitionSettings Config() { return new CompetitionSettings { Enabled = true, Server = "https://fixture.invalid" }; }
    sealed class FakeAuth : ICompetitionAuth
    {
        internal bool Available = true;
        internal int Seals;
        public CompetitionAuthIdentity Identity() { return !Available ? null : new CompetitionAuthIdentity { protocol = CompetitionAuthProtocol.Name,
            key_id = new string('a', 32), timer_exe_sha256 = new string('b', 64), timer_version = "3.37.7.1", component_sha256 = new string('c', 64) }; }
        public string Seal(string origin, string eventId, string payload)
        {
            Seals++; if (!Available) return null;
            var run = CompetitionProtocol.Json().Deserialize<CompetitionRun>(payload);
            return CompetitionProtocol.Json().Serialize(new { protocol = CompetitionAuthProtocol.Name, kind = "run", key_id = Identity().key_id,
                timer_exe_sha256 = Identity().timer_exe_sha256, timer_version = run.timer_version, component_sha256 = Identity().component_sha256,
                server_origin = origin, scope = eventId, hwid = run.hwid, run_id = run.run_id,
                payload_base64 = CompetitionAuthProtocol.Encode(payload), signature_base64 = "unit-test-not-a-valid-cryptographic-signature" });
        }
        public string Prove(string sealedJson, string challenge) { return Available ? "{}" : null; }
    }
    sealed class Fake : ICompetitionTransport
    {
        internal int Active, MaxActive, Gets, Posts, Registers;
        internal bool HoldGet, BadJson; internal bool V2=true; internal bool Published=true;
        internal bool Approved = true, RejectChallenge;
        internal string DeniedBuild;
        internal int AuthChecks;
        internal TaskCompletionSource<bool> GetGate;
        internal int UploadStatus = 201;
        internal readonly ConcurrentQueue<string> Payloads = new ConcurrentQueue<string>();
        internal readonly ConcurrentQueue<string> Urls = new ConcurrentQueue<string>();
        public async Task<CompetitionHttpResult> Send(string method, string url, string hwid, string secret, string body, int timeout, CancellationToken stop)
        {
            int active = Interlocked.Increment(ref Active); MaxActive = Math.Max(MaxActive, active);
            try
            {
                Urls.Enqueue(url);
                if (url.EndsWith("/devices")) { Registers++; return Response(null, null); }
                if (url.Contains("/auth/status?"))
                {
                    AuthChecks++; var authQuery = HttpUtility.ParseQueryString(new Uri(url).Query);
                    return new CompetitionHttpResult { Status = 200, Body = CompetitionProtocol.Json().Serialize(new { protocol = CompetitionAuthProtocol.Name,
                        scope = CompetitionProtocol.Scope, server_origin = new Uri(url).GetLeftPart(UriPartial.Authority), key_id = authQuery["key_id"],
                        timer_exe_sha256 = authQuery["timer_exe_sha256"], approved = Approved && authQuery["timer_exe_sha256"] != DeniedBuild }) };
                }
                if (url.EndsWith("/auth/challenges")) return new CompetitionHttpResult { Status = RejectChallenge ? 403 : 200,
                    Body = RejectChallenge ? "{\"code\":\"build_not_approved\"}" : "{}" };
                if (method == "POST")
                {
                    var envelope = CompetitionProtocol.Json().Deserialize<System.Collections.Generic.Dictionary<string, object>>(body);
                    var seal = CompetitionProtocol.Json().Deserialize<CompetitionAuthSeal>(CompetitionAuthProtocol.Decode((string)envelope["sealed_run"]));
                    body = CompetitionAuthProtocol.Decode(seal.payload_base64);
                    Posts++; Payloads.Enqueue(body);
                    if (UploadStatus != 201) return new CompetitionHttpResult { Status = UploadStatus, RetryAfterSeconds = UploadStatus == 429 ? 60 : 0 };
                    var run = CompetitionProtocol.Json().Deserialize<CompetitionRun>(body);
                    var reply = Envelope(); reply.configuration_id = run.ranking.configuration_id; reply.custom_competition_id=run.custom_competition_id; reply.route_sha256 = run.route_sha256;
                    reply.receipt = new CompetitionReceipt { run_id = run.run_id, phase = "warmup",daily_status="pending_publication",custom_status="not_requested",custom_reason="",received_at=DateTimeOffset.UtcNow.ToString("o") };
                    return new CompetitionHttpResult { Status = 201, Body = CompetitionProtocol.Json().Serialize(reply) };
                }
                Gets++;
                if (HoldGet) await Task.Delay(30000, stop);
                if (GetGate != null) await GetGate.Task;
                var query = HttpUtility.ParseQueryString(new Uri(url).Query);
                return BadJson ? new CompetitionHttpResult { Status = 200, Body = "bad json" } : Response(query["route_sha256"], query["checkpoint_id"], query["configuration_id"], query["elapsed_ms"], query["board"],query["custom_competition_id"]);
            }
            finally { Interlocked.Decrement(ref Active); }
        }
        CompetitionReply Envelope() { return new CompetitionReply { supported_run_protocols = V2 ? new[] { CompetitionProtocol.Online } : null, protocol = CompetitionProtocol.Online, scope=CompetitionProtocol.Scope,published=Published,title="测试日常榜", reference_only = true, includes_warmup = true, player = new CompetitionPlayer { bound = true, display_name = "联调玩家" } }; }
        CompetitionHttpResult Response(string route, string node, string configuration = null, string elapsed = null, string board = null, string custom = null)
        {
            var reply = Envelope(); reply.board = board; reply.configuration_id = configuration; reply.custom_competition_id=custom; reply.route_sha256 = route;
            if (node != null) reply.node = new CompetitionNode { checkpoint_id = node, elapsed_ms = long.Parse(elapsed),
                best_complete_line = new CompetitionComparison { rank = 2, comparison_players = 2 }, personal_checkpoint_best = new CompetitionComparison { rank = 3, comparison_players = 3 } };
            return new CompetitionHttpResult { Status = 200, Body = CompetitionProtocol.Json().Serialize(reply) };
        }
        public void Dispose() { }
    }
    static async Task ClientCases()
    {
        var store = Store("three"); var fake = new Fake(); var client = new CompetitionClient(store, fake, new FakeAuth()); client.Configure(Config());
        await Until(() => fake.Registers > 0, "registered asynchronously");
        foreach (var tuple in new[] { new[] { 1200, 10 }, new[] { 800, 10 }, new[] { 800, 9 }, new[] { 1200, 9 } })
        {
            var token = Guid.NewGuid().ToString();
            client.Invalidate(token);
            client.Publish(Observation(token, 0, tuple[0], tuple[1]));
            await Task.Delay(350);
            client.Publish(Observation(token, 1, tuple[0], tuple[1]));
            await Until(() => client.View.Reply != null && client.View.Reply.node != null && client.View.Reply.configuration_id == Observation(token,1,tuple[0],tuple[1]).Ranking.configuration_id, "node track " + tuple[0] + "/" + tuple[1]);
            Check(client.View.Reply.node.best_complete_line.rank == 2 && client.View.Reply.node.personal_checkpoint_best.rank == 3, "both rankings displayed");
            int posts = fake.Posts; client.Publish(Observation(token, 2, tuple[0], tuple[1]));
            await Until(() => fake.Posts > posts && Directory.Exists(Path.Combine(store.Root, "receipts")) && Directory.GetFiles(Path.Combine(store.Root, "receipts"), "*.json", SearchOption.AllDirectories).Length > posts, "completion receipt persisted");
            client.Publish(Observation(token, 2, tuple[0], tuple[1])); await Task.Delay(350);
            Check(fake.Posts == posts + 1, "repeated completion not duplicated");
        }
        var uploads = fake.Payloads.Select(s => CompetitionProtocol.Json().Deserialize<CompetitionRun>(s)).ToArray();
        Check(uploads.All(u=>u.protocol==CompetitionProtocol.Online && u.track_id==null) && uploads.Select(u=>u.ranking.configuration_id).Distinct().Count()==4, "four daily configurations do not use event tracks");
        Check(uploads.All(u => u.total_ms == 1100 && u.splits.Last().elapsed_ms == 1200), "main and final split preserved independently");
        Check(fake.MaxActive == 1, "one network request at a time");
        await client.CloseAsync();

        store = Store("offline"); fake = new Fake { UploadStatus = 503 }; client = new CompetitionClient(store, fake, new FakeAuth()); client.Configure(Config());
        await Until(() => fake.Registers > 0, "offline registration fixture ready");
        client.Publish(Observation("offline", 0)); await Task.Delay(350); client.Publish(Observation("offline", 2));
        await Until(() => store.LoadPending(Config()).Any(p => p.Attempts == 1), "503 persists outbox before retry");
        var saved = store.LoadPending(Config()).Single(); string original = saved.Payload;
        Check(saved.NextAttemptUtc > DateTime.UtcNow && !saved.Rejected, "503 exponential backoff scheduled");
        Check(File.Exists(Path.Combine(store.Root, "network.log")) && !client.View.Stale && !client.View.Status.Contains("503") && !client.View.Status.Contains("重试"), "network failure logged without visible retry hint");
        await client.CloseAsync();
        fake = new Fake(); client = new CompetitionClient(store, fake, new FakeAuth()); client.Retry();
        await Until(() => fake.Posts == 1, "restart retries persisted result");
        Check(fake.Payloads.Single() == original, "retry payload and run id immutable"); await client.CloseAsync();

        store = Store("reject"); fake = new Fake { UploadStatus = 409 }; client = new CompetitionClient(store, fake, new FakeAuth()); client.Configure(Config());
        await Until(() => fake.Registers > 0, "409 fixture ready"); client.Publish(Observation("reject", 0)); await Task.Delay(350); client.Publish(Observation("reject", 2));
        await Until(() => store.LoadPending(Config()).Any(p => p.Rejected), "409 stops automatic retries");
        await Task.Delay(600); Check(fake.Posts == 1, "no loop on permanent conflict"); await client.CloseAsync();

        store = Store("cancel"); fake = new Fake(); client = new CompetitionClient(store, fake, new FakeAuth()); client.Configure(Config());
        await Until(() => fake.Registers > 0, "cancel fixture ready");
        client.Publish(Observation("old", 0)); await Task.Delay(350); fake.HoldGet = true; int gets = fake.Gets;
        client.Publish(Observation("old", 1)); await Until(() => fake.Gets > gets, "slow query in flight");
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++) client.Publish(Observation("old", 1));
        watch.Stop(); Check(watch.ElapsedMilliseconds < 500, "10000 snapshots do not wait on slow HTTP: " + watch.ElapsedMilliseconds + " ms");
        client.Publish(Observation("old", 2));
        client.Invalidate("new");
        Check(client.View.Reply == null, "reset immediately clears old ranking");
        watch.Restart(); await client.CloseAsync();
        Check(watch.ElapsedMilliseconds < 1500, "close cancels HTTP without its timeout");
        Check(store.LoadPending(Config()).Count() == 1, "close drains completed result to disk");

        store = Store("midrun"); fake = new Fake(); client = new CompetitionClient(store, fake, new FakeAuth()); client.Configure(Config());
        await Until(() => fake.Registers > 0, "midrun fixture ready");
        var middle = Observation("middle", 1); middle.BeganHere = false; client.Publish(middle); await Task.Delay(350);
        var end = Observation("middle", 2); end.BeganHere = false; client.Publish(end);
        await Until(() => store.LoadPending(Config()).Any(p => p.LocalRejected), "midrun enabling never uploads partial run");
        client.Retry(); await Task.Delay(350); Check(fake.Posts == 0, "retry cannot clear local ineligibility"); await client.CloseAsync();

        store = Store("lateidentity"); fake = new Fake(); client = new CompetitionClient(store, fake, new FakeAuth()); client.Configure(Config());
        await Until(() => fake.Registers > 0, "late identity fixture ready"); client.Publish(Observation("lateidentity", 0));
        var missing = Observation("lateidentity", 2); missing.DllHash = ""; missing.GameVersion = ""; client.Publish(missing);
        await Until(() => store.LoadPending(Config()).Any(p => p.LocalRejected), "missing DLL identity completion retained locally");
        string waitingId = store.LoadPending(Config()).Single().Run.run_id;
        client.Publish(Observation("lateidentity", 2)); await Until(() => fake.Posts == 1, "identity completion automatically releases pending result");
        Check(CompetitionProtocol.Json().Deserialize<CompetitionRun>(fake.Payloads.Single()).run_id == waitingId, "identity retry retains original run id"); await client.CloseAsync();

        store = Store("stale"); fake = new Fake(); client = new CompetitionClient(store, fake, new FakeAuth()); client.Configure(Config());
        await Until(() => fake.Registers > 0, "stale fixture ready"); client.Publish(Observation("before", 0)); await Task.Delay(350);
        fake.GetGate = new TaskCompletionSource<bool>(); gets = fake.Gets;
        client.Publish(Observation("before", 1));
        Check(client.View.Reply == null || client.View.Reply.node == null, "new checkpoint rank starts unknown until its own response");
        await Until(() => fake.Gets > gets, "old response held");
        client.Invalidate("after"); client.Publish(Observation("before", 2)); fake.GetGate.SetResult(true); await Task.Delay(550);
        Check(client.View.Reply == null && fake.Posts == 0, "reset rejects stale reply and late old completion"); await client.CloseAsync();

        store = Store("destination"); fake = new Fake { UploadStatus = 503 }; client = new CompetitionClient(store, fake, new FakeAuth()); client.Configure(Config());
        await Until(() => fake.Registers > 0, "destination fixture ready"); client.Publish(Observation("bound", 0)); await Task.Delay(350);
        fake.GetGate = new TaskCompletionSource<bool>(); gets = fake.Gets; client.Publish(Observation("bound", 1));
        await Until(() => fake.Gets > gets, "destination query held"); client.Publish(Observation("bound", 2));
        var other = Config(); other.Server = "https://second.invalid"; client.Configure(other); fake.GetGate.SetResult(true);
        await Until(() => fake.Registers > 1 && client.Settings.Server == other.Server, "destination change applied asynchronously");
        Check(store.LoadPending(Config()).Single().Settings.Server == Config().Server && !store.LoadPending(other).Any(), "queued completion retains original destination");
        Check(!fake.Urls.Any(u => u.StartsWith(Config().Server) && u.Contains("/runs")), "old endpoint result not posted under new credentials");
        await client.CloseAsync();

        store = Store("diskfailure"); fake = new Fake { UploadStatus = 429 }; client = new CompetitionClient(store, fake, new FakeAuth()); client.Configure(Config());
        await Until(() => fake.Registers > 0, "disk fixture ready"); client.Publish(Observation("disk", 0));
        string blocker = Path.Combine(store.Root, "outbox"); File.WriteAllText(blocker, "fixture path blocker");
        client.Publish(Observation("disk", 2)); await Task.Delay(550);
        Check(!await client.CloseAsync(), "disk failure refuses silent loss at close");
        File.Delete(blocker); Check(await client.CloseAsync(), "close retry drains completion after directory repair");
        Check(store.LoadPending(Config()).Count() == 1, "disk repair preserves completion");

        store = Store("ratelimit"); fake = new Fake { UploadStatus = 429 }; client = new CompetitionClient(store, fake, new FakeAuth()); client.Configure(Config());
        await Until(() => fake.Registers > 0, "429 fixture ready"); client.Publish(Observation("limit", 0)); client.Publish(Observation("limit", 2));
        await Until(() => store.LoadPending(Config()).Any(p => p.Attempts == 1), "429 saved for retry");
        Check(store.LoadPending(Config()).Single().NextAttemptUtc > DateTime.UtcNow.AddSeconds(50), "honor server Retry-After"); await client.CloseAsync();

        store = Store("badresponse"); fake = new Fake(); client = new CompetitionClient(store, fake, new FakeAuth()); client.Configure(Config());
        await Until(() => fake.Registers > 0, "bad response fixture ready"); client.Publish(Observation("json", 0)); await Task.Delay(350);
        client.Publish(Observation("json", 1)); await Until(() => client.View.Reply != null && client.View.Reply.node != null, "valid ranking before bad response");
        var previous = client.View.Reply; string visible = client.View.Status; fake.BadJson = true;
        client.Publish(Observation("json", 1)); await Until(() => File.Exists(Path.Combine(store.Root, "network.log")), "invalid response recorded locally");
        Check(ReferenceEquals(previous, client.View.Reply) && client.View.Status == visible && !client.View.Stale, "bad response preserves display without any failure cue");
        string log = File.ReadAllText(Path.Combine(store.Root, "network.log"));
        Check(!log.Contains(store.Credential(Config().Server).Secret) && !log.Contains("fixture-device") && !log.Contains("https://"), "network log excludes credentials HWID and endpoint"); await client.CloseAsync();
    }

    static CompetitionObservation AutoObservation(string token, int step, bool hardcore = false) {
        var value=Observation(token,step); value.Core="PAL98DX9_AUTO";
        value.Gameplay=new GameplayIdentity { rules_sha256=new string('a',64),content_id="wuqiang-fixture",content_sha256=new string('b',64),family="standard",fade_ms=1200,map_speed_ticks=10 };
        value.Hardcore=new CompetitionHardcore {requested=hardcore,run_verified=hardcore,rules_version=hardcore?4:0,evidence_status=hardcore?"runtime_observed":"ordinary"};
        value.TimelineId=TimelineIdentity.Create(value.Gameplay.rules_sha256,CompetitionProtocol.RouteHash(value.Splits.Select(p=>p.checkpoint_id)),hardcore);
        return value;
    }
    static async Task GameplayCases() {
        var store=Store("v2");var fake=new Fake{V2=true};var auth=new FakeAuth();var client=new CompetitionClient(store,fake,auth);client.Configure(Config());
        await Until(()=>fake.Registers>0,"v2 server capability registered");
        var before=Observation("v2",-1);before.Core="PAL98DX9_AUTO";client.Publish(before);
        client.Publish(AutoObservation("v2",0,true));client.Publish(AutoObservation("v2",1,true));
        await Until(()=>client.View.Reply?.node!=null,"automatic node uses ordinary reference by default");
        var style=Config();style.ReferenceBoard="hardcore";client.Configure(style);
        await Until(()=>client.Settings.ReferenceBoard=="hardcore","hardcore reference selected without switching gameplay");
        await Until(()=>client.View.Reply?.board=="hardcore"&&client.View.Reply.node!=null,"hardcore response bound to selected board");
        client.Publish(AutoObservation("v2",2,true));await Until(()=>fake.Posts==1,"v2 completion uploaded without pre-start identity race");
        var run=CompetitionProtocol.Json().Deserialize<CompetitionRun>(fake.Payloads.Single());
        Check(run.protocol==CompetitionProtocol.Online&&run.hardcore.run_verified&&run.hardcore.rules_version==4,"v2 signed body carries continuous hardcore evidence");
        Check(run.timeline_id==AutoObservation("v2",2,true).TimelineId&&CompetitionProtocol.Validate(run)=="","v2 identity formula matches signed route");
        run.hardcore.requested=false;Check(CompetitionProtocol.Validate(run).Length>0,"changed hardcore classification invalidates body");
        await client.CloseAsync();

        store=Store("old-v2-server");fake=new Fake{V2=false};auth=new FakeAuth();client=new CompetitionClient(store,fake,auth);client.Configure(Config());
        await Until(()=>fake.Registers>0,"old server registered");client.Publish(AutoObservation("old",0));client.Publish(AutoObservation("old",2));
        await Until(()=>store.LoadPending(Config()).Any(p=>!string.IsNullOrEmpty(p.SealedRun)),"v2 run sealed locally even on old server");
        string original=store.LoadPending(Config()).Single().Payload;await Task.Delay(550);
        Check(fake.Posts==0&&auth.Seals==1,"old server never receives downgraded or stripped v2");
        fake.V2=true;client.Retry();await Until(()=>fake.Posts==1,"v2 server upgrade releases original signed bytes");
        Check(fake.Payloads.Single()==original&&auth.Seals==1,"pending v2 is not resealed after server upgrade");await client.CloseAsync();

        store=Store("v2-rule-change");fake=new Fake{V2=true};client=new CompetitionClient(store,fake,new FakeAuth());client.Configure(Config());
        await Until(()=>fake.Registers>0,"rule change fixture ready");client.Publish(AutoObservation("rules",0));
        var changed=AutoObservation("rules",2,true);client.Publish(changed);
        await Until(()=>store.LoadPending(Config()).Any(),"changed identity retains local record");
        Check(store.LoadPending(Config()).Single().LocalRejected&&fake.Posts==0,"mid-run normal to hardcore cannot claim either board");await client.CloseAsync();

        store=Store("board-stale");fake=new Fake{V2=true};client=new CompetitionClient(store,fake,new FakeAuth());client.Configure(Config());
        await Until(()=>fake.Registers>0,"board fixture ready");client.Publish(AutoObservation("board",0));await Task.Delay(350);
        fake.GetGate=new TaskCompletionSource<bool>();int gets=fake.Gets;client.Publish(AutoObservation("board",1));
        await Until(()=>fake.Gets>gets,"overall reply held while changing board");style=Config();style.ReferenceBoard="hardcore";client.Configure(style);
        fake.GetGate.SetResult(true);await Until(()=>client.Settings.ReferenceBoard=="hardcore"&&client.View.Reply?.board=="hardcore","old overall reply cannot replace hardcore view");
        client.Invalidate("next");Check(client.View.Reply==null,"reset clears previous board rank to unknown");await client.CloseAsync();
    }

    static void ModelCases()
    {
        var vectorPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ranking-configuration-vectors.json");
        var vectors=CompetitionProtocol.Json().Deserialize<RankingConfiguration[]>(File.ReadAllText(vectorPath));
        foreach(var vector in vectors) Check(RankingConfiguration.Digest(vector.rules)==vector.configuration_id,"shared ranking canonical vector");
        Check(CompetitionProtocol.RouteHash(new[] { "鬼将军", "拜月" }) == "fe3a4dc38528a7d49eafcd6e8b75e79f6946d45b28b3b09012a3ae6e8a41dc92", "Python/C# route digest exact UTF8 LF");
        Check(CompetitionProtocol.CheckpointId("鬼将军") == "鬼将军" && CompetitionProtocol.CheckpointId("a b").StartsWith("id-sha256-"), "stable names and explicit whitespace mapping");
        Check(new CompetitionSettings { Server = "http://example.com" }.Validate().Length > 0, "reject insecure remote endpoint");
        Check(new CompetitionSettings { Server = "http://127.0.0.1:8776" }.Validate().Length == 0, "allow loopback test endpoint");
        Check(new CompetitionSettings { Server = "https://user:pw@example.com" }.Validate().Length > 0, "reject credentials in URL");
        Check(CompetitionProtocol.TrackFor(1200, 9) == null, "unsupported fourth mode not mixed into three tracks");
        Check(CompetitionGameSettings.Parse("{\"schema\":\"PAL98.ToolLaunchSettings.v1\",\"competition_upload\":{\"schema\":\"PAL98.TimerOnlineSettings.v1\",\"xiaorou\":{\"enabled\":true,\"server\":\"https://fixture.invalid\",\"use_custom_competition\":true,\"custom_competition_id\":\"cup\"}}}").CustomCompetitionId==null,"unlocked custom id cannot confer competition membership");
        var store = Store("identity"); var credential = store.Credential(Config().Server);
        Check(credential.Hwid == store.Credential(Config().Server).Hwid && credential.Secret == store.Credential(Config().Server).Secret, "device identity persists across directory versions");
        Check(!File.ReadAllText(Directory.GetFiles(store.Root, "*.device").Single()).Contains(credential.Secret), "credential encrypted with DPAPI");
    }
    static async Task ActivationCases()
    {
        var store = Store("approval"); var fake = new Fake { Approved = false }; var auth = new FakeAuth();
        var client = new CompetitionClient(store, fake, auth); client.Configure(Config());
        await Until(() => client.ActivationText == "未激活，仅本地保存", "unapproved build has independent settings-only activation");
        client.Publish(Observation("approval", 0)); client.Publish(Observation("approval", 1));
        await Until(() => client.View.Reply != null && client.View.Reply.node != null, "unapproved build can compare nodes");
        client.Publish(Observation("approval", 2));
        await Until(() => store.LoadPending(Config()).Any(p => !string.IsNullOrEmpty(p.SealedRun)), "unapproved result sealed and saved locally");
        string originalSeal = store.LoadPending(Config()).Single().SealedRun;
        await Task.Delay(800); Check(fake.Posts == 0 && fake.AuthChecks == 1 && auth.Seals == 1, "unapproved build neither posts nor busy-polls nor reseals");
        Check(!client.View.Status.Contains("激活"), "activation state never enters rank view status");
        fake.Approved = true; client.Retry();
        await Until(() => fake.Posts == 1, "approval releases original sealed result");
        Check(client.ActivationText == "联机上传：已激活" && auth.Seals == 1, "approval does not re-sign past data"); await client.CloseAsync();

        store = Store("mixed-producers"); fake = new Fake { Approved = false }; auth = new FakeAuth();
        client = new CompetitionClient(store, fake, auth); client.Configure(Config());
        await Until(() => fake.Registers > 0, "mixed producer fixture ready");
        client.Publish(Observation("older", 0)); client.Publish(Observation("older", 2));
        await Until(() => store.LoadPending(Config()).Any(p => !string.IsNullOrEmpty(p.SealedRun)), "older producer record saved");
        await client.CloseAsync();
        var older = store.LoadPending(Config()).Single();
        var oldSeal = CompetitionProtocol.Json().Deserialize<System.Collections.Generic.Dictionary<string, object>>(CompetitionAuthProtocol.Decode(older.SealedRun));
        oldSeal["timer_exe_sha256"] = new string('d', 64);
        older.SealedRun = CompetitionAuthProtocol.Encode(CompetitionProtocol.Json().Serialize(oldSeal));
        older.NextAttemptUtc = DateTime.MinValue; store.SavePending(older);
        fake = new Fake { DeniedBuild = new string('d', 64) }; auth = new FakeAuth(); client = new CompetitionClient(store, fake, auth);
        await Until(() => store.LoadPending(Config()).Single().NextAttemptUtc > DateTime.UtcNow.AddMinutes(4), "unapproved original build receives a bounded wait");
        client.Publish(Observation("newer", 0)); client.Publish(Observation("newer", 2));
        await Until(() => fake.Posts == 1, "unapproved old producer does not starve approved current record");
        Check(store.LoadPending(Config()).Single().Run.run_id == older.Run.run_id && auth.Seals == 1, "only original old sealed record remains without resealing");
        await client.CloseAsync();

        store = Store("legacy"); fake = new Fake { Approved = false }; auth = new FakeAuth(); client = new CompetitionClient(store, fake, auth); client.Configure(Config());
        await Until(() => fake.Registers > 0, "legacy fixture ready"); client.Publish(Observation("legacy", 0)); client.Publish(Observation("legacy", 2));
        await Until(() => store.LoadPending(Config()).Any(), "legacy seed persisted"); await client.CloseAsync();
        var legacy = store.LoadPending(Config()).Single(); legacy.SealedRun = null; legacy.SealAttempted = false; store.SavePending(legacy);
        fake = new Fake(); auth = new FakeAuth(); client = new CompetitionClient(store, fake, auth); client.Retry();
        await Until(() => store.LoadPending(Config()).Any(p => p.LocalRejected), "legacy unsigned record stays permanently local");
        client.Retry(); await Task.Delay(400); Check(fake.Posts == 0 && auth.Seals == 0, "new approved executable cannot retroactively sign legacy record"); await client.CloseAsync();

        store = Store("missing-component"); fake = new Fake(); auth = new FakeAuth { Available = false }; client = new CompetitionClient(store, fake, auth); client.Configure(Config());
        await Until(() => fake.Registers > 0, "missing component can register for read-only queries"); client.Publish(Observation("missing-component", 0)); client.Publish(Observation("missing-component", 2));
        await Until(() => store.LoadPending(Config()).Any(p => p.LocalRejected), "missing component result persists locally");
        Check(fake.Posts == 0 && client.ActivationText == "仅本地保存", "missing component has no cloud dependency or upload fallback"); await client.CloseAsync();

        store = Store("revocation"); fake = new Fake { RejectChallenge = true }; auth = new FakeAuth(); client = new CompetitionClient(store, fake, auth); client.Configure(Config());
        await Until(() => client.ActivationText == "联机上传：已激活", "initially approved fixture ready"); client.Publish(Observation("revocation", 0)); client.Publish(Observation("revocation", 2));
        await Until(() => store.LoadPending(Config()).Any(p => p.Attempts == 1), "server revocation pauses queue even after cached approval");
        var revoked = store.LoadPending(Config()).Single(); Check(!revoked.Rejected && revoked.NextAttemptUtc > DateTime.UtcNow.AddMinutes(4) && fake.Posts == 0, "revocation retains original evidence with low-frequency retry");
        await client.CloseAsync();
    }
    static async Task ManagedConfigurationCases()
    {
        Check(!CompetitionGameSettings.Parse(null).Enabled, "missing game configuration disables upload");
        Check(!CompetitionGameSettings.Parse("{\"schema\":\"PAL98.ToolLaunchSettings.v1\",\"tools\":[]}").Enabled, "legacy game configuration never inherits local endpoint");
        string json = "{\"schema\":\"PAL98.ToolLaunchSettings.v1\",\"competition_upload\":{\"schema\":\"PAL98.CompetitionUploadSettings.v1\",\"xiaorou\":{\"enabled\":true,\"server\":\"https://www.pallab.top/\",\"event_id\":\"wuqiang\",\"ruleset_id\":\"wuqiang-2026-v1\"}}}";
        var parsed = CompetitionGameSettings.Parse(json);
        Check(parsed.Enabled && parsed.Server == "https://www.pallab.top" && parsed.Event == "wuqiang", "producer contract selects endpoint and IDs");
        bool bad = false; try { CompetitionGameSettings.Parse(json.Replace("https://www.pallab.top/", "http://unsafe.example/")); } catch (InvalidDataException) { bad = true; }
        Check(bad, "unsafe endpoint refused");
        var store = Store("managed"); var previous = Config(); previous.OverlayFont = "宋体"; previous.OverlayFontSize = 18; previous.OverlayAlignment = "right";
        store.SaveSettings(previous);
        var fake = new Fake(); int reads = 0; bool readOnCallerStack = false, readOnWorker = false, previousEndpointDisabled = false;
        var gameA = new Process(); var gameB = new Process();
        CompetitionClient client = null;
        client = new CompetitionClient(store, fake, new FakeAuth(), true, process => {
            Interlocked.Increment(ref reads); readOnCallerStack |= insideObserveGame; readOnWorker = Thread.CurrentThread.IsThreadPoolThread;
            if (ReferenceEquals(process, gameB)) previousEndpointDisabled = !client.Enabled;
            return ReferenceEquals(process, gameA) ? parsed.Copy() : new CompetitionSettings();
        });
        await Task.Delay(400);
        Check(!client.Enabled && fake.Registers == 0, "legacy local enabled setting never activates network");
        Check(client.Settings.OverlayFont == "宋体" && client.Settings.OverlayFontSize == 18, "local appearance survives server ownership migration");
        insideObserveGame = true;
        try { client.ObserveGame(gameA); } finally { insideObserveGame = false; }
        await Until(() => client.Enabled && fake.Registers > 0, "selected game configuration enables service");
        Check(!readOnCallerStack && readOnWorker, "game file reading is asynchronous on the background worker");
        for (int i = 0; i < 100; i++) client.ObserveGame(gameA);
        await Task.Delay(350); Check(reads == 1, "same process is not repeatedly scanned by UI polling");
        var appearance = client.Settings; appearance.Enabled = false; appearance.Server = "https://unwanted.invalid";
        appearance.OverlayColor = "#00FF44"; appearance.OverlayAlignment = "center"; appearance.Overlay = true;
        client.ConfigureAppearance(appearance);
        await Until(() => client.Settings.OverlayColor == "#00FF44", "appearance update saved in background");
        Check(client.Enabled && client.Settings.Server == parsed.Server, "appearance cannot change managed server settings");
        Check(!store.LoadSettings().Enabled, "managed permission is never persisted as local upload authority");
        fake.GetGate = new TaskCompletionSource<bool>(); int gets = fake.Gets;
        client.Publish(Observation("managed-switch", 0)); client.Publish(Observation("managed-switch", 1));
        await Until(() => fake.Gets > gets, "managed query held before target switch");
        appearance.OverlayColor = "#FF8844"; client.ConfigureAppearance(appearance);
        client.ObserveGame(gameB); Check(!client.Enabled, "target change immediately stops old publication");
        fake.GetGate.SetResult(true);
        await Until(() => reads == 2 && !client.Settings.Enabled, "disabled next game cannot inherit previous competition");
        Check(previousEndpointDisabled, "queued appearance save cannot re-enable old target during switch");
        Check(client.View.Reply == null && client.Settings.OverlayColor == "#FF8844", "target switch clears rankings but retains personal layout");
        await client.CloseAsync(); gameA.Dispose(); gameB.Dispose();

        fake = new Fake(); store = Store("managed-exit");
        client = new CompetitionClient(store, fake, new FakeAuth(), true, process => parsed.Copy());
        using (var game = new Process()) {
            client.ObserveGame(game); await Until(() => client.Enabled && fake.Registers > 0, "managed completion destination loaded");
            client.Publish(Observation("managed-exit", 0)); client.Publish(Observation("managed-exit", 2)); client.ObserveGame(null);
            await Until(() => fake.Posts == 1, "PAL exit preserves accepted completion upload");
            Check(!client.Enabled, "PAL exit disables new run publication");
        }
        await client.CloseAsync();

        var invalid = new CompetitionSettings { OverlayFontSize = float.NaN };
        Check(invalid.ValidateAppearance().Length > 0, "NaN font size refused");
        Check(CompetitionOverlayForm.TransparencyColor(System.Drawing.ColorTranslator.FromHtml("#FF00FF")).ToArgb() != System.Drawing.Color.Magenta.ToArgb(), "magenta text does not become transparent");
        Check(CompetitionOverlayForm.HitTest(new System.Drawing.Point(5, 5), new System.Drawing.Size(600, 300), true) == 13 &&
            CompetitionOverlayForm.HitTest(new System.Drawing.Point(200, 150), new System.Drawing.Size(600, 300), true) == 2 &&
            CompetitionOverlayForm.HitTest(new System.Drawing.Point(595, 295), new System.Drawing.Size(600, 300), true) == 17 &&
            CompetitionOverlayForm.HitTest(new System.Drawing.Point(5, 5), new System.Drawing.Size(600, 300), false) == 1, "borderless drag and resize hit tests");
        Check(CompetitionOverlayForm.TextFor(new CompetitionView()).Contains("未知") && !CompetitionOverlayForm.TextFor(new CompetitionView()).Contains("重试"), "offline overlay has unknown ranks and no retry message");

        client = new CompetitionClient(Store("overlay"), new Fake(), new FakeAuth());
        var style = new CompetitionSettings { Overlay = true, OverlayFont = "Microsoft YaHei UI", OverlayFontSize = 14, OverlayColor = "#66DDFF", OverlayAlignment = "center", OverlayWidth = 720, OverlayHeight = 330 };
        client.ConfigureAppearance(style); await Until(() => client.Settings.OverlayWidth == 720, "preview style persisted");
        Exception uiError = null;
        var ui = new Thread(() => {
            try {
                System.Windows.Forms.Application.EnableVisualStyles();
                using (var form = new CompetitionOverlayForm(client))
                using (var settingsForm = new CompetitionSettingsForm(client)) {
                    Check(form.FormBorderStyle == System.Windows.Forms.FormBorderStyle.None && form.ShowInTaskbar && form.Text == CompetitionOverlayForm.ObsWindowTitle, "capture window retains identity without a caption bar");
                    form.Opacity = 0; settingsForm.Opacity = 0; form.Show(); settingsForm.Show(); System.Windows.Forms.Application.DoEvents();
                    using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, form.ClientRectangle); bitmap.Save(Path.Combine(root, "competition-overlay-preview.png")); }
                    using (var bitmap = new System.Drawing.Bitmap(settingsForm.Width, settingsForm.Height)) { settingsForm.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(Path.Combine(root, "competition-settings-preview.png")); }
                    Check(form.Width == 720 && form.Height == 330, "saved overlay dimensions restored");
                }
            } catch (Exception error) { uiError = error; }
        }) { IsBackground = true };
        ui.SetApartmentState(ApartmentState.STA); ui.Start(); Check(ui.Join(10000), "overlay STA rendering completed"); if (uiError != null) throw uiError;
        await client.CloseAsync();
    }
    static async Task Integration(string server)
    {
        var store = Store("http"); var cfg = Config(); cfg.Server = server;
        var client = new CompetitionClient(store); client.Configure(cfg);
        await Until(() => client.View.Status.Contains("绑定"), "real PR11 device registration");
        foreach (var mode in new[] { new[] { 1200, 10 }, new[] { 800, 10 }, new[] { 800, 9 }, new[] { 1200, 9 } })
        {
            int receipts = Directory.Exists(Path.Combine(store.Root, "receipts")) ? Directory.GetFiles(Path.Combine(store.Root, "receipts"), "*.json", SearchOption.AllDirectories).Length : 0;
            string token = Guid.NewGuid().ToString(); client.Invalidate(token); client.Publish(Observation(token, 0, mode[0], mode[1])); await Task.Delay(350);
            client.Publish(Observation(token, 1, mode[0], mode[1])); await Task.Delay(600);
            client.Publish(Observation(token, 2, mode[0], mode[1]));
            await Until(() => Directory.Exists(Path.Combine(store.Root, "receipts")) && Directory.GetFiles(Path.Combine(store.Root, "receipts"), "*.json", SearchOption.AllDirectories).Length > receipts, "PR11 accepts track " + mode[0] + "/" + mode[1]);
        }
        await client.CloseAsync();
    }
    static int Main(string[] args)
    {
        try { root = args[0]; Directory.CreateDirectory(root); if (args.Length > 1) Integration(args[1]).GetAwaiter().GetResult(); else { ModelCases(); ClientCases().GetAwaiter().GetResult(); ActivationCases().GetAwaiter().GetResult(); ManagedConfigurationCases().GetAwaiter().GetResult(); GameplayCases().GetAwaiter().GetResult(); }
            Console.WriteLine("PASS total=" + count); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
