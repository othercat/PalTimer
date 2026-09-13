$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $repoRoot 'artifacts\palette-fade-mode-tests'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
$exe = Join-Path $testRoot 'PaletteFadeModeBehaviorTest.exe'
& $csc /nologo /target:exe "/out:$exe" /reference:System.dll /reference:System.Core.dll (Join-Path $repoRoot 'Pal98Timer\PaletteFadeModeReader.cs') (Join-Path $PSScriptRoot 'palette_fade_mode_behavior_test.cs')
if ($LASTEXITCODE -ne 0) { throw 'Palette mode test compilation failed' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Palette mode behavior failed' }
foreach ($core in @('仙剑98柔情DX9.cs','仙剑98柔情不欢乐模式.cs')) {
    $source = Get-Content -LiteralPath (Join-Path $repoRoot ('Pal98Timer\' + $core)) -Raw
    if (-not $source.Contains('FormatPaletteFadeVersion(TournamentDisplayName)') -or
        -not $source.Contains('exdata["PaletteFadeModeMs"]')) { throw "Missing display/recording integration: $core" }
}
