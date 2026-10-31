using Pal98Timer;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

[assembly: AssemblyVersion("3.37.7.2")]
[assembly: AssemblyFileVersion("3.37.7.2")]
internal static class NativeProbe
{
    static int checks;
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
    static CompetitionRun Run(string hwid)
    {
        return new CompetitionRun { run_id = Guid.NewGuid().ToString("D"), hwid = hwid, track_id = "wuqiang", ruleset_id = "wuqiang-2026-v1",
            route_sha256 = CompetitionProtocol.RouteHash(new[] { "鬼将军", "拜月" }), started_at = DateTimeOffset.UtcNow.AddSeconds(-2).ToString("o"),
            finished_at = DateTimeOffset.UtcNow.ToString("o"), total_ms = 1100, timer_version = "3.37.7.2", game_version = "1.6.8.12", pal_dll_sha256 = new string('1',64),
            splits = new[] { new CompetitionSplit { checkpoint_id = "鬼将军", elapsed_ms = 500, status = "completed" }, new CompetitionSplit { checkpoint_id = "拜月", elapsed_ms = 1200, status = "completed" } } };
    }
    static async Task Until(Func<bool> condition, string name, int milliseconds = 15000)
    {
        var watch = Stopwatch.StartNew(); while (!condition() && watch.ElapsedMilliseconds < milliseconds) await Task.Delay(100);
        Check(condition(), name);
    }
    static CompetitionObservation Observation(string token, int step, int fade, int speed)
    {
        return new CompetitionObservation { Token=token,Core="PAL98DX9",Step=step,BeganHere=true,Finished=step==2,
            TotalMilliseconds=1100,DllHash=new string('1',64),GameVersion="1.6.8.12",FadeMilliseconds=fade,MapSpeedTicks=speed,ObservedAt=DateTimeOffset.UtcNow,ValidationError="",
            Splits=new[]{new CompetitionSplit{checkpoint_id="鬼将军",elapsed_ms=step>0?(long?)500:null,status=step>0?"completed":"in_progress"},
            new CompetitionSplit{checkpoint_id="拜月",elapsed_ms=step==2?(long?)1200:null,status=step==2?"completed":"in_progress"}}};
    }
    static async Task Http(string root,string server)
    {
        var auth = new NativeCompetitionAuth(); Check(auth.Identity()!=null,"real native identity available");
        File.WriteAllText(Path.Combine(root,"identity.json"),CompetitionProtocol.Json().Serialize(auth.Identity()));
        var store=new CompetitionStorage(Path.Combine(root,"client"));var cfg=new CompetitionSettings { Enabled=true,Server=server };
        var client=new CompetitionClient(store,null,auth);client.Configure(cfg);
        await Until(()=>client.ActivationText=="未激活，仅本地保存","HTTP unknown build stays unactivated");
        client.Publish(Observation("before-approval",0,1200,10));await Task.Delay(350);client.Publish(Observation("before-approval",1,1200,10));await Task.Delay(350);client.Publish(Observation("before-approval",2,1200,10));
        await Until(()=>store.LoadPending(cfg).Any(p=>p.SealedRun!=null),"real native run sealed before approval");
        File.WriteAllText(Path.Combine(root,"ready-for-approval"),"fixture ready");
        await Until(()=>File.Exists(Path.Combine(root,"approved")),"fixture administrator approves exact build",120000);
        client.Retry();await Until(()=>Directory.Exists(Path.Combine(store.Root,"receipts"))&&Directory.GetFiles(Path.Combine(store.Root,"receipts"),"*.json",SearchOption.AllDirectories).Length==1,"unapproved original native record uploads after approval");
        foreach(var mode in new[]{new[]{800,10},new[]{800,9}}){int before=Directory.GetFiles(Path.Combine(store.Root,"receipts"),"*.json",SearchOption.AllDirectories).Length;string token=Guid.NewGuid().ToString();client.Invalidate(token);client.Publish(Observation(token,0,mode[0],mode[1]));await Task.Delay(350);client.Publish(Observation(token,2,mode[0],mode[1]));await Until(()=>Directory.GetFiles(Path.Combine(store.Root,"receipts"),"*.json",SearchOption.AllDirectories).Length>before,"native HTTP track "+mode[0]+"/"+mode[1]);}
        Check(client.ActivationText=="比赛上传：已激活","settings show independent activation");
        await client.CloseAsync();
    }
    static void Local(string root)
    {
        var auth=new NativeCompetitionAuth();var identity=auth.Identity();Check(identity!=null&&identity.Valid,"native verifies exact host executable");
        string hwid=CompetitionStorage.ReadWindowsIdentity();var run=Run(hwid);string payload=CompetitionProtocol.SerializeRun(run);
        string sealedJson=auth.Seal("https://fixture.invalid","wuqiang",payload);Check(sealedJson!=null,"native seals actual derived HWID and UTF8 route");
        var parsed=CompetitionAuthProtocol.ReadSeal(CompetitionAuthProtocol.Encode(sealedJson),new CompetitionSettings { Server="https://fixture.invalid",Event="wuqiang" },run,payload);
        Check(parsed.timer_exe_sha256==identity.timer_exe_sha256,"producer hash originates in native component");
        var challenge=new {protocol=CompetitionAuthProtocol.Name,@event="wuqiang",server_origin="https://fixture.invalid",hwid=hwid,key_id=identity.key_id,timer_exe_sha256=identity.timer_exe_sha256,
            challenge_id=Guid.NewGuid().ToString("D"),nonce=new string('d',64),request_sha256=CompetitionProtocol.Hash(sealedJson),expires_at=DateTimeOffset.UtcNow.AddMinutes(2).ToString("o")};
        string challengeJson=CompetitionProtocol.Json().Serialize(challenge);string proof=auth.Prove(sealedJson,challengeJson);Check(proof!=null,"native answers payload-bound challenge");
        Check(auth.Seal("https://fixture.invalid","wuqiang",payload.Replace(hwid,"win-"+new string('0',64)))==null,"native rejects caller-supplied other HWID");
        Check(auth.Seal("https://fixture.invalid","wuqiang",payload.Replace("3.37.7.2","3.37.7.0"))==null,"native refuses signing another producer version");
        Check(auth.Seal("http://example.com","wuqiang",payload)==null,"native refuses insecure remote scope");
        Check(auth.Seal("https://fixture.invalid","wuqiang",payload.Replace("\"run_id\":","\"run_id\":\"duplicate\",\"run_id\":"))==null,"native rejects duplicate JSON fields");
        Check(auth.Prove(sealedJson,challengeJson.Replace("fixture.invalid","another.invalid"))==null,"native rejects cross-server proof");
        Check(auth.Prove(sealedJson,challengeJson.Replace(challenge.request_sha256,new string('0',64)))==null,"native rejects substituted request digest");
        run.protocol=CompetitionProtocol.NameV2;
        run.gameplay=new GameplayIdentity { rules_sha256=new string('a',64),content_id="fixture",content_sha256=new string('b',64),family="standard",fade_ms=1200,map_speed_ticks=10 };
        run.hardcore=new CompetitionHardcore { requested=true,run_verified=true,rules_version=4,evidence_status="runtime_observed" };
        run.timeline_id=TimelineIdentity.Create(run.gameplay.rules_sha256,run.route_sha256,true);
        string v2=CompetitionProtocol.SerializeRun(run);
        string v2seal=auth.Seal("https://fixture.invalid","wuqiang",v2);
        Check(v2seal!=null,"native accepts complete v2 gameplay and continuous hardcore evidence");
        Check(auth.Seal("https://fixture.invalid","wuqiang",v2.Replace("\"rules_version\":4","\"rules_version\":5"))==null,"native rejects unknown hardcore revision");
        Check(auth.Seal("https://fixture.invalid","wuqiang",v2.Replace(run.timeline_id,new string('0',64)))==null,"native rejects substituted timeline");
        Check(auth.Seal("https://fixture.invalid","wuqiang",v2.Replace("\"requested\":true","\"requested\":false"))==null,"native rejects inconsistent hardcore claim");
        Check(auth.Seal("https://fixture.invalid","wuqiang",v2.Replace("\"fade_ms\":1200","\"fade_ms\":800"))==null,"native rejects different speed track");
        File.WriteAllText(Path.Combine(root,"v2-seal.json"),v2seal);
        File.WriteAllText(Path.Combine(root,"identity.json"),CompetitionProtocol.Json().Serialize(identity));
        File.WriteAllText(Path.Combine(root,"seal.json"),sealedJson);File.WriteAllText(Path.Combine(root,"challenge.json"),challengeJson);File.WriteAllText(Path.Combine(root,"proof.json"),proof);
        var clock=Stopwatch.StartNew();for(int i=0;i<20;i++)Check(auth.Seal("https://fixture.invalid","wuqiang",payload)!=null,"repeated native seal "+i);clock.Stop();
        Console.WriteLine("20 native signatures wall_ms="+clock.ElapsedMilliseconds);
    }
    static int Main(string[] args)
    {
        try {Directory.CreateDirectory(args[0]);if(args.Length>1&&args[1]=="reject-host")Check(new NativeCompetitionAuth().Identity()==null,"copied component rejects modified executable");else if(args.Length>1)Http(args[0],args[1]).GetAwaiter().GetResult();else Local(args[0]);Console.WriteLine("PASS total="+checks);return 0;}
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
