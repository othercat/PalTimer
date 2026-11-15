// Exact shipped assemblies, native diagnostic snapshots, no UI or network.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using Pal98Timer;
static class R116ModContract
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static JavaScriptSerializer J=new JavaScriptSerializer{MaxJsonLength=8388608};
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);++checks;}
    static object Get(object o,string k)=>o.GetType().GetProperty(k,All)?.GetValue(o)??o.GetType().GetField(k,All)?.GetValue(o);
    static void Set(object o,string k,object v){var p=o.GetType().GetProperty(k,All);if(p!=null)p.SetValue(o,v);else o.GetType().GetField(k,All).SetValue(o,v);}
    static object Call(Type t,object o,string k,params object[] a)=>t.GetMethods(All).Single(m=>m.Name==k&&m.GetParameters().Length==a.Length&&m.GetParameters().Select((p,i)=>a[i]==null||p.ParameterType.IsInstanceOfType(a[i])).All(v=>v)).Invoke(o,a);
    static object Parse(Type t,object value)=>J.GetType().GetMethods().Single(m=>m.Name=="Deserialize"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==1).MakeGenericMethod(t).Invoke(J,new object[]{J.Serialize(value)});
    static object Read(Type bridge,string json)=>Call(bridge,null,"ReadSnapshotJson",json);
    static byte[] Header(byte[] bytes,ulong epoch){var b=new byte[64];Encoding.ASCII.GetBytes("SDLPAL1\0").CopyTo(b,0);BitConverter.GetBytes((uint)1).CopyTo(b,8);BitConverter.GetBytes((uint)64).CopyTo(b,12);BitConverter.GetBytes((uint)4194304).CopyTo(b,16);BitConverter.GetBytes((uint)bytes.Length).CopyTo(b,24);BitConverter.GetBytes((uint)7001).CopyTo(b,28);BitConverter.GetBytes((ulong)1).CopyTo(b,32);BitConverter.GetBytes(epoch).CopyTo(b,40);BitConverter.GetBytes((ulong)10000).CopyTo(b,56);return b;}
    static object Decode(Type bridge,string json){byte[] bytes=Encoding.UTF8.GetBytes(json);var d=J.Deserialize<Dictionary<string,object>>(json);return Call(bridge,null,"Decode",Header(bytes,Convert.ToUInt64(d["epoch"])),bytes,7001,(ulong)1,(ulong)10000);}
    static void Reject(Action a,string label){try{a();}catch(TargetInvocationException e){Check(e.InnerException is InvalidDataException,label);return;}throw new Exception("Accepted "+label);}
    static int Main(string[] args)
    {
      try{
        var host=typeof(OnlineSnapshotV1).Assembly;var online=Assembly.LoadFrom(args[1]);var browser=Assembly.LoadFrom(args[3]);
        var bridge=host.GetType("Pal98Timer.SdlpalBridge",true);var browserBridge=browser.GetType("Pal98Robot.SdlpalBridge")??browser.GetTypes().Single(t=>t.Name=="SdlpalBridge");
        var snapshot=host.GetType("Pal98Timer.SdlpalSnapshot",true);var splits=host.GetType("Pal98Timer.SdlpalSplitRule",true);var observerType=host.GetType("Pal98Timer.SdlpalRunObserver",true);
        var ranking=host.GetType("Pal98Timer.RankingConfiguration",true);var configType=host.GetType("Pal98Timer.SdlpalChallengeConfiguration",true);var observationType=host.GetType("Pal98Timer.CompetitionObservation",true);
        var routeType=host.GetType("Pal98Timer.CompetitionProtocol",true);var timelineType=host.GetType("Pal98Timer.TimelineIdentity",true);
        var create=host.GetType("Pal98Timer.GForm",true).GetMethod("CreateCompetitionSnapshot",All);
        var read=online.GetType("Pal98Timer.OnlineModule",true).GetMethod("ReadSnapshot",All);
        var cases=new List<object>();int contents=0;
        foreach(string dir in Directory.GetDirectories(args[0]).Where(p=>File.Exists(Path.Combine(p,"mod/r116-before.json"))).OrderBy(p=>p,StringComparer.Ordinal)){
          ++contents;string content=Path.GetFileName(dir),profile=content+"-v1";
          var normal=(Array)Call(splits,null,"Defaults",profile,false);var rules=(Array)Call(splits,null,"Defaults",profile,true);
          Call(splits,null,"ValidateStoryRoute",rules);Check((string)Get(rules.GetValue(rules.Length-1),"Id")=="story-mod01-yuhang-richest","mod endpoint "+content);
          Check(!Equals(Call(splits,null,"Identity",normal),Call(splits,null,"Identity",rules)),"independent route "+content);
          foreach(string label in new[]{"mod","equal","hide-higher","random-higher","mod-higher","stacked"}){
            string run=Path.Combine(dir,label),preText=File.ReadAllText(Path.Combine(run,"r116-before.json")),afterText=File.ReadAllText(Path.Combine(run,"r116-after-baiyue.json")),endText=File.ReadAllText(Path.Combine(run,"r116-celebration.json"));
            var pre=Decode(bridge,preText);var after=Decode(bridge,afterText);var end=Decode(bridge,endText);
            Decode(browserBridge,preText);Decode(browserBridge,afterText);Decode(browserBridge,endText);checks+=6;
            var observer=Activator.CreateInstance(observerType,true);Call(observerType,observer,"Observe",pre,rules);Call(observerType,observer,"Observe",after,rules);
            var reached=(HashSet<string>)Get(observer,"Reached");Check(reached.Contains("story-mod01-baiyue")&&!reached.Contains("story-mod01-yuhang-richest"),"Baiyue continues timing "+content+label);
            Call(observerType,observer,"Observe",end,rules);Check(reached.Contains("story-mod01-yuhang-richest"),"celebration completes "+content+label);
            var config=Activator.CreateInstance(configType,true);Call(configType,config,"ReadFiles",run,7001,(ulong)DateTime.UtcNow.AddMinutes(1).ToFileTimeUtc(),profile);Check(Get(config,"Error")==null,"full configuration "+content+label);
            var facts=(Dictionary<string,string>)Call(snapshot,after,"RankingFacts",false);foreach(var pair in (Dictionary<string,string>)Get(config,"Facts"))facts[pair.Key]=pair.Value;
            Check(facts["config.slot.RuntimeStoryMod01"]=="1"&&facts["config.slot.RuntimeStoryModRevision"]=="3"&&facts["config.slot.StoryMod01"]=="1","stable mod facts "+content+label);
            string id=(string)Call(ranking,null,"Digest",facts);var ids=rules.Cast<object>().Select(r=>(string)Get(r,"Id")).ToArray();string route=(string)Call(routeType,null,"RouteHash",(object)ids);
            object obs=Activator.CreateInstance(observationType,true);
            var game=new{schema="PAL98.GameplayIdentity.v1",family=facts["family"],content_id=profile,content_sha256=facts["content"],rules_sha256=id,fade_ms=Get(after,"fadeCycleMs"),map_speed_ticks=0,walk_frame_ms=Get(after,"walkFrameMs"),speed=Get(after,"speed"),battle_speed=Get(after,"battleSpeed")};
            Set(obs,"Gameplay",Parse(observationType.GetField("Gameplay",All).FieldType,game));Set(obs,"Ranking",Parse(ranking,new{schema="PAL98.RankingConfiguration.v1",covered=true,configuration_id=id,rules=facts}));Set(obs,"Hardcore",Activator.CreateInstance(observationType.GetField("Hardcore",All).FieldType,true));
            Set(obs,"Token","offline-mod-contract");Set(obs,"Core","SDLPAL_GESHI_EASY");Set(obs,"DllHash",Get(after,"exeHash"));Set(obs,"GameVersion","1.0.0.0");Set(obs,"TimelineId",Call(timelineType,null,"Create",id,route,false));Set(obs,"GameTitle","OFFLINE MOD CONTRACT");Set(obs,"ObservedAt",DateTimeOffset.UtcNow);Set(obs,"Step",-1);Set(obs,"ValidationError","OFFLINE DIAGNOSTIC; NOT A RUN");Set(obs,"LiveTimingValid",false);
            Set(obs,"FadeMilliseconds",Get(after,"fadeCycleMs"));Set(obs,"MapSpeedTicks",0);
            Set(obs,"Splits",Parse(observationType.GetField("Splits",All).FieldType,ids.Select(k=>new{checkpoint_id=k,elapsed_ms=(long?)null,status="in_progress"}).ToArray()));
            var dto=(OnlineSnapshotV1)create.Invoke(null,new[]{obs});var result=read.Invoke(null,new object[]{dto});var actualRanking=Get(result,"Ranking");var actualFacts=(Dictionary<string,string>)Get(actualRanking,"rules");
            Check(actualFacts["config.slot.RuntimeStoryMod01"]=="1"&&actualFacts["config.slot.RuntimeStoryModRevision"]=="3","API1 preserves mod facts "+content+label);
            foreach(var pair in facts.Where(p=>p.Key.StartsWith("config.",StringComparison.Ordinal)))Check(actualFacts[pair.Key]==pair.Value,"API1 full priority and rule "+pair.Key);
            cases.Add(new{content,label,ranking=actualRanking,gameplay=Get(result,"Gameplay"),timeline=Get(result,"TimelineId"),route,checkpoints=ids});
            if(label=="mod"){
              var d=J.Deserialize<Dictionary<string,object>>(afterText);d["storyModRevision"]=2;Reject(()=>Read(bridge,J.Serialize(d)),"old mod revision");Reject(()=>Decode(browserBridge,J.Serialize(d)),"browser old mod revision");
              d=J.Deserialize<Dictionary<string,object>>(afterText);d["storyMod01"]="true";Reject(()=>Read(bridge,J.Serialize(d)),"bool coercion");
              d=J.Deserialize<Dictionary<string,object>>(afterText);d["schema"]=1;Reject(()=>Read(bridge,J.Serialize(d)),"legacy schema mod");
              Reject(()=>Read(bridge,afterText.Replace("\"storyModRevision\":3","\"StoryModRevision\":3")),"case alias");
              d=J.Deserialize<Dictionary<string,object>>(afterText);((Dictionary<string,object>)d["identity"])["storyMod01"]=false;Reject(()=>Read(bridge,J.Serialize(d)),"inner mismatch");
            }
          }
          Decode(bridge,File.ReadAllText(Path.Combine(dir,"off/r116-normal-complete.json")));++checks;
          Console.WriteLine("PASS mod route, decoder, observer, API1 "+content);
        }
        Check(contents==21,"all21 contents");Directory.CreateDirectory(args[2]);File.WriteAllText(Path.Combine(args[2],"contracts.json"),J.Serialize(cases),new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(args[2],"summary.json"),J.Serialize(new{passed=true,checks,contents,cases=cases.Count,network=false,gameplayExecuted=false}),new UTF8Encoding(false));Console.WriteLine("PASS "+checks+" / "+cases.Count);return 0;
      }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
