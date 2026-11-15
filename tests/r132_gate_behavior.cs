using System;
using System.Collections.Generic;
using System.Linq;
using Pal98Timer;
internal static partial class SdlpalBehavior {
    static SdlpalStoryFacts EmptyR132Facts() => new SdlpalStoryFacts {revision=1};
    static void SetR132Fact(SdlpalSnapshot s,int gate,ulong sequence) {
        s.storyFacts.sequence=sequence;typeof(SdlpalStoryFacts).GetProperty("gate"+gate).SetValue(s.storyFacts,sequence,null);
    }
    static void GeshiGateTests() {
        var names=new[]{"送死","圣灵觉醒","到京城","过京城赵莲儿","过赤鬼王","过地魁","收归服之剑","回阳间","出桃源村幻境","过剑圣","回现实","进麻烦洞","进扬州","救林品如","出扬州","过阳间游天霸","过游乐","通关"};
        var profiles=new[]{"dream211","dream211-pudu","dream211-yueying","dream211-yueying-rat","dream22-puppet100","dream222-hard","dream222-normal","dreamspace35","dreamspace39x-hard","geshi-easy","geshi-fanmade","geshi-fanmade-entertainment","geshi-hard","geshi-nightwolf","hunqian-branch","hunqian167-easy","hunqian167-hard","mogai10","pal-dos","pal98-classic-v6","pal98-suiji-v02","tianqi20"};
        foreach(string cid in profiles) {
            string profile=cid+"-v1";var rules=SdlpalSplitRule.Defaults(profile);SdlpalSplitRule.Validate(rules);
            var final=rules.Last();Check(final.Kind=="ending-shown" && final.Name=="通关" && final.Id.EndsWith("-shown-r132"),"actual visible ending and online checkpoint identity "+cid);
            var observer=new SdlpalRunObserver();var s=Snapshot();s.profile=profile;s.storyFacts=EmptyR132Facts();observer.Observe(Clone(s),rules);
            s.started=true;observer.Observe(Clone(s),rules);s.completed=true;
            s.battleEvents=new[]{new SdlpalBattleEvent {seq=1,epoch=s.epoch,scene=297,team=389,enemies=new[]{576},result=3}};observer.Observe(Clone(s),rules);
            Check(!observer.Reached.Contains(final.Id),"last battle/early completion cannot finish new route "+cid);
            s.storyFacts.endingShown=true;observer.Observe(Clone(s),rules);Check(observer.Reached.Contains(final.Id),"first visible ending finishes without confirmation "+cid);
            int reached=observer.Reached.Count;observer.Observe(Clone(s),rules);Check(observer.Reached.Count==reached,"ending is idempotent "+cid);
            var story=SdlpalSplitRule.Defaults(profile,true);SdlpalSplitRule.ValidateStoryRoute(story);
            observer=new SdlpalRunObserver();s=Snapshot();s.profile=profile;s.storyFacts=EmptyR132Facts();s.storyMod01=true;s.storyModRevision=4;s.schema=3;
            observer.Observe(Clone(s),story);s.started=true;observer.Observe(Clone(s),story);s.storyFacts.endingShown=true;observer.Observe(Clone(s),story);
            Check(!observer.Reached.Contains(story.Last().Id),"ordinary ending does not replace richest celebration "+cid);
            s.completed=true;observer.Observe(Clone(s),story);Check(observer.Reached.Contains(story.Last().Id),"richest celebration retains dedicated completion "+cid);
            if(!cid.StartsWith("geshi-",StringComparison.Ordinal))continue;
            Check(rules.Length==18 && rules.Select(r=>r.Name).SequenceEqual(names),"five Geshi retain exact eighteen-node order "+cid);
            Check(rules.Take(4).Select(r=>r.Fact.Value).SequenceEqual(new[]{1,2,3,4}) && rules[8].Fact==5 && rules[13].Fact==6,"all six completed-operation gates "+cid);
            foreach(int fact in Enumerable.Range(1,6)) {
                var rule=rules.Single(r=>r.Fact==fact);observer=new SdlpalRunObserver();s=Snapshot();s.profile=profile;s.storyFacts=EmptyR132Facts();observer.Observe(Clone(s),rules);
                s.started=true;observer.Observe(Clone(s),rules);s.scene=fact==2?47:fact==3?100:139;
                s.battleEvents=new[]{new SdlpalBattleEvent {seq=1,epoch=s.epoch,scene=139,team=39,enemies=new[]{468},result=3}};observer.Observe(Clone(s),rules);
                Check(!observer.Reached.Contains(rule.Id),"scene/death/battle/entry alone cannot invent fact "+cid+"/"+fact);
                SetR132Fact(s,fact,1);observer.Observe(Clone(s),rules);Check(observer.Reached.Contains(rule.Id),"new completed-operation fact reaches node "+cid+"/"+fact);
                int countBefore=observer.Reached.Count;s.epoch++;s.loadedFrom=1;s.battleEvents=new SdlpalBattleEvent[0];s.storyFacts=EmptyR132Facts();s.storyFacts.sequence=1;observer.Observe(Clone(s),rules);
                SetR132Fact(s,fact,2);observer.Observe(Clone(s),rules);Check(observer.Reached.Count==countBefore,"return/load does not duplicate reached node "+cid+"/"+fact);
                observer=new SdlpalRunObserver();observer.Observe(Clone(s),rules);observer.Observe(Clone(s),rules);Check(!observer.Reached.Contains(rule.Id),"late attach cannot replay stale facts "+cid+"/"+fact);
                observer=new SdlpalRunObserver();observer.MarkImported(Clone(s));observer.Observe(Clone(s),rules);Check(!observer.Reached.Contains(rule.Id),"import rebases fact cursor "+cid+"/"+fact);
            }
            using(var editor=new SdlpalRulesForm(rules,Snapshot())) {
                var bar=editor.Controls.OfType<System.Windows.Forms.FlowLayoutPanel>().Single();
                var save=bar.Controls.OfType<System.Windows.Forms.Button>().Single(b=>b.Text=="验证并保存");
                typeof(System.Windows.Forms.Button).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(save,new object[]{EventArgs.Empty});
                Check(editor.Rules!=null && SdlpalSplitRule.Identity(editor.Rules)==SdlpalSplitRule.Identity(rules),"real advanced editor preserves fact ID "+cid);
            }
        }
        var custom=new[]{new SdlpalSplitRule {Id="custom-start",Name="自定义（保留）",Kind="scene",Scene=106},new SdlpalSplitRule {Id="custom-end",Name="旧末点",Kind="completion"}};
        string bytes=SerializeFixture(custom),identity=SdlpalSplitRule.Identity(custom);SdlpalSplitRule.Validate(custom);
        var roundtrip=SdlpalBridge.Json().Deserialize<SdlpalSplitRule[]>(bytes);
        Check(bytes==SerializeFixture(roundtrip) && identity==SdlpalSplitRule.Identity(roundtrip),"historical/custom files retain bytes and identity");
        Check(identity!=SdlpalSplitRule.Identity(SdlpalSplitRule.Defaults("geshi-easy-v1")),"new route never inherits historical/custom PB/relay identity");
        var raw=Snapshot();raw.storyFacts=EmptyR132Facts();raw.ma35=new SdlpalMa35State{revision=1,enabled=true,feeds=34,departed=true,npcGone=false};
        string encoded=SerializeFixture(raw);var decoded=SdlpalBridge.ReadSnapshotJson(encoded);Check(decoded.storyFacts.revision==1 && decoded.ma35.feeds==34,"strict optional story/Ma35 decoder");
        var historical=Clone(raw);historical.storyFacts=null;
        Check(RankingConfiguration.Digest(raw.RankingFacts())!=RankingConfiguration.Digest(historical.RankingFacts()),"ending timing contract isolates PB even for retained custom completion nodes");
        Check(!historical.RankingFacts().ContainsKey("config.slot.RuntimeEndingShownRevision"),"legacy engine category bytes remain unchanged");
        foreach(string invalid in new[]{encoded.Replace("\"gate1\":0","\"gate1\":0.5"),encoded.Replace("\"gate1\":0","\"gate1\":0,\"gate1\":1"),encoded.Replace("\"feeds\":34","\"feeds\":36")})
            Reject(()=>SdlpalBridge.ReadSnapshotJson(invalid),"reject malformed r132 facts");
    }
}
