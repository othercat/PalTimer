$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$run = Join-Path $root ('artifacts\dx9-timing-category-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$runtime = Join-Path $root 'Pal98Timer\bin\x64\Release'
New-Item -ItemType Directory -Path $run | Out-Null
Get-ChildItem -LiteralPath $runtime -File | Where-Object { $_.Extension -eq '.dll' -or $_.Name -eq 'Pal98Timer.exe' } | Copy-Item -Destination $run
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
$exe = Join-Path $run 'Dx9TimingCategoryBehaviorTest.exe'
Copy-Item -LiteralPath (Join-Path $runtime 'Pal98Timer.exe.config') -Destination ($exe + '.config')
& $csc /nologo /target:exe /platform:x64 "/out:$exe" "/reference:$runtime\Pal98Timer.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'dx9_timing_category_behavior_test.cs')
if ($LASTEXITCODE -ne 0) { throw 'Timing category harness compilation failed' }
& $exe $run
if ($LASTEXITCODE -ne 0) { throw 'Timing category behavior failed' }
Write-Output "Evidence: $run"
