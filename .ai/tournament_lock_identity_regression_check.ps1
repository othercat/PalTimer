$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = Split-Path -Parent $PSScriptRoot
$assemblyPath = Join-Path $repoRoot 'Pal98Timer\bin\x64\Release\Pal98Timer.exe'
$privateKeyPath = Join-Path $repoRoot 'Pal98Timer\TournamentIntegrityKey.txt'
if (-not [IO.File]::Exists($assemblyPath)) {
    throw 'Build Pal98Timer Release|x64 before running this check.'
}
if (-not [IO.File]::Exists($privateKeyPath)) {
    throw 'Trusted Release tournament integrity key is unavailable.'
}
$tempRoot = Join-Path $env:TEMP ('PalTimer-tournament-lock-' + [Guid]::NewGuid().ToString('N'))

function Write-SignedManifest([string]$root, [bool]$legacy = $false) {
    $competition = -join @(
        [char]0x79CB, [char]0x5B63, [char]0x676F, [char]0x6BD4,
        [char]0x8D5B, [char]0x4E13, [char]0x7528)
    $active = Join-Path $root 'palmod\TournamentLock\v1'
    $snapshots = Join-Path $active 'snapshots'
    [IO.Directory]::CreateDirectory($snapshots) | Out-Null
    $files = @()
    foreach ($name in @('config.ini', 'mod.ini', 'dxwrapper.ini')) {
        $snapshot = Join-Path $snapshots $name
        [IO.File]::WriteAllText($snapshot, "[$name]`nlocked=1`n", [Text.UTF8Encoding]::new($false))
        $snapshotBytes = [IO.File]::ReadAllBytes($snapshot)
        $snapshotHash = ([BitConverter]::ToString(
            [Security.Cryptography.SHA256]::Create().ComputeHash($snapshotBytes))).Replace('-', '').ToLowerInvariant()
        $files += [ordered]@{
            name = $name
            snapshot = 'snapshots/' + $name
            size = $snapshotBytes.Length
            sha256 = $snapshotHash
        }
    }
    $manifest = [ordered]@{
        schema = 'PAL98.TournamentLock.v1'
        version = 1
        locked = $true
        locker_name = 'AB12'
        competition_name = (-join @(
            [char]0x79CB, [char]0x5B63, [char]0x676F))
        competition_display_name = $competition
        display_lines = @('line1', 'line2', 'line3', 'line4')
    }
    if ($legacy) {
        $manifest.locked_footer_line = (-join @(
            [char]0x672C, [char]0x6B21, [char]0x6E38, [char]0x620F,
            [char]0x5185, [char]0x5BB9, [char]0x4E0D, [char]0x53EF,
            [char]0x66F4, [char]0x6539, [char]0x20, [char]0x9501,
            [char]0x5B9A, [char]0x8005, [char]0x20)) + 'AB12'
    }
    else {
        $manifest.display_line_overrides = @($true, $false, $true, $false)
        $manifest.configuration_code_marker = 'Ab12_-'
        $manifest.locked_footer_line = (-join @(
            [char]0x9501, [char]0x5B9A, [char]0x8005)) + 'AB12 : Ab12_-'
    }
    $manifest.files = $files
    $json = ConvertTo-Json $manifest -Compress -Depth 6
    $manifestPath = Join-Path $active 'manifest.json'
    [IO.File]::WriteAllText($manifestPath, $json, [Text.UTF8Encoding]::new($false))
    $key = [Text.Encoding]::ASCII.GetBytes(
        [IO.File]::ReadAllText($privateKeyPath, [Text.Encoding]::ASCII).Trim())
    $hmac = [Security.Cryptography.HMACSHA256]::new($key)
    try {
        $signature = ([BitConverter]::ToString($hmac.ComputeHash(
            [IO.File]::ReadAllBytes($manifestPath)))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $hmac.Dispose()
    }
    [IO.File]::WriteAllText((Join-Path $active 'manifest.sig'), $signature + "`n", [Text.Encoding]::ASCII)
}

function Read-Lock([Reflection.Assembly]$assembly, [string]$root) {
    $type = $assembly.GetType('Pal98Timer.TournamentLockInfoReader', $true)
    $method = $type.GetMethod('Load', [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::Public)
    return $method.Invoke($null, @($root))
}

try {
    [IO.Directory]::CreateDirectory($tempRoot) | Out-Null
    Write-SignedManifest $tempRoot
    $assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
    $locked = Read-Lock $assembly $tempRoot
    $expectedCompetition = -join @(
        [char]0x79CB, [char]0x5B63, [char]0x676F, [char]0x6BD4,
        [char]0x8D5B, [char]0x4E13, [char]0x7528)
    if ([string]$locked.State -ne 'Locked' -or $locked.CompetitionDisplayName -ne $expectedCompetition) {
        throw 'Valid signed tournament identity was not accepted.'
    }

    Write-SignedManifest $tempRoot $true
    $legacyLocked = Read-Lock $assembly $tempRoot
    if ([string]$legacyLocked.State -ne 'Locked' -or
        $legacyLocked.CompetitionDisplayName -ne $expectedCompetition) {
        throw 'Legacy signed tournament identity was not accepted.'
    }
    Write-SignedManifest $tempRoot

    $snapshotPath = Join-Path $tempRoot 'palmod\TournamentLock\v1\snapshots\config.ini'
    [IO.File]::AppendAllText($snapshotPath, 'tampered')
    $invalidSnapshot = Read-Lock $assembly $tempRoot
    if ([string]$invalidSnapshot.State -ne 'Invalid' -or $invalidSnapshot.CompetitionDisplayName -ne '') {
        throw 'Tampered tournament snapshot must invalidate timer identity.'
    }
    Write-SignedManifest $tempRoot

    $manifestPath = Join-Path $tempRoot 'palmod\TournamentLock\v1\manifest.json'
    [IO.File]::AppendAllText($manifestPath, ' ')
    $invalid = Read-Lock $assembly $tempRoot
    if ([string]$invalid.State -ne 'Invalid' -or $invalid.CompetitionDisplayName -ne '') {
        throw 'Tampered tournament identity must be ignored.'
    }

    $emptyRoot = Join-Path $tempRoot 'unlocked'
    [IO.Directory]::CreateDirectory($emptyRoot) | Out-Null
    $unlocked = Read-Lock $assembly $emptyRoot
    if ([string]$unlocked.State -ne 'Unlocked') {
        throw 'Absent tournament lock must preserve the normal timer identity.'
    }

    $readerSource = Get-Content -LiteralPath (Join-Path $repoRoot 'Pal98Timer\TournamentLockInfoReader.cs') -Raw
    if ($readerSource.IndexOf('ReadProcessMemory', [StringComparison]::Ordinal) -ge 0 -or
        $readerSource.IndexOf('WriteProcessMemory', [StringComparison]::Ordinal) -ge 0) {
        throw 'Tournament identity reader must not add process-memory access.'
    }
    foreach ($file in @('Dream220Visible.cs', 'Hunqian167.cs')) {
        $source = Get-Content -LiteralPath (Join-Path $repoRoot ('Pal98Timer\' + $file)) -Raw
        if ($source.IndexOf('TournamentDisplayName', [StringComparison]::Ordinal) -lt 0) {
            throw "$file does not preserve tournament display identity."
        }
    }
    $allCoreSources = (Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Pal98Timer') -Filter '*.cs' |
        ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
    if ([regex]::Matches($allCoreSources, 'LoadForProcessExecutable\(PalProcess\.MainModule\.FileName\)').Count -ne 2) {
        throw 'PAL98DX9 and PAL98UNHAPPY must both load the signed tournament identity once per attach.'
    }

    Write-Output 'PASS: signed tournament name is displayed by all PAL98DX9-family timer cores; tamper is ignored; no memory contract changed'
}
finally {
    if ([IO.Directory]::Exists($tempRoot)) {
        [IO.Directory]::Delete($tempRoot, $true)
    }
}
