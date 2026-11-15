param([Parameter(Mandatory)][string]$RuntimeDirectory,
 [Parameter(Mandatory)][string]$NativeInputs,
 [Parameter(Mandatory)][string]$BrowserExe,
 [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Use a fresh Issue evidence directory.'}
New-Item -ItemType Directory -Path $out | Out-Null
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc=& $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
$exe=Join-Path $out 'R116ModContract.exe'
& $csc /nologo /noconfig /target:exe /platform:x64 /debug:full "/out:$exe" /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll "/r:$RuntimeDirectory\Pal98Timer.exe" (Join-Path $PSScriptRoot 'r116_mod_contract.cs') *> (Join-Path $out 'build.log')
if($LASTEXITCODE){Get-Content -LiteralPath (Join-Path $out 'build.log');throw 'Host build failed.'}
foreach($name in @('Pal98Timer.exe','PalCloudLib.dll','TimerPluginBase.dll','System.Web.Script.Serialization.dll')) {Copy-Item -LiteralPath (Join-Path $RuntimeDirectory $name) -Destination $out}
Copy-Item -LiteralPath (Join-Path $RuntimeDirectory 'Pal98Timer.exe.config') -Destination ($exe+'.config')
& $exe $NativeInputs (Join-Path $RuntimeDirectory 'PalTimerOnline.dll') $out $BrowserExe *> (Join-Path $out 'result.log')
Get-Content -LiteralPath (Join-Path $out 'result.log') -Tail 24
if($LASTEXITCODE){throw 'Mod contract failed.'}
