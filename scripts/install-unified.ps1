# Install the same frozen payload in either supported game layout.
param(
    [Parameter(Mandatory)][string]$GameDirectory,
    [Parameter(Mandatory)][ValidateSet('Original','SDL')][string]$Edition,
    [string]$PayloadDirectory = (Join-Path $PSScriptRoot 'payload')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$game = (Resolve-Path -LiteralPath $GameDirectory).Path
if (!(Test-Path -LiteralPath (Join-Path $game 'Pal.exe') -PathType Leaf)) { throw 'Select the game root containing Pal.exe.' }
$payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
$manifest = Get-Content -LiteralPath (Join-Path $payload 'payload.json') -Raw | ConvertFrom-Json
$expected = @('Pal98Timer.exe','Pal98Timer.exe.config','PalTimerOnline.dll','PalCompetitionAuth.dll',
    'PalCompetitionRegistration.public.json','PalCloudLib.dll','System.Web.Script.Serialization.dll',
    'TimerPluginBase.dll','KeyChanger.exe','KeyChanger.exe.config')
if ($manifest.schema -ne 'PalTimer.UnifiedPayload.v1' -or @($manifest.files).Count -ne $expected.Count) { throw 'Invalid payload manifest.' }
$seen = @{}
foreach ($file in $manifest.files) {
    if ($file.name -cnotin $expected -or $seen.ContainsKey($file.name)) { throw 'Unexpected or duplicate payload file.' }
    $seen[$file.name] = $true
    $source = Join-Path $payload $file.name
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ine $file.sha256) { throw "Payload checksum mismatch: $($file.name)" }
}
$relative = if ($Edition -eq 'Original') { 'Tools\仙剑98自动计时器' } else { 'Tools\Timer' }
$target = Join-Path $game $relative
if ($target -ieq $payload) { throw 'Payload must be separate from the installation directory.' }
# Check running tools before changing any file. Process paths on other users may be unavailable.
foreach ($process in @(Get-Process Pal98Timer,KeyChanger -ErrorAction SilentlyContinue)) {
    if ($process.Path -and ([IO.Path]::GetDirectoryName($process.Path) -ieq $target)) { throw 'Close the timer and key changer before updating.' }
}
$backup = Join-Path $target ('Rollback\PalTimer-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
if (Test-Path -LiteralPath $backup) { throw 'Backup directory already exists; retry shortly.' }
[void][IO.Directory]::CreateDirectory($backup)
$existing = @()
$created = [Collections.Generic.List[string]]::new()
# Complete all backups before the first replacement. User settings/PBs/saves are never payload files.
foreach ($name in $expected) {
    $destination = Join-Path $target $name
    if (Test-Path -LiteralPath $destination) { Copy-Item -LiteralPath $destination -Destination (Join-Path $backup $name); $existing += $name }
    else { $created.Add($name) }
}
$record = [ordered]@{schema='PalTimer.UnifiedInstall.v1';version=$manifest.version;edition=$Edition;target=$target;replaced=$existing;added=$created.ToArray();created_settings=@()}
[IO.File]::WriteAllText((Join-Path $backup 'install.json'),($record | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
try {
    foreach ($name in $expected) { Copy-Item -LiteralPath (Join-Path $payload $name) -Destination (Join-Path $target $name) -Force }
    foreach ($file in $manifest.files) {
        if ((Get-FileHash -LiteralPath (Join-Path $target $file.name) -Algorithm SHA256).Hash -ine $file.sha256) { throw "Installed checksum mismatch: $($file.name)" }
    }
    $defaults = @{LastCore=$(if ($Edition -eq 'Original') {'Pal98Dx9Automatic'} else {'SdlpalGeshi'})}
    if ($Edition -eq 'SDL') { $defaults['skip_node'] = '1' }
    foreach ($name in $defaults.Keys) {
        $path = Join-Path $target $name
        if (!(Test-Path -LiteralPath $path)) {
            $created.Add($name)
            [IO.File]::WriteAllText($path,$defaults[$name],[Text.UTF8Encoding]::new($false))
            $record.created_settings += $name
        }
    }
    $record.status = 'installed'
    [IO.File]::WriteAllText((Join-Path $backup 'install.json'),($record | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
} catch {
    $failure = $_
    foreach ($name in $existing) { Copy-Item -LiteralPath (Join-Path $backup $name) -Destination (Join-Path $target $name) -Force }
    foreach ($name in $created) {
        $path = Join-Path $target $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
    throw $failure
}
Write-Output "Installed the shared timer $($manifest.version): $target"
Write-Output "Previous matched files: $backup"
