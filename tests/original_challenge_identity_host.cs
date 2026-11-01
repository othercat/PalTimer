// Exercise the built timer against the real native publisher fixture. No PAL process.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Web.Script.Serialization;

static class OriginalChallengeIdentityHost
{
    static int checks;
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=8388608};
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static void Check(bool ok,string why) {if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
    static object Get(object o,string key) => o.GetType().GetField(key,All)?.GetValue(o) ?? o.GetType().GetProperty(key,All)?.GetValue(o);
    static void Set(object o,string key,object value) {var f=o.GetType().GetField(key,All);if(f!=null)f.SetValue(o,value);else o.GetType().GetProperty(key,All).SetValue(o,value);}
    static object Invoke(Type t,object target,string name,params object[] args) => t.GetMethod(name,All).Invoke(target,args);
    static void Rejected(Action action,string why) {try{action();}catch(TargetInvocationException e)when(e.InnerException is InvalidDataException){Check(true,why);return;}throw new Exception(why);}
    static Dictionary<string,object> Map(object value)=>(Dictionary<string,object>)value;
    static int Main(string[] args)
    {
        try {
            var assembly=Assembly.LoadFrom(args[0]);var type=assembly.GetType("Pal98Timer.OriginalChallengeIdentity",true);
            var snapshotType=assembly.GetType("Pal98Timer.GameplaySnapshot",true);
            string root=args[1],payload=File.ReadAllText(args[2]),selected=Path.Combine(root,"palmod/challenge-selection.v1.json");
            string selection=File.ReadAllText(selected),configPath=Path.Combine(root,"config.ini"),config=File.ReadAllText(configPath);
            object native=typeof(JavaScriptSerializer).GetMethod("Deserialize").MakeGenericMethod(snapshotType).Invoke(Json,new object[]{payload}), ranking=Get(native,"ranking");
            var context=Invoke(type,null,"Read",root,native);
            Check(context!=null && ((string)Get(context,"Title")).Contains("魂牵"),"downloaded challenge identity matches actual native content and filtered Hunqian pool");
            var facts=(Dictionary<string,string>)Get(ranking,"rules");
            Check(facts["random_skills.pool"]=="e5c4deb832d4ce44e6dfc62f29e7ecd5a9e6410e1a7265e3b67ad5499c518aca","native pool includes the existing unusable-definition filter");
            var originalFacts=new Dictionary<string,string>(facts);
            string originalId=(string)Get(ranking,"configuration_id"),gameplayHash=(string)Get(native,"rules_sha256");
            object online=Invoke(type,context,"Decorate",ranking);
            Check(originalId==(string)Get(ranking,"configuration_id") && facts.Count==originalFacts.Count && originalFacts.All(p=>facts[p.Key]==p.Value),"online decoration preserves native ranking");
            Check(gameplayHash==(string)Get(native,"rules_sha256"),"online decoration preserves local gameplay/PB identity");
            var onlineFacts=(Dictionary<string,string>)Get(online,"rules");
            Check(onlineFacts["challenge.id"]==(string)Get(context,"Id") && onlineFacts.Count==facts.Count+4,"online identity carries all four pinned challenge facts");
            string firstId=(string)Get(online,"configuration_id");Set(context,"Id",Guid.NewGuid().ToString());
            Check(firstId!=(string)Get(Invoke(type,context,"Decorate",ranking),"configuration_id"),"equal gameplay in distinct challenges does not collide online");
            string pool=facts["random_skills.pool"];facts["random_skills.pool"]=new string('e',64);
            Rejected(()=>Invoke(type,null,"Read",root,native),"changed effective skill pool is rejected");facts["random_skills.pool"]=pool;
            string speed=facts["fade_ms"];facts["fade_ms"]="1200";
            Rejected(()=>Invoke(type,null,"Read",root,native),"changed effective gameplay is rejected");facts["fade_ms"]=speed;
            Set(native,"covered",false);Rejected(()=>Invoke(type,null,"Read",root,native),"uncovered native content cannot claim a challenge");Set(native,"covered",true);
            File.WriteAllText(configPath,config.Replace("DefaultPatch=pal98fix-classic-v5","DefaultPatch=suiji-v0.2"));
            Rejected(()=>Invoke(type,null,"Read",root,native),"different installed local package cannot claim the selected challenge");File.WriteAllText(configPath,config);
            string iniPath=Path.Combine(root,"challenge-rules.ini"),ini=File.ReadAllText(iniPath);File.AppendAllText(iniPath,"; changed\n");
            Rejected(()=>Invoke(type,null,"Read",root,native),"changed downloaded INI is rejected");File.WriteAllText(iniPath,ini);
            var document=Map(Json.DeserializeObject(selection));Map(Map(document["activation"])["challenge"])["title"]="在线：麻烦夜狼开枪赛";
            File.WriteAllText(selected,Json.Serialize(document));context=Invoke(type,null,"Read",root,native);
            Check((string)Get(context,"Title")=="麻烦夜狼开枪赛","dropdown online prefix never enters timer title");
            var observation=Activator.CreateInstance(assembly.GetType("Pal98Timer.CompetitionObservation",true));
            Set(observation,"Ranking",ranking);Set(observation,"Hardcore",Activator.CreateInstance(assembly.GetType("Pal98Timer.CompetitionHardcore",true)));
            Set(native,"Challenge",context);Invoke(type,null,"Apply",native,observation);
            Check((string)Get(Get(observation,"Ranking"),"configuration_id")!=(string)Get(ranking,"configuration_id"),"observation receives a copied online ranking");
            Set(Get(observation,"Hardcore"),"requested",true);Invoke(type,null,"Apply",native,observation);
            Check(!string.IsNullOrEmpty((string)Get(observation,"ValidationError")),"nostalgia requires matching actual runtime evidence");
            document["active"]=false;File.WriteAllText(selected,Json.Serialize(document));
            Check(Invoke(type,null,"Read",root,native)==null,"selecting local content removes online grouping");
            File.WriteAllText(selected,"{\"schema\":1,\"active\":true}");
            Rejected(()=>Invoke(type,null,"Read",root,native),"incomplete selection fails as a readable data error");
            File.WriteAllText(selected,selection);
            Console.WriteLine("PASS: "+checks+" built-timer/native fixture checks; no game used");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
