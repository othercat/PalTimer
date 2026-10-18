// Local integration fixture, NOT a game run or a release timer.
// Compile unchanged production online sources and pair this exact host with
// a test-publisher Native Auth DLL. Never use a release approval or endpoint.
using Pal98Timer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
[assembly:System.Reflection.AssemblyVersion("3.38.3.18")]
[assembly:System.Reflection.AssemblyFileVersion("3.38.3.18")]

internal static class GeshiHttpHost
{
    static int checks;
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        ++checks; Console.WriteLine("PASS " + label);
    }
    static async Task Until(Func<bool> condition, string label)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < 30000) await Task.Delay(80);
        Check(condition(), label);
    }

    // The only injected fault is losing the first successful acknowledgement
    // AFTER the real service commits it. All requests use production HTTP.
    sealed class Wire : ICompetitionTransport
    {
        readonly CompetitionHttpTransport inner = new CompetitionHttpTransport();
        readonly string origin;
        readonly object gate = new object();
        readonly List<object> calls = new List<object>();
        internal volatile bool LostAcknowledgement;
        internal int Uploads;
        internal Wire(string origin) { this.origin = origin; }
        public async Task<CompetitionHttpResult> Send(string method, string url, string hwid, string secret,
            string body, int timeoutMilliseconds, CancellationToken stop)
        {
            var uri = new Uri(url);
            if (uri.GetLeftPart(UriPartial.Authority) != origin) throw new InvalidOperationException("Fixture cannot leave its loopback origin");
            var result = await inner.Send(method, url, hwid, secret, body, timeoutMilliseconds, stop);
            object receipt = null;
            bool drop = false;
            if (method == "POST" && uri.AbsolutePath == "/api/v1/timer/runs")
            {
                Interlocked.Increment(ref Uploads);
                if (result.Success)
                {
                    var parsed = CompetitionProtocol.Json().Deserialize<Dictionary<string, object>>(result.Body);
                    receipt = parsed["receipt"];
                    if (result.Status == 201 && !LostAcknowledgement) { LostAcknowledgement = true; drop = true; }
                }
            }
            lock (gate) calls.Add(new { method, path = uri.AbsolutePath, status = result.Status, receipt, acknowledgementDropped = drop });
            return drop ? new CompetitionHttpResult { Status = 0 } : result;
        }
        internal void Save(string root)
        {
            // No request bodies, credentials, HWID, query strings or live tokens.
            lock (gate) File.WriteAllText(Path.Combine(root, "wire.json"), CompetitionProtocol.Json().Serialize(calls));
        }
        public void Dispose() { inner.Dispose(); }
    }

    static CompetitionObservation Observation(string token, int step, bool assisted)
    {
        var resources = new Dictionary<string, string> {
            ["SSS.MKF"]="92efd5ec75dc945f3620f96f61e38879fdc0c605f5916d1143c36a4311ad57f4",
            ["DATA.MKF"]="c1342c3fe39e6c624c7388ad718438819d5981cf73e712d3c613548e7162dbc5",
            ["WORD.DAT"]="01c2ceb561730205ca358bb31d8cb0cedf79c0b63113abc1f32905849e231811",
            ["M.MSG"]="7e4597015be34d8f772bdde13453106b8c9cd824a4fc69073a33bf9aac39612f" };
        string engine = "57893f2eecf5565ee3b600e87f374b42836685ca2d16c928cecea1a0def185b4";
        var facts = new Dictionary<string, string> {
            ["family"]="sdlpal-geshi-easy", ["profile"]="geshi-easy-v1",
            ["content"]=CompetitionProtocol.Hash(string.Join("\n", resources.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+"="+p.Value))),
            ["engine_sha256"]=engine, ["engine_build"]="d26a11c", ["fade_ms"]="800", ["map_speed_ticks"]="0",
            ["walk_frame_ms"]="90", ["speed"]="2", ["battle_speed"]="1",
            // A distinct test route ensures these are never mistaken for player evidence.
            ["route_rules_sha256"]=CompetitionProtocol.Hash("LOCAL HTTP FIXTURE - NOT A PLAYTHROUGH") };
        foreach (var pair in resources) facts["resource." + pair.Key] = pair.Value;
        string digest = RankingConfiguration.Digest(facts);
        var splits = new[] {
            new CompetitionSplit { checkpoint_id="fixture-eight-heads", elapsed_ms=step>=1 ? (long?)500 : null, status=step>=1 ? "completed" : "in_progress" },
            new CompetitionSplit { checkpoint_id="fixture-completed", elapsed_ms=step==2 ? (long?)1200 : null, status=step==2 ? "completed" : "in_progress" } };
        if (assisted && step>=1) { splits[0].status="manual_skipped"; splits[0].elapsed_ms=null; }
        return new CompetitionObservation {
            Token=token, Core="SDLPAL_GESHI_EASY", Step=step, BeganHere=!assisted, Finished=step==2,
            TotalMilliseconds=step==2 ? 1200 : step==1 ? 500 : 0, RestartMilliseconds=0,
            DllHash=engine, GameVersion="1.0.0.0", FadeMilliseconds=800, MapSpeedTicks=0,
            ObservedAt=DateTimeOffset.UtcNow, ValidationError=assisted ? "LOCAL FIXTURE: imported and skipped node" : "",
            Gameplay=new GameplayIdentity { family=facts["family"],content_id=facts["profile"],content_sha256=facts["content"],rules_sha256=digest,fade_ms=800,map_speed_ticks=0,walk_frame_ms=90 },
            Ranking=new RankingConfiguration { covered=true,rules=facts,configuration_id=digest },
            Hardcore=new CompetitionHardcore(), Splits=splits,
            TimelineId=TimelineIdentity.Create(digest,CompetitionProtocol.RouteHash(splits.Select(s=>s.checkpoint_id)),false) };
    }
    static string[] Receipts(CompetitionStorage storage)
    {
        string path = Path.Combine(storage.Root, "receipts");
        return Directory.Exists(path) ? Directory.GetFiles(path,"*.json",SearchOption.AllDirectories) : new string[0];
    }
    static async Task Publish(CompetitionClient client, string token, bool assisted)
    {
        client.Invalidate(token);
        client.Publish(Observation(token,0,assisted)); await Task.Delay(450);
        client.Publish(Observation(token,1,assisted)); await Task.Delay(800);
        client.Publish(Observation(token,2,assisted));
    }
    static async Task Run(string root, string origin)
    {
        var auth = new NativeCompetitionAuth();
        var identity = auth.Identity();
        Check(identity != null && identity.Valid && identity.timer_version == "3.38.3.18", "exact test host and Native Auth identity");
        Check(auth.Registration() != null && auth.Registration().Matches(identity), "paired test build public registration");
        var storage = new CompetitionStorage(Path.Combine(root,"client"));
        var wire = new Wire(origin);
        var client = new CompetitionClient(storage,wire,auth);
        try
        {
            client.Configure(new CompetitionSettings { Enabled=true,Server=origin });
            await Until(()=>client.ActivationText=="未激活，仅本地保存", "unapproved build remains inactive");
            await Publish(client,Guid.NewGuid().ToString("D"),false);
            await Until(()=>client.View.Pending==1,"completed test observation persisted while unapproved");
            Check(wire.Uploads==0,"no score POST before build approval");
            File.WriteAllText(Path.Combine(root,"pending.fixture"),"LOCAL FIXTURE ONLY");
            await Until(()=>File.Exists(Path.Combine(root,"approved.fixture")),"isolated service approved test build");
            client.Retry();
            await Until(()=>wire.LostAcknowledgement,"real HTTP 201 received then deliberately dropped");
            await Until(()=>client.View.Pending==1,"lost receipt leaves immutable pending record");
            client.Retry();
            await Until(()=>Receipts(storage).Length==1,"same sealed result retried and receipt persisted");
            Check(!storage.LoadPending(client.Settings).Any(),"successful retry removed pending entry");
            File.WriteAllText(Path.Combine(root,"receipt.fixture"),"LOCAL FIXTURE ONLY");
            await Until(()=>File.Exists(Path.Combine(root,"published.fixture")),"test configuration published in isolated service");
            await Until(()=>client.LiveCaption.Contains("[已连接:"),"native authenticated live session connected");
            await Publish(client,Guid.NewGuid().ToString("D"),true);
            await Until(()=>Receipts(storage).Length==2,"assisted observation retained for server review");
            Check(await client.CloseAsync(),"client drained and closed");
        }
        finally { client.Dispose(); wire.Save(root); }
        File.WriteAllText(Path.Combine(root,"client-result.json"),CompetitionProtocol.Json().Serialize(new { checks, fixtureOnly=true, frozenReleaseTested=false }));
    }
    static int Main(string[] args)
    {
        try
        {
            Uri endpoint;
            if (args.Length!=2 || !Uri.TryCreate(args[1],UriKind.Absolute,out endpoint) || endpoint.Scheme!="http" || endpoint.Host!="127.0.0.1" || endpoint.AbsolutePath!="/" || endpoint.UserInfo!="" || endpoint.Query!="" || endpoint.Fragment!="")
                throw new ArgumentException("Only an explicit http://127.0.0.1:port fixture endpoint is allowed");
            Directory.CreateDirectory(args[0]);
            Run(args[0],endpoint.GetLeftPart(UriPartial.Authority)).GetAwaiter().GetResult();
            Console.WriteLine("PASS total="+checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
