param(
    [Parameter(Mandatory=$true)][string]$RuntimeDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$runtime = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw 'Choose a new evidence directory.' }
New-Item -ItemType Directory -Path $out | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$csc) { throw 'VS 2026 Roslyn was not found.' }
$exe = Join-Path $out 'TimerOverlayWindowBehavior.exe'
& $csc /nologo /target:exe /platform:x64 "/out:$exe" /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'timer_overlay_window_behavior.cs') *> (Join-Path $out 'build.log')
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $out 'build.log'); throw 'Test build failed.' }

# No windows are shown on the interactive desktop, and no input is sent.
# The child starts on the test desktop, so both its main STA and its product
# overlay STA inherit the same desktop. Never switch the user's active desktop.
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class TimerFixtureDesktop
{
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct Startup {
        public int cb; public string reserved, desktop, title;
        public int x,y,cx,cy,xchars,ychars,attr,flags;
        public short show,reserved2; public IntPtr pdata,input,output,error;
    }
    [StructLayout(LayoutKind.Sequential)] struct Child { public IntPtr process,thread; public int pid,tid; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern IntPtr CreateDesktop(string name,IntPtr device,IntPtr mode,uint flags,uint access,IntPtr attributes);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern bool CreateProcess(string application,StringBuilder command,IntPtr processAttrs,IntPtr threadAttrs,bool inherit,
        int flags,IntPtr environment,string working,ref Startup startup,out Child child);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr handle,out int code);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process,uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    public static int Run(string exe,string product,string report,string working) {
        string name="PalTimerFixture"+Guid.NewGuid().ToString("N");
        var desktop=CreateDesktop(name,IntPtr.Zero,IntPtr.Zero,0,0x1ff,IntPtr.Zero);
        if(desktop==IntPtr.Zero)throw new Win32Exception();
        try {
            var setup=new Startup{cb=Marshal.SizeOf(typeof(Startup)),desktop=name};
            var cmd=new StringBuilder("\""+exe+"\" \""+product+"\" \""+report+"\"");
            Child child;
            if(!CreateProcess(exe,cmd,IntPtr.Zero,IntPtr.Zero,false,0x08000000,IntPtr.Zero,working,ref setup,out child))throw new Win32Exception();
            try {
                if(WaitForSingleObject(child.process,30000)!=0){TerminateProcess(child.process,1);throw new TimeoutException("Test desktop timed out.");}
                int code;if(!GetExitCodeProcess(child.process,out code))throw new Win32Exception();
                return code;
            } finally {CloseHandle(child.thread);CloseHandle(child.process);}
        } finally {CloseDesktop(desktop);}
    }
}
'@
$log = Join-Path $out 'result.txt'
$code = [TimerFixtureDesktop]::Run($exe,(Join-Path $runtime 'Pal98Timer.exe'),$log,$out)
if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 25 }
if ($code -ne 0) { throw "Window fixture failed ($code). Evidence is in $out." }
