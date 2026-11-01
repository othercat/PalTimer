// SPDX-License-Identifier: GPL-2.0-only
using Pal98Timer;
using Pal98.Maintenance;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static class SdlpalBehavior
{
    static int count;
    static readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
    static void Reject(Action action, string name)
    { try { action(); } catch (Exception) { Check(true, name); return; } throw new Exception(name); }
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(value, args);
    static void Set(object value, string name, object data) => value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(value, data);
    static SdlpalSnapshot Snapshot() => new SdlpalSnapshot {
        schema = 1, profile = "geshi-easy-v1", verified = true, diagnostic = true, resourceHash = new string('a',64),
        resourceHashes = new Dictionary<string,string> { ["SSS.MKF"] = new string('a',64), ["DATA.MKF"] = new string('b',64), ["WORD.DAT"] = new string('c',64), ["M.MSG"] = new string('d',64) },
        build = "offline-test", exeHash = SdlpalBridge.HashFile(Process.GetCurrentProcess().MainModule.FileName), gamePath = root, savePath = Path.Combine(root,"save"),
        focused = true, canControl = true, epoch = 1, fadeCycleMs = 800, walkFrameMs = 90, battle = new SdlpalBattle(), battleEvents = new SdlpalBattleEvent[0],
        inventory = new SdlpalInventory[0], party = new[] {0},
        roles = Enumerable.Range(0,6).Select(id => new SdlpalItemRole { id=id, name="角色"+id, equipment=new int[6] }).ToArray(),
        catalog = Enumerable.Range(0,700).Select(id => new SdlpalCatalogEntry { id=id, name="物品"+id, data=new[]{0,id*10,0,0,0,0,0} }).ToArray(),
        itemIds = Enumerable.Range(61,234).Concat(new[]{344,567,568,569,577,583,584,585}).ToArray()
    };
    static byte[] Header(SdlpalSnapshot value, int length, int pid = 777, ulong created = 123, ulong now = 10000)
    {
        var header = new byte[64]; Encoding.ASCII.GetBytes("SDLPAL1\0").CopyTo(header,0);
        BitConverter.GetBytes(1U).CopyTo(header,8); BitConverter.GetBytes(64U).CopyTo(header,12); BitConverter.GetBytes((uint)SdlpalBridge.Capacity).CopyTo(header,16);
        BitConverter.GetBytes(2U).CopyTo(header,20); BitConverter.GetBytes(length).CopyTo(header,24); BitConverter.GetBytes(pid).CopyTo(header,28);
        BitConverter.GetBytes(created).CopyTo(header,32); BitConverter.GetBytes(value.epoch).CopyTo(header,40); BitConverter.GetBytes(now).CopyTo(header,56); return header;
    }
    static SdlpalSnapshot Clone(SdlpalSnapshot value) => SdlpalBridge.Json().Deserialize<SdlpalSnapshot>(SerializeFixture(value));
    // The native legacy wire omits rule fields. New native snapshots publish
    // the complete binding at both root and identity, never JSON nulls.
    static string SerializeFixture(object value)
    {
        var serializer=SdlpalBridge.Json();
        var state=value as SdlpalSnapshot;
        if(state==null) return serializer.Serialize(value);
        var fields=serializer.Deserialize<Dictionary<string,object>>(serializer.Serialize(state));
        string[] names={"gameplayRuleId","gameplayRuleRevision","gameplayRuleSha256"};
        if(state.gameplayRuleId==null&&state.gameplayRuleRevision==null&&state.gameplayRuleSha256==null)
            foreach(string name in names) fields.Remove(name);
        else fields["identity"]=names.ToDictionary(n=>n,n=>fields[n]);
        return serializer.Serialize(fields);
    }
    static void BindRules(SdlpalSnapshot value)
    {
        value.gameplayRuleId=value.profile.Substring(0,value.profile.Length-3)+"-rules-v1";
        value.gameplayRuleRevision=1; value.gameplayRuleSha256=new string('e',64);
    }
    static void GameplayRuleTests()
    {
        var old=Snapshot();var current=Clone(old);BindRules(current);
        Check(old.RankingFacts().Count==7&&current.RankingFacts().Count==10&&old.RankingFacts()["content"]==current.RankingFacts()["content"],
            "explicit rules partition gameplay without changing the four-resource digest or old category");
        Check(!SdlpalSnapshot.SameResourceEngineIdentity(old.IdentityKey,current.IdentityKey),"legacy and rule-bound runs cannot relay into one another");
        Func<Dictionary<string,object>> wire=()=>SdlpalBridge.Json().Deserialize<Dictionary<string,object>>(SerializeFixture(current));
        Func<Dictionary<string,object>,SdlpalSnapshot> decode=fields=>{
            byte[] bytes=Encoding.UTF8.GetBytes(SerializeFixture(fields));
            return SdlpalBridge.Decode(Header(current,bytes.Length),bytes,777,123,10000);
        };
        Check(decode(wire()).gameplayRuleSha256==current.gameplayRuleSha256,"complete matching native formula identity accepted");
        Check(SdlpalBridge.ReadSnapshotJson(SerializeFixture(current)).gameplayRuleRevision==1,"offline initialization preserves validated rules");
        foreach(string key in new[]{"gameplayRuleId","gameplayRuleRevision","gameplayRuleSha256"})
        foreach(bool nested in new[]{false,true})
        {
            var fields=wire();var target=nested?(Dictionary<string,object>)fields["identity"]:fields;
            target.Remove(key);Reject(()=>decode(fields),"formula field must exist in both layers: "+key+" / "+nested);
            Reject(()=>SdlpalBridge.ReadSnapshotJson(SerializeFixture(fields)),"offline initialization rejects partial formula binding");
        }
        var badValues=new Dictionary<string,object[]> {
            ["gameplayRuleId"]=new object[]{null,1,"", "dreamspace39x-hard-rules-v1","geshi-easy-rules-v01","geshi-easy-rules-v1|other"},
            ["gameplayRuleRevision"]=new object[]{null,"1",1.0m,true,0,-1,2147483648L},
            ["gameplayRuleSha256"]=new object[]{null,1,"",new string('F',64),new string('e',63),new string('g',64)}
        };
        foreach(var group in badValues) foreach(object value in group.Value)
        {
            var fields=wire();fields[group.Key]=value;((Dictionary<string,object>)fields["identity"])[group.Key]=value;
            Reject(()=>decode(fields),"noncanonical formula field rejected: "+group.Key+" / "+(value??"null"));
        }
        var conflict=wire();((Dictionary<string,object>)conflict["identity"])["gameplayRuleSha256"]=new string('f',64);
        Reject(()=>decode(conflict),"conflicting inner formula binding rejected");
        foreach(string key in new[]{"gameplayRuleId","gameplayRuleRevision","gameplayRuleSha256"})
        foreach(bool duplicate in new[]{false,true}) {
            var fields=wire();string alias=char.ToUpperInvariant(key[0])+key.Substring(1);fields[alias]=fields[key];
            if(!duplicate) {fields.Remove(key);fields.Remove("identity");}
            Reject(()=>decode(fields),"case alias cannot bypass formula validation: "+key+" / "+duplicate);
            Reject(()=>SdlpalBridge.ReadSnapshotJson(SerializeFixture(fields)),"offline initializer rejects formula aliases");
        }
        foreach(string duplicate in new[]{"\"gameplayRuleRevision\":2,","\"gameplayRuleRevision\":1,","\"\\u0067ameplayRuleRevision\":1,"}) {
            string json="{"+duplicate+SerializeFixture(current).Substring(1);
            byte[] bytes=Encoding.UTF8.GetBytes(json);
            Reject(()=>SdlpalBridge.Decode(Header(current,bytes.Length),bytes,777,123,10000),"duplicate wire rule key rejected before coercion");
            Reject(()=>SdlpalBridge.ReadSnapshotJson(json),"offline initializer rejects duplicate wire keys");
        }
        Func<Dictionary<string,string>,bool> valid=facts=>new RankingConfiguration {covered=true,rules=facts,configuration_id=RankingConfiguration.Digest(facts)}.Valid(
            new GameplayIdentity {family=facts["family"],content_id=facts["profile"],content_sha256=facts["content"],rules_sha256=RankingConfiguration.Digest(facts),fade_ms=800,map_speed_ticks=0,walk_frame_ms=90,speed=0,battle_speed=0});
        Check(valid(current.RankingFacts()),"complete rule-bound ranking accepted");
        foreach(string key in new[]{"gameplay.rule","gameplay.revision","gameplay.sha256"})
        {var facts=current.RankingFacts();facts.Remove(key);Check(!valid(facts),"partial rule ranking rejected: "+key);}
        foreach(string revision in new[]{"0","01","-1","2147483648"})
        {var facts=current.RankingFacts();facts["gameplay.revision"]=revision;Check(!valid(facts),"noncanonical ranking revision rejected: "+revision);}
        foreach(var change in new Action<SdlpalSnapshot>[] {s=>s.gameplayRuleId="geshi-easy-rules-v2",s=>s.gameplayRuleRevision++,s=>s.gameplayRuleSha256=new string('f',64)})
        {
            var changed=Clone(current);change(changed);
            Check(valid(changed.RankingFacts())&&RankingConfiguration.Digest(changed.RankingFacts())!=RankingConfiguration.Digest(current.RankingFacts())&&
                !SdlpalSnapshot.SameResourceEngineIdentity(current.IdentityKey,changed.IdentityKey),"rule ID, revision and bytes each isolate ranking and relay");
        }
        var faster=Clone(current);faster.speed=3;faster.dreamspaceThrowFormula=true;
        Check(RankingConfiguration.Digest(current.RankingFacts())==RankingConfiguration.Digest(faster.RankingFacts())&&
            SdlpalSnapshot.SameResourceEngineIdentity(current.IdentityKey,faster.IdentityKey),"speed and informational throw option retain existing rule-bound grouping");
        using(var run=new CompletionFixture(true))
        {
            BindRules(run.Engine.State);run.StartNewGame();string path=run.Core.ActiveBestPath;
            try {
                run.Core.SaveBest();byte[] bytes=File.ReadAllBytes(path);
                var metadata=new HFrame.ENT.HObj(run.Core.GetRStr()).GetValue<HFrame.ENT.HObj>("SdlpalProfileData");
                Check(metadata.GetValue<string>("gameplayRuleSha256")==run.Engine.State.gameplayRuleSha256,"timeline carries native formula identity");
                run.Engine.State.gameplayRuleRevision=2;run.Tick();
                Check(run.Core.ActiveBestPath!=path,"formula revision selects a separate best file");
                Reject(()=>run.Core.SaveBest(),"mixed-rule run cannot save a best time");
                run.Engine.State.gameplayRuleRevision=1;run.Tick();
                Reject(()=>run.Core.SaveBest(),"formula round trip retains mixed-run protection");
                Check(File.ReadAllBytes(path).SequenceEqual(bytes),"formula round trip preserves old best bytes");
                string timeline=run.Core.GetRStr();var imported=Call(run.Core,"ValidateImport",timeline,run.Core.CurrentSnapshot.IdentityKey);
                var snapshot=run.Core.CurrentSnapshot;run.Core.Reset();Call(run.Core,"Restore",imported,snapshot);
                Reject(()=>run.Core.SaveBest(),"import preserves mixed-rule latch");
                run.Core.Reset();run.Tick();run.Core.SaveBest();Check(File.Exists(path),"explicit reset unlocks the current formula configuration");
            } finally {File.Delete(path);}
        }
    }
    static void RankingTests()
    {
        var initial=Snapshot();
        Func<SdlpalSnapshot,string> category=s=>RankingConfiguration.Digest(s.RankingFacts());
        string original=category(initial);
        var changed=Clone(initial);changed.exeHash=new string('f',64);changed.build="another-engine-build";
        changed.gamePath="another-window-location";changed.savePath="other-saves";
        Check(original==category(changed),"online configuration survives engine update and installation paths");
        foreach(var edit in new Action<SdlpalSnapshot>[] {s=>s.fadeCycleMs++,s=>s.walkFrameMs++,s=>s.speed++,s=>s.battleSpeed++})
        {
            changed=Clone(initial);edit(changed);Check(original==category(changed),"all starting and in-run speeds share the content configuration");
            Check(initial.IdentityKey!=changed.IdentityKey&&initial.ResourceEngineIdentity==changed.ResourceEngineIdentity&&
                SdlpalSnapshot.SameResourceEngineIdentity(initial.IdentityKey,changed.IdentityKey),"legacy serialized identity accepts only speed differences for relay");
        }
        foreach(string name in initial.resourceHashes.Keys)
        {changed=Clone(initial);changed.resourceHashes[name]=new string('e',64);Check(original!=category(changed),"gameplay resource change partitions configuration: "+name);}
        var facts=initial.RankingFacts();
        foreach(var edit in new Action<SdlpalSnapshot>[] {s=>s.monsterRush=true,s=>s.invisibleMonsters=true})
        {
            changed=Clone(initial);changed.respawnRule="pal98-current-scene-v1";edit(changed);
            Check(original!=category(changed),"monster mode partitions the original ranking category");
            Check(!SdlpalSnapshot.SameResourceEngineIdentity(initial.IdentityKey,changed.IdentityKey),"relay refuses a different monster rule or mode");
            var modeFacts=changed.RankingFacts();
            var modeGame=new GameplayIdentity {family="sdlpal-geshi-easy",content_id=changed.profile,content_sha256=modeFacts["content"],
                rules_sha256=category(changed),fade_ms=changed.fadeCycleMs,map_speed_ticks=0,walk_frame_ms=changed.walkFrameMs,speed=changed.speed,battle_speed=changed.battleSpeed};
            Check(new RankingConfiguration {covered=true,rules=modeFacts,configuration_id=category(changed)}.Valid(modeGame),"known monster mode has valid isolated ranking facts");
            modeFacts["respawn_rule"]="unknown";
            Check(!new RankingConfiguration {covered=true,rules=modeFacts,configuration_id=RankingConfiguration.Digest(modeFacts)}.Valid(modeGame),"unknown respawn rule is rejected");
        }
        Check(facts.Count==7&&!facts.ContainsKey("engine_sha256")&&!facts.ContainsKey("fade_ms")&&!facts.ContainsKey("route_rules_sha256")&&!facts.ContainsKey("timer_exe_sha256"),
            "only content enters base ranking facts; speed, signatures and node routes remain independent");
        var game=new GameplayIdentity {family="sdlpal-geshi-easy",content_id=initial.profile,content_sha256=facts["content"],rules_sha256=original,fade_ms=800,map_speed_ticks=0,walk_frame_ms=90,speed=0,battle_speed=0};
        Func<bool> valid=()=>new RankingConfiguration {covered=true,rules=facts,configuration_id=RankingConfiguration.Digest(facts)}.Valid(game);
        Check(valid(),"content ranking validates");
        facts["WindowWidth"]="1920";Check(!valid(),"display setting cannot leak into accepted ranking facts");facts.Remove("WindowWidth");
        facts=initial.RankingFacts(true);
        Check(valid(),"historical speed configuration remains valid");
        facts["engine_sha256"]=initial.exeHash;Check(!valid(),"partial legacy identity is rejected");
        facts["engine_build"]=initial.build;facts["route_rules_sha256"]=new string('e',64);
        Check(valid(),"complete old configuration remains readable without rewriting its digest");
        facts["resource.DATA.MKF"]=new string('e',64);Check(!valid(),"mismatched resource aggregate rejected");
    }
    static void DreamspaceThrowTests()
    {
        var normal=Snapshot();normal.profile="dreamspace39x-hard-v1";normal.respawnRule="pal98-current-scene-v1";
        var enabled=Clone(normal);enabled.dreamspaceThrowFormula=true;
        Check(!normal.RankingFacts().ContainsKey("dreamspace_throw"),"disabled Dreamspace formula preserves r70 ranking keys");
        Check(!enabled.RankingFacts().ContainsKey("dreamspace_throw")&&
            RankingConfiguration.Digest(normal.RankingFacts())==RankingConfiguration.Digest(enabled.RankingFacts())&&
            enabled.ResourceEngineIdentity==normal.ResourceEngineIdentity,
            "Dreamspace formula changes neither score groups nor relay compatibility");
        Func<Dictionary<string,string>,bool> valid=facts=>new RankingConfiguration {
            covered=true,rules=facts,configuration_id=RankingConfiguration.Digest(facts)
        }.Valid(new GameplayIdentity {family=facts["family"],content_id=facts["profile"],content_sha256=facts["content"],
            rules_sha256=RankingConfiguration.Digest(facts),fade_ms=800,map_speed_ticks=0,walk_frame_ms=90,speed=0,battle_speed=0});
        Check(valid(normal.RankingFacts())&&valid(enabled.RankingFacts()),"both Dreamspace formulas have valid complete ranking facts");
        var bad=enabled.RankingFacts();bad["dreamspace_throw"]="lostedit-v1";
        Check(!valid(bad),"informational throw choice cannot leak into accepted ranking facts");
        Func<Dictionary<string,object>,SdlpalSnapshot> decode=fields=>{
            byte[] bytes=Encoding.UTF8.GetBytes(SerializeFixture(fields));
            return SdlpalBridge.Decode(Header(normal,bytes.Length),bytes,777,123,10000);
        };
        var fields=SdlpalBridge.Json().Deserialize<Dictionary<string,object>>(SerializeFixture(enabled));
        var identity=new Dictionary<string,object>{{"dreamspaceThrowFormula",true}};fields["identity"]=identity;
        Check(decode(fields).dreamspaceThrowFormula,"matching native Dreamspace formula accepted");
        identity["dreamspaceThrowFormula"]=false;Reject(()=>decode(fields),"conflicting nested throw formula rejected");
        identity["dreamspaceThrowFormula"]=true;
        foreach(object value in new object[]{null,1,"true"}) {
            fields["dreamspaceThrowFormula"]=value;identity["dreamspaceThrowFormula"]=value;
            Reject(()=>decode(fields),"throw formula requires actual JSON boolean");
        }
        fields.Remove("dreamspaceThrowFormula");identity["dreamspaceThrowFormula"]=true;
        Reject(()=>decode(fields),"nested-only throw formula rejected");
        fields.Remove("identity");Check(!decode(fields).dreamspaceThrowFormula,"old snapshots without the field retain classic throw identity");
        fields["dreamspaceThrowFormula"]=true;fields["profile"]="geshi-easy-v1";
        Reject(()=>decode(fields),"effective throw mode rejected on non-Dreamspace snapshots");
        fields["dreamspaceThrowFormula"]=false;Check(!decode(fields).dreamspaceThrowFormula,"false throw flag allowed on all contents");
    }
    static void DecodeTests(string[] paths)
    {
        byte[] malformed=Encoding.UTF8.GetBytes("{\"schema\":");
        try { SdlpalBridge.Decode(Header(Snapshot(),malformed.Length),malformed,777,123,10000);throw new Exception("malformed JSON accepted"); }
        catch(InvalidDataException) {Check(true,"malformed JSON becomes a controlled reader error");}
        Func<Dictionary<string,object>,SdlpalSnapshot> decodeMode=fields=>{
            byte[] bytes=Encoding.UTF8.GetBytes(SerializeFixture(fields));
            return SdlpalBridge.Decode(Header(Snapshot(),bytes.Length),bytes,777,123,10000);
        };
        foreach(string key in new[]{"monsterRush","invisibleMonsters"})
        {
            var mode=SdlpalBridge.Json().Deserialize<Dictionary<string,object>>(SerializeFixture(Snapshot()));
            mode["respawnRule"]="pal98-current-scene-v1";mode[key]=true;
            var identity=new Dictionary<string,object>{{key,true},{"respawnRule","pal98-current-scene-v1"}};mode["identity"]=identity;
            Check(decodeMode(mode).respawnRule=="pal98-current-scene-v1","matching nested monster identity accepted: "+key);
            identity[key]=false;Reject(()=>decodeMode(mode),"conflicting nested monster identity rejected: "+key);
            identity[key]=true;mode.Remove(key);Reject(()=>decodeMode(mode),"nested-only mode cannot invent runtime state: "+key);
            mode[key]="true";Reject(()=>decodeMode(mode),"monster mode does not coerce text into a boolean: "+key);
            mode[key]=true;identity["respawnRule"]="unknown";
            Reject(()=>decodeMode(mode),"nested respawn rule disagreement rejected: "+key);
        }
        var state=Snapshot(); byte[] json=Encoding.UTF8.GetBytes(SerializeFixture(state)); var header=Header(state,json.Length);
        Check(SdlpalBridge.Decode(header,json,777,123,10000).profile=="geshi-easy-v1","valid versioned snapshot");
        Reject(()=>SdlpalBridge.Decode(header,json,778,123,10000),"foreign PID rejected");
        Reject(()=>SdlpalBridge.Decode(header,json,777,124,10000),"reused PID creation time rejected");
        Reject(()=>SdlpalBridge.Decode(header,json,777,123,12001),"stale snapshot rejected");
        header[20]=3; Reject(()=>SdlpalBridge.Decode(header,json,777,123,10000),"odd seqlock rejected"); header[20]=2;
        header[8]=2; Reject(()=>SdlpalBridge.Decode(header,json,777,123,10000),"unknown protocol rejected"); header[8]=1;
        state.verified=false; json=Encoding.UTF8.GetBytes(SerializeFixture(state)); Reject(()=>SdlpalBridge.Decode(Header(state,json.Length),json,777,123,10000),"unverified resources rejected");
        state=Snapshot(); string missing=SerializeFixture(state).Replace("\"diagnostic\":true,",""); json=Encoding.UTF8.GetBytes(missing);
        Reject(()=>SdlpalBridge.Decode(Header(state,json.Length),json,777,123,10000),"missing diagnostic flag rejected");
        state=Snapshot();state.started=true;state.inventory=new[]{new SdlpalInventory{slot=0,id=61,amount=4}};
        json=Encoding.UTF8.GetBytes(SerializeFixture(state).Replace("\"amount\":4,",""));
        var optional=SdlpalBridge.Decode(Header(state,json.Length),json,777,123,10000);var unavailable=new SdlpalItemTracker();unavailable.Observe(optional);
        Check(optional.verified&&!unavailable.View.Available,"missing nested item quantity disables item data without fabricating zero or blocking timing identity");
        foreach(string path in paths)
        {
            json=File.ReadAllBytes(path); state=SdlpalBridge.Json().Deserialize<SdlpalSnapshot>(new UTF8Encoding(false,true).GetString(json));
            var actual=SdlpalBridge.Decode(Header(state,json.Length),json,777,123,10000);
            bool formulaFixture=Path.GetFileName(path)=="verification-gameplay-snapshot.json"; bool contentFixture=formulaFixture||Path.GetFileName(path)=="verification-content.json";
            string expectedProfile=formulaFixture?new DirectoryInfo(Path.GetDirectoryName(path)).Name+"-v1":contentFixture?new DirectoryInfo(Path.GetDirectoryName(path)).Parent.Name+"-v1":"geshi-easy-v1";
            Check(actual.profile==expectedProfile&&actual.diagnostic,"real native diagnostic snapshot decodes: "+expectedProfile);
            var metadata=SdlpalBridge.Json().Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(json));
            if(metadata.ContainsKey("rankingConfigurationId"))
                Check((string)metadata["rankingConfigurationId"]==RankingConfiguration.Digest(actual.RankingFacts()),
                    "native title configuration ID matches timer ranking rules: "+Path.GetFileName(path));
            var items=new SdlpalItemTracker();items.Observe(actual);
            if(contentFixture) {
                if(formulaFixture) {
                    Check(!actual.started&&!items.View.Available,"post-initialization diagnostic does not invent a running playthrough");
                    Check(actual.gameplayRuleRevision==1&&SdlpalBridge.ValidGameplayRule(actual.profile,actual.gameplayRuleId,"1",actual.gameplayRuleSha256),"actual content publishes its complete canonical formula binding");
                } else {
                    Check(items.View.Available&&items.View.Rows.Select(row=>row.Id).OrderBy(id=>id).SequenceEqual(actual.itemIds.OrderBy(id=>id)),"actual runtime catalog remains complete for "+expectedProfile);
                    Check(items.View.Rows.All(row=>row.ResourcePrice==actual.catalog.Single(c=>c.id==row.Id).data[1]),"item prices retain the selected content values");
                }
                var facts=actual.RankingFacts();
                Check(new RankingConfiguration {covered=true,rules=facts,configuration_id=RankingConfiguration.Digest(facts)}.Valid(new GameplayIdentity {family=SdlpalBridge.ProfileFamily(actual.profile),content_id=actual.profile,content_sha256=facts["content"],rules_sha256=RankingConfiguration.Digest(facts),fade_ms=actual.fadeCycleMs,map_speed_ticks=0,walk_frame_ms=actual.walkFrameMs,speed=actual.speed,battle_speed=actual.battleSpeed}),"actual content ranking validates");
                var onlineType=Assembly.LoadFrom(Path.Combine(root,"PalTimerOnline.dll")).GetType("Pal98Timer.GameplayIdentity",true);
                var onlineIdentity=SdlpalBridge.Json().GetType().GetMethods().Single(m=>m.Name=="Deserialize"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==1).MakeGenericMethod(onlineType).Invoke(SdlpalBridge.Json(),new object[]{SerializeFixture(new GameplayIdentity {family=SdlpalBridge.ProfileFamily(actual.profile),content_id=actual.profile,content_sha256=facts["content"],rules_sha256=RankingConfiguration.Digest(facts),fade_ms=actual.fadeCycleMs,map_speed_ticks=0,walk_frame_ms=actual.walkFrameMs,speed=actual.speed,battle_speed=actual.battleSpeed})});
                Check((bool)onlineType.GetProperty("Valid",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(onlineIdentity,null),"shipped online module accepts actual content identity");
                continue;
            }
            Check(items.View.Available&&items.View.Rows.Any(row=>row.Id==585)&&!items.View.Rows.Any(row=>row.Id==571||row.Id==575),"native item catalog includes modified items and excludes initial magics: "+Path.GetFileName(path));
            Check(items.View.Rows.Single(row=>row.Id==61).ResourcePrice==150&&new[]{"銅錢鏢","铜钱镖"}.Contains(items.View.Rows.Single(row=>row.Id==344).Name),"native item names/prices come from current resources");
            Check(actual.scene==227 ? items.View.Cash==31265&&items.View.Kinds==80&&items.View.Quantity==355 :
                actual.scene==93&&items.View.Cash==9817&&items.View.Kinds==107&&items.View.Quantity==458,"native save ownership/cash totals (all six equipment slots, active party only)");
        }
    }
    static void WuQiangTests()
    {
        var normal=Snapshot();var enabled=Clone(normal);enabled.wuqiang=true;
        string legacy=normal.profile+"|"+normal.exeHash+"|"+normal.build+"|"+
            string.Join("|",normal.resourceHashes.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+"="+p.Value));
        Check(normal.ResourceEngineIdentity==legacy&&normal.RankingFacts().Count==7,
            "ordinary identity and twelve ranking facts preserve historical categories");
        Check(enabled.ResourceEngineIdentity==legacy+"|wuqiang=1"&&enabled.RankingFacts()["wuqiang"]=="1"&&
            RankingConfiguration.Digest(enabled.RankingFacts())!=RankingConfiguration.Digest(normal.RankingFacts()),
            "WuQiang partitions runtime identity and ranking facts without changing resources");
        foreach(var state in new[]{normal,enabled})
        {
            var facts=state.RankingFacts();var game=new GameplayIdentity {family="sdlpal-geshi-easy",content_id=state.profile,
                content_sha256=facts["content"],rules_sha256=RankingConfiguration.Digest(facts),fade_ms=state.fadeCycleMs,
                map_speed_ticks=0,walk_frame_ms=state.walkFrameMs,speed=state.speed,battle_speed=state.battleSpeed};
            Func<bool> valid=()=>new RankingConfiguration {covered=true,rules=facts,configuration_id=RankingConfiguration.Digest(facts)}.Valid(game);
            Check(valid(),"ordinary/WuQiang ranking facts validate: "+state.wuqiang);
            foreach(string invalid in new[]{"0","true","false","01","2","",null})
            {facts["wuqiang"]=invalid;Check(!valid(),"only explicit wuqiang=1 is accepted: "+(invalid??"null"));}
            var speed=Clone(state);speed.speed++;speed.battleSpeed++;speed.walkFrameMs++;
            Check(SdlpalSnapshot.SameResourceEngineIdentity(state.IdentityKey,speed.IdentityKey),
                "same-mode relay still accepts speed changes: "+state.wuqiang);
        }
        Func<Dictionary<string,object>,SdlpalSnapshot> decode=fields=>{
            byte[] json=Encoding.UTF8.GetBytes(SerializeFixture(fields));
            return SdlpalBridge.Decode(Header(normal,json.Length),json,777,123,10000);
        };
        Func<Dictionary<string,object>> original=()=>SdlpalBridge.Json().Deserialize<Dictionary<string,object>>(SerializeFixture(normal));
        var fields=original();fields.Remove("wuqiang");Check(!decode(fields).wuqiang,"legacy missing no-wall field decodes as ordinary");
        foreach(bool mode in new[]{false,true})
        {
            fields=original();fields["wuqiang"]=mode;fields["identity"]=new Dictionary<string,object>{{"wuqiang",mode}};
            Check(decode(fields).wuqiang==mode,"flattened and nested native boolean agree: "+mode);
            fields["identity"]=new Dictionary<string,object>{{"wuqiang",!mode}};
            Reject(()=>decode(fields),"conflicting native no-wall identities are rejected");
        }
        foreach(object invalid in new object[]{null,0,1,"true","false",new object[0],new Dictionary<string,object>()})
        {
            fields=original();fields["wuqiang"]=invalid;Reject(()=>decode(fields),"non-boolean flattened no-wall state is rejected");
            fields=original();fields["identity"]=new Dictionary<string,object>{{"wuqiang",invalid}};
            Reject(()=>decode(fields),"non-boolean nested no-wall identity is rejected");
        }
        fields=original();fields.Remove("wuqiang");fields["identity"]=new Dictionary<string,object>{{"wuqiang",true}};
        Reject(()=>decode(fields),"nested no-wall identity cannot silently become legacy ordinary mode");
        byte[] save=new byte[2048];
        foreach(var origin in new[]{normal,enabled})
        {
            var package=new SdlpalRelayPackage{identity=origin.IdentityKey,timer="{}",saveBase64=Convert.ToBase64String(save),saveSha256=SdlpalRelayPackage.Hash(save)};
            Check(package.Validate(origin.IdentityKey).SequenceEqual(save),"same-mode relay package remains readable: "+origin.wuqiang);
            Reject(()=>package.Validate(origin.wuqiang?normal.IdentityKey:enabled.IdentityKey),"cross-mode relay package is rejected before control");
        }
        using(var engine=new Engine()) using(var bridge=new SdlpalBridge())
        {
            bridge.Attach(Process.GetProcessById(engine.Process.Id));SdlpalSnapshot before;bridge.TryRead(out before);
            string slot=Path.Combine(engine.State.savePath,"250.rpg");File.WriteAllBytes(slot,save);
            ulong request=engine.Control.ReadUInt64(40);engine.State.wuqiang=true;engine.Publish();
            Reject(()=>SdlpalControl.Execute(bridge,2,before,new byte[4096]),"mode change before LOAD is rejected");
            Check(engine.Control.ReadUInt64(40)==request&&File.ReadAllBytes(slot).SequenceEqual(save),
                "rejected cross-mode LOAD preserves command and reserved save bytes");
        }
        foreach(bool initial in new[]{false,true}) using(var run=new CompletionFixture(true))
        {
            run.Engine.State.wuqiang=initial;run.StartNewGame();string path=run.Core.ActiveBestPath;
            try
            {
                run.Core.SaveBest();byte[] saved=File.ReadAllBytes(path);string legacyTimer=run.Core.GetRStr();
                var reference=SdlpalBridge.Json().Deserialize<Dictionary<string,object>>(legacyTimer);reference.Remove("SdlpalWuQiangChanged");
                Call(run.Core,"ValidateImport",SerializeFixture(reference),run.Core.CurrentSnapshot.IdentityKey);
                Check(true,"older ordinary-mode change metadata remains optional for references");
                run.Engine.State.wuqiang=!initial;run.Engine.State.epoch++;run.Tick();
                Check(run.Watch.IsRunning&&!run.Core.IsUIPause&&run.Core.ActiveBestPath!=path&&run.Core.GetScoreValidationError().Length>0,
                    "restart changing no-wall mode partitions PB and preserves running clock: "+initial);
                Reject(()=>Call(run.Core,"ValidateImport",legacyTimer,run.Core.CurrentSnapshot.IdentityKey),"cross-mode timeline import is rejected");
                Reject(()=>run.Core.SaveBest(),"mixed-mode run cannot become the other mode PB");
                run.Engine.State.wuqiang=initial;run.Engine.State.epoch++;run.Tick();
                Check(run.Core.ActiveBestPath==path&&run.Watch.IsRunning,"returning to starting mode restores reference without pausing");
                Reject(()=>run.Core.SaveBest(),"returning to original mode cannot erase mixed-run PB protection");
                Check(File.ReadAllBytes(path).SequenceEqual(saved),"rejected mixed-mode PB save leaves historical reference byte-identical");
                string mixed=run.Core.GetRStr();var imported=Call(run.Core,"ValidateImport",mixed,run.Core.CurrentSnapshot.IdentityKey);
                var snapshot=run.Core.CurrentSnapshot;run.Core.Reset();Call(run.Core,"Restore",imported,snapshot);
                Reject(()=>run.Core.SaveBest(),"export and import retain mixed-mode PB protection");
                Check(run.Core.IsUIPause,"mixed reference import keeps existing pause semantics");
                run.Core.Reset();run.Tick();run.Core.SaveBest();
                Check(File.Exists(path),"explicit reset permits new local references in the selected mode");
            }
            finally {File.Delete(path);}
        }
    }
    static void ReleaseTitleTests()
    {
        var baseline=Snapshot();
        Func<Dictionary<string,object>,SdlpalSnapshot> decode=fields=>{
            byte[] json=Encoding.UTF8.GetBytes(SerializeFixture(fields));
            return SdlpalBridge.Decode(Header(baseline,json.Length),json,777,123,10000);
        };
        Func<Dictionary<string,object>> original=()=>SdlpalBridge.Json().Deserialize<Dictionary<string,object>>(SerializeFixture(baseline));
        var fields=original();fields.Remove("releaseRevision");
        Check(decode(fields).releaseRevision==null,"legacy snapshots do not invent a release revision");
        foreach(string revision in new[]{"r48","r49","r100"})
        {
            fields=original();fields["releaseRevision"]=revision;fields["identity"]=new Dictionary<string,object>{{"releaseRevision",revision}};
            var actual=decode(fields);
            Check(actual.releaseRevision==revision&&actual.IdentityKey==baseline.IdentityKey&&
                RankingConfiguration.Digest(actual.RankingFacts())==RankingConfiguration.Digest(baseline.RankingFacts()),
                "release display metadata is detected without changing ranking or relay rules: "+revision);
        }
        foreach(string revision in new[]{null,"","r0","r048","r48\n","r48 extra","R48","r1234567890"})
        {
            fields=original();fields["releaseRevision"]=revision;
            Check(decode(fields).releaseRevision==null,"invalid display label is omitted: "+(revision??"null"));
        }
        foreach(object revision in new object[]{48,true,new object[0],new Dictionary<string,object>()})
        {fields=original();fields["releaseRevision"]=revision;Reject(()=>decode(fields),"non-text release metadata is rejected");}
        fields=original();fields["releaseRevision"]="r48";fields["identity"]=new Dictionary<string,object>{{"releaseRevision","r49"}};
        Check(decode(fields).releaseRevision==null,"conflicting nested revision is not displayed");
        using(var run=new CompletionFixture(true))
        {
            run.Engine.State.releaseRevision="r48";run.Tick();
            Check(run.Core.GetGameVersion()==SdlpalGeshi.GameplayName+" r48","title-screen snapshot displays its detected release before timing starts");
            string category=run.Core.ActiveBestPath;
            run.Engine.State.releaseRevision="r49";run.Tick();
            Check(run.Core.GetGameVersion().EndsWith(" r49")&&run.Core.ActiveBestPath==category,"release label refresh leaves best-reference partition unchanged");
            run.Engine.State.releaseRevision="r48";run.Engine.State.wuqiang=true;run.Tick();
            Check(run.Core.GetGameVersion()==SdlpalGeshi.GameplayName+"（吴强） r48"&&run.Core.ReleaseSuffix==" r48",
                "actual no-wall state adds Wuqiang and release at the end");
            Check(run.Core.GetWindowTitle("测试状态"," [离线]")=="自动计时器 "+SdlpalGeshi.GameplayName+"（吴强） 测试状态 [离线] r48",
                "actual window-title formatter keeps release after integrity and online status");
            var overlay=(Dx9OverlaySnapshot)Call(run.Core,"OverlaySnapshot");
            Check(overlay.TimingModeLabel==run.Core.GetGameVersion(),"OBS gameplay label agrees with the timer title");
            run.Engine.View.Write(56,SdlpalBridge.GetTickCount64()-3000);Call(run.Core,"OnTick");
            Check(run.Core.GetGameVersion()==SdlpalGeshi.GameplayName&&run.Core.ReleaseSuffix=="","stale/disconnected snapshot removes old Wuqiang and release labels");
            run.Engine.State.releaseRevision=null;run.Engine.State.wuqiang=false;run.Tick();
            Check(run.Core.GetGameVersion()==SdlpalGeshi.GameplayName,"reconnecting an older ordinary game retains no r48 or Wuqiang label");
        }
    }
    static void ItemTests()
    {
        var s=Snapshot();s.started=true;s.cash=uint.MaxValue;s.party=new[]{0,1};
        s.inventory=new[]{new SdlpalInventory{slot=0,id=61,amount=3,inUse=1},new SdlpalInventory{slot=1,id=61,amount=2},new SdlpalInventory{slot=255,id=585,amount=1}};
        s.roles[0].equipment=new[]{61,62,0,0,0,0};s.roles[1].equipment=new[]{62,0,0,0,0,0};s.roles[5].equipment=new[]{344,0,0,0,0,0};
        var tracker=new SdlpalItemTracker();tracker.Observe(s);var v=tracker.View;
        Check(v.Available&&v.Cash==uint.MaxValue&&v.Kinds==3&&v.Quantity==9,"cash preserves uint32; duplicate stock aggregates, ownership deduplicates and absent-party gear is excluded");
        Check(v.Rows.Single(r=>r.Id==61).Backpack==5&&v.Rows.Single(r=>r.Id==61).InUse==1&&v.Rows.Single(r=>r.Id==61).Total==6,"battle reservation remains owned stock, not a second copy or a consumed item");
        Check(v.Rows.Single(r=>r.Id==62).Equipped==2&&v.Rows.All(r=>r.NetChange==0),"multiple equipped copies are quantities, first attach creates no gains");
        s.inventory[0].amount++;tracker.Observe(s);Check(tracker.View.Rows.Single(r=>r.Id==61).NetChange==1,"observed increase changes current baseline total");
        s.inventory[0].amount--;s.roles[0].equipment[0]=0;s.inventory[0].amount++;tracker.Observe(s);
        Check(tracker.View.Rows.Single(r=>r.Id==61).NetChange==0,"moving equipment into inventory is not a gain");
        s.inventory=s.inventory.Concat(new[]{new SdlpalInventory{slot=2,id=600,amount=1}}).ToArray();tracker.Observe(s);
        Check(tracker.View.Rows.Single(r=>r.Id==600).NetChange==1,"observed modified item outside audited catalog is still monitored");
        s.speed=5;s.battleSpeed=7;s.walkFrameMs=80;s.fadeCycleMs=700;tracker.Observe(s);
        Check(tracker.View.Rows.Single(r=>r.Id==600).NetChange==1,"normal speed adjustment preserves observed item gains");
        s.inventory=s.inventory.Where(i=>i.id!=600).ToArray();tracker.Observe(s);
        Check(tracker.View.Rows.Single(r=>r.Id==600).Total==0,"a newly seen item stays visible after losing the last copy");
        s.epoch++;s.cash=9817;s.inventory[0].amount=99;tracker.Observe(s);
        Check(tracker.View.Rows.All(r=>r.NetChange==0)&&tracker.View.Cash==9817,"load epoch rebuilds item baseline without manufacturing gains");
        tracker.Unavailable("lost");Check(!tracker.View.Available,"disconnect marks last table unavailable");
        s.inventory[0].amount=1;tracker.Observe(s);Check(tracker.View.Rows.All(r=>r.NetChange==0),"reconnect establishes a new baseline");
        tracker.Reset();tracker.Observe(s);Check(tracker.View.Rows.All(r=>r.NetChange==0),"timer/manual baseline reset creates no false item event");
        s.Pid=2;s.inventory[0].amount=50;tracker.Observe(s);Check(tracker.View.Rows.All(r=>r.NetChange==0),"replacement game process creates a new baseline");
        s.inventory[0].inUse=51;tracker.Observe(s);Check(!tracker.View.Available&&tracker.View.Status.Contains("背包"),"malformed stock is unavailable, never silently zero");s.inventory[0].inUse=0;
        s.roles[1].id=0;tracker.Observe(s);Check(!tracker.View.Available,"duplicate role data is rejected");s.roles[1].id=1;
        s.catalog[61].data=new[]{1,2};tracker.Observe(s);Check(!tracker.View.Available,"malformed object union cannot supply price or count");s.catalog[61].data=new int[7];
        s.itemIds=null;tracker.Observe(s);Check(!tracker.View.Available,"missing audited catalog does not guess item types from union flags");
        Reject(()=>new SdlpalItemSettings{WatchedIds=new[]{61,61}}.Validate(),"duplicate configured watches rejected");
        Reject(()=>new SdlpalItemSettings{WatchedIds=new[]{700}}.Validate(),"out-of-range configured watch rejected");
    }

    sealed class ItemCloudClient : ICloudStepClient
    {
        internal readonly ManualResetEvent Received=new ManualResetEvent(false);
        internal CloudPayload Last;
        public bool Initialized=>true;public int NextDataKind=>2;
        public CloudStepResult Step(CloudPayload payload,bool finish) {Last=payload;Received.Set();return new CloudStepResult{Initialized=true,Id=42};}
        public void Upload(string local,string remote){throw new Exception("Unexpected upload");}
        public void Download(string remote,string local){throw new Exception("Unexpected download");}
        public void Stop(){}
    }
    sealed class RenderHost : System.Windows.Forms.Form
    {
        protected override bool ShowWithoutActivation=>true;
        internal RenderHost() {ShowInTaskbar=false;StartPosition=System.Windows.Forms.FormStartPosition.Manual;Location=new System.Drawing.Point(-20000,-20000);}
    }
    static void ItemCoreTests(bool showUi)
    {
        using(var engine=new Engine())
        {
            var form=(GForm)FormatterServices.GetUninitializedObject(typeof(GForm));
            using(var controlState=new System.Windows.Forms.Form())
            {
                var window=typeof(System.Windows.Forms.Control).GetField("window",BindingFlags.NonPublic|BindingFlags.Instance);window.SetValue(form,window.GetValue(controlState));
                var core=new SdlpalGeshi(form);Call(core,"InitCheckPoints");core.LoadPlugins();
                var bridge=new SdlpalBridge();bridge.Attach(Process.GetProcessById(engine.Process.Id));Set(core,"bridge",bridge);
                engine.State.diagnostic=false;engine.Publish();Call(core,"OnTick");engine.State.started=true;engine.State.cash=12345;engine.State.inventory=new[]{new SdlpalInventory{slot=0,id=585,amount=4}};
                engine.State.catalog[585].name="音乐播放器";engine.Publish();Call(core,"OnTick");
                var config=core.ItemSettings;config.WatchedIds=new[]{585,61};core.ApplyItemSettings(config);
                Check(core.GetPluginResult(TimerPluginBase.TimerPlugin.EPluginPosition.BR)=="钱12345 道具1种 / 4件","production OnTick publishes SDL Fujia through real plugin slot");
                Check(core.GetPluginResult(TimerPluginBase.TimerPlugin.EPluginPosition.BL).Contains("音乐播放器×4"),"production watch output uses live modified names and counts");
                engine.State.catalog[585].name="音樂播放器";engine.Publish();Call(core,"OnTick");
                Check(core.GetPluginResult(TimerPluginBase.TimerPlugin.EPluginPosition.BL).Contains("音乐播放器×4"),"simplified mode converts native traditional item names");
                var oldCulture=Thread.CurrentThread.CurrentCulture;var oldUiCulture=Thread.CurrentThread.CurrentUICulture;
                try { foreach(string culture in new[]{"zh-CN","zh-TW","zh-HK"}) {
                    Thread.CurrentThread.CurrentCulture=new System.Globalization.CultureInfo(culture);
                    Thread.CurrentThread.CurrentUICulture=new System.Globalization.CultureInfo(culture);
                    Call(core,"PublishItemPlugins");
                    Check(core.GetPluginResult(TimerPluginBase.TimerPlugin.EPluginPosition.BL).Contains("音乐播放器×4") &&
                        ((Dx9OverlaySnapshot)Call(core,"OverlaySnapshot")).FooterText.Contains("音乐播放器") &&
                        (string)Call(core,"T","隔世追兇 銅錢鏢")=="隔世追凶 铜钱镖",culture+" keeps dedicated recognition and OBS simplified");
                } } finally {Thread.CurrentThread.CurrentCulture=oldCulture;Thread.CurrentThread.CurrentUICulture=oldUiCulture;}
                var overlay=(Dx9OverlaySnapshot)Call(core,"OverlaySnapshot");
                Check(overlay.FooterText.Contains("钱12345")&&overlay.FooterText.Contains("音乐播放器×4"),"OBS wrapping footer includes both Fujia and selected item output");
                Check(overlay.TimelineFirst.HasValue&&overlay.TimelineSecond.HasValue&&!overlay.TimelineThird.HasValue&&overlay.IdentityLine.Contains("云ID:")&&overlay.IdentityLine.Contains("连接ID:"),"dedicated OBS reserves third timeline position for cloud and connection identities");
                Check(core.GetGameVersion()=="隔世追凶-简单"&&overlay.TimingModeLabel=="隔世追凶-简单"&&!overlay.State.Contains("SDLPAL")&&!overlay.FooterText.Contains("SDLPAL"),"timer and overlay display gameplay name without engine branding");
                core.Jump(0);
                var payload=GForm.BuildCloudPayload(core,false,2);
                Check(payload.SuppressScores&&payload.Lite==""&&payload.Big==""&&payload.Plugins["BR"].Contains("12345")&&payload.Plugins["BL"].Contains("音乐播放器×4"),"cloud preparation carries assisted-run item display without enabling its old score stream");
                engine.State.diagnostic=true;engine.Publish();Call(core,"OnTick");var diagnostic=GForm.BuildCloudPayload(core,false,2);
                Check(diagnostic.SuppressScores&&diagnostic.Plugins.Values.All(text=>text=="")&&!core.CompetitionPublicationAllowed,"diagnostic snapshots never send synthetic plugin values or scores to cloud");
                engine.State.diagnostic=false;engine.Publish();Call(core,"OnTick");
                var fake=new ItemCloudClient();using(var runner=new CloudSessionRunner(_=>fake,10))
                {
                    runner.Start(core.CoreName,()=>true,_=>GForm.BuildCloudPayload(core,false,2),(session,state)=>{});
                    Check(fake.Received.WaitOne(3000)&&fake.Last.Plugins["BR"]==payload.Plugins["BR"]&&fake.Last.SuppressScores,"real CloudSessionRunner passes SDL plugin values to offline transport");
                }
                engine.State.inventory[0].amount=6;engine.Publish();Call(core,"OnTick");
                Check(core.GetPluginResult(TimerPluginBase.TimerPlugin.EPluginPosition.BL).Contains("×6(+2)"),"live MMF update advances selected-item net change");
                core.RebaseItems();Check(!core.GetPluginResult(TimerPluginBase.TimerPlugin.EPluginPosition.BL).Contains("(+2)"),"manual observation baseline does not reset timer");
                config=core.ItemSettings;config.OnlyOwned=false;config.FujiaEnabled=false;config.WatchEnabled=false;core.ApplyItemSettings(config);
                payload=GForm.BuildCloudPayload(core,false,2);overlay=(Dx9OverlaySnapshot)Call(core,"OverlaySnapshot");
                Check(core.GetPluginResult(TimerPluginBase.TimerPlugin.EPluginPosition.BR)==""&&payload.Plugins["BR"]==""&&payload.Plugins["BL"]==""&&!overlay.FooterText.Contains("音乐播放器"),"disabling plugins explicitly clears main/OBS/cloud outputs");
                var persisted=SdlpalItemSettings.Load();
                Check(!persisted.FujiaEnabled&&!persisted.WatchEnabled&&!persisted.OnlyOwned&&persisted.WatchedIds.SequenceEqual(new[]{585,61})&&File.Exists(TimerUserSettings.GetPath(SdlpalItemSettings.FileName)+".previous"),"watch settings persist with previous configuration backup");
                config.FujiaEnabled=true;config.WatchEnabled=true;core.ApplyItemSettings(config);
                using(var manager=new PluginMgrForm(()=>core))
                {
                    var table=(System.Windows.Forms.DataGridView)typeof(PluginMgrForm).GetField("dvPlugins",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(manager);
                    Check(table.Rows.Count==2&&Convert.ToString(table.Rows[0].Cells[2].Value)==SdlpalGeshi.GameplayName,"plugin manager lists dedicated built-in Fujia and item display");
                    table.Rows[0].Cells[0].Value=false;Call(manager,"dvPlugins_CellContentClick",table,new System.Windows.Forms.DataGridViewCellEventArgs(0,0));
                    Check(!core.ItemSettings.FujiaEnabled&&core.GetPluginResult(TimerPluginBase.TimerPlugin.EPluginPosition.BR)=="","plugin manager checkbox updates actual SDL plugin immediately");
                }
                core.ApplyItemSettings(config);
                using(var dialog=new SdlpalItemsForm(()=>core.ItemView,()=>core.ItemSettings,core.ApplyItemSettings,core.RebaseItems,text=>(string)Call(core,"T",text)))
                {
                    var grid=(System.Windows.Forms.DataGridView)typeof(SdlpalItemsForm).GetField("grid",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialog);
                    var music=grid.Rows.Cast<System.Windows.Forms.DataGridViewRow>().Single(row=>(int)row.Tag==585);
                    Check(grid.Rows.Count==242&&(string)music.Cells[2].Value=="音乐播放器"&&(int)music.Cells[6].Value==6,"real item window displays simplified audited catalog and live modified item quantities");
                    music.Cells[0].Value=false;
                    Check(!core.ItemSettings.WatchedIds.Contains(585),"real checkbox operation persists watch removal");
                if(showUi) using(var host=new RenderHost())
                    {
                        dialog.TopLevel=false;host.ClientSize=dialog.Size;host.Controls.Add(dialog);dialog.Show();host.Show();System.Windows.Forms.Application.DoEvents();
                        using(var bitmap=new System.Drawing.Bitmap(dialog.Width,dialog.Height)) {dialog.DrawToBitmap(bitmap,new System.Drawing.Rectangle(System.Drawing.Point.Empty,dialog.Size));bitmap.Save(Path.Combine(root,"items-window.png"));}
                        var only=(System.Windows.Forms.CheckBox)typeof(SdlpalItemsForm).GetField("onlyWatched",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialog);
                        only.Checked=true;System.Windows.Forms.Application.DoEvents();
                        Check(grid.Rows.Count==1&&(int)grid.Rows[0].Tag==61,"only-watched filter keeps selected zero-count item visible");
                        grid.Rows[0].Cells[0].Value=false;System.Windows.Forms.Application.DoEvents();
                        Check(grid.Rows.Count==0&&core.ItemSettings.WatchedIds.Length==0,"removing last watched row commits safely before filtering it away");
                        host.Hide();
                    }
                if(!showUi) Console.WriteLine("SKIP item window rendering and bound filtering (--no-gui)");
                }
                config.WatchedIds=new[]{585};core.ApplyItemSettings(config);
                engine.View.Write(56,SdlpalBridge.GetTickCount64()-3000);Call(core,"OnTick");payload=GForm.BuildCloudPayload(core,false,2);
                Check(!core.ItemView.Available&&!core.GetMoreInfo().Contains("12345")&&payload.Plugins["BR"].Contains("不可用"),"stale bridge clears cash and sends unavailable plugin state");
                engine.State.epoch++;engine.State.inventory[0].amount=50;engine.Publish();Call(core,"OnTick");
                Check(core.ItemView.Available&&core.ItemView.Rows.All(row=>row.NetChange==0),"production core rebuilds item baseline after reconnect/load");
                core.Reset();Check(!core.ItemView.Available&&core.ItemSettings.WatchedIds.SequenceEqual(new[]{585}),"reset clears observed items but preserves user watches");
                core.Unload();core.UnloadPlugins();
                Reject(()=>core.ApplyItemSettings(config),"closed/core-switched plugin configuration cannot modify new session");
                new SdlpalItemSettings().Save();
            }
        }
    }
    static SdlpalBattleEvent Victory(ulong sequence, ulong epoch=1, int result=3) => new SdlpalBattleEvent { seq=sequence,epoch=epoch,team=315,result=result,enemies=new[]{547},scene=10 };
    static void ObserverTests()
    {
        var rules=SdlpalSplitRule.Defaults(); SdlpalSplitRule.Validate(rules);
        var renamed=SdlpalSplitRule.Defaults();renamed[0].Name="八頭蛇勝利";
        Check(SdlpalSplitRule.Identity(rules)==SdlpalSplitRule.Identity(renamed),"split display names do not partition ranking identity");
        var run=new SdlpalRunObserver();var state=Snapshot();run.Observe(Clone(state),rules);state.started=true;run.Observe(Clone(state),rules);
        Check(run.Started&&run.BeganHere&&run.Issue=="","new-game start is observed without claiming a late attach");
        foreach(int result in new[]{1,32767,65535,0}) {state.battleEvents=new[]{Victory((ulong)result+100,1,result)}; var other=new SdlpalRunObserver();other.Observe(Snapshot(),rules);other.Observe(Clone(state),rules);Check(!other.Reached.Contains(rules[0].Id),"non-victory cannot split: "+result);}
        state.battleEvents=new[]{Victory(1)};run.Observe(Clone(state),rules);Check(run.Reached.Contains(rules[0].Id),"boss victory from ring advances its rule");
        state.completed=true;run.Observe(Clone(state),rules);Check(run.Reached.Contains(rules[1].Id),"real completed latch advances ending");
        state.epoch=2;state.completed=false;state.battleEvents=new SdlpalBattleEvent[0];run.Observe(Clone(state),rules);
        Check(run.EpochChanged&&!run.Reached.Any()&&run.Issue!="","load epoch clears pending victories and marks run");
        foreach(string profile in new[]{"geshi-easy-v1","dream222-normal-v1","dream222-hard-v1","dreamspace39x-hard-v1","dream22-puppet100-v1"})
        foreach(int slot in new[]{1,5,100})
        {
            var loading=new SdlpalRunObserver();var before=Snapshot();before.profile=profile;
            loading.Observe(Clone(before),rules);before.started=true;loading.Observe(Clone(before),rules);
            before.battleEvents=new[]{Victory(1)};loading.Observe(Clone(before),rules);
            var reached=loading.Reached.ToArray();
            before.epoch++;before.loadedFrom=slot;before.battleEvents=new SdlpalBattleEvent[0];loading.Observe(Clone(before),rules);
            Check(loading.BeganHere&&loading.Issue==""&&loading.Reached.SetEquals(reached),"ordinary and quick load preserve run origin and observed checkpoints: "+profile+" / "+slot);
            before.battleEvents=new[]{Victory(1)};loading.Observe(Clone(before),rules);
            Check(loading.Issue==""&&loading.Reached.SetEquals(reached),"rebased battle sequence neither duplicates a checkpoint nor creates a gap after load");
        }
        run=new SdlpalRunObserver();state=Snapshot();state.started=true;state.battleEvents=new[]{Victory(1)};run.Observe(Clone(state),rules);
        Check(!run.BeganHere&&!run.Reached.Contains(rules[0].Id),"late attach cannot consume historical victory");
        state.battleEvents=new[]{Victory(3)};run.Observe(Clone(state),rules);Check(run.Issue.Contains("缺口")&&!run.Reached.Contains(rules[0].Id),"ring gap reports and rebuilds without inventing split");
        state.walkFrameMs++;run.Observe(Clone(state),rules);Check(run.Issue.Contains("缺口"),"speed change does not replace existing event-gap evidence");
        foreach(var edit in new Action<SdlpalSnapshot>[] {s=>s.build="another-build",s=>s.exeHash=new string('f',64),
            s=>s.resourceHashes["SSS.MKF"]=new string('e',64),s=>s.resourceHashes["DATA.MKF"]=new string('e',64),
            s=>s.resourceHashes["WORD.DAT"]=new string('e',64),s=>s.resourceHashes["M.MSG"]=new string('e',64)})
        {
            run=new SdlpalRunObserver();state=Snapshot();run.Observe(Clone(state),rules);state.started=true;run.Observe(Clone(state),rules);
            var changed=Clone(state);edit(changed);run.Observe(changed,rules);
            Check(run.Issue=="计时期间资源或引擎配置变化","engine/build/each resource change still invalidates the run");
            run.Observe(Clone(state),rules);Check(run.Issue.Length>0,"restoring the original identity cannot erase the observed change");
            Check(!SdlpalSnapshot.SameResourceEngineIdentity(state.IdentityKey,changed.IdentityKey),"relay still rejects engine/build/resource changes");
        }
        foreach(string invalid in new[]{"",null,"wrong profile",Snapshot().IdentityKey+"|0",Snapshot().IdentityKey.Substring(0,Snapshot().IdentityKey.LastIndexOf('|'))+"|101"})
            Check(!SdlpalSnapshot.SameResourceEngineIdentity(Snapshot().IdentityKey,invalid),"malformed legacy identity cannot bypass relay validation");
        Reject(()=>SdlpalSplitRule.Validate(new[]{rules[0],rules[0],rules[1]}),"duplicate node IDs rejected");
        Reject(()=>SdlpalSplitRule.Validate(new[]{rules[1],rules[0]}),"completion must be final");
        var repeated=new[]{rules[0],new SdlpalSplitRule{Id="second-serpent",Name="第二次八头蛇",Kind="battle",Team=315,Enemy=547},rules[1]};
        run=new SdlpalRunObserver();state=Snapshot();run.Observe(Clone(state),repeated);state.started=true;run.Observe(Clone(state),repeated);
        state.battleEvents=new[]{Victory(1)};run.Observe(Clone(state),repeated);run.Observe(Clone(state),repeated);
        Check(run.Reached.Contains(rules[0].Id)&&!run.Reached.Contains("second-serpent"),"one victory cannot complete multiple repeated-boss rules");
        state.battleEvents=new[]{Victory(1),Victory(2)};run.Observe(Clone(state),repeated);Check(run.Reached.Contains("second-serpent"),"second victory completes next repeated-boss rule");
    }
    sealed class Engine : IDisposable
    {
        readonly MemoryMappedFile map,control;
        internal readonly MemoryMappedViewAccessor View,Control;
        internal SdlpalSnapshot State=Snapshot();
        internal readonly Process Process=Process.GetCurrentProcess();
        internal Engine()
        {
            Directory.CreateDirectory(State.savePath);
            map=MemoryMappedFile.CreateNew(SdlpalBridge.Prefix+Process.Id,SdlpalBridge.Capacity);View=map.CreateViewAccessor();
            control=MemoryMappedFile.CreateNew("Local\\SDLPAL.LostEdit.Control.v1."+Process.Id,128);Control=control.CreateViewAccessor();
            byte[] magic=Encoding.ASCII.GetBytes("SDLCMD1\0");Control.WriteArray(0,magic,0,8);Control.Write(8,1U);Control.Write(12,128U);Control.Write(16,(uint)Process.Id);Control.Write(24,(ulong)Process.StartTime.ToUniversalTime().ToFileTimeUtc());Publish();
        }
        internal void Publish()
        {
            byte[] json=Encoding.UTF8.GetBytes(SerializeFixture(State));byte[] header=Header(State,json.Length,Process.Id,(ulong)Process.StartTime.ToUniversalTime().ToFileTimeUtc(),SdlpalBridge.GetTickCount64());
            uint seq=View.ReadUInt32(20)+2;View.Write(20,seq|1);BitConverter.GetBytes(seq|1).CopyTo(header,20);View.WriteArray(0,header,0,header.Length);View.WriteArray(64,json,0,json.Length);Thread.MemoryBarrier();View.Write(20,seq&~1U);
        }
        internal Task Reply(uint expectedCommand, uint status=1, byte[] expectedLoad=null, Action<SdlpalSnapshot> beforeAck=null)
        {
            ulong previous=Control.ReadUInt64(40);
            return Task.Run(()=>{
                var wait=Stopwatch.StartNew();ulong id=previous;
                while(wait.ElapsedMilliseconds<3000) {if((Control.ReadUInt32(32)&1)==0&&(id=Control.ReadUInt64(40))!=previous&&id!=0)break;Thread.Sleep(5);}
                if(id==previous)throw new Exception("no relay request");
                Check(Control.ReadUInt32(36)==expectedCommand,"limited command reaches control mapping");
                string file=Path.Combine(State.savePath,"250.rpg");
                if(status==1)
                {
                    if(expectedCommand==1)File.WriteAllBytes(file,Enumerable.Range(0,2048).Select(i=>(byte)i).ToArray());
                    else {Check(File.ReadAllBytes(file).SequenceEqual(expectedLoad),"LOAD stages exact bytes in fixed slot");State.epoch++;State.canControl=false;}
                    beforeAck?.Invoke(State);Publish();
                }
                Control.Write(72,status);Control.Write(80,State.epoch);Thread.MemoryBarrier();Control.Write(64,id);
            });
        }
        public void Dispose(){View.Dispose();Control.Dispose();map.Dispose();control.Dispose();Process.Dispose();}
    }
    static void IntegrationTests()
    {
        using(var engine=new Engine()) using(var bridge=new SdlpalBridge())
        {
            bridge.Attach(Process.GetProcessById(engine.Process.Id));SdlpalSnapshot snapshot;
            Check(bridge.TryRead(out snapshot)&&snapshot.diagnostic,"actual read-only MMF path attaches to live owner");
            engine.View.Write(20,3U);Check(!bridge.TryRead(out snapshot),"reader does not accept a torn publication");engine.Publish();
            engine.View.Write(56,SdlpalBridge.GetTickCount64()-3000);Check(!bridge.TryRead(out snapshot),"reader stops on stale MMF");engine.Publish();bridge.TryRead(out snapshot);
            var saveReply=engine.Reply(1);var saved=SdlpalControl.Execute(bridge,1,snapshot);saveReply.GetAwaiter().GetResult();
            Check(saved.SavedBytes.Length==2048,"SAVE reads closed fixed-slot file after ack");
            var pack=new SdlpalRelayPackage{identity=snapshot.IdentityKey,timer="{}",saveBase64=Convert.ToBase64String(saved.SavedBytes),saveSha256=SdlpalRelayPackage.Hash(saved.SavedBytes)};
            Check(pack.Validate(snapshot.IdentityKey).SequenceEqual(saved.SavedBytes),"bounded relay JSON preserves RPG bytes");
            var anotherSpeed=Clone(snapshot);anotherSpeed.speed=5;anotherSpeed.battleSpeed=7;anotherSpeed.walkFrameMs=80;anotherSpeed.fadeCycleMs=700;
            Check(pack.Validate(anotherSpeed.IdentityKey).SequenceEqual(saved.SavedBytes),"existing v1 relay accepts different current speeds without changing RPG bytes");
            Reject(()=>pack.Validate("wrong profile"),"relay wrong engine/resource identity rejected");
            pack.saveSha256=new string('f',64);Reject(()=>pack.Validate(snapshot.IdentityKey),"relay corrupt hash rejected");
            byte[] load=Enumerable.Repeat((byte)42,2048).ToArray();var loadReply=engine.Reply(2,1,load);var loaded=SdlpalControl.Execute(bridge,2,snapshot,load);loadReply.GetAwaiter().GetResult();
            Check(loaded.Snapshot.epoch==2&&!loaded.Snapshot.canControl,"LOAD accepts matching ack during fade without requiring canControl");
            Check(Directory.GetFiles(engine.State.savePath,"250.rpg.backup-*").Length==1,"staging preserves prior reserved-slot file backup");
            Reject(()=>SdlpalControl.Execute(bridge,2,snapshot,load),"outdated epoch cannot issue LOAD");
            engine.State.canControl=true;engine.Publish();bridge.TryRead(out snapshot);
            engine.State.speed=5;engine.State.walkFrameMs=80;engine.Publish();
            var speedReply=engine.Reply(1,1,null,s=>{s.battleSpeed=7;s.fadeCycleMs=700;});
            var speedSaved=SdlpalControl.Execute(bridge,1,snapshot);speedReply.GetAwaiter().GetResult();
            Check(speedSaved.Snapshot.speed==5&&speedSaved.Snapshot.battleSpeed==7&&speedSaved.Snapshot.fadeCycleMs==700,
                "relay transaction accepts speed changes before command and acknowledgement");
            var foreign=Clone(speedSaved.Snapshot);foreign.build="foreign-engine";
            ulong request=engine.Control.ReadUInt64(40);
            Reject(()=>SdlpalControl.Execute(bridge,1,foreign),"relay transaction rejects changed engine before issuing a command");
            Check(engine.Control.ReadUInt64(40)==request,"rejected identity never writes a relay request");
            engine.State.canControl=true;engine.Publish();bridge.TryRead(out snapshot);var rejected=engine.Reply(1,2);
            Reject(()=>SdlpalControl.Execute(bridge,1,snapshot),"busy response does not claim save success");rejected.GetAwaiter().GetResult();
        }
        using(var engine=new Engine())
        {
            var form=(GForm)FormatterServices.GetUninitializedObject(typeof(GForm));form.IsNonSequentialCheck=false;form.IsShowPSInDots=false;
            using (var controlState = new System.Windows.Forms.Form())
            {
                var window=typeof(System.Windows.Forms.Control).GetField("window",BindingFlags.NonPublic|BindingFlags.Instance);
                window.SetValue(form,window.GetValue(controlState));
            var core=new SdlpalGeshi(form);Call(core,"InitCheckPoints");var bridge=new SdlpalBridge();bridge.Attach(Process.GetProcessById(engine.Process.Id));Set(core,"bridge",bridge);
            form.rr=new GRender(controlState);
            foreach(var point in core.CheckPoints)point.SetUIItem(form.rr.AddItem(point.GetNickName(),point.Best));
            var keyConfig=core.CreateKeyConfigStartInfo();
            string executableDirectory=Path.GetDirectoryName(engine.Process.MainModule.FileName);
            Check(keyConfig.WorkingDirectory==executableDirectory && keyConfig.FileName==Path.Combine(executableDirectory,"我的 配置.exe") &&
                keyConfig.WorkingDirectory!=engine.State.gamePath,"F11 locates key configuration beside the verified executable, independently of resources");
            Check(keyConfig.Arguments.Contains("--game-exe "+MaintenanceExecutable.Quote(engine.Process.MainModule.FileName)),
                "F11 passes the actual game executable and supports renamed tools");
            Call(core,"OnTick");engine.State.started=true;engine.Publish();Call(core,"OnTick");Thread.Sleep(70);
            Check(core.GetMainWatch().TotalMilliseconds>=50,"production core clock starts on new game");
            core.IsUIPause=true;Call(core,"OnTick");var paused=core.GetMainWatch();Thread.Sleep(50);Call(core,"OnTick");
            Check(core.GetMainWatch()==paused,"F9 UI pause freezes clock");
            core.IsUIPause=false;engine.State.focused=false;engine.Publish();Call(core,"OnTick");paused=core.GetMainWatch();Thread.Sleep(40);Call(core,"OnTick");Check(core.GetMainWatch()==paused,"focus loss freezes clock");
            engine.State.focused=true;engine.Publish();Call(core,"OnTick");Thread.Sleep(40);
            engine.State.battleEvents=new[]{Victory(1)};engine.Publish();Call(core,"OnTick");Check(core.CurrentStep==1&&core.CheckPoints[0].Status==CheckPointStatus.Completed,"production core advances boss node after real ring victory");
            string exported=core.GetRStr();Check(exported.Contains("SdlpalIdentity")&&exported.Contains("SdlpalStatuses"),"timeline export includes runtime identity and statuses");
            var imported=Call(core,"ValidateImport",exported,core.CurrentSnapshot.IdentityKey);Call(core,"Restore",imported,core.CurrentSnapshot);
            Check(core.IsUIPause&&core.CurrentStep==1&&core.GetScoreValidationError().Length>0,"timeline import restores progress paused and unverified");
            foreach(var status in new[]{CheckPointStatus.Completed,CheckPointStatus.ManualSkipped,CheckPointStatus.AutoSkipped})
            {
                var timeline=SdlpalBridge.Json().Deserialize<Dictionary<string,object>>(exported);
                timeline["Current"]="00:01:23.456";
                timeline["CheckPoints"]=core.CheckPoints.Select((point,index)=>new {name=point.Name,time=index==0?"00:00:20.123":"00:00:00.000"}).ToArray();
                timeline["SdlpalStatuses"]=new[]{status.ToString(),CheckPointStatus.InProgress.ToString()};
                core.Jump(0);
                core.CheckPoints[0].SetCurrentTSForLoad(TimeSpan.FromSeconds(3));
                imported=Call(core,"ValidateImport",SerializeFixture(timeline),core.CurrentSnapshot.IdentityKey);
                Call(core,"Restore",imported,core.CurrentSnapshot);
                Check(core.CheckPoints[0].Status==status&&form.rr.GetItem(0).Status==status&&
                    form.rr.GetItem(0).Cur==core.CheckPoints[0].Current&&form.rr.GetItem(1).Status==CheckPointStatus.InProgress&&core.IsUIPause,
                    "import from an earlier UI node preserves displayed status/time and pause: "+status);
                Check(core.GetMainWatch()==TimeSpan.FromMilliseconds(83456)&&core.CheckPoints[0].Current==TimeSpan.FromMilliseconds(20123)&&
                    core.GetPointSpan()=="[0~1] 0:20.12","import recalculates the displayed interval from restored time: "+status);
            }
            Check(!core.CompetitionPublicationAllowed&&core.GetScoreValidationError().Contains("诊断"),"diagnostic runtime cannot publish competition observations");
            var observation=new CompetitionObservation();core.CaptureCompetitionGameplay(observation);
            Check(observation.Gameplay.Valid&&observation.Ranking.Valid(observation.Gameplay)&&observation.Gameplay.map_speed_ticks==0&&observation.Gameplay.walk_frame_ms==90,"independent SDL family uses milliseconds without PALDLL ticks");
            Check(observation.Ranking.rules.Count==7&&!observation.Ranking.rules.ContainsKey("engine_sha256"),"actual core publishes content-only category with truthful speed evidence");
            Set(core,"relayRun",core.ScoreRunSequence);Set(core,"relayBridge",bridge);Call(core,"CheckRelayOwner");
            core.SetTS(TimeSpan.FromSeconds(1));Reject(()=>Call(core,"CheckRelayOwner"),"manual time/reset cancels an earlier relay transaction lease");
            Check(!core.CanBeginCompetitionHere(0),"manual time change cannot claim a fresh competition start");
            Set(core,"relayRun",core.ScoreRunSequence);core.Jump(0);Check(!core.CanBeginCompetitionHere(0),"manual node rewind remains assisted");
            Reject(()=>Call(core,"CheckRelayOwner"),"manual node jump invalidates an earlier relay transaction lease");
            string old=core.ActiveBestPath;engine.State.walkFrameMs=80;engine.Publish();Call(core,"OnTick");Check(old==core.ActiveBestPath,"speed changes preserve this run's local best line");
            core.Reset();Check(core.GetMainWatch()==TimeSpan.Zero&&!core.IsUIPause,"reset clears clock and pause");core.Unload();
            }
        }
    }
    static void IsolationTests()
    {
        Check(SdlpalBridge.Prefix.StartsWith("Local\\SDLPAL.GeshiFixture.",StringComparison.Ordinal),
            "synthetic host refuses the production discovery namespace");
        using(var engine=new Engine())
            foreach(var suffix in new[]{"v1.","Control.v1."})
            {
                // Split the literal so the runner cannot rewrite this assertion.
                string productionName="Local\\"+"SDLPAL."+"LostEdit."+suffix+engine.Process.Id;
                bool absent=false;
                try {using(var unexpected=MemoryMappedFile.OpenExisting(productionName,MemoryMappedFileRights.Read)) { }}
                catch(FileNotFoundException) {absent=true;}
                Check(absent,"fixture exposes no production mapping: "+suffix);
            }
    }
    sealed class CompletionFixture : IDisposable
    {
        internal readonly Engine Engine=new Engine();
        readonly System.Windows.Forms.Form controlState=new System.Windows.Forms.Form();
        internal readonly SdlpalGeshi Core;
        internal readonly GForm Form;
        internal readonly PTimer Watch;
        internal CompletionFixture(bool nonSequential)
        {
            // Bypass GForm startup, networking and hotkeys. The donated window
            // is never shown; true models GForm's default nonsequential mode.
            var form=(GForm)FormatterServices.GetUninitializedObject(typeof(GForm));
            Form=form;
            form.IsNonSequentialCheck=nonSequential;form.IsShowPSInDots=false;
            var window=typeof(System.Windows.Forms.Control).GetField("window",BindingFlags.NonPublic|BindingFlags.Instance);
            window.SetValue(form,window.GetValue(controlState));
            Core=new SdlpalGeshi(form);Set(form,"core",Core);Call(Core,"InitCheckPoints");
            form.rr=new GRender(controlState);
            foreach(var point in Core.CheckPoints)point.SetUIItem(form.rr.AddItem(point.GetNickName(),point.Best));
            var bridge=new SdlpalBridge();bridge.Attach(Process.GetProcessById(Engine.Process.Id));Set(Core,"bridge",bridge);
            Watch=(PTimer)typeof(TimerCore).GetField("MT",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(Core);
            // Exercise assisted-run validation independently of the diagnostic
            // rejection. No cloud runner or competition session is constructed.
            Engine.State.diagnostic=false;
        }
        internal void Tick() {Engine.Publish();Call(Core,"OnTick");}
        internal bool Statuses(CheckPointStatus first,CheckPointStatus last) =>
            Core.CheckPoints[0].Status==first&&Core.CheckPoints[1].Status==last;
        internal bool Assisted => !Core.CanBeginCompetitionHere(0)&&Core.GetScoreValidationError().Length>0;
        internal void StartNewGame()
        {
            Tick();Engine.State.started=true;Tick();
            Check(Watch.IsRunning&&Core.CurrentStep==0&&Statuses(CheckPointStatus.InProgress,CheckPointStatus.NotStarted)&&
                Core.CanBeginCompetitionHere(0)&&Core.GetScoreValidationError()=="","completion fixture observes a genuine new-game origin");
        }
        internal void CheckFrozen(string name)
        {
            var time=Core.GetMainWatch();var splits=Core.CheckPoints.Select(point=>point.Current).ToArray();
            var statuses=Core.CheckPoints.Select(point=>point.Status).ToArray();
            for(int i=0;i<4;i++) {Thread.Sleep(10);Tick();}
            Check(!Watch.IsRunning&&Core.CurrentStep==2&&Core.GetMainWatch()==time&&
                Core.CheckPoints.Select(point=>point.Current).SequenceEqual(splits)&&
                Core.CheckPoints.Select(point=>point.Status).SequenceEqual(statuses),name+" stays stopped with unchanged main/split times and statuses");
        }
        public void Dispose() {Core.Unload();controlState.Dispose();Engine.Dispose();}
    }
    static void SpeedConfigurationTests()
    {
        using(var run=new CompletionFixture(true))
        {
            run.StartNewGame();run.Watch.Stop();run.Watch.SetTS(TimeSpan.FromSeconds(20));run.Tick();
            Func<CompetitionObservation> observe=()=>{
                var value=new CompetitionObservation();
                run.Core.CaptureCompetitionIdentity(out string hash,out string version,out int fade,out int speed,out string error);
                value.DllHash=hash;value.GameVersion=version;value.FadeMilliseconds=fade;value.MapSpeedTicks=speed;
                run.Core.CaptureCompetitionGameplay(value);return value;
            };
            var initial=observe();string path=run.Core.ActiveBestPath,exported=run.Core.GetRStr();
            var reference=SdlpalBridge.Json().Deserialize<Dictionary<string,object>>(exported);
            var savedTime=TimeSpan.ParseExact(Convert.ToString(reference["Current"]),@"hh\:mm\:ss\.fff",System.Globalization.CultureInfo.InvariantCulture);
            reference["CheckPoints"]=run.Core.CheckPoints.Select((point,index)=>new{name=point.Name,des="Speed baseline "+index,time=index==0?"00:00:30.000":"00:02:00.000"}).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                File.WriteAllText(path,SerializeFixture(reference));run.Core.RefreshBestReference();
                foreach(var edit in new Action<SdlpalSnapshot>[] {s=>s.speed=5,s=>s.speed=1,s=>s.speed=0,s=>s.battleSpeed=7,
                    s=>s.battleSpeed=1,s=>s.battleSpeed=0,s=>s.walkFrameMs=80,s=>s.walkFrameMs=100,s=>s.fadeCycleMs=600,s=>s.fadeCycleMs=1200})
                {
                    edit(run.Engine.State);run.Tick();var current=observe();
                    Check(run.Watch.IsRunning&&!run.Core.IsUIPause&&run.Core.GetScoreValidationError()==""&&current.LiveTimingValid==true,
                        "normal speed adjustment keeps timing, score evidence and live validity intact");
                    Check(current.FadeMilliseconds==run.Engine.State.fadeCycleMs&&current.MapSpeedTicks==initial.MapSpeedTicks&&current.TimelineId==initial.TimelineId&&
                        current.Gameplay.walk_frame_ms==run.Engine.State.walkFrameMs&&current.Gameplay.speed==run.Engine.State.speed&&current.Gameplay.battle_speed==run.Engine.State.battleSpeed&&
                        SerializeFixture(current.Ranking)==SerializeFixture(initial.Ranking)&&current.Ranking.Valid(current.Gameplay),
                        "real speed evidence changes while configuration and timeline remain stable");
                    Check(run.Core.ActiveBestPath==path&&run.Core.CheckPoints[0].Best==TimeSpan.FromSeconds(30)&&run.Core.CheckPoints[0].NickName=="Speed baseline 0",
                        "in-run speed changes preserve the loaded PB and its nickname");
                    var actual=new HFrame.ENT.HObj(run.Core.GetRStr()).GetValue<HFrame.ENT.HObj>("SdlpalProfileData");
                    Check(actual.GetValue<int>("speed")==run.Engine.State.speed&&actual.GetValue<int>("battleSpeed")==run.Engine.State.battleSpeed&&
                        actual.GetValue<int>("walkFrameMs")==run.Engine.State.walkFrameMs&&actual.GetValue<int>("fadeCycleMs")==run.Engine.State.fadeCycleMs,
                        "exported runtime evidence retains every actual speed setting");
                }
                run.Core.Reset();run.Tick();
                Check(run.Core.ActiveBestPath==path&&observe().Gameplay.walk_frame_ms==100&&observe().FadeMilliseconds==1200,
                    "a new starting speed keeps the same PB category and real observation values");
                var imported=Call(run.Core,"ValidateImport",exported,run.Core.CurrentSnapshot.IdentityKey);
                Call(run.Core,"Restore",imported,run.Core.CurrentSnapshot);
                Check(run.Core.IsUIPause&&!run.Watch.IsRunning&&run.Core.GetMainWatch()==savedTime&&run.Assisted,
                    "timeline from another starting speed restores its time and retains assisted pause semantics");
                var foreign=Clone(run.Core.CurrentSnapshot);foreign.resourceHashes["DATA.MKF"]=new string('e',64);
                Reject(()=>Call(run.Core,"ValidateImport",exported,foreign.IdentityKey),"timeline import still rejects changed resources");
                reference=SdlpalBridge.Json().Deserialize<Dictionary<string,object>>(exported);reference["SdlpalRoute"]=new string('e',64);
                Reject(()=>Call(run.Core,"ValidateImport",SerializeFixture(reference),run.Core.CurrentSnapshot.IdentityKey),"timeline import still rejects a changed route across speeds");
            }
            finally {File.Delete(path);}
        }
    }
    sealed class PluginTestCore : TimerCore
    {
        internal PluginTestCore(GForm form) : base(form) {CoreName="PLUGIN_TEST";}
        protected override void InitCheckPoints() {CheckPoints=new List<CheckPoint>{new CheckPoint(0,new CheckPointNewer{Name="test"}){Check=()=>false}};}
        protected override void OnTick() { }
        public override void InitUI() { }
        public override void OnFunctionKey(int number) { }
        public override string GetMoreInfo()=>"";
        public override string GetSmallWatch()=>"";
        public override string GetSecondWatch()=>"";
        public override string GetGameVersion()=>"";
        public override string GetAAction()=>"";
        public override string GetCriticalError()=>"";
        public override bool IsMainWatchStar()=>false;
        public override bool IsShowC()=>false;
        public override bool NeedBlockCtrlEnter()=>false;
    }
    sealed class SilentUiContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback,object state) { }
        public override void Send(SendOrPostCallback callback,object state) { }
    }
    static void PluginCoreSwitchTests()
    {
        // Exercise GForm.LoadCore itself, with no shown window, hooks, real
        // cloud transport or background game reader in this isolated test host.
        using(var controlState=new System.Windows.Forms.Form())
        using(var menu=new System.Windows.Forms.ContextMenuStrip())
        using(var cloudMenu=new System.Windows.Forms.ContextMenuStrip())
        using(var board=new GBoard())
        using(var runner=new CloudSessionRunner(_=>new ItemCloudClient(),10))
        {
            var form=(GForm)FormatterServices.GetUninitializedObject(typeof(GForm));
            var window=typeof(System.Windows.Forms.Control).GetField("window",BindingFlags.NonPublic|BindingFlags.Instance);
            window.SetValue(form,window.GetValue(controlState));form.SyncContext=new SilentUiContext();
            form.rr=new GRender(controlState);form.rr.SetGBoard(board);form.btnPause=form.rr.AddBtn("pause",(x,y,b)=>{},9);
            Set(form,"btnCloud",form.rr.AddBtn("cloud",(x,y,b)=>{},30));
            Set(form,"btnCloudInit",new System.Windows.Forms.ToolStripMenuItem());Set(form,"cloudStatusItem",new System.Windows.Forms.ToolStripMenuItem());
            form.mnData=menu;Set(form,"cmCloud",cloudMenu);Set(form,"cloudRunner",runner);
            TimerCore selected=null;
            Action<TimerCore> load=next=>{
                typeof(TimerCore).GetField("IsAllRun",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(next,false);
                selected=next;form.LoadCore(next);
            };
            using(var manager=new PluginMgrForm(()=>selected))
            {
                Set(form,"pmf",manager);
                var table=(System.Windows.Forms.DataGridView)typeof(PluginMgrForm).GetField("dvPlugins",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(manager);
                load(new PluginTestCore(form));Check(table.Rows.Count==0,"ordinary core has no SDL built-in plugin rows");
                var first=new SdlpalGeshi(form);load(first);
                Check(table.Rows.Count==2&&Convert.ToString(table.Rows[0].Cells[2].Value)==SdlpalGeshi.GameplayName,
                    "an already-open plugin manager gains SDL rows immediately after GForm.LoadCore");
                table.Rows[0].Cells[0].Value=false;Call(manager,"dvPlugins_CellContentClick",table,new System.Windows.Forms.DataGridViewCellEventArgs(0,0));
                Check(!first.ItemSettings.FujiaEnabled,"retained manager toggles the first active SDL core");
                var second=new SdlpalGeshi(form);load(second);
                Check(table.Rows.Count==2&&!(bool)table.Rows[0].Cells[0].Value,"SDL-to-SDL switch refreshes persisted settings without duplicate rows");
                table.Rows[0].Cells[0].Value=true;Call(manager,"dvPlugins_CellContentClick",table,new System.Windows.Forms.DataGridViewCellEventArgs(0,0));
                Check(second.ItemSettings.FujiaEnabled&&!first.ItemSettings.FujiaEnabled,"retained manager rebinds changes to the replacement core only");
                load(new PluginTestCore(form));Check(table.Rows.Count==0,"switching back removes both SDL rows from the same manager");
                manager.Dispose();load(new PluginTestCore(form));Check(manager.IsDisposed,"core switching tolerates a disposed plugin window");
            }
            selected.Unload();selected.UnloadUI();selected.UnloadPlugins();
            new SdlpalItemSettings().Save();
        }
    }
    static void BattleClockTests()
    {
        foreach(bool excludePause in new[]{false,true}) using(var run=new CompletionFixture(true))
        {
            run.Form.IsBattlePauseNoTimer=excludePause;run.StartNewGame();
            var battle=(PTimer)typeof(SdlpalGeshi).GetField("battleTimer",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(run.Core);
            run.Engine.State.battle.active=true;run.Engine.State.battle.sequence=1;run.Tick();
            Check(battle.IsRunning&&run.Watch.IsRunning,"active battle starts both clocks, exclude pause="+excludePause);
            run.Core.IsUIPause=true;run.Tick();
            var main=run.Core.GetMainWatch();var pausedBattle=battle.CurrentTS;Thread.Sleep(20);run.Tick();
            Check(!run.Watch.IsRunning&&run.Core.GetMainWatch()==main&&battle.IsRunning==!excludePause&&
                (excludePause?battle.CurrentTS==pausedBattle:battle.CurrentTS>pausedBattle),"F9 freezes the main clock and follows the battle pause option="+excludePause);
            run.Core.IsUIPause=false;run.Engine.State.focused=false;run.Tick();
            Check(!run.Watch.IsRunning&&battle.IsRunning==!excludePause,"focus loss follows the battle pause option="+excludePause);
            run.Form.IsBattlePauseNoTimer=!excludePause;run.Tick();
            Check(!run.Watch.IsRunning&&battle.IsRunning==excludePause,"changing the battle pause option takes effect while paused");
            run.Form.IsBattlePauseNoTimer=excludePause;run.Engine.State.focused=true;run.Tick();
            Check(run.Watch.IsRunning&&battle.IsRunning,"regaining focus resumes both clocks");
            Set(run.Core,"busyRelay",true);run.Tick();Check(!run.Watch.IsRunning&&!battle.IsRunning,"relay freezes both clocks regardless of pause option");
            Set(run.Core,"busyRelay",false);run.Tick();
            run.Engine.View.Write(56,SdlpalBridge.GetTickCount64()-3000);Call(run.Core,"OnTick");
            Check(!run.Watch.IsRunning&&!battle.IsRunning,"stale game data freezes both clocks regardless of pause option");
            run.Tick();Check(battle.IsRunning,"fresh game data resumes the active battle");
            run.Engine.State.battle.active=false;run.Tick();Check(!battle.IsRunning,"battle exit stops the subclock");
            battle.SetTS(TimeSpan.FromSeconds(60));run.Engine.State.battle.active=true;run.Engine.State.battle.sequence++;run.Tick();
            Check(battle.IsRunning&&battle.CurrentTS<TimeSpan.FromSeconds(1),"a new battle starts its own subclock");
            battle.Stop();battle.SetTS(TimeSpan.FromSeconds(60));run.Engine.State.epoch++;run.Tick();
            Check(!run.Core.IsUIPause&&run.Watch.IsRunning&&battle.CurrentTS<TimeSpan.FromSeconds(1)&&battle.IsRunning,
                "ordinary load resumes and cannot carry the old battle time, even when the sequence is reused");
            run.Core.IsUIPause=false;run.Engine.State.completed=true;run.Tick();
            Check(!battle.IsRunning&&!run.Watch.IsRunning,"completion stops both clocks even if the battle flag remains set");
            run.Core.Reset();Check(!battle.IsRunning&&battle.CurrentTS==TimeSpan.Zero,"reset clears the battle subclock");
            run.Core.InitCheckPointsEx();
            run.Engine.State.completed=false;run.Engine.State.started=false;run.Tick();
            Check(!battle.IsRunning,"title screen cannot count a leftover battle flag");
            run.Engine.State.started=true;run.Tick();Check(battle.IsRunning,"new game can start another battle");
            run.Core.Unload();Check(!battle.IsRunning&&!run.Watch.IsRunning,"unloading the core stops its clocks");
        }
    }
    static void GameLoadResumeTests()
    {
        foreach(bool banana in new[]{false,true})
        foreach(string transition in new[]{"load","death-load","load-then-restart"})
        foreach(string pause in new[]{"none","F8","F9","relay"})
        using(var run=new CompletionFixture(true))
        {
            string name=transition+", banana="+banana+", pause="+pause;
            run.Engine.State.inventory=banana?new[]{new SdlpalInventory{slot=0,id=0x123,amount=1}}:new SdlpalInventory[0];
            run.Form.btnPause=new GRender.GBtn(null);
            run.StartNewGame();run.Watch.SetTS(TimeSpan.FromSeconds(20));
            run.Engine.State.battleEvents=new[]{Victory(1)};run.Tick();
            var completedSplit=run.Core.CheckPoints[0].Current;
            if(pause=="F8")run.Core.OnFunctionKey(8);
            if(pause=="F9")run.Form.UIPause();
            if(pause=="relay")Call(run.Core,"PauseForLoad");
            run.Tick();
            if(transition=="death-load")
            {
                run.Engine.State.battle.active=false;
                run.Engine.State.battleEvents=new[]{Victory(1),Victory(2,1,1)};run.Tick();
                Check(run.Core.CurrentStep==1&&!run.Core.CurrentSnapshot.completed,name+": defeat is not a completion");
            }
            // The game publishes ordinary/death loads in the SAME process,
            // still started, with a new epoch. Focus may return one tick later.
            run.Engine.State.epoch++;run.Engine.State.loadedFrom=3;
            run.Engine.State.battleEvents=new SdlpalBattleEvent[0];run.Engine.State.focused=false;run.Tick();
            Check(!run.Watch.IsRunning&&run.Core.IsUIPause==(pause!="none"),name+": load preserves the chosen pause without inventing one");
            run.Engine.State.focused=true;run.Tick();
            Check(run.Watch.IsRunning==(pause=="none")&&run.Core.IsUIPause==(pause!="none"),name+": loaded gameplay resumes only when not explicitly paused");
            if(transition=="load-then-restart")
            {
                run.Engine.View.Write(56,SdlpalBridge.GetTickCount64()-3000);Call(run.Core,"OnTick");
                run.Core.CurrentSnapshot.Pid++;
                run.Engine.State.started=false;run.Engine.State.epoch=0;run.Tick();
                Check(!run.Watch.IsRunning&&run.Core.IsUIPause==(pause!="none"),name+": closed game and replacement title wait automatically");
                run.Engine.State.epoch=1;run.Engine.State.started=true;run.Tick();
                Check(run.Watch.IsRunning==(pause=="none"),name+": restart after a prior load is not stuck paused");
            }
            Check(run.Core.GetMainWatch()>=TimeSpan.FromSeconds(20)&&run.Core.CurrentStep==1&&
                run.Core.CheckPoints[0].Current==completedSplit&&run.Statuses(CheckPointStatus.Completed,CheckPointStatus.InProgress),
                name+": elapsed time and completed splits survive");
            var observation=new CompetitionObservation();run.Core.CaptureCompetitionGameplay(observation);
            Check(observation.LiveTimingValid==true&&observation.ValidationError==(transition=="load-then-restart"?"游戏已重开或切换进程":"")&&
                run.Form.ManualPauseCount==((pause=="F8"||pause=="F9")?1:0),
                name+": ordinary loads preserve score eligibility while process restart evidence remains");
        }
    }
    static void ReconnectTests()
    {
        foreach(bool reusedPid in new[]{false,true}) foreach(bool titleFirst in new[]{false,true})
        foreach(string pause in new[]{"none","F9","relay"}) using(var run=new CompletionFixture(true))
        {
            run.StartNewGame();run.Watch.SetTS(TimeSpan.FromSeconds(20));
            if(pause=="F9")run.Core.IsUIPause=true;
            if(pause=="relay")Call(run.Core,"PauseForLoad");
            run.Engine.View.Write(56,SdlpalBridge.GetTickCount64()-3000);Call(run.Core,"OnTick");
            Check(!run.Watch.IsRunning,"unavailable game stops timing before reconnect");
            // The isolated MMF keeps its real process identity; model the prior
            // game's identity without launching/discovering any user's game.
            if(reusedPid)run.Core.CurrentSnapshot.Created--;
            else run.Core.CurrentSnapshot.Pid++;
            if(titleFirst)
            {
                run.Engine.State.started=false;run.Engine.State.epoch=1;run.Tick();run.Tick();
                Check(!run.Watch.IsRunning&&run.Core.IsUIPause==(pause!="none"),"replacement title waits without adding or clearing a pause");
            }
            run.Engine.State.started=true;run.Engine.State.loadedFrom=3;run.Engine.State.epoch++;run.Tick();
            Check(run.Watch.IsRunning==(pause=="none")&&run.Core.IsUIPause==(pause!="none")&&run.Core.GetMainWatch()>=TimeSpan.FromSeconds(20),
                "reconnect preserves time and selected pause: reused PID="+reusedPid+", title="+titleFirst+", pause="+pause);
            var observation=new CompetitionObservation();run.Core.CaptureCompetitionGameplay(observation);
            Check(observation.LiveTimingValid==true&&observation.ValidationError=="游戏已重开或切换进程",
                "reconnected live timing is valid while score restart evidence remains");
            run.Core.IsUIPause=false;run.Engine.State.epoch++;run.Tick();
            Check(!run.Core.IsUIPause&&run.Watch.IsRunning,"later same-process load also continues after reconnect");
        }
        using(var run=new CompletionFixture(true))
        {
            run.StartNewGame();
            Func<CompetitionObservation> observe=()=>{var value=new CompetitionObservation();run.Core.CaptureCompetitionGameplay(value);return value;};
            Check(observe().LiveTimingValid==true,"fresh ordinary run has a valid live timing observation");
            run.Engine.View.Write(56,SdlpalBridge.GetTickCount64()-3000);Call(run.Core,"OnTick");
            Check(observe().LiveTimingValid==false,"stale native snapshot invalidates live timing");
            run.Tick();run.Engine.View.Write(0,(byte)0);Call(run.Core,"OnTick");
            Check(observe().LiveTimingValid==false,"malformed native snapshot invalidates live timing");
            run.Engine.State.walkFrameMs=0;run.Tick();Check(observe().LiveTimingValid==false,"missing runtime speed invalidates live timing");
            run.Engine.State.walkFrameMs=90;run.Engine.State.diagnostic=true;run.Tick();
            Check(observe().LiveTimingValid==false,"diagnostic snapshot cannot claim valid live timing");
        }
    }
    static void MaintenanceSaveIsolationTests()
    {
        string[] profiles={"geshi-easy-v1","dream222-normal-v1","dream222-hard-v1","dreamspace39x-hard-v1","dream22-puppet100-v1"};
        byte[] bytes=Enumerable.Repeat((byte)42,2048).ToArray();
        foreach(string profile in profiles) using(var engine=new Engine()) using(var bridge=new SdlpalBridge()) {
            engine.State.profile=profile;engine.State.saveLayout="content-v1";
            string id=profile.Substring(0,profile.Length-3);
            engine.State.savePath=Path.Combine(root,"isolated-save",id);Directory.CreateDirectory(engine.State.savePath);
            string marker=Path.Combine(engine.State.savePath,".content-package");File.WriteAllText(marker,id+"\n");
            engine.Publish();bridge.Attach(Process.GetProcessById(engine.Process.Id));SdlpalSnapshot snapshot;
            Check(bridge.TryRead(out snapshot),"content relay snapshot reads "+profile);
            var pack=new SdlpalRelayPackage{identity=snapshot.IdentityKey,timer="{}",saveBase64=Convert.ToBase64String(bytes),saveSha256=SdlpalRelayPackage.Hash(bytes)};
            Check(pack.Validate(snapshot.IdentityKey).SequenceEqual(bytes),"same content cloud payload matches "+profile);
            foreach(string foreign in profiles.Where(p=>p!=profile)) {var other=Clone(snapshot);other.profile=foreign;Reject(()=>pack.Validate(other.IdentityKey),"cross content cloud package rejected "+profile+" -> "+foreign);}
            var reply=engine.Reply(2,1,bytes);SdlpalControl.Execute(bridge,2,snapshot,pack.Validate(snapshot.IdentityKey));reply.GetAwaiter().GetResult();
            Check(File.ReadAllBytes(Path.Combine(engine.State.savePath,"250.rpg")).SequenceEqual(bytes),"cloud load writes only selected content slot "+profile);
            engine.State.canControl=true;engine.Publish();bridge.TryRead(out snapshot);
            ulong request=engine.Control.ReadUInt64(40);File.WriteAllText(marker,"foreign-content\n");
            Reject(()=>SdlpalControl.Execute(bridge,2,snapshot,new byte[2048]),"changed directory marker rejects before cloud write "+profile);
            Check(request==engine.Control.ReadUInt64(40)&&File.ReadAllBytes(Path.Combine(engine.State.savePath,"250.rpg")).SequenceEqual(bytes),"rejected cloud load changes neither command nor RPG "+profile);
            File.WriteAllText(marker,id+"\n");var moved=Clone(snapshot);moved.savePath=Path.Combine(root,"save");
            Reject(()=>SdlpalControl.Execute(bridge,2,moved,bytes),"changed destination cancels cloud transaction "+profile);
        }
    }
    static void OnlineSnapshotBridgeTests()
    {
        // Load only pure mapping methods from the freshly built pair. Never
        // initialize the online module, authenticate, or open a timer window.
        var host=Assembly.LoadFrom(Path.Combine(root,"Pal98Timer.exe"));
        var online=Assembly.LoadFrom(Path.Combine(root,"PalTimerOnline.dll"));
        var observationType=host.GetType("Pal98Timer.CompetitionObservation",true);
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var read=online.GetType("Pal98Timer.OnlineModule",true).GetMethod("ReadSnapshot",BindingFlags.Static|BindingFlags.NonPublic);
        var create=host.GetType("Pal98Timer.GForm",true).GetMethod("CreateCompetitionSnapshot",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(string family in new[]{"sdlpal-geshi-easy","sdlpal-dream222","sdlpal-dreamspace39x","sdlpal-dream22","standard","drawcard"}) foreach(bool? valid in new bool?[]{null,false,true})
        foreach(int? speed in new int?[]{null,0,7,100})
        {
            var value=Activator.CreateInstance(observationType,true);
            var gameplay=Activator.CreateInstance(host.GetType("Pal98Timer.GameplayIdentity",true),true);
            gameplay.GetType().GetProperty("family").SetValue(gameplay,family);
            gameplay.GetType().GetProperty("speed").SetValue(gameplay,speed);
            gameplay.GetType().GetProperty("battle_speed").SetValue(gameplay,speed.HasValue?(int?)(100-speed.Value):null);
            observationType.GetField("Gameplay",flags).SetValue(value,gameplay);
            observationType.GetField("LiveTimingValid",flags).SetValue(value,valid);
            observationType.GetField("ReleaseRevision",flags).SetValue(value,"r53");
            observationType.GetField("ValidationError",flags).SetValue(value,"游戏已读档或重启");
            observationType.GetField("Splits",flags).SetValue(value,Array.CreateInstance(host.GetType("Pal98Timer.CompetitionSplit",true),0));
            var snapshot=create.Invoke(null,new[]{value});
            var extensions=(IReadOnlyDictionary<string,string>)snapshot.GetType().GetProperty("Extensions").GetValue(snapshot);
            var decoded=read.Invoke(null,new[]{snapshot});
            bool? expected=GameplayIdentity.IsSdlFamily(family)?valid:null;
            var decodedGameplay=decoded.GetType().GetField("Gameplay",flags).GetValue(decoded);
            int? expectedSpeed=GameplayIdentity.IsSdlFamily(family)?speed:null;
            Check((int?)decodedGameplay.GetType().GetProperty("speed").GetValue(decodedGameplay)==expectedSpeed&&
                (int?)decodedGameplay.GetType().GetProperty("battle_speed").GetValue(decodedGameplay)==(expectedSpeed.HasValue?(int?)(100-expectedSpeed.Value):null)&&
                extensions.ContainsKey("sdlpal.speed")==expectedSpeed.HasValue&&extensions.ContainsKey("sdlpal.battle_speed")==expectedSpeed.HasValue,
                "actual host/module bridge preserves optional SDL speeds and legacy isolation: "+family+"/"+speed);
            Check((string)decoded.GetType().GetField("ReleaseRevision",flags).GetValue(decoded)==(GameplayIdentity.IsSdlFamily(family)?"r53":null), "release display extension follows SDL family: "+family);
            Check(extensions.ContainsKey("sdlpal.live_timing_valid")==expected.HasValue&&
                (bool?)decoded.GetType().GetField("LiveTimingValid",flags).GetValue(decoded)==expected&&
                (string)decoded.GetType().GetField("ValidationError",flags).GetValue(decoded)=="游戏已读档或重启",
                "actual host/module bridge preserves score evidence and optional SDL live validity: "+family+"/"+valid);
        }
        var snapshotType=host.GetType("Pal98Timer.OnlineSnapshotV1",true);
        var game=Activator.CreateInstance(host.GetType("Pal98Timer.OnlineGameplayV1",true),new object[]{null,null,null,null,"sdlpal-geshi-easy",800,0});
        foreach(string malformed in new[]{null,"","TRUE","1","valid"})
        {
            var snapshot=Activator.CreateInstance(snapshotType,new object[]{"fixture","SDLPAL_GESHI_EASY",null,null,null,null,null,game,null,null,
                0,800,0,0L,DateTimeOffset.UtcNow,false,false,Array.CreateInstance(host.GetType("Pal98Timer.OnlineSplitV1",true),0),
                new Dictionary<string,string>{{"sdlpal.live_timing_valid",malformed}}});
            bool rejected=false;
            try{read.Invoke(null,new[]{snapshot});}catch(TargetInvocationException error){if(error.InnerException is ArgumentException)rejected=true;else throw;}
            Check(rejected,"malformed live validity extension is rejected: "+(malformed??"null"));
        }
        foreach(string key in new[]{"sdlpal.speed","sdlpal.battle_speed"})
        foreach(string malformed in new[]{null,"","-1","+1","101","2147483648","1.5"," 1","1 ","NaN"})
        {
            var snapshot=Activator.CreateInstance(snapshotType,new object[]{"fixture","SDLPAL_GESHI_EASY",null,null,null,null,null,game,null,null,
                0,800,0,0L,DateTimeOffset.UtcNow,false,false,Array.CreateInstance(host.GetType("Pal98Timer.OnlineSplitV1",true),0),
                new Dictionary<string,string>{{key,malformed}}});
            bool rejected=false;
            try{read.Invoke(null,new[]{snapshot});}catch(TargetInvocationException error){if(error.InnerException is ArgumentException)rejected=true;else throw;}
            Check(rejected,"malformed SDL speed extension is rejected: "+key+"/"+(malformed??"null"));
        }
    }
    static void BestReferenceTests()
    {
        using(var run=new CompletionFixture(true))
        {
            run.StartNewGame();run.Engine.State.battleEvents=new[]{Victory(1)};run.Tick();
            run.Core.IsUIPause=true;run.Tick();
            var defaults=SdlpalSplitRule.Defaults().Select(rule=>rule.Name).ToArray();
            var times=run.Core.CheckPoints.Select(point=>point.Current).ToArray();
            var statuses=run.Core.CheckPoints.Select(point=>point.Status).ToArray();
            var elapsed=run.Core.GetMainWatch();
            string path=run.Core.ActiveBestPath;
            var reference=SdlpalBridge.Json().Deserialize<Dictionary<string,object>>(run.Core.GetRStr());
            reference["CheckPoints"]=run.Core.CheckPoints.Select((point,index)=>new {name=point.Name,des="Reference A "+index,
                time=index==0?"00:00:20.000":"00:02:00.000"}).ToArray();
            string complete=SerializeFixture(reference);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Action assertCleared=()=>{
                Check(run.Core.CheckPoints.All(point=>point.Best==TimeSpan.Zero)&&
                    run.Core.CheckPoints.Select(point=>point.GetNickName()).SequenceEqual(defaults),"missing or invalid reference clears times and restores route labels");
                Check((TimeSpan)typeof(TimerCore).GetField("BestClear",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(run.Core)==TimeSpan.Zero&&
                    (TimeSpan)typeof(TimerCore).GetField("WillClear",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(run.Core)==TimeSpan.Zero,
                    "missing or invalid reference clears the total and predicted finish");
                Check(run.Core.CheckPoints.All(point=>{
                    var item=(GRender.GItem)typeof(CheckPoint).GetField("_uiItem",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(point);
                    return item.Best==point.Best&&item.Name==point.GetNickName();}),"rendered references follow the active category");
            };
            try
            {
                File.WriteAllText(path,complete);run.Core.RefreshBestReference();
                Check(run.Core.CheckPoints[0].Best==TimeSpan.FromSeconds(20)&&run.Core.CheckPoints[1].Best==TimeSpan.FromSeconds(120)&&
                    run.Core.CheckPoints[0].NickName=="Reference A 0","category A loads both reference values and edited labels");
                run.Engine.State.resourceHashes["DATA.MKF"]=new string('e',64);run.Tick();
                Check(path!=run.Core.ActiveBestPath&&!File.Exists(run.Core.ActiveBestPath),"category B has no reference file");
                assertCleared();
                run.Engine.State.resourceHashes["DATA.MKF"]=new string('b',64);run.Tick();
                Check(run.Core.CheckPoints[1].Best==TimeSpan.FromSeconds(120)&&run.Core.CheckPoints[1].NickName=="Reference A 1",
                    "switching back restores category A's reference");
                reference["CheckPoints"]=new[]{new{name=run.Core.CheckPoints[0].Name,des="Partial",time="00:00:30.000"}};
                File.WriteAllText(path,SerializeFixture(reference));run.Core.RefreshBestReference();
                Check(run.Core.CheckPoints[0].Best==TimeSpan.FromSeconds(30)&&run.Core.CheckPoints[0].NickName=="Partial"&&
                    run.Core.CheckPoints[1].Best==TimeSpan.Zero&&run.Core.CheckPoints[1].GetNickName()==defaults[1],
                    "a partial reference resets omitted checkpoints to route defaults");
                reference["CheckPoints"]=new object[]{new{name=run.Core.CheckPoints[0].Name,des="Invalid partial",time="00:00:40.000"},null};
                string malformed=SerializeFixture(reference);File.WriteAllText(path,malformed);
                Reject(()=>run.Core.RefreshBestReference(),"an invalid reference after a valid first point is reported");assertCleared();
                Check(File.ReadAllText(path)==malformed,"invalid reference input remains available for repair");
                File.WriteAllText(path,complete);run.Core.RefreshBestReference();
                reference["SdlpalCategory"]="wrong-category";File.WriteAllText(path,SerializeFixture(reference));
                Reject(()=>run.Core.RefreshBestReference(),"foreign category reference is rejected");assertCleared();
                Check(run.Core.CurrentStep==1&&run.Core.IsUIPause&&!run.Watch.IsRunning&&run.Core.GetMainWatch()==elapsed&&
                    run.Core.CheckPoints.Select(point=>point.Current).SequenceEqual(times)&&run.Core.CheckPoints.Select(point=>point.Status).SequenceEqual(statuses)&&
                    run.Core.GetScoreValidationError().Contains("配置变化"),
                    "reference refresh preserves the paused run, actual splits, statuses and changed-identity validation");
            }
            finally {File.Delete(path);}
        }
    }
    static void CompletionTests()
    {
        foreach(bool nonSequential in new[]{true,false})
        {
            string mode=nonSequential?"default nonsequential":"explicit sequential";
            using(var run=new CompletionFixture(nonSequential))
            {
                run.StartNewGame();run.Engine.State.battleEvents=new[]{Victory(1)};run.Tick();
                Check(run.Watch.IsRunning&&run.Core.CurrentStep==1&&run.Statuses(CheckPointStatus.Completed,CheckPointStatus.InProgress),
                    mode+": production OnTick observes the new serpent victory before the ending");
                run.Engine.State.completed=true;run.Tick();
                Check(!run.Watch.IsRunning&&run.Core.CurrentStep==2&&run.Statuses(CheckPointStatus.Completed,CheckPointStatus.Completed)&&
                    run.Core.GetScoreValidationError()=="",mode+": observed new game completes and stops without becoming assisted");
                run.CheckFrozen(mode+" new game");
            }
            using(var run=new CompletionFixture(nonSequential))
            {
                run.Engine.State.started=true;run.Engine.State.loadedFrom=1;run.Tick();
                Check(run.Watch.IsRunning&&run.Core.CurrentStep==0&&run.Statuses(CheckPointStatus.InProgress,CheckPointStatus.NotStarted)&&run.Assisted,
                    mode+": original-save late attach starts an assisted clock without inventing a serpent victory");
                run.Engine.State.completed=true;run.Tick();
                if(nonSequential)
                {
                    Check(!run.Watch.IsRunning&&run.Core.CurrentStep==2&&run.Statuses(CheckPointStatus.AutoSkipped,CheckPointStatus.Completed)&&run.Assisted,
                        mode+": completed skips the unobserved serpent and stops the assisted run");
                }
                else
                {
                    var before=run.Core.GetMainWatch();Thread.Sleep(20);run.Tick();
                    Check(run.Core.CurrentSnapshot.completed&&run.Watch.IsRunning&&run.Core.GetMainWatch()>before&&run.Core.CurrentStep==0&&
                        run.Statuses(CheckPointStatus.InProgress,CheckPointStatus.NotStarted)&&run.Assisted,
                        mode+": completed alone retains the unobserved current node and running clock");
                    run.Core.Jump(1);run.Tick();
                    Check(!run.Watch.IsRunning&&run.Core.CurrentStep==2&&run.Statuses(CheckPointStatus.ManualSkipped,CheckPointStatus.Completed)&&run.Assisted,
                        mode+": manual jump to the latched ending stops and remains assisted");
                }
                run.CheckFrozen(mode+" late attach");
            }
            var imports=new[]{
                new{Step=0,First=CheckPointStatus.InProgress},
                new{Step=1,First=CheckPointStatus.Completed},
                new{Step=1,First=CheckPointStatus.ManualSkipped},
                new{Step=1,First=CheckPointStatus.AutoSkipped},
                new{Step=2,First=CheckPointStatus.Completed}
            };
            foreach(var source in imports) using(var run=new CompletionFixture(nonSequential))
            {
                run.StartNewGame();
                string name=mode+" imported step "+source.Step+"/"+source.First;
                var last=source.Step==2?CheckPointStatus.Completed:source.Step==1?CheckPointStatus.InProgress:CheckPointStatus.NotStarted;
                var timeline=SdlpalBridge.Json().Deserialize<Dictionary<string,object>>(run.Core.GetRStr());
                timeline["Current"]="00:01:23.456";timeline["Step"]=source.Step;
                timeline["CheckPoints"]=run.Core.CheckPoints.Select((point,index)=>new {name=point.Name,
                    time=index==0&&source.Step>0?"00:00:20.123":index==1&&source.Step==2?"00:01:05.432":"00:00:00.000"}).ToArray();
                timeline["SdlpalStatuses"]=new[]{source.First.ToString(),last.ToString()};
                var imported=Call(run.Core,"ValidateImport",SerializeFixture(timeline),run.Core.CurrentSnapshot.IdentityKey);
                Call(run.Core,"Restore",imported,run.Core.CurrentSnapshot);
                Check(run.Core.IsUIPause&&!run.Watch.IsRunning&&run.Core.GetMainWatch()==TimeSpan.FromMilliseconds(83456)&&
                    run.Core.CurrentStep==source.Step&&run.Statuses(source.First,last)&&run.Assisted,name+": restore preserves progress, pauses and marks assistance");
                run.Engine.State.completed=true;run.Tick();
                Check(run.Core.CurrentSnapshot.completed&&run.Core.IsUIPause&&!run.Watch.IsRunning&&run.Core.GetMainWatch()==TimeSpan.FromMilliseconds(83456)&&
                    run.Core.CurrentStep==source.Step&&run.Statuses(source.First,last)&&run.Assisted,name+": observing completed while paused does not advance or restart");
                run.Core.IsUIPause=false;run.Tick();
                if(source.Step==0&&!nonSequential)
                {
                    Check(run.Watch.IsRunning&&run.Core.CurrentStep==0&&run.Statuses(CheckPointStatus.InProgress,CheckPointStatus.NotStarted)&&run.Assisted,
                        name+": resume still waits for the unobserved serpent in sequential mode");
                }
                else
                {
                    var expectedFirst=source.Step==0?CheckPointStatus.AutoSkipped:source.First;
                    Check(!run.Watch.IsRunning&&run.Core.CurrentStep==2&&run.Statuses(expectedFirst,CheckPointStatus.Completed)&&run.Assisted,
                        name+": resume completes or retains the finished route without granting eligibility");
                    if(source.Step==2)
                        Check(run.Core.GetMainWatch()==TimeSpan.FromMilliseconds(83456)&&run.Core.CheckPoints[1].Current==TimeSpan.FromMilliseconds(65432),
                            name+": finished import retains its distinct main and final-split values");
                    run.CheckFrozen(name);
                }
            }
        }
    }
    static void OverlayIdentityTests()
    {
        Check(SdlpalGeshi.OverlayIdentity(-1,"")=="云ID:未激活  连接ID:--（未连接）","unavailable cloud and live connection never show an invented ID");
        Check(SdlpalGeshi.OverlayIdentity(42," [已连接:123456]")=="云ID:42  连接ID:123456（已连接）","OBS displays acknowledged public live ID beside cloud ID");
        Check(SdlpalGeshi.OverlayIdentity(42," [不完全连接:234567]").Contains("连接ID:234567（不完全连接）"),"OBS preserves incomplete connection state");
        Check(SdlpalGeshi.OverlayIdentity(42," [连接失败]").EndsWith("连接ID:--（连接失败）"),"disconnect clears displayed public ID without claiming connection");
        Check(SdlpalGeshi.OverlayIdentity(42," [系统时间不同步]").Contains("系统时间不同步"),"OBS retains authentication clock warning");
        Check(SdlpalGeshi.OverlayIdentity(42," [已连接:abc]").EndsWith("连接ID:--（未连接）"),"malformed caption cannot manufacture a connected ID");
        var first=new Dx9OverlayTimelineEntry("八头蛇胜利","01:00:00.00","00:58:10.50",-110,false,true);
        var second=new Dx9OverlayTimelineEntry("通关","01:20:00.00","01:02:03.45",0,true,false);
        var legacy=new Dx9OverlaySnapshot(IntPtr.Zero,"Microsoft YaHei UI","01:02:03.45","12.3s","00:02:01.00","金钱 10000",1,first,second,first,"暂停",false,true,"旧核心","","","",42,"道具107种");
        Check(legacy.IdentityLine==null&&legacy.TimelineThird.HasValue&&legacy.FooterText.Contains("云ID:42"),"original cores retain third timeline row and constructor/footer behavior");
        foreach(var size in new[]{9f,18f})
        {
            string identity=SdlpalGeshi.OverlayIdentity(int.MaxValue," [不完全连接:999999999999999999]");
            var snapshot=new Dx9OverlaySnapshot(IntPtr.Zero,"Microsoft YaHei UI","01:02:03.45","12.3s","00:02:01.00","隔世追凶 简单  金钱 10000",1,first,second,new Dx9OverlayTimelineEntry(),"暂停",false,true,"隔世追凶-简单","","","",int.MaxValue,"钱10000 道具107种 / 458件  监测 铜钱镖×99",identity);
            Check(!snapshot.FooterText.Contains("云ID")&&snapshot.IdentityLine.Contains("999999999999999999"),"identity moves out of plug-in footer without truncating long public ID at font "+size);
            var layout=Dx9OverlayLayoutSettings.CreateDefault();layout.FontFamily="Microsoft YaHei UI";layout.FontSize=size;
            using(var overlay=new Dx9OverlayForm(()=>snapshot,layout,_=>{}))
            {
                Set(overlay,"CurrentSnapshot",snapshot);
                float line=(float)Call(overlay,"GetInfoHeightLogicalPixels"),identityHeight=(float)Call(overlay,"GetIdentityHeightLogicalPixels");
                float height=(float)Call(overlay,"GetOverlayHeightLogicalPixels");
                Set(overlay,"CurrentSnapshot",legacy);float legacyHeight=(float)Call(overlay,"GetOverlayHeightLogicalPixels");Set(overlay,"CurrentSnapshot",snapshot);
                Check(identityHeight>line&&height>legacyHeight,"long identity row expands canvas instead of overlapping plug-in footer at font "+size);
                overlay.ClientSize=new System.Drawing.Size(340,(int)Math.Ceiling(height));
                using(var bitmap=new System.Drawing.Bitmap(overlay.ClientSize.Width,overlay.ClientSize.Height))
                using(var graphics=System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.Clear(System.Drawing.Color.FromArgb(40,43,45));
                    Call(overlay,"OnPaint",new System.Windows.Forms.PaintEventArgs(graphics,overlay.ClientRectangle));
                    bitmap.Save(Path.Combine(root,"obs-identities-font-"+size.ToString(System.Globalization.CultureInfo.InvariantCulture)+".png"));
                }
            }
        }
    }
    static void PauseHotkeyTests()
    {
        var previousHotkey=SoundConfig.ins.ToggleHotkey;
        SoundConfig.ins.ToggleHotkey=System.Windows.Forms.Keys.None;
        try
        {
            foreach(var first in new[]{System.Windows.Forms.Keys.F8,System.Windows.Forms.Keys.F9})
            using(var run=new CompletionFixture(true))
            {
                var second=first==System.Windows.Forms.Keys.F8?System.Windows.Forms.Keys.F9:System.Windows.Forms.Keys.F8;
                Set(run.Form,"startupCompleted",true);
                run.Form.btnPause=new GRender.GBtn(null);
                run.Form.SetUIPause(false);run.StartNewGame();
                Action<System.Windows.Forms.Keys,int> send=(key,flags)=>{
                    bool blocked;
                    run.Form.OnKeyPress(new KeyboardLib.HookStruct{vkCode=(int)key,flags=flags},out blocked);
                    Check(!blocked,key+" retains SDL key passthrough");
                };
                send(first,0);send(first,0);run.Tick();
                Check(!run.Core.IsUIPause&&run.Watch.IsRunning&&run.Form.ManualPauseCount==0&&run.Form.btnPause.IsWhite(),
                    first+" keydown/repeat does not pause");
                send(first,128);run.Tick();
                Check(run.Core.IsUIPause&&!run.Watch.IsRunning&&run.Form.ManualPauseCount==1&&run.Form.btnPause.IsRed(),
                    first+" keyup pauses exactly once");
                send(second,0);send(second,0);run.Tick();
                Check(run.Core.IsUIPause&&!run.Watch.IsRunning&&run.Form.ManualPauseCount==1&&run.Form.btnPause.IsRed(),
                    second+" keydown/repeat does not resume");
                send(second,128);run.Tick();
                Check(!run.Core.IsUIPause&&run.Watch.IsRunning&&run.Form.ManualPauseCount==1&&run.Form.btnPause.IsWhite(),
                    second+" keyup resumes without incrementing pause count");
            }
        }
        finally {SoundConfig.ins.ToggleHotkey=previousHotkey;}
    }
    static void MaintenanceProfiles()
    {
        ClassicRouteTests();
        foreach (string profile in new[]{"geshi-hard-v1", "geshi-nightwolf-v1", "geshi-fanmade-v1", "geshi-fanmade-entertainment-v1"})
        {
            var rules = SdlpalSplitRule.Defaults(profile); SdlpalSplitRule.Validate(rules);
            Check(rules.Length == 2 && SdlpalBridge.SupportedProfile(profile), "independent Geshi route: " + profile);
            var fight = new SdlpalBattleEvent{team=315,enemies=new[]{547},result=3};
            Check(rules[0].Matches(fight), "Geshi audited eight-headed serpent victory");
            fight.result = 1; Check(!rules[0].Matches(fight), "Geshi defeat cannot split");
            fight.result = 3; fight.team = 314; Check(!rules[0].Matches(fight), "another team cannot split Geshi");
            var state = Snapshot(); state.profile = profile; state.started = false;
            var observer = new SdlpalRunObserver(); observer.Observe(Clone(state),rules);
            state.started = true; observer.Observe(Clone(state),rules);
            state.battleEvents = new[]{new SdlpalBattleEvent{seq=1,epoch=1,team=315,enemies=new[]{547},result=3}};
            observer.Observe(Clone(state),rules);
            Check(!observer.Reached.Contains(rules[1].Id), "Geshi boss victory waits for the ending");
            state.completed = true; observer.Observe(Clone(state),rules);
            Check(observer.Reached.Contains(rules[1].Id), "actual Geshi ending completes route");
            var facts = state.RankingFacts();
            Check(facts["profile"] == profile && facts["family"] == "sdlpal-geshi", "Geshi difficulty keeps independent ranking identity");
        }
        foreach (string profile in new[]{"mogai10-v1", "tianqi20-v1", "dream211-v1", "dream211-pudu-v1",
            "dream211-yueying-v1", "dream211-yueying-rat-v1", "hunqian-branch-v1", "hunqian167-easy-v1", "hunqian167-hard-v1"})
        {
            var expanded = SdlpalSplitRule.Defaults(profile); SdlpalSplitRule.Validate(expanded);
            int count = profile == "tianqi20-v1" ? 17 : profile == "dream211-v1" || profile == "dream211-pudu-v1" ? 21 : 18;
            Check(expanded.Length == count && SdlpalBridge.SupportedProfile(profile), "independent expanded route: " + profile);
            var ending = expanded.Last();
            Check(ending.Kind == "completion" && !ending.Team.HasValue && !ending.Scene.HasValue && !ending.Enemy.HasValue,
                "expanded endings wait for the actual ending instead of reusing a Dream boss");
            var state = Snapshot(); state.profile = profile; state.started = true; state.music = 23; state.scene = 106;
            var tower = expanded.Single(r => r.Id == "dream-tower");
            Check(!tower.MatchesState(state), "same music in an earlier scene cannot split the tower");
            state.scene = 144; Check(tower.MatchesState(state), "tower music splits in its audited scene");
            var observer = new SdlpalRunObserver(); state.started = false; observer.Observe(Clone(state), expanded);
            state.started = true; observer.Observe(Clone(state), expanded);
            state.battleEvents = new[]{new SdlpalBattleEvent{seq=1,epoch=1,team=389,scene=297,enemies=new[]{576},result=3}};
            observer.Observe(Clone(state), expanded);
            Check(!observer.Reached.Contains(ending.Id), "foreign Dream final battle cannot complete expanded content");
            state.completed = true; observer.Observe(Clone(state), expanded);
            Check(observer.Reached.Contains(ending.Id), "actual ending observation completes expanded content");
            foreach (var rule in expanded.Where(r => r.Kind == "battle" || r.Kind == "battle-end"))
            {
                var fight = new SdlpalBattleEvent{team=rule.Team.Value,scene=rule.Scenes[0],enemies=new[]{rule.Enemy.Value},result=3};
                Check(rule.Matches(fight), "expanded audited battle: " + rule.Id);
                fight.scene = 1; Check(!rule.Matches(fight), "shared enemy in another scene cannot split expanded route");
            }
            var facts = state.RankingFacts();
            Check(facts["profile"] == profile && facts["family"] == SdlpalBridge.ProfileFamily(profile),
                "expanded profile keeps its own ranking family");
        }
        var paths = new HashSet<string>();
        foreach (string profile in new[]{"dream222-normal-v1","dream222-hard-v1","dreamspace39x-hard-v1","dream22-puppet100-v1"})
        {
            var rules=SdlpalSplitRule.Defaults(profile); SdlpalSplitRule.Validate(rules);
            Check(rules.Length==21,"audited original Dream 2.2 route has 21 nodes: "+profile);
            var s=Snapshot();s.profile=profile;s.scene=5;s.viewportX=1088;s.viewportY=1088;s.started=true;
            Check(rules[0].MatchesState(s),"ship node uses actual viewport coordinates");
            s.viewportX+=200;Check(!rules[0].MatchesState(s),"position outside route bounds cannot split");
            var caiyi=rules.Single(r=>r.Id=="dream-caiyi");
            Check(caiyi.Matches(new SdlpalBattleEvent{team=39,scene=108,enemies=new[]{468},result=1}),"Caiyi preserves original battle-end exception");
            var poison=rules.Single(r=>r.Id=="dream-seven-poisons");
            Check(!poison.Matches(new SdlpalBattleEvent{team=224,scene=200,enemies=new[]{533},result=3}),"Dreamspace shared enemy ID in fire fight cannot trigger seven-poisons node");
            foreach(var rule in rules.Where(r=>r.Kind=="battle" || r.Kind=="completion"))
            {
                var fight=new SdlpalBattleEvent{team=rule.Team.Value,scene=rule.Scene??rule.Scenes[0],enemies=new[]{rule.Enemy.Value},result=3};
                Check(rule.Matches(fight),"audited team/scene/object victory: "+rule.Id);
                fight.result=1;Check(!rule.Matches(fight),"defeat cannot satisfy victory: "+rule.Id);
            }
            var observer=new SdlpalRunObserver();s=Snapshot();s.profile=profile;observer.Observe(Clone(s),rules);
            s.started=true;observer.Observe(Clone(s),rules);s.battleEvents=new[]{new SdlpalBattleEvent{seq=1,epoch=1,team=389,scene=297,enemies=new[]{576},result=3}};
            observer.Observe(Clone(s),rules);
            Check(observer.Reached.Contains("dream-ending")&&!s.completed,"final victory stops timer before ending dialogue");
            var settings=new SdlpalItemSettings{WatchedIds=new[]{579}};settings.Save(profile);
            Check(SdlpalItemSettings.Load(profile).WatchedIds.SequenceEqual(new[]{579}),"profile-local watched items persist");
            var facts=s.RankingFacts(); var game=new GameplayIdentity{family=SdlpalBridge.ProfileFamily(profile),content_id=profile,content_sha256=facts["content"],rules_sha256=RankingConfiguration.Digest(facts),fade_ms=800,map_speed_ticks=0,walk_frame_ms=90,speed=s.speed,battle_speed=s.battleSpeed};
            Check(new RankingConfiguration{configuration_id=game.rules_sha256,covered=true,rules=facts}.Valid(game),"known family/profile ranking remains validated");
            Check(paths.Add(s.IdentityKey),"profile separates PB/relay identity");
        }
        using(var engine=new Engine())
        {
            var form=(GForm)FormatterServices.GetUninitializedObject(typeof(GForm));
            using(var control=new System.Windows.Forms.Form())
            {
                typeof(System.Windows.Forms.Control).GetField("window",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(form,typeof(System.Windows.Forms.Control).GetField("window",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(control));
                var core=new SdlpalGeshi(form);Call(core,"InitCheckPoints");form.rr=new GRender(control);
                var bridge=new SdlpalBridge();bridge.Attach(Process.GetProcessById(engine.Process.Id));Set(core,"bridge",bridge);
                try {
                    Call(core,"OnTick");engine.State.profile="dream222-normal-v1";engine.Publish();Call(core,"OnTick");
                    Check(core.ActiveProfile==engine.State.profile&&core.CheckPoints.Count==21&&core.GetGameVersion().Contains("梦幻"),"actual core auto-switches from Geshi to Dream route and label");
                    var itemBefore = core.ItemSettings;
                    Reject(() => core.ApplyProfileItemSettings(new SdlpalItemSettings { WatchedIds = new[] { 61 } }, "geshi-easy-v1"),
                        "stale item editor cannot save Geshi settings into Dream");
                    Check(core.ItemSettings.WatchedIds.SequenceEqual(itemBefore.WatchedIds), "rejected old editor preserves active content item settings");
                    string routePath = TimerUserSettings.GetPath("sdlpal_dream222-normal-v1_splits.json");
                    byte[] routeBefore = File.Exists(routePath) ? File.ReadAllBytes(routePath) : null;
                    Reject(() => core.SaveProfileRules(new SdlpalSplitRule[0], "geshi-easy-v1"), "stale route editor cannot overwrite Dream nodes");
                    Check(routeBefore == null ? !File.Exists(routePath) : routeBefore.SequenceEqual(File.ReadAllBytes(routePath)),
                        "stale route rejection preserves active content rules byte-for-byte");
                    string previous=core.ActiveBestPath;engine.State.profile="dreamspace39x-hard-v1";engine.Publish();Call(core,"OnTick");
                    Check(core.ActiveProfile==engine.State.profile&&previous!=core.ActiveBestPath&&core.GetGameVersion().Contains("梦时空"),"actual core isolates Dreamspace PB and display");
                    previous=core.ActiveBestPath;engine.State.profile="dream22-puppet100-v1";engine.Publish();Call(core,"OnTick");
                    Check(core.ActiveProfile==engine.State.profile&&previous!=core.ActiveBestPath&&core.CheckPoints.Count==21&&core.GetGameVersion().Contains("傀儡100%"),"actual core selects Dream 2.2 puppet route and separate PB");
                    engine.State.profile="geshi-easy-v1";engine.Publish();Call(core,"OnTick");
                    Check(core.CheckPoints.Count==2&&core.GetGameVersion().Contains("隔世"),"switch back restores original Geshi route");
                } finally {core.Unload();}
            }
        }
    }
    static void ClassicRouteTests()
    {
        foreach (string profile in new[] { "pal-dos-v1", "pal98-classic-v6-v1" })
        {
            bool dos = profile == "pal-dos-v1";
            var rules = SdlpalSplitRule.Defaults(profile); SdlpalSplitRule.Validate(rules);
            Check(rules.Length == 21 && SdlpalBridge.SupportedProfile(profile), "classic route retains all 21 native checkpoints: " + profile);
            Check(SdlpalBridge.ProfileFamily(profile) == (dos ? "sdlpal-dos" : "sdlpal-classic98"), "new content has its own ranking family");
            Check(rules.Single(r => r.Name == "进十年前").Scene == (dos ? 227 : 247) &&
                rules.Single(r => r.Name == "祈雨").Scene == (dos ? 258 : 228), "late DOS scene reorder is not an offset from Win95");
            foreach (var rule in rules.Where(r => r.Kind == "battle" || r.Kind == "battle-end" || r.Kind == "completion"))
            {
                var battle = new SdlpalBattleEvent { scene = rule.Scene.Value, team = rule.Team.Value, enemies = new[] { rule.Enemy.Value }, result = 3 };
                Check(rule.Matches(battle), "audited classic battle matches " + rule.Name);
                battle.scene++; Check(!rule.Matches(battle), "same enemy in another scene cannot split " + rule.Name);
                battle.scene--; battle.result = 1;
                Check(rule.Matches(battle) == (rule.Kind == "battle-end"), "Caiyi alone accepts non-victory battle end");
            }
            var ending = rules.Last();
            Check(ending.Scene == (dos ? 281 : 282) && ending.Team == 313 && ending.Enemy == 546,
                "classic final battle uses Baiyue instead of the Dream final boss");
            var tower = rules.Single(r => r.Name == "进锁妖塔");
            var state = Snapshot(); state.profile = profile; state.started = true;
            foreach (var location in tower.Alternatives)
            {
                state.scene = location.Scene; state.viewportX = location.X ?? 0; state.viewportY = location.Y ?? 0;
                Check(tower.MatchesState(state), "each native tower alternative completes the same single node");
                if (location.X.HasValue) { state.viewportX += location.Radius.Value * 16 + 1; Check(!tower.MatchesState(state), "tower position bounds are exact"); }
            }
            var changed = SdlpalBridge.Json().Deserialize<SdlpalSplitRule[]>(SerializeFixture(rules));
            changed.Single(r => r.Name == "进锁妖塔").Alternatives = tower.Alternatives.Reverse().ToArray();
            Check(SdlpalSplitRule.Identity(rules) == SdlpalSplitRule.Identity(changed), "OR entry order is not a new route");
            changed.Single(r => r.Name == "水灵珠").ReturnScene++;
            Check(SdlpalSplitRule.Identity(rules) != SdlpalSplitRule.Identity(changed), "water-pearl return scene changes route identity");
            changed.Single(r => r.Name == "进锁妖塔").Alternatives = new[] { new SdlpalLocationRule { Scene = 3, X = 1 } };
            Reject(() => SdlpalSplitRule.Validate(changed), "partial alternative coordinates are rejected");

            var pearl = rules.Single(r => r.Name == "水灵珠");
            Func<SdlpalSnapshot> exchange = () => {
                var s = Snapshot(); s.profile = profile; s.started = true; s.scene = pearl.Scene.Value;
                s.partyOffsetX = 160; s.partyOffsetY = 112; s.partyDirection = 3;
                s.viewportX = 1072 - 160; s.viewportY = 1608 - 112;
                s.inventory = new[] { new SdlpalInventory { id = 265, amount = 0 } }; return s;
            };
            var observer = new SdlpalRunObserver(); state = exchange(); observer.Observe(Clone(state), rules);
            state.inventory[0].amount = 1; observer.Observe(Clone(state), rules);
            Check(observer.Reached.Contains(pearl.Id), "manual exchange requires a count increase in the correct facing range");
            observer = new SdlpalRunObserver(); state = exchange(); state.partyDirection = 1;
            observer.Observe(Clone(state), rules); state.inventory[0].amount = 1; observer.Observe(Clone(state), rules);
            Check(!observer.Reached.Contains(pearl.Id), "wrong facing cannot claim an exchange merely from owning the pearl");
            observer = new SdlpalRunObserver(); state = exchange(); observer.Observe(Clone(state), rules);
            state.epoch++; state.loadedFrom = 1; state.inventory[0].amount = 1; observer.Observe(Clone(state), rules);
            Check(!observer.Reached.Contains(pearl.Id), "load epoch rebases pearl count without fabricating an exchange");
            observer = new SdlpalRunObserver(); state = exchange(); state.partyDirection = null;
            observer.Observe(Clone(state), rules); state.inventory[0].amount = 1; observer.Observe(Clone(state), rules);
            Check(!observer.Reached.Contains(pearl.Id), "legacy snapshot lacking facing does not guess a water-pearl checkpoint");
            state.partyDirection = 0; state.scene = pearl.ReturnScene.Value;
            state.viewportX = 1168 - 160; state.viewportY = 760 - 112;
            observer.Observe(Clone(state), rules);
            Check(observer.Reached.Contains(pearl.Id), "return to Dali with pearl follows the original fallback gate");
            using (var editor = new SdlpalRulesForm(rules, state))
            {
                var bar = editor.Controls.OfType<System.Windows.Forms.FlowLayoutPanel>().Single();
                var button = bar.Controls.OfType<System.Windows.Forms.Button>().Single(b => b.Text == "验证并保存");
                typeof(System.Windows.Forms.Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty });
                Check(editor.Rules != null && SdlpalSplitRule.Identity(editor.Rules) == SdlpalSplitRule.Identity(rules),
                    "actual route editor roundtrip retains composite gates");
            }
        }
        Reject(() => SdlpalDreamRoutes.ForProfile("future-unknown-v1"), "unknown content never inherits the Dream route");
        foreach (string key in new[] { "partyOffsetX", "partyOffsetY", "partyDirection" })
        {
            var s = Snapshot(); var fields = SdlpalBridge.Json().Deserialize<Dictionary<string, object>>(SerializeFixture(s));
            fields[key] = "1"; byte[] payload = Encoding.UTF8.GetBytes(SerializeFixture(fields));
            Reject(() => SdlpalBridge.Decode(Header(s, payload.Length), payload, 777, 123, 10000), "party coordinates never coerce text: " + key);
        }
    }
    static void BestConfigurationTests()
    {
        string[] profiles={"geshi-easy-v1","dream222-normal-v1","dream222-hard-v1","dreamspace39x-hard-v1","dream22-puppet100-v1"};
        Action<SdlpalSnapshot>[] changes={
            s=>{s.monsterRush=true;s.respawnRule="pal98-current-scene-v1";},
            s=>{s.invisibleMonsters=true;s.respawnRule="pal98-current-scene-v1";},
            s=>s.respawnRule="pal98-current-scene-v1",
            s=>s.resourceHashes["DATA.MKF"]=new string('e',64)
        };
        foreach(string profile in profiles) foreach(var change in changes)
        using(var run=new CompletionFixture(true))
        {
            run.Engine.State.profile=profile;run.StartNewGame();
            string path=run.Core.ActiveBestPath;var original=Clone(run.Engine.State);
            try
            {
                run.Core.SaveBest();byte[] saved=File.ReadAllBytes(path);
                change(run.Engine.State);run.Tick();string other=run.Core.ActiveBestPath;
                Check(other!=path,"changed content/effect selects a different best file: "+profile);
                Reject(()=>run.Core.SaveBest(),"mixed configuration cannot overwrite the selected configuration PB: "+profile);
                Check(!File.Exists(other),"rejected mixed save creates no foreign best file: "+profile);
                run.Engine.State=original;run.Tick();
                Reject(()=>run.Core.SaveBest(),"returning to the original configuration retains mixed-run protection: "+profile);
                Check(run.Core.ActiveBestPath==path&&File.ReadAllBytes(path).SequenceEqual(saved),"original best bytes survive configuration round trip: "+profile);
                string mixed=run.Core.GetRStr();var imported=Call(run.Core,"ValidateImport",mixed,run.Core.CurrentSnapshot.IdentityKey);
                var snapshot=run.Core.CurrentSnapshot;run.Core.Reset();Call(run.Core,"Restore",imported,snapshot);
                Reject(()=>run.Core.SaveBest(),"timeline/relay import retains mixed-configuration PB protection: "+profile);
                Check(run.Core.IsUIPause,"best-line protection keeps import pause semantics: "+profile);
                run.Core.Reset();run.Tick();run.Core.SaveBest();
                Check(File.Exists(path),"explicit reset allows saving the current configuration PB: "+profile);
                var legacy=SdlpalBridge.Json().Deserialize<Dictionary<string,object>>(run.Core.GetRStr());
                legacy.Remove("SdlpalBestConfigurationChanged");
                Call(run.Core,"ValidateImport",SerializeFixture(legacy),run.Core.CurrentSnapshot.IdentityKey);
                Check(true,"old timeline without configuration-change metadata remains readable: "+profile);
                legacy["SdlpalBestConfigurationChanged"]="false";
                Reject(()=>Call(run.Core,"ValidateImport",SerializeFixture(legacy),run.Core.CurrentSnapshot.IdentityKey),
                    "non-boolean configuration-change metadata is rejected: "+profile);
            }
            finally {File.Delete(path);}
        }
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(string profile in profiles) using(var run=new CompletionFixture(true))
        {
            run.Engine.State.profile=profile;run.Tick();
            run.Form.rr.ClearAllItem();
            foreach(var point in run.Core.CheckPoints) point.SetUIItem(run.Form.rr.AddItem(point.GetNickName(),point.Best));
            var files=new List<string>();var bytes=new List<byte[]>();
            Action<int> select=mode=>{
                run.Engine.State.wuqiang=(mode&1)!=0;run.Engine.State.monsterRush=(mode&2)!=0;run.Engine.State.invisibleMonsters=(mode&4)!=0;
                run.Engine.State.respawnRule=(mode&6)!=0?"pal98-current-scene-v1":null;
                run.Tick();
            };
            try
            {
                for(int mode=0;mode<8;mode++)
                {
                    run.Core.Reset();select(mode);
                    for(int i=0;i<run.Core.CheckPoints.Count;i++)
                    {
                        run.Core.CheckPoints[i].Current=TimeSpan.FromSeconds(100+mode*10+i);
                        run.Core.CheckPoints[i].NickName="PB "+profile+" / "+mode+" / "+i;
                    }
                    run.Core.SaveBest();string path=run.Core.ActiveBestPath;
                    Check(paths.Add(path),"each profile/effect combination has an independent best file: "+profile+" / "+mode);
                    files.Add(path);bytes.Add(File.ReadAllBytes(path));
                }
                foreach(int mode in Enumerable.Range(0,8).Reverse().Concat(Enumerable.Range(0,8)))
                {
                    select(mode);
                    Check(run.Core.ActiveBestPath==files[mode]&&run.Core.CheckPoints.Select((point,i)=>
                        point.Best==TimeSpan.FromSeconds(100+mode*10+i)&&point.GetNickName()=="PB "+profile+" / "+mode+" / "+i).All(ok=>ok),
                        "automatic round-trip switch restores every best time and label: "+profile+" / "+mode);
                    Check(run.Core.CheckPoints.Select((point,i)=>run.Form.rr.GetItem(i).Best==point.Best&&run.Form.rr.GetItem(i).Name==point.GetNickName()).All(ok=>ok),
                        "rendered best references follow the selected configuration: "+profile+" / "+mode);
                    var bestClear=(TimeSpan)typeof(TimerCore).GetField("BestClear",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(run.Core);
                    Check(bestClear==run.Core.CheckPoints.Last().Best,"predicted finish baseline follows the selected configuration: "+profile+" / "+mode);
                    run.Engine.State.speed=mode;run.Engine.State.battleSpeed=7-mode;run.Engine.State.walkFrameMs=30+mode;run.Engine.State.fadeCycleMs=400+mode;
                    run.Tick();
                    Check(run.Core.ActiveBestPath==files[mode]&&run.Core.CheckPoints[0].Best==TimeSpan.FromSeconds(100+mode*10),
                        "speed changes keep the same best file and reference: "+profile+" / "+mode);
                }
                Check(files.Select((path,i)=>File.ReadAllBytes(path).SequenceEqual(bytes[i])).All(ok=>ok),
                    "all configuration best files remain byte-identical after switching: "+profile);
            }
            finally {foreach(string path in files) File.Delete(path);}
        }
        Check(paths.Count==profiles.Length*8,"profile and effect facts always produce independent best references, including synthetic unsupported combinations");
    }
    static void SplitDisplayTests()
    {
        string output=Path.Combine(root,"split-display");Directory.CreateDirectory(output);
        foreach(string profile in new[]{"geshi-easy-v1","dream222-normal-v1","dream222-hard-v1","dreamspace39x-hard-v1","dream22-puppet100-v1"})
        using(var engine=new Engine())
        using(var control=new System.Windows.Forms.Form { ClientSize=new System.Drawing.Size(346,916) })
        using(var board=new GBoard())
        {
            // Real OnTick -> BeginInvoke -> GRender binding, without GForm
            // startup, hooks, a network client, or a visible/game window.
            var handle=control.Handle;
            var form=(GForm)FormatterServices.GetUninitializedObject(typeof(GForm));
            for(Type type=typeof(System.Windows.Forms.Control);type!=null;type=type.BaseType)
                foreach(var field in type.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly))
                    field.SetValue(form,field.GetValue(control));
            form.IsNonSequentialCheck=true;
            form.rr=new GRender(control);form.rr.SetGBoard(board);
            form.rr.SetTitle("分段显示回归");form.rr.SetVersion(GForm.CurrentVersion);
            var core=new SdlpalGeshi(form);Set(form,"core",core);
            core.OnCurrentStepChanged=index=>form.rr.ItemIdx=index;
            form.InitCheckPoints();
            var bridge=new SdlpalBridge();bridge.Attach(Process.GetProcessById(engine.Process.Id));Set(core,"bridge",bridge);
            var watch=(PTimer)typeof(TimerCore).GetField("MT",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(core);
            Action tick=()=>{engine.Publish();Call(core,"OnTick");};
            Action drain=()=>{
                if(typeof(System.Windows.Forms.Control).GetField("threadCallbackList",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form)!=null)
                    typeof(System.Windows.Forms.Control).GetMethod("InvokeMarshaledCallbacks",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,null);
            };
            Func<int,string,int[]> render=(index,name)=>{
                form.rr.SetGameVersion(core.GetGameVersion());form.rr.SetMainTimer(core.GetMainWatch());form.rr.Draw();
                var bitmap=(System.Drawing.Bitmap)control.BackgroundImage;
                bitmap.Save(Path.Combine(output,profile+"-"+name+".png"));
                var area=(System.Drawing.Rectangle)typeof(GRender).GetField("rcItems",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form.rr);
                var pixels=new List<int>();
                for(int y=area.Top+index*board.ItemHeight;y<area.Top+(index+1)*board.ItemHeight;y++)
                    for(int x=area.Right-110;x<area.Right;x++) pixels.Add(bitmap.GetPixel(x,y).ToArgb());
                return pixels.ToArray();
            };
            try
            {
                engine.State.profile=profile;tick();engine.State.started=true;tick();
                watch.Stop();watch.SetTS(TimeSpan.FromSeconds(90));tick();core.IsUIPause=true;tick();
                var elapsed=core.GetMainWatch();drain();
                bool synchronized=form.rr.ItemIdx==0&&form.rr.GetItem(0).IsActive&&form.rr.GetItem(0).Cur==core.CheckPoints[0].Current;
                var pixels=render(0,"active");
                Check(core.CurrentStep==0&&core.CheckPoints[0].Current.TotalSeconds>=90,"actual core has a running split before display binding: "+profile);
                Check(synchronized,
                    "right-hand current split is active and synchronized after queued content binding: "+profile);
                Check(pixels.Distinct().Count()>4,"actual right-hand split region contains rendered time glyphs: "+profile);
                Check(core.GetMainWatch()==elapsed&&core.IsUIPause,"display binding preserves paused elapsed time: "+profile);
                core.IsUIPause=false;watch.SetTS(TimeSpan.FromSeconds(112));tick();core.IsUIPause=true;tick();
                Check(!pixels.SequenceEqual(render(0,"advanced")),"right-hand time glyphs update when the clock advances: "+profile);

                // Advance the first node through its actual route predicate.
                core.IsUIPause=false;
                if(profile=="geshi-easy-v1") engine.State.battleEvents=new[]{Victory(1)};
                else {engine.State.scene=5;engine.State.viewportX=1088;engine.State.viewportY=1088;}
                tick();core.IsUIPause=true;tick();var completed=core.CheckPoints[0].Current;
                Check(core.CurrentStep==1&&form.rr.GetItem(0).IsPassed&&form.rr.GetItem(1).IsActive,
                    "completed split stays visible and following split becomes active: "+profile);
                Check(render(0,"completed").Distinct().Count()>4&&form.rr.GetItem(0).Cur==completed,
                    "completed right-hand time is retained: "+profile);
                // Switch back while paused before the queued bind, with both
                // old and new renderer indices equal to zero.
                engine.State.profile=profile=="geshi-easy-v1"?"dream222-normal-v1":"geshi-easy-v1";
                engine.State.scene=0;engine.State.battleEvents=new SdlpalBattleEvent[0];tick();
                watch.Stop();watch.SetTS(TimeSpan.FromSeconds(34));tick();core.IsUIPause=true;tick();drain();
                synchronized=form.rr.ItemIdx==0&&form.rr.GetItem(0).IsActive&&form.rr.GetItem(0).Cur==core.CheckPoints[0].Current;
                render(0,"switched");
                Check(synchronized,
                    "switching content restores the same-index active split: "+profile);
                // A slow UI can receive the binding callback after a split has
                // already completed. It must retain both completed and live rows.
                engine.State.profile=profile;tick();watch.Stop();watch.SetTS(TimeSpan.FromSeconds(45));
                if(profile=="geshi-easy-v1") engine.State.battleEvents=new[]{Victory(1)};
                else {engine.State.scene=5;engine.State.viewportX=1088;engine.State.viewportY=1088;}
                tick();core.IsUIPause=true;tick();drain();
                Check(core.CurrentStep==1&&form.rr.ItemIdx==1&&form.rr.GetItem(0).IsPassed&&form.rr.GetItem(1).IsActive&&
                    form.rr.GetItem(0).Cur==core.CheckPoints[0].Current&&form.rr.GetItem(0).Status==CheckPointStatus.Completed&&
                    form.rr.GetItem(1).Cur==core.CheckPoints[1].Current&&form.rr.GetItem(1).Status==CheckPointStatus.InProgress,
                    "late UI binding retains completed/current times and statuses: "+profile);
                Check(render(0,"late-bind").Distinct().Count()>4,"late-bound completed time is actually rendered: "+profile);
            }
            finally
            {
                core.Unload();drain();
                var graphics=(System.Drawing.Graphics)typeof(GRender).GetField("CG",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form.rr);
                if(graphics!=null) graphics.Dispose();if(control.BackgroundImage!=null) control.BackgroundImage.Dispose();
            }
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            typeof(TimerUserSettings).GetField("store",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,new TimerSettingsStore(root,Path.Combine(root,"localappdata"),_=>{}));
            TimerUserSettings.ClearPathCache();SoundConfig.ins.GlobalEnabled=false;
            bool noGui=args.Contains("--no-gui");args=args.Where(arg=>arg!="--no-gui").ToArray();
            if(args.Contains("--split-display-only")) {SplitDisplayTests();Console.WriteLine("PASS total="+count);return 0;}
            if(args.Contains("--best-configuration-only")) {BestConfigurationTests();Console.WriteLine("PASS total="+count);return 0;}
            if(args.Contains("--throw-formula-only")) {DreamspaceThrowTests();Console.WriteLine("PASS total="+count);return 0;}
            if(args.Contains("--native-snapshots-only")) {DecodeTests(args.Where(arg=>arg!="--native-snapshots-only").ToArray());Console.WriteLine("PASS total="+count);return 0;}
            if(noGui&&args.Contains("--ui")) throw new ArgumentException("--no-gui cannot be combined with --ui");
            if(args.Length==1&&args[0]=="--ui")
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                var state=Snapshot();state.battle.active=true;state.battle.team=163;
                using(var editor=new SdlpalRulesForm(SdlpalSplitRule.Defaults(),state))
                    if(editor.ShowDialog()==System.Windows.Forms.DialogResult.OK)
                        File.WriteAllText(Path.Combine(root,"ui-rules-result.json"),SerializeFixture(editor.Rules));
                var entry=new Dx9OverlayTimelineEntry("八头蛇胜利","01:00:00.00","00:58:10.50",-110,false,true);
                var layout=Dx9OverlayLayoutSettings.CreateDefault();layout.HasWindowPosition=true;layout.WindowLeft=280;layout.WindowTop=180;
                using(var overlay=new Dx9OverlayForm(()=>new Dx9OverlaySnapshot(IntPtr.Zero,"Microsoft YaHei UI","01:02:03.45","12.3s","00:02:01.00","隔世追凶（简单）  金钱 10000",1,entry,
                    new Dx9OverlayTimelineEntry("通关","01:20:00.00","01:02:03.45",0,true,false),new Dx9OverlayTimelineEntry(),"离线界面验收",false,false,"SDLPAL / geshi-easy-v1"),
                    layout,_=>{}))
                { overlay.Text="SDLPAL OBS 离线界面验收";overlay.Start();overlay.BeginEditMode();System.Windows.Forms.Application.Run(overlay); }
                return 0;
            }
            Check(AutomationArgs.Parse(new[]{"timer","--core","sdlpal-geshi-easy"}).RequestedCore=="SdlpalGeshi","explicit core startup alias");
            Reject(()=>AutomationArgs.Parse(new[]{"timer","--core","unknown"}),"unknown core does not silently fall back");
            GameLoadResumeTests();IsolationTests();RankingTests();GameplayRuleTests();DreamspaceThrowTests();DecodeTests(args);WuQiangTests();ReleaseTitleTests();ObserverTests();ItemTests();IntegrationTests();MaintenanceSaveIsolationTests();BestReferenceTests();SpeedConfigurationTests();BattleClockTests();ReconnectTests();OnlineSnapshotBridgeTests();CompletionTests();PauseHotkeyTests();PluginCoreSwitchTests();ItemCoreTests(!noGui);OverlayIdentityTests();MaintenanceProfiles();SplitDisplayTests();BestConfigurationTests();Console.WriteLine("PASS total="+count);return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
