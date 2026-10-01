param(
    [Parameter(Mandatory=$true)][string]$PluginPath,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$runtime=Join-Path $repo 'Pal98Timer/bin/x64/Release'
$out=[IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($out)
Get-ChildItem -LiteralPath $runtime -File | Where-Object { $_.Extension -eq '.dll' -or $_.Name -eq 'Pal98Timer.exe' } | Copy-Item -Destination $out
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$csc=& $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1
if (!$csc) { throw 'VS 2026 Roslyn not found' }
$exe=Join-Path $out 'PluginDisplayBehavior.exe'
& $csc /nologo /target:exe /platform:x64 "/out:$exe" /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll "/reference:$runtime/Pal98Timer.exe" "/reference:$runtime/TimerPluginBase.dll" (Join-Path $PSScriptRoot 'plugin_display_behavior.cs') *> (Join-Path $out 'build.log')
if ($LASTEXITCODE) { Get-Content -LiteralPath (Join-Path $out 'build.log'); throw 'Plugin display test build failed' }
Copy-Item -LiteralPath (Join-Path $runtime 'Pal98Timer.exe.config') -Destination ($exe+'.config')
& $exe ([IO.Path]::GetFullPath($PluginPath)) 2>&1 | Tee-Object -FilePath (Join-Path $out 'result.txt')
if ($LASTEXITCODE) { throw 'Plugin display behavior failed' }
Write-Output "Evidence: $out"
