using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.7.2")]

// Developer evidence host: no game launch, termination, injection, memory write,
// cloud session, or external replacement of the compiled release manifest.
internal static class V169ReadonlyProbe
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Assembly Product;
    static Type Type(string name) { return Product.GetType("Pal98Timer."+name,true); }
    static object New(string name,params object[] args) { return Activator.CreateInstance(Type(name),Members,null,args,null); }
    static object Field(object value,string name) { return value.GetType().GetField(name,Members).GetValue(value); }
    static object Call(object value,string name,params object[] args) { return value.GetType().GetMethod(name,Members).Invoke(value,args); }
    static void WaitWorker(object monitor)
    {
        var watch=Stopwatch.StartNew();
        while((int)Field(monitor,"working")!=0 && watch.ElapsedMilliseconds<5000)Thread.Sleep(10);
        if((int)Field(monitor,"working")!=0)throw new IOException("Readonly diagnostic worker did not finish in 5 seconds");
    }
    static Dictionary<string,object> Snapshot(object monitor,int sample,long elapsed)
    {
        var evidence=Field(monitor,"evidence");var runtime=Field(evidence,"Runtime");var identity=Field(evidence,"Identity");
        var result=new Dictionary<string,object> {
            {"sample",sample},{"elapsed_ms",elapsed},{"pid",identity==null?0:Field(identity,"Pid")},
            {"creation_filetime",identity==null?"":Field(identity,"CreationTime").ToString()},
            {"files",Field(evidence,"Files").ToString()},{"code",Field(evidence,"Code").ToString()},
            {"heartbeat_valid",Field(evidence,"HeartbeatValid")},{"alerts",Field(evidence,"StickyAlerts")},
            {"protection",runtime==null?"Unknown":Field(runtime,"Protection").ToString()},
            {"summary",Call(monitor,"Summary",false)},{"detail",Field(evidence,"Detail")},
            {"release_id",Field(evidence,"ReleaseId")},{"release_frozen",Field(evidence,"Frozen")},
            {"graphics_chain",Field(evidence,"GraphicsChain")}
        };
        var session=Field(monitor,"session");
        if(session!=null)
        {
            var checkedRegions=Field(session,"CheckedRegions");var names=new List<string>();
            foreach(var name in (System.Collections.IEnumerable)checkedRegions)names.Add((string)name);
            result["checked_regions"]=names.ToArray();
            var mode=Field(session,"Mode");result["content_id"]=mode==null?"Unknown":Field(mode,"ContentId");
        }
        return result;
    }
    static int Main(string[] args)
    {
        try
        {
            if(args.Length!=6)throw new ArgumentException("Expected: timer-exe pid creation-filetime expected-pal-exe seconds output-json");
            Product=Assembly.LoadFrom(Path.GetFullPath(args[0]));int pid=int.Parse(args[1]);long creation=long.Parse(args[2]);
            string expected=Path.GetFullPath(args[3]);int seconds=int.Parse(args[4]);
            if(pid<=0 || creation<=0 || seconds<5 || seconds>120)throw new ArgumentException("Invalid pinned process identity or duration");
            var snapshots=new List<Dictionary<string,object>>();var watch=Stopwatch.StartNew();
            using(var reader=(IDisposable)New("IntegrityProcessReader",pid))
            using(var process=Process.GetProcessById(pid))
            using(var monitor=(IDisposable)New("RuntimeIntegrityMonitor"))
            {
                var identity=Field(reader,"Identity");
                if((int)Field(identity,"Pid")!=pid || (long)Field(identity,"CreationTime")!=creation ||
                    !expected.Equals((string)Field(identity,"ExecutablePath"),StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Target identity differs from explicitly requested process; no observation performed");
                while(watch.Elapsed.TotalSeconds<seconds)
                {
                    if(!(bool)Call(reader,"IsCurrent"))throw new IOException("Pinned target exited or changed");
                    Call(monitor,"Observe",process);WaitWorker(monitor);
                    snapshots.Add(Snapshot(monitor,snapshots.Count,watch.ElapsedMilliseconds));
                    Thread.Sleep(250);
                }
                if(!(bool)Call(reader,"IsCurrent"))throw new IOException("Pinned target changed before evidence completion");
            }
            var result=new Dictionary<string,object> {
                {"schema","PAL98.ReadonlyIntegrityProbe.v1"},{"pid",pid},{"creation_filetime",creation.ToString()},
                {"expected_executable",expected},{"timer_assembly",Path.GetFullPath(args[0])},{"elapsed_ms",watch.ElapsedMilliseconds},
                {"read_only",true},{"game_launched",false},{"real_cloud_used",false},{"samples",snapshots.ToArray()},
                {"final",snapshots[snapshots.Count-1]}
            };
            var serializer=new JavaScriptSerializer { MaxJsonLength=4*1024*1024 };
            File.WriteAllText(Path.GetFullPath(args[5]),serializer.Serialize(result),new UTF8Encoding(false));
            Console.WriteLine(serializer.Serialize(snapshots[snapshots.Count-1]));return 0;
        }
        catch(Exception ex)
        {
            while(ex is TargetInvocationException)ex=ex.InnerException;
            Console.Error.WriteLine(ex.GetType().Name+": "+ex.Message);return 1;
        }
    }
}
