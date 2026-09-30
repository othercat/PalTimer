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
    static void Cores() {
        var auto=new Pal98Dx9Automatic(null);Call(auto,"InitCheckPoints");
        var legacy=new 仙剑98柔情DX9(null);Call(legacy,"InitCheckPoints");
        Check(auto.CheckPoints.Select(p=>p.Name).SequenceEqual(legacy.CheckPoints.Select(p=>p.Name)),"automatic and legacy have identical ordered nodes");
        Check(auto.CheckPoints.All(p=>p.Best==TimeSpan.Zero),"new line starts with zero reference");
        Check(auto.CheckPoints.Select(p=>p.Check.Method).SequenceEqual(legacy.CheckPoints.Select(p=>p.Check.Method)),"automatic reuses original checkpoint predicates");
        Check(legacy.ActiveBestPath=="bestPAL98DX9.txt","legacy best filename unchanged");
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
            render.SetTitle("自动计时器"); render.SetVersion("3.37.7"); render.SetMainTimer(TimeSpan.Zero);
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
    static int Main(string[] args) { try { Decoder();Identity();Cores();Storage();LiveRanking();Presentation();
        foreach(var path in args){var bytes=File.ReadAllBytes(path);var decoded=GameplayModeReader.Decode(bytes,BitConverter.ToInt32(bytes,8),BitConverter.ToInt64(bytes,16),BitConverter.ToInt32(bytes,24),BitConverter.ToInt32(bytes,24));Check(decoded!=null&&decoded.covered,"actual native snapshot validates in managed reader: "+Path.GetDirectoryName(path));Check(decoded.ranking!=null&&decoded.ranking.Valid(decoded.Identity),"actual native online ranking validates independently of local timeline");}
        Console.WriteLine("CHECKS="+checks+" FAILURES=0");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;} }
}
