using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Load the shipped executable as a library. No GForm, keyboard hook, game,
// Internet connection or streaming software is started. Use a private desktop.
internal static class TimerOverlayWindowBehavior
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private delegate bool WindowCallback(IntPtr hwnd, IntPtr ignored);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr value);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder title, int count);
    [DllImport("user32.dll", EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr hdc, uint options);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private static readonly List<string> results = new List<string>();
    private static readonly string title = "PalTimer offline capture fixture " + Guid.NewGuid().ToString("N");
    private static object Call(object o, string name, params object[] args) =>
        o.GetType().GetMethod(name, All).Invoke(o, args);
    private static bool Property(object o, string name) =>
        (bool)o.GetType().GetProperty(name, All).GetValue(o);
    private static void Check(bool ok, string name)
    {
        if (!ok) throw new InvalidDataException(name);
        results.Add("PASS " + name);
    }
    private static bool Wait(Func<bool> predicate)
    {
        var time = Stopwatch.StartNew();
        while (time.ElapsedMilliseconds < 4000)
        {
            Application.DoEvents();
            if (predicate()) return true;
            Thread.Sleep(5);
        }
        return false;
    }
    private static IntPtr Locate()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid != Process.GetCurrentProcess().Id || !IsWindowVisible(h)) return true;
            var caption = new StringBuilder(160);
            GetWindowText(h, caption, caption.Capacity);
            if (caption.ToString() != title) return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }
    private static bool Capturable(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        int style = GetWindowLong(hwnd, -20);
        return GetWindow(hwnd, 4) == IntPtr.Zero && (style & 0x40000) != 0
            && (style & (0x80000 | 0x80 | 0x20)) == 0 && (GetWindowLong(hwnd, -16) & 0x40000000) == 0;
    }
    private static void CheckRendered(IntPtr hwnd)
    {
        using (var bmp = new Bitmap(300, 150))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr dc = g.GetHdc();
                try { Check(PrintWindow(hwnd, dc, 1), "client capture succeeds"); }
                finally { g.ReleaseHdc(dc); }
            }
            int background=0, text=0;
            for (int x=0; x<bmp.Width; ++x) for (int y=0; y<bmp.Height; ++y)
            {
                var pixel=bmp.GetPixel(x,y);
                if(pixel.ToArgb()==Color.Magenta.ToArgb()) ++background;
                else if(pixel.G>80) ++text;
            }
            Check(background > 1000 && text > 50, "client pixels contain opaque magenta and visible timer text");
        }
    }

    private static void Run(string executable)
    {
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var product = Assembly.LoadFrom(executable);
        var type = product.GetType("Pal98Timer.TimerOverlayHost", true);
        var frame = product.GetType("Pal98Timer.Dx9OverlaySnapshot", true);
        var settings = product.GetType("Pal98Timer.Dx9OverlayLayoutSettings", true);
        var layout = settings.GetMethod("CreateDefault").Invoke(null, null);
        string key = "saved-layout";
        var saves = new List<string>();
        var blocked = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var workerDone = new ManualResetEventSlim(false);
        uint sampleThread = 0;
        object data = null;
        bool hold = false;
        Func<object> read = () => {
            sampleThread = GetCurrentThreadId();
            if (hold) { blocked.Set(); if(!release.Wait(6000)) throw new TimeoutException(); }
            return data;
        };
        var ctor = frame.GetConstructors().Single(c=>c.GetParameters().Length==16);
        var parameters=ctor.GetParameters().Select(p=>p.ParameterType==typeof(string) ? (object)"test 123" :
            p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
        parameters[1]="SimSun";
        data=ctor.Invoke(parameters);
        var getter=Expression.Lambda(typeof(Func<>).MakeGenericType(frame),
            Expression.Convert(Expression.Invoke(Expression.Constant(read)),frame)).Compile();
        var value=Expression.Parameter(settings,"value");
        Action<object> write = _ => { lock(saves) saves.Add(key); };
        var setter=Expression.Lambda(typeof(Action<>).MakeGenericType(settings),
            Expression.Invoke(Expression.Constant(write),Expression.Convert(value,typeof(object))),value).Compile();
        Func<object> create=()=>Activator.CreateInstance(type, All, null,new object[]{title,getter,layout,setter},null);
        object host=create();
        try
        {
            Call(host,"Start");
            Check(Wait(()=>Capturable(Locate())), "ownerless nonlayered app window is discoverable even before first game snapshot");
            IntPtr initial=Locate(); uint ignored;
            uint owner=GetCurrentThreadId(), renderer=GetWindowThreadProcessId(initial,out ignored);
            Check(renderer!=owner,"window/render thread differs from timer UI owner");
            hold=true;
            var worker=new Thread(()=>{try {Call(host,"Sample");} finally {workerDone.Set();}}) {IsBackground=true};
            worker.Start();
            Check(Wait(()=>blocked.IsSet),"timing worker can produce a slow snapshot");
            Check(sampleThread!=owner && sampleThread!=renderer,"sampling, window and UI have distinct threads");
            bool ownerPosted=false; SynchronizationContext.Current.Post(_=>ownerPosted=true,null);
            Check(Wait(()=>ownerPosted),"owner message pump keeps dispatching while sampling is stalled");
            CheckRendered(initial);
            release.Set();
            Check(Wait(()=>workerDone.IsSet),"slow sample completes");
            // Allow subsequent WinForms paint messages to use the new frame.
            Check(Wait(()=>ReferenceEquals(type.GetField("latest", All).GetValue(host),data)),"published immutable frame reaches host");
            CheckRendered(initial);
            Call(host,"ToggleEditMode");
            Check(Wait(()=>Property(host,"IsInEditMode") && Locate()==initial),"editing preserves the capture HWND");
            Call(host,"ToggleEditMode");
            Check(Wait(()=>!Property(host,"IsInEditMode")),"editing can be closed");
            var next=settings.GetMethod("CreateDefault").Invoke(null,null);
            settings.GetField("HasWindowPosition").SetValue(next,true);
            settings.GetField("WindowLeft").SetValue(next,50);
            settings.GetField("WindowTop").SetValue(next,60);
            Call(host,"SwitchLayout",next,setter);
            Call(host,"ToggleCaptureWindow");
            Check(Wait(()=>!Property(host,"CaptureWindow") && Locate()!=IntPtr.Zero && (GetWindowLong(Locate(),-20)&0x80020)==0x80020),"transparent game overlay retains colour key and click-through");
            Call(host,"ToggleCaptureWindow");
            Check(Wait(()=>Property(host,"CaptureWindow") && Capturable(Locate())),"switching back restores discoverability");
            lock(saves) Check(saves.Count>=2,"capture mode changes are persisted through the supplied layout sink");
            Call(host,"ResetLayout");
            Check(Wait(()=>Capturable(Locate())),"reset defaults to capturable source");
        }
        finally {release.Set();((IDisposable)host).Dispose();}
        Check(Wait(()=>Locate()==IntPtr.Zero),"closed overlay removes capture HWND");
        for(int i=0;i<12;i++)
        {
            var rapid=create();
            Call(rapid,"Start");
            ((IDisposable)rapid).Dispose();
        }
        var timer=Stopwatch.StartNew();
        Check(Wait(()=>timer.ElapsedMilliseconds>=200 && Locate()==IntPtr.Zero),"rapid open/close leaves no discoverable window");
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try { Run(args[0]); results.Add("PASS checks="+results.Count); return 0; }
        catch (Exception error) { results.Add(error.ToString()); return 1; }
        finally { File.WriteAllLines(args[1],results,new UTF8Encoding(false)); }
    }
}
