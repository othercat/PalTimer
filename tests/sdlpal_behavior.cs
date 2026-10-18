// SPDX-License-Identifier: GPL-2.0-only
using Pal98Timer;
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
    static SdlpalSnapshot Clone(SdlpalSnapshot value) => SdlpalBridge.Json().Deserialize<SdlpalSnapshot>(SdlpalBridge.Json().Serialize(value));
    static void RankingTests()
    {
        var initial=Snapshot();
        Func<SdlpalSnapshot,string> category=s=>RankingConfiguration.Digest(s.RankingFacts());
        string original=category(initial);
        var changed=Clone(initial);changed.exeHash=new string('f',64);changed.build="another-engine-build";
        changed.gamePath="another-window-location";changed.savePath="other-saves";
        Check(original==category(changed),"online configuration survives engine update and installation paths");
        foreach(var edit in new Action<SdlpalSnapshot>[] {s=>s.fadeCycleMs++,s=>s.walkFrameMs++,s=>s.speed++,s=>s.battleSpeed++})
        {changed=Clone(initial);edit(changed);Check(original!=category(changed),"actual speed change partitions online configuration");}
        foreach(string name in initial.resourceHashes.Keys)
        {changed=Clone(initial);changed.resourceHashes[name]=new string('e',64);Check(original!=category(changed),"gameplay resource change partitions configuration: "+name);}
        var facts=initial.RankingFacts();
        Check(facts.Count==12&&!facts.ContainsKey("engine_sha256")&&!facts.ContainsKey("engine_build")&&!facts.ContainsKey("route_rules_sha256")&&!facts.ContainsKey("timer_exe_sha256"),
            "only content and speed enter ranking facts; signatures and node routes remain independent");
        var game=new GameplayIdentity {family="sdlpal-geshi-easy",content_id=initial.profile,content_sha256=facts["content"],rules_sha256=original,fade_ms=800,map_speed_ticks=0,walk_frame_ms=90};
        Func<bool> valid=()=>new RankingConfiguration {covered=true,rules=facts,configuration_id=RankingConfiguration.Digest(facts)}.Valid(game);
        Check(valid(),"content-speed ranking validates");
        facts["WindowWidth"]="1920";Check(!valid(),"display setting cannot leak into accepted ranking facts");facts.Remove("WindowWidth");
        facts["engine_sha256"]=initial.exeHash;Check(!valid(),"partial legacy identity is rejected");
        facts["engine_build"]=initial.build;facts["route_rules_sha256"]=new string('e',64);
        Check(valid(),"complete old configuration remains readable without rewriting its digest");
        facts["resource.DATA.MKF"]=new string('e',64);Check(!valid(),"mismatched resource aggregate rejected");
    }
    static void DecodeTests(string[] paths)
    {
        var state=Snapshot(); byte[] json=Encoding.UTF8.GetBytes(SdlpalBridge.Json().Serialize(state)); var header=Header(state,json.Length);
        Check(SdlpalBridge.Decode(header,json,777,123,10000).profile=="geshi-easy-v1","valid versioned snapshot");
        Reject(()=>SdlpalBridge.Decode(header,json,778,123,10000),"foreign PID rejected");
        Reject(()=>SdlpalBridge.Decode(header,json,777,124,10000),"reused PID creation time rejected");
        Reject(()=>SdlpalBridge.Decode(header,json,777,123,12001),"stale snapshot rejected");
        header[20]=3; Reject(()=>SdlpalBridge.Decode(header,json,777,123,10000),"odd seqlock rejected"); header[20]=2;
        header[8]=2; Reject(()=>SdlpalBridge.Decode(header,json,777,123,10000),"unknown protocol rejected"); header[8]=1;
        state.verified=false; json=Encoding.UTF8.GetBytes(SdlpalBridge.Json().Serialize(state)); Reject(()=>SdlpalBridge.Decode(Header(state,json.Length),json,777,123,10000),"unverified resources rejected");
        state=Snapshot(); string missing=SdlpalBridge.Json().Serialize(state).Replace("\"diagnostic\":true,",""); json=Encoding.UTF8.GetBytes(missing);
        Reject(()=>SdlpalBridge.Decode(Header(state,json.Length),json,777,123,10000),"missing diagnostic flag rejected");
        state=Snapshot();state.started=true;state.inventory=new[]{new SdlpalInventory{slot=0,id=61,amount=4}};
        json=Encoding.UTF8.GetBytes(SdlpalBridge.Json().Serialize(state).Replace("\"amount\":4,",""));
        var optional=SdlpalBridge.Decode(Header(state,json.Length),json,777,123,10000);var unavailable=new SdlpalItemTracker();unavailable.Observe(optional);
        Check(optional.verified&&!unavailable.View.Available,"missing nested item quantity disables item data without fabricating zero or blocking timing identity");
        foreach(string path in paths)
        {
            json=File.ReadAllBytes(path); state=SdlpalBridge.Json().Deserialize<SdlpalSnapshot>(new UTF8Encoding(false,true).GetString(json));
            var actual=SdlpalBridge.Decode(Header(state,json.Length),json,777,123,10000);
            Check(actual.profile=="geshi-easy-v1"&&actual.diagnostic,"real native diagnostic snapshot decodes: "+Path.GetFileName(path));
            var items=new SdlpalItemTracker();items.Observe(actual);
            Check(items.View.Available&&items.View.Rows.Any(row=>row.Id==585)&&!items.View.Rows.Any(row=>row.Id==571||row.Id==575),"native item catalog includes modified items and excludes initial magics: "+Path.GetFileName(path));
            Check(items.View.Rows.Single(row=>row.Id==61).ResourcePrice==150&&new[]{"銅錢鏢","铜钱镖"}.Contains(items.View.Rows.Single(row=>row.Id==344).Name),"native item names/prices come from current resources");
            Check(actual.scene==227 ? items.View.Cash==31265&&items.View.Kinds==80&&items.View.Quantity==355 :
                actual.scene==93&&items.View.Cash==9817&&items.View.Kinds==107&&items.View.Quantity==458,"native save ownership/cash totals (all six equipment slots, active party only)");
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
        run=new SdlpalRunObserver();state=Snapshot();state.started=true;state.battleEvents=new[]{Victory(1)};run.Observe(Clone(state),rules);
        Check(!run.BeganHere&&!run.Reached.Contains(rules[0].Id),"late attach cannot consume historical victory");
        state.battleEvents=new[]{Victory(3)};run.Observe(Clone(state),rules);Check(run.Issue.Contains("缺口")&&!run.Reached.Contains(rules[0].Id),"ring gap reports and rebuilds without inventing split");
        state.walkFrameMs++;run.Observe(Clone(state),rules);Check(run.Issue.Contains("变化"),"speed change retains an unverified observation");
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
            byte[] json=Encoding.UTF8.GetBytes(SdlpalBridge.Json().Serialize(State));byte[] header=Header(State,json.Length,Process.Id,(ulong)Process.StartTime.ToUniversalTime().ToFileTimeUtc(),SdlpalBridge.GetTickCount64());
            uint seq=View.ReadUInt32(20)+2;View.Write(20,seq|1);BitConverter.GetBytes(seq|1).CopyTo(header,20);View.WriteArray(0,header,0,header.Length);View.WriteArray(64,json,0,json.Length);Thread.MemoryBarrier();View.Write(20,seq&~1U);
        }
        internal Task Reply(uint expectedCommand, uint status=1, byte[] expectedLoad=null)
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
                    Publish();
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
            Reject(()=>pack.Validate("wrong profile"),"relay wrong engine/resource identity rejected");
            pack.saveSha256=new string('f',64);Reject(()=>pack.Validate(snapshot.IdentityKey),"relay corrupt hash rejected");
            byte[] load=Enumerable.Repeat((byte)42,2048).ToArray();var loadReply=engine.Reply(2,1,load);var loaded=SdlpalControl.Execute(bridge,2,snapshot,load);loadReply.GetAwaiter().GetResult();
            Check(loaded.Snapshot.epoch==2&&!loaded.Snapshot.canControl,"LOAD accepts matching ack during fade without requiring canControl");
            Check(Directory.GetFiles(engine.State.savePath,"250.rpg.backup-*").Length==1,"staging preserves prior reserved-slot file backup");
            Reject(()=>SdlpalControl.Execute(bridge,2,snapshot,load),"outdated epoch cannot issue LOAD");
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
            Check(keyConfig.WorkingDirectory==executableDirectory && keyConfig.FileName==Path.Combine(executableDirectory,"LostEditKeyConfig.exe") &&
                keyConfig.WorkingDirectory!=engine.State.gamePath,"F11 locates key configuration beside the verified executable, independently of resources");
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
                imported=Call(core,"ValidateImport",SdlpalBridge.Json().Serialize(timeline),core.CurrentSnapshot.IdentityKey);
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
            Check(observation.Ranking.rules.Count==12&&!observation.Ranking.rules.ContainsKey("engine_sha256"),"actual core publishes content-speed category");
            Set(core,"relayRun",core.ScoreRunSequence);Set(core,"relayBridge",bridge);Call(core,"CheckRelayOwner");
            core.SetTS(TimeSpan.FromSeconds(1));Reject(()=>Call(core,"CheckRelayOwner"),"manual time/reset cancels an earlier relay transaction lease");
            Check(!core.CanBeginCompetitionHere(0),"manual time change cannot claim a fresh competition start");
            Set(core,"relayRun",core.ScoreRunSequence);core.Jump(0);Check(!core.CanBeginCompetitionHere(0),"manual node rewind remains assisted");
            Reject(()=>Call(core,"CheckRelayOwner"),"manual node jump invalidates an earlier relay transaction lease");
            string old=core.ActiveBestPath;engine.State.walkFrameMs=80;engine.Publish();Call(core,"OnTick");Check(old!=core.ActiveBestPath,"speed configuration partitions local best lines");
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
            Check(run.Core.IsUIPause&&!run.Watch.IsRunning&&battle.CurrentTS<TimeSpan.FromSeconds(1)&&battle.IsRunning==!excludePause,
                "load epoch cannot carry the old battle time, even when the sequence is reused");
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
            string complete=SdlpalBridge.Json().Serialize(reference);
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
                run.Engine.State.walkFrameMs=80;run.Tick();
                Check(path!=run.Core.ActiveBestPath&&!File.Exists(run.Core.ActiveBestPath),"category B has no reference file");
                assertCleared();
                run.Engine.State.walkFrameMs=90;run.Tick();
                Check(run.Core.CheckPoints[1].Best==TimeSpan.FromSeconds(120)&&run.Core.CheckPoints[1].NickName=="Reference A 1",
                    "switching back restores category A's reference");
                reference["CheckPoints"]=new[]{new{name=run.Core.CheckPoints[0].Name,des="Partial",time="00:00:30.000"}};
                File.WriteAllText(path,SdlpalBridge.Json().Serialize(reference));run.Core.RefreshBestReference();
                Check(run.Core.CheckPoints[0].Best==TimeSpan.FromSeconds(30)&&run.Core.CheckPoints[0].NickName=="Partial"&&
                    run.Core.CheckPoints[1].Best==TimeSpan.Zero&&run.Core.CheckPoints[1].GetNickName()==defaults[1],
                    "a partial reference resets omitted checkpoints to route defaults");
                reference["CheckPoints"]=new object[]{new{name=run.Core.CheckPoints[0].Name,des="Invalid partial",time="00:00:40.000"},null};
                string malformed=SdlpalBridge.Json().Serialize(reference);File.WriteAllText(path,malformed);
                Reject(()=>run.Core.RefreshBestReference(),"an invalid reference after a valid first point is reported");assertCleared();
                Check(File.ReadAllText(path)==malformed,"invalid reference input remains available for repair");
                File.WriteAllText(path,complete);run.Core.RefreshBestReference();
                reference["SdlpalCategory"]="wrong-category";File.WriteAllText(path,SdlpalBridge.Json().Serialize(reference));
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
                var imported=Call(run.Core,"ValidateImport",SdlpalBridge.Json().Serialize(timeline),run.Core.CurrentSnapshot.IdentityKey);
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
    [STAThread] static int Main(string[] args)
    {
        try
        {
            typeof(TimerUserSettings).GetField("store",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,new TimerSettingsStore(root,Path.Combine(root,"localappdata"),_=>{}));
            TimerUserSettings.ClearPathCache();SoundConfig.ins.GlobalEnabled=false;
            bool noGui=args.Contains("--no-gui");args=args.Where(arg=>arg!="--no-gui").ToArray();
            if(noGui&&args.Contains("--ui")) throw new ArgumentException("--no-gui cannot be combined with --ui");
            if(args.Length==1&&args[0]=="--ui")
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                var state=Snapshot();state.battle.active=true;state.battle.team=163;
                using(var editor=new SdlpalRulesForm(SdlpalSplitRule.Defaults(),state))
                    if(editor.ShowDialog()==System.Windows.Forms.DialogResult.OK)
                        File.WriteAllText(Path.Combine(root,"ui-rules-result.json"),SdlpalBridge.Json().Serialize(editor.Rules));
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
            IsolationTests();RankingTests();DecodeTests(args);ObserverTests();ItemTests();IntegrationTests();BestReferenceTests();BattleClockTests();CompletionTests();PluginCoreSwitchTests();ItemCoreTests(!noGui);Console.WriteLine("PASS total="+count);return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
