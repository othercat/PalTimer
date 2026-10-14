using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using HFrame.ENT;
using Pal98Timer;

// Exactly the existing v165 offline-restore boundary: use real validation,
// then suppress only SetTimerFromString's optional live-PAL attachment probe.
internal interface IRestoreProbe { void Restore(string json); }
internal sealed class OfflineDx9 : 仙剑98柔情DX9, IRestoreProbe {
    private bool suppressAttach;
    internal OfflineDx9() : base(null) {}
    protected override int TimingModeMs => suppressAttach ? 0 : base.TimingModeMs;
    public void Restore(string json) {
        ValidateTimerImport(json);
        suppressAttach = true;
        try { SetTimerFromString(json); } finally { suppressAttach = false; }
    }
}
internal sealed class OfflineAuto : Pal98Dx9Automatic, IRestoreProbe {
    private bool suppressAttach;
    internal OfflineAuto() : base(null) {}
    protected override int TimingModeMs => suppressAttach ? 0 : base.TimingModeMs;
    public void Restore(string json) {
        ValidateTimerImport(json);
        suppressAttach = true;
        try { SetTimerFromString(json); } finally { suppressAttach = false; }
    }
}
internal static class SrpgRelayTimeHost {
    const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly Assembly Product = typeof(TimerCore).Assembly;
    static Process self;
    static int checks;
    static void Check(bool condition,string message) { ++checks; if(!condition)throw new Exception(message); Console.WriteLine("PASS "+message); }
    static object Get(object target,string name) {
        for(var t=target.GetType();t!=null;t=t.BaseType) {
            var field=t.GetField(name,I|BindingFlags.DeclaredOnly);
            if(field!=null)return field.GetValue(target);
        }
        throw new MissingFieldException(name);
    }
    static void Set(object target,string name,object value) {
        for(var t=target.GetType();t!=null;t=t.BaseType) {
            var field=t.GetField(name,I|BindingFlags.DeclaredOnly);
            if(field!=null){field.SetValue(target,value);return;}
        }
        throw new MissingFieldException(name);
    }
    static object Call(object target,string name,params object[] args) {
        for(var t=target.GetType();t!=null;t=t.BaseType) {
            var method=t.GetMethod(name,I|BindingFlags.DeclaredOnly);
            if(method!=null)return method.Invoke(target,args);
        }
        throw new MissingMethodException(name);
    }
    static PTimer Watch(TimerCore c)=>(PTimer)Get(c,"MT");
    static PTimer Idle(TimerCore c)=>(PTimer)Get(c,"ST");
    static void BindIsolatedSettings(string root) {
        var type=Product.GetType("Pal98Timer.TimerUserSettings",true);
        var settings=Activator.CreateInstance(Product.GetType("Pal98Timer.TimerSettingsStore",true),I|S,null,
            new object[]{root,Path.Combine(root,"isolated-user"),null},null);
        type.GetField("store",S).SetValue(null,settings);
        type.GetMethod("ClearPathCache",S).Invoke(null,null);
    }
    static byte[] ModeFrame() {
        var b=new byte[592];Action<int,byte[]> put=(offset,bytes)=>Buffer.BlockCopy(bytes,0,b,offset,bytes.Length);
        put(0,BitConverter.GetBytes(0x324D5450u));put(4,BitConverter.GetBytes((ushort)2));put(6,BitConverter.GetBytes((ushort)592));
        put(8,BitConverter.GetBytes(self.Id));put(12,BitConverter.GetBytes(1200));
        put(16,BitConverter.GetBytes(self.StartTime.ToUniversalTime().ToFileTimeUtc()));put(24,BitConverter.GetBytes(0x01060500u));
        put(28,BitConverter.GetBytes(10));put(32,BitConverter.GetBytes(1));
        put(40,Encoding.UTF8.GetBytes("pal98fix-classic-v5"));put(200,Encoding.UTF8.GetBytes("1.0.0"));
        put(264,Encoding.UTF8.GetBytes(new string('a',64)));put(329,Encoding.UTF8.GetBytes("offline same-content fixture"));
        return b;
    }
    static void SetAutoIdentity(TimerCore c) {
        var type=Product.GetType("Pal98Timer.GameplaySnapshot",true);var snap=Activator.CreateInstance(type);
        Action<string,object> prop=(key,value)=>type.GetProperty(key).SetValue(snap,value);
        prop("schema","PAL98.GameplayMode.v1");prop("covered",true);prop("rules_sha256",new string('b',64));
        prop("content_id","pal98fix-classic-v5");prop("content_sha256",new string('a',64));prop("family","standard");
        prop("fade_ms",1200);prop("map_speed_ticks",10);prop("rules",new Dictionary<string,string>());
        Set(snap,"OrdinaryTimelineId",new string('c',64));Set(snap,"HardcoreTimelineId",new string('d',64));
        Set(c,"selected",snap);Set(c,"identity",new string('c',64));Set(c,"frozen",new string('c',64));
        Set(c,"requested",false);Set(c,"frozenHardcore",false);Set(c,"validation","");
    }
    static TimerCore Core(bool automatic,long current,long restart,int step) {
        TimerCore c=automatic?(TimerCore)new OfflineAuto():new OfflineDx9();Call(c,"InitCheckPoints");
        Set(c,"PalProcess",self);Set(c,"PID",self.Id);Set(c,"DX9Version","1.72");
        if(automatic)SetAutoIdentity(c);
        var form=(GForm)FormatterServices.GetUninitializedObject(typeof(GForm));
        form.btnPause=new GRender.GBtn(0,null);Set(form,"core",c);Set(c,"form",form);
        Watch(c).SetTS(TimeSpan.FromMilliseconds(current));Idle(c).SetTS(TimeSpan.FromMilliseconds(restart));
        Set(c,"_IsFirstStarted",true);Set(c,"_HasGameStart",true);
        for(int i=0;i<c.CheckPoints.Count;++i) {
            c.CheckPoints[i].Current=i<step?TimeSpan.FromMilliseconds((i+1)*60000+current%1000):TimeSpan.Zero;
            c.CheckPoints[i].Status=i<step?CheckPointStatus.Completed:CheckPointStatus.NotStarted;
            c.CheckPoints[i].Check=()=>false;
        }
        c.Jump(step);return c;
    }
    static SRPGobj Pack(TimerCore c) {
        var p=new SRPGobj{RPG=new byte[]{1,2,3},TimerStr=c.GetRStr()};
        using(var stream=new MemoryStream()) {
            var formatter=new BinaryFormatter();formatter.Serialize(stream,p);stream.Position=0;
            return (SRPGobj)formatter.Deserialize(stream);
        }
    }
    static void AssertRestored(TimerCore c,SRPGobj donor,string label) {
        var record=new HObj(donor.TimerStr);
        Check(c.GetMainWatch()==TimerCore.ConvertTimeSpan(record.GetValue<string>("Current")),label+" donor cumulative time restored");
        Check(Idle(c).CurrentTSOnly.Ticks/TimeSpan.TicksPerMillisecond==record.GetValue<long>("RestartMilliseconds"),label+" donor restart/idle restored");
        Check(c.CurrentStep==record.GetValue<int>("Step"),label+" donor current checkpoint restored");
        var nodes=record.GetValue<HObj>("CheckPoints");
        Check(nodes.Count==c.CheckPoints.Count,label+" same checkpoint count");
        for(int i=0;i<nodes.Count;++i)
            Check(c.CheckPoints[i].Current==TimerCore.ConvertTimeSpan(nodes.GetValue<HObj>(i).GetValue<string>("time")),label+" checkpoint "+i+" donor elapsed value restored");
    }
    static void Restore(TimerCore c,SRPGobj donor,string label) {
        long sequence=c.ScoreRunSequence;
        ((IRestoreProbe)c).Restore(donor.TimerStr);
        ((GForm)Get(c,"form")).SetUIPause(true); // The actual cloud/relay caller's next UI action.
        AssertRestored(c,donor,label);
        Check(c.ScoreRunSequence>sequence,label+" import changes local run identity");
        Check(c.IsUIPause&&((GForm)Get(c,"form")).btnPause.IsRed(),label+" real UI pause state applied");
        Call(c,"JudgePause"); // UI pause returns before foreground OS queries.
        Check(!(bool)Call(c,"HasStartGame"),label+" paused import cannot start main timing");
        Watch(c).Stop();var frozen=c.GetMainWatch();Thread.Sleep(20);
        Check(c.GetMainWatch()==frozen&&!Watch(c).IsRunning,label+" imported cumulative time stays stopped");
    }
    static void ResumeAndCheckpoint(TimerCore c,string label) {
        var mainBefore=c.GetMainWatch();var idleBefore=Idle(c).CurrentTSOnly;
        var splits=c.CheckPoints.Select(p=>p.Current).ToArray();int step=c.CurrentStep;
        Call(c,"ClearGameState");
        Call(c,"UpdatePalRestartIdle",Idle(c),true,false);Thread.Sleep(25);Idle(c).Stop();
        Check(c.GetMainWatch()==mainBefore,label+" process cleanup/restart leaves imported main time intact");
        Check(Idle(c).CurrentTSOnly>idleBefore,label+" restart waiting accumulates only restart/idle time");
        Check(c.CheckPoints.Select(p=>p.Current).SequenceEqual(splits)&&c.CurrentStep==step,label+" restart preserves imported splits and position");
        Check(c.IsUIPause,label+" process cleanup does not release UI pause");
        Set(c,"_HasGameStart",true); // Synthetic readiness; no PAL memory is read.
        Call(c,"UpdatePalRestartIdle",Idle(c),true,true);
        var idleReady=Idle(c).CurrentTSOnly;
        Check(!Idle(c).IsRunning,label+" game-ready gate stops restart/idle");
        Call(c,"JudgePause");
        Check(!(bool)Call(c,"HasStartGame"),label+" ready game still waits for manual resume");
        // GForm's normal F9 release dispatch is UIPause followed by OnFunctionKey(9).
        var form=(GForm)Get(c,"form");form.UIPause();c.OnFunctionKey(9);
        Check(!c.IsUIPause&&form.btnPause.IsWhite(),label+" F9 callback releases pause");
        Set(c,"IsPause",false); // Model game foreground; no foreground window is manipulated.
        Check((bool)Call(c,"HasStartGame"),label+" foreground ready game permits timing");
        Call(c,"StartAndCheckMainTimer");Thread.Sleep(30);Watch(c).Stop();
        Check(c.GetMainWatch()>mainBefore,label+" donor cumulative time advances after resume");
        Check(Idle(c).CurrentTSOnly==idleReady,label+" resumed main time does not add restart/idle");
        Check(c.CheckPoints.Take(step).Select(p=>p.Current).SequenceEqual(splits.Take(step)),label+" completed donor split values survive resume");
        c.CheckPoints[step].Check=()=>true;Call(c,"StartAndCheckMainTimer");Watch(c).Stop();
        Check(c.CurrentStep==step+1&&c.CheckPoints[step].Current>=mainBefore,label+" next checkpoint records cumulative time after donor history");
    }
    static void Mutual(bool automatic) {
        string label=automatic?"AUTO":"DX9";
        var a=Core(automatic,600123,3210,2);var b=Core(automatic,1200456,9876,5);
        try {
            var pa=Pack(a);var pb=Pack(b);
            Restore(a,pb,label+" B-to-A");Restore(b,pa,label+" A-to-B");
            Check(a.GetMainWatch()!=b.GetMainWatch(),label+" distinct sender totals remain distinct after mutual import");
            var returnToB=Pack(a);Restore(b,returnToB,label+" B-to-A-to-B");
            ResumeAndCheckpoint(a,label+" A");ResumeAndCheckpoint(b,label+" B");
        } finally {a.Unload();b.Unload();}
    }
    static void ConfigurationIndependentRestore() {
        var source=Core(true,987654,5432,4);var target=Core(true,123,10,1);var dx9=Core(false,456,20,3);
        try {
            var donor=Pack(source);var record=new HObj(donor.TimerStr);
            record["GameplayVerified"]=false;record["TimingRulesVerified"]=false;
            record["TimelineIdentity"]="another-configuration";record["ReferenceTimeline"]=true;
            record["LocalReferenceConfiguration"]=new HObj("{\"id\":\"another-preset\"}");
            record["ContentHash"]=new string('e',64);record["TimingModeMs"]=800;record["PaletteFadeModeMs"]=800;
            record["MapSpeedTicks"]=9;record["TimingMapSpeedTicks"]=9;record["TimerCore"]="PAL98DX9_800_SPEED";
            donor.TimerStr=record.ToJson();
            Set(target,"selected",null);Set(target,"validation","unclassified gameplay");
            Set(target,"timingRunInvalidated",true);Set(target,"timingEvidenceMissingDuringRun",true);
            Restore(target,donor,"unknown/reference/different configuration AUTO");
            Restore(dx9,donor,"different fade/speed/core DX9");
            foreach(var core in new[]{target,dx9}) {
                var exported=new HObj(core.GetRStr());
                Check(!exported.GetValue<bool>("TimingRulesVerified"),"restored timing is never re-certified");
                Check(!(bool)Call(core,"CanBeginCompetitionHere",0),"restored run cannot begin ranked upload");
                Check((bool)Get(core,"importedUnverifiedTiming"),"restore remains local after configuration mismatch");
                ResumeAndCheckpoint(core,"configuration-independent resume");
            }
            Check(!new HObj(target.GetRStr()).GetValue<bool>("GameplayVerified"),"automatic restore cannot forge gameplay verification");
            foreach(var mutate in new Action<HObj>[] {
                data=>data["Step"]=99, data=>data["Current"]="invalid", data=>data["CheckPoints"]=new HObj("[]")
            }) {
                var bad=new HObj(donor.TimerStr);mutate(bad);var before=target.GetMainWatch();var step=target.CurrentStep;
                bool refused=false;try{((IRestoreProbe)target).Restore(bad.ToJson());}catch(InvalidDataException){refused=true;}
                Check(refused && target.GetMainWatch()==before && target.CurrentStep==step,"malformed timeline refuses before mutating timing");
            }
        } finally {source.Unload();target.Unload();dx9.Unload();}
    }
    static void LegacyCoreRestore() {
        var source=Core(true,765432,6543,3);var donor=Pack(source);
        try {
            foreach(TimerCore target in new TimerCore[]{new 仙剑98柔情(null),new 仙剑98柔情不欢乐模式(null)}) {
                try {
                    var form=(GForm)FormatterServices.GetUninitializedObject(typeof(GForm));
                    form.btnPause=new GRender.GBtn(0,null);Set(form,"core",target);Set(target,"form",form);
                    Call(target,"InitCheckPoints");
                    var incoming=donor;
                    if(target.CheckPoints.Count!=source.CheckPoints.Count) {
                        // The unhappy core has a different route schema. Relay
                        // its own route while varying all configuration metadata.
                        Watch(target).SetTS(source.GetMainWatch());Idle(target).SetTS(Idle(source).CurrentTSOnly);
                        for(int i=0;i<3;++i)target.CheckPoints[i].SetCurrentTSForLoad(TimeSpan.FromSeconds((i+1)*60));
                        target.Jump(3);incoming=Pack(target);
                        var data=new HObj(incoming.TimerStr);data["ContentHash"]=new string('f',64);data["TimelineIdentity"]="different-config";
                        data["TimingModeMs"]=800;data["TimingRulesVerified"]=false;incoming.TimerStr=data.ToJson();
                        Watch(target).SetTS(TimeSpan.Zero);Idle(target).SetTS(TimeSpan.Zero);target.Jump(-1);
                    }
                    Call(target,"SetTimerFromString",incoming.TimerStr);
                    AssertRestored(target,incoming,target.GetType().Name+" cross-configuration");
                    Check((bool)Get(target,"competitionImportedRun"),"legacy core import cannot create verified run");
                    Check(!(bool)Call(target,"CanBeginCompetitionHere",0),"legacy core import cannot begin ranked upload");
                    var before=target.GetMainWatch();var bad=new HObj(incoming.TimerStr);bad["Step"]=99;
                    bool refused=false;try{Call(target,"SetTimerFromString",bad.ToJson());}catch(TargetInvocationException ex){refused=ex.InnerException is InvalidDataException;}
                    Check(refused&&target.GetMainWatch()==before,"legacy invalid structure preserves previous timing");
                } finally {target.Unload();}
            }
        } finally {source.Unload();}
    }
    [STAThread]static int Main(string[] args) {
        try {
            string root=Path.GetFullPath(args[0]);Directory.SetCurrentDirectory(root);BindIsolatedSettings(root);
            using(self=Process.GetCurrentProcess())
            using(var map=MemoryMappedFile.CreateNew("Local\\PAL98.TimingMode.v2."+self.Id,592))
            using(var view=map.CreateViewAccessor()) {var b=ModeFrame();view.WriteArray(0,b,0,b.Length);Mutual(false);Mutual(true);ConfigurationIndependentRestore();LegacyCoreRestore();}
            Console.WriteLine("PASS: "+checks+" real timer restore / checkpoint / pause / restart-idle / F9-callback checks; network=false; game=false; visibleUI=false");
            return 0;
        } catch(Exception error) {Console.Error.WriteLine(error);return 1;}
    }
}
