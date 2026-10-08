[CmdletBinding()]
param(
    [string]$CandidateName = '',
    [string]$AuthDirectory = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$timerPath = Join-Path $repoRoot 'Pal98Timer\bin\x64\Release\Pal98Timer.exe'
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($timerPath).FileVersion
$version = ([Version]$fileVersion).ToString(3)
if (!$CandidateName) { $CandidateName = 'paltimer-' + $version + '-gpl-candidate-' + (Get-Date -Format 'yyyyMMdd-HHmmss') }
if ([Version]$fileVersion -ge [Version]'3.37.7.13' -and !$AuthDirectory) { throw 'This release requires the matching private Auth build directory.' }
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$candidateRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot $CandidateName))

if (-not $candidateRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Candidate path escapes the repository artifacts directory: $candidateRoot"
}
if (Test-Path -LiteralPath $candidateRoot) {
    throw "Candidate already exists; refusing to overwrite it: $candidateRoot"
}

$runtimeFiles = [ordered]@{
    'Pal98Timer.exe' = 'Pal98Timer\bin\x64\Release\Pal98Timer.exe'
    'Pal98Timer.exe.config' = 'Pal98Timer\bin\x64\Release\Pal98Timer.exe.config'
    'PalTimerOnline.dll' = 'PalTimerOnline\bin\x64\Release\PalTimerOnline.dll'
    'PalCloudLib.dll' = 'Pal98Timer\bin\x64\Release\PalCloudLib.dll'
    'System.Web.Script.Serialization.dll' = 'Pal98Timer\bin\x64\Release\System.Web.Script.Serialization.dll'
    'TimerPluginBase.dll' = 'Pal98Timer\bin\x64\Release\TimerPluginBase.dll'
    'KeyChanger.exe' = 'KeyChanger\bin\Release\KeyChanger.exe'
    'KeyChanger.exe.config' = 'KeyChanger\bin\Release\KeyChanger.exe.config'
    'ModuleAddrX64Delegate.exe' = 'ModuleAddrX64Delegate\bin\x64\Release\ModuleAddrX64Delegate.exe'
    'ModuleAddrX64Delegate.exe.config' = 'ModuleAddrX64Delegate\bin\x64\Release\ModuleAddrX64Delegate.exe.config'
    'ModuleAddrX86Delegate.exe' = 'ModuleAddrX86Delegate\bin\x86\Release\ModuleAddrX86Delegate.exe'
    'ModuleAddrX86Delegate.exe.config' = 'ModuleAddrX86Delegate\bin\x86\Release\ModuleAddrX86Delegate.exe.config'
}

foreach ($relativeSource in $runtimeFiles.Values) {
    $source = Join-Path $repoRoot $relativeSource
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Release x64 build output is incomplete: $source"
    }
}
if ($AuthDirectory) {
    $registration = Get-Content -LiteralPath (Join-Path $AuthDirectory 'PalCompetitionRegistration.public.json') -Raw | ConvertFrom-Json
    foreach ($pair in @(@('exe_sha256',$timerPath),@('online_component_sha256',(Join-Path $repoRoot $runtimeFiles['PalTimerOnline.dll'])),@('component_sha256',(Join-Path $AuthDirectory 'PalCompetitionAuth.dll')))) {
        if ($registration.build.($pair[0]) -ne (Get-FileHash -LiteralPath $pair[1] -Algorithm SHA256).Hash.ToLowerInvariant()) { throw ('Companion registration mismatch: ' + $pair[0]) }
    }
}

New-Item -ItemType Directory -Path $candidateRoot | Out-Null
foreach ($entry in $runtimeFiles.GetEnumerator()) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $entry.Value) -Destination (Join-Path $candidateRoot $entry.Key)
}
if ($AuthDirectory) {
    foreach ($name in @('PalCompetitionAuth.dll','PalCompetitionRegistration.public.json')) {
        Copy-Item -LiteralPath (Join-Path $AuthDirectory $name) -Destination (Join-Path $candidateRoot $name)
    }
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $candidateRoot 'LICENSE')
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination (Join-Path $candidateRoot 'README.md')
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\PAL98DX9_PROFILE_CORES.md') -Destination (Join-Path $candidateRoot 'PAL98DX9_PROFILE_CORES.md')

