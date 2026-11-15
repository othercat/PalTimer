using System;
using Pal98Timer;
internal static class RunStatisticsEdgeCases
{
    static int checks,failures;
    static RunStatisticsSample Sample(long n,long money=100,bool settled=true,string key="same") => new RunStatisticsSample {
        key=key,source="sdlpal-events-v1",deaths=new long[]{n,0,0,0,0,n},defeats=n,controlled=0x21,money=money,settled=settled
    };
    static long Death(RunStatisticsAccumulator a) => a.Snapshot().player_deaths[0].deaths;
    static void Check(bool ok,string name) { ++checks; if(!ok)++failures; Console.WriteLine((ok?"PASS ":"FAIL ")+name); }
    static int Main()
    {
        var a=new RunStatisticsAccumulator(); a.Observe(Sample(0),false,false); a.Observe(Sample(0,100,false),true,true);
        a.Observe(null,true,true); a.Observe(Sample(0,500),true,true);
        Check(a.FinalReady && a.Snapshot().final_money==500 && a.Snapshot().complete,"transient read loss during settlement recovers final reward money");
        var saved=new RunStatisticsAccumulator(); saved.Observe(Sample(2),false,false); saved.Observe(Sample(5),true,false);
        var b=new RunStatisticsAccumulator(); b.Observe(Sample(7),false,false); b.Restore(saved.Save()); b.Observe(Sample(7),true,false);
        Check(Death(b)==5 && b.Snapshot().battle_defeats==5,"restoring after a fresh baseline includes saved-cursor events 5 through 7");
        var c=new RunStatisticsAccumulator(); c.Observe(Sample(0),false,false); c.Observe(Sample(1),true,false);
        c.Observe(Sample(0),true,false); c.Observe(Sample(1),true,false);
        Check(Death(c)==1 && c.Snapshot().battle_defeats==1,"late same-source snapshot cannot rewind a processed cursor");
        b.Restore(saved.Save()); b.Observe(Sample(7),true,false);
        Check(Death(b)==5 && b.Snapshot().battle_defeats==5,"repeated same-source restore is idempotent");
        var d=new RunStatisticsAccumulator(); d.Observe(Sample(2,key:"new-process"),false,false); d.Restore(saved.Save()); d.Observe(Sample(2,key:"new-process"),true,false);
        Check(Death(d)==5 && d.Snapshot().battle_defeats==5,"fresh process baseline before import retains its continuation events");
        d.Restore(saved.Save()); d.Observe(Sample(2,key:"new-process"),true,false);
        Check(Death(d)==5,"new-process continuation is not added again by repeated import");
        var e=new RunStatisticsAccumulator(); e.Observe(Sample(1),false,false); e.Restore(saved.Save()); e.Observe(Sample(1),true,false); e.Observe(Sample(5),true,false);
        Check(Death(e)==3,"restore ahead of a delayed read preserves its high-water cursor");
        long tick=1; var f=new RunStatisticsAccumulator(()=>tick); f.Observe(Sample(0),false,false); f.Observe(Sample(0,100,false),true,true);
        f.Observe(null,true,true); Check(!f.FinalReady,"one missing frame keeps settlement pending");
        tick+=6*System.Diagnostics.Stopwatch.Frequency; f.Observe(null,true,true);
        Check(f.FinalReady && f.Snapshot().final_money==null && !f.Snapshot().complete,"persistent read loss eventually retains a completion with unknown money");
        Console.WriteLine("checks="+checks+" failures="+failures+" gameProcess=0 gameInput=0");return failures==0?0:1;
    }
}
