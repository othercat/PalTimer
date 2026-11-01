using Pal98Timer;
using System;
using System.Linq;
using System.Threading.Tasks;

internal static partial class CompetitionBehavior
{
    static async Task OriginalChallengeOnlineCases()
    {
        var store=Store("original-challenge-capability");var wire=new Fake();var auth=new FakeAuth();
        var client=new CompetitionClient(store,wire,auth);client.Configure(Config());
        await Until(()=>wire.Registers>0,"challenge compatibility server registered");
        var identity=new OriginalChallengeIdentity {Id=Guid.NewGuid().ToString(),Revision=1,
            Content="paldll-dx9-pal98fix-classic-v5",RulesHash=new string('c',64),Title="挑战"};
        var start=Observation("challenge",0);start.Ranking=identity.Decorate(start.Ranking);client.Publish(start);
        var finish=Observation("challenge",2);finish.Ranking=identity.Decorate(finish.Ranking);client.Publish(finish);
        await Until(()=>store.LoadPending(Config()).Any(p=>p.LastStatus?.Contains("原版挑战") == true),"unsupported server retains challenge score for upgrade");
        var pending=store.LoadPending(Config()).Single();string payload=pending.Payload;
        Check(wire.Posts==0 && payload.Contains("challenge.id"),"old server receives no downgraded challenge score");
        int seals=auth.Seals;wire.Features=new[]{CompetitionProtocol.RestartTimeFeature,"original-challenge-ranking.v1"};client.Retry();
        await Until(()=>wire.Posts==1,"upgraded server receives the retained challenge score");
        Check(wire.Payloads.Single()==payload && auth.Seals==seals,"upgrade preserves exact signed facts without resealing");
        await client.CloseAsync();
    }
}
