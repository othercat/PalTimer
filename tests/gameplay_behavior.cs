using Pal98Timer;
using HFrame.ENT;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal sealed class StorageAutomaticCore : Pal98Dx9Automatic
{
    internal StorageAutomaticCore() : base(null) { }
    // Storage cases supply synthetic run facts; gameplay validation is covered
    // separately by the real timing and hardcore hosts.
    public override string GetScoreValidationError() => "";
}
internal static class GameplayBehavior
{
    sealed class RestartAutomaticCore : Pal98Dx9Automatic
    {
        internal RestartAutomaticCore() : base(null) { }
        public override string GetScoreValidationError() => ""; // timing identity has its own host
        public override string GetGameVersion() => "restart integration fixture";
        internal override void CaptureCompetitionIdentity(out string hash,out string version,out int fade,out int speed,out string error)
        { hash=new string('a',64);version="1.7.2.0";fade=1200;speed=10;error=""; }
    }
    sealed class OnlineRecorder : IPalTimerOnlineV1
    {
        internal readonly List<OnlineSnapshotV1> Snapshots=new List<OnlineSnapshotV1>();
        internal int Clocks;
        public int ApiVersion => 1;
        internal bool Available = true, DropNext;
        public bool Enabled => Available;
        public string LiveCaption => "";
        public void ObserveTarget(int pid,long generation) { }
        public void Publish(OnlineSnapshotV1 snapshot) { if (DropNext) { DropNext = false; return; } Snapshots.Add(snapshot); }
        public void PublishClock(string token,long elapsed,bool running,long observedTick) { ++Clocks; }
        public void Invalidate(string token) { }
        public void ShowSettings(IWin32Window owner) { }
        public void UiTick() { }
        public System.Threading.Tasks.Task<bool> CloseAsync() => System.Threading.Tasks.Task.FromResult(true);
        public void Dispose() { }
    }
    static int checks;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
    static void Set(object value,string field,object content) { for(var type=value.GetType();type!=null;type=type.BaseType) { var f=type.GetField(field,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.DeclaredOnly); if(f!=null){f.SetValue(value,content);return;} } throw new Exception(field); }
    static object Get(object value,string field) { for(var type=value.GetType();type!=null;type=type.BaseType) { var f=type.GetField(field,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.DeclaredOnly); if(f!=null)return f.GetValue(value); } throw new Exception(field); }
    static object Call(object value,string method,params object[] args) { for(var type=value.GetType();type!=null;type=type.BaseType) {var m=type.GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);if(m!=null)return m.Invoke(value,args);}throw new Exception(method); }
    static string Rules(Dictionary<string,string> rules) => CompetitionProtocol.Hash("PAL98.GameplayRules.v1\n"+string.Concat(rules.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>GameplayModeReader.Quote(p.Key)+":"+GameplayModeReader.Quote(p.Value)+"\n")));
    static GameplaySnapshot Snapshot(string route, bool skills=false) {
        var rules=new Dictionary<string,string> { {"content",new string('b',64)},{"fade_ms","1200"},{"map_speed_ticks","10"},{"random_skills.enabled",skills?"1":"0"} };
        string hash=Rules(rules);
        return new GameplaySnapshot {schema="PAL98.GameplayMode.v1",covered=true,rules_sha256=hash,content_id="classic-v5",content_sha256=new string('b',64),family="standard",fade_ms=1200,map_speed_ticks=10,rules=rules,
            OrdinaryTimelineId=TimelineIdentity.Create(hash,route,false),HardcoreTimelineId=TimelineIdentity.Create(hash,route,true)};
    }
    static byte[] Frame(GameplaySnapshot snapshot,Process process) {
        var bytes=new byte[32808]; Action<int,byte[]> put=(at,value)=>Array.Copy(value,0,bytes,at,value.Length);
        put(0,BitConverter.GetBytes(0x314D5047u));put(4,BitConverter.GetBytes((ushort)1));put(6,BitConverter.GetBytes((ushort)40));
        put(8,BitConverter.GetBytes(process.Id));put(12,BitConverter.GetBytes(0x0106080Du));put(16,BitConverter.GetBytes(process.StartTime.ToUniversalTime().ToFileTimeUtc()));
        put(24,BitConverter.GetBytes(2));put(28,BitConverter.GetBytes(1u));var payload=Encoding.UTF8.GetBytes(CompetitionProtocol.Json().Serialize(snapshot));put(32,BitConverter.GetBytes(payload.Length));put(40,payload);return bytes;
    }
    static void Decoder() {
        using(var process=Process.GetCurrentProcess()) {
            string route=CompetitionProtocol.RouteHash(new[]{"鬼将军","拜月"});var s=Snapshot(route);var frame=Frame(s,process);
            long creation=process.StartTime.ToUniversalTime().ToFileTimeUtc();
            Check(GameplayModeReader.Decode(frame,process.Id,creation,2,2)?.covered==true,"bounded runtime frame accepted");
            var revision14=(byte[])frame.Clone();Array.Copy(BitConverter.GetBytes(0x0106080Eu),0,revision14,12,4);
            Check(GameplayModeReader.Decode(revision14,process.Id,creation,2,2)?.covered==true,"1.6.8.14 runtime frame accepted");
            var revision15=(byte[])frame.Clone();Array.Copy(BitConverter.GetBytes(0x0106080Fu),0,revision15,12,4);
            Check(GameplayModeReader.Decode(revision15,process.Id,creation,2,2)?.covered==true,"1.6.8.15 keeps the local gameplay reader compatible");
            var future=(byte[])frame.Clone();Array.Copy(BitConverter.GetBytes(0x01060810u),0,future,12,4);
            Check(GameplayModeReader.Decode(future,process.Id,creation,2,2)==null,"unknown future producer is not silently accepted");
            Check(GameplayModeReader.Decode(frame,process.Id+1,creation,2,2)==null,"PID reuse cannot transfer facts");
            Check(GameplayModeReader.Decode(frame,process.Id,creation+1,2,2)==null,"creation identity checked");
            Check(GameplayModeReader.Decode(frame,process.Id,creation,2,4)==null,"torn frame rejected");
            Check(GameplayModeReader.Decode(frame,process.Id,creation,3,3)==null,"writer-in-progress rejected");
            var bad=(byte[])frame.Clone();bad[44]^=1;Check(GameplayModeReader.Decode(bad,process.Id,creation,2,2)==null,"corrupt payload rejected");
            s.rules["random_skills.enabled"]="1";Check(GameplayModeReader.Decode(Frame(s,process),process.Id,creation,2,2)==null,"rules and digest must agree");
            s=Snapshot(route);
            using(var mmf=MemoryMappedFile.CreateNew("Local\\PAL98.GameplayMode.v1."+process.Id,32808))
            using(var view=mmf.CreateViewAccessor()) {
                view.WriteArray(0,Frame(s,process),0,32808);var reader=new GameplayModeReader {Route=route};reader.Observe(process);
                var wait=Stopwatch.StartNew();while(reader.Current==null&&wait.ElapsedMilliseconds<3000)Thread.Sleep(10);
                Check(reader.Current?.OrdinaryTimelineId==s.OrdinaryTimelineId,"background reader prepares identities before timing publication");
                var clock=Stopwatch.StartNew();for(int i=0;i<10000;i++){reader.Observe(process);var current=reader.Current;}clock.Stop();
                Check(clock.ElapsedMilliseconds<500,"10000 observations use cached facts: "+clock.ElapsedMilliseconds+" ms");
                var measurements=new List<object>();
                foreach(bool automatic in new[]{false,true}) {
                    GC.Collect();long memory=GC.GetTotalMemory(true);process.Refresh();var cpu=process.TotalProcessorTime;var times=new List<double>();
                    var total=Stopwatch.StartNew();
                    for(int batch=0;batch<100;batch++) {var slice=Stopwatch.StartNew();for(int i=0;i<1000;i++) {
                        if(automatic){reader.Observe(process);GC.KeepAlive(reader.Current);}else GC.KeepAlive(s);
                    }slice.Stop();times.Add(slice.Elapsed.TotalMilliseconds/1000);}
                    total.Stop();process.Refresh();times.Sort();
                    measurements.Add(new {mode=automatic?"cached-gameplay-observation":"baseline-reference",calls=100000,
                        elapsed_ms=total.Elapsed.TotalMilliseconds,cpu_ms=(process.TotalProcessorTime-cpu).TotalMilliseconds,
                        live_memory_delta_bytes=GC.GetTotalMemory(false)-memory,median_call_ms=times[50],p95_call_ms=times[95]});
                }
                File.WriteAllText("gameplay-performance.json",CompetitionProtocol.Json().Serialize(new {scope="offline cached observation microbenchmark, not game route acceptance",measurements}),Encoding.UTF8);
                reader.Observe(null);Check(reader.Current==null,"disconnect clears cached target immediately");
            }
        }
    }
    static void Identity() {
        string route=CompetitionProtocol.RouteHash(new[]{"鬼将军","拜月"});var a=Snapshot(route);var b=Snapshot(route);
        Check(a.OrdinaryTimelineId==b.OrdinaryTimelineId,"same gameplay across copies and devices shares identity");
        Check(a.HardcoreTimelineId!=a.OrdinaryTimelineId,"hardcore and ordinary never share best line");
        Check(Snapshot(route,true).OrdinaryTimelineId!=a.OrdinaryTimelineId,"random skills split timelines");
        foreach(string field in new[]{"random_skills.pool","love.enabled","module.fixture","village","card.fixture"}) {
            b=Snapshot(route);b.rules[field]="changed";Check(Rules(b.rules)!=a.rules_sha256,field+" affects rules");
        }
        Check(TimelineIdentity.Create(a.rules_sha256,CompetitionProtocol.RouteHash(new[]{"拜月","鬼将军"}),false)!=a.OrdinaryTimelineId,"route order splits timelines");
        var ids=new HashSet<string>();foreach(var speed in new[]{new[]{1200,10},new[]{800,10},new[]{800,9}}) {
            var s=Snapshot(route);s.rules["fade_ms"]=speed[0].ToString();s.rules["map_speed_ticks"]=speed[1].ToString();ids.Add(TimelineIdentity.Create(Rules(s.rules),route,false));
        }Check(ids.Count==3,"three speed categories isolated");
        b.rules=new Dictionary<string,string>(a.rules.Reverse().ToDictionary(p=>p.Key,p=>p.Value));Check(Rules(b.rules)==a.rules_sha256,"field ordering does not create a line");
    }
    static void LiveRanking()
    {
        var core=new StorageAutomaticCore();Call(core,"InitCheckPoints");string route=(string)Get(core,"route");
        var first=Snapshot(route);var changed=Snapshot(route);
        Func<GameplaySnapshot,string,RankingConfiguration> ranking=(snapshot,poison)=>{
            var rules=new Dictionary<string,string>(snapshot.rules) { ["family"]="standard",["mechanics.poison_zero"]=poison };
            return new RankingConfiguration {schema="PAL98.RankingConfiguration.v1",covered=true,rules=rules,configuration_id=RankingConfiguration.Digest(rules)};
        };
        first.ranking=ranking(first,"0");changed.ranking=ranking(changed,"1");
        Call(core,"SelectIdentity",first,false);Set(core,"frozen",first.OrdinaryTimelineId);
        Set(Get(core,"gameplayReader"),"current",changed);
        var observed=new CompetitionObservation();core.CaptureCompetitionGameplay(observed);
        Check(observed.Ranking.configuration_id==changed.ranking.configuration_id,"online facts observe a ranking-only change after local identity freezes");
        Check(observed.TimelineId==first.OrdinaryTimelineId&&core.ActiveBestPath.Contains(first.OrdinaryTimelineId),"new ranking contract does not repartition existing local best directories");
        changed.ranking.covered=false;observed=new CompetitionObservation();core.CaptureCompetitionGameplay(observed);
        Check(!string.IsNullOrEmpty(observed.ValidationError),"invalid current ranking cannot reuse frozen start eligibility");
        core.Unload();
    }
    static void RestartTimelineAndOnline()
    {
        var core=new RestartAutomaticCore();Call(core,"InitCheckPoints");string route=(string)Get(core,"route");
        var facts=Snapshot(route);var rankingRules=new Dictionary<string,string>(facts.rules) { ["family"]="standard" };
        facts.ranking=new RankingConfiguration { schema="PAL98.RankingConfiguration.v1",covered=true,rules=rankingRules,configuration_id=RankingConfiguration.Digest(rankingRules) };
        Func<uint,long,HardcoreSnapshot> segment=(pid,birth)=>{
            var bytes=new byte[72];Action<int,byte[]> put=(at,value)=>Array.Copy(value,0,bytes,at,value.Length);
            put(8,BitConverter.GetBytes(pid));put(12,BitConverter.GetBytes(0x01070200u));put(16,BitConverter.GetBytes(birth));
            put(28,BitConverter.GetBytes(2u));put(32,BitConverter.GetBytes(4u));put(36,BitConverter.GetBytes(1u));
            put(40,BitConverter.GetBytes(15u));put(56,BitConverter.GetBytes(100UL));
            return new HardcoreSnapshot(bytes,new string('a',64),new string('b',64),"fixture","");
        };
        var run=(HardcoreRunEvidence)Get(core,"hardcoreRun");var old=segment(100,1000);
        run.Observe(old,false);run.Observe(old,true,observedRuntimeIdentity:"dll|content");
        Call(core,"SelectIdentity",facts,true);Set(core,"frozen",facts.HardcoreTimelineId);Set(core,"frozenHardcore",true);
        var timer=(PTimer)Get(core,"MT");timer.SetTS(TimeSpan.FromSeconds(73));core.CheckPoints[0].SetCurrentTSForLoad(TimeSpan.FromSeconds(12));
        var ui=(GForm)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(GForm));
        var online=new OnlineRecorder();Set(ui,"core",core);Set(ui,"competition",online);Set(core,"form",ui);
        Set(Get(core,"gameplayReader"),"current",facts);
        Call(core,"PublishCompetitionIfChanged");
        Check(online.Snapshots.Count==1&&online.Snapshots[0].Hardcore.Verified&&string.IsNullOrEmpty(online.Snapshots[0].ValidationError),"actual online publisher starts with verified metadata");
        Set(core,"competitionPoll",0L);Call(core,"PublishCompetitionIfChanged");
        Check(online.Snapshots.Count==1,"unchanged metadata remains deduplicated within one heartbeat");
        online.Available=false;Call(core,"PublishCompetitionIfChanged");online.Available=true;
        Set(core,"competitionPoll",0L);Call(core,"PublishCompetitionIfChanged");
        Check(online.Snapshots.Count==2,"settings reconnection republishes identical metadata");
        online.DropNext=true;Set(core,"competitionMetadataPoll",0L);Set(core,"competitionPoll",0L);Call(core,"PublishCompetitionIfChanged");
        Check(online.Snapshots.Count==2,"fixture drops a publication during asynchronous reconnect");
        Set(core,"competitionMetadataPoll",System.Diagnostics.Stopwatch.GetTimestamp()-1);Set(core,"competitionPoll",0L);Call(core,"PublishCompetitionIfChanged");
        Check(online.Snapshots.Count==3,"periodic metadata heals a lost publication without a new node or focus change");
        for(int pass=0;pass<2;++pass) {
            var q=new HardcoreControlSnapshot { Pid=old.Pid,Creation=old.ProcessCreation,ProducerVersion=old.ProducerVersion,State=2,
                Ticket=Guid.NewGuid().ToString("N"),HelperPid=55,HelperCreation=555,RequestQpc=101 };
            var r=new HardcoreRestartSnapshot { Pid=q.Pid,Creation=q.Creation,ProducerVersion=q.ProducerVersion,State=1,
                Ticket=q.Ticket,HelperPid=q.HelperPid,HelperCreation=q.HelperCreation,ChangedQpc=102 };
            run.Observe(null,true,q,r,helperAlive:true);
            Set(Get(core,"gameplayReader"),"current",null);Set(Get(core,"onlineGameplayReader"),"current",null);
            int published=online.Snapshots.Count,clocks=online.Clocks,step=core.CurrentStep;
            Call(core,"ObserveGameplay");Set(core,"competitionPoll",0L);Call(core,"PublishCompetitionIfChanged");
            Check(online.Clocks==clocks+1&&online.Snapshots.Count==published,"pending P publishes clock without invalid metadata "+pass);
            Check((string)Get(core,"validation")==""&&(string)Get(core,"frozen")==facts.HardcoreTimelineId&&core.CurrentStep==step&&timer.CurrentTSOnly==TimeSpan.FromSeconds(73)&&core.CheckPoints[0].Current==TimeSpan.FromSeconds(12),"pending P preserves timeline time and nodes "+pass);
            r.State=2;r.NewPid=old.Pid+1;r.NewCreation=old.ProcessCreation+1;
            old=segment(r.NewPid,r.NewCreation);Set(Get(core,"onlineGameplayReader"),"current",facts);
            run.Observe(old,true,q,r,"dll|content",old.Pid,old.ProcessCreation,targetAlive:true);
            Call(core,"ObserveGameplay");
            Check((string)Get(core,"validation")==""&&core.CaptureHardcoreEvidence().run_verified,"verified reader bridges automatic reader startup after P "+pass);
            // Reconnection must refresh metadata itself, even if gameplay,
            // timing and the current node are identical to the old process.
            Set(core,"competitionPoll",0L);Call(core,"PublishCompetitionIfChanged");
            var resumed=online.Snapshots.Last();
            Check(online.Snapshots.Count==published+1&&resumed.Hardcore.Verified&&resumed.TimelineId==facts.HardcoreTimelineId&&string.IsNullOrEmpty(resumed.ValidationError),"resumed metadata remains eligible without sticky transition error "+pass);
        }
        run.Observe(null,true);Set(Get(core,"gameplayReader"),"current",facts);Call(core,"ObserveGameplay");
        Set(core,"competitionPoll",0L);Call(core,"PublishCompetitionIfChanged");
        Check(!online.Snapshots.Last().Hardcore.Verified&&!string.IsNullOrEmpty(online.Snapshots.Last().ValidationError),"genuine evidence failure leaves wait gate and reaches online review");
        core.Unload();
    }
    sealed class RestartLegacyCore : 仙剑98柔情DX9
    {
        internal RestartLegacyCore() : base(null) { }
        public override string GetScoreValidationError() => "";
        public override string GetGameVersion() => "ordinary legacy fixture";
        internal override void CaptureCompetitionIdentity(out string hash,out string version,out int fade,out int speed,out string error)
        { hash=new string('a',64);version="1.7.2.0";fade=1200;speed=10;error=""; }
    }
    static void OrdinaryRestart()
    {
        using(var original=Process.GetCurrentProcess())
        using(var replacement=Process.GetCurrentProcess()) {
            // Separate wrappers model reader target generations. No game is run.
            foreach(bool automatic in new[]{false,true}) {
                仙剑98柔情DX9 core=automatic?(仙剑98柔情DX9)new RestartAutomaticCore():new RestartLegacyCore();
                Call(core,"InitCheckPoints");
                string route=CompetitionProtocol.RouteHash(core.CheckPoints.Select(p=>CompetitionProtocol.CheckpointId(p.Name)));
                var facts=Snapshot(route);
                var rules=new Dictionary<string,string>(facts.rules) { ["family"]="standard" };
                facts.ranking=new RankingConfiguration {schema="PAL98.RankingConfiguration.v1",covered=true,rules=rules,configuration_id=RankingConfiguration.Digest(rules)};
                Set(core,"PalProcess",original);Set(core,"_IsFirstStarted",true);
                if(automatic) { Call(core,"SelectIdentity",facts,false);Set(core,"frozen",facts.OrdinaryTimelineId); }
                var reader=Get(core,"onlineGameplayReader");Set(reader,"target",original);Set(reader,"current",facts);Set(reader,"busy",true);
                if(automatic) { var autoReader=Get(core,"gameplayReader");Set(autoReader,"target",original);Set(autoReader,"current",facts);Set(autoReader,"busy",true); }
                Set(Get(core,"runtimeIntegrity"),"evidence",new RuntimeIntegrityEvidence {PalDllSha256=new string('a',64),PalDllVersion="1.7.2.0"});
                var timer=(PTimer)Get(core,"MT");timer.SetTS(TimeSpan.FromSeconds(73));core.CheckPoints[0].SetCurrentTSForLoad(TimeSpan.FromSeconds(12));
                int step=core.CurrentStep;
                var ui=(GForm)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(GForm));
                var online=new OnlineRecorder();Set(ui,"core",core);Set(ui,"competition",online);Set(core,"form",ui);
                Call(core,"PublishCompetitionIfChanged");
                Check(online.Snapshots.Count==1&&string.IsNullOrEmpty(online.Snapshots.Last().ValidationError),"ordinary publisher starts with accepted facts: "+automatic);
                Check(((GameplayContinuation)Get(core,"gameplayContinuation")).Snapshot==facts,"first publication retains its accepted gameplay for an immediate P: "+automatic);
                Process previous=original;
                for(int pass=0;pass<2;++pass) {
                    int count=online.Snapshots.Count;
                    Set(core,"PalProcess",null);Set(reader,"target",null);Set(reader,"current",null);
                    if(automatic) Set(Get(core,"gameplayReader"),"current",null);
                    Call(core,"ObserveGameplayContinuation",null,null,null,null);
                    if(automatic) Call(core,"ObserveGameplay");
                    Set(core,"competitionPoll",0L);Call(core,"PublishCompetitionIfChanged");
                    Check(core.CompetitionMetadataPending&&online.Snapshots.Count==count,"ordinary P gap does not publish a false invalid frame: "+automatic+"/"+pass);
                    if(automatic) {
                        Check((string)Get(core,"validation")==""&&(string)Get(core,"frozen")==facts.OrdinaryTimelineId,"ordinary P preserves frozen gameplay without sticky failure: "+pass);
                        var data=new HObj();Call(core,"FillMoreTimerData",data);
                        Check(!data.GetValue<bool>("GameplayVerified"),"pending ordinary restart cannot certify an export: "+pass);
                    }
                    var next=ReferenceEquals(previous,original)?replacement:original;
                    Set(core,"PalProcess",next);Set(reader,"target",next);
                    Call(core,"ObserveGameplayContinuation",next,null,null,null);
                    Check(core.CompetitionMetadataPending,"replacement waits for its own gameplay evidence: "+automatic);
                    Set(reader,"current",facts);Call(core,"ObserveGameplayContinuation",next,facts,null,null);
                    Check(core.CompetitionMetadataPending,"replacement gameplay alone cannot borrow the old DLL hash: "+automatic);
                    Call(core,"ObserveGameplayContinuation",next,facts,new string('a',64),"1.7.2.0");
                    if(automatic) Call(core,"ObserveGameplay");
                    Set(core,"competitionPoll",0L);Call(core,"PublishCompetitionIfChanged");
                    Check(!core.CompetitionMetadataPending&&online.Snapshots.Count==count+1&&string.IsNullOrEmpty(online.Snapshots.Last().ValidationError),"ordinary P resumes accepted metadata after both readers recover: "+automatic+"/"+pass);
                    Check(core.CurrentStep==step&&timer.CurrentTSOnly==TimeSpan.FromSeconds(73)&&core.CheckPoints[0].Current==TimeSpan.FromSeconds(12),"P continuation preserves stopwatch and checkpoint values: "+automatic);
                    previous=next;
                }
                // A real runtime replacement must still become a sticky error.
                Call(core,"ObserveGameplayContinuation",previous,facts,new string('b',64),"1.7.2.0");
                Call(core,"ObserveGameplayContinuation",previous,facts,new string('a',64),"1.7.2.0");
                Set(core,"competitionPoll",0L);Call(core,"PublishCompetitionIfChanged");
                Check(!string.IsNullOrEmpty(online.Snapshots.Last().ValidationError),"DLL mismatch remains invalid even after switching back: "+automatic);
                core.Reset();Check(((GameplayContinuation)Get(core,"gameplayContinuation")).Snapshot==null,"explicit Reset clears continuation identity: "+automatic);
                core.Unload();
            }
            foreach(string change in new[]{"rules","ranking","content","speed","coverage"}) {
                var facts=Snapshot(new string('f',64));
                var rules=new Dictionary<string,string>(facts.rules) { ["family"]="standard" };
                facts.ranking=new RankingConfiguration {schema="PAL98.RankingConfiguration.v1",covered=true,rules=rules,configuration_id=RankingConfiguration.Digest(rules)};
                var continuity=new GameplayContinuation();continuity.Observe(original,facts,new string('a',64),"1.7.2.0",true,false);
                var changed=Snapshot(new string('f',64));changed.ranking=facts.ranking;
                if(change=="rules")changed.rules_sha256=new string('d',64);
                if(change=="ranking")changed.ranking=new RankingConfiguration {covered=true,configuration_id=new string('d',64)};
                if(change=="content")changed.content_sha256=new string('d',64);
                if(change=="speed")changed.fade_ms=800;
                if(change=="coverage")changed.covered=false;
                continuity.Observe(replacement,changed,new string('a',64),"1.7.2.0",true,false);
                continuity.Observe(replacement,facts,new string('a',64),"1.7.2.0",true,false);
                Check(continuity.Error.Length>0&&!continuity.WaitingFor(replacement,true),"restart mismatch remains a visible failure: "+change);
            }
        }
    }
    static void ImmediateStart()
    {
        using (var process=Process.GetCurrentProcess())
        using (var mapping=MemoryMappedFile.CreateNew("Local\\PAL98.GameplayMode.v1."+process.Id,32808))
        using (var view=mapping.CreateViewAccessor()) {
            var core=new RestartAutomaticCore();Call(core,"InitCheckPoints");Set(core,"PalProcess",process);
            string route=(string)Get(core,"route");var origin=Snapshot(route);var changed=Snapshot(route,true);
            var reader=(GameplayModeReader)Get(core,"gameplayReader");
            view.WriteArray(0,Frame(origin,process),0,32808);
            var capture=reader.CaptureStart(process);
            view.WriteArray(0,Frame(changed,process),0,32808);
            var wait=System.Diagnostics.Stopwatch.StartNew();while(capture.Pending&&wait.ElapsedMilliseconds<3000)Thread.Sleep(5);
            Check(!capture.Pending&&capture.Snapshot?.rules_sha256==origin.rules_sha256,"start copy cannot be replaced by rules published after local timing begins");
            Check(reader.CurrentOrCapturedStart(capture)?.rules_sha256==origin.rules_sha256,"captured start bridges an earlier pending reader result");
            reader.Observe(process);
            wait.Restart();while(reader.Current?.rules_sha256!=changed.rules_sha256&&wait.ElapsedMilliseconds<3000)Thread.Sleep(5);
            Check(reader.CurrentOrCapturedStart(capture)?.rules_sha256==changed.rules_sha256,"later coherent rules take precedence over the start frame");
            view.Write(0,0);Set(reader,"next",0L);reader.Observe(process);
            wait.Restart();while(reader.Current!=null&&wait.ElapsedMilliseconds<3000)Thread.Sleep(5);
            Check(reader.CurrentOrCapturedStart(capture)==null,"a later invalid frame cannot borrow valid start evidence");
            foreach(bool hardcore in new[]{false,true}) {
                core.Reset();Set(core,"_IsFirstStarted",true);
                var decode=new System.Threading.Tasks.TaskCompletionSource<GameplaySnapshot>();
                Set(core,"startObservation",new GameplayStartObservation(decode.Task,capture.CapturedAt));
                Set(core,"startProcess",process);Set(core,"startHardcore",hardcore);
                Call(core,"FreezeStartIdentity");
                Check(core.CompetitionMetadataPending&&!(bool)Get(core,"startedWithoutIdentity"),"delayed decoding is pending, not a permanent unknown start: "+hardcore);
                decode.SetResult(capture.Snapshot);Call(core,"FreezeStartIdentity");
                Check(!core.CompetitionMetadataPending&&(string)Get(core,"frozen")== (hardcore?origin.HardcoreTimelineId:origin.OrdinaryTimelineId)&& (string)Get(core,"validation")=="",
                    "immediate local start keeps captured gameplay while server/decoder connects: "+hardcore);
            }
            core.Reset();Set(core,"_IsFirstStarted",true);
            Set(core,"startObservation",new GameplayStartObservation(System.Threading.Tasks.Task.FromResult<GameplaySnapshot>(null),System.Diagnostics.Stopwatch.GetTimestamp()));
            Call(core,"FreezeStartIdentity");Call(core,"SelectIdentity",origin,false);
            Check((bool)Get(core,"startedWithoutIdentity")&&Get(core,"identity")==null,"missing start frame cannot be certified using later facts");
            core.Unload();
        }
    }
    static GameplaySnapshot NativeFrame(string path)
    {
        var bytes=File.ReadAllBytes(path);
        return GameplayModeReader.Decode(bytes,BitConverter.ToInt32(bytes,8),BitConverter.ToInt64(bytes,16),
            BitConverter.ToInt32(bytes,24),BitConverter.ToInt32(bytes,24));
    }
    static void NativeStartupRestart(string directory,string prematureFrame)
    {
        var premature=NativeFrame(prematureFrame);
        var ready=NativeFrame(Path.Combine(directory,"ready-frame.bin"));
        var resumed=NativeFrame(Path.Combine(directory,"restart-frame.bin"));
        var invalid=NativeFrame(Path.Combine(directory,"invalid-frame.bin"));
        var changed=NativeFrame(Path.Combine(directory,"changed-frame.bin"));
        Check(premature?.covered==true&&premature.ranking?.covered==false,
            "old production publisher emits coherent but provisional ranking failure");
        Check(ready?.ranking?.covered==true&&resumed?.ranking?.covered==true,
            "fixed production first and replacement frames both pass the managed decoder");
        using(var original=Process.GetCurrentProcess())
        using(var replacement=Process.GetCurrentProcess()) {
            var negative=new GameplayContinuation();
            negative.Observe(original,ready,new string('a',64),"1.7.2.0",true,false);
            negative.Observe(replacement,premature,new string('a',64),"1.7.2.0",true,false);
            negative.Observe(replacement,resumed,new string('a',64),"1.7.2.0",true,false);
            Check(negative.Error=="本局玩法规则或内容已改变，保留未归类记录；请重置后开始新跑次。",
                "old native startup frame reproduces the exact sticky live failure");
            foreach(bool automatic in new[]{false,true}) {
                仙剑98柔情DX9 core=automatic?(仙剑98柔情DX9)new RestartAutomaticCore():new RestartLegacyCore();
                Call(core,"InitCheckPoints");
                string route=CompetitionProtocol.RouteHash(core.CheckPoints.Select(p=>CompetitionProtocol.CheckpointId(p.Name)));
                foreach(var facts in new[]{ready,resumed,invalid,changed}) {
                    facts.OrdinaryTimelineId=TimelineIdentity.Create(facts.rules_sha256,route,false);
                    facts.HardcoreTimelineId=TimelineIdentity.Create(facts.rules_sha256,route,true);
                }
                Set(core,"PalProcess",original);Set(core,"_IsFirstStarted",true);
                var timer=(PTimer)Get(core,"MT");timer.SetTS(TimeSpan.FromSeconds(73));
                core.CheckPoints[0].SetCurrentTSForLoad(TimeSpan.FromSeconds(12));
                int step=core.CurrentStep;
                var reader=Get(core,"onlineGameplayReader");Set(reader,"target",original);Set(reader,"current",ready);Set(reader,"busy",true);
                if(automatic) {
                    Call(core,"SelectIdentity",ready,false);Set(core,"frozen",ready.OrdinaryTimelineId);
                    var autoReader=Get(core,"gameplayReader");Set(autoReader,"target",original);Set(autoReader,"current",ready);Set(autoReader,"busy",true);
                }
                Set(Get(core,"runtimeIntegrity"),"evidence",new RuntimeIntegrityEvidence {PalDllSha256=new string('a',64),PalDllVersion="1.7.2.0"});
                var ui=(GForm)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(GForm));
                var online=new OnlineRecorder();Set(ui,"core",core);Set(ui,"competition",online);Set(core,"form",ui);
                Call(core,"PublishCompetitionIfChanged");
                Check(online.Snapshots.Count==1&&string.IsNullOrEmpty(online.Snapshots.Last().ValidationError),
                    "native initial facts publish accepted metadata: "+automatic);
                Set(core,"PalProcess",replacement);Set(reader,"target",replacement);Set(reader,"current",null);
                if(automatic) Set(Get(core,"gameplayReader"),"current",null);
                Call(core,"ObserveGameplayContinuation",replacement,null,null,null);
                if(automatic) Call(core,"ObserveGameplay");
                Set(core,"competitionPoll",0L);Call(core,"PublishCompetitionIfChanged");
                Check(core.CompetitionMetadataPending&&online.Snapshots.Count==1,
                    "fixed native initialization gap retains metadata without a false invalid update: "+automatic);
                Set(reader,"current",resumed);
                Call(core,"ObserveGameplayContinuation",replacement,resumed,new string('a',64),"1.7.2.0");
                if(automatic) Call(core,"ObserveGameplay");
                Set(core,"competitionPoll",0L);Call(core,"PublishCompetitionIfChanged");
                Check(!core.CompetitionMetadataPending&&online.Snapshots.Count==2&&string.IsNullOrEmpty(online.Snapshots.Last().ValidationError),
                    "fixed native replacement frame resumes online validity: "+automatic);
                Check(core.CurrentStep==step&&timer.CurrentTSOnly==TimeSpan.FromSeconds(73)&&core.CheckPoints[0].Current==TimeSpan.FromSeconds(12),
                    "native restart replay preserves stopwatch and route nodes: "+automatic);
                Call(core,"ObserveGameplayContinuation",replacement,invalid,new string('a',64),"1.7.2.0");
                Call(core,"ObserveGameplayContinuation",replacement,resumed,new string('a',64),"1.7.2.0");
                Set(core,"competitionPoll",0L);Call(core,"PublishCompetitionIfChanged");
                Check(!string.IsNullOrEmpty(online.Snapshots.Last().ValidationError),
                    "real native runtime coverage loss still reaches online validation: "+automatic);
                core.Unload();
            }
            var change=new GameplayContinuation();change.Observe(original,ready,new string('a',64),"1.7.2.0",true,false);
            change.Observe(replacement,changed,new string('a',64),"1.7.2.0",true,false);
            Check(change.Error.Length>0,"native effective ranking changes still invalidate continuation");
        }
    }
    static void Cores() {
        var auto=new Pal98Dx9Automatic(null);Call(auto,"InitCheckPoints");
        var legacy=new 仙剑98柔情DX9(null);Call(legacy,"InitCheckPoints");
        Check(auto.CheckPoints.Select(p=>p.Name).SequenceEqual(legacy.CheckPoints.Select(p=>p.Name)),"automatic and legacy have identical ordered nodes");
        Check(auto.CheckPoints.All(p=>p.Best==TimeSpan.Zero),"new line starts with zero reference");
        Check(auto.CheckPoints.Select(p=>p.Check.Method).SequenceEqual(legacy.CheckPoints.Select(p=>p.Check.Method)),"automatic reuses original checkpoint predicates");
        Check(Path.GetFullPath(legacy.ActiveBestPath)==Path.GetFullPath("bestPAL98DX9.txt"),"writable legacy best location and filename unchanged");
        string route=(string)Get(auto,"route");var a=Snapshot(route);Call(auto,"SelectIdentity",a,false);string first=auto.ActiveBestPath;
        Check(first.EndsWith(Path.Combine(a.OrdinaryTimelineId,"best.json")),"active best storage uses gameplay identity");
        Thread.Sleep(100);Set(auto,"frozen",a.OrdinaryTimelineId);Call(auto,"SelectIdentity",Snapshot(route,true),false);
        Check((string)Get(auto,"identity")==a.OrdinaryTimelineId&&auto.ActiveBestPath.Contains("Unclassified"),"mid-run rule change quarantines without replacing identity");
        Check(!File.Exists(first),"identity detection never creates or scans all timeline files");
        auto.Reset();Call(auto,"SelectIdentity",a,true);Check(auto.ActiveBestPath.EndsWith(Path.Combine(a.HardcoreTimelineId,"best.json")),"Reset can select separate hardcore timeline");
        string relay=(string)typeof(Pal98Dx9Automatic).GetProperty("RelayFileName",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(auto);
        Check(Path.GetDirectoryName(relay)==Path.GetDirectoryName(auto.ActiveBestPath),"relay and best use same identity directory");
        try { Call(auto,"ValidateTimerImport","{\"TimelineIdentity\":\"other\"}");throw new Exception("different relay accepted"); }
        catch(TargetInvocationException error) { Check(error.InnerException is InvalidDataException,"different timeline relay rejected before restore"); }
        auto.Reset();Set(auto,"startedWithoutIdentity",true);Call(auto,"SelectIdentity",a,false);Check(Get(auto,"identity")==null,"late facts cannot certify an unknown start");
        auto.Unload();legacy.Unload();
    }
    static void Storage()
    {
        var core=new StorageAutomaticCore();Call(core,"InitCheckPoints");string route=(string)Get(core,"route");var facts=Snapshot(route);
        Call(core,"SelectIdentity",facts,false);Thread.Sleep(100);
        string original=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"old-best-繁體.json");
        core.CheckPoints[0].SetCurrentTSForLoad(TimeSpan.FromSeconds(12));string originalJson=core.GetRStr();File.WriteAllText(original,originalJson,Encoding.UTF8);
        core.ImportReference(original);
        Check(File.ReadAllText(original,Encoding.UTF8)==originalJson,"legacy import leaves original bytes and identity alone");
        Check(core.CheckPoints[0].Best==TimeSpan.FromSeconds(12),"imported reference round-trips times");
        var reference=new HObj(File.ReadAllText(core.ActiveBestPath));
        Check(reference.GetValue<bool>("ReferenceTimeline")&&!reference.GetValue<bool>("GameplayVerified"),"import is reference only, never certified result");
        try {Call(core,"ValidateTimerImport",reference.ToJson());throw new Exception("reference accepted");}
        catch(TargetInvocationException e){Check(e.InnerException is InvalidDataException,"reference cannot restore a certified relay");}
        core.SaveBest();string ordinaryPath=core.ActiveBestPath;string saved=File.ReadAllText(ordinaryPath);
        Check(!new HObj(saved).HasValue("ReferenceTimeline"),"saving actual run replaces reference marker");
        var reopened=new StorageAutomaticCore();Call(reopened,"InitCheckPoints");Call(reopened,"SelectIdentity",facts,false);Thread.Sleep(100);reopened.RefreshBestReference();
        Check(reopened.CheckPoints[0].Best==TimeSpan.FromSeconds(12),"reopen loads same identity best file");
        reopened.Reset();Call(reopened,"SelectIdentity",Snapshot(route,true),false);Thread.Sleep(100);reopened.RefreshBestReference();
        Check(reopened.CheckPoints.All(p=>p.Best==TimeSpan.Zero),"different gameplay cannot inherit prior best");
        Set(core,"frozen",facts.OrdinaryTimelineId);Set(core,"completed",true);Set(core,"_CurrentStep",core.CheckPoints.Count);Call(core,"ObserveGameplay");
        Check(core.ActiveBestPath==ordinaryPath&&new HObj(core.GetRStr()).GetValue<bool>("GameplayVerified"),"closing game after completion preserves finished identity");
        Set(core,"archiveCompletedRun",true);Call(core,"ArchiveCompletedResult");var archive=(System.Threading.Tasks.Task)Get(core,"archiveTask");
        Check(archive!=null&&archive.Wait(3000),"completed local run archive finishes outside timing loop");
        Check(Directory.GetFiles(Path.GetDirectoryName(ordinaryPath),"*.json").Any(p=>Path.GetFileName(p)!="best.json"&&new HObj(File.ReadAllText(p)).GetValue<string>("TimelineIdentity")==facts.OrdinaryTimelineId),"archive retains full identity");
        core.Jump(0);Call(core,"ObserveGameplay");
        Check(!(bool)Get(core,"completed")&&core.ActiveBestPath.Contains("Unclassified"),"Jump resumes identity observation and cannot reuse completed evidence while disconnected");
        Call(core,"AdvanceScoreRunSequence",true);
        Check(!core.CanBeginCompetitionHere(0),"import before first node cannot become a new upload run");
        core.Reset();Check(core.CanBeginCompetitionHere(0),"Reset restores genuine new-run upload origin");
        Call(core,"SelectIdentity",facts,true);Thread.Sleep(100);core.SaveBest();
        Check(File.ReadAllText(ordinaryPath)==saved,"unverified hardcore never overwrites ordinary best");
        Check(File.Exists(TimelineIdentity.PathFor(AppDomain.CurrentDomain.BaseDirectory,facts.HardcoreTimelineId,false,"best.json")),"unverified hardcore saves to quarantine");
        core.Unload();reopened.Unload();
    }
    static void ContinuousStealPresentation()
    {
        string route = CompetitionProtocol.RouteHash(new[] { "鬼将军", "拜月" });
        var automatic = new Pal98Dx9Automatic(null);
        var legacy = new 仙剑98柔情DX9(null);
        var facts = Snapshot(route);
        facts.rules["prd.EnableScopedStealPrd"] = "1";
        facts.rules["prd.scoped_steal_policy"] = "battle-shared-continuous-70-exact-v4";
        Set(automatic, "selected", facts);
        var reader = (GameplayModeReader)Get(legacy, "onlineGameplayReader");
        Set(reader, "current", facts);
        foreach (var mode in new[] { new[] { 1200, 10 }, new[] { 800, 10 }, new[] { 800, 9 }, new[] { 1200, 9 } })
        {
            facts.fade_ms = mode[0]; facts.map_speed_ticks = mode[1];
            string expected = (mode[0] == 800 ? "0.8" : "1.2") + (mode[1] == 9 ? "+快走速A" : "+普通走速A");
            string before = Rules(facts.rules);
            foreach (var core in new 仙剑98柔情DX9[] { legacy, automatic })
            {
                Set(core, "lastConfirmedTimingMode", new RuntimeTimingMode(mode[0], mode[1], false,
                    "classic-v5", "1.0.22", facts.content_sha256, "速通 v5"));
                Check(((string)Call(core, "FormatGameTitle", "1.7.2")).Contains(expected), "title uses compact enabled A for " + expected);
                Check(core.GameplayWindowTitleSuffix == " " + expected, "native window title uses the same A mode");
                Check(core.AppendGameplayCodes("custom title") == "custom title A", "small-window header keeps A and user or plugin title");
                Check((string)Call(core, "GameplayTimingDisplayLabel") == expected, "OBS uses the same A mode");
                object previous = Get(core, "form");
                Set(core, "form", System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(GForm)));
                try { Check((string)Get(Call(core, "CreateDx9OverlaySnapshot"), "TimingModeLabel") == expected,
                    "real overlay snapshot carries A without an extra config read"); }
                finally { Set(core, "form", previous); }
                Check(!((string)Call(core, "FormatPaletteFadeVersion", "1.7.2")).Contains("A"), "structured DX9Version stays unchanged");
            }
            Check(Rules(facts.rules) == before, "display never changes canonical gameplay identity");
        }
        foreach (string value in new[] { "0", "requested-but-disabled", "", "1" })
        {
            facts.rules["prd.EnableScopedStealPrd"] = value;
            if (value == "1") facts.rules["prd.scoped_steal_policy"] = "battle-shared-continuous-70-exact-v3";
            Check(facts.DisplayCodes == "" && automatic.GameplayWindowTitleSuffix == "" && legacy.GameplayWindowTitleSuffix == "",
                "disabled or old theft policy does not claim A: " + value);
            Check(automatic.AppendGameplayCodes("custom title") == "custom title", "disabled A preserves configured header text");
            Check(!((string)Call(automatic,"GameplayTimingDisplayLabel")).Contains("A") &&
                !((string)Call(legacy,"GameplayTimingDisplayLabel")).Contains("A"), "disabled A clears both OBS paths");
        }
        facts.rules.Remove("prd.EnableScopedStealPrd");
        facts.rules["prd.EnablePlayerFleePrd"] = "1";
        facts.rules["prd.EnableBeehiveDropPrd"] = "1";
        Check(facts.DisplayCodes == "", "legacy bee and Q flags never display A");
        facts.rules["prd.EnableScopedStealPrd"] = "1";
        facts.rules["prd.scoped_steal_policy"] = "battle-shared-continuous-70-exact-v4";
        facts.covered = false;
        Check(facts.Label(false).Contains("A") && facts.Label(false).EndsWith("未归类"), "A does not invent ranking coverage");
        using (var process = Process.GetCurrentProcess()) { Set(reader,"target",process); reader.Observe(null); }
        Check(legacy.GameplayWindowTitleSuffix == "" && !((string)Call(legacy,"GameplayTimingDisplayLabel")).Contains("A"),
            "disconnect clears traditional title and OBS together");
        automatic.Reset();
        Check(automatic.GameplayWindowTitleSuffix == "" && !((string)Call(automatic,"GameplayTimingDisplayLabel")).Contains("A"),
            "reset clears automatic title and OBS together");
        automatic.Unload(); legacy.Unload();
    }
    static void Presentation()
    {
        var core = new Pal98Dx9Automatic(null);
        var legacy = new 仙剑98柔情DX9(null);
        Set(core, "DX9Version", "1.68 r14");
        Check(core.GetGameVersion() == "等待游戏运行", "no invented gameplay in disconnected caption");
        string route = CompetitionProtocol.RouteHash(new[] { "鬼将军", "拜月" });
        var facts = Snapshot(route);
        facts.rules["wuqiang"] = "1";
        foreach (var speed in new[] { new[] { 1200, 10 }, new[] { 800, 10 }, new[] { 800, 9 } })
        foreach (bool hardcore in new[] { false, true })
        {
            facts.fade_ms = speed[0]; facts.map_speed_ticks = speed[1];
            Set(core, "selected", facts); Set(core, "requested", hardcore);
            Set(core, "lastConfirmedTimingMode", new RuntimeTimingMode(speed[0], speed[1], true,
                "classic-v5", "1.0.22", facts.content_sha256, "速通 v5"));
            string modes = (speed[0] == 800 ? "0.8秒" : "1.2秒") + "&" +
                (speed[1] == 9 ? "快走速" : "普通走速") + "&吴强&" + (hardcore ? "硬核模式" : "普通模式");
            Check(core.GetGameVersion() == "98柔情原版 1.68－" + modes, "caption merges actual gameplay once with ampersands");
            Set(core, "lastConfirmedTournamentDisplayName", "吴强杯比赛专用");
            Check(core.GetGameVersion() == "吴强杯比赛专用－" + modes, "merged caption retains tournament identity");
            Set(core, "lastConfirmedTournamentDisplayName", "");
            Check(core.GetMoreInfo() == legacy.GetMoreInfo() && !core.GetMoreInfo().Contains("\n"),
                "bottom remains the original one-line item and encounter statistics");
            Check((string)Call(core, "FormatPaletteFadeVersion", "1.68") == "1.68" +
                Dx9TimingCategory.Suffix(core.RecordedTimingMode), "structured version suffix does not gain gameplay text");
        }
        Set(core, "requested", false);
        facts.rules["prd.EnableScopedStealPrd"] = "1";
        facts.rules["prd.scoped_steal_policy"] = "battle-shared-continuous-70-exact-v4";
        string example = "[测试版] " + core.GetGameVersion();
        facts.family = "drawcard";
        foreach (string key in new[] { "random_items", "random_skills.enabled", "love.enabled", "village" }) facts.rules[key] = "1";
        string longTitle = "[随机数待核验][测试版] " + core.GetGameVersion();
        foreach (string label in new[] { "抽卡", "随机物品", "随机技能", "爱无限", "村村通" })
            Check(longTitle.Contains(label), "merged caption retains " + label);
        Set(core, "selected", null);
        Check(core.GetGameVersion().EndsWith("&未归类"), "pending gameplay is not presented as ordinary mode");
        Set(core, "selected", facts); Set(core, "timingRunInvalidated", true);
        Check(core.GetScoreValidationError().Length != 0 && core.GetGameVersion() == core.GetScoreValidationError(),
            "existing timing warning takes priority over gameplay caption");

        foreach (var size in new[] { new Size(346, 916), new Size(270, 200), new Size(560, 916) })
        using (var panel = new Panel { Size = size })
        using (var board = new GBoard())
        {
            var render = new GRender(panel); render.SetGBoard(board);
            render.SetTitle(core.AppendGameplayCodes("自动计时器")); render.SetVersion("3.37.8"); render.SetMainTimer(TimeSpan.Zero);
            render.SetMoreInfo(legacy.GetMoreInfo()); render.SetSubTimer("0.00s");
            render.AddBtn("隐藏", null); render.AddBtn("重置", null); render.AddBtn("功能", null); render.AddBtn("云", null);
            if (size.Height > 250) {
                Call(legacy, "InitCheckPoints");
                foreach (var point in legacy.CheckPoints) render.AddItem(point.Name, TimeSpan.Zero);
            }
            render.SetGameVersion(""); render.Draw();
            var header = (Rectangle)Get(render, "rcGameVersion");
            using (var empty = new Bitmap(panel.BackgroundImage))
            {
                render.SetGameVersion(example); render.Draw();
                SavePreview(panel, "caption-" + size.Width + "x" + size.Height + ".png");
                Check((string)Get(render, "MoreInfo") == legacy.GetMoreInfo(), "renderer does not duplicate gameplay at the bottom");
                render.SetGameVersion(longTitle); render.Draw();
                var expandedHeader = (Rectangle)Get(render, "rcGameVersion");
                var items = (Rectangle)Get(render, "rcItems");
                var dots = (Rectangle)Get(render, "rcDots");
                Check(dots.Top >= expandedHeader.Bottom && items.Top >= dots.Bottom,
                    "wrapped caption reserves space before indicators and checkpoint rows");
                bool bounded = true;
                using (var current = new Bitmap(size.Width, size.Height))
                using (var graphics = Graphics.FromImage(current))
                {
                    Set(render, "isGameVersionChanged", true);
                    Call(render, "DrawGameVersion", graphics, null);
                    for (int y = 0; y < size.Height; ++y)
                    for (int x = 0; x < size.Width; ++x)
                        if (!expandedHeader.Contains(x, y) && current.GetPixel(x, y).A != 0) bounded = false;
                }
                Check(bounded, "long header does not paint over other rows at " + size);
                SavePreview(panel, "caption-long-" + size.Width + "x" + size.Height + ".png");
                foreach (string connection in new[] { " [已连接:1042]", " [连接失败]" }) {
                    render.SetGameVersion(example + connection); render.Draw();
                    var liveHeader = (Rectangle)Get(render, "rcGameVersion");
                    Check(((Rectangle)Get(render, "rcDots")).Top >= liveHeader.Bottom,
                        "online suffix preserves wrapped caption spacing");
                    SavePreview(panel, (connection.Contains(":") ? "live-connected-" : "live-failed-") + size.Width + "x" + size.Height + ".png");
                }
                render.SetGameVersion(""); render.Draw();
                bool restored = true;
                using (var current = new Bitmap(panel.BackgroundImage))
                    for (int y = header.Top; y < header.Bottom; ++y)
                    for (int x = header.Left; x < header.Right; ++x)
                        if (empty.GetPixel(x, y) != current.GetPixel(x, y)) restored = false;
                Check(restored, "shortened caption clears all previous title pixels");
                Check(!render.Draw(), "unchanged caption does not trigger another repaint");
            }
            ((Graphics)Get(render, "CG")).Dispose(); panel.BackgroundImage.Dispose();
        }
        core.Unload(); legacy.Unload();
    }
    static void SavePreview(Panel panel, string filename)
    {
        using (var bitmap = new Bitmap(panel.Width, panel.Height))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black); graphics.DrawImageUnscaled(panel.BackgroundImage, 0, 0);
            bitmap.Save(filename, ImageFormat.Png);
        }
    }
    [STAThread]
    static int Main(string[] args) { try { Decoder();Identity();Cores();Storage();LiveRanking();RestartTimelineAndOnline();OrdinaryRestart();ImmediateStart();ContinuousStealPresentation();Presentation();
        for(int i=0;i<args.Length;++i) {
            if(args[i]=="--startup") { NativeStartupRestart(args[++i],args[++i]);continue; }
            var decoded=NativeFrame(args[i]);Check(decoded!=null&&decoded.covered,"actual native snapshot validates in managed reader: "+Path.GetDirectoryName(args[i]));Check(decoded.ranking!=null&&decoded.ranking.Valid(decoded.Identity),"actual native online ranking validates independently of local timeline");
        }
        Console.WriteLine("CHECKS="+checks+" FAILURES=0");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;} }
}
