// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Pal98Timer;

internal static partial class SdlpalBehavior
{
    static readonly string[] GeshiProfiles={"geshi-easy-v1","geshi-hard-v1","geshi-nightwolf-v1","geshi-fanmade-v1","geshi-fanmade-entertainment-v1"};
    static string RulePath(string profile) => TimerUserSettings.GetPath(profile=="geshi-easy-v1"?"sdlpal_geshi_splits.json":"sdlpal_"+profile+"_splits.json");
    sealed class TemporaryGeshiRules : IDisposable
    {
        readonly Dictionary<string,byte[]> previous=new Dictionary<string,byte[]>();
        internal TemporaryGeshiRules(SdlpalSplitRule[] rules)
        {
            if(rules!=null)SdlpalSplitRule.Validate(rules);
            foreach(string profile in GeshiProfiles)
            {
                string path=RulePath(profile);previous[path]=File.Exists(path)?File.ReadAllBytes(path):null;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if(rules==null)File.Delete(path);else File.WriteAllText(path,SerializeFixture(rules));
            }
        }
        public void Dispose()
        {
            foreach(var entry in previous)
                if(entry.Value==null)File.Delete(entry.Key);else File.WriteAllBytes(entry.Key,entry.Value);
        }
    }
    static SdlpalSplitRule[] LegacyGeshiRules() => new[] {
        new SdlpalSplitRule{Id="geshi-eight-headed-serpent",Name="八头蛇胜利",Kind="battle",Team=315,Enemy=547},
        new SdlpalSplitRule{Id="geshi-ending",Name="通关",Kind="completion"}
    };
    static void GeshiLifecycleTests()
    {
        var rules=SdlpalGeshiRoutes.Create();
        var records=new List<object>();
        // No custom files: exercise actual shipped defaults and profile switches.
        using(var files=new TemporaryGeshiRules(null))
        {
            foreach(string profile in GeshiProfiles) foreach(bool nonSequential in new[]{false,true})
            using(var run=new CompletionFixture(nonSequential))
            {
                var s=run.Engine.State;s.profile=profile;s.scene=281;run.StartNewGame();
                Check(run.Core.CheckPoints.Count==18&&run.Core.CheckPoints.Select(p=>p.Name).SequenceEqual(rules.Select(r=>r.Id)),
                    "default eighteen-node route loads in production core: "+profile+"/"+nonSequential);
                var events=new List<SdlpalBattleEvent>();ulong sequence=0;
                Action<int,string> tick=(expected,label)=>{
                    run.Tick();Check(run.Core.CurrentStep==expected,"ordered node progress "+profile+"/"+nonSequential+" "+label);
                    Check(run.Core.CheckPoints.Take(expected).All(p=>p.Status==CheckPointStatus.Completed)&&
                        run.Core.CheckPoints.Skip(expected+1).All(p=>p.Status==CheckPointStatus.NotStarted),
                        "no manual or automatic skip on the observed route: "+label);
                };
                Action<int,int,int,int,int> battle=(team,scene,enemy,result,step)=>{
                    s.scene=scene;s.battle.sequence=++sequence;
                    events.Add(new SdlpalBattleEvent{seq=sequence,epoch=s.epoch,team=team,scene=scene,enemies=new[]{600,enemy},result=result});
                    if(team==305)s.events=new[]{new SdlpalMapEvent{id=223,state=result==3?0:2}};
                    s.battleEvents=events.ToArray();tick(step,"battle "+team+" result "+result);
                };
                s.scene=277;tick(1,"出地宫");s.scene=285;tick(2,"进试炼窟");
                battle(382,86,579,3,3);battle(383,80,580,3,4);battle(27,59,473,3,5);battle(25,156,502,3,6);
                s.scene=65;s.inventory=new SdlpalInventory[0];tick(6,"sword reward not yet granted");
                s.inventory=new[]{new SdlpalInventory{id=175,amount=1}};tick(7,"actual sword reward");
                battle(21,168,480,3,8);
                run.Core.IsUIPause=true;run.Tick();var paused=run.Core.GetMainWatch();Thread.Sleep(12);run.Tick();
                Check(!run.Watch.IsRunning&&run.Core.GetMainWatch()==paused&&run.Core.CurrentStep==8,"pause preserves current time and node on the long route");
                run.Core.IsUIPause=false;run.Tick();Check(run.Watch.IsRunning,"explicit resume continues the same eighteen-node run");
                s.scene=184;s.inventory=new[]{new SdlpalInventory{id=175,amount=1},new SdlpalInventory{id=567,amount=1}};tick(8,"first compass armed");
                s.scene=64;s.inventory=new[]{new SdlpalInventory{id=175,amount=1}};tick(9,"first compass used");
                battle(163,227,494,3,10);battle(26,148,472,3,11);
                string timeline=run.Core.GetRStr();
                var imported=Call(run.Core,"ValidateImport",timeline,run.Core.CurrentSnapshot.IdentityKey);
                Check(imported!=null,"eighteen-node timeline accepts its own actual identity");
                // JSON fields make the wrong-profile and wrong-route cases
                // independent of UI state.
                var fields=SdlpalBridge.Json().Deserialize<Dictionary<string,object>>(timeline);
                fields["SdlpalProfile"]=profile=="geshi-easy-v1"?"geshi-hard-v1":"geshi-easy-v1";
                Reject(()=>Call(run.Core,"ValidateImport",SerializeFixture(fields),run.Core.CurrentSnapshot.IdentityKey),"another Geshi profile cannot import the common-looking route");
                fields=SdlpalBridge.Json().Deserialize<Dictionary<string,object>>(timeline);fields["SdlpalRoute"]=SdlpalSplitRule.Identity(LegacyGeshiRules());
                Reject(()=>Call(run.Core,"ValidateImport",SerializeFixture(fields),run.Core.CurrentSnapshot.IdentityKey),"old two-node timeline cannot masquerade as the new route");
                Check(run.Core.GetRStr().Contains("geshi-return-reality")&&run.Core.CurrentStep==11,"rejected import retains current route and progress");
                s.scene=106;tick(12,"confirmed human-Buhuagu disabled-skill road");
                s.scene=81;tick(12,"Yangzhou source road");s.scene=84;tick(13,"real Yangzhou entry");
                s.scene=294;s.inventory=new[]{new SdlpalInventory{id=175,amount=1},new SdlpalInventory{id=583,amount=1}};
                s.events=new[]{new SdlpalMapEvent{id=5372,state=2}};tick(13,"second compass armed");
                s.events[0].state=0;tick(13,"fire state alone does not rescue");
                s.inventory=new[]{new SdlpalInventory{id=175,amount=1}};tick(14,"second compass and rescue");
                s.scene=84;tick(14,"Yangzhou before exit");s.scene=105;tick(15,"Yangzhou exit");
                battle(309,154,543,3,16);battle(305,14,539,1,16);battle(305,14,539,3,17);
                if(profile=="geshi-nightwolf-v1")battle(108,154,499,3,17);
                Check(run.Watch.IsRunning,"route stays running before underworld final victory");
                battle(307,154,541,3,18);
                Check(!run.Watch.IsRunning&&run.Core.CheckPoints.All(p=>p.Status==CheckPointStatus.Completed),"final requested boss stops all eighteen checkpoints");
                var finalTime=run.Core.GetMainWatch();var times=run.Core.CheckPoints.Select(p=>p.Current).ToArray();
                for(int i=0;i<3;i++){Thread.Sleep(12);run.Tick();}
                Check(!run.Watch.IsRunning&&run.Core.CurrentStep==18&&run.Core.GetMainWatch()==finalTime&&
                    run.Core.CheckPoints.Select(p=>p.Current).SequenceEqual(times),"completed long route remains frozen");
                timeline=run.Core.GetRStr();imported=Call(run.Core,"ValidateImport",timeline,run.Core.CurrentSnapshot.IdentityKey);
                Call(run.Core,"Restore",imported,run.Core.CurrentSnapshot);
                Check(run.Core.IsUIPause&&run.Core.CurrentStep==18&&run.Core.CheckPoints.All(p=>p.Status==CheckPointStatus.Completed),"completed eighteen-node restore preserves pause and every node status");
                run.Core.IsUIPause=false;run.Tick();Check(!run.Watch.IsRunning&&run.Core.CurrentStep==18,"resuming a completed imported route does not restart timing");
                records.Add(new{profile,nonSequential,passed=true,completedNodes=18,node12Verified=true});
            }
            // Profile switch uses real OnTick/InitCheckPoints, not just a factory.
            using(var run=new CompletionFixture(true))
            {
                run.StartNewGame();
                foreach(string profile in GeshiProfiles)
                {
                    run.Engine.State.profile=profile;run.Tick();
                    Check(run.Core.ActiveProfile==profile&&run.Core.CheckPoints.Count==18,"automatic profile selection uses the shared default route: "+profile);
                }
                run.Engine.State.profile="dreamspace35-v1";run.Tick();
                Check(run.Core.CheckPoints.Count==21&&run.Core.CheckPoints[0].Name=="dream-ship","unrelated content retains its separate default route");
                run.Engine.State.profile="geshi-easy-v1";run.Tick();
                Check(run.Core.CheckPoints.Count==18&&run.Core.CheckPoints[0].Name==rules[0].Id,"switching back restores the shared default Geshi route");
            }
        }
        using(var files=new TemporaryGeshiRules(LegacyGeshiRules()))
        using(var run=new CompletionFixture(true))
        {
            run.StartNewGame();string path=run.Core.ActiveBestPath;run.Core.SaveBest();byte[] old=File.ReadAllBytes(path);
            byte[] oldRules=File.ReadAllBytes(RulePath("geshi-easy-v1"));
            Call(run.Core,"SaveProfileRules",rules,"geshi-easy-v1");run.Core.Reset();Call(run.Core,"InitCheckPoints");
            run.Engine.State.started=false;run.Tick();run.Engine.State.started=true;run.Tick();
            Check(run.Core.ActiveBestPath!=path&&run.Core.CheckPoints.Count==18,"new semantic route chooses a different PB file");
            Check(File.ReadAllBytes(path).SequenceEqual(old),"old two-node PB bytes survive the route update");
            Check(Directory.GetFiles(Path.GetDirectoryName(RulePath("geshi-easy-v1")),Path.GetFileName(RulePath("geshi-easy-v1"))+".backup-*").Any(p=>File.ReadAllBytes(p).SequenceEqual(oldRules)),
                "real rule-save path keeps the prior user configuration backup");
            File.Delete(path);
        }
        File.WriteAllText(Path.Combine(root,"lifecycle-summary.json"),SerializeFixture(new{
            schema="PalTimer.Geshi18Lifecycle.v1",passed=true,cases=records.Count,records,
            node12Verified=true,runtimeDefaultsUpdated=true,gameplayExecuted=false,networkRequests=0}));
    }
}
