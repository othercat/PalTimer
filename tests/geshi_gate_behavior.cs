// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Pal98Timer;

internal static partial class SdlpalBehavior
{
    static void GeshiGateTests()
    {
        var template=SdlpalGeshiRoutes.Create();SdlpalSplitRule.Validate(template);
        var names=new[]{"出地宫","进试炼窟","过大理钱无通","过京城赵莲儿","过赤鬼王","过地魁","收归服之剑",
            "回阳间（打败南盗侠李逍遥）","出桃源村幻境（使用命运罗盘）","过剑圣","回现实（打败楼澈）","进麻烦洞",
            "进扬州","救林品如（第二次使用命运罗盘）","出扬州","过阳间游天霸","过游乐","过阴间游天霸"};
        Check(template.Select(r=>r.Name).SequenceEqual(names),"route template retains all eighteen user-specified labels and their order");
        foreach(string p in new[]{"geshi-easy-v1","geshi-hard-v1","geshi-nightwolf-v1","geshi-fanmade-v1","geshi-fanmade-entertainment-v1"})
            Check(SdlpalGeshiRoutes.Supports(p),"audited shared-template scope: "+p);
        Check(!SdlpalGeshiRoutes.Supports("dreamspace35-v1")&&!SdlpalGeshiRoutes.Supports("future-geshi-v1"),"unrelated or unknown content cannot borrow Geshi definitions");
        Check(template[11].Kind=="scene"&&template[11].Scene==106,"cave entrance uses the confirmed human-Buhuagu road scene");
        System.IO.File.WriteAllText(System.IO.Path.Combine(root,"route-template.json"),SerializeFixture(new{
            node12Verified=true,runtimeDefaultsUpdated=true,rules=template}));
        foreach(var rule in template.Where(r=>r.Kind=="scene-transition"))
        {
            var observer=new SdlpalRunObserver();var s=Snapshot();s.started=true;s.scene=rule.ReturnScene.Value;
            observer.Observe(Clone(s),template);
            Check(!observer.Reached.Contains(rule.Id),"initial connection in a destination is not an observed transition: "+rule.Id);
            s.scene=rule.Scene.Value;observer.Observe(Clone(s),template);s.scene=rule.ReturnScene.Value;
            observer.Observe(Clone(s),template);Check(observer.Reached.Contains(rule.Id),"audited directional transition completes: "+rule.Id);
            observer=new SdlpalRunObserver();s.scene=rule.Scene.Value;observer.Observe(Clone(s),template);
            s.scene=rule.ReturnScene.Value;s.epoch++;s.loadedFrom=1;observer.Observe(Clone(s),template);
            Check(!observer.Reached.Contains(rule.Id),"loading across an entrance cannot fabricate a transition: "+rule.Id);
        }
        var escape = new SdlpalSplitRule { Id="geshi-peach-escape", Name="出桃源村幻境（使用命运罗盘）",
            Kind="item-consumed", Scene=184, ReturnScene=64, Item=567 };
        var rescue = new SdlpalSplitRule { Id="geshi-rescue-pinru", Name="救林品如（第二次使用命运罗盘）",
            Kind="item-consumed", Scene=294, ReturnScene=294, Item=583, Event=5372, EventState=0 };
        var youle = new SdlpalSplitRule { Id="geshi-youle", Name="过游乐", Kind="battle-after-loss", Team=305, Scene=14, Enemy=539, Event=223, EventState=0 };
        var end = new SdlpalSplitRule { Id="geshi-underworld-youtianba", Name="过阴间游天霸", Kind="battle-finish", Team=307, Scene=154, Enemy=541 };
        var rules = new[] {escape,rescue,youle,end}; SdlpalSplitRule.Validate(rules);
        Check(true,"new consumption/prelude/final gates form a valid route");
        var old = LegacyGeshiRules(); SdlpalSplitRule.Validate(old);
        Check(old.Last().Kind=="completion","legacy explicit completion routes remain valid");
        var changed = SdlpalBridge.Json().Deserialize<SdlpalSplitRule[]>(SerializeFixture(rules));
        Check(SdlpalSplitRule.Identity(changed)==SdlpalSplitRule.Identity(rules),"gate fields survive JSON roundtrip");
        changed[1].EventState=1;
        Check(SdlpalSplitRule.Identity(changed)!=SdlpalSplitRule.Identity(rules),"required story state partitions route identity");
        changed[1].EventState=null;Reject(()=>SdlpalSplitRule.Validate(changed),"partial event gate cannot be saved");
        changed=SdlpalBridge.Json().Deserialize<SdlpalSplitRule[]>(SerializeFixture(rules));changed[0].ReturnScene=null;
        Reject(()=>SdlpalSplitRule.Validate(changed),"consumption gate requires an explicit destination");
        changed=SdlpalBridge.Json().Deserialize<SdlpalSplitRule[]>(SerializeFixture(rules));changed[3].Enemy=null;
        Reject(()=>SdlpalSplitRule.Validate(changed),"final battle requires exact team scene and core enemy");

        Func<int,int,int,SdlpalSnapshot> stock=(scene,item,count)=>{
            var s=Snapshot();s.started=true;s.scene=scene;
            s.inventory=count==0?new SdlpalInventory[0]:new[]{new SdlpalInventory{id=item,amount=count}};
            s.events=new[]{new SdlpalMapEvent{id=5372,state=2}};return s;
        };
        foreach(bool sameSourceSnapshot in new[]{false,true})
        {
            var gate=new SdlpalItemConsumptionGate();var s=stock(184,567,1);
            Check(!gate.Observe(s,escape),"owning compass567 does not count a use");
            s.inventory=new SdlpalInventory[0];
            if(sameSourceSnapshot)Check(!gate.Observe(s,escape),"decrease before scene change is pending, not completed");
            s.scene=64;Check(gate.Observe(s,escape),"first compass requires a decrease and scene64, both publication orders");
        }
        foreach(int scene in new[]{1,64,183,294})
        {
            var gate=new SdlpalItemConsumptionGate();var s=stock(scene,567,1);gate.Observe(s,escape);
            s.scene=64;s.inventory=new SdlpalInventory[0];
            Check(!gate.Observe(s,escape),"wrong origin cannot manufacture first compass use: "+scene);
        }
        {
            var gate=new SdlpalItemConsumptionGate();var s=stock(184,567,1);gate.Observe(s,escape);s.scene=64;
            Check(!gate.Observe(s,escape),"entering target scene while still owning compass is not use");
            s.inventory=new SdlpalInventory[0];Check(!gate.Observe(s,escape),"a later decrease in the destination cannot retroactively count");
        }
        foreach(bool hasEvents in new[]{false,true}) foreach(int eventState in new[]{0,2})
        {
            var gate=new SdlpalItemConsumptionGate();var s=stock(294,583,1);gate.Observe(s,rescue);
            s.inventory=new SdlpalInventory[0];s.events=hasEvents?new[]{new SdlpalMapEvent{id=5372,state=eventState}}:null;
            Check(gate.Observe(s,rescue)==(hasEvents&&eventState==0),"second compass requires actual consumption and extinguished fire state: "+hasEvents+"/"+eventState);
        }
        {
            var gate=new SdlpalItemConsumptionGate();var s=stock(294,583,1);s.events[0].state=0;gate.Observe(s,rescue);
            Check(!gate.Observe(s,rescue),"rescued-looking scene plus item ownership is insufficient");
            s.ItemDataError="missing capability";s.inventory=new SdlpalInventory[0];
            Check(!gate.Observe(s,rescue),"invalid item data is not interpreted as zero stock");
        }
        {
            var s=stock(294,583,1);var payload=System.Text.Encoding.UTF8.GetBytes(SerializeFixture(s));
            var decoded=SdlpalBridge.Decode(Header(s,payload.Length),payload,777,123,10000);
            Check(decoded.EventDataError==null&&decoded.events.Single().id==5372,"existing native event fields decode without engine changes");
            var fields=SdlpalBridge.Json().Deserialize<Dictionary<string,object>>(SerializeFixture(s));fields.Remove("events");
            payload=System.Text.Encoding.UTF8.GetBytes(SerializeFixture(fields));
            decoded=SdlpalBridge.Decode(Header(s,payload.Length),payload,777,123,10000);
            Check(decoded.EventDataError!=null&&decoded.profile==s.profile,"legacy snapshots remain usable but cannot fabricate story state");
            foreach(object state in new object[]{"0",null,32768})
            {
                fields["events"]=new[]{new Dictionary<string,object>{{"id",5372},{"state",state}}};
                payload=System.Text.Encoding.UTF8.GetBytes(SerializeFixture(fields));
                bool rejected=false;
                try {decoded=SdlpalBridge.Decode(Header(s,payload.Length),payload,777,123,10000);rejected=decoded.EventDataError!=null;}
                catch(Exception){rejected=true;}
                Check(rejected,"noncanonical story state cannot satisfy gate: "+(state??"null"));
            }
        }
        Func<int,int,ulong,SdlpalBattleEvent> fight=(team,result,seq)=>new SdlpalBattleEvent{
            team=team,result=result,seq=seq,epoch=1,scene=team==305?14:154,enemies=new[]{team==305?539:541}};
        foreach(bool onePublication in new[]{false,true})
        {
            var observer=new SdlpalRunObserver();var s=Snapshot();observer.Observe(Clone(s),rules);s.started=true;observer.Observe(Clone(s),rules);
            s.scene=14;s.events=new[]{new SdlpalMapEvent{id=223,state=2}};
            s.battleEvents=new[]{fight(305,1,1)};
            if(!onePublication){observer.Observe(Clone(s),rules);Check(!observer.Reached.Contains(youle.Id),"scripted first loss does not complete Youle");}
            s.battleEvents=new[]{fight(305,1,1),fight(305,3,2)};observer.Observe(Clone(s),rules);
            Check(!observer.Reached.Contains(youle.Id),"retrying first-phase loss then abnormal win cannot complete Youle");
            s.events[0].state=0;observer.Observe(Clone(s),rules);
            Check(observer.Reached.Contains(youle.Id),"Youle requires loss then victory and real second-phase plot state");
            observer.Observe(Clone(s),rules);Check(observer.Reached.Count==1,"duplicate publications do not duplicate checkpoints");
            s.completed=true;observer.Observe(Clone(s),rules);
            Check(!observer.Reached.Contains(end.Id),"completed flag cannot replace the requested final boss victory");
            s.battleEvents=new[]{fight(305,1,1),fight(305,3,2),fight(307,3,3)};observer.Observe(Clone(s),rules);
            Check(observer.Reached.Contains(end.Id),"exact underworld victory completes the new route");
        }
        foreach(string mode in new[]{"no-loss","wrong-scene","epoch","import","gap"})
        {
            var observer=new SdlpalRunObserver();var s=Snapshot();observer.Observe(Clone(s),rules);s.started=true;s.scene=14;s.events=new[]{new SdlpalMapEvent{id=223,state=0}};observer.Observe(Clone(s),rules);
            if(mode!="no-loss")
            {
                var loss=fight(305,1,1);if(mode=="wrong-scene")loss.scene=15;
                s.battleEvents=new[]{loss};observer.Observe(Clone(s),rules);
            }
            if(mode=="epoch"){s.epoch++;s.loadedFrom=1;s.battleEvents=new SdlpalBattleEvent[0];observer.Observe(Clone(s),rules);}
            if(mode=="import")observer.MarkImported(Clone(s));
            var win=fight(305,3,mode=="no-loss"?1UL:mode=="gap"?3UL:2UL);win.epoch=s.epoch;
            s.battleEvents=new[]{win};observer.Observe(Clone(s),rules);
            Check(!observer.Reached.Contains(youle.Id),"Youle rejects missing or invalid prelude: "+mode);
        }
        foreach(string mode in new[]{"epoch","import","gap"})
        {
            var observer=new SdlpalRunObserver();var s=Snapshot();observer.Observe(Clone(s),rules);
            s.started=true;s.scene=14;s.events=new[]{new SdlpalMapEvent{id=223,state=2}};observer.Observe(Clone(s),rules);
            s.battleEvents=new[]{fight(305,1,1),fight(305,3,2)};observer.Observe(Clone(s),rules);
            Check(!observer.Reached.Contains(youle.Id),"pending second-phase result waits for actual plot state");
            if(mode=="epoch"){s.epoch++;s.loadedFrom=1;s.battleEvents=new SdlpalBattleEvent[0];observer.Observe(Clone(s),rules);}
            if(mode=="import")observer.MarkImported(Clone(s));
            if(mode=="gap"){s.battleEvents=new[]{fight(307,3,4)};observer.Observe(Clone(s),rules);}
            s.events[0].state=0;observer.Observe(Clone(s),rules);
            Check(!observer.Reached.Contains(youle.Id),"stale pending victory cannot cross state reset: "+mode);
        }
        {
            var s=stock(184,567,1);var observer=new SdlpalRunObserver();observer.Observe(Clone(s),rules);
            s.epoch++;s.loadedFrom=1;s.scene=64;s.inventory=new SdlpalInventory[0];observer.Observe(Clone(s),rules);
            Check(!observer.Reached.Contains(escape.Id),"ordinary load cannot manufacture compass use");
        }
        using(var editor=new SdlpalRulesForm(rules,stock(294,583,1)))
        {
            var bar=editor.Controls.OfType<System.Windows.Forms.FlowLayoutPanel>().Single();
            var save=bar.Controls.OfType<System.Windows.Forms.Button>().Single(b=>b.Text=="验证并保存");
            typeof(System.Windows.Forms.Button).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(save,new object[]{EventArgs.Empty});
            Check(editor.Rules!=null&&SdlpalSplitRule.Identity(editor.Rules)==SdlpalSplitRule.Identity(rules),"actual advanced editor preserves new gate fields");
        }
    }
}
