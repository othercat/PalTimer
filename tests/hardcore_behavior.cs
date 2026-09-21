using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Runtime.Serialization;
using System.Windows.Forms;
using HFrame.ENT;
using Pal98Timer;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.7.2")]

// Compiled with the project's exact Compile sources in an isolated directory.
// No game process, network, keyboard injection, or visible forms are used.
internal static class HardcoreBehavior
{
    static int checks;
    static Process host;
    static long creation;
    static void Check(bool value, string text)
    { ++checks; if (!value) throw new Exception(text); Console.WriteLine("PASS " + text); }
    static void U16(byte[] b, int o, ushort v) { Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,o,2); }
    static void U32(byte[] b, int o, uint v) { Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,o,4); }
    static void U64(byte[] b, int o, ulong v) { Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,o,8); }
    static void Text(byte[] b, int o, string t) { Buffer.BlockCopy(Encoding.UTF8.GetBytes(t),0,b,o,Encoding.UTF8.GetByteCount(t)); }
    static byte[] Bytes(uint state=2, uint dc=0, uint rc=0, ulong qpc=100)
    {
        byte[] b=new byte[1024]; U32(b,0,0x31484350);U16(b,4,1);U16(b,6,1024);
        U32(b,8,(uint)host.Id);U32(b,12,0x01060700);U64(b,16,(ulong)creation);U32(b,24,2);
        U32(b,28,state);U32(b,32,1);U32(b,36,1);U32(b,40,state==0?0u:state==2?15u:state==5?7u:3u);
        U32(b,44,state==1||state==3?7u:state==5?8u:state==4?1u:0u);
        U32(b,48,dc);U32(b,52,rc);U64(b,56,qpc);
        if(state!=0){U16(b,64,0x1234);U16(b,66,0x5678);Text(b,72,new string('a',64));Text(b,137,new string('b',64));Text(b,202,"测试USB键盘");}
        if(state==4)Text(b,458,"规则校验拒绝");return b;
    }
    static HardcoreSnapshot Decode(byte[] b) { return HardcoreModeReader.Decode(b,host.Id,creation,2,2); }
    static byte[] NewBytes(uint state=2, bool ps2=false, bool confirmation=false, uint dc=0, uint rc=0, ulong qpc=100)
    {
        var b=Bytes(state,dc,rc,qpc);U32(b,12,0x01060701);
        if(ps2){U32(b,68,2);U16(b,64,0);U16(b,66,0);Array.Clear(b,202,256);Text(b,202,"本地PS/2键盘");}
        if(confirmation){U32(b,28,1);U32(b,40,15);U32(b,44,11);Text(b,458,"请在本地键盘按下并松开一个键");}
        return b;
    }
    static HObj Record(HardcoreRunEvidence run) { var d=new HObj();run.Fill(d);return d; }
    static bool Verified(HardcoreRunEvidence run) { return Record(run).GetValue<bool>("HardcoreRunVerified"); }
    static HardcoreRunEvidence Started()
    { var r=new HardcoreRunEvidence();var s=Decode(Bytes());r.Observe(s,false);r.Observe(s,true);Check(Verified(r),"continuous Active start verified");return r; }
    static void Invalid(string name, Action<byte[]> mutation)
    { var b=Bytes();mutation(b);Check(Decode(b)==null,name); }

    static void Decoder()
    {
        foreach(uint state in new uint[]{0,1,2,3,4,5})Check(Decode(Bytes(state,state==3?1u:0))!=null,"valid state "+state);
        Check(HardcoreModeReader.Decode(new byte[1023],host.Id,creation,2,2)==null,"short frame rejected");
        Check(HardcoreModeReader.Decode(Bytes(),host.Id,creation,1,1)==null,"odd seqlock rejected");
        Check(HardcoreModeReader.Decode(Bytes(),host.Id,creation,2,4)==null,"changed seqlock rejected");
        Check(HardcoreModeReader.Decode(Bytes(),host.Id+1,creation,2,2)==null,"PID mismatch rejected");
        Check(HardcoreModeReader.Decode(Bytes(),host.Id,creation+1,2,2)==null,"creation mismatch rejected");
        Invalid("magic",b=>b[0]=0);Invalid("version",b=>U16(b,4,2));Invalid("size",b=>U16(b,6,1023));
        Invalid("old producer",b=>U32(b,12,0x01060600));Invalid("zero rules",b=>U32(b,32,0));Invalid("zero blacklist",b=>U32(b,36,0));
        Invalid("unknown transport",b=>b[68]=1);Invalid("reserved tail",b=>b[1023]=1);Invalid("state",b=>U32(b,28,6));
        Invalid("unknown flags",b=>U32(b,40,31));Invalid("Active without focus",b=>U32(b,40,7));Invalid("Active bad reason",b=>U32(b,44,8));
        Invalid("reconnect greater than disconnect",b=>U32(b,52,1));Invalid("empty VID",b=>U16(b,64,0));Invalid("empty PID",b=>U16(b,66,0));
        Invalid("uppercase hash",b=>b[72]=(byte)'A');Invalid("nonzero string padding",b=>b[450]=1);
        Invalid("invalid UTF8",b=>{b[202]=0xc0;b[203]=0xaf;});Invalid("unterminated text",b=>{for(int i=202;i<458;i++)b[i]=65;});
        var rejected=Bytes(4);Array.Clear(rejected,72,65);Array.Clear(rejected,137,65);Array.Clear(rejected,202,256);U16(rejected,64,0);U16(rejected,66,0);
        Check(Decode(rejected)!=null,"Rejected may lack binding identity");
        Array.Clear(rejected,458,256);Check(Decode(rejected)==null,"Rejected requires reason text");
    }

    static void RuleVersions()
    {
        foreach(uint version in new uint[]{1,2,3,4,uint.MaxValue})
        foreach(uint blacklist in new uint[]{1,2})
        {
            var bytes=NewBytes();U32(bytes,12,0x01060703);U32(bytes,32,version);U32(bytes,36,blacklist);
            var snapshot=Decode(bytes);bool supported=version<=3&&blacklist==1;
            Check(snapshot!=null&&snapshot.Requested&&snapshot.RulesSupported==supported,"rule request retained "+version+"/"+blacklist);
            var guard=new HardcoreKeyChangerGuard(()=>snapshot.Requested);
            Check(guard.BeginAutoStart()<0&&guard.BeginRequest()<0&&!guard.TryRun(()=>{throw new Exception("remap ran");}),
                "known and future rules block every remap request "+version+"/"+blacklist);
            var run=new HardcoreRunEvidence();run.Observe(snapshot,false);run.Observe(snapshot,true);
            Check(Verified(run)==supported,"only supported rules certify a run "+version+"/"+blacklist);
            Check(run.CaptureDisplay().Visible&&(supported?run.CaptureDisplay().Status.Contains("生效"):
                run.CaptureDisplay().Status.Contains("请更新计时器")),"rule status is visible "+version+"/"+blacklist);
        }
    }

    static void ToolVersions()
    {
        foreach(uint reason in new uint[]{12,13})
        {
            var bytes=Bytes(reason==12?4u:1u);U32(bytes,12,0x01060705);U32(bytes,44,reason);
            Array.Clear(bytes,458,256);Text(bytes,458,"关闭旧工具，更新后按 P 重启");
            var snapshot=Decode(bytes);
            Check(snapshot!=null&&snapshot.Requested&&snapshot.StateLabel.Contains("工具"),"tool gate decoded and displayed "+reason);
            var guard=new HardcoreKeyChangerGuard(()=>snapshot.Requested);
            Check(guard.BeginAutoStart()<0&&guard.BeginRequest()<0,"tool gate never reopens remapping "+reason);
            var run=Started();run.Observe(snapshot,true);Check(!Verified(run),"tool gate invalidates current run "+reason);
            U32(bytes,12,0x01060704);Check(Decode(bytes)==null,"old producer cannot claim new tool gate reason "+reason);
            U32(bytes,12,0x01060705);U32(bytes,28,2);U32(bytes,40,15);
            Check(Decode(bytes)==null,"tool gate cannot report Active "+reason);
        }
    }

    static void PublishedRequestProtection()
    {
        Func<Process[]> processes=()=>new[]{Process.GetProcessById(host.Id)};
        Check(!new HardcoreRequestedProcesses(processes).Read(),"no publication preserves ordinary behavior");
        using(var mapping=MemoryMappedFile.CreateNew(HardcoreModeReader.MappingPrefix+host.Id,1024))
        using(var view=mapping.CreateViewAccessor())
        {
            foreach(uint version in new uint[]{1,2,3,4})
            {
                var b=NewBytes();U32(b,32,version);view.WriteArray(0,b,0,1024);
                Check(new HardcoreRequestedProcesses(processes).Read(),"first published request blocks remapping rules "+version);
            }
            view.Write(24,3);
            Check(new HardcoreRequestedProcesses(processes).Read(),"first torn publication blocks remapping");
            view.WriteArray(0,Bytes(0),0,1024);
            Check(!new HardcoreRequestedProcesses(processes).Read(),"validated Off publication preserves ordinary behavior");
        }
    }

    static void KeyChangerExecution()
    {
        // No form constructor/global hook, no native keyboard injection. Exercise
        // the real helper callback and restore handlers with a controlled reader.
        var assembly=Assembly.LoadFrom("KeyChanger.exe");var type=assembly.GetType("KeyChanger.MainForm");
        var form=FormatterServices.GetUninitializedObject(type);
        var kcType=assembly.GetType("KeyChanger.KC");
        var kc=Activator.CreateInstance(kcType,new object[]{"1\n65:66"});
        Action<string,object> set=(name,value)=>type.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).SetValue(form,value);
        Func<string,object[],object> call=(name,args)=>type.GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.DeclaredOnly).Invoke(form,args);
        bool blocked=true;int injections=0;
        set("kc",kc);set("KeyStat",new int[2020]);
        set("readHardcoreRequested",(Func<bool>)(()=>blocked));
        set("injectKey",(Action<int,int>)((key,flags)=>++injections));
        set("btnEnable",new ToolStripMenuItem());set("btnBlockCE",new ToolStripMenuItem());
        var packetType=assembly.GetType("KeyChanger.KeyboardLib+HookStruct");
        foreach(int key in new[]{65,13})foreach(int flags in new[]{0,128})
        {
            kcType.GetField("IsEnable").SetValue(kc,true);set("BlockCtrlEnter",true);set("OnCtrlDown",true);
            var packet=Activator.CreateInstance(packetType);packetType.GetField("vkCode").SetValue(packet,key);packetType.GetField("flags").SetValue(packet,flags);
            object[] args={packet,true};call("OnKeyPress",args);
            Check(!(bool)args[1]&&injections==0&&!(bool)kcType.GetField("IsEnable").GetValue(kc),"helper blocked callback never swallows or injects "+key+"/"+flags);
            var shown=(int[])type.GetField("KeyStat",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
            Check(shown[key]==(flags>=128?0:key),"hardcore still renders physical press/release "+key+"/"+flags);
            call("RefreshHardcoreProtection",new object[0]);
            Check(shown[key]==(flags>=128?0:key),"repeated hardcore check preserves display state");
        }
        call("btnEnable_Click",new object[]{null,EventArgs.Empty});
        Check(!(bool)kcType.GetField("IsEnable").GetValue(kc),"helper tray cannot enable while requested");
        File.WriteAllText("keychange.txt","1\n65:66");call("ApplyKeyChange",new object[0]);
        kc=type.GetField("kc",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
        Check(!(bool)kcType.GetField("IsEnable").GetValue(kc),"helper old settings save cannot restore enable");
        blocked=false;call("RefreshHardcoreProtection",new object[0]);
        Check(!(bool)kcType.GetField("IsEnable").GetValue(kc),"helper does not auto-enable after hardcore ends");
        call("btnEnable_Click",new object[]{null,EventArgs.Empty});
        var normal=Activator.CreateInstance(packetType);packetType.GetField("vkCode").SetValue(normal,65);
        object[] normalArgs={normal,false};call("OnKeyPress",normalArgs);
        Check((bool)normalArgs[1]&&injections==1,"ordinary explicit enable retains remapping through controlled output");
    }

    static void NativeSnapshot(string path)
    {
        var bytes=File.ReadAllBytes(path);
        var s=HardcoreModeReader.Decode(bytes,BitConverter.ToInt32(bytes,8),BitConverter.ToInt64(bytes,16),
            BitConverter.ToInt32(bytes,24),BitConverter.ToInt32(bytes,24));
        Check(s!=null&&s.Requested&&s.RulesSupported&&s.RulesVersion==3,"current native host snapshot accepted by actual timer consumer");
        var run=new HardcoreRunEvidence();run.Observe(s,false);
        Check(run.CaptureDisplay().Visible,"actual native waiting snapshot has a hardcore display");
    }

    static void TransportAndConfirmation()
    {
        var legacy=Decode(Bytes());
        Check(legacy.KeyboardTransport==HardcoreKeyboardTransport.Usb&&legacy.DeviceLabel.StartsWith("VID:1234 PID:5678 "),"old producer zero transport remains USB");
        foreach(bool ps2 in new[]{false,true})
        {
            string kind=ps2?"PS/2":"USB";
            foreach(uint state in new uint[]{1,2,3,4,5})
                Check(Decode(NewBytes(state,ps2,dc:state==3?1u:0))!=null,"new "+kind+" valid state "+state);
            foreach(uint flags in new uint[]{7,15})
            {
                var waiting=NewBytes(ps2:ps2,confirmation:true,dc:2,rc:1);U32(waiting,40,flags);
                var s=Decode(waiting);
                Check(s!=null&&s.State==HardcoreState.Waiting&&s.Requested&&s.StateLabel=="硬核待本地按键确认",
                    kind+" confirmation remains Waiting with counts and flags "+flags);
                U32(waiting,12,0x01060700);Check(Decode(waiting)==null,"old producer cannot publish confirmation "+kind+" flags "+flags);
            }
        }
        var ps2Active=NewBytes(ps2:true);
        Check(Decode(ps2Active).DeviceLabel=="PS/2 本地PS/2键盘","PS/2 label has no fictitious VID/PID");
        U32(ps2Active,12,0x01060700);Check(Decode(ps2Active)==null,"old producer cannot publish PS/2");
        foreach(uint transport in new uint[]{1,3,uint.MaxValue})
        {var b=NewBytes();U32(b,68,transport);Check(Decode(b)==null,"unsupported transport rejected "+transport);}
        foreach(int offset in new[]{64,66})
        {
            var b=NewBytes(ps2:true);U16(b,offset,1);Check(Decode(b)==null,"PS/2 nonzero identity rejected "+offset);
            b=NewBytes();U16(b,offset,0);Check(Decode(b)==null,"new USB missing identity rejected "+offset);
        }
        var missing=NewBytes(1,true,dc:1);Check(Decode(missing)==null,"device-missing Waiting still forbids disconnect counts");
        var bad=NewBytes(confirmation:true);Array.Clear(bad,458,256);Check(Decode(bad)==null,"confirmation requires explanation");
        foreach(uint flags in new uint[]{0,1,3,9,11})
        {bad=NewBytes(confirmation:true);U32(bad,40,flags);Check(Decode(bad)==null,"confirmation requires matched device flags "+flags);}
        foreach(uint state in new uint[]{0,2,3,4,5})
        {bad=NewBytes(confirmation:true);U32(bad,28,state);Check(Decode(bad)==null,"confirmation cannot masquerade as state "+state);}
        bad=NewBytes(ps2:true,confirmation:true,dc:1,rc:2);Check(Decode(bad)==null,"confirmation reconnect bound retained");
        bad=NewBytes(ps2:true);bad[714]=1;Check(Decode(bad)==null,"new transport retains zero tail requirement");

        var waitingPs2=Decode(NewBytes(ps2:true,confirmation:true));var activePs2=Decode(NewBytes(ps2:true));
        var r=new HardcoreRunEvidence();r.Observe(waitingPs2,false);r.Observe(waitingPs2,true);
        Check(!Verified(r)&&Record(r).GetValue<string>("HardcoreKeyboardTransport")=="unknown","waiting start has no certified run transport");
        r.Observe(activePs2,true);r.Observe(activePs2,true);Check(!Verified(r),"local confirmation after start cannot upgrade waiting run");
        r.Reset();r.Observe(waitingPs2,false);r.Observe(activePs2,true);Check(!Verified(r),"confirmation at start lacks continuous Active evidence");
        r.Reset();r.Observe(waitingPs2,false);r.Observe(activePs2,false);r.Observe(activePs2,true);
        Check(Verified(r)&&Record(r).GetValue<string>("HardcoreKeyboardTransport")=="ps2","confirmation before start permits own PS/2 run identity");
        r.Observe(Decode(NewBytes(3,true,dc:1,qpc:200)),true);r.Observe(Decode(NewBytes(ps2:true,dc:1,rc:1,qpc:300)),true);
        Check(Verified(r)&&Record(r).GetValue<uint>("HardcoreReconnectCount")==1,"confirmed PS/2 reconnect preserves run and count");
        Check(Record(r).GetValue<string>("HardcoreHardwareReview")=="unknown","PS/2 hardware review remains manual unknown");
        r.Observe(Decode(NewBytes(dc:1,rc:1,qpc:400)),true);
        Check(!Verified(r)&&Record(r).GetValue<string>("HardcoreKeyboardTransport")=="ps2","transport change invalidates and retains original run identity");
        r.Reset();Check(Record(r).GetValue<string>("HardcoreKeyboardTransport")=="unknown","Reset discards run transport");
        r.ImportUnverified();r.Observe(activePs2,false);r.Observe(activePs2,true);
        Check(!Verified(r)&&Record(r).GetValue<string>("HardcoreKeyboardTransport")=="unknown","import cannot inherit or upgrade transport");
        r=Started();Check(Record(r).GetValue<string>("HardcoreKeyboardTransport")=="usb","legacy USB run exports USB transport");
        r=new HardcoreRunEvidence();var newUsb=Decode(NewBytes());r.Observe(newUsb,false);r.Observe(newUsb,true);
        r.Observe(Decode(NewBytes(confirmation:true,dc:1,rc:1,qpc:200)),true);
        Check(!Verified(r),"confirmed run returning to Waiting confirmation becomes unverified");
        Check(!newUsb.SameRun(activePs2),"USB and PS/2 bindings are different run identities");
    }

    static void Runs()
    {
        var active=Decode(Bytes());var off=Decode(Bytes(0));var r=new HardcoreRunEvidence();
        r.Observe(off,false);r.Observe(off,true);Check(!Verified(r),"ordinary not verified");
        Check(!r.CaptureDisplay().Visible,"ordinary never-requested UI retains original layout");
        r.Reset();r.Observe(null,true);Check(!r.CaptureDisplay().Visible,"missing snapshot UI retains original layout");
        r.Observe(active,true);r.Observe(active,true);Check(!Verified(r),"late confirmation cannot upgrade old run");
        r=Started();r.Observe(Decode(Bytes(3,1,0,200)),true);Check(Verified(r),"same device disconnect preserves run");
        Check(r.CaptureDisplay().Status.Contains("断开"),"disconnect is visible");r.Observe(Decode(Bytes(2,1,1,300)),true);
        Check(Verified(r),"same device reconnect preserves run");Check(Record(r).GetValue<uint>("HardcoreReconnectCount")==1,"reconnect count exported");
        Check(Record(r).GetValue<string>("HardcoreHardwareReview")=="unknown"&&!Record(r).GetValue<bool>("HardcoreHardwareVerified"),"hardware remains unknown");
        r.Observe(Decode(Bytes(5,1,1,400)),true);Check(Verified(r),"unfocus preserves run");
        r.Complete();r.Observe(null,true);Check(Verified(r)&&Record(r).GetValue<string>("HardcoreRuntimeState")=="Unknown","finished evidence frozen but current unknown");
        r.Reset();Check(!Verified(r)&&!Record(r).GetValue<bool>("HardcoreRunCompleted"),"reset clears finish and identity");
        r=Started();r.ImportUnverified();r.Observe(active,false);r.Observe(active,true);Check(!Verified(r)&&Record(r).GetValue<string>("HardcoreBindingSha256")=="","import cannot restore identity");
        r=Started();var change=Bytes();change[137]=(byte)'c';r.Observe(Decode(change),true);r.Observe(active,true);Check(!Verified(r),"binding change cannot be undone");
        r=Started();change=Bytes();change[72]=(byte)'c';r.Observe(Decode(change),true);Check(!Verified(r),"config change invalidates");
        r=Started();change=Bytes();U32(change,8,(uint)(host.Id+1));var other=HardcoreModeReader.Decode(change,host.Id+1,creation,2,2);r.Observe(other,true);Check(!Verified(r),"process change invalidates");
        r=Started();r.Observe(null,true);r.Observe(active,true);Check(!Verified(r),"missing frame cannot later upgrade");
        r=Started();r.Observe(Decode(Bytes(4)),true);Check(!Verified(r),"rejected during run invalidates");
        r=Started();r.Observe(Decode(Bytes(2,1,1,300)),true);r.Observe(active,true);
        Check(!Verified(r)&&Record(r).GetValue<string>("HardcoreValidationError").Contains("计数回退"),"actual producer counter rollback remains invalid");
    }

    static void Guard()
    {
        bool requested=false;int calls=0;var g=new HardcoreKeyChangerGuard(()=>requested);
        Check(g.BeginAutoStart()>=0&&g.BeginAutoStart()<0,"ordinary startup once");
        requested=true;Check(g.BeginAutoStart()<0&&!g.TryRun(()=>++calls)&&calls==0,"Requested blocks start and guarded action");
        requested=false;Check(g.BeginAutoStart()<0&&g.TryRun(()=>++calls)&&calls==1,"ordinary restores manual access without auto-start");
        var entered=new ManualResetEvent(false);var release=new ManualResetEvent(false);var refreshed=new ManualResetEvent(false);
        bool revoked=false;
        var editor=new Thread(()=>g.TryRun(()=>{entered.Set();release.WaitOne();},revoke:()=>revoked=true));editor.IsBackground=true;editor.Start();
        Check(entered.WaitOne(2000),"external action entered");requested=true;
        var ui=new Thread(()=>{g.Refresh();refreshed.Set();});ui.IsBackground=true;ui.Start();
        bool responsive=refreshed.WaitOne(1000);release.Set();editor.Join(2000);ui.Join(2000);
        Check(responsive&&revoked,"modal action leaves Refresh responsive and is revoked after Requested");
        requested=false;long oldRequest=g.BeginRequest();Check(g.TryRun(()=>{},oldRequest),"ordinary F11 open accepted");
        requested=true;g.Refresh();requested=false;
        Check(!g.TryRun(()=>++calls,oldRequest),"old F11 enable rejected after Requested then ordinary");
        Check(!g.TryRun(()=>++calls,oldRequest),"old menu edit rejected by the same generation");
        Check(g.TryRun(()=>++calls,g.BeginRequest()),"fresh manual request allowed after ordinary restoration");
        g=new HardcoreKeyChangerGuard(()=>requested);long autoRequest=g.BeginAutoStart();requested=true;g.Refresh();requested=false;
        Check(!g.TryRun(()=>++calls,autoRequest),"deferred automatic start rejected after Requested edge");
        bool failedRevoked=false;long throwingRequest=g.BeginRequest();
        try {g.TryRun(()=>{requested=true;throw new InvalidOperationException();},throwingRequest,()=>failedRevoked=true);}
        catch(InvalidOperationException){}
        Check(failedRevoked,"failed in-flight action still revoked after Requested");
    }

    static void Set(object obj,string name,object value)
    { for(Type t=obj.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);if(f!=null){f.SetValue(obj,value);return;}}throw new Exception(name); }
    static object Field(object obj,string name)
    { for(Type t=obj.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);if(f!=null)return f.GetValue(obj);}throw new Exception(name); }
    static void ObserveCore(仙剑98柔情DX9 c,bool begin)
    { typeof(仙剑98柔情DX9).GetMethod("ObserveHardcoreRuntime",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c,new object[]{host,begin}); }
    static void Cores()
    {
        using(var mapping=MemoryMappedFile.CreateNew(HardcoreModeReader.MappingPrefix+host.Id,1024))
        using(var view=mapping.CreateViewAccessor())
        {
            var reader=new HardcoreModeReader();view.WriteArray(0,Bytes(),0,1024);Check(reader.Read(host).State==HardcoreState.Active,"real readonly mapping Active");
            view.WriteArray(0,Bytes(3,1,0,200),0,1024);Check(reader.Read(host).State==HardcoreState.Disconnected,"reader does not cache Active");
            view.Write(24,3);Check(reader.Read(host)==null,"reader rejects in-progress mapped frame");
            foreach(var c in new 仙剑98柔情DX9[]{new 仙剑98柔情DX9(null),new Pal98Dx9Fast800(null),new Pal98Dx9Fast800Speed(null)})
            {
                view.WriteArray(0,Bytes(),0,1024);Set(c,"PalProcess",host);ObserveCore(c,false);ObserveCore(c,true);
                var data=new HObj(c.GetTimerJson());Check(data.GetValue<bool>("HardcoreRunVerified"),c.CoreName+" uses common runtime evidence export");
                var timer=(PTimer)Field(c,"MT");timer.Start();view.WriteArray(0,Bytes(3,1,0,200),0,1024);ObserveCore(c,true);
                Check(timer.IsRunning,c.CoreName+" observation does not pause timer");
                timer.Stop();view.WriteArray(0,Bytes(2,1,1,300),0,1024);ObserveCore(c,true);Check(new HObj(c.GetTimerJson()).GetValue<bool>("HardcoreRunVerified"),c.CoreName+" reconnect retained");
                c.Reset();Check(!new HObj(c.GetTimerJson()).GetValue<bool>("HardcoreRunVerified"),c.CoreName+" Reset clears verification");
                view.WriteArray(0,NewBytes(ps2:true,confirmation:true),0,1024);ObserveCore(c,false);ObserveCore(c,true);
                view.WriteArray(0,NewBytes(ps2:true),0,1024);ObserveCore(c,true);
                data=new HObj(c.GetTimerJson());
                Check(!data.GetValue<bool>("HardcoreRunVerified")&&data.GetValue<string>("HardcoreKeyboardTransport")=="unknown",c.CoreName+" waiting start cannot upgrade through real mapping");
                c.Reset();ObserveCore(c,false);ObserveCore(c,true);data=new HObj(c.GetTimerJson());
                Check(data.GetValue<bool>("HardcoreRunVerified")&&data.GetValue<string>("HardcoreKeyboardTransport")=="ps2",c.CoreName+" confirmed PS/2 identity exported through common core");
            }
        }
    }

    sealed class ControlledReader : IHardcoreModeReader
    {
        internal HardcoreSnapshot First, Later;
        internal int Calls;
        internal readonly ManualResetEvent Entered=new ManualResetEvent(false), Release=new ManualResetEvent(false);
        internal bool Hold;
        public HardcoreSnapshot Read(Process ignored)
        {
            int number=Interlocked.Increment(ref Calls);
            if(number==1&&Hold){Entered.Set();if(!Release.WaitOne(5000))throw new TimeoutException("reader release");}
            return number==1?First:Later;
        }
    }
    static HardcoreRunEvidence Evidence(仙剑98柔情DX9 c) {return (HardcoreRunEvidence)Field(c,"hardcoreRun");}
    static 仙剑98柔情DX9 StartedCore()
    {
        var c=new 仙剑98柔情DX9(null);Set(c,"PalProcess",host);
        Set(c,"hardcoreReader",new ControlledReader{First=Decode(Bytes()),Later=Decode(Bytes())});
        ObserveCore(c,false);ObserveCore(c,true);Check(Verified(Evidence(c)),"core begins with own runtime identity");return c;
    }
    static void ConcurrentBoundary(string operation)
    {
        var c=StartedCore();var reader=new ControlledReader{Hold=true,First=Decode(Bytes(3,1,0,200)),Later=Decode(Bytes(2,1,1,300))};
        Set(c,"hardcoreReader",reader);Set(c,"_hasCallPointEnd",true);
        Exception firstError=null,secondError=null;HObj exported=null;
        var secondStarted=new ManualResetEvent(false);var secondDone=new ManualResetEvent(false);
        var first=new Thread(()=>{try{ObserveCore(c,false);}catch(Exception e){firstError=e;}});first.IsBackground=true;first.Start();
        Check(reader.Entered.WaitOne(2000),operation+" held after actual read entry");
        var second=new Thread(()=>{
            secondStarted.Set();
            try{
                if(operation=="export")exported=new HObj(c.GetTimerJson());
                else if(operation=="reset")c.Reset();
                else if(operation=="finish")typeof(仙剑98柔情DX9).GetMethod("OnCheckPointEnd",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c,null);
                else c.SetTimerFromString("{"); // malformed before optional PAL attach; verifies the public import boundary
            }catch(Exception e){secondError=e;}finally{secondDone.Set();}
        });second.IsBackground=true;second.Start();
        Check(secondStarted.WaitOne(2000),operation+" concurrent caller started");
        bool correctlyWaited=!secondDone.WaitOne(150)&&reader.Calls==1;
        reader.Release.Set();Check(first.Join(3000)&&second.Join(3000),operation+" threads completed");
        Check(correctlyWaited,operation+" cannot overtake read and commit");Check(firstError==null,operation+" first observer succeeded");
        if(operation=="import"){Check(secondError!=null,"malformed import rejected after synchronized boundary");return;}
        Check(secondError==null,operation+" operation succeeded");
        if(operation=="export")Check(exported.GetValue<bool>("HardcoreRunVerified")&&exported.GetValue<uint>("HardcoreReconnectCount")==1,
            "concurrent newer export follows older observation without false rollback");
        else if(operation=="reset")Check(!Verified(Evidence(c))&&!Record(Evidence(c)).GetValue<bool>("HardcoreRunCompleted"),"Reset wins after in-flight observation");
        else{
            Check(Verified(Evidence(c))&&Record(Evidence(c)).GetValue<bool>("HardcoreRunCompleted"),"finish freezes final synchronized observation");
            Set(c,"hardcoreReader",new ControlledReader());ObserveCore(c,false);
            Check(Verified(Evidence(c))&&Record(Evidence(c)).GetValue<string>("HardcoreRuntimeState")=="Unknown","exit after finish cannot rewrite completed run");
        }
    }

    static void Display()
    {
        var run=new HardcoreRunEvidence();var a=Bytes();Array.Clear(a,202,256);Text(a,202,"Alpha");
        var b=Bytes(3,1,0,200);Array.Clear(b,202,256);Text(b,202,"Beta");
        var first=Decode(a);var second=Decode(b);run.Observe(first,false);var captured=run.CaptureDisplay();
        run.Observe(second,false);Check(captured.Status.Contains("生效")&&captured.Device.EndsWith("Alpha"),"display capture is immutable");
        Check(captured.Device.StartsWith("VID:1234 PID:5678 "),"device identity prefixes long name");
        var writer=new Thread(()=>{for(int i=0;i<10000;i++)run.Observe((i&1)==0?first:second,false);});writer.Start();
        bool consistent=true;for(int i=0;i<10000;i++){var d=run.CaptureDisplay();consistent&=d.Status.Contains("生效")?d.Device.EndsWith("Alpha"):d.Device.EndsWith("Beta");}
        writer.Join();Check(consistent,"display status and device always come from one observation");
        string longName=new string('键',75);var longBytes=Bytes();Array.Clear(longBytes,202,256);Text(longBytes,202,longName);
        run.Observe(Decode(longBytes),false);var display=run.CaptureDisplay();
        using(var panel=new Panel{Size=new Size(327,600)})
        {
            var render=new GRender(panel);var board=new GBoard();render.SetGBoard(board);
            render.SetGameVersion("仙剑98原版 新补丁 "+new string('版',100));render.SetTitle("硬核界面回归");render.SetMainTimer(TimeSpan.FromSeconds(1234));
            render.Draw();Rectangle ordinaryItems=(Rectangle)Field(render,"rcItems"),ordinaryDots=(Rectangle)Field(render,"rcDots");
            render.SetHardcoreDisplay(display);render.Draw();
            var status=(Rectangle)Field(render,"rcHardcoreStatus");var device=(Rectangle)Field(render,"rcHardcoreDevice");
            var version=(Rectangle)Field(render,"rcGameVersion");var dots=(Rectangle)Field(render,"rcDots");var items=(Rectangle)Field(render,"rcItems");
            Check(status.Top>=version.Bottom&&device.Top>=status.Bottom&&dots.Top>=device.Bottom&&items.Top>=dots.Bottom,"independent main rows do not overlap version, dots or route");
            Check(status.Width==317&&device.Width==317,"hardcore uses full main-window width");
            using(var bitmap=new Bitmap(panel.Width,panel.Height))using(var g=Graphics.FromImage(bitmap))
            {g.Clear(Color.FromArgb(30,30,30));g.DrawImage(panel.BackgroundImage,0,0);bitmap.Save("hardcore-main.png",ImageFormat.Png);}
            render.SetHardcoreDisplay(HardcoreDisplaySnapshot.Empty);render.Draw();
            Check((Rectangle)Field(render,"rcItems")==ordinaryItems&&(Rectangle)Field(render,"rcDots")==ordinaryDots,"ordinary restores exact original main layout");
        }
        var snapshot=new Dx9OverlaySnapshot(IntPtr.Zero,"SimSun","00:20:34","12s","0s","蜂2 蜜3",0,
            new Dx9OverlayTimelineEntry(),new Dx9OverlayTimelineEntry(),new Dx9OverlayTimelineEntry(),"",false,false,"1.2秒",display.Status,display.Device);
        using(var overlay=new Dx9OverlayForm(()=>snapshot,Dx9OverlayLayoutSettings.CreateDefault()))
        {
            Set(overlay,"CurrentSnapshot",snapshot);overlay.ClientSize=new Size(520,320);
            float expanded=(float)typeof(Dx9OverlayForm).GetMethod("GetOverlayHeightLogicalPixels",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(overlay,null);
            using(var bitmap=new Bitmap(520,320))using(var graphics=Graphics.FromImage(bitmap))
            {graphics.Clear(Color.FromArgb(30,30,30));typeof(Dx9OverlayForm).GetMethod("OnPaint",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(overlay,new object[]{new PaintEventArgs(graphics,new Rectangle(0,0,520,320))});bitmap.Save("hardcore-obs.png",ImageFormat.Png);}
            Set(overlay,"CurrentSnapshot",new Dx9OverlaySnapshot(IntPtr.Zero,"SimSun","0","","","",0,new Dx9OverlayTimelineEntry(),new Dx9OverlayTimelineEntry(),new Dx9OverlayTimelineEntry(),"",false,false,"1.2秒"));
            float ordinary=(float)typeof(Dx9OverlayForm).GetMethod("GetOverlayHeightLogicalPixels",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(overlay,null);
            Check(expanded>ordinary,"OBS reserves separate hardcore rows only when shown");
        }
    }
    [STAThread]
    static int Main(string[] args)
    {
        try{host=Process.GetCurrentProcess();creation=host.StartTime.ToUniversalTime().ToFileTimeUtc();Decoder();RuleVersions();ToolVersions();PublishedRequestProtection();KeyChangerExecution();TransportAndConfirmation();Runs();Guard();Cores();
            if(args.Length==1)NativeSnapshot(args[0]);
            foreach(string operation in new[]{"export","reset","finish","import"})ConcurrentBoundary(operation);Display();
            Console.WriteLine("CHECKS="+checks+" FAILURES=0");return 0;}
        catch(Exception e){Console.WriteLine(e);return 1;}
    }
}
