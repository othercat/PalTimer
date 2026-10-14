param([Parameter(Mandatory=$true)][string]$TimerAssembly)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repo ('artifacts\cloud-save-menu-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
[void][IO.Directory]::CreateDirectory($out)
$TimerAssembly = (Resolve-Path -LiteralPath $TimerAssembly).Path
$bin = Split-Path -Parent $TimerAssembly
foreach ($name in @('Pal98Timer.exe','PalCloudLib.dll','System.Web.Script.Serialization.dll','TimerPluginBase.dll')) {
    Copy-Item -LiteralPath (Join-Path $bin $name) -Destination (Join-Path $out $name)
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$csc) { throw 'VS 2026 Roslyn not found' }
$exe = Join-Path $out 'CloudSaveMenu.exe'
Copy-Item -LiteralPath (Join-Path $bin 'Pal98Timer.exe.config') -Destination ($exe + '.config')
& $csc /nologo /target:exe /platform:x64 "/out:$exe" "/reference:$TimerAssembly" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'cloud_save_menu_host.cs') *> (Join-Path $out 'build.log')
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $out 'build.log'); throw "Menu host build failed: $out" }
& $exe $out *> (Join-Path $out 'result.log')
$result = $LASTEXITCODE
Get-Content -LiteralPath (Join-Path $out 'result.log')
[ordered]@{schema='PalTimer.CloudSaveMenuTest.v1'; assembly=$TimerAssembly; assemblySha256=(Get-FileHash -LiteralPath $TimerAssembly).Hash; realMenuWorker=$true; realCloudSession=$true; realGame=$false; network=$false; result= $(if ($result -eq 0) {'PASS'} else {'FAIL'})} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'receipt.json') -Encoding utf8
Write-Output "Evidence: $out"
if ($result -ne 0) { throw 'Offline cloud menu regression failed' }
