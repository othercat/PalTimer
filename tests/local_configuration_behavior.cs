using Pal98Timer;
using HFrame.ENT;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class LocalConfigurationBehavior
{
    static int checks;
    static readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string local = Path.Combine(root, "isolated-localappdata");
    static readonly string routeA = CompetitionProtocol.Hash("route A"), routeB = CompetitionProtocol.Hash("route B");
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); ++checks; Console.WriteLine("PASS " + message); }
    static void Throws<T>(Action action, string message) where T : Exception
    { try { action(); } catch (T) { Check(true, message); return; } catch (TargetInvocationException e) when (e.InnerException is T) { Check(true, message); return; } throw new Exception(message); }
    static FieldInfo Field(object target, string name) { for (Type t=target.GetType();t!=null;t=t.BaseType) { var f=t.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.DeclaredOnly); if(f!=null)return f; } throw new Exception(name); }
    static void Set(object target, string name, object value) => Field(target,name).SetValue(target,value);
    static object Get(object target, string name) => Field(target,name).GetValue(target);
    static object Call(object target,string name,params object[] args) { for(Type t=target.GetType();t!=null;t=t.BaseType) { var m=t.GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly); if(m!=null)return m.Invoke(target,args); } throw new Exception(name); }
    static void Put(string directory,string path,string text) { string full=Path.Combine(directory,path.Replace('/',Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(full)); File.WriteAllText(full,text,new UTF8Encoding(false)); }
    static string Enc(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    static LocalConfigurationReader.Preset Preset(string ini="[Patch]\nDefaultPatch=Classic\n[Hardcore]\nEnabled=0\n") => new LocalConfigurationReader.Preset {
        schema="PAL98.ConfigurationPreset.v1",id=Guid.NewGuid().ToString("D"),name="同名配置",producer_version="1.7.2.1",settings_contract="PAL98.Settings.v1",minimum_runtime="1.7.2.1",minimum_timer="3.37.9.0",
        files=new SortedDictionary<string,string>(StringComparer.Ordinal) { {"config.ini",Enc(ini)}, {"dxwrapper.ini",Enc("[time]\nbattle_speed=10\n[dd7to9]\nDdrawOverrideWidth=640\n")}, {"Graphics/renderer-state.v1.json",Enc("{\"name\":\"before\"}")} },
        tournament=new SortedDictionary<string,string>(StringComparer.Ordinal),dependencies=new LocalConfigurationReader.Dependency[0] };
    static LocalConfigurationReader.Preset Clone(LocalConfigurationReader.Preset p) => LocalTimelineConfiguration.Json().Deserialize<LocalConfigurationReader.Preset>(LocalTimelineConfiguration.Json().Serialize(p));
    static void Publish(string directory,LocalConfigurationReader.Preset p,string filename="selected.pal98config",bool pointer=true)
    {
        p.files=new SortedDictionary<string,string>(p.files,StringComparer.Ordinal);p.tournament=new SortedDictionary<string,string>(p.tournament,StringComparer.Ordinal);
        p.content_sha256=LocalConfigurationReader.PresetDigest(p);
        Put(directory,"palmod/Configurations/"+filename,LocalTimelineConfiguration.Json().Serialize(p));
        if(pointer)Put(directory,LocalConfigurationReader.Pointer,LocalTimelineConfiguration.Json().Serialize(new { p.id,p.content_sha256,storage_key=filename }));
        foreach(var file in p.files) { string path=Path.Combine(directory,file.Key.Replace('/',Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path,Convert.FromBase64String(file.Value)); }
    }
    static LocalTimelineConfiguration Read(string directory,string route=null) => LocalConfigurationReader.ReadCurrent(directory,route??routeA,local);
    static Pal98Dx9Automatic Core() { var core=new Pal98Dx9Automatic(null); Call(core,"InitCheckPoints"); return core; }
    static void BindStorage(bool userMode)
    {
        if(userMode) Directory.CreateDirectory(Path.Combine(new Pal98.Storage.UserDataStore(root,local).DirectoryPath,"timer"));
        typeof(TimerUserSettings).GetField("store",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,new TimerSettingsStore(root,local,_=>{}));
        TimerUserSettings.ClearPathCache();
    }
    static void Normalization()
    {
        var p=Preset();var a=LocalConfigurationReader.FromPreset(p,routeA);var q=Clone(p);
        q.name="改名";q.files["music-new.ini"]=Enc("[MUSIC]\n001=another.mp3");q.files["key.ini"]=Enc("a=b");q.files["Graphics/renderer-state.v1.json"]=Enc("{\"name\":\"after\"}");
        q.files["dxwrapper.ini"]=Enc("[dd7to9]\nDdrawOverrideWidth=1920\nDdrawOverrideHeight=1080\n[d3d9]\nEnableWindowMode=1\n[time]\nbattle_speed=10\n");
        Check(a.key==LocalConfigurationReader.FromPreset(q,routeA).key,"rename, music, keys, graphics and window changes retain line");
        q=Clone(p);q.id=Guid.NewGuid().ToString("D");Check(a.key!=LocalConfigurationReader.FromPreset(q,routeA).key,"same display name with different configuration ID is isolated");
        foreach(string ini in new[]{"[time]\nbattle_speed=11","[time]\nbattle_speed=10\nskip_comeacross=1","[time]\nbattle_speed=10\nmap_speed=5","[time]\nbattle_speed=10\nfuture_gameplay_rule=1"}) {
            q=Clone(p);q.files["dxwrapper.ini"]=Enc(ini);Check(a.key!=LocalConfigurationReader.FromPreset(q,routeA).key,"speed and unknown gameplay options partition: "+ini.Replace('\n',' '));
        }
        q=Clone(p);q.files["config.ini"]=Enc("[Patch]\nDefaultPatch=Another\n[Hardcore]\nEnabled=0\n");Check(a.key!=LocalConfigurationReader.FromPreset(q,routeA).key,"gameplay profile partitions line");
        q=Clone(p);q.files["config.ini"]=Enc("[Patch]\nDefaultPatch=Classic\n[Hardcore]\nEnabled=1\n");var hard=LocalConfigurationReader.FromPreset(q,routeA);
        Check(hard.hardcore&&a.key!=hard.key,"ordinary and hardcore partition line");
        Check(a.key!=a.ForRoute(routeB).key,"node route partitions line");
        q=Clone(p);q.files["config.ini"]=Enc("[Patch]\nDefaultPatch='Classic' ; comment\n[Hardcore]\nEnabled=0 # another comment\n");Check(a.key==LocalConfigurationReader.FromPreset(q,routeA).key,"INI quotes and comments keep semantic identity");
        q.files["config.ini"]=Enc("[Hardcore]\nEnabled=1 ; on\n");Check(LocalConfigurationReader.FromPreset(q,routeA).hardcore,"hardcore inline comment does not change enabled value");
        q=Clone(p);q.files["palmod/random-skill-selection.v1.json"]=null;Check(LocalConfigurationReader.FromPreset(q,routeA).Valid,"nullable optional settings are accepted");
        q=Clone(p);q.files["config.ini"]=Enc("[Patch]\nDefaultPatch=Classic\n[Hardcore]\nEnabled=0\n[extra]\nRandomLevelUpSkillsSeed=12345\n");Check(a.key==LocalConfigurationReader.FromPreset(q,routeA).key,"per-run random skill seed does not create another best line");
        q=Clone(p);q.dependencies=new[]{new LocalConfigurationReader.Dependency{path="MUS.MKF",sha256=CompetitionProtocol.Hash("music"),size=1},new LocalConfigurationReader.Dependency{path="Midi/catalog.json",sha256=CompetitionProtocol.Hash("MIDI"),size=1}};Check(a.key==LocalConfigurationReader.FromPreset(q,routeA).key,"legacy MUS and Midi dependencies do not split line");
        q=Clone(p);q.files["copymen_scripts/test.rule"]=Enc("some new behavior");Check(a.key!=LocalConfigurationReader.FromPreset(q,routeA).key,"script content partitions line");
        q=Clone(p);q.files["future.json"]=Enc("{\"a\":1,\"b\":2}");var j=LocalConfigurationReader.FromPreset(q,routeA);q.files["future.json"]=Enc("{ \"b\": 2, \"a\": 1 }");Check(j.key==LocalConfigurationReader.FromPreset(q,routeA).key,"JSON formatting/order does not partition line");
        q.content_sha256=LocalConfigurationReader.PresetDigest(new LocalConfigurationReader.Preset {schema=q.schema,id=q.id,name=q.name,producer_version=q.producer_version,settings_contract=q.settings_contract,minimum_runtime=q.minimum_runtime,minimum_timer=q.minimum_timer,files=new SortedDictionary<string,string>(q.files,StringComparer.Ordinal),tournament=q.tournament,dependencies=q.dependencies});
        Check(LocalConfigurationReader.Parse(LocalTimelineConfiguration.Json().Serialize(q)).id==q.id,"real ConfigTool ordinal projection survives JSON deserialization");
        q.files["../escape.ini"]=Enc("invalid");Throws<InvalidDataException>(()=>LocalConfigurationReader.FromPreset(q,routeA),"traversal configuration path rejected");
    }
    static void EffectiveConfiguration()
    {
        string game=Path.Combine(root,"game-fixture");var p=Preset();Publish(game,p);var a=Read(game);
        Put(game,"config.ini","[Patch]\nDefaultPatch=Classic\n[Hardcore]\nEnabled=1\n");var hard=Read(game);
        Check(hard.hardcore&&hard.key!=a.key,"effective local hardcore overlay overrides stripped preset");
        Publish(game,p);Put(game,"MAP.MKF","different map");Check(Read(game).key!=a.key,"actual MAP resource participates even without server identity");
        var withMap=Read(game);Put(game,"music-new.ini","changed music");Check(Read(game).key==withMap.key,"live music change keeps line");
        Put(game,"copymen_scripts/new.rule","new live rules");Check(Read(game).key!=withMap.key,"new effective script outside container participates");
        var old=Clone(p);old.files["config.ini"]=Enc("[Patch]\nDefaultPatch=Old\n");Publish(game,old,"old.pal98config",false);Publish(game,p);
        Check(Read(game).configuration_id==p.id,"storage_key selects correct same-ID preset");
        var changed=Clone(p);changed.name="forged without checksum";Put(game,"palmod/Configurations/selected.pal98config",LocalTimelineConfiguration.Json().Serialize(changed));
        Throws<InvalidDataException>(()=>Read(game),"stale or edited container checksum rejected");Publish(game,p);
        string fallback=Path.Combine(new Pal98.Storage.UserDataStore(game,local).DirectoryPath,"game");Publish(fallback,p);
        Put(fallback,"config.ini","[Patch]\nDefaultPatch=UserOverlay\n");Check(Read(game).key!=a.key,"user-directory effective configuration has priority");
        File.Move(Path.Combine(fallback,"config.ini"),Path.Combine(fallback,"config.ini.missing"));Throws<InvalidDataException>(()=>Read(game),"missing user config never silently uses package config");
        File.Move(Path.Combine(fallback,"config.ini.missing"),Path.Combine(fallback,"config.ini"));
        Put(fallback,"hardcore-transaction.pending","unfinished");Throws<InvalidDataException>(()=>Read(game),"unfinished configuration transaction cannot become frozen identity");
    }
    static void ProfileResources()
    {
        string game=Path.Combine(root,"profile-fixture");var p=Preset();Publish(game,p);Put(game,"PAL.exe","fixture identity only - never executed");
        var json=LocalTimelineConfiguration.Json();
        Func<string,string,string> publishProfile=(version,contents)=> {
            var resource=new {kind="SSS.MKF",relative_path="resources/SSS.MKF",sha256=CompetitionProtocol.Hash(contents),size_bytes=Encoding.UTF8.GetByteCount(contents)};
            string descriptor=json.Serialize(new{schema="PAL98.GameProfile.v1",profile_id="test.profile",profile_version=version,features=new{battle_rule=version},resource_set=new[]{resource}});
            string hash=CompetitionProtocol.Hash(descriptor),stage="p/"+hash.Substring(0,32);
            Put(game,"palmod/Profiles/"+stage+"/manifest/game-profile.json",descriptor);Put(game,"palmod/Profiles/"+stage+"/resources/SSS.MKF",contents);
            Put(game,"patches/Classic.json",json.Serialize(new{manifest_version=2,patches=new[]{new{zip_file="Classic.zip",game_profile=new{schema="PAL98.GameProfileEnvelope.v1",profile_id="test.profile",profile_version=version,descriptor_sha256=hash}}}}));
            return json.Serialize(new{schema="PAL98.EffectiveGameProfilePointer.v1",profile_id="test.profile",profile_version=version,descriptor_sha256=hash,staging_relative_path=stage});
        };
        string first=publishProfile("1.0.0","first resource");Put(game,"palmod/Profiles/current.json",first);var a=Read(game);
        Put(game,"SSS.MKF","unused root changed");Check(Read(game).key==a.key,"profile overlay ignores unused root resource");
        Check(LocalConfigurationReader.FromFile(Path.Combine(game,"palmod/Configurations/selected.pal98config"),routeA).key==a.key,"manual current preset resolves the same effective identity as automatic reopening");
        string second=publishProfile("1.0.1","second resource");var pointer=json.Deserialize<Dictionary<string,object>>(second);string hash=(string)pointer["descriptor_sha256"];
        Put(game,"palmod/Profiles/updates/"+hash+".json",second);var update=Read(game);Check(update.key!=a.key,"same-profile immutable update pointer follows selected package");
        Put(game,"palmod/Profiles/current.json",second);Check(Read(game).key==update.key,"direct and update-pointer activation have identical effective identity");
        string stage=(string)pointer["staging_relative_path"];Put(game,"palmod/Profiles/"+stage+"/resources/SSS.MKF","changed actual resource");Check(Read(game).key!=update.key,"actual profile resource mutation partitions even with unchanged declared hash");
        Put(game,"palmod/Profiles/"+stage+"/manifest/game-profile.json","{}");Throws<InvalidDataException>(()=>Read(game),"invalid profile descriptor is not bound to another configuration");
        string detached=Path.Combine(root,"detached.pal98config");File.Copy(Path.Combine(game,"palmod/Configurations/selected.pal98config"),detached);
        Check(LocalConfigurationReader.FromFile(detached,routeA).source=="manual","detached file is explicitly a manual reference, never claims effective configuration");
    }
    static GameplaySnapshot Snapshot(string route)
    {
        string rules=CompetitionProtocol.Hash("recognized fixture");
        return new GameplaySnapshot {schema="PAL98.GameplayMode.v1",covered=true,rules_sha256=rules,content_id="classic-v5",content_sha256=CompetitionProtocol.Hash("content"),family="standard",fade_ms=1200,map_speed_ticks=10,rules=new Dictionary<string,string>(),OrdinaryTimelineId=TimelineIdentity.Create(rules,route,false),HardcoreTimelineId=TimelineIdentity.Create(rules,route,true)};
    }
    static void SaveAndReopen(bool userMode)
    {
        BindStorage(userMode);var core=Core();string route=(string)Get(core,"route");var a=LocalConfigurationReader.FromPreset(Preset(),route);
        core.SelectLocalConfiguration(a,false);core.SetTS(TimeSpan.FromSeconds(73));core.CheckPoints[0].SetCurrentTSForLoad(TimeSpan.FromSeconds(12));Set(core,"_CurrentStep",1);Set(core,"startedWithoutIdentity",true);Set(core,"configurationFrozen",true);
        string error=core.GetScoreValidationError();Check(error.Length>0,"unknown runtime has real score validation error");core.SaveBest();
        Check(File.Exists(a.BestPath)&&core.CheckPoints[0].Best==TimeSpan.FromSeconds(12),"unknown runtime saves and immediately compares in "+(userMode?"user directory":"package directory"));
        Check(core.GetScoreValidationError()==error&&((PTimer)Get(core,"MT")).CurrentTSOnly==TimeSpan.FromSeconds(73)&&core.CurrentStep==1&&core.CheckPoints[0].Current==TimeSpan.FromSeconds(12),"save preserves current timer, node, step and validation");
        var saved=new HObj(File.ReadAllText(a.BestPath));Check(saved.GetValue<bool>("ReferenceTimeline")&&!saved.GetValue<bool>("GameplayVerified")&&!saved.GetValue<bool>("TimingRulesVerified")&&!saved.GetValue<bool>("HardcoreRunVerified"),"local reference carries no certified result flags");
        var observation=new CompetitionObservation();core.CaptureCompetitionGameplay(observation);Check(observation.ValidationError.Length>0&&observation.Gameplay==null,"local save cannot create online gameplay evidence");
        saved["ReferenceTimeline"]=false;saved["GameplayVerified"]=true;Throws<InvalidDataException>(()=>Call(core,"ValidateTimerImport",saved.ToJson()),"local reference cannot restore relay even with forged verified marker");
        var reopened=Core();reopened.SelectLocalConfiguration(a,false);reopened.RefreshBestReference();Check(reopened.CheckPoints[0].Best==TimeSpan.FromSeconds(12),"reopening unknown same configuration loads best");
        var other=LocalConfigurationReader.FromPreset(Preset(),route);Throws<InvalidOperationException>(()=>core.SelectLocalConfiguration(other,true),"running configuration cannot switch after freeze");
        string path=core.ActiveBestPath;Call(core,"FreezeLocalConfiguration");core.SaveBest();Check(core.ActiveBestPath==path,"save after process exit keeps frozen config path");
        string legacyPath=TimelineIdentity.PathFor(root,Snapshot(route).OrdinaryTimelineId,true,"best.json");legacyPath=TimerUserSettings.TimelinePath(legacyPath);Directory.CreateDirectory(Path.GetDirectoryName(legacyPath));File.WriteAllText(legacyPath,"preserved legacy marker");
        Call(reopened,"SelectIdentity",Snapshot(route),false);reopened.RefreshBestReference();Check(reopened.ActiveBestPath==path&&reopened.CheckPoints[0].Best==TimeSpan.FromSeconds(12)&&File.ReadAllText(legacyPath)=="preserved legacy marker","restored identity keeps config reference and leaves old best untouched");
        var wrong=new HObj(File.ReadAllText(path));var points=wrong.GetValue<HObj>("CheckPoints");((HObj)points[0])["name"]="wrong node";
        Throws<InvalidDataException>(()=>Call(reopened,"ValidateBestReference",wrong),"stored route must match actual checkpoint names and order");
        string old=File.ReadAllText(path);core.CheckPoints[0].SetCurrentTSForLoad(TimeSpan.FromSeconds(20));
        using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))Throws<IOException>(()=>core.SaveBest(),"failed replace reports failure rather than success");
        Check(File.ReadAllText(path)==old&&core.CheckPoints[0].Current==TimeSpan.FromSeconds(20)&&core.CurrentStep==1&&Directory.GetFiles(Path.GetDirectoryName(path),"*.pending-*").Length>0,"failed write preserves original best, current run and recovery bytes");
        core.SaveBest();Check(core.CheckPoints[0].Best==TimeSpan.FromSeconds(20),"retry after releasing file refreshes best");
        Check(Directory.GetFiles(Path.GetDirectoryName(path),"best-*.json").Length>0,"replaced best has historical backup");
        var plain=new 仙剑98柔情DX9(null);Call(plain,"InitCheckPoints");Throws<InvalidOperationException>(()=>plain.SaveBest(),"fixed DX9 score gate remains enforced");
        var missing=Core();missing.SetTS(TimeSpan.FromSeconds(9));Throws<OperationCanceledException>(()=>missing.SaveBest(),"unreadable config needs explicit selection with run preserved");Check(((PTimer)Get(missing,"MT")).CurrentTSOnly==TimeSpan.FromSeconds(9),"cancelled configuration selection preserves current time");
        string before=missing.ActiveBestPath;var manual=LocalTimelineConfiguration.Manual("自选玩法",route,false);missing.SelectLocalConfiguration(manual,true);missing.SaveBest();
        Check(missing.ActiveBestPath!=before&&File.Exists(manual.BestPath),"explicit manual config can save unknown run");
        var sameName=LocalTimelineConfiguration.Manual("自选玩法",route,false);Check(sameName.key!=manual.key,"manual same-name configurations never collapse");
        Check(LocalTimelineConfiguration.Known(route).Any(c=>c.key==manual.key),"manual selection remains available after reopen");
        using(var dialog=new LocalTimelineConfigurationForm(route,manual)) {
            Check(dialog.Controls.Count>0&&dialog.AcceptButton!=null&&dialog.CancelButton!=null,"local config picker can initialize from saved catalog");
            dialog.ShowInTaskbar=false;dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new System.Drawing.Point(-30000,-30000);dialog.Show();Application.DoEvents();
            dialog.PerformLayout();using(var bitmap=new System.Drawing.Bitmap(dialog.Width,dialog.Height)) {
                dialog.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,dialog.Width,dialog.Height));bitmap.Save(Path.Combine(root,"local-configuration-picker-"+userMode+".png"));
            }
            dialog.Close();
        }
        core.Unload();reopened.Unload();plain.Unload();missing.Unload();
    }
    static void LegacyReference()
    {
        var core=Core();string route=(string)Get(core,"route");Call(core,"SelectIdentity",Snapshot(route),false);
        string legacy=core.ActiveBestPath;core.CheckPoints[0].SetCurrentTSForLoad(TimeSpan.FromSeconds(11));core.CheckPoints[core.CheckPoints.Count-1].SetCurrentTSForLoad(TimeSpan.FromSeconds(3600));
        string old=core.GetRStr();BestTimelineStorage.Write(legacy,old);
        var config=LocalConfigurationReader.FromPreset(Preset(),route);core.SelectLocalConfiguration(config,false);core.RefreshBestReference();
        Check(core.CheckPoints[0].Best==TimeSpan.FromSeconds(11)&&!File.Exists(config.BestPath),"old gameplay best is read without creating or overwriting config best");
        core.CheckPoints[0].SetCurrentTSForLoad(TimeSpan.FromSeconds(26));Set(core,"_CurrentStep",1);core.RefreshBestReference();
        Check((TimeSpan)Get(core,"WillClear")==TimeSpan.FromSeconds(3615),"loaded reference prediction includes final time plus node difference");
        core.CreateBestReferenceIfMissing();Check(File.Exists(config.BestPath)&&LocalTimelineConfiguration.Known(route).Any(v=>v.key==config.key),"editing a missing local reference records its reusable configuration");
        core.SaveBest();Check(File.ReadAllText(BestTimelineStorage.IoPath(legacy))==old&&(TimeSpan)Get(core,"WillClear")==TimeSpan.FromSeconds(3600),"saving refreshes prediction and preserves original gameplay best bytes");
        // Hold the state lock while an older background queue reaches it.
        // A newer explicit refresh must not be permanently erased afterwards.
        Task queued;var begun=new ManualResetEventSlim();
        lock(Get(core,"configurationSync")) lock(Get(core,"timelineSync")) {
            queued=Task.Run(()=>{begun.Set();Call(core,"QueueReference");});Check(begun.Wait(3000),"older reference queue is interleaved with explicit refresh");
            core.RefreshBestReference();Check((TimeSpan)Get(core,"WillClear")==TimeSpan.FromSeconds(3600),"explicit refresh applies saved full prediction");
        }
        Check(queued.Wait(3000),"interleaved queue completes");
        var watch=Stopwatch.StartNew();while(!(bool)Get(core,"referenceReady")&&watch.ElapsedMilliseconds<3000)Thread.Sleep(5);
        Call(core,"ApplyQueuedReference");Check((TimeSpan)Get(core,"WillClear")==TimeSpan.FromSeconds(3600)&&core.CheckPoints[0].Best==TimeSpan.FromSeconds(26),"old queue cannot leave newly saved best or prediction cleared");
        string source=Path.Combine(root,"legacy-reference-source.json");File.WriteAllText(source,old);
        core.ImportReference(source);Check(core.CheckPoints[0].Best==TimeSpan.FromSeconds(11)&&File.ReadAllText(source)==old,"explicit legacy import refreshes only reference and preserves source");
        core.Unload();
    }
    static void StartAndReset()
    {
        var core=Core();string route=(string)Get(core,"route");var a=LocalConfigurationReader.FromPreset(Preset(),route);var tcs=new TaskCompletionSource<LocalTimelineConfiguration>();
        using(var process=Process.GetCurrentProcess()) {
            Set(core,"PalProcess",process);var observer=(LocalConfigurationObserver)Get(core,"configurationReader");Set(observer,"target",process);Set(observer,"reading",tcs.Task);
            Call(core,"OnGameplayStarting");Check((bool)Get(core,"configurationFrozen")&&Get(core,"localConfiguration")==null,"immediate start freezes pending configuration read without blocking");
            core.SetTS(TimeSpan.FromSeconds(45));tcs.SetResult(a);Call(core,"CompleteStartConfiguration");Check(core.ActiveBestPath==a.BestPath&&((PTimer)Get(core,"MT")).CurrentTSOnly==TimeSpan.FromSeconds(45),"pre-start pending read can complete without losing current time");
            Set(core,"PalProcess",null);core.SaveBest();Check(File.Exists(a.BestPath),"frozen pending read survives later game exit");
            core.Reset();Call(core,"InitCheckPoints");var stale=new TaskCompletionSource<LocalTimelineConfiguration>();Set(core,"startConfiguration",stale.Task);Set(core,"configurationFrozen",true);core.Reset();stale.SetResult(a);Call(core,"CompleteStartConfiguration");Check(Get(core,"localConfiguration")==null,"reset cannot adopt previous run pending configuration");
            Task queued;var ready=new ManualResetEventSlim();
            lock(Get(core,"configurationSync")) {
                Set(core,"startConfiguration",Task.FromResult(a));Set(core,"configurationFrozen",true);
                queued=Task.Run(()=>{ready.Set();Call(core,"CompleteStartConfiguration");});Check(ready.Wait(3000),"completed snapshot callback queued concurrently with UI reset");
                core.Reset();
            }
            Check(queued.Wait(3000)&&Get(core,"localConfiguration")==null,"snapshot apply and reset are atomic across timer/UI threads");
            var begun=new ManualResetEventSlim();var release=new ManualResetEventSlim();var observed=new LocalConfigurationObserver((p,r)=>{begun.Set();release.Wait();return a;});
            observed.Observe(process,route);Check(begun.Wait(3000),"background observer started");var pending=observed.CaptureStart(process,route);observed.Reset();release.Set();Check(pending.Wait(3000),"old observer read finished");Thread.Sleep(40);Check(observed.Current==null,"old observer callback cannot populate reset generation");
        }
        core.Unload();
    }
    static void RealPresets(string directory)
    {
        foreach(string path in Directory.GetFiles(directory,"*.pal98config")) {
            var value=LocalConfigurationReader.FromFile(path,routeA);Check(value.Valid,"existing signed preset remains usable: "+Path.GetFileName(path));
        }
        string game=Directory.GetParent(directory).Parent.FullName;
        Check(LocalConfigurationReader.ReadCurrent(game,routeA,local).Valid,"real complete package current configuration and effective profile are readable");
    }
    static void ObserverProcess()
    {
        var preset=Preset();Publish(root,preset);
        string stub=Path.Combine(root,"PAL.exe");File.Copy(Assembly.GetExecutingAssembly().Location,stub);
        File.Copy(Assembly.GetExecutingAssembly().Location+".config",stub+".config");
        using(var process=Process.Start(new ProcessStartInfo(stub,"--fixture-process") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true})) {
            try {
                var observer=new LocalConfigurationObserver();var capture=observer.CaptureStart(process,routeA);
                Check(capture.Wait(5000)&&capture.Result?.configuration_id==preset.id,"production observer reads configuration from synthetic PAL process directory");
                string original=capture.Result.key;process.StandardInput.WriteLine("exit");Check(process.WaitForExit(3000),"synthetic PAL process exits cleanly");
                Publish(root,Preset("[Patch]\nDefaultPatch=ChangedAfterExit\n"));Check(capture.Result.key==original,"completed start snapshot never rereads configuration changed after exit");
                var exited=new LocalConfigurationObserver().CaptureStart(process,routeA);Check(exited.Wait(5000)&&exited.Result==null,"exited process cannot contribute a late configuration snapshot");
            } finally { if(!process.HasExited)process.Kill(); }
        }
    }
    [STAThread] static int Main(string[] args)
    {
        if(args.Length>0&&args[0]=="--fixture-process") {Console.ReadLine();return 0;}
        try { BindStorage(false);Normalization();EffectiveConfiguration();ProfileResources();SaveAndReopen(false);StartAndReset();SaveAndReopen(true);LegacyReference();if(args.Length>0)RealPresets(args[0]);ObserverProcess();Console.WriteLine("PASS total="+checks+" realGameUsed=false networkUsed=false syntheticProcessUsed=true");return 0; }
        catch(Exception error) { Console.Error.WriteLine(error);return 1; }
    }
}
