[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repo 'Pal98Timer'
$corePath = Join-Path $project 'Dream220Visible.cs'
$profilePath = Join-Path $project 'Dream220VisibleProfile.cs'
$routePath = Join-Path $project 'Dream220VisibleRoute.cs'
$hunqianCorePath = Join-Path $project 'Hunqian167.cs'
$hunqianProfilePath = Join-Path $project 'Hunqian167Profile.cs'
$hunqianRoutePath = Join-Path $project 'Hunqian167Route.cs'
$legacyPath = Join-Path $project '梦幻22.cs'
$basePath = Join-Path $project '仙剑98柔情DX9.cs'
$timerCorePath = Join-Path $project 'TimerCore.cs'
$gformPath = Join-Path $project 'GForm.cs'

$core = [IO.File]::ReadAllText($corePath)
$profile = [IO.File]::ReadAllText($profilePath)
$route = [IO.File]::ReadAllText($routePath)
$hunqianCore = [IO.File]::ReadAllText($hunqianCorePath)
$hunqianProfile = [IO.File]::ReadAllText($hunqianProfilePath)
$legacy = [IO.File]::ReadAllText($legacyPath)
$baseCore = [IO.File]::ReadAllText($basePath)
$timerCore = [IO.File]::ReadAllText($timerCorePath)
$gform = [IO.File]::ReadAllText($gformPath)

if ($core -notmatch 'sealed class Dream220Visible\s*:\s*仙剑98柔情DX9' -or
    $core -notmatch 'TimerCoreDisplayName\(Dream220VisibleProfile\.CoreDisplayName\)' -or
    $core -match 'ReadProcessMemory|WriteProcessMemory|GetProcessesByName|0x00[0-9A-Fa-f]{4,}') {
    throw 'New Dream220 visible core does not preserve the PAL98DX9 read-owner boundary.'
}
if ($legacy -notmatch 'GetProcessesByName\("sdlpal"\)' -or $legacy -notmatch 'CoreName\s*=\s*"DREAM22"') {
    throw 'Legacy SDLPal Dream22 core identity changed unexpectedly.'
}
if ($timerCore -notmatch 'sealed class TimerCoreDisplayNameAttribute' -or
    $timerCore -notmatch 'GetCoreDisplayName\(string name\)' -or
    $gform -notmatch 'ti\.Text\s*=\s*TimerCore\.GetCoreDisplayName\(cn\)' -or
    $gform -notmatch 'streamWriter\.Write\(core\.GetType\(\)\.Name\)') {
    throw 'Player-facing core display names are not separated from stable LastCore class identities.'
}
if ($baseCore -notmatch 'protected GameObject GameObj' -or
    $baseCore -notmatch 'protected virtual bool TryValidateAttachedGameProfile' -or
    $baseCore -notmatch 'LastRejectedProfileProcessId\s*==\s*res\[0\]\.Id' -or
    $baseCore -notmatch 'profile_rejected_cached' -or
    $baseCore -match 'WriteProcessMemory') {
    throw 'PAL98DX9 base seam is missing, retries invalid profile files every tick, or introduced a new write-memory path.'
}

$checkpointNames = @(
    '上船','出蛇洞','过智修','过鬼将军','过赤鬼王','进扬州','出扬州','过鬼母','过彩依','过剑老头',
    '过明王','拆塔','过凤凰','过木道人','过火麒麟','过十年前','过七毒','过血角青龙','过五神龙','过桥头拜月','通关'
)
foreach ($name in $checkpointNames) {
    if ([regex]::Matches($core, [regex]::Escape('"' + $name + '"')).Count -ne 1) {
        throw "Checkpoint '$name' is missing or duplicated."
    }
}
if ($core -match 'EXBattleResult|EXBattleExpGet|EXBattleGoldGet|NewBattleEnd' -or
    $core -notmatch '541, 542, 543, 544, 545' -or
    $core -notmatch 'CaptureRelaySaveBuffer' -or
    $profile -notmatch 'ExpectedSaveLength = 185872') {
    throw 'Route still depends on SDLPal-only battle fields or lacks the five-dragon identity gate.'
}
if ($profile -notmatch 'pal98\.dream220\.compat' -or
    $profile -notmatch 'pal98\.dream220\.compat\.drawcard\.16e143813df5' -or
    $profile -notmatch '1\.0\.18' -or
    $profile -notmatch '梦幻2\.2显血版' -or
    $profile -notmatch '主播粉丝，孙小柔，othercat' -or
    $route -notmatch 'PAL98 runtime route' -or
    $route -notmatch 'acceptance remains a separate manual gate') {
    throw 'Public profile or evidence boundary is incomplete.'
}
if ($core -notmatch 'selectedProfile = Dream220VisibleProfile\.Canonical' -or
    $core -notmatch 'out validatedProfile' -or
    $core -notmatch 'exdata\["ProfileId"\] = selectedProfile\.ProfileId' -or
    $profile -notmatch '8794d9f660ef3a1c5849c0b9dfb1a2c4d318d990b2e31f89e893f0e157e13e6a') {
    throw 'Dream DrawCard exact-package validation or timer metadata projection is incomplete.'
}
if ($profile -notmatch '仙剑98柔情DX9梦幻22显血' -or
    $hunqianProfile -notmatch '仙剑98柔情DX9魂牵' -or
    $hunqianProfile -notmatch 'pal98\.hunqian167\.easy' -or
    $hunqianProfile -notmatch 'pal98\.hunqian167\.hard' -or
    $hunqianProfile -notmatch 'pal98\.hunqian167\.nightmare' -or
    $hunqianCore -notmatch 'CoreName\s*=\s*"PAL98DX9HUNQIAN"' -or
    $hunqianCore -notmatch 'GetBest\(name, TimeSpan\.Zero\)' -or
    $hunqianCore -match 'ReadProcessMemory|WriteProcessMemory|GetProcessesByName|EXBattleResult|EXBattleExpGet|EXBattleGoldGet') {
    throw 'Independent Classic/Hunqian/Dream PAL98DX9 core boundary is incomplete.'
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw 'vswhere.exe is required for the focused regression harness.'
}
$install = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
$csc = Join-Path $install 'MSBuild\Current\Bin\Roslyn\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    throw "VS 2026 Roslyn compiler not found: $csc"
}

$tempRoot = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'paltimer-dream220-visible-' + [Guid]::NewGuid().ToString('N'))
$tempRoot = [IO.Path]::GetFullPath($tempRoot)
$expectedPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $tempRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not [IO.Path]::GetFileName($tempRoot).StartsWith('paltimer-dream220-visible-', [StringComparison]::Ordinal)) {
    throw "Unsafe temporary path: $tempRoot"
}

[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
try {
    $harnessExe = Join-Path $tempRoot 'Dream220VisibleRegressionHarness.exe'
    $harnessSource = Join-Path $repo '.ai\tests\Dream220VisibleRegressionHarness.cs'
    & $csc /nologo /target:exe "/out:$harnessExe" $profilePath $routePath $hunqianProfilePath $hunqianRoutePath $harnessSource
    if ($LASTEXITCODE -ne 0) {
        throw "Focused harness compilation failed with exit code $LASTEXITCODE."
    }
    & $harnessExe $repo (Join-Path $tempRoot 'game')
    if ($LASTEXITCODE -ne 0) {
        throw "Focused harness failed with exit code $LASTEXITCODE."
    }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        [IO.Directory]::Delete($tempRoot, $true)
    }
}

Write-Output 'PASS dream220-visible structural, profile, route, and no-new-memory-write checks'
