using HFrame.ENT;
using Pal98Timer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.7.2")]

internal static class V169IntegrityBehavior
{
    const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly Product = typeof(TimerCore).Assembly;
    static int Assertions, Passed, Failed;
    static string Root, Repo;
    static Process Host;
    static long Birth;
    static readonly List<string> Failures = new List<string>();
    static double MaxHashSliceMs, HashCpuMs, ObserveBatchMs;
    static Type Type(string name) { return Product.GetType("Pal98Timer." + name, true); }
    static object New(string name, params object[] args) { return Activator.CreateInstance(Type(name), Inst, null, args, null); }
    static object Call(object obj, string method, params object[] args) { return obj.GetType().GetMethod(method, Inst).Invoke(obj, args); }
    static object Static(string name, string method, params object[] args) { return Type(name).GetMethod(method, Stat).Invoke(null, args); }
    static object Field(object obj, string name)
    {
        for (var type = obj.GetType(); type != null; type = type.BaseType) { var field = type.GetField(name, Inst); if (field != null) return field.GetValue(obj); }
        throw new MissingFieldException(name);
    }
    static void Set(object obj, string name, object value)
    {
        for (var type = obj.GetType(); type != null; type = type.BaseType) { var field = type.GetField(name, Inst); if (field != null) { field.SetValue(obj, value); return; } }
        throw new MissingFieldException(name);
    }
    static object Property(object obj, string name) { return obj.GetType().GetProperty(name, Inst).GetValue(obj); }
    static void Check(bool value, string why) { ++Assertions; if (!value) throw new Exception(why); }
    static void Reject(Action action, string why)
    {
        try { action(); } catch (TargetInvocationException ex) { Check(ex.InnerException is InvalidDataException, why + " failure category"); return; }
        Check(false, why + " accepted");
    }
    static void Scenario(string name, Action action)
    {
        Directory.CreateDirectory(Path.Combine(Root, name)); Directory.SetCurrentDirectory(Path.Combine(Root, name));
        try { action(); ++Passed; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { while (ex is TargetInvocationException) ex = ex.InnerException; ++Failed; Failures.Add(name + ": " + ex); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    static void Put(byte[] b, int offset, byte[] data) { Buffer.BlockCopy(data, 0, b, offset, data.Length); }
    static byte[] Snapshot(uint flags = 0, int protection = 3)
    {
        var b = new byte[320]; Put(b,0,BitConverter.GetBytes(0x31495250u)); Put(b,4,BitConverter.GetBytes((ushort)1)); Put(b,6,BitConverter.GetBytes((ushort)320));
        Put(b,8,BitConverter.GetBytes((uint)Host.Id)); Put(b,12,BitConverter.GetBytes(2u)); Put(b,16,BitConverter.GetBytes(Birth));
        Put(b,24,BitConverter.GetBytes(0x0106080Au)); Put(b,28,BitConverter.GetBytes(protection));
        Put(b,32,BitConverter.GetBytes(Stopwatch.GetTimestamp())); Put(b,40,BitConverter.GetBytes(Stopwatch.Frequency));
        Put(b,48,BitConverter.GetBytes(10UL)); Put(b,56,BitConverter.GetBytes(10UL)); Put(b,72,BitConverter.GetBytes(flags));
        Put(b,76,BitConverter.GetBytes(3u)); Put(b,96,Encoding.UTF8.GetBytes("fixture-build")); return b;
    }
    static object Decode(byte[] b, int? pid=null, long? birth=null)
    { return Static("RuntimeIntegrityReader", "Decode", b, pid??Host.Id, birth??Birth, Stopwatch.GetTimestamp(), Stopwatch.Frequency); }
    static object Mode()
    { return New("RuntimeTimingMode",1200,10,true,"fixture-classic","1",new string('a',64),"fixture"); }
    static string Hash(byte[] b) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(b)).Replace("-","").ToLowerInvariant(); }
    static string FileJson(string path, byte[] bytes)
    { return "{\"path\":\""+path+"\",\"size\":"+bytes.Length+",\"sha256\":\""+Hash(bytes)+"\"}"; }
    static string Fixture(bool frozen=true)
    {
        if (File.Exists("ReShade.dll")) File.Delete("ReShade.dll"); // This scenario's synthetic file only.
        var core = new byte[4*1024*1024+17]; for(int i=0;i<core.Length;i+=251)core[i]=(byte)i;
        byte[] gameplay={1,2,3}, graphics={4,5};
        File.WriteAllBytes("PAL.EXE",core); File.WriteAllBytes("DATA.MKF",gameplay); File.WriteAllBytes("ddraw.dll",graphics);
        File.WriteAllText("config.ini","[ExTrA]\nMapSpeedTicks=+0010\nLanguage=1\n");
        return "{\"schema\":\"PAL98.ReleaseIntegrity.v1\",\"release_id\":\"fixture\",\"build\":\"fixture-build\",\"frozen\":"+(frozen?"true":"false")+",\"files\":["+FileJson("PAL.EXE",core)+"],"+
            "\"profiles\":[{\"content_id\":\"fixture-classic\",\"content_version\":\"1\",\"content_sha256\":\""+new string('a',64)+"\",\"files\":["+FileJson("DATA.MKF",gameplay)+"]}],"+
            "\"graphics_chains\":[{\"id\":\"compat\",\"files\":["+FileJson("ddraw.dll",graphics)+"],\"absent_files\":[\"ReShade.dll\"]},{\"id\":\"filter\",\"files\":["+FileJson("ddraw.dll",new byte[]{8,9})+","+FileJson("ReShade.dll",new byte[]{7})+"]}],"+
            "\"settings\":[{\"file\":\"config.ini\",\"section\":\"extra\",\"key\":\"MapSpeedTicks\",\"value\":\"10\"}],"+
            "\"memory_regions\":[{\"id\":\"rng\",\"module\":\"PAL.EXE\",\"rva\":16,\"expected\":\"e80000000090909090\",\"fixups\":[{\"offset\":1,\"module\":\"PAL.dll\",\"rva\":32,\"kind\":\"rel32\"}]}]}";
    }
    static object Manifest(string json) { return Static("ReleaseIntegrityManifest","Parse",json); }
    static object RunVerifier(object manifest, object mode=null)
    {
        var verifier=New("ReleaseIntegrityVerifier",Directory.GetCurrentDirectory(),manifest,mode??Mode());
        int turns=0; TimeSpan cpu=Process.GetCurrentProcess().TotalProcessorTime;
        while(!(bool)Property(verifier,"Complete")) { var watch=Stopwatch.StartNew(); int bytes=(int)Call(verifier,"Advance"); MaxHashSliceMs=Math.Max(MaxHashSliceMs,watch.Elapsed.TotalMilliseconds); Check(bytes<=1024*1024,"per-slice byte budget"); if(++turns>500)throw new Exception("hash never completes"); }
        HashCpuMs += (Process.GetCurrentProcess().TotalProcessorTime-cpu).TotalMilliseconds;
        Call(verifier,"Advance"); Check(turns>=4,"large file must yield"); return verifier;
    }
    static void Contract()
    {
        Check(Product.GetName().Version.ToString()=="3.37.5.0","assembly version");
        Check(Decode(Snapshot())!=null,"valid r10 native layout");
        Check((uint)Field(Decode(Snapshot()),"ProducerVersion")==0x0106080Au,"r10 producer identity preserved");
        var legacy=Snapshot();Put(legacy,24,BitConverter.GetBytes(0x01060900u));
        Check(Decode(legacy)!=null,"known v1.69 diagnostic layout remains readable without bypassing independent build checks");
        var unknown=Snapshot();Put(unknown,24,BitConverter.GetBytes(0x01060901u));
        Check(Decode(unknown)==null,"unknown producer is not silently accepted");
        Check(Decode(Snapshot(),Host.Id+1)==null,"PID identity"); Check(Decode(Snapshot(),null,Birth+1)==null,"creation identity");
        foreach(int offset in new[]{0,4,6,24,84}) { var b=Snapshot(); b[offset]^=1; Check(Decode(b)==null,"bad field offset "+offset); }
        var odd=Snapshot();Put(odd,12,BitConverter.GetBytes(3u));Check(Decode(odd)==null,"odd seqlock");
        var stale=Snapshot();Put(stale,32,BitConverter.GetBytes(Stopwatch.GetTimestamp()-4*Stopwatch.Frequency));Check(Decode(stale)==null,"stale heartbeat");
        Check(Decode(Snapshot(128))==null,"unknown alert bit");
        Check(Decode(Snapshot(127,2))!=null,"Preparing is valid independent heartbeat");
        var noCalls=Snapshot();Put(noCalls,48,BitConverter.GetBytes(0UL));Put(noCalls,56,BitConverter.GetBytes(0UL));
        Check(Decode(noCalls)!=null && (uint)Field(Decode(noCalls),"Alerts")==0,"no random calls is not cheating");
        var embedded=Static("ReleaseIntegrityManifest","LoadEmbedded");
        File.WriteAllText("release_integrity.v1.json",Fixture());
        var unchanged=Static("ReleaseIntegrityManifest","LoadEmbedded");
        Check((string)Property(embedded,"release_id")== (string)Property(unchanged,"release_id"),"external JSON ignored");
        if(!(bool)Property(embedded,"frozen")) Check((string)Property(embedded,"release_id")=="v1.69-candidate-not-frozen","unfrozen explicit");
    }
    static void ManifestsAndFiles()
    {
        string json=Fixture(); var manifest=Manifest(json);
        Reject(()=>Manifest(json.Replace("PAL.EXE","../PAL.EXE")),"path traversal");
        Reject(()=>Manifest(json.Replace("\"offset\":1","\"offset\":7")),"out of range fixup");
        Reject(()=>Manifest(json.Replace("rel32","mask")),"blind mask rejected");
        using(var verifier=(IDisposable)RunVerifier(manifest))Check(Property(verifier,"State").ToString()=="Match","semantic normalized settings and complete graphics chain match");
        File.AppendAllText("config.ini","Music=custom\n");
        using(var verifier=(IDisposable)RunVerifier(manifest))Check(Property(verifier,"State").ToString()=="Match","music exception not hashed");
        File.WriteAllBytes("DATA.MKF",new byte[]{9,2,3});
        using(var verifier=(IDisposable)RunVerifier(manifest))Check(Property(verifier,"State").ToString()=="Mismatch","content changed");
        json=Fixture(); manifest=Manifest(json); File.WriteAllBytes("ReShade.dll",new byte[]{7});
        using(var verifier=(IDisposable)RunVerifier(manifest))Check(Property(verifier,"State").ToString()=="Mismatch","mixed rendering chain fails");
        json=Fixture();manifest=Manifest(json.Replace("\"content_id\":\"fixture-classic\"","\"content_id\":\"other\""));
        using(var verifier=(IDisposable)RunVerifier(manifest))Check(Property(verifier,"State").ToString()=="Incomplete","unsupported content not matched");
        json=Fixture(false);manifest=Manifest(json);
        using(var verifier=(IDisposable)RunVerifier(manifest))Check(Property(verifier,"State").ToString()=="Incomplete","unfrozen never matches");
        json=Fixture();manifest=Manifest(json);
        using(var locked=new FileStream("PAL.EXE",FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        using(var verifier=(IDisposable)New("ReleaseIntegrityVerifier",Directory.GetCurrentDirectory(),manifest,Mode()))
        {for(int i=0;i<20 && !(bool)Property(verifier,"Complete");++i)Call(verifier,"Advance");Check(Property(verifier,"State").ToString()=="Incomplete","access failure not cheat");}
    }
    static void ReadOnlyAndFixups()
    {
        using(var process=(IDisposable)New("IntegrityProcessReader",Host.Id))
        {
            Check((uint)Type("IntegrityProcessReader").GetField("ReadAccess",Stat).GetRawConstantValue()==0x00101010u,"minimum read rights");
            Check((bool)Call(process,"IsCurrent"),"live fixed process identity");
            Check(((IDictionary)Call(process,"Modules")).Count>0,"actual loaded module enumeration");
            var liveModules=Call(process,"Modules");
            var liveRegion=New("ReleaseIntegrityRegion");
            Type("ReleaseIntegrityRegion").GetProperty("module").SetValue(liveRegion,Path.GetFileName(Host.MainModule.FileName));
            Type("ReleaseIntegrityRegion").GetProperty("rva").SetValue(liveRegion,0u);
            Type("ReleaseIntegrityRegion").GetProperty("expected").SetValue(liveRegion,"4d5a");
            Check((bool)Call(process,"VerifyRegion",liveRegion,liveModules),"independent actual mapped code bytes match");
            Type("ReleaseIntegrityRegion").GetProperty("expected").SetValue(liveRegion,"9090");
            Check(!(bool)Call(process,"VerifyRegion",liveRegion,liveModules),"independent mismatched bytes fail without trusting IPC");
            IntPtr bytes=Marshal.AllocHGlobal(16);try { Marshal.Copy(new byte[]{2,3,4,5},0,bytes,4);Check(((byte[])Call(process,"Read",bytes.ToInt64(),4)).SequenceEqual(new byte[]{2,3,4,5}),"read own fixture memory"); }finally{Marshal.FreeHGlobal(bytes);}
        }
        var manifest=Manifest(Fixture()); var region=((Array)Property(manifest,"memory_regions")).GetValue(0);
        var modules=(IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string),Type("IntegrityModule")));
        var main=New("IntegrityModule");Set(main,"Address",0x400000L);Set(main,"Size",(uint)0x1000);modules.Add("PAL.EXE",main);
        var dll=New("IntegrityModule");Set(dll,"Address",0x70000000L);Set(dll,"Size",(uint)0x1000);modules.Add("PAL.dll",dll);
        var arguments=new object[]{region,modules,0L};var expected=(byte[])Type("IntegrityProcessReader").GetMethod("ExpectedBytes",Stat).Invoke(null,arguments);
        Check((long)arguments[2]==0x400010L,"module-relative source");Check(BitConverter.ToInt32(expected,1)==0x70000020L-(0x400010L+5),"exact ASLR relative jump target");
    }
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint allocationType, uint protection);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool VirtualFree(IntPtr address, UIntPtr size, uint freeType);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern uint GetPrivateProfileInt(string section,string key,int fallback,string file);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool FreeLibrary(IntPtr module);
    static void ReadFailure(Action action,string why)
    {
        try { action(); } catch(TargetInvocationException ex) { Check((bool)Static("ReleaseIntegrityVerifier","ReadFailure",ex.InnerException),why+" is incomplete/read failure");return; }
        Check(false,why+" unexpectedly read");
    }
    static void IndirectTrampoline()
    {
        string json=Fixture();
        Reject(()=>Manifest(json.Replace("\"rva\":16","\"pointer_rva\":16,\"rva\":16")),"two region sources");
        Reject(()=>Manifest(json.Replace("\"rva\":16,","")),"missing region source");
        Reject(()=>Manifest(json.Replace("\"rva\":16","\"pointer_rva\":4294967294")),"pointer slot overflows uint32");
        Reject(()=>Manifest(json.Replace("\"offset\":1","\"offset\":2147483647")),"fixup offset integer overflow");
        var manifest=Manifest(json.Replace("\"rva\":16","\"pointer_rva\":0"));
        var region=((Array)Property(manifest,"memory_regions")).GetValue(0);
        IntPtr block=IntPtr.Zero;
        for(int i=0;i<64 && block==IntPtr.Zero;i++)block=VirtualAlloc(new IntPtr(0x50000000L+i*0x100000L),(UIntPtr)4096,0x3000,4);
        Check(block!=IntPtr.Zero,"allocated isolated low-address uint32 fixture");
        try
        {
            var modules=(IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string),Type("IntegrityModule")));
            var source=New("IntegrityModule");Set(source,"Address",block.ToInt64());Set(source,"Size",4096u);modules.Add("PAL.EXE",source);
            var destination=New("IntegrityModule");Set(destination,"Address",block.ToInt64()+0x800);Set(destination,"Size",4096u);modules.Add("PAL.dll",destination);
            long trampoline=block.ToInt64()+0x100;
            byte[] expected=(byte[])Static("IntegrityProcessReader","ExpectedBytesAtAddress",region,modules,trampoline);
            Check(BitConverter.ToInt32(expected,1)==block.ToInt64()+0x820-(trampoline+5),"indirect rel32 uses actual heap region address");
            Marshal.WriteInt32(block,unchecked((int)(uint)trampoline));Marshal.Copy(expected,0,new IntPtr(trampoline),expected.Length);
            using(var reader=(IDisposable)New("IntegrityProcessReader",Host.Id))
            {
                Check((bool)Call(reader,"VerifyRegion",region,modules),"independent pointer-slot and trampoline bytes match");
                Marshal.WriteByte(new IntPtr(trampoline),0x90);
                Check(!(bool)Call(reader,"VerifyRegion",region,modules),"trampoline modification is a byte mismatch");
                Marshal.Copy(expected,0,new IntPtr(trampoline),expected.Length);
                Marshal.WriteInt32(block,0);ReadFailure(()=>Call(reader,"VerifyRegion",region,modules),"null trampoline");
                Marshal.WriteInt32(block,unchecked((int)0xfffffffe));ReadFailure(()=>Call(reader,"VerifyRegion",region,modules),"trampoline end overflows uint32");
                Marshal.WriteInt32(block,unchecked((int)(uint)trampoline));Set(source,"Size",3u);
                ReadFailure(()=>Call(reader,"VerifyRegion",region,modules),"pointer slot outside declared module");Set(source,"Size",4096u);
                ReadFailure(()=>Call(reader,"Read",long.MaxValue,2),"invalid read address range");
            }
        }
        finally { VirtualFree(block,UIntPtr.Zero,0x8000); }
    }
    static void SettingsValuesAndComments()
    {
        string json=Fixture();
        Reject(()=>Manifest(json.Replace("\"value\":\"10\"","\"value\":\"10\",\"allowed_values\":[\"9\",\"10\"]")),"mutually exclusive setting forms");
        Reject(()=>Manifest(json.Replace("\"value\":\"10\"","\"allowed_values\":[]")),"empty allowed set");
        Reject(()=>Manifest(json.Replace("\"value\":\"10\"","\"allowed_values\":[\"1\",\"+001\"]")),"duplicate semantic values");
        Reject(()=>Manifest(json.Replace("\"value\":\"10\"","\"allowed_values\":[\"TrUe\",\"1\"]")),"boolean and numeric aliases are duplicates");
        Check((string)Static("ReleaseIntegrityVerifier","Normalize"," FaLsE ")=="0" && (string)Static("ReleaseIntegrityVerifier","Normalize","TrUe")=="1","boolean normalization ignores case");
        var manifest=Manifest(json.Replace("\"value\":\"10\"","\"allowed_values\":[\"9\",\"10\"],\"default_value\":\"10\""));
        var setting=((Array)Property(manifest,"settings")).GetValue(0);
        using(var verifier=(IDisposable)New("ReleaseIntegrityVerifier",Directory.GetCurrentDirectory(),manifest,Mode()))
        {
            foreach(string raw in new[]{" 10 ; 中文说明","+0010;相邻注释","9#相邻注释"," 9\t# 空白注释","'10' ; 说明"})
            {
                File.WriteAllText("config.ini","[extra]\nMapSpeedTicks="+raw+"\n");
                Check((bool)Call(verifier,"VerifySetting",setting),"allowed numeric value/comment "+raw);
            }
            File.WriteAllText("config.ini","[extra]\nMapSpeedTicks=10;native-comment\n");
            Check(GetPrivateProfileInt("extra","MapSpeedTicks",-1,Path.GetFullPath("config.ini"))==10,"real Win32 numeric reader accepts adjacent semicolon");
            foreach(string raw in new[]{"8 ; not allowed","10x;garbage","10.0;not integer"," ; empty is not default"})
            {File.WriteAllText("config.ini","[extra]\nMapSpeedTicks="+raw+"\n");Check(!(bool)Call(verifier,"VerifySetting",setting),"reject malformed/disallowed numeric "+raw);}
            File.WriteAllText("config.ini","[extra]\nUnrelated=1\n");Check((bool)Call(verifier,"VerifySetting",setting),"explicit absent default is checked against allowed values");
            Type("ReleaseIntegritySetting").GetProperty("default_value").SetValue(setting,null);Check(!(bool)Call(verifier,"VerifySetting",setting),"absent key without default never matches");
            File.WriteAllText("config.ini","[extra]\nMapSpeedTicks=10\nMapSpeedTicks=9\n");Check(!(bool)Call(verifier,"VerifySetting",setting),"duplicate value remains rejected");
        }
        foreach(var pair in new[]{Tuple.Create("D:/PAL;Test/file","D:/PAL;Test/file"),Tuple.Create("10;assets","10;assets"),Tuple.Create("\"D:/PAL ;Test/file\" ; comment","D:/PAL ;Test/file"),Tuple.Create("D:/PAL/file ; comment","D:/PAL/file"),Tuple.Create("D:/PAL#1/file","D:/PAL#1/file")})
            Check((string)Static("ReleaseIntegrityVerifier","ParseIniValue",pair.Item1,false)==pair.Item2,"string comment boundary "+pair.Item1);
        var booleanManifest=Manifest(json.Replace("\"value\":\"10\"","\"value\":\"0\""));
        var booleanSetting=((Array)Property(booleanManifest,"settings")).GetValue(0);
        using(var verifier=(IDisposable)New("ReleaseIntegrityVerifier",Directory.GetCurrentDirectory(),booleanManifest,Mode()))
        {
            foreach(string raw in new[]{"false","FaLsE ; 中文","FALSE;紧连"})
            {File.WriteAllText("config.ini","[extra]\nMapSpeedTicks="+raw+"\n");Check((bool)Call(verifier,"VerifySetting",booleanSetting),"boolean false maps to expected zero "+raw);}
            File.WriteAllText("config.ini","[extra]\nMapSpeedTicks=TrUe\n");Check(!(bool)Call(verifier,"VerifySetting",booleanSetting),"true never matches expected zero");
        }
        string hidden=Fixture().Replace("\"section\":\"extra\"","\"section\":\"Special\"").Replace("\"key\":\"MapSpeedTicks\"","\"key\":\"RandomControl\"").Replace("\"value\":\"10\"","\"value\":\"0\"");
        File.WriteAllText("config.ini","[Special]\nRandomControl=1\n");
        using(var verifier=(IDisposable)RunVerifier(Manifest(hidden)))
        {Check(Property(verifier,"State").ToString()=="Mismatch","hidden gameplay key mismatch still checked");Check((string)Property(verifier,"Detail")=="玩法设置与登记配置不符","ordinary diagnostic does not expose internal setting names");}
    }
    static byte[] NormalizedModule(byte[] bytes)
    {
        object[] args={bytes,false};
        return (byte[])Type("ReleaseIntegrityVerifier").GetMethod("NormalizeModuleBytes",Stat).Invoke(null,args);
    }
    static void R10ModuleSwitches()
    {
        string json=Fixture();
        Directory.CreateDirectory("copymen_scripts"); Directory.CreateDirectory("palmod");
        byte[] payload=Encoding.UTF8.GetBytes("trusted script payload");
        string source="{\"schema\":\"PAL98.CopymenScriptModule.v1\",\"id\":\"fixture\",\"enabled\":true,\"payload\":\"palmod/payload.json\",\"sha256\":\""+Hash(payload)+"\"}";
        byte[] on=Encoding.UTF8.GetBytes(source),off=Encoding.UTF8.GetBytes(source.Replace("true","false"));
        Check(NormalizedModule(on).SequenceEqual(NormalizedModule(off)),"only top-level switch normalizes");
        Check(NormalizedModule(new byte[]{239,187,191}.Concat(off).ToArray()).SequenceEqual(NormalizedModule(off)),"UTF8 BOM is transport only");
        Reject(()=>NormalizedModule(Encoding.UTF8.GetBytes(source.Replace("\"enabled\":true","\"enabled\":true,\"enabled\":false"))),"duplicate module switch rejected");
        Reject(()=>NormalizedModule(Encoding.UTF8.GetBytes(source.Replace("\"enabled\":true","\"enabled\":1"))),"nonboolean module switch rejected");
        string entry=FileJson("copymen_scripts/one.module.json",NormalizedModule(on));
        entry=entry.Substring(0,entry.Length-1)+",\"normalization\":\"copymen-enabled-v1\"}";
        string conditional=FileJson("palmod/payload.json",payload);
        conditional=conditional.Substring(0,conditional.Length-1)+",\"enabled_by\":[\"copymen_scripts/one.module.json\"]}";
        int end=json.IndexOf("],\"profiles\"",StringComparison.Ordinal);
        json=json.Insert(end,","+entry+","+conditional);
        var manifest=Manifest(json);
        File.WriteAllBytes("copymen_scripts/one.module.json",on); File.WriteAllBytes("palmod/payload.json",payload);
        using(var verifier=(IDisposable)RunVerifier(manifest))Check(Property(verifier,"State").ToString()=="Match","enabled trusted module matches");
        File.WriteAllBytes("copymen_scripts/one.module.json",off);
        File.WriteAllBytes("palmod/payload.json",new byte[]{9});
        using(var verifier=(IDisposable)RunVerifier(manifest))Check(Property(verifier,"State").ToString()=="Match","disabled payload not active or hashed");
        File.WriteAllBytes("copymen_scripts/one.module.json",on);
        using(var verifier=(IDisposable)RunVerifier(manifest))Check(Property(verifier,"State").ToString()=="Mismatch","enabled payload modification detected");
        File.WriteAllBytes("palmod/payload.json",payload);
        File.WriteAllBytes("copymen_scripts/one.module.json",Encoding.UTF8.GetBytes(source.Replace("fixture","modified").Replace("true","false")));
        using(var verifier=(IDisposable)RunVerifier(manifest))Check(Property(verifier,"State").ToString()=="Mismatch","disabled module definition remains pinned");
        Reject(()=>Manifest(json.Replace("copymen-enabled-v1","ignore-entire-json")),"unknown normalization rejected");
        Reject(()=>Manifest(json.Replace("\"enabled_by\":[\"copymen_scripts/one.module.json\"]","\"enabled_by\":[\"copymen_scripts/missing.module.json\"]")),"missing module owner rejected");
    }
    static void R10UncoveredStillChecksCore()
    {
        string json=Fixture(); var unknown=New("RuntimeTimingMode",1200,10,true,"third-party","1",new string('b',64),"custom");
        using(var verifier=(IDisposable)RunVerifier(Manifest(json),unknown)) {
            Check(Property(verifier,"State").ToString()=="Incomplete","unregistered content never claims match");
            Check((string)Property(verifier,"GraphicsChain")=="compat","graphics checked for unregistered content");
        }
        File.WriteAllBytes("ddraw.dll",new byte[]{1});
        using(var verifier=(IDisposable)RunVerifier(Manifest(json),unknown))Check(Property(verifier,"State").ToString()=="Mismatch","unregistered profile cannot bypass graphics mismatch");
        json=Fixture();File.WriteAllText("config.ini","[extra]\nMapSpeedTicks=99\n");
        using(var verifier=(IDisposable)RunVerifier(Manifest(json),unknown))Check(Property(verifier,"State").ToString()=="Mismatch","unregistered profile cannot bypass fixed safety settings");
        json=Fixture();File.WriteAllBytes("PAL.EXE",new byte[]{8});
        using(var verifier=(IDisposable)New("ReleaseIntegrityVerifier",Directory.GetCurrentDirectory(),Manifest(json),unknown)) {
            for(int i=0;i<50 && !(bool)Property(verifier,"Complete");i++)Call(verifier,"Advance");
            Check(Property(verifier,"State").ToString()=="Mismatch","unregistered profile cannot bypass core mismatch");
        }
    }
    static void R10SettingRanges()
    {
        string json=Fixture().Replace("\"value\":\"10\"","\"min_value\":0,\"max_value\":4294967295,\"default_value\":\"0\"");
        var manifest=Manifest(json); var setting=((Array)Property(manifest,"settings")).GetValue(0);
        using(var verifier=(IDisposable)New("ReleaseIntegrityVerifier",Directory.GetCurrentDirectory(),manifest,Mode())) {
            foreach(string value in new[]{"0","1","4294967295","+000123 ; seed"}) {
                File.WriteAllText("config.ini","[extra]\nMapSpeedTicks="+value+"\n");
                Check((bool)Call(verifier,"VerifySetting",setting),"valid uint32 range "+value);
            }
            foreach(string value in new[]{"-1","4294967296","1.0","invalid"}) {
                File.WriteAllText("config.ini","[extra]\nMapSpeedTicks="+value+"\n");
                Check(!(bool)Call(verifier,"VerifySetting",setting),"invalid range "+value);
            }
        }
        Reject(()=>Manifest(json.Replace("\"min_value\":0,","")),"incomplete range rejected");
        Reject(()=>Manifest(json.Replace("\"min_value\":0","\"min_value\":4294967296")),"inverted range rejected");
        string choices=Fixture().Replace("\"value\":\"10\"","\"allowed_values\":[\"Classic\",\"EffectiveProfile\"],\"ignore_case\":true");
        var modes=Manifest(choices); var modeSetting=((Array)Property(modes,"settings")).GetValue(0);
        using(var verifier=(IDisposable)New("ReleaseIntegrityVerifier",Directory.GetCurrentDirectory(),modes,Mode())) {
            File.WriteAllText("config.ini","[extra]\nMapSpeedTicks=effectiveProfile ; valid runtime spelling\n");
            Check((bool)Call(verifier,"VerifySetting",modeSetting),"runtime enum case is semantic");
            File.WriteAllText("config.ini","[extra]\nMapSpeedTicks=unregistered\n");
            Check(!(bool)Call(verifier,"VerifySetting",modeSetting),"unknown enum is rejected");
        }
        Reject(()=>Manifest(choices.Replace("\"Classic\",\"EffectiveProfile\"","\"Classic\",\"classic\"")),"case-equivalent duplicate choices rejected");
    }
    static void ExactScriptDirectory()
    {
        string json=Fixture();
        string directory="\"exact_directories\":[{\"path\":\"copymen_scripts\",\"extensions\":[\".json\",\".txt\",\".rule\"],\"entries\":[\"one.module.json\",\"two.rule\"]}]";
        json=json.Substring(0,json.Length-1)+","+directory+"}";
        Reject(()=>Manifest(json.Replace("copymen_scripts","Tools")),"only script directory may be enumerated");
        Reject(()=>Manifest(json.Replace("one.module.json","../one.module.json")),"single-level exact entry path");
        Reject(()=>Manifest(json.Replace("\".json\",\".txt\",\".rule\"","\".json\",\".txt\"")),"all runtime script extensions required");
        var manifest=Manifest(json);
        using(var verifier=(IDisposable)RunVerifier(manifest))Check(Property(verifier,"State").ToString()=="Mismatch","missing script directory fails");
        Directory.CreateDirectory("copymen_scripts");File.WriteAllText("copymen_scripts/one.module.json","{}");File.WriteAllText("copymen_scripts/two.rule","fixture");File.WriteAllText("copymen_scripts/ignored.png","ignored");
        Directory.CreateDirectory("copymen_scripts/subdirectory");File.WriteAllText("copymen_scripts/subdirectory/nested.rule","not recursively inspected");
        using(var verifier=(IDisposable)RunVerifier(manifest))Check(Property(verifier,"State").ToString()=="Match","exact script set ignores nonexecutables and subdirectories");
        File.WriteAllText("copymen_scripts/extra.txt","extra executable script");
        using(var verifier=(IDisposable)RunVerifier(manifest))Check(Property(verifier,"State").ToString()=="Mismatch","new unregistered executable module detected");
        File.Delete("copymen_scripts/extra.txt");File.Delete("copymen_scripts/two.rule");
        using(var verifier=(IDisposable)RunVerifier(manifest))Check(Property(verifier,"State").ToString()=="Mismatch","missing registered module detected");
        File.WriteAllText("copymen_scripts/two.rule","fixture");
        string link=Path.GetFullPath("copymen_scripts/link"),target=Path.GetFullPath("link-target");
        Check(link.StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&target.StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"junction fixture paths stay in test workspace");
        Directory.CreateDirectory(target);
        using(var command=Process.Start(new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec"),"/c mklink /J \""+link+"\" \""+target+"\"") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}))
        { command.StandardOutput.ReadToEnd();command.StandardError.ReadToEnd();command.WaitForExit();Check(command.ExitCode==0,"created local junction fixture"); }
        try { using(var verifier=(IDisposable)RunVerifier(manifest))Check(Property(verifier,"State").ToString()=="Incomplete","reparse point is unverified rather than cheating"); }
        finally { Directory.Delete(link); }
    }
    static void SettingsBudgetAndCache()
    {
        string json=Fixture();
        var manifest=Manifest(json);var setting=((Array)Property(manifest,"settings")).GetValue(0);
        using(var verifier=(IDisposable)New("ReleaseIntegrityVerifier",Directory.GetCurrentDirectory(),manifest,Mode()))
        {
            Call(verifier,"AdvanceSettings",Stopwatch.GetTimestamp()-1);
            Check((int)Property(Field(verifier,"pendingSettings"),"Count")==1 && !(bool)Field(verifier,"settingsComplete"),"expired settings slice does no work");
            Check((bool)Call(verifier,"VerifySettingCore",setting,true),"initial saved INI parses");
            Check((int)Property(Field(verifier,"iniReads"),"Count")==1,"one INI cached for the scan");
            using(var locked=new FileStream("config.ini",FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                Check((bool)Call(verifier,"VerifySettingCore",setting,true),"additional settings reuse cached lines without reopening INI");
            Call(verifier,"AdvanceSettings",Stopwatch.GetTimestamp()+Stopwatch.Frequency);
            Check((bool)Field(verifier,"settingsComplete") && Field(verifier,"settingsState").ToString()=="Match","remaining settings and cache identity recheck complete");
        }
        using(var verifier=(IDisposable)New("ReleaseIntegrityVerifier",Directory.GetCurrentDirectory(),manifest,Mode()))
        {
            Check((bool)Call(verifier,"VerifySettingCore",setting,true),"prime stable INI snapshot");
            File.AppendAllText("config.ini","; changed during scan\n");
            Call(verifier,"AdvanceSettings",Stopwatch.GetTimestamp()+Stopwatch.Frequency);
            Check((bool)Field(verifier,"settingsComplete") && Field(verifier,"settingsState").ToString()=="Incomplete","changed cached INI becomes incomplete instead of accepting stale values");
            Check((string)Field(verifier,"settingsDetail")=="玩法设置尚未完成核验","read failure detail reveals no internal key");
        }
    }
    static void DuplicateModuleIdentity()
    {
        var modules=(IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string),Type("IntegrityModule")));
        Func<string,long,string,object> module=(name,address,path)=>{var value=New("IntegrityModule");Set(value,"Name",name);Set(value,"Address",address);Set(value,"Size",4096u);Set(value,"Path",path);return value;};
        Static("IntegrityProcessReader","RecordModule",modules,module("PAL.EXE",0x400000,"D:/fixture/PAL.EXE"));
        Static("IntegrityProcessReader","RecordModule",modules,module("PAL.EXE",0x400000,"d:/fixture/pal.exe"));
        Check(modules.Count==1 && !(bool)Field(modules["PAL.EXE"],"Ambiguous"),"identical WOW64 main module duplicates coalesce");
        Static("IntegrityProcessReader","RecordModule",modules,module("ntdll.dll",0x70000000,"C:/Windows/SysWOW64/ntdll.dll"));
        Static("IntegrityProcessReader","RecordModule",modules,module("ntdll.dll",0x7ff000000000,"C:/Windows/System32/ntdll.dll"));
        Check((bool)Field(modules["ntdll.dll"],"Ambiguous"),"different native/WOW64 system modules retain ambiguity");
        var region=((Array)Property(Manifest(Fixture()),"memory_regions")).GetValue(0);
        Static("IntegrityProcessReader","RecordModule",modules,module("PAL.dll",0x60000000,"D:/fixture/PAL.dll"));
        object[] arguments={region,modules,0L};
        var bytes=(byte[])Type("IntegrityProcessReader").GetMethod("ExpectedBytes",Stat).Invoke(null,arguments);
        Check(bytes.Length==9,"unrelated ambiguous system module does not suppress exact PAL check");
        Static("IntegrityProcessReader","RecordModule",modules,module("PAL.EXE",0x500000,"D:/fixture/PAL.EXE"));
        ReadFailure(()=>Type("IntegrityProcessReader").GetMethod("ExpectedBytes",Stat).Invoke(null,arguments),"ambiguous code source module");
        ReadFailure(()=>Static("IntegrityProcessReader","ModuleAtPath",modules,"PAL.EXE","D:/fixture/PAL.EXE"),"same path with different module bases remains ambiguous");
        Set(modules["PAL.EXE"],"Ambiguous",false);
        Static("IntegrityProcessReader","RecordModule",modules,module("PAL.dll",0x60000000,"D:/different/PAL.dll"));
        ReadFailure(()=>Type("IntegrityProcessReader").GetMethod("ExpectedBytes",Stat).Invoke(null,arguments),"ambiguous fixup destination module");
        Check((long)Field(Static("IntegrityProcessReader","ModuleAtPath",modules,"PAL.dll","D:/different/PAL.dll"),"Address")==0x60000000L,"exact registered path resolves independently of basename ambiguity");
        Check(Static("IntegrityProcessReader","ModuleAtPath",modules,"PAL.dll","D:/missing/PAL.dll")==null,"missing exact path cannot use another same-name module");
    }
    static void RealProxyModulePaths()
    {
        const string name="pal169-path-fixture.dll";
        string first=Path.GetFullPath(Path.Combine("proxy 空格",name)),second=Path.GetFullPath(Path.Combine("system copy",name));
        Directory.CreateDirectory(Path.GetDirectoryName(first));Directory.CreateDirectory(Path.GetDirectoryName(second));
        string source=Path.Combine(Environment.SystemDirectory,"version.dll");
        File.Copy(source,first);File.Copy(source,second);
        // DONT_RESOLVE_DLL_REFERENCES maps these isolated host fixtures without
        // running DLL initialization or loading dependencies. Nothing is called.
        IntPtr one=LoadLibraryEx(first,IntPtr.Zero,1),two=IntPtr.Zero;
        try
        {
            Check(one!=IntPtr.Zero,"first independent DLL fixture mapped");
            two=LoadLibraryEx(second,IntPtr.Zero,1);Check(two!=IntPtr.Zero && two!=one,"same-name DLL fixtures coexist at different full paths");
            using(var reader=(IDisposable)New("IntegrityProcessReader",Host.Id))
            {
                var modules=(IDictionary)Call(reader,"Modules");
                Check((bool)Field(modules[name],"Ambiguous"),"real enumeration preserves same-name ambiguity");
                var left=Static("IntegrityProcessReader","ModuleAtPath",modules,name,first);
                var right=Static("IntegrityProcessReader","ModuleAtPath",modules,name,second);
                Check((long)Field(left,"Address")==one.ToInt64() && (long)Field(right,"Address")==two.ToInt64(),"exact paths select each real loaded identity");
                Check(Static("IntegrityProcessReader","ModuleAtPath",modules,name,Path.Combine(Directory.GetCurrentDirectory(),name))==null,"unregistered expected path does not fall back to basename");
                var region=New("ReleaseIntegrityRegion");Type("ReleaseIntegrityRegion").GetProperty("module").SetValue(region,name);
                Type("ReleaseIntegrityRegion").GetProperty("rva").SetValue(region,(uint?)0);Type("ReleaseIntegrityRegion").GetProperty("expected").SetValue(region,"4d5a");
                ReadFailure(()=>Call(reader,"VerifyRegion",region,modules),"real same-name code reference still requires unique identity");
            }
        }
        finally {if(two!=IntPtr.Zero)FreeLibrary(two);if(one!=IntPtr.Zero)FreeLibrary(one);}
    }
    static void LiveDiagnosticsAndNoTimingGate()
    {
        using(var mapping=MemoryMappedFile.CreateNew("Local\\PAL98.RuntimeIntegrity.v1."+Host.Id,320))
        using(var view=mapping.CreateViewAccessor())
        {
            var monitor=New("RuntimeIntegrityMonitor");
            try
            {
                var bytes=Snapshot(1|2|8);view.WriteArray(0,bytes,0,bytes.Length);
                var scheduling=Stopwatch.StartNew();for(int repeat=0;repeat<1000;++repeat)Call(monitor,"Observe",Host);ObserveBatchMs=scheduling.Elapsed.TotalMilliseconds;
                Check(ObserveBatchMs<1000,"observation only schedules, never hashes synchronously");
                for(int i=0;i<200 && ((string)Call(monitor,"Summary",false)).IndexOf("数值改写")<0;++i)Thread.Sleep(10);
                string summary=(string)Call(monitor,"Summary",false);
                Check(!summary.Contains("云认证")&&!summary.Contains("云ID")&&summary.Contains("数值改写已拦截")&&summary.Contains("随机数待核验")&&summary.Contains("采样不完整"),"warning summary excludes redundant cloud labels");
                Check(!summary.Contains("核验未完成"),"ordinary scan progress never occupies the title");
                var data=new HObj();Call(monitor,"Fill",data,false);Check(data.GetValue<HObj>("RuntimeIntegrity").GetValue<bool>("DiagnosticOnly"),"export is diagnostic only");
                bytes=Snapshot(0);view.WriteArray(0,bytes,0,bytes.Length);Thread.Sleep(260);Call(monitor,"Observe",Host);Thread.Sleep(100);
                Check(((string)Call(monitor,"Summary",true)).Contains("随机数待核验"),"runtime reset cannot erase anomaly");
                foreach(var core in new TimerCore[]{new 仙剑98柔情(null),new 仙剑98柔情不欢乐模式(null),new 仙剑98柔情DX9(null)})
                {
                    Call(core,"InitCheckPoints");Set(core,"runtimeIntegrity",monitor);
                    string before=core.GetScoreValidationError();var watch=(PTimer)Field(core,"MT");watch.Start();
                    Check(core.GetGameVersion().Contains("随机数待核验"),"core title integration");
                    var roundtrip = new HObj(core.GetRStr());
                    Check(roundtrip.GetValue<HObj>("RuntimeIntegrity").GetValue<string>("Summary").Contains("随机数待核验"),"record diagnostic round trip");
                    Check(watch.IsRunning,"recording warning does not stop timing");
                    Check(core.GetScoreValidationError()==before,"new diagnostics do not change existing timing gate");
                    core.Reset();Check(((string)Call(monitor,"Summary",false)).Contains("数值改写已拦截"),"score reset preserves process evidence");
                }
                var second=New("RuntimeIntegrityMonitor");try { Thread.Sleep(260);Call(second,"Observe",Host);Thread.Sleep(100);Check(((string)Call(second,"Summary",false)).Contains("数值改写已拦截"),"new core keeps same-process ledger"); }finally{((IDisposable)second).Dispose();}
            }
            finally { ((IDisposable)monitor).Dispose(); }
        }
    }
    static void VersionCompatibility()
    {
        foreach(string version in new[]{"1.6.8.1","1.6.8.2"})Check((bool)Static("TournamentLockInfoReader","SupportedLockVersions",version,"PAL98.Settings.v1","1.6.8.1","3.37.4.4"),"legacy signed lock supported");
        Check((bool)Static("TournamentLockInfoReader","SupportedLockVersions","1.6.9.0","PAL98.Settings.v1","1.6.9.0","3.37.5.0"),"current lock supported");
        Check((bool)Static("TournamentLockInfoReader","SupportedLockVersions","1.6.8.10","PAL98.Settings.v1","1.6.8.10","3.37.5.0"),"r10 lock supported");
        Check(!(bool)Static("TournamentLockInfoReader","SupportedLockVersions","1.6.8.2","PAL98.Settings.v1","1.6.9.0","3.37.5.0"),"mixed version tuple rejected");
    }
    static void WriteLock(string root, string producer, int version=2)
    {
        string active=Path.Combine(root,"palmod","TournamentLock","v1"), snapshots=Path.Combine(active,"snapshots");
        Directory.CreateDirectory(snapshots);var files=new List<object>();
        foreach(string name in version==1?new[]{"config.ini","mod.ini","dxwrapper.ini"}:new[]{"config.ini","mod.ini","dxwrapper.ini","palmod/common-tools.v1.json"})
        {
            byte[] bytes=Encoding.UTF8.GetBytes(name.EndsWith(".json")?"{\"schema\":\"PAL98.ToolLaunchSettings.v1\",\"tools\":[{\"id\":\"browser\",\"executable\":\"Tools/browser.exe\",\"sha256\":\""+new string('a',64)+"\"}]}":"[extra]\nMapSpeedTicks=10\n");
            string target=Path.Combine(snapshots,name);Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllBytes(target,bytes);
            files.Add(new Dictionary<string,object>{{"name",name},{"snapshot","snapshots/"+name},{"size",bytes.Length},{"sha256",Hash(bytes)}});
        }
        var data=new HObj();data["schema"]="PAL98.TournamentLock.v"+version;data["version"]=version;data["locked"]=true;
        data["locker_name"]="AB12CD34";data["competition_name"]="测试";data["competition_display_name"]="测试比赛专用";
        data["display_lines"]=new[]{"line1","line2","line3","line4"};data["locked_footer_line"]="本次游戏内容不可更改 锁定者 AB12CD34";
        data["files"]=files.ToArray();
        if(version==2)
        {
            data["configuration_id"]=Guid.NewGuid().ToString("D");data["configuration_sha256"]=new string('c',64);
            data["producer_version"]=producer;data["settings_contract"]="PAL98.Settings.v1";
            data["minimum_runtime"]=producer=="1.6.9.0"?"1.6.9.0":"1.6.8.1";data["minimum_timer"]=producer=="1.6.9.0"?"3.37.5.0":"3.37.4.4";
            File.WriteAllBytes(Path.Combine(root,"DATA.MKF"),new byte[]{1,2,3});
            data["dependencies"]=new object[]{new Dictionary<string,object>{{"path","DATA.MKF"},{"size",3},{"sha256",Hash(new byte[]{1,2,3})}}};data["absent_files"]=new string[0];
        }
        byte[] manifest=Encoding.UTF8.GetBytes(data.ToJson());File.WriteAllBytes(Path.Combine(active,"manifest.json"),manifest);
        // Use the already embedded test subject's key; never log or persist it.
        byte[] key=Encoding.ASCII.GetBytes((string)Static("TournamentLockInfoReader","GetIntegrityKey"));
        using(var hmac=new HMACSHA256(key))File.WriteAllText(Path.Combine(active,"manifest.sig"),BitConverter.ToString(hmac.ComputeHash(manifest)).Replace("-","").ToLowerInvariant(),Encoding.ASCII);
        Array.Clear(key,0,key.Length);
    }
    static void SignedLocks()
    {
        foreach(string producer in new[]{"1.6.8.1","1.6.8.2","1.6.9.0"})
        {
            string root=Path.Combine(Directory.GetCurrentDirectory(),producer);Directory.CreateDirectory(root);WriteLock(root,producer);
            string signature=Path.Combine(root,"palmod","TournamentLock","v1","manifest.sig");byte[] before=File.ReadAllBytes(signature);
            Check(Property(Static("TournamentLockInfoReader","Load",root),"State").ToString()=="Locked","signed lock "+producer);
            Directory.CreateDirectory(Path.Combine(root,"Tools"));File.WriteAllBytes(Path.Combine(root,"Tools","browser.exe"),new byte[]{5,6,7});
            Check(Property(Static("TournamentLockInfoReader","Load",root),"State").ToString()=="Locked","tool update keeps old lock valid");
            Check(before.SequenceEqual(File.ReadAllBytes(signature)),"old signature never rewritten");
            File.WriteAllBytes(Path.Combine(root,"DATA.MKF"),new byte[]{7,2,3});
            Check(Property(Static("TournamentLockInfoReader","Load",root),"State").ToString()=="Invalid","game resource remains strict");
            WriteLock(root,producer);File.AppendAllText(signature,"0");
            Check(Property(Static("TournamentLockInfoReader","Load",root),"State").ToString()=="Invalid","damaged signature rejected");
        }
        string legacy=Path.Combine(Directory.GetCurrentDirectory(),"v1");Directory.CreateDirectory(legacy);WriteLock(legacy,"",1);
        Check(Property(Static("TournamentLockInfoReader","Load",legacy),"State").ToString()=="Locked","original v1 lock remains valid");
    }
    static void WaitUntil(Func<bool> predicate, string message)
    {
        var watch=Stopwatch.StartNew();while(!predicate() && watch.ElapsedMilliseconds<3000)Thread.Sleep(5);
        Check(predicate(),message);
    }
    static void ObserveNow(object monitor, Process process)
    { Set(monitor,"nextObservation",0L);Call(monitor,"Observe",process); }
    static HObj Evidence(object monitor)
    { var result=new HObj();Call(monitor,"Fill",result,false);return result.GetValue<HObj>("RuntimeIntegrity"); }
    static string MemoryFixture(bool mismatch)
    {
        string json=Fixture();int start=json.IndexOf("\"memory_regions\":",StringComparison.Ordinal);
        string module=Path.GetFileName(Host.MainModule.FileName);
        return json.Substring(0,start)+"\"memory_regions\":[{\"id\":\"always\",\"module\":\""+module+"\",\"rva\":0,\"expected\":\""+(mismatch?"9090":"4d5a")+"\"},"+
            "{\"id\":\"conditional\",\"module\":\""+module+"\",\"rva\":0,\"expected\":\"9090\",\"protection_states\":[3]}]}";
    }
    static void HeartbeatLossCannotSuppressCode()
    {
        using(var mapping=MemoryMappedFile.CreateNew("Local\\PAL98.RuntimeIntegrity.v1."+Host.Id,320))
        using(var view=mapping.CreateViewAccessor())
        {
            var monitor=New("RuntimeIntegrityMonitor");try
            {
                var initial=Snapshot();view.WriteArray(0,initial,0,initial.Length);ObserveNow(monitor,Host);
                WaitUntil(()=> (int)Field(monitor,"working")==0,"initial diagnostic worker completes");
                var session=Field(monitor,"session");Check((bool)Field(session,"NativeReady"),"native ready is latched by stable heartbeat");
                // Inject a synthetic *compiled contract object* into this isolated
                // host session; production still only loads its embedded resource.
                Set(session,"Manifest",Manifest(MemoryFixture(false)));
                var state=Field(session,"Evidence");Set(state,"CodeMismatchSeen",false);Set(state,"StickyAlerts",0u);
                view.WriteArray(0,new byte[320],0,320);ObserveNow(monitor,Host);
                WaitUntil(()=> (int)Field(monitor,"working")==0,"worker completes after mapping loss");
                Check(!(bool)Evidence(monitor).GetValue<bool>("CodeMismatchSeen"),"unknown protection skips conditional dispatch");
                Check(!Evidence(monitor).GetValue<bool>("HeartbeatValid"),"lost heartbeat remains incomplete");
                Check((int)Property(Field(session,"CheckedRegions"),"Count")==1,"unconditional region still checked without heartbeat");
                Set(session,"Manifest",Manifest(MemoryFixture(true)));ObserveNow(monitor,Host);
                WaitUntil(()=> (int)Field(monitor,"working")==0,"independent mismatch worker completes");
                Check(Evidence(monitor).GetValue<bool>("CodeMismatchSeen"),"unconditional code mismatch found despite missing IPC");
                string summary=(string)Call(monitor,"Summary",false);
                Check(summary.Contains("运行完整性异常")&&!summary.Contains("运行诊断不可用"),"code evidence stays visible while heartbeat availability remains metadata only");
            }
            finally { ((IDisposable)monitor).Dispose(); }
        }
    }
    static void TargetSwitchPublication()
    {
        using(var child=Process.Start(new ProcessStartInfo(Host.MainModule.FileName,"--integrity-child") {UseShellExecute=false,CreateNoWindow=true}))
        using(var mapping=MemoryMappedFile.CreateNew("Local\\PAL98.RuntimeIntegrity.v1."+Host.Id,320))
        using(var view=mapping.CreateViewAccessor())
        {
            var monitor=New("RuntimeIntegrityMonitor");try
            {
                var initial=Snapshot(1);view.WriteArray(0,initial,0,initial.Length);ObserveNow(monitor,Host);
                WaitUntil(()=> (int)Field(monitor,"working")==0,"first target ready");
                Check(Evidence(monitor).GetValue<int>("ProcessId")==Host.Id,"first target identity");
                Call(monitor,"SelectTarget",new object[]{null});
                Check(Evidence(monitor).GetValue<int>("ProcessId")==Host.Id && ((string)Call(monitor,"Summary",false)).Contains("数值改写"),"normal detach retains score evidence");
                ObserveNow(monitor,Host);WaitUntil(()=> (int)Field(monitor,"working")==0,"same target reattach ready");
                var session=Field(monitor,"session");
                object barrier=Type("RuntimeIntegrityMonitor").GetField("ledgerSync",Stat).GetValue(null);
                Monitor.Enter(barrier);
                try
                {
                    var next=Snapshot(1|2);Put(next,12,BitConverter.GetBytes(4u));view.WriteArray(0,next,0,next.Length);ObserveNow(monitor,Host);
                    WaitUntil(()=> {var runtime=Field(Field(session,"Evidence"),"Runtime");return runtime!=null&&(uint)Field(runtime,"Sequence")==4;},"old worker reached publication barrier");
                    Call(monitor,"Observe",child);
                    var pending=Evidence(monitor);
                    Check(pending.GetValue<int>("ProcessId")==child.Id,"target B selected immediately while worker A busy");
                    Check(pending.GetValue<string>("ProcessCreationTime")==""&&pending.GetValue<string>("FileState")=="Incomplete","B has unverified identity placeholder");
                    Check(pending.GetValue<int>("Alerts")==0&&!pending.GetValue<bool>("CodeMismatchSeen"),"A warnings cleared before async B work");
                }
                finally { Monitor.Exit(barrier); }
                WaitUntil(()=> (int)Field(monitor,"working")==0,"old worker completion returns");
                Check(Evidence(monitor).GetValue<int>("ProcessId")==child.Id&&Evidence(monitor).GetValue<int>("Alerts")==0,"late A worker cannot overwrite B placeholder");
                ObserveNow(monitor,child);WaitUntil(()=> (int)Field(monitor,"working")==0,"B confirmed independently");
                Check(Evidence(monitor).GetValue<string>("ProcessCreationTime")==child.StartTime.ToUniversalTime().ToFileTimeUtc().ToString(),"B kernel creation time");
                Check(Evidence(monitor).GetValue<int>("Alerts")==0,"A runtime warnings do not leak to B");
            }
            finally { ((IDisposable)monitor).Dispose();if(!child.HasExited){child.Kill();child.WaitForExit(2000);} }
        }
    }
    static void DetachedTargetClearsAvailabilityOnly()
    {
        foreach(bool confirmed in new[]{false,true})
        {
            var monitor=New("RuntimeIntegrityMonitor");try
            {
                long generation=(long)Call(monitor,"SelectTarget",Host);
                var state=New("RuntimeIntegrityEvidence");Set(state,"RequestedProcessId",Host.Id);
                Set(state,"ReadFailed",true);Set(state,"DiagnosticUnavailable",true);Set(state,"FileRecheckInProgress",true);
                Set(state,"Files",Enum.Parse(Type("IntegrityCheckState"),confirmed?"Mismatch":"Match"));
                Set(state,"FileMismatchSeen",confirmed);Set(state,"PalDllMismatchSeen",confirmed);Set(state,"CodeMismatchSeen",confirmed);Set(state,"StickyAlerts",confirmed?3u:0u);
                Set(monitor,"evidence",state);
                Check(Evidence(monitor).GetValue<bool>("DiagnosticUnavailable")&&!((string)Call(monitor,"Summary",false)).Contains("运行诊断不可用"),"selected-game availability loss remains recorded without a title label");
                Call(monitor,"SelectTarget",new object[]{null});
                string summary=(string)Call(monitor,"Summary",false);var record=Evidence(monitor);
                Check(!summary.Contains("运行诊断不可用")&&!summary.Contains("核验读取失败"),"normal close clears transient availability labels");
                Check(!record.GetValue<bool>("DiagnosticUnavailable")&&!record.GetValue<bool>("ReadFailed")&&!record.GetValue<bool>("FileRecheckInProgress"),"detached metadata no longer claims a live failed check");
                Check(record.GetValue<int>("ProcessId")==Host.Id&&record.GetValue<bool>("FileMismatchSeen")==confirmed&&record.GetValue<bool>("CodeMismatchSeen")==confirmed,"detaching keeps target identity and confirmed file/code evidence");
                Check(confirmed?summary.Contains("[测试版]")&&summary.Contains("数值改写")&&summary.Contains("随机数待核验"):summary=="","only confirmed historical alerts remain after exit");
                Call(monitor,"Publish",Host,generation,state);
                Check((string)Call(monitor,"Summary",false)==summary,"late pre-exit worker cannot restore stale availability warning");
                Call(monitor,"SelectTarget",Host);
                Check((string)Call(monitor,"Summary",false)=="","reattaching starts a fresh availability check");
            }finally{((IDisposable)monitor).Dispose();}
        }
    }
    static void QuietSuccessfulSummary()
    {
        var pending=New("RuntimeIntegrityEvidence");
        Check((string)Call(pending,"Summary",false)=="","initial incomplete state is not a player-facing warning");
        foreach(int protection in new[]{0,1,2})
        {
            var runtime=New("RuntimeIntegritySnapshot");Set(runtime,"Protection",protection);Set(pending,"Runtime",runtime);
            Check((string)Call(pending,"Summary",false)=="","normal coverage and preparation states stay in metadata");
        }
        Set(pending,"ReadFailed",true);Check(((string)Call(pending,"Summary",false)).Contains("核验读取失败"),"real read failure is visible");
        Set(pending,"ReadFailed",false);Set(pending,"DiagnosticUnavailable",true);
        Check((string)Call(pending,"Summary",false)=="","diagnostic availability never adds a title label");
        var state=New("RuntimeIntegrityEvidence");
        Set(state,"Frozen",true);Set(state,"HeartbeatValid",true);Set(state,"Runtime",Decode(Snapshot()));
        Set(state,"Files",Enum.Parse(Type("IntegrityCheckState"),"Match"));Set(state,"Code",Enum.Parse(Type("IntegrityCheckState"),"Match"));
        Check((string)Call(state,"Summary",false)=="" && (string)Call(state,"Summary",true)=="","successful title is quiet with or without cloud activation");
        var monitor=New("RuntimeIntegrityMonitor");try
        {
            Set(monitor,"evidence",state);Set(monitor,"observed",true);
            Check((string)Call(monitor,"Append","原有标题",false)=="原有标题","no added whitespace on quiet title");
            foreach(bool activated in new[]{false,true})
            {
                var data=new HObj();Call(monitor,"Fill",data,activated);var details=data.GetValue<HObj>("RuntimeIntegrity");
                Check(details.GetValue<bool>("CloudIdActivated")==activated && details.GetValue<string>("FileState")=="Match","record retains explicit cloud and file metadata");
            }
            Set(state,"FileMismatchSeen",true);
            Check((string)Call(state,"Summary",true)=="","non-DLL file differences are recorded without a test-version title");
            Set(state,"StickyAlerts",3u);Set(state,"PalDllMismatchSeen",true);
            string warning=(string)Call(state,"Summary",true);
            Check(warning.Contains("数值改写")&&warning.Contains("随机数待核验")&&warning.Contains("[测试版]"),"actual warnings retained with the requested file-difference label");
            Check(!warning.Contains("文件匹配")&&!warning.Contains("云ID")&&!warning.Contains("云认证"),"redundant success/cloud labels absent from warnings too");
        }finally{((IDisposable)monitor).Dispose();}
    }
    static void DiagnosticStartupGrace()
    {
        using(var mapping=MemoryMappedFile.CreateNew("Local\\PAL98.RuntimeIntegrity.v1."+Host.Id,320))
        using(var view=mapping.CreateViewAccessor())
        {
            var monitor=New("RuntimeIntegrityMonitor");try
            {
                ObserveNow(monitor,Host);WaitUntil(()=> (int)Field(monitor,"working")==0,"initial pending diagnostic finishes in background");
                var session=Field(monitor,"session");
                Check(!Evidence(monitor).GetValue<bool>("DiagnosticUnavailable"),"startup grace is quiet");
                Check(!((string)Call(monitor,"Summary",false)).Contains("核验未完成"),"startup progress stays out of title");
                Set(session,"StartedAt",Stopwatch.GetTimestamp()-31*Stopwatch.Frequency);
                ObserveNow(monitor,Host);WaitUntil(()=> (int)Field(monitor,"working")==0,"expired startup grace observed");
                Check(Evidence(monitor).GetValue<bool>("DiagnosticUnavailable"),"persistent missing diagnostic is reported after grace");
                Check(!((string)Call(monitor,"Summary",false)).Contains("运行诊断不可用"),"expired diagnostic availability stays in metadata, not in the title");
                var bytes=Snapshot();view.WriteArray(0,bytes,0,bytes.Length);
                ObserveNow(monitor,Host);WaitUntil(()=> (int)Field(monitor,"working")==0,"valid diagnostic recovers");
                Check(!Evidence(monitor).GetValue<bool>("DiagnosticUnavailable"),"actual recovery clears availability warning");
            }finally{((IDisposable)monitor).Dispose();}
        }
    }
    static void SeqlockReadAndExpiry()
    {
        using(var reader=(IDisposable)New("IntegrityProcessReader",Host.Id))
        {
            object identity=Field(reader,"Identity"), previous;
            using(var mapping=MemoryMappedFile.CreateNew("Local\\PAL98.RuntimeIntegrity.v1."+Host.Id,320))
            using(var view=mapping.CreateViewAccessor())
            {
                var bytes=Snapshot();view.WriteArray(0,bytes,0,bytes.Length);
                previous=Static("RuntimeIntegrityReader","Read",identity,Stopwatch.Frequency,null);
                Check(previous!=null,"fresh snapshot read with time captured after copying");
                Put(bytes,12,BitConverter.GetBytes(3u));view.WriteArray(0,bytes,0,bytes.Length);
                Check(ReferenceEquals(previous,Static("RuntimeIntegrityReader","Read",identity,Stopwatch.Frequency,previous)),"busy writer retains recent same-session snapshot");
                long originalHeartbeat=(long)Field(previous,"Heartbeat");
                Check((long)Field(previous,"Heartbeat")==originalHeartbeat,"fallback never extends heartbeat");
                Set(previous,"Heartbeat",Stopwatch.GetTimestamp()-4*Stopwatch.Frequency);
                Check(Static("RuntimeIntegrityReader","Read",identity,Stopwatch.Frequency,previous)==null,"busy writer cannot keep expired heartbeat alive");
                previous=Decode(Snapshot());bytes=Snapshot();bytes[0]^=1;view.WriteArray(0,bytes,0,bytes.Length);
                Check(Static("RuntimeIntegrityReader","Read",identity,Stopwatch.Frequency,previous)==null,"stable malformed snapshot never uses old success");
                bytes=Snapshot();Put(bytes,32,BitConverter.GetBytes(Stopwatch.GetTimestamp()+Stopwatch.Frequency));view.WriteArray(0,bytes,0,bytes.Length);
                Check(Static("RuntimeIntegrityReader","Read",identity,Stopwatch.Frequency,previous)==null,"genuinely future heartbeat still invalid");
            }
            Check(Static("RuntimeIntegrityReader","Read",identity,Stopwatch.Frequency,previous)==null,"missing mapping never uses cached success");
        }
    }
    static void AdvanceMonitor(object monitor,object session,MemoryMappedViewAccessor view)
    {
        var bytes=Snapshot();view.WriteArray(0,bytes,0,bytes.Length);Set(session,"NextFileSlice",0L);ObserveNow(monitor,Host);
        WaitUntil(()=> (int)Field(monitor,"working")==0,"scan slice finishes");
    }
    static void PeriodicVerificationStability()
    {
        string json=MemoryFixture(false).Replace("\"expected\":\"9090\"","\"expected\":\"4d5a\"");
        string prefix=Directory.GetCurrentDirectory().Substring(Path.GetDirectoryName(Host.MainModule.FileName).Length+1).Replace('\\','/');
        json=json.Replace("\"path\":\"","\"path\":\""+prefix+"/")
            .Replace("\"file\":\"config.ini\"","\"file\":\""+prefix+"/config.ini\"")
            .Replace("\"absent_files\":[\"ReShade.dll\"]","\"absent_files\":[\""+prefix+"/ReShade.dll\"]");
        using(var mapping=MemoryMappedFile.CreateNew("Local\\PAL98.RuntimeIntegrity.v1."+Host.Id,320))
        using(var view=mapping.CreateViewAccessor())
        {
            var monitor=New("RuntimeIntegrityMonitor");try
            {
                var bytes=Snapshot();view.WriteArray(0,bytes,0,bytes.Length);ObserveNow(monitor,Host);
                WaitUntil(()=> (int)Field(monitor,"working")==0,"bootstrap fixture session");
                var session=Field(monitor,"session");var old=Field(session,"Verifier") as IDisposable;if(old!=null)old.Dispose();
                Set(session,"Manifest",Manifest(json));Set(session,"Verifier",null);Set(session,"Evidence",New("RuntimeIntegrityEvidence"));
                Set(session,"NextFileScan",0L);Set(session,"NativeReady",true);
                var mode=Mode();var timing=Field(monitor,"timingReader");Set(timing,"cached",mode);Set(timing,"observedProcess",Host);Set(timing,"nextRetry",long.MaxValue);
                for(int i=0;i<100;++i){AdvanceMonitor(monitor,session,view);if(Evidence(monitor).GetValue<string>("FileState")=="Match"&&Evidence(monitor).GetValue<string>("CodeState")=="Match")break;}
                Check((string)Call(monitor,"Summary",false)=="","first complete scan becomes quiet");
                Check((long)Field(session,"NextFileScan")-Stopwatch.GetTimestamp()>299*Stopwatch.Frequency,"full rescan waits five minutes after completion");
                long verified=long.Parse(Evidence(monitor).GetValue<string>("FilesVerifiedAtQpc"));Check(verified>0,"completed file check has timestamp");
                Set(session,"NextFileScan",0L);AdvanceMonitor(monitor,session,view);
                Check(Evidence(monitor).GetValue<bool>("FileRecheckInProgress")&&Evidence(monitor).GetValue<string>("FileState")=="Match","routine unfinished rescan retains last complete match");
                Check((string)Call(monitor,"Summary",false)=="","routine rescan does not flash incomplete");
                var verifier=Field(session,"Verifier");long position=((FileStream)Field(verifier,"stream")).Position;
                bytes=Snapshot();view.WriteArray(0,bytes,0,bytes.Length);ObserveNow(monitor,Host);WaitUntil(()=> (int)Field(monitor,"working")==0,"heartbeat without due file slice");
                Check(((FileStream)Field(verifier,"stream")).Position==position,"lightweight heartbeat does not consume another file slice before one second");
                object barrier=Type("RuntimeIntegrityMonitor").GetField("ledgerSync",Stat).GetValue(null);Monitor.Enter(barrier);
                try
                {
                    bytes=Snapshot();Put(bytes,12,BitConverter.GetBytes(4u));view.WriteArray(0,bytes,0,bytes.Length);Set(session,"NextFileSlice",0L);ObserveNow(monitor,Host);
                    WaitUntil(()=> {var s=Field(Field(session,"Evidence"),"Runtime");return s!=null&&(uint)Field(s,"Sequence")==4;},"worker stages new evidence");
                    Check((string)Call(monitor,"Summary",false)=="","intermediate code state is not published while worker runs");
                }finally{Monitor.Exit(barrier);}
                WaitUntil(()=> (int)Field(monitor,"working")==0,"publication completes");
                for(int i=0;i<100 && Evidence(monitor).GetValue<bool>("FileRecheckInProgress");++i)
                {AdvanceMonitor(monitor,session,view);Check((string)Call(monitor,"Summary",false)=="","all background slices remain quiet");}
                Check(!Evidence(monitor).GetValue<bool>("FileRecheckInProgress"),"background round completes");
                using(var locked=new FileStream("PAL.EXE",FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                {
                    Set(session,"NextFileScan",0L);AdvanceMonitor(monitor,session,view);
                    Check(Evidence(monitor).GetValue<string>("FileState")=="Incomplete"&&((string)Call(monitor,"Summary",false)).Contains("核验读取失败"),"actual read failure immediately invalidates old success");
                }
                Set(session,"NextFileScan",0L);
                for(int i=0;i<100;++i){AdvanceMonitor(monitor,session,view);if(!Evidence(monitor).GetValue<bool>("FileRecheckInProgress"))break;}
                Check((string)Call(monitor,"Summary",false)=="","successful retry recovers unavailable file state");
                File.WriteAllBytes("DATA.MKF",new byte[]{9,2,3});Set(session,"NextFileScan",0L);
                for(int i=0;i<100&&!Evidence(monitor).GetValue<bool>("FileMismatchSeen");++i)AdvanceMonitor(monitor,session,view);
                Check(Evidence(monitor).GetValue<bool>("FileMismatchSeen")&&!((string)Call(monitor,"Summary",false)).Contains("[测试版]"),"changed non-DLL core remains recorded without a test-version title");
            }finally{((IDisposable)monitor).Dispose();}
        }
        var standalone=Manifest(Fixture());using(var verifier=(IDisposable)New("ReleaseIntegrityVerifier",Directory.GetCurrentDirectory(),standalone,Mode()))
        {
            int hashes=0;
            while(!(bool)Property(verifier,"Complete"))
            {
                int read=(int)Call(verifier,"AdvanceBackground");Check(read<=256*1024,"background byte cap");
                int complete=0;foreach(DictionaryEntry item in (IDictionary)Field(verifier,"results"))if(Field(item.Value,"Hash")!=null)complete++;
                Check(complete-hashes<=1,"one file completion per slice");hashes=complete;
            }
        }
    }
    static void OfficialPalDllClassification()
    {
        var manifest=Static("ReleaseIntegrityManifest","LoadEmbedded");
        object current=((IEnumerable)Property(manifest,"files")).Cast<object>().Single(f=>(string)Property(f,"path")=="PAL.dll");
        Action<long,string,string,bool> verify=(size,hash,version,missing)=>{
            using(var verifier=(IDisposable)New("ReleaseIntegrityVerifier",Directory.GetCurrentDirectory(),manifest,Mode()))
            {
                var result=New("IntegrityFileResult");Set(result,"Size",size);Set(result,"Hash",hash);Set(result,"Missing",missing);
                ((IDictionary)Field(verifier,"results"))["PAL.dll"]=result;Call(verifier,"EvaluatePalDll");
                Check(Property(verifier,"PalDllState").ToString()==(version==null?"Mismatch":"Match"),"DLL identity classification "+(version??"test"));
                Check((string)Property(verifier,"PalDllVersion")== (version??""),"DLL recognized release metadata");
            }
        };
        long currentSize=(long)Property(current,"size");string currentHash=(string)Property(current,"sha256");
        verify(currentSize,currentHash,"current",false);
        verify(526336,"B3BC8A7B53CB92A8E7910C3B6E3176CDFEB888CA50CBA79C8E26C4F8E9B634E6","1.14",false);
        verify(477184,"CB47B9E66119DE098C3D4D9BC6A1FE2D9C0672D1AFC2A13D8110F3A98A8AC8B0","1.02",false);
        verify(1986560,"252e2938d30775f0d9d1ed6f82ada672f2d7e1abab4ed37e6d0760a2d8098c51",null,false);
        verify(currentSize,new string('0',64),null,false);verify(currentSize+1,currentHash,null,false);verify(currentSize,currentHash,null,true);
        using(var verifier=(IDisposable)New("ReleaseIntegrityVerifier",Directory.GetCurrentDirectory(),manifest,Mode()))
        {
            Call(verifier,"EvaluatePalDll");Check(Property(verifier,"PalDllState").ToString()=="Incomplete","DLL not yet scanned is unclassified");
            var unreadable=New("IntegrityFileResult");Set(unreadable,"Error","access denied");((IDictionary)Field(verifier,"results"))["PAL.dll"]=unreadable;
            Call(verifier,"EvaluatePalDll");Check(Property(verifier,"PalDllState").ToString()=="Incomplete","read error is not invented test-version evidence");
        }
        var evidence=New("RuntimeIntegrityEvidence");Set(evidence,"Files",Enum.Parse(Type("IntegrityCheckState"),"Mismatch"));Set(evidence,"FileMismatchSeen",true);
        Set(evidence,"PalDll",Enum.Parse(Type("IntegrityCheckState"),"Match"));Set(evidence,"PalDllVersion","1.14");Set(evidence,"DiagnosticUnavailable",true);
        Check((string)Call(evidence,"Summary",false)=="","official legacy DLL stays quiet despite modern file and diagnostic differences");
        var export=new HObj();Call(evidence,"Fill",export,false);var details=export.GetValue<HObj>("RuntimeIntegrity");
        Check(details.GetValue<string>("FileState")=="Mismatch"&&details.GetValue<bool>("FileMismatchSeen")&&details.GetValue<bool>("DiagnosticUnavailable"),"other check results remain in score metadata");
        Check(details.GetValue<string>("PalDllState")=="Match"&&details.GetValue<string>("PalDllVersion")=="1.14","DLL classification has independent metadata");
        Set(evidence,"PalDll",Enum.Parse(Type("IntegrityCheckState"),"Mismatch"));Check((string)Call(evidence,"Summary",false)=="[测试版]","unknown DLL alone selects test-version label");
        Set(evidence,"PalDll",Enum.Parse(Type("IntegrityCheckState"),"Match"));Set(evidence,"PalDllMismatchSeen",true);
        Check((string)Call(evidence,"Summary",false)=="[测试版]","confirmed DLL mismatch remains in the same-process record");
    }
    [STAThread] static int Main(string[] args)
    {
        if(args.Length==1 && args[0]=="--integrity-child") {Thread.Sleep(30000);return 0;}
        Root=Path.GetFullPath(args[0]);Repo=Path.GetFullPath(args[1]);using(Host=Process.GetCurrentProcess())
        {
            Birth=Host.StartTime.ToUniversalTime().ToFileTimeUtc();
            Scenario("contract",Contract);Scenario("manifest_files",ManifestsAndFiles);Scenario("readonly_fixups",ReadOnlyAndFixups);
            Scenario("live_diagnostics",LiveDiagnosticsAndNoTimingGate);Scenario("legacy_versions",VersionCompatibility);Scenario("signed_locks",SignedLocks);
            Scenario("heartbeat_loss",HeartbeatLossCannotSuppressCode);Scenario("target_switch",TargetSwitchPublication);
            Scenario("indirect_trampoline",IndirectTrampoline);Scenario("settings_comments",SettingsValuesAndComments);Scenario("exact_scripts",ExactScriptDirectory);Scenario("settings_budget",SettingsBudgetAndCache);Scenario("module_duplicates",DuplicateModuleIdentity);Scenario("real_proxy_paths",RealProxyModulePaths);
            Scenario("r10_module_switches",R10ModuleSwitches);Scenario("r10_unknown_content",R10UncoveredStillChecksCore);Scenario("r10_setting_ranges",R10SettingRanges);
            Scenario("quiet_summary",QuietSuccessfulSummary);Scenario("seqlock_read",SeqlockReadAndExpiry);Scenario("periodic_scan",PeriodicVerificationStability);Scenario("diagnostic_grace",DiagnosticStartupGrace);Scenario("normal_detach",DetachedTargetClearsAvailabilityOnly);Scenario("official_dlls",OfficialPalDllClassification);
        }
        var result=new HObj();result["passed"]=Passed;result["failed"]=Failed;result["assertions"]=Assertions;result["failures"]=string.Join("\n",Failures);
        result["realGameUsed"]=false;result["realCloudUsed"]=false;
        result["maxSyntheticHashSliceMs"]=MaxHashSliceMs;result["syntheticHashProcessCpuMs"]=HashCpuMs;result["thousandObserveCallsMs"]=ObserveBatchMs;
        File.WriteAllText(Path.Combine(Root,"results.json"),result.ToJson(),new UTF8Encoding(false));
        Console.WriteLine("RESULT "+Passed+" passed, "+Failed+" failed; "+Assertions+" assertions");return Failed==0?0:1;
    }
}
