using HFrame.ENT;
using Pal98Timer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Windows.Forms;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.7.2")]

// The success-path restore body is exercised without its optional PAL attach
// probe. Policy is checked with the real category before calling this body.
// Rejection tests below use the unmodified public and relay entry points.
internal sealed class OfflineRestoreCore : 仙剑98柔情DX9
{
    internal int Expected;
    internal OfflineRestoreCore() : base(null) { }
    protected override int TimingModeMs { get { return Expected; } }
}
internal sealed class EmptySnapshotProvider<T> { public T Get() { return default(T); } }

internal static class V165TimingBehavior
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly Assembly Product = typeof(TimerCore).Assembly;
    static readonly Type Dx9 = typeof(仙剑98柔情DX9);
    static readonly Type Reader = Product.GetType("Pal98Timer.TimingModeReader", true);
    static readonly Type Category = Product.GetType("Pal98Timer.Dx9TimingCategory", true);
    static Process Host;
    static long Creation;
    static MemoryMappedViewAccessor View;
    static int Assertions, Passed, Failed;
    static string Root, Repo;
    static readonly List<string> Failures = new List<string>();

    static void Check(bool value, string message) { ++Assertions; if (!value) throw new Exception(message); }
    static object Call(object target, string name, params object[] args)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        { var method = type.GetMethod(name, Instance | BindingFlags.DeclaredOnly); if (method != null) return method.Invoke(target, args); }
        throw new MissingMethodException(name);
    }
    static object Field(object target, string name)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        { var field = type.GetField(name, Instance | BindingFlags.DeclaredOnly); if (field != null) return field.GetValue(target); }
        throw new MissingFieldException(name);
    }
    static void Set(object target, string name, object value)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        { var field = type.GetField(name, Instance | BindingFlags.DeclaredOnly); if (field != null) { field.SetValue(target, value); return; } }
        throw new MissingFieldException(name);
    }
    static Exception Unwrap(Exception ex) { while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException; return ex; }
    static void Reject(Action action, string reason)
    {
        try { action(); }
        catch (Exception ex) { ex = Unwrap(ex); Check(ex is InvalidDataException || ex is InvalidOperationException, reason + ": unexpected " + ex.GetType()); return; }
        Check(false, reason + ": accepted");
    }
    static void Scenario(string name, Action action)
    {
        string directory = Path.Combine(Root, name); Directory.CreateDirectory(directory); Directory.SetCurrentDirectory(directory);
        int before = Assertions;
        try { action(); ++Passed; Console.WriteLine("PASS " + name + " (" + (Assertions - before) + " assertions)"); }
        catch (Exception ex) { ex = Unwrap(ex); ++Failed; Failures.Add(name + ": " + ex.Message); Console.WriteLine("FAIL " + name + ": " + ex.Message + "\n" + ex.StackTrace); }
    }
    static void Put(byte[] bytes, int offset, byte[] value) { Buffer.BlockCopy(value, 0, bytes, offset, value.Length); }
    static void Text(byte[] bytes, int offset, string value) { Put(bytes, offset, Encoding.UTF8.GetBytes(value)); }
    static byte[] Bytes(int fade, int speed, bool official, string id = "pal98fix-classic-v5", string version = "1.0.0", string hash = null, string name = "原版速通v5包")
    {
        var bytes = new byte[592];
        Put(bytes, 0, BitConverter.GetBytes(0x324D5450u)); Put(bytes, 4, BitConverter.GetBytes((ushort)2));
        Put(bytes, 6, BitConverter.GetBytes((ushort)592)); Put(bytes, 8, BitConverter.GetBytes(Host.Id));
        Put(bytes, 12, BitConverter.GetBytes(fade)); Put(bytes, 16, BitConverter.GetBytes(Creation));
        Put(bytes, 24, BitConverter.GetBytes(0x01060500u)); Put(bytes, 28, BitConverter.GetBytes(speed));
        Put(bytes, 32, BitConverter.GetBytes(official ? 1 : 2));
        Text(bytes, 40, id); Text(bytes, 200, version); Text(bytes, 264, hash ?? new string('a', 64)); Text(bytes, 329, name);
        return bytes;
    }
    static object Decode(byte[] bytes, int? pid = null, long? creation = null)
    { return Reader.GetMethod("DecodeV2", Static).Invoke(null, new object[] { bytes, pid ?? Host.Id, creation ?? Creation }); }
    static object Mode(int fade, int speed, bool official, string id = "pal98fix-classic-v5", string version = "1.0.0", string hash = null, string name = "原版速通v5包")
    { return Decode(Bytes(fade, speed, official, id, version, hash, name)); }
    static void Attach(TimerCore core, object mode)
    {
        Check(mode != null, "Harness requires valid decoded mode");
        byte[] bytes = Bytes((int)Field(mode,"FadeMilliseconds"), (int)Field(mode,"MapSpeedTicks"),
            (bool)Field(mode,"OfficialSpeedrun"), (string)Field(mode,"ContentId"), (string)Field(mode,"ContentVersion"),
            (string)Field(mode,"ContentHash"), (string)Field(mode,"DisplayName"));
        View.WriteArray(0,bytes,0,bytes.Length);
        Call(Field(core,"paletteFadeMode"),"Read",new object[]{null});
        Set(core,"PalProcess",Host); Set(core,"lastConfirmedTimingMode",mode); Set(core,"PID",Host.Id); Set(core,"DX9Version","1.65");
    }
    static TimerCore Core(int index)
    {
        TimerCore core = index == 0 ? (TimerCore)new 仙剑98柔情DX9(null) : index == 1 ? new Pal98Dx9Fast800(null) : (TimerCore)new Pal98Dx9Fast800Speed(null);
        Call(core,"InitCheckPoints"); return core;
    }
    static int Fade(int index) { return index == 0 ? 1200 : 800; }
    static int Speed(int index) { return index == 2 ? 9 : 10; }
    static string Label(int fade,int speed) { return (fade==1200?"1.2秒":"0.8秒")+(speed==9?"&快走速":""); }
    static string Error(int expected,int speed,object actual)
    { return (string)Category.GetMethod("RuntimeError",Static).Invoke(null,new[]{(object)expected,speed,actual}); }
    static void Validate(string core,int fade,HObj record,object actual)
    { Category.GetMethod("ValidateImport",Static).Invoke(null,new[]{(object)core,fade,record,actual}); }
    static HObj Record(TimerCore core,object mode) { Attach(core,mode); return new HObj(core.GetRStr()); }
    static PTimer Watch(TimerCore core) { return (PTimer)Field(core,"MT"); }
    static HObj Clone(HObj record) { return new HObj(record.ToJson()); }
    static object Snapshot(TimerCore core)
    {
        object previous=Field(core,"form"); Set(core,"form",FormatterServices.GetUninitializedObject(typeof(GForm)));
        try { return Call(core,"CreateDx9OverlaySnapshot"); } finally { Set(core,"form",previous); }
    }

    static void SnapshotContract()
    {
        foreach(bool official in new[]{true,false}) foreach(int fade in new[]{1200,800}) foreach(int speed in new[]{10,9})
        {
            object mode=Decode(Bytes(fade,speed,official)); bool valid=!official||fade==800||speed==10;
            Check((mode!=null)==valid,"Mode contract "+official+"/"+fade+"/"+speed);
            if(mode!=null) Check((int)Field(mode,"FadeMilliseconds")==fade&&(int)Field(mode,"MapSpeedTicks")==speed,"Decoded timing changed");
        }
        var good=Bytes(1200,9,false,"pal98.package.random","1.2.3",new string('B',64),"完全隨機物品&快走速");
        Check((string)Field(Decode(good),"ContentHash")==new string('b',64),"Hash case normalization");
        Check((string)Field(Decode(good),"DisplayName")=="完全隨機物品&快走速","UTF-8 name/ampersand");
        Check(Decode(good,Host.Id+1)==null,"Foreign PID"); Check(Decode(good,creation:Creation+1)==null,"PID reuse/creation time");
        Check(Decode(null)==null&&Decode(new byte[591])==null&&Decode(new byte[593])==null,"Fixed mapping size");
        foreach(int offset in new[]{0,4,6,8,12,16,24,28,32})
        { var bad=(byte[])good.Clone(); Array.Clear(bad,offset,offset==4||offset==6?2:4); Check(Decode(bad)==null,"Invalid header field "+offset); }
        foreach(int speed in new[]{0,7,8,11,-1})
        { var bad=(byte[])good.Clone(); Put(bad,28,BitConverter.GetBytes(speed)); Check(Decode(bad)==null,"Unsupported walking speed "+speed); }
        foreach(int offset in new[]{36,199,263,328,584,585,591})
        { var bad=(byte[])good.Clone(); bad[offset]=1; Check(Decode(bad)==null,"Reserved/string padding "+offset); }
        foreach(int offset in new[]{40,264,329})
        { var bad=(byte[])good.Clone(); bad[offset]=0; Check(Decode(bad)==null,"Missing required identity text "+offset); }
        var utf8=(byte[])good.Clone();utf8[329]=0xff;Check(Decode(utf8)==null,"Invalid UTF-8");
        var hash=(byte[])good.Clone();hash[264]=(byte)'g';Check(Decode(hash)==null,"Non-hex hash");
        Check(Decode(new byte[32])==null,"V1 cannot be decoded as V2");
    }
    static void LiveReader()
    {
        object reader=Activator.CreateInstance(Reader,true);var good=Bytes(800,9,false,"pal98.package.random");
        View.WriteArray(0,good,0,good.Length);Check(Call(reader,"Read",Host)!=null,"Read host-bound mapping");
        var bad=(byte[])good.Clone();Put(bad,8,BitConverter.GetBytes(Host.Id+1));View.WriteArray(0,bad,0,bad.Length);Set(reader,"nextRetry",0L);
        Check(Call(reader,"Read",Host)==null,"Invalid update clears cached identity");
        View.WriteArray(0,good,0,good.Length);Call(reader,"Read",new object[]{null});
        Check(Call(reader,"Read",Host)!=null,"Detach/reattach refreshes mode");Check(Call(reader,"Read",new object[]{null})==null,"Detach clears mode");
        View.WriteArray(0,new byte[592],0,592);
        using(var oldTiming=MemoryMappedFile.CreateNew("Local\\PAL98.TimingMode.v1."+Host.Id,32))
        using(var oldFade=MemoryMappedFile.CreateNew("Local\\PAL98.PaletteFadeMode.v1."+Host.Id,32))
        using(var oldView=oldTiming.CreateViewAccessor())
        {
            var old=new byte[32];Put(old,0,BitConverter.GetBytes(0x314D5450u));Put(old,4,BitConverter.GetBytes((ushort)1));
            Put(old,6,BitConverter.GetBytes((ushort)32));Put(old,8,BitConverter.GetBytes(Host.Id));Put(old,12,BitConverter.GetBytes(800));
            Put(old,16,BitConverter.GetBytes(Creation));Put(old,24,BitConverter.GetBytes(0x01060300u));Put(old,28,BitConverter.GetBytes(9));oldView.WriteArray(0,old,0,32);
            Check(Call(reader,"Read",Host)==null,"Old interface fallback forbidden");
            object legacy=Reader.GetMethod("Decode",Static).Invoke(null,new object[]{old,Host.Id,Creation});
            Check(Error(800,9,legacy).Length!=0,"Legacy mode lacks content proof");
        }
    }
    static void CategoryMatrix()
    {
        for(int selected=0;selected<3;++selected)
        {
            for(int actual=0;actual<3;++actual)
                Check((Error(Fade(selected),Speed(selected),Mode(Fade(actual),Speed(actual),true)).Length==0)==(selected==actual),"Three formal categories strict");
            foreach(int fade in new[]{1200,800})foreach(int speed in new[]{10,9})
                Check(Error(Fade(selected),Speed(selected),Mode(fade,speed,false,"pal98.package.random")).Length==0,"Non-speedrun combination allowed");
            Check(Error(Fade(selected),Speed(selected),null).Length!=0,"Unknown mode pauses");
        }
        Check(Error(0,0,null)=="","Other legacy cores unchanged");
        Check(TimerCore.GetAllCores().Contains("Pal98Dx9Fast800Speed"),"Third established core exists");
        Check(!TimerCore.GetAllCores().Any(n=>n.Contains("1200Speed")||n.Contains("NonSpeedrun")),"No new core/timeline");
        Check(TimerCore.GetAllCores().Any(n=>n.Contains("Hunqian")||n.Contains("魂牵")),"Hunqian core retained");
        Check(TimerCore.GetAllCores().Any(n=>n.Contains("Dream220")||n.Contains("梦幻")),"Dream core retained");
    }
    static void RejectedRestore(TimerCore target,int category,object actual,HObj incoming,string name)
    {
        Attach(target,actual);
        // Prove rejection first, so no test can fall through to a live PAL probe.
        Reject(()=>Validate(target.CoreName,Fade(category),incoming,actual),name+" policy");
        var times=target.CheckPoints.Select(p=>p.Current).ToArray();long sequence=target.ScoreRunSequence;
        TimeSpan watch=target.GetMainWatch();int step=target.CurrentStep;
        Reject(()=>((仙剑98柔情DX9)target).SetTimerFromString(incoming.ToJson()),name+" public restore");
        Check(sequence==target.ScoreRunSequence&&watch==target.GetMainWatch()&&step==target.CurrentStep&&target.CheckPoints.Select(p=>p.Current).SequenceEqual(times),name+" changed timer state");
        string rpg=Path.GetFullPath("1.RPG"),sidecar=Path.GetFullPath("1.RPG.metadata.json");
        File.WriteAllText(rpg,"save-sentinel");File.WriteAllText(sidecar,"sidecar-sentinel");
        Set(target,"WillCopyRPG","copy-sentinel");Set(target,"LoadedSrpgRequiresGameRestart",true);
        using(var file=File.Create("rejected.bin"))new BinaryFormatter().Serialize(file,new SRPGobj{TimerStr=incoming.ToJson(),RPG=new byte[]{9,8,7}});
        Reject(()=>Call(target,"LoadGame","rejected.bin",rpg),name+" relay restore");
        Check(File.ReadAllText(rpg)=="save-sentinel"&&File.ReadAllText(sidecar)=="sidecar-sentinel",name+" changed save/sidecar");
        Check((string)Field(target,"WillCopyRPG")=="copy-sentinel"&&(bool)Field(target,"LoadedSrpgRequiresGameRestart"),name+" changed pending relay state");
        Check(sequence==target.ScoreRunSequence&&target.CheckPoints.Select(p=>p.Current).SequenceEqual(times),name+" relay changed timer");
    }
    static void CrossImports()
    {
        var cores=Enumerable.Range(0,3).Select(Core).ToArray();
        var modes=Enumerable.Range(0,3).Select(i=>Mode(Fade(i),Speed(i),true)).ToArray();
        var records=Enumerable.Range(0,3).Select(i=>Record(cores[i],modes[i])).ToArray();
        for(int target=0;target<3;++target)for(int from=0;from<3;++from)
            if(target==from){Validate(cores[target].CoreName,Fade(target),records[from],modes[target]);Check(true,"Same category accepted");}
            else RejectedRestore(cores[target],target,modes[target],records[from],"formal-"+from+"-to-"+target);
    }
    static void ContentImports()
    {
        TimerCore core=Core(0);
        var variants=new[]{Mode(1200,9,false,"pal98.package.random"),Mode(1200,9,false,"pal98.package.wuqiang"),
            Mode(1200,9,false,"pal98.package.random","2.0.0"),Mode(1200,9,false,"pal98.package.random",hash:new string('b',64))};
        var source=Record(core,variants[0]);
        for(int i=1;i<variants.Length;++i)RejectedRestore(core,0,variants[i],source,"content-change-"+i);
        var formal=Mode(1200,10,true);var formalRecord=Record(core,formal);
        RejectedRestore(core,0,Mode(1200,10,true,"pal98fix-classic-v4"),formalRecord,"formal-content-change");
        var modes=new[]{Mode(1200,10,false,"pal98.package.random"),Mode(1200,9,false,"pal98.package.random"),Mode(800,10,false,"pal98.package.random"),Mode(800,9,false,"pal98.package.random")};
        var records=modes.Select(m=>Record(core,m)).ToArray();
        for(int target=0;target<4;++target)for(int from=0;from<4;++from)
            if(target!=from)RejectedRestore(core,0,modes[target],records[from],"nonformal-mode-"+from+"-to-"+target);
        foreach(string key in new[]{"TimingRulesVerified","ReferenceTimeline","TimingValidationError","ContentHash","OfficialSpeedrun"})
        {
            var invalid=Clone(source);
            if(key=="TimingValidationError")invalid[key]="unconfirmed";else if(key=="ContentHash")invalid.Remove(key);else invalid[key]=key!="TimingRulesVerified";
            RejectedRestore(core,0,variants[0],invalid,"invalid-"+key);
        }
    }
    static void RunIdentity()
    {
        foreach(object next in new[]{Mode(800,10,false,"pal98.package.a"),Mode(1200,9,false,"pal98.package.b"),
            Mode(1200,9,false,"pal98.package.a","2.0.0"),Mode(1200,9,false,"pal98.package.a",hash:new string('b',64))})
        {
            var core=Core(0);var original=Mode(1200,9,false,"pal98.package.a");
            Attach(core,original);Check(core.GetScoreValidationError()=="","Initial mode accepted");
            Watch(core).SetTS(TimeSpan.FromSeconds(5));Check(core.GetScoreValidationError()=="","Started mode captured");
            Attach(core,next);Check(core.GetScoreValidationError().Contains("重置"),"Run change must latch invalidation");
            Attach(core,original);Check(core.GetScoreValidationError().Contains("重置"),"Reverting does not undo invalidation");
            Reject(()=>core.SaveBest(),"Invalidated SaveBest");Reject(()=>core.ForCloudBigData(),"Invalidated cloud export");
            core.Reset();Attach(core,next);Check(core.GetScoreValidationError()=="","Reset clears invalidation");
        }
        var unchanged=Core(0);var a=Mode(1200,9,false,"pal98.package.a");Attach(unchanged,a);unchanged.GetScoreValidationError();
        Attach(unchanged,Mode(800,10,false,"pal98.package.b"));Check(unchanged.GetScoreValidationError()=="","Pre-start change allowed");
        Watch(unchanged).SetTS(TimeSpan.FromSeconds(7));unchanged.GetScoreValidationError();
        Set(unchanged,"PalProcess",null);Set(unchanged,"lastConfirmedTimingMode",null);
        Check(unchanged.GetScoreValidationError().Contains("待确认"),"Unknown runtime pauses");
        Attach(unchanged,Mode(800,10,false,"pal98.package.b"));Check(unchanged.GetScoreValidationError()=="","Unknown is not proof of category change");
    }
    static void LegacyRecords()
    {
        var mode=Mode(1200,10,true);var core=Core(0);var legacy=Record(core,mode);
        foreach(string key in new[]{"TimingRulesVersion","MapSpeedTicks","TimingMapSpeedTicks","TimingRulesVerified","ContentId","ContentVersion","ContentHash","ContentDisplayName","OfficialSpeedrun","LeaderboardCategory","TimerCore","TimingModeMs","PaletteFadeModeMs"})legacy.Remove(key);
        Validate("PAL98DX9",1200,legacy,mode);
        Reject(()=>Validate("PAL98DX9_800",800,legacy,Mode(800,10,true)),"Untagged legacy stays classic");
        Reject(()=>Validate("PAL98DX9_800_SPEED",800,legacy,Mode(800,9,true)),"Legacy never becomes fast walking");
        Reject(()=>Validate("PAL98DX9",1200,legacy,Mode(1200,10,false,"pal98.package.random")),"Legacy not transferred to other content");
        var fast=Core(1);var fastMode=Mode(800,10,true);var oldFast=Record(fast,fastMode);
        foreach(string key in new[]{"TimingRulesVersion","MapSpeedTicks","TimingMapSpeedTicks","TimingRulesVerified"})oldFast.Remove(key);
        Validate(fast.CoreName,800,oldFast,fastMode);
        Reject(()=>Validate("PAL98DX9_800_SPEED",800,oldFast,Mode(800,9,true)),"Old 0.8 line stays separate");
        var restore=new OfflineRestoreCore();Call(restore,"InitCheckPoints");Attach(restore,mode);
        restore.SetTimerFromString(legacy.ToJson());restore.Expected=1200;
        Check(restore.GetScoreValidationError().Contains("仅供参考"),"Restored history is unverified");
        Check(!new HObj(restore.GetRStr()).GetValue<bool>("TimingRulesVerified"),"Re-export cannot verify legacy proof");
        Reject(()=>restore.SaveBest(),"Restored history is not a new valid score");
        restore.Reset();Attach(restore,mode);Check(restore.GetScoreValidationError()=="","Reset allows new verified run");
        var oldEight=Record(Core(2),Mode(800,9,true));oldEight["TimingRulesVersion"]=1;oldEight["MapSpeedTicks"]=8;oldEight["TimingMapSpeedTicks"]=8;
        for(int i=0;i<3;++i)Reject(()=>Validate(Core(i).CoreName,Fade(i),oldEight,Mode(Fade(i),Speed(i),true)),"Old 8-tick records not converted");
    }
    static void StorageTransactions()
    {
        for (int i = 0; i < 3; ++i)
        {
            var core = Core(i);
            Attach(core, Mode(Fade(i), Speed(i), true));
            var point = core.CheckPoints[0];
            var checker = point.Check;
            string name = point.Name;
            long run = core.ScoreRunSequence;
            point.NickName = "自定义节点" + i;
            point.Current = TimeSpan.FromSeconds(111 + i);
            var item = new GRender.GItem(0, name, TimeSpan.Zero);
            item.Cur = point.Current;
            point.SetUIItem(item);
            string active = "best" + core.CoreName + ".txt";
            for (int n = 0; n < 4; ++n) core.SaveBest();
            Check(Directory.GetFiles(".", "best" + core.CoreName + "-*.txt").Length == 3,
                "Repeated saves have unique recoverable archives " + i);
            Check(point.Best == point.Current && item.Best == point.Current && item.Name == point.NickName,
                "Successful save immediately refreshes current reference and UI " + i);
            Check(point.Name == name && ReferenceEquals(point.Check, checker) && core.ScoreRunSequence == run,
                "Reference refresh preserves route and run identity " + i);
            var reopened = Core(i);
            Check(reopened.CheckPoints[0].GetNickName() == point.NickName && reopened.CheckPoints[0].Name == name,
                "UTF8 nickname reopens without changing route key " + i);
            byte[] before = File.ReadAllBytes(active);
            TimeSpan reference = point.Best;
            point.Current = TimeSpan.FromSeconds(222 + i);
            Action mustFail = () => {
                bool rejected = false;
                try { core.SaveBest(); }
                catch (IOException) { rejected = true; }
                catch (UnauthorizedAccessException) { rejected = true; }
                Check(rejected, "Failed replacement is reported " + i);
                Check(core.ScoreRunSequence == run && point.Current == TimeSpan.FromSeconds(222 + i) && point.Best == reference,
                    "Failed save retains running time and old reference " + i);
            };
            using (var locked = new FileStream(active, FileMode.Open, FileAccess.Read, FileShare.Read)) mustFail();
            Check(File.ReadAllBytes(active).SequenceEqual(before), "Locked target preserves bytes " + i);
            File.SetAttributes(active, FileAttributes.ReadOnly);
            try { mustFail(); }
            finally { File.SetAttributes(active, FileAttributes.Normal); }
            Check(File.ReadAllBytes(active).SequenceEqual(before), "Read-only target preserves bytes " + i);
            core.SaveBest();
            Check(Core(i).CheckPoints[0].Best == point.Current, "Retry after failure saves the same run " + i);
        }
    }

    static void StorageIsolation()
    {
        var cores=Enumerable.Range(0,3).Select(Core).ToArray();
        Check(cores[1].CheckPoints.All(p=>p.Best==TimeSpan.Zero)&&cores[2].CheckPoints.All(p=>p.Best==TimeSpan.Zero),"Fast references start empty");
        for(int i=0;i<3;++i)
        {
            cores[i].CreateBestReferenceIfMissing();
            Check(!new HObj(File.ReadAllText("best"+cores[i].CoreName+".txt")).GetValue<bool>("TimingRulesVerified"),"Offline line is not verified");
            cores[i].CheckPoints[0].Current=TimeSpan.FromSeconds(100+i);Attach(cores[i],Mode(Fade(i),Speed(i),true));cores[i].SaveBest();
            var reopened=Core(i);Check(reopened.CheckPoints[0].Best==TimeSpan.FromSeconds(100+i),"Independent best save/reopen "+i);
        }
        var before=cores.ToDictionary(c=>"best"+c.CoreName+".txt",c=>File.ReadAllBytes("best"+c.CoreName+".txt"));
        for(int selected=0;selected<3;++selected)foreach(int fade in new[]{1200,800})foreach(int speed in new[]{10,9})
        {
            Attach(cores[selected],Mode(fade,speed,false,"pal98.package.random",name:"完全随机物品"));cores[selected].SaveBest();
            foreach(var file in before)Check(File.ReadAllBytes(file.Key).SequenceEqual(file.Value),"Non-speedrun did not overwrite "+file.Key);
        }
        var files=Directory.GetFiles("Records","*.txt");Check(files.Length==12,"Non-speedrun saves have records, not best/core");
        foreach(string file in files)
        {
            var data=new HObj(File.ReadAllText(file));Check(data.GetValue<bool>("TimingRulesVerified")&&!data.GetValue<bool>("OfficialSpeedrun"),"Nonformal record verified as own content");
            Check(data.GetValue<string>("LeaderboardCategory")=="","No traditional leaderboard");
            Check(data.GetValue<string>("ContentId")=="pal98.package.random","Content identity retained");
            Reject(()=>Call(cores[0],"ValidateBestReference",data),"Non-speedrun rejected as traditional reference");
        }
        string[] relay={"SRPG.bin","SRPG.PAL98DX9_800.bin","SRPG.PAL98DX9_800_SPEED.bin"};
        for(int i=0;i<3;++i)Check((string)Dx9.GetProperty("RelayFileName",Instance).GetValue(cores[i])==relay[i],"Relay identity preserved");
    }
    static void PresentationAndCompletion()
    {
        foreach(string culture in new[]{"zh-CN","zh-TW"})
        {
            Thread.CurrentThread.CurrentUICulture=CultureInfo.GetCultureInfo(culture);
            for(int selected=0;selected<3;++selected)
            {
                var core=Core(selected);Attach(core,Mode(Fade(selected),Speed(selected),true));
                Dx9.GetProperty("TournamentDisplayName",Instance).SetValue(core,"秋季杯比赛专用");
                string label=Label(Fade(selected),Speed(selected));
                Check(core.GetGameVersion()=="秋季杯比赛专用-"+label,"Tournament includes actual mode");
                var snapshot=Snapshot(core);
                Check((string)Field(snapshot,"TimingModeLabel")==label,"Overlay has no leading hyphen and retains &");
                Check(!((string)Field(snapshot,"State")).Contains(label),"Mode not crowded into state row");
                var record=new HObj(core.GetRStr());
                Check(record.GetValue<string>("DX9Version").EndsWith("-"+label),"Export matches displayed mode");
                Check(record.GetValue<string>("GameVersion")==core.GetGameVersion()&&record.GetValue<string>("TournamentDisplayName")=="秋季杯比赛专用","Export preserves tournament identity");
                Check(record.GetValue<string>("LeaderboardCategory")==new[]{"PC NewPatch Classic","PC NewPatch Classic-0.8s","PC NewPatch Classic-0.8s+Speed"}[selected],"English leaderboard");
                Watch(core).SetTS(TimeSpan.FromMinutes(30));Watch(core).Stop();Set(core,"PalProcess",null);Set(core,"PID",-1);
                Dx9.GetProperty("TournamentDisplayName",Instance).SetValue(core,"");
                var ended=new HObj(core.ForCloudBigData());
                Check(ended.GetValue<bool>("TimingRulesVerified")&&ended.GetValue<string>("ContentId")=="pal98fix-classic-v5","Ended export retains identity");
                Check(core.GetGameVersion()=="秋季杯比赛专用-"+label,"Completed tournament lost its display identity");
                Check(ended.GetValue<string>("GameVersion")==core.GetGameVersion()&&ended.GetValue<string>("TournamentDisplayName")=="秋季杯比赛专用","Completed export preserves tournament identity");
            }
            var nonformal=Core(0);Attach(nonformal,Mode(1200,9,false,"pal98.package.random",name:"完全隨機物品&快走速"));
            Check(nonformal.GetGameVersion()=="仙剑98原版 新补丁 1.65-1.2秒&快走速","Nonformal caption retains only version and actual mode");
            foreach(string contentName in new[]{"完全随机物品","完全隨機物品","全随机技能","全隨機技能","完全随机物品+全随机技能"})
            {
                var random=Core(0);Attach(random,Mode(1200,9,false,"pal98.package.random",name:contentName));
                Check(random.GetGameVersion()=="仙剑98原版 新补丁 1.65-1.2秒&快走速","Random labels do not lengthen caption");
                var randomRecord=new HObj(random.GetRStr());
                Check(randomRecord.GetValue<string>("ContentDisplayName")==contentName&&randomRecord.GetValue<string>("ContentId")=="pal98.package.random","Compact caption preserves record identity");
                Set(random,"PalProcess",null);Set(random,"PID",-1);
                Check(random.GetGameVersion()=="仙剑98原版 新补丁 1.65-1.2秒&快走速","Completed random caption stays compact");
            }
            Check((string)Field(Snapshot(nonformal),"TimingModeLabel")=="1.2秒&快走速","Fourth combination in overlay");
            string menu=TimerCore.GetCoreDisplayName("Pal98Dx9Fast800Speed");
            Check(menu.EndsWith("0.8秒&快走速")&&(culture!="zh-TW"||menu.StartsWith("仙劍")),"Traditional/simplified menu");
        }
        string source=File.ReadAllText(Path.Combine(Repo,"Pal98Timer","GForm.cs"));
        Check(source.Contains("GetCoreDisplayName(cn).Replace(\"&\", \"&&\")"),"Menu escapes literal ampersand");
        Thread.CurrentThread.CurrentUICulture=CultureInfo.GetCultureInfo("zh-CN");
    }
    static object Timeline(string name,bool active)
    {return Activator.CreateInstance(Product.GetType("Pal98Timer.Dx9OverlayTimelineEntry"),new object[]{name,"00:06:05","00:05:59",0L,active,false});}
    static object RenderData(string font,string label)
    {
        return Activator.CreateInstance(Product.GetType("Pal98Timer.Dx9OverlaySnapshot"),new object[]{IntPtr.Zero,font,"00:11:28.34","123.45s","00:12:34","蜂0 蜜0 火0 血0 观0 剑0 钱0",3,
            Timeline("见石碑",true),Timeline("李大娘",false),Timeline("上船",false),"已暂停",false,true,label,"",""});
    }
    static Bitmap Render(Form form,object data)
    {
        Set(form,"CurrentSnapshot",data);var bitmap=new Bitmap(form.Width,form.Height);
        using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.FromArgb(35,31,27));Call(form,"OnPaint",new PaintEventArgs(graphics,form.ClientRectangle));}
        return bitmap;
    }
    static void OverlayLayout()
    {
        Type snapshot=Product.GetType("Pal98Timer.Dx9OverlaySnapshot"),overlay=Product.GetType("Pal98Timer.Dx9OverlayForm"),layoutType=Product.GetType("Pal98Timer.Dx9OverlayLayoutSettings");
        Type providerType=typeof(EmptySnapshotProvider<>).MakeGenericType(snapshot);object provider=Activator.CreateInstance(providerType);
        var callback=Delegate.CreateDelegate(typeof(Func<>).MakeGenericType(snapshot),provider,providerType.GetMethod("Get"));int number=0;
        foreach(string font in new[]{"SimSun","MingLiU"})foreach(float scale in new[]{0.8f,1f,1.5f})
        foreach(string label in new[]{"1.2秒","0.8秒","0.8秒&快走速","1.2秒&快走速"})
        {
            object layout=layoutType.GetMethod("CreateDefault",Static).Invoke(null,null);
            using(var form=(Form)Activator.CreateInstance(overlay,Instance,null,new[]{callback,layout},null))
            {
                Set(form,"CurrentScale",scale);float height=(float)Call(form,"GetOverlayHeightLogicalPixels");
                form.ClientSize=new Size((int)Math.Ceiling(340*scale),(int)Math.Ceiling(height*scale));
                using(var blank=Render(form,RenderData(font,"")))using(var painted=Render(form,RenderData(font,label)))
                {
                    int minY=painted.Height,maxX=-1,pixels=0;
                    for(int y=0;y<painted.Height;++y)for(int x=0;x<painted.Width;++x)
                        if(painted.GetPixel(x,y)!=blank.GetPixel(x,y)){minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);++pixels;}
                    Check(pixels>0&&minY>=(height-27)*scale-1&&maxX>=painted.Width-16*scale,"Bottom/right text with long idle time "+font+"/"+scale+"/"+label);
                    Check(!form.Visible&&!form.IsHandleCreated,"Offscreen rendering must not open a window");
                    painted.Save("render-"+(++number)+".png",ImageFormat.Png);
                }
            }
        }
    }
    [STAThread]
    static int Main(string[] args)
    {
        Root=Path.GetFullPath(args[0]);Repo=Path.GetFullPath(args[1]);
        using(Host=Process.GetCurrentProcess())
        using(var mapping=MemoryMappedFile.CreateNew("Local\\PAL98.TimingMode.v2."+Host.Id,592))
        using(View=mapping.CreateViewAccessor())
        {
            Creation=Host.StartTime.ToUniversalTime().ToFileTimeUtc();
            Scenario("snapshot_contract",SnapshotContract);Scenario("live_reader",LiveReader);Scenario("category_matrix",CategoryMatrix);
            Scenario("cross_imports",CrossImports);Scenario("content_imports",ContentImports);Scenario("run_identity",RunIdentity);
            Scenario("legacy_records",LegacyRecords);Scenario("storage_isolation",StorageIsolation);
            Scenario("storage_transactions",StorageTransactions);
            Scenario("presentation_completion",PresentationAndCompletion);Scenario("overlay_layout",OverlayLayout);
        }
        HObj result=new HObj();result["scenariosPassed"]=Passed;result["scenariosFailed"]=Failed;result["assertions"]=Assertions;
        result["failures"]=string.Join("\n",Failures);result["visibleWindows"]=false;result["realGameUsed"]=false;
        File.WriteAllText(Path.Combine(Root,"results.json"),result.ToJson(),new UTF8Encoding(false));
        Console.WriteLine("RESULT "+Passed+" passed, "+Failed+" failed; "+Assertions+" assertions");return Failed==0?0:1;
    }
}
