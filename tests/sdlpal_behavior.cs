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
        focused = true, canControl = true, epoch = 1, fadeCycleMs = 800, walkFrameMs = 90, battle = new SdlpalBattle(), battleEvents = new SdlpalBattleEvent[0]
    };
    static byte[] Header(SdlpalSnapshot value, int length, int pid = 777, ulong created = 123, ulong now = 10000)
    {
        var header = new byte[64]; Encoding.ASCII.GetBytes("SDLPAL1\0").CopyTo(header,0);
        BitConverter.GetBytes(1U).CopyTo(header,8); BitConverter.GetBytes(64U).CopyTo(header,12); BitConverter.GetBytes((uint)SdlpalBridge.Capacity).CopyTo(header,16);
        BitConverter.GetBytes(2U).CopyTo(header,20); BitConverter.GetBytes(length).CopyTo(header,24); BitConverter.GetBytes(pid).CopyTo(header,28);
        BitConverter.GetBytes(created).CopyTo(header,32); BitConverter.GetBytes(value.epoch).CopyTo(header,40); BitConverter.GetBytes(now).CopyTo(header,56); return header;
    }
    static SdlpalSnapshot Clone(SdlpalSnapshot value) => SdlpalBridge.Json().Deserialize<SdlpalSnapshot>(SdlpalBridge.Json().Serialize(value));
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
        foreach(string path in paths)
        {
            json=File.ReadAllBytes(path); state=SdlpalBridge.Json().Deserialize<SdlpalSnapshot>(new UTF8Encoding(false,true).GetString(json));
            var actual=SdlpalBridge.Decode(Header(state,json.Length),json,777,123,10000);
            Check(actual.profile=="geshi-easy-v1"&&actual.diagnostic,"real native diagnostic snapshot decodes: "+Path.GetFileName(path));
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
            Check(!core.CompetitionPublicationAllowed&&core.GetScoreValidationError().Contains("诊断"),"diagnostic runtime cannot publish competition observations");
            var observation=new CompetitionObservation();core.CaptureCompetitionGameplay(observation);
            Check(observation.Gameplay.Valid&&observation.Ranking.Valid(observation.Gameplay)&&observation.Gameplay.map_speed_ticks==0&&observation.Gameplay.walk_frame_ms==90,"independent SDL family uses milliseconds without PALDLL ticks");
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
    [STAThread] static int Main(string[] args)
    {
        try
        {
            typeof(TimerUserSettings).GetField("store",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,new TimerSettingsStore(root,Path.Combine(root,"localappdata"),_=>{}));
            TimerUserSettings.ClearPathCache();SoundConfig.ins.GlobalEnabled=false;
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
            DecodeTests(args);ObserverTests();IntegrationTests();Console.WriteLine("PASS total="+count);return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
