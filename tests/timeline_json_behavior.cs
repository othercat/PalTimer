using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HFrame.ENT;
using Pal98Timer;
using System.Web.Script.Serialization;

// Refs #68. Tests the published EXE; no game, process attachment, timer loop,
// GForm construction, cloud client or live user settings are used.
internal sealed class JsonTimelineCore : TimerCore
{
    private readonly string path;
    internal string Note;
    internal JsonTimelineCore(string target) : base(null)
    {
        path=target;CoreName="ISSUE68_OFFLINE";
        CheckPoints=new List<CheckPoint>{new CheckPoint(0,new CheckPointNewer{Name="offline",NickName="离线节点",BestTS=TimeSpan.Zero})};
    }
    public override string ActiveBestPath {get{return path;}}
    internal static string StatisticsText(HObj data){return ReadSavedRunStatistics(data);}
    internal void RestoreStatistics(HObj data){RestoreRunStatistics(ReadSavedRunStatistics(data));}
    protected override void FillMoreTimerData(HObj data){data["note"]=Note;}
    protected override void InitCheckPoints(){}
    public override void InitUI(){}
    protected override void OnTick(){}
    public override void OnFunctionKey(int n){}
    public override string GetAAction(){return "";}
    public override bool IsShowC(){return false;}
    public override bool NeedBlockCtrlEnter(){return false;}
    public override string GetCriticalError(){return "";}
    public override string GetMoreInfo(){return "";}
    public override string GetSmallWatch(){return "";}
    public override string GetSecondWatch(){return "";}
    public override bool IsMainWatchStar(){return false;}
    public override string GetGameVersion(){return "OFFLINE TEST";}
}
internal static class TimelineJsonBehavior
{
    static int checks;
    static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=4*1024*1024};
    static Dictionary<string,object> Read(string text){return Json.Deserialize<Dictionary<string,object>>(text);}
    static string Statistics(JsonTimelineCore core){return (string)Read(core.GetRStr())["RunStatisticsState"];}
    static long Deaths(string text){return Convert.ToInt64(((System.Collections.IList)Read(text)["deaths"])[0]);}
    static void Sample(JsonTimelineCore core,long deaths)
    {
        var assembly=typeof(TimerCore).Assembly;
        var type=assembly.GetType("Pal98Timer.RunStatisticsSample",true);
        object sample=Activator.CreateInstance(type,true);
        var values=new Dictionary<string,object>{{"key","issue68-offline"},{"source","sdlpal-events-v1"},
            {"deaths",new long[]{deaths,0,0,0,0,0}},{"defeats",deaths},{"controlled",(uint)1},
            {"gaps",(uint)0},{"money",(long?)123456},{"settled",true}};
        foreach(var pair in values)type.GetProperty(pair.Key).SetValue(sample,pair.Value,null);
        object accumulator=typeof(TimerCore).GetField("runStatistics",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(core);
        accumulator.GetType().GetMethod("Observe",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(accumulator,new[]{sample,(object)(deaths!=2),(object)false,(object)false});
    }
    static void SaveText(JsonTimelineCore core,string text)
    {
        try{typeof(TimerCore).GetMethod("SaveBestValue",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(core,new object[]{text});}
        catch(TargetInvocationException error){throw error.InnerException;}
    }
    static int Main(string[] args)
    {
        string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        try
        {
            string[] values={"","中文与侑梦","quote\"apostrophe'",@"C:\PAL\Saves\1.rpg","line1\r\nline2\t\b\f\0",
                "emoji \ud83d\ude00","html <>&' / slash","trailing\\","\"","\\",new string('x',300000)};
            foreach(string value in values)
            {
                var data=new HObj();string key="key\"\\\r\n";data[key]=value;
                string text=data.ToJson();Check((string)Read(text)[key]==value,"key/string JSON escaping length="+value.Length);
                if(!value.StartsWith("{")&&!value.StartsWith("["))Check(new HObj(text).GetValue<string>(key)==value,"HObj string readback length="+value.Length);
                var list=new HObj();list.Add(value);data=new HObj();data["array"]=list;
                Check(((System.Collections.IList)Read(data.ToJson())["array"])[0].Equals(value),"array string escaping length="+value.Length);
            }
            foreach(char value in new[]{'"','\\','\n','\0','中'})
            {
                var data=new HObj();data["char"]=value;
                Check((string)Read(data.ToJson())["char"]==value.ToString(),"character JSON escaping U+"+((int)value).ToString("X4"));
            }
            string path=Path.Combine(output,"best.txt");
            var core=new JsonTimelineCore(path){Note="带引号\"和路径C:\\PAL\\save\r\n换行"};
            core.SetTS(TimeSpan.FromMilliseconds(123456));
            Sample(core,2);Sample(core,4294967301L);
            string exported=core.GetRStr();var wire=Read(exported);
            Check(wire["RunStatisticsState"] is string,"statistics wire field remains a JSON string");
            Check((string)wire["note"]==core.Note,"actual GetRStr escapes diagnostic text");
            string state=(string)wire["RunStatisticsState"];
            Check(Deaths(state)==4294967299L,"actual timeline preserves 64-bit counters");
            Check(JsonTimelineCore.StatisticsText(new HObj(exported))!=null,"HObj nested-string promotion is accepted on read");
            var restored=new JsonTimelineCore(Path.Combine(output,"restored.txt"));
            restored.RestoreStatistics(new HObj(exported));
            Check(Deaths(Statistics(restored))==4294967299L,"statistics restore from escaped timeline retains counters");
            var objectWire=Read(exported);objectWire["RunStatisticsState"]=Read(state);
            var restoredObject=new JsonTimelineCore(Path.Combine(output,"object-form.txt"));
            restoredObject.RestoreStatistics(new HObj(Json.Serialize(objectWire)));
            Check(Deaths(Statistics(restoredObject))==4294967299L,"legacy object-form statistics remain readable");
            const string legacy="{\"Current\":\"00:01:23.456\",\"Step\":-1,\"CheckPoints\":[{\"name\":\"offline\",\"des\":\"旧时间线\",\"time\":\"00:00:00.000\"}]}";
            Check(JsonTimelineCore.StatisticsText(new HObj(legacy))==null,"older timeline without statistics remains readable");
            SaveText(restored,legacy);
            Check(File.ReadAllText(restored.ActiveBestPath)==legacy,"older timeline still saves through production storage");
            const string sentinel="{\"keep\":true}";
            File.WriteAllText(path,sentinel,new UTF8Encoding(false));
            TimeSpan before=core.GetMainWatch();core.SaveBest();
            Check(core.GetMainWatch()==before,"saving does not reset or alter the current clock");
            var saved=Read(File.ReadAllText(path));
            Check(Deaths((string)saved["RunStatisticsState"])==4294967299L,"actual SaveBest writes readable statistics");
            Check(Directory.GetFiles(output,"best-*.txt").Any(p=>File.ReadAllText(p)==sentinel),"replacement preserves previous PB backup");
            byte[] good=File.ReadAllBytes(path);
            try{SaveText(core,"{\"broken\":}");throw new Exception("bad JSON accepted");}catch(Exception e){Check(e.Message!="bad JSON accepted","malformed timeline is refused");}
            Check(File.ReadAllBytes(path).SequenceEqual(good),"malformed timeline cannot overwrite PB");
            bool failed=false;
            using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None))
            {try{core.SaveBest();}catch(IOException){failed=true;}}
            Check(failed,"locked PB reports save failure");
            Check(File.ReadAllBytes(path).SequenceEqual(good),"failed replacement preserves original PB bytes");
            Check(Directory.GetFiles(output,"best.txt.pending-*").Length==1,"failed replacement retains recoverable pending timeline");
            foreach(string pending in Directory.GetFiles(output,"best.txt.pending-*"))Check(Read(File.ReadAllText(pending))["RunStatisticsState"] is string,"pending timeline is complete valid JSON");
            File.WriteAllText(Path.Combine(output,"valid-export.json"),exported,new UTF8Encoding(false));
            string report="PASS "+checks+" checks; actual production EXE; gameProcess=0 gameInput=0 network=0";
            File.WriteAllText(Path.Combine(output,"result.txt"),report,Encoding.UTF8);Console.WriteLine(report);return 0;
        }
        catch(Exception error)
        {File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString(),Encoding.UTF8);Console.WriteLine(error.GetType().FullName+": "+error.Message);return 1;}
    }
}
