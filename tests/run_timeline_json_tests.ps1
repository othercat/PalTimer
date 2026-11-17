param([Parameter(Mandatory=$true)][string]$RuntimeDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$runtime=(Resolve-Path -LiteralPath $RuntimeDirectory).Path
$out=[IO.Path]::GetFullPath($OutputDirectory)
if($out -notmatch '[\\/]PALWork[\\/]artifacts[\\/]PalTimer-issue-68[\\/]'){throw 'Use an Issue 68 artifacts output directory.'}
if(Test-Path -LiteralPath $out){throw 'Choose a fresh output directory.'}
New-Item -ItemType Directory -Path $out | Out-Null
foreach($name in @('Pal98Timer.exe','Pal98Timer.exe.config','PalCloudLib.dll','System.Web.Script.Serialization.dll','TimerPluginBase.dll')){
    Copy-Item -LiteralPath (Join-Path $runtime $name) -Destination $out
}
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc=& $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe'|Select-Object -First 1
$framework=Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2'
$references=@('System.dll','System.Core.dll','System.Data.dll','System.Xml.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll')|ForEach-Object {'/reference:'+(Join-Path $framework $_)}
$exe=Join-Path $out 'TimelineJsonBehavior.exe'
& $csc /nologo /noconfig /target:exe /platform:x64 ('/out:'+$exe) ('/reference:'+(Join-Path $out 'Pal98Timer.exe')) $references (Join-Path $PSScriptRoot 'timeline_json_behavior.cs') *> (Join-Path $out 'build.log')
if($LASTEXITCODE-ne0){Get-Content -LiteralPath (Join-Path $out 'build.log');throw 'Timeline JSON host compilation failed.'}
Copy-Item -LiteralPath (Join-Path $out 'Pal98Timer.exe.config') -Destination ($exe+'.config')
& $exe (Join-Path $out 'output') *> (Join-Path $out 'test.log')
$code=$LASTEXITCODE
Get-Content -LiteralPath (Join-Path $out 'test.log') -Tail 12
if($code-ne0){throw 'Timeline JSON regression failed.'}
