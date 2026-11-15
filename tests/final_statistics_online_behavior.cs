using Pal98Timer;
using System;
using System.Linq;
using System.Threading.Tasks;

internal static partial class CompetitionBehavior
{
    static async Task FinalStatisticsOnlineCases()
    {
        var store=Store("final-statistics"); var wire=new Fake { UploadStatus=503 }; var auth=new FakeAuth();
        var client=new CompetitionClient(store,wire,auth); client.Configure(Config());
        await Until(()=>wire.Registers>0,"statistics client registers without game");
        client.Publish(Observation("final-statistics",0)); await Task.Delay(350);
        var finish=Observation("final-statistics",2);
        finish.FinishedAt=finish.ObservedAt.AddMilliseconds(-50);
        finish.FinalStatistics=new FinalRunStatistics {final_money=4294967295L,battle_defeats=4,counter_source="pal98-events-v1",complete=true,
            player_deaths=new[]{new PlayerDeathStatistic {role_id=5,deaths=7},new PlayerDeathStatistic {role_id=15,deaths=2}}};
        client.Publish(finish);
        await Until(()=>store.LoadPending(Config()).Any(p=>p.Attempts==1),"final statistics persist before 503 retry");
        var pending=store.LoadPending(Config()).Single(); string payload=pending.Payload, seal=pending.SealedRun;
        var stats=pending.Run.observations.final_statistics;
        Check(DateTimeOffset.Parse(pending.Run.finished_at)==finish.FinishedAt && pending.Run.total_ms==finish.TotalMilliseconds,"reward settlement changes neither original finish instant nor total time");
        Check(stats.final_money==4294967295L && stats.player_deaths[0].role_id==5 && stats.player_deaths[0].deaths==7 && stats.battle_defeats==4,"actual ProcessObservation preserves whole final statistics");
        Check(CompetitionProtocol.Json().Deserialize<CompetitionRun>(payload).observations.final_statistics.player_deaths[1].role_id==15,"outbox JSON retains physical role ID15");
        finish.FinalStatistics.final_money=1; finish.FinalStatistics.player_deaths[0].deaths=999;
        Check(store.LoadPending(Config()).Single().Payload==payload,"mutable late observation cannot rewrite completed facts");
        await client.CloseAsync();
        wire=new Fake { Features=new[]{CompetitionProtocol.RestartTimeFeature} }; var retryAuth=new FakeAuth();
        client=new CompetitionClient(store,wire,retryAuth); client.Configure(Config()); client.Retry();
        await Until(()=>store.LoadPending(Config()).Any(p=>p.LastStatus?.Contains("尚未支持通关统计")==true),"old server retains statistics score for upgrade");
        Check(wire.Posts==0 && retryAuth.Seals==0 && store.LoadPending(Config()).Single().SealedRun==seal,"unsupported server receives no stripped or resealed result");
        wire.Features=new[]{CompetitionProtocol.RestartTimeFeature,CompetitionProtocol.RunStatisticsFeature}; client.Retry();
        await Until(()=>wire.Posts==1,"upgraded server receives retained overall score");
        Check(wire.Payloads.Single()==payload && retryAuth.Seals==0,"retry after restart/upgrade preserves exact overall bytes and signature");
        await client.CloseAsync();
    }
}
