using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using Pal98Timer;

internal static class RunStatisticsBehavior
{
    static int checks;
    static void Check(bool ok, string name) { ++checks; if (!ok) throw new Exception(name); }
    static RunStatisticsSample Sample(string key="process1", long death=0, long loss=0, long? money=66666, uint gap=0) => new RunStatisticsSample {
        key=key, source="sdlpal-events-v1", deaths=new long[] { death,0,0,0,0,death }, defeats=loss, controlled=0x21, gaps=gap, money=money
    };
    static long Death(FinalRunStatistics s, int role) => s.player_deaths.Single(p=>p.role_id==role).deaths;
    static void Counters()
    {
        var a=new RunStatisticsAccumulator();
        a.Observe(Sample(death:8,loss:3),false,false); a.Observe(Sample(death:8,loss:3),true,false);
        Check(Death(a.Snapshot(),5)==0 && a.Snapshot().battle_defeats==0,"pre-run totals are excluded");
        a.Observe(Sample(death:10,loss:4),true,false); a.Observe(Sample(death:10,loss:4),true,false);
        Check(Death(a.Snapshot(),0)==2 && Death(a.Snapshot(),5)==2 && a.Snapshot().battle_defeats==1,"per-role cumulative differences; repeat/load deduplicated");
        string old=a.Save(); a.Observe(Sample(death:11,loss:5),true,false); a.Restore(old);
        a.Observe(Sample(death:11,loss:5),true,false);
        Check(Death(a.Snapshot(),5)==3 && a.Snapshot().battle_defeats==2,"importing an older same-process timer cannot erase/recount events");
        Check(!a.Snapshot().complete,"imported observations remain incomplete");
        var b=new RunStatisticsAccumulator(); b.Restore(a.Save()); b.Observe(Sample(death:12,loss:5),true,false);
        Check(Death(b.Snapshot(),5)==4 && b.Snapshot().battle_defeats==2,"saved event cursor survives timer restart");
        b.Observe(Sample("process2",death:2,loss:1),true,false);
        Check(Death(b.Snapshot(),5)==6 && b.Snapshot().battle_defeats==3 && !b.Snapshot().complete,"process restart retains previous totals and marks continuity unknown");
        b.Observe(Sample("process2",death:1,loss:0),true,false);
        Check(Death(b.Snapshot(),5)==6 && b.Snapshot().battle_defeats==3,"decreasing counter cannot subtract or wrap");
        var c=new RunStatisticsAccumulator(); c.Observe(Sample(),false,false);
        c.Observe(Sample(death:1,loss:1,money:-999),true,true);
        Check(c.Snapshot().complete && c.Snapshot().final_money==-999 && Death(c.Snapshot(),5)==1,"finish captures signed cash balance and exact zero/nonzero facts");
        c.Observe(Sample(death:9,loss:7,money:555),true,true);
        Check(c.Snapshot().final_money==-999 && Death(c.Snapshot(),5)==1,"completion facts freeze before outbox retries");
        var d=new RunStatisticsAccumulator(); d.Observe(Sample(),false,false); d.Observe(null,true,false); d.Observe(Sample(death:2),true,false);
        Check(Death(d.Snapshot(),5)==2 && d.Snapshot().complete,"temporary read interruption is recovered from cumulative events");
        d.Observe(null,true,true,true);
        Check(d.Snapshot().final_money==null && !d.Snapshot().complete,"missing final read cannot reuse stale money");
        var e=new RunStatisticsAccumulator(); e.Observe(Sample(death:5,loss:2),true,false);
        Check(!e.Snapshot().complete && Death(e.Snapshot(),5)==0,"late attach does not attribute earlier deaths to this run");
        var f=new RunStatisticsAccumulator(); f.Observe(null,true,true);
        Check(f.Snapshot().counter_source=="unavailable" && f.Snapshot().battle_defeats==null && f.Snapshot().player_deaths.Length==0,"unsupported producer is unknown, not zero");
        var g=new RunStatisticsAccumulator(); g.Observe(Sample(),false,false); g.Observe(Sample(gap:1),true,true);
        Check(!g.Snapshot().complete,"producer observation gap is disclosed");
        var h=new RunStatisticsAccumulator(); h.Observe(Sample(),false,false);
        var beforeRewards=Sample(money:100); beforeRewards.settled=false; h.Observe(beforeRewards,true,true);
        Check(!h.FinalReady && !h.Snapshot().complete,"boss death cannot freeze pre-reward money");
        h.Observe(Sample(money:500),true,true);
        Check(h.FinalReady && h.Snapshot().complete && h.Snapshot().final_money==500,"final money freezes after reward settlement");
        var i=new RunStatisticsAccumulator(); i.Observe(Sample(),false,false); i.Observe(beforeRewards,true,true); i.Observe(null,true,true,true);
        Check(i.FinalReady && i.Snapshot().final_money==null && !i.Snapshot().complete,"exit before settlement records unknown instead of stale money");
        bool rejected=false; try { FinalRunStatistics.Parse("{\"schema\":\"PAL.RunStatistics.v1\",\"final_money\":1,\"counter_source\":\"unavailable\",\"battle_defeats\":0,\"player_deaths\":[],\"complete\":false}"); } catch(ArgumentException) {rejected=true;}
        Check(rejected,"unavailable cannot claim zero defeats");
    }
    static readonly BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static object Deserialize(JavaScriptSerializer json,string text,Type type) => typeof(JavaScriptSerializer).GetMethods()
        .Single(m=>m.Name=="Deserialize" && m.IsGenericMethodDefinition).MakeGenericMethod(type).Invoke(json,new object[]{text});
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint allocation, uint protect);
    [DllImport("kernel32.dll")] static extern bool VirtualFree(IntPtr address, UIntPtr size, uint operation);
    static void NativeTransport()
    {
        using(var process=Process.GetCurrentProcess())
        using(var mapping=MemoryMappedFile.CreateNew("Local\\PAL98.RunStatistics.v1."+process.Id,256))
        using(var view=mapping.CreateViewAccessor())
        {
            IntPtr memory=VirtualAlloc(new IntPtr(0x25000000),new UIntPtr(4096),0x3000,4);
            if(memory==IntPtr.Zero) throw new Exception("Could not allocate bounded 32-bit fixture memory.");
            try {
                var data=new byte[256];
                Encoding.ASCII.GetBytes("PALSTAT1").CopyTo(data,0);
                BitConverter.GetBytes(1).CopyTo(data,8); BitConverter.GetBytes(256).CopyTo(data,12);
                BitConverter.GetBytes(2).CopyTo(data,16); BitConverter.GetBytes(process.Id).CopyTo(data,20);
                BitConverter.GetBytes((ulong)process.StartTime.ToUniversalTime().ToFileTimeUtc()).CopyTo(data,24);
                BitConverter.GetBytes(123UL).CopyTo(data,32); BitConverter.GetBytes(7).CopyTo(data,48);
                BitConverter.GetBytes(16).CopyTo(data,52); BitConverter.GetBytes(0x8021u).CopyTo(data,56);
                BitConverter.GetBytes(7L).CopyTo(data,64+15*8); BitConverter.GetBytes(4L).CopyTo(data,192);
                BitConverter.GetBytes((uint)memory.ToInt64()).CopyTo(data,212);
                Marshal.WriteInt32(memory,0x2B4,-1);
                view.WriteArray(0,data,0,data.Length);
                var sample=NativeRunStatisticsReader.Read(process);
                Check(sample!=null && sample.deaths[15]==7 && sample.defeats==4 && sample.money==4294967295L,"real native MMF reader validates creation and reads fresh unsigned money");
                view.Write(16,3);
                Check(NativeRunStatisticsReader.Read(process)==null,"in-flight native seqlock snapshot rejected");
                view.Write(16,2); view.Write(20,process.Id+1);
                Check(NativeRunStatisticsReader.Read(process)==null,"native PID mismatch rejected");
                view.Write(20,process.Id); view.Write(24,0UL);
                Check(NativeRunStatisticsReader.Read(process)==null,"reused native PID cannot bind old creation");
                view.WriteArray(0,data,0,data.Length); view.Write(212,0u);
                sample=NativeRunStatisticsReader.Read(process);
                Check(sample!=null && sample.money==null,"failed fresh money read stays unknown despite cached producer value");
            } finally { VirtualFree(memory,UIntPtr.Zero,0x8000); }
        }
    }
    static object Field(object value,string name) => value.GetType().GetField(name,Flags).GetValue(value);
    static void Set(object value,string name,object field) => value.GetType().GetField(name,Flags).SetValue(value,field);
    static void Pipeline(string runtime,string fixture,string output)
    {
        var json=new JavaScriptSerializer {MaxJsonLength=1048576};
        var host=Assembly.LoadFrom(Path.Combine(runtime,"Pal98Timer.exe"));
        var online=Assembly.LoadFrom(Path.Combine(runtime,"PalTimerOnline.dll"));
        var observe=Activator.CreateInstance(host.GetType("Pal98Timer.CompetitionObservation"),true);
        Set(observe,"Token","run-statistics-fixture"); Set(observe,"Splits",Array.CreateInstance(host.GetType("Pal98Timer.CompetitionSplit"),0));
        var model=host.GetType("Pal98Timer.FinalRunStatistics");
        var finishAt=DateTimeOffset.UtcNow.AddSeconds(-5); Set(observe,"Finished",true); Set(observe,"FinishedAt",finishAt); Set(observe,"ObservedAt",DateTimeOffset.UtcNow);
        string stats="{\"schema\":\"PAL.RunStatistics.v1\",\"final_money\":-123,\"player_deaths\":[{\"role_id\":5,\"deaths\":7}],\"battle_defeats\":2,\"counter_source\":\"sdlpal-events-v1\",\"complete\":true}";
        Set(observe,"FinalStatistics",Deserialize(json,stats,model));
        var publish=host.GetTypes().SelectMany(t=>t.GetMethods(Flags)).Single(m=>m.Name=="CreateCompetitionSnapshot");
        var snapshot=publish.Invoke(null,new[]{observe});
        var consume=online.GetTypes().SelectMany(t=>t.GetMethods(Flags)).Single(m=>m.Name=="ReadSnapshot" && m.GetParameters().Length==1 && m.GetParameters()[0].ParameterType.Name=="OnlineSnapshotV1");
        var received=consume.Invoke(null,new[]{snapshot});
        var receivedStats=Field(received,"FinalStatistics");
        Check((DateTimeOffset?)Field(received,"FinishedAt")==finishAt,"API1 preserves original finish instant while money settles");
        Check(json.Serialize(receivedStats)==json.Serialize(Deserialize(json,stats,receivedStats.GetType())),"real API1 extension preserves all final statistics into Online");
        var runType=online.GetType("Pal98Timer.CompetitionRun");
        var run=Deserialize(json,File.ReadAllText(fixture),runType);
        object observations=runType.GetProperty("observations").GetValue(run);
        observations.GetType().GetProperty("final_statistics").SetValue(observations,receivedStats);
        var protocol=online.GetType("Pal98Timer.CompetitionProtocol");
        var serialize=protocol.GetMethod("SerializeRun",Flags);
        string payload=(string)serialize.Invoke(null,new[]{run,(object)true});
        Check(payload.Contains("\"final_statistics\"") && payload.Contains("\"final_money\":-123") && payload.Contains("\"role_id\":5"),"statistics enter actual canonical overall run");
        string validate=(string)protocol.GetMethod("Validate",Flags).Invoke(null,new object[]{run});
        Check(validate=="","new statistics accepted by actual client run validator");
        File.WriteAllText(Path.Combine(output,"actual-final-run.json"),payload);
        Check(payload==(string)serialize.Invoke(null,new object[]{Deserialize(json,payload,runType),true}),"pending/outbox JSON reload retains immutable payload bytes");
        observations.GetType().GetProperty("final_statistics").SetValue(observations,null);
        string old=(string)serialize.Invoke(null,new[]{run,(object)true});
        Check(!old.Contains("final_statistics"),"old run serialization omits optional field instead of adding null");
        var coreType=host.GetType("Pal98Timer.TimerCore");
        var actual=FormatterServices.GetUninitializedObject(host.GetType("Pal98Timer.SdlpalGeshi"));
        var accumulatorType=host.GetType("Pal98Timer.RunStatisticsAccumulator");
        var accumulator=Activator.CreateInstance(accumulatorType,true);
        var sampleType=host.GetType("Pal98Timer.RunStatisticsSample");
        var sample=Deserialize(json,"{\"key\":\"close-fixture\",\"source\":\"sdlpal-events-v1\",\"deaths\":[0,0,0,0,0,0],\"defeats\":0,\"controlled\":1,\"money\":100,\"settled\":false}",sampleType);
        accumulatorType.GetMethod("Observe",Flags).Invoke(accumulator,new object[]{sample,true,true,false});
        coreType.GetField("runStatistics",Flags).SetValue(actual,accumulator);
        coreType.GetField("runStatisticsObservationGate",Flags).SetValue(actual,new object());
        coreType.GetField("_CurrentStep",Flags).SetValue(actual,1);
        var points=Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(host.GetType("Pal98Timer.CheckPoint")));
        points.GetType().GetMethod("Add").Invoke(points,new object[]{null});
        coreType.GetField("CheckPoints",Flags).SetValue(actual,points);
        coreType.GetMethod("PreserveCompletedStatisticsBeforeClose",Flags).Invoke(actual,null);
        var closedStats=accumulatorType.GetMethod("Snapshot",Flags).Invoke(accumulator,null);
        Check((bool)accumulatorType.GetProperty("FinalReady",Flags).GetValue(accumulator) &&
            closedStats.GetType().GetProperty("final_money").GetValue(closedStats)==null,"actual completed core close flush retains unknown-money completion before outbox closes");
        coreType.GetField("_CurrentStep",Flags).SetValue(actual,0);
        accumulatorType.GetMethod("Reset",Flags).Invoke(accumulator,null);
        coreType.GetMethod("PreserveCompletedStatisticsBeforeClose",Flags).Invoke(actual,null);
        Check(!(bool)accumulatorType.GetProperty("FinalReady",Flags).GetValue(accumulator),"closing an unfinished core cannot create a completed score");
    }
    static int Main(string[] args)
    {
        try { Counters(); NativeTransport(); Pipeline(args[0],args[1],args[2]); Console.WriteLine("checks="+checks+" failures=0 gameProcess=0 gameInput=0"); return 0; }
        catch(Exception error) { Console.WriteLine(error); return 1; }
    }
}
