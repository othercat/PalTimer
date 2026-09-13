$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$run = Join-Path $root ('artifacts\timing-mode-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $run | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
$exe = Join-Path $run 'TimingModeBehaviorTest.exe'
& $csc /nologo /target:exe "/out:$exe" /reference:System.dll /reference:System.Core.dll (Join-Path $root 'Pal98Timer\TimingModeReader.cs') (Join-Path $PSScriptRoot 'timing_mode_behavior_test.cs')
if ($LASTEXITCODE -ne 0) { throw 'Timing reader harness compilation failed' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Timing reader behavior failed' }
