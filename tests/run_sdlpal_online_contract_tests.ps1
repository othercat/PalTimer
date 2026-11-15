param([Parameter(Mandatory)][string]$RuntimeDirectory,
      [Parameter(Mandatory)][string]$OnlineDll,
      [Parameter(Mandatory)][string]$NativeInputs,
      [Parameter(Mandatory)][string]$OutputDirectory,
      [switch]$Legacy)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) {throw 'Choose a fresh Issue evidence directory.'}
if ($out.StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase)) {throw 'Do not generate output inside the source checkout.'}
New-Item -ItemType Directory -Path $out | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1
if (!$csc) {throw 'VS 2026 Roslyn required.'}
$probe = Join-Path $out 'SdlpalOnlineContractHost.exe'
& $csc /nologo /noconfig /target:exe /platform:x64 "/out:$probe" /r:System.dll /r:System.Core.dll "/r:$RuntimeDirectory/Pal98Timer.exe" "/r:$RuntimeDirectory/System.Web.Script.Serialization.dll" (Join-Path $PSScriptRoot 'sdlpal_online_contract_host.cs') *> (Join-Path $out 'build.log')
if ($LASTEXITCODE) {Get-Content -LiteralPath (Join-Path $out 'build.log'); throw 'Contract host build failed.'}
foreach ($name in 'Pal98Timer.exe','System.Web.Script.Serialization.dll','PalCloudLib.dll') {Copy-Item -LiteralPath (Join-Path $RuntimeDirectory $name) -Destination $out}
Copy-Item -LiteralPath (Join-Path $RuntimeDirectory 'Pal98Timer.exe.config') -Destination ($probe+'.config')
$mode = if ($Legacy) {'legacy'} else {'projected'}
& $probe $RuntimeDirectory $OnlineDll $NativeInputs $out $mode *> (Join-Path $out 'result.log')
Get-Content -LiteralPath (Join-Path $out 'result.log')
if ($LASTEXITCODE) {throw 'Frozen EXE / built Online contract tests failed.'}
