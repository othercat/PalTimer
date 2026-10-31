using Pal98Timer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
[assembly:System.Reflection.AssemblyVersion("3.37.7.13")]
[assembly:System.Reflection.AssemblyFileVersion("3.37.7.13")]
internal static class OnlineProbe
{
    static int checks;
    static void Check(bool ok,string label) { if(!ok) throw new Exception(label); ++checks; Console.WriteLine("PASS "+label); }
    static CompetitionRun Run(string hwid,int fade=1200,int speed=10)
    {
        var game=new GameplayIdentity { rules_sha256=new string('a',64),content_id="fixture",content_sha256=new string('b',64),family="standard",fade_ms=fade,map_speed_ticks=speed };
        var facts=new Dictionary<string,string> { ["content"]=game.content_sha256,["family"]=game.family,["fade_ms"]=fade.ToString(),["map_speed_ticks"]=speed.ToString() };
        var run=new CompetitionRun { run_id=Guid.NewGuid().ToString("D"),hwid=hwid,started_at=DateTimeOffset.UtcNow.AddSeconds(-2).ToString("o"),finished_at=DateTimeOffset.UtcNow.ToString("o"),total_ms=1100,timer_version="3.37.7.13",game_version="1.6.8.15",pal_dll_sha256=new string('1',64),
            gameplay=game,hardcore=new CompetitionHardcore(),ranking=new RankingConfiguration { covered=true,rules=facts,configuration_id=RankingConfiguration.Digest(facts) },
            splits=new[]{new CompetitionSplit{checkpoint_id="鬼将军",elapsed_ms=500,status="completed"},new CompetitionSplit{checkpoint_id="拜月",elapsed_ms=1200,status="completed"}} };
        run.route_sha256=CompetitionProtocol.RouteHash(run.splits.Select(p=>p.checkpoint_id));
        run.timeline_id=TimelineIdentity.Create(game.rules_sha256,run.route_sha256,false);return run;
    }
    static CompetitionObservation Observation(string token,int step,int fade,int speed)
    {
        var run=Run("local-fixture",fade,speed);
        foreach(var split in run.splits.Skip(step)) { split.status="in_progress";split.elapsed_ms=null; }
        return new CompetitionObservation { Token=token,Core="PAL98DX9_AUTO",Step=step,BeganHere=true,Finished=step==2,TotalMilliseconds=run.total_ms,DllHash=run.pal_dll_sha256,GameVersion=run.game_version,FadeMilliseconds=fade,MapSpeedTicks=speed,ObservedAt=DateTimeOffset.UtcNow,ValidationError="",Gameplay=run.gameplay,Ranking=run.ranking,Hardcore=run.hardcore,TimelineId=run.timeline_id,Splits=run.splits };
    }
    static CompetitionRun SdlRun(string hwid)
    {
        var run=Run(hwid,800,0);run.protocol=CompetitionProtocol.OnlineV2;run.game_version="1.0.0.0";run.total_ms=1200;
        var game=run.gameplay;game.family="sdlpal-geshi-easy";game.content_id="geshi-easy-v1";game.walk_frame_ms=90;
        var resources=new Dictionary<string,string> { ["SSS.MKF"]=new string('a',64),["DATA.MKF"]=new string('b',64),["WORD.DAT"]=new string('c',64),["M.MSG"]=new string('d',64) };
        game.content_sha256=CompetitionProtocol.Hash(string.Join("\n",resources.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+"="+p.Value)));
        var facts=run.ranking.rules;facts["content"]=game.content_sha256;facts["family"]=game.family;facts["profile"]=game.content_id;
        facts["engine_sha256"]=run.pal_dll_sha256;facts["engine_build"]="fixture-sdl-build";facts["walk_frame_ms"]="90";facts["speed"]="2";facts["battle_speed"]="1";
        facts["route_rules_sha256"]=CompetitionProtocol.Hash("fixture SDL route");foreach(var pair in resources)facts["resource."+pair.Key]=pair.Value;
        game.rules_sha256=run.ranking.configuration_id=RankingConfiguration.Digest(facts);run.timeline_id=TimelineIdentity.Create(game.rules_sha256,run.route_sha256,false);
        run.observations=new CompetitionRunObservations{began_here=true,finished=true,issues=new string[0],initial_gameplay=game,initial_ranking=run.ranking,initial_hardcore=run.hardcore};
        return run;
    }
    static async Task Until(Func<bool> value,string label) { var clock=Stopwatch.StartNew();while(!value()&&clock.ElapsedMilliseconds<15000) await Task.Delay(80);Check(value(),label); }
    static void Local(string root)
    {
        var auth=new NativeCompetitionAuth();var identity=auth.Identity();Check(identity!=null&&identity.Valid,"native exact host and version");
        Check(auth.Registration()!=null&&auth.Registration().Matches(identity),"native host matches automatic public registration");
        var liveChallenge=new { protocol="PAL98.TimerLive.v1",scope=CompetitionProtocol.Scope,server_origin="https://fixture.invalid",hwid=CompetitionStorage.ReadWindowsIdentity(),
            key_id=identity.key_id,timer_exe_sha256=identity.timer_exe_sha256,instance_id=Guid.NewGuid().ToString("D"),challenge_id=Guid.NewGuid().ToString("D"),nonce=new string('e',64),expires_at=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+120 };
        string liveJson=CompetitionProtocol.Json().Serialize(liveChallenge),liveProof=auth.ProveLive(liveJson);
        Check(liveProof!=null,"native fixed-purpose live session proof");
        Check(auth.ProveLive(liveJson.Replace(liveChallenge.hwid,"win-"+new string('0',64)))==null,"live wrong hardware identity refused");
        Check(auth.ProveLive(liveJson.Replace(identity.key_id,new string('0',32)))==null,"live wrong build key refused");
        Check(auth.ProveLive(liveJson.Replace("https://fixture.invalid","http://fixture.invalid"))==null,"live insecure server refused");
        Check(auth.ProveLive(liveJson.Replace("PAL98.TimerLive.v1",CompetitionAuthProtocol.Name))==null,"live domain cannot sign upload proof");
        Check(auth.ProveLive(liveJson.Replace(liveChallenge.expires_at.ToString(),"1"))==null,"expired live challenge refused");
        Check(auth.ProveLive(liveJson.Insert(1,"\"arbitrary\":\"data\","))==null,"live arbitrary signing fields refused");
        File.WriteAllText(Path.Combine(root,"live-challenge.json"),liveJson);File.WriteAllText(Path.Combine(root,"live-proof.json"),liveProof);
        var run=Run(CompetitionStorage.ReadWindowsIdentity());string body=CompetitionProtocol.SerializeRun(run);
        string seal=auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,body);Check(seal!=null,"Online v1 real native seal");
        var parsed=CompetitionAuthProtocol.ReadSeal(CompetitionAuthProtocol.Encode(seal),new CompetitionSettings{Server="https://fixture.invalid"},run,body);
        Check(parsed.scope==CompetitionProtocol.Scope&&parsed.timer_exe_sha256==identity.timer_exe_sha256,"v2 scope and producer identity");
        var challenge=new { protocol=CompetitionAuthProtocol.Name,scope=CompetitionProtocol.Scope,server_origin="https://fixture.invalid",hwid=run.hwid,key_id=identity.key_id,timer_exe_sha256=identity.timer_exe_sha256,challenge_id=Guid.NewGuid().ToString("D"),nonce=new string('d',64),request_sha256=CompetitionProtocol.Hash(seal),expires_at=DateTimeOffset.UtcNow.AddMinutes(2).ToString("o") };
        string challengeJson=CompetitionProtocol.Json().Serialize(challenge);string proof=auth.Prove(seal,challengeJson);Check(proof!=null,"payload-bound native challenge proof");
        Check(auth.Seal("https://fixture.invalid","wuqiang",body)==null,"old event-purpose signing refused");
        Check(auth.Seal("http://example.com",CompetitionProtocol.Scope,body)==null,"insecure remote origin refused");
        Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,body.Replace(run.hwid,"win-"+new string('0',64)))==null,"other device refused");
        Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,body.Replace("3.37.7.13","3.37.7.5"))==null,"other producer version refused");
        Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,body.Replace(run.ranking.configuration_id,new string('0',64)))==null,"forged ranking digest refused");
        Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,body.Replace("\"run_id\":","\"run_id\":\"duplicate\",\"run_id\":"))==null,"duplicate field refused");
        Check(auth.Prove(seal,challengeJson.Replace("fixture.invalid","another.invalid"))==null,"cross-origin proof refused");
        Check(auth.Prove(seal,challengeJson.Replace(challenge.request_sha256,new string('0',64)))==null,"substituted challenge digest refused");
        foreach(var pair in new[]{new[]{1200,10},new[]{800,10},new[]{800,9},new[]{1200,9}})Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,CompetitionProtocol.SerializeRun(Run(run.hwid,pair[0],pair[1])))!=null,"daily speed "+pair[0]+"/"+pair[1]);
        run.custom_competition_id="registered-cup";Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,CompetitionProtocol.SerializeRun(run))!=null,"optional custom ID protected inside run");
        run.hardcore=new CompetitionHardcore{requested=true,run_verified=true,rules_version=4,evidence_status="runtime_observed"};run.timeline_id=TimelineIdentity.Create(run.gameplay.rules_sha256,run.route_sha256,true);
        Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,CompetitionProtocol.SerializeRun(run))!=null,"hardcore fields accepted and signed");
        run.hardcore.rules_version=5;Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,CompetitionProtocol.SerializeRun(run))==null,"unknown hardcore revision refused");
        var relaxed=Run(run.hwid);relaxed.protocol=CompetitionProtocol.OnlineV2;
        relaxed.observations=new CompetitionRunObservations{began_here=true,finished=true,issues=new string[0],initial_gameplay=relaxed.gameplay,initial_ranking=relaxed.ranking,initial_hardcore=relaxed.hardcore};
        var variants=new Dictionary<string,Action<CompetitionRun>> {
            ["out-of-order"]=r=>r.splits[1].elapsed_ms=200,
            ["missing-node"]=r=>{r.splits[0].elapsed_ms=null;r.splits[0].status="manual_skipped";},
            ["unfinished"]=r=>{r.splits[1].elapsed_ms=null;r.splits[1].status="in_progress";r.observations.finished=false;},
            ["unknown-facts"]=r=>{r.gameplay=null;r.ranking=null;r.hardcore=null;r.timeline_id=null;r.game_version=null;r.pal_dll_sha256=null;},
            ["unverified-hardcore"]=r=>{r.hardcore.requested=true;r.hardcore.run_verified=false;r.hardcore.evidence_status="interrupted";},
            ["mismatched-ranking"]=r=>r.ranking.configuration_id=new string('f',64),
            ["zero-total"]=r=>r.total_ms=0,
            ["changed-rules"]=r=>{r.observations.began_here=false;r.observations.issues=new[]{"rules-changed"};r.gameplay.family="unknown-mod";}
        };
        foreach(var pair in variants) {
            var variant=CompetitionProtocol.Json().Deserialize<CompetitionRun>(CompetitionProtocol.SerializeRun(relaxed));pair.Value(variant);
            string payload=CompetitionProtocol.SerializeRun(variant), signed=auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,payload);
            Check(signed!=null,"v2 preserves "+pair.Key+" for server review");
            File.WriteAllText(Path.Combine(root,"v2-"+pair.Key+".payload.json"),payload);
            File.WriteAllText(Path.Combine(root,"v2-"+pair.Key+".seal.json"),signed);
        }
        string v2=CompetitionProtocol.SerializeRun(relaxed);
        var sdl=SdlRun(run.hwid);string sdlPayload=CompetitionProtocol.SerializeRun(sdl);
        string sdlSeal=auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,sdlPayload);
        Check(sdlSeal!=null,"native seals independent SDL millisecond identity");
        var sdlSigned=CompetitionProtocol.Json().Deserialize<Dictionary<string,object>>(sdlSeal);
        Check(CompetitionAuthProtocol.Decode((string)sdlSigned["payload_base64"])==sdlPayload,"SDL seal preserves exact gameplay and initial gameplay bytes");
        File.WriteAllText(Path.Combine(root,"v2-sdl.payload.json"),sdlPayload);File.WriteAllText(Path.Combine(root,"v2-sdl.seal.json"),sdlSeal);
        foreach(int? walk in new int?[]{null,0,1,1000000}) {
            sdl.gameplay.walk_frame_ms=walk;
            Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,CompetitionProtocol.SerializeRun(sdl))!=null,"native seals bounded SDL observation for server review: "+walk);
        }
        foreach(string invalid in new[]{"-1","1000001","true","1.5","\"90\""})
            Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,sdlPayload.Replace("\"walk_frame_ms\":90","\"walk_frame_ms\":"+invalid))==null,"native rejects invalid walk milliseconds: "+invalid);
        var contentRun=SdlRun(run.hwid);contentRun.gameplay.speed=0;contentRun.gameplay.battle_speed=100;contentRun.release_revision="r54";
        foreach(var name in new[]{"fade_ms","map_speed_ticks","walk_frame_ms","speed","battle_speed","engine_sha256","engine_build","route_rules_sha256"})contentRun.ranking.rules.Remove(name);
        contentRun.gameplay.rules_sha256=contentRun.ranking.configuration_id=RankingConfiguration.Digest(contentRun.ranking.rules);
        contentRun.timeline_id=TimelineIdentity.Create(contentRun.gameplay.rules_sha256,contentRun.route_sha256,false);
        string contentPayload=CompetitionProtocol.SerializeRun(contentRun),contentSeal=auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,contentPayload);
        Check(contentSeal!=null,"native seals speed-independent content rules and actual observations");
        var contentSigned=CompetitionProtocol.Json().Deserialize<Dictionary<string,object>>(contentSeal);
        Check(CompetitionAuthProtocol.Decode((string)contentSigned["payload_base64"])==contentPayload,"content seal preserves speed and release metadata bytes");
        foreach(string name in new[]{"speed","battle_speed"}) {
            string token="\""+name+"\":"+(name=="speed"?"0":"100");
            foreach(string valid in new[]{"null","0","1","100"})Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,contentPayload.Replace(token,"\""+name+"\":"+valid))!=null,"bounded native "+name+": "+valid);
            foreach(string invalid in new[]{"-1","101","true","1.5","\"1\""})Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,contentPayload.Replace(token,"\""+name+"\":"+invalid))==null,"invalid native "+name+": "+invalid);
        }
        foreach(string revision in new[]{"null","\"r1\"","\"r999999\""})Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,contentPayload.Replace("\"release_revision\":\"r54\"","\"release_revision\":"+revision))!=null,"native release metadata: "+revision);
        foreach(string revision in new[]{"\"r0\"","\"r01\"","\"r1000000\"","\"R54\"","\"r54x\"","54","true"})Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,contentPayload.Replace("\"release_revision\":\"r54\"","\"release_revision\":"+revision))==null,"invalid native release metadata: "+revision);
        Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,contentPayload.Insert(1,"\"unknown\":true,"))==null,"new fields do not relax unknown run fields");
        sdl.protocol="PAL98.TimerOnline.v1";sdl.gameplay.walk_frame_ms=90;
        Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,CompetitionProtocol.SerializeRun(sdl))==null,"SDL field cannot weaken original v1 schema");
        foreach(long? restart in new long?[]{null,0,321,86400000}) {
            relaxed.restart_ms=restart;
            string payload=CompetitionProtocol.SerializeRun(relaxed), signed=auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,payload);
            Check(signed!=null,"native seals nullable bounded restart: "+restart);
            var original=CompetitionProtocol.Json().Deserialize<Dictionary<string,object>>(signed);
            Check(CompetitionAuthProtocol.Decode((string)original["payload_base64"])==payload,"restart seal preserves complete original bytes");
        }
        foreach(long restart in new long[]{-1,86400001}) {
            relaxed.restart_ms=restart;
            Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,CompetitionProtocol.SerializeRun(relaxed))==null,"native rejects out-of-range restart: "+restart);
        }
        relaxed.restart_ms=null;
        var oldFields=CompetitionProtocol.Json().Deserialize<Dictionary<string,object>>(CompetitionProtocol.SerializeRun(relaxed));oldFields.Remove("restart_ms");
        string oldPayload=CompetitionProtocol.Json().Serialize(oldFields);
        Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,oldPayload)!=null,"native accepts original pre-restart-field v2");
        foreach(string invalid in new[]{"true","1.5","\"0\""})
            Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,oldPayload.Insert(1,"\"restart_ms\":"+invalid+","))==null,"native rejects non-integer restart: "+invalid);
        Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,v2.Insert(1,"\"arbitrary\":true,"))==null,"v2 arbitrary signing field refused");
        Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,v2.Replace("1100","86400001"))==null,"v2 capacity still bounded");
        Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,v2.Replace(relaxed.hwid,"win-"+new string('0',64)))==null,"v2 foreign device refused");
        Check(auth.Seal("https://fixture.invalid",CompetitionProtocol.Scope,v2.Replace("3.37.7.13","3.37.7.12"))==null,"v2 foreign producer refused");
        File.WriteAllText(Path.Combine(root,"identity.json"),CompetitionProtocol.Json().Serialize(identity));File.WriteAllText(Path.Combine(root,"seal.json"),seal);File.WriteAllText(Path.Combine(root,"proof.json"),proof);File.WriteAllText(Path.Combine(root,"challenge.json"),challengeJson);
    }
    static async Task Http(string root,string server,bool candidate=false)
    {
        var store=new CompetitionStorage(Path.Combine(root,"client"));var cfg=new CompetitionSettings{Enabled=true,Server=server};
        var client=new CompetitionClient(store,null,new NativeCompetitionAuth());client.Configure(cfg);
        if(candidate) {
            await Until(()=>File.Exists(Path.Combine(root,"candidate-observed.fixture")),"server observed a signed pending candidate");
            Check(client.ActivationText=="未激活，仅本地保存","automatic registration did not activate upload");
            client.Publish(Observation("before-approval",0,1200,10));await Task.Delay(400);client.Publish(Observation("before-approval",2,1200,10));
            await Until(()=>client.View.Pending==1,"unapproved native result remains local");
            File.WriteAllText(Path.Combine(root,"client-pending.fixture"),"fixture only");
            await Until(()=>File.Exists(Path.Combine(root,"approved.fixture")),"fixture administrator approved exact build");
            client.Retry();
            await Until(()=>Directory.Exists(Path.Combine(store.Root,"receipts"))&&Directory.GetFiles(Path.Combine(store.Root,"receipts"),"*.json",SearchOption.AllDirectories).Length==1,"pending native result uploads after approval");
        }
        await Until(()=>client.ActivationText=="联机上传：已激活","real service approved fixture build");
        await Until(()=>client.LiveCaption.Contains("[已连接:"),"native approved session visible in title");
        string liveCaption=client.LiveCaption;
        client.Invalidate("live-preflight");client.Publish(Observation("live-preflight",0,800,9));client.PublishClock("live-preflight",2500,true,Stopwatch.GetTimestamp());
        await Task.Delay(600);client.Publish(Observation("live-preflight",1,800,9));client.PublishClock("live-preflight",3500,true,Stopwatch.GetTimestamp());
        await Task.Delay(700);
        Check(client.LiveCaption==liveCaption,"node updates retain native live short ID");
        File.WriteAllText(Path.Combine(root,"live-observed.fixture"),"fixture only");
        int count=candidate?1:0;
        foreach(var pair in new[]{new[]{1200,10},new[]{800,10},new[]{800,9},new[]{1200,9}}) {
            string token=Guid.NewGuid().ToString("D");client.Invalidate(token);client.Publish(Observation(token,0,pair[0],pair[1]));await Task.Delay(400);client.Publish(Observation(token,1,pair[0],pair[1]));await Task.Delay(400);client.Publish(Observation(token,2,pair[0],pair[1])); ++count;
            await Until(()=>Directory.Exists(Path.Combine(store.Root,"receipts"))&&Directory.GetFiles(Path.Combine(store.Root,"receipts"),"*.json",SearchOption.AllDirectories).Length==count,"real signed upload "+pair[0]+"/"+pair[1]);
        }
        Check(!CompetitionOverlayText(client.View).Contains("第 "),"unpublished board stays unknown");
        foreach(var path in Directory.GetFiles(Path.Combine(store.Root,"receipts"),"*.json",SearchOption.AllDirectories))Check(File.ReadAllText(path).Contains("pending_publication"),"daily pending publication retained in receipt");
        Check(await client.CloseAsync(),"shutdown drains local records");
    }
    static string CompetitionOverlayText(CompetitionView view)=>view.Reply?.node?.best_complete_line?.rank?.ToString()??"未知";
    static int Main(string[] args) {try{Directory.CreateDirectory(args[0]);if(args.Length>1&&args[1]=="reject-host")Check(new NativeCompetitionAuth().Identity()==null,"modified EXE cannot reuse auth DLL");else if(args.Length>1)Http(args[0],args[1],args.Length>2&&args[2]=="candidate").GetAwaiter().GetResult();else Local(args[0]);Console.WriteLine("PASS total="+checks);return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