$sourceStage = Join-Path $candidateRoot '_source-stage'
New-Item -ItemType Directory -Path $sourceStage | Out-Null

$sourceFiles = @(& git -C $repoRoot -c core.quotepath=false ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) {
    throw "git source inventory failed: $LASTEXITCODE"
}

$excludedPrefixes = @('.agents/', '.claude/', '.codegraph/', '.ai/', 'artifacts/')
$excludedFiles = @('AGENTS.md', 'CLAUDE.md', '.ai/resume.md')
foreach ($relative in $sourceFiles) {
    $normalized = $relative.Replace('\', '/')
    if ($excludedFiles -contains $normalized) { continue }
    if ($excludedPrefixes | Where-Object { $normalized.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }) { continue }
    if ($normalized -match '(^|/)(bin|obj)/' -or $normalized -match '\.(pfx|snk|suo|user)$') { continue }
    if ($normalized -eq 'Pal98Timer/TournamentIntegrityKey.txt') { continue }

    $source = Join-Path $repoRoot $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
    $destination = Join-Path $sourceStage $relative
    $destinationDirectory = Split-Path -Parent $destination
    if (-not (Test-Path -LiteralPath $destinationDirectory)) {
        New-Item -ItemType Directory -Path $destinationDirectory | Out-Null
    }
    Copy-Item -LiteralPath $source -Destination $destination
}

$head = (& git -C $repoRoot rev-parse HEAD).Trim()
$metadata = @(
    'PalTimer complete corresponding source snapshot'
    'SPDX-License-Identifier: GPL-2.0-only'
    "Repository: https://github.com/othercat/PalTimer"
    "Source revision: $head"
    'Snapshot policy: current tracked and untracked build sources, excluding generated outputs, local Goal overlays, private-agent notes and signing-key file types.'
    'Build: Visual Studio 2026 / MSBuild 18, Pal98Timer.sln, Release|x64'
) -join [Environment]::NewLine
[IO.File]::WriteAllText((Join-Path $sourceStage 'SOURCE_SNAPSHOT_METADATA.txt'), $metadata + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

$sourceZip = Join-Path $candidateRoot ('PalTimer-' + $version + '-source.zip')
Compress-Archive -Path (Join-Path $sourceStage '*') -DestinationPath $sourceZip -CompressionLevel Optimal

$resolvedStage = [IO.Path]::GetFullPath($sourceStage)
if (-not $resolvedStage.StartsWith($candidateRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Source staging path escaped candidate root: $resolvedStage"
}
Remove-Item -LiteralPath $resolvedStage -Recurse -Force

$payload = Get-ChildItem -LiteralPath $candidateRoot -File | Sort-Object Name | ForEach-Object {
    [ordered]@{
        path = $_.Name
        size = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
}
$manifest = [ordered]@{
    schema = 'pal98.local-public-tool-release.v1'
    product = 'PalTimer'
    version = $version
    file_version = $fileVersion
    license = 'GPL-2.0-only'
    repository_owner = 'othercat'
    repository = 'https://github.com/othercat/PalTimer'
    build_configuration = 'Release|x64'
    source_revision = $head
    source_snapshot_includes_uncommitted_changes = @(& git -C $repoRoot status --porcelain -- . ':!.agents' ':!.claude' ':!.codegraph' ':!artifacts' ':!AGENTS.md' ':!CLAUDE.md' ':!.ai/resume.md').Count -gt 0
    payload = $payload
}
[IO.File]::WriteAllText(
    (Join-Path $candidateRoot 'release-manifest.json'),
    ($manifest | ConvertTo-Json -Depth 5) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false)
)

$sumLines = Get-ChildItem -LiteralPath $candidateRoot -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, $_.Name
}
[IO.File]::WriteAllText((Join-Path $candidateRoot 'SHA256SUMS.txt'), ($sumLines -join [Environment]::NewLine) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

Write-Output "PalTimer GPL candidate: $candidateRoot"
Get-Content -LiteralPath (Join-Path $candidateRoot 'SHA256SUMS.txt')
