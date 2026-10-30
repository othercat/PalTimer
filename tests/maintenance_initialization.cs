using Pal98Timer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

internal static partial class CompetitionBehavior
{
    sealed class InitializationWire : ICompetitionTransport
    {
        internal bool Approved = true, Supports = true, LoseReceipt;
        internal int Calls, Posts;
        internal CompetitionRun Last;
        public Task<CompetitionHttpResult> Send(string method, string url, string hwid, string secret, string body, int timeout, CancellationToken stop)
        {
            ++Calls;
            Check(new Uri(url).Host == "fixture.invalid", "initializer test never reaches production");
            object reply;
            if (url.Contains("/auth/status?")) reply = new { protocol=CompetitionAuthProtocol.Name, scope=CompetitionProtocol.Scope,
                server_origin="https://fixture.invalid", key_id=new string('a',32), timer_exe_sha256=new string('b',64), approved=Approved,
                supported_features=Supports ? new[]{CompetitionProtocol.ContentEffectsFeature} : new string[0] };
            else if (url.EndsWith("/auth/candidates")) return Task.FromResult(new CompetitionHttpResult {Status=202,Body="{}"});
            else if (url.EndsWith("/auth/challenges")) reply = new { fixture=true };
            else if (url.EndsWith("/runs"))
            {
                ++Posts;
                var outer=CompetitionProtocol.Json().Deserialize<Dictionary<string,object>>(body);
                var seal=CompetitionProtocol.Json().Deserialize<Dictionary<string,object>>(CompetitionAuthProtocol.Decode((string)outer["sealed_run"]));
                Last=CompetitionProtocol.Json().Deserialize<CompetitionRun>(CompetitionAuthProtocol.Decode((string)seal["payload_base64"]));
                if (LoseReceipt) {LoseReceipt=false;return Task.FromResult(new CompetitionHttpResult{Status=0});}
                reply=new {protocol=CompetitionProtocol.Online,scope=CompetitionProtocol.Scope,configuration_id=Last.ranking.configuration_id,
                    route_sha256=Last.route_sha256,receipt=new {id=123,run_id=Last.run_id,daily_status="pending_review",auth_verified=true,board_eligible=false}};
            }
            else throw new Exception("Unexpected initialization endpoint");
            return Task.FromResult(new CompetitionHttpResult{Status=200,Body=CompetitionProtocol.Json().Serialize(reply)});
        }
        public void Dispose() { }
    }
    static async Task MaintenanceInitializationCases()
    {
        var resourceNames=new[]{"DATA.MKF","M.MSG","SSS.MKF","WORD.DAT"};
        var categories=new HashSet<string>();
        foreach(string profile in new[]{"geshi-easy-v1","dream222-normal-v1","dream222-hard-v1","dreamspace39x-hard-v1"})
        foreach(bool wuqiang in new[]{false,true})
        {
            var snapshot=new SdlpalSnapshot {schema=1,verified=true,diagnostic=false,profile=profile,exeHash=new string('1',64),
                releaseRevision="r54",resourceHashes=resourceNames.ToDictionary(p=>p,p=>new string('2',64)),
                fadeCycleMs=800,walkFrameMs=90,speed=0,battleSpeed=0,respawnRule="pal98-current-scene-v1",wuqiang=wuqiang};
            string[] ids=profile=="geshi-easy-v1"?new[]{"geshi-eight-headed-serpent","geshi-ending"}:new[]{"dream-ship","dream-ending"};
            var run=GeshiConfigurationInitialization.CreateMaintenance(snapshot,"fixture-device-init","3.38.3.7",ids);
            Check(run.ranking.Valid(run.gameplay)&&CompetitionProtocol.Validate(run)==""&&categories.Add(run.ranking.configuration_id),"eight content/effect configurations remain separate");
            Check(run.total_ms==0&&run.restart_ms==0&&!run.observations.began_here&&!run.observations.finished&&
                run.splits.All(p=>p.status=="in_progress"&&p.elapsed_ms==null)&&run.observations.issues.Contains("synthetic_configuration_initialization"),"initializer cannot claim a completed playthrough");
            Check(run.release_revision=="r54"&&run.gameplay.speed==0&&run.gameplay.battle_speed==0&&run.ranking.SpeedIndependent,"release and actual speed are observation metadata only");
            snapshot.speed=10;snapshot.battleSpeed=7;snapshot.fadeCycleMs=600;snapshot.walkFrameMs=40;
            var changed=GeshiConfigurationInitialization.CreateMaintenance(snapshot,"fixture-device-init","3.38.3.7",ids);
            Check(changed.ranking.configuration_id==run.ranking.configuration_id&&changed.gameplay.speed==10&&changed.gameplay.walk_frame_ms==40,"initial speed never changes a maintenance category");
            var wire=new InitializationWire {Approved=false};var auth=new FakeAuth();
            var credential=new CompetitionCredential{Hwid="fixture-device-init",Secret="fixture-only-not-production"};
            string directory=Path.Combine(root,"initializers",profile+(wuqiang?"-wuqiang":"-normal"));
            Func<Task<int>> upload=()=>GeshiConfigurationInitialization.Upload(snapshot,directory,"https://fixture.invalid",credential,auth,wire,ids);
            Check(await upload()==2&&wire.Posts==0,"unapproved initialization never sends a record");
            wire.Approved=true;wire.Supports=false;
            Check(await upload()==2&&wire.Posts==0,"old server cannot receive new content-only initialization");
            wire.Supports=true;wire.LoseReceipt=true;
            Check(await upload()==2&&wire.Posts==1,"lost receipt retains pending initialization");
            string pending=File.ReadAllText(Path.Combine(directory,"initialization.pending.json"));
            string original=wire.Last.run_id;
            Check(await upload()==0&&wire.Posts==2&&wire.Last.run_id==original&&pending==File.ReadAllText(Path.Combine(directory,"initialization.pending.json")),"retry preserves exact run id and sealed bytes");
            int calls=wire.Calls;Check(await upload()==0&&calls==wire.Calls,"existing receipt returns with zero network");
            snapshot.speed++;
            bool denied=false;try{await upload();}catch(InvalidDataException){denied=true;}
            Check(denied,"changed actual input cannot rewrite a historical initialization seal");
            snapshot.diagnostic=true;denied=false;
            try{GeshiConfigurationInitialization.CreateMaintenance(snapshot,"fixture-device-init","3.38.3.7",ids);}catch(InvalidDataException){denied=true;}
            Check(denied,"diagnostic snapshot cannot masquerade as release configuration evidence");
        }
    }
    static async Task SpeedIndependentClientCases()
    {
        var wire=new Fake {Features=new[]{CompetitionProtocol.RestartTimeFeature,CompetitionProtocol.MaintenanceFeature,CompetitionProtocol.ContentEffectsFeature}};
        var store=Store("content-speed");var client=new CompetitionClient(store,wire,new FakeAuth());client.Configure(Config());
        await Until(()=>wire.Registers>0,"content-only test server capability discovered");
        Func<int,CompetitionObservation> observe=step=>{
            var o=SdlpalObservation("same-content",step);
            foreach(string key in new[]{"fade_ms","map_speed_ticks","walk_frame_ms","speed","battle_speed","engine_sha256","engine_build","route_rules_sha256"})o.Ranking.rules.Remove(key);
            o.Gameplay.fade_ms=o.FadeMilliseconds=step==0?800:600;o.Gameplay.walk_frame_ms=step==0?90:35;
            o.Gameplay.speed=step==0?0:9;o.Gameplay.battle_speed=step==0?0:4;
            o.Ranking.configuration_id=RankingConfiguration.Digest(o.Ranking.rules);o.Gameplay.rules_sha256=o.Ranking.configuration_id;
            o.TimelineId=TimelineIdentity.Create(o.Ranking.configuration_id,CompetitionProtocol.RouteHash(o.Splits.Select(p=>p.checkpoint_id)),false);return o;
        };
        client.Publish(observe(0));client.Publish(observe(1));client.Publish(observe(2));
        await Until(()=>wire.Posts==1,"changed speed uploads in the same content category");
        var run=CompetitionProtocol.Json().Deserialize<CompetitionRun>(wire.Payloads.Single());
        Check(run.observations.issues.Length==0&&run.gameplay.speed==9&&run.observations.initial_gameplay.speed==0,
            "speed changes preserve qualification and truthful initial/final speed evidence");
        await client.CloseAsync();
    }
}
