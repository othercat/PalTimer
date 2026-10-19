[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory,[switch]$Cloud)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$timer = Join-Path $package 'Tools\Timer'
$gameName = if (Test-Path -LiteralPath (Join-Path $package 'pal.exe')) { 'pal.exe' } else { 'sdlpal.exe' }
foreach ($file in @($gameName,'SDL2.dll','save\1.rpg','save\2.rpg','Tools\Timer\Pal98Timer.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $package $file) -PathType Leaf)) { throw "Missing package file: $file" }
}
$out = Join-Path $repo ('artifacts\native-relay-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $out,(Join-Path $out 'PAL') | Out-Null
Get-ChildItem -LiteralPath (Join-Path $package 'PAL') -File | Where-Object { $_.Extension -in '.mkf','.dat','.fon','.asc','.msg' } | Copy-Item -Destination (Join-Path $out 'PAL')
Get-ChildItem -LiteralPath $timer -File | Where-Object { $_.Extension -in '.exe','.dll','.config' } | Copy-Item -Destination $out
$originalHashes = @{}
foreach ($side in @('game-a','game-b')) {
    $directory = Join-Path $out $side
    New-Item -ItemType Directory -Path $directory,(Join-Path $directory 'save') | Out-Null
    Copy-Item -LiteralPath (Join-Path $package $gameName) -Destination (Join-Path $directory 'sdlpal.exe')
    Copy-Item -LiteralPath (Join-Path $package 'SDL2.dll') -Destination $directory
    foreach ($slot in 1..5) {
        $source = Join-Path $package "save\$slot.rpg"
        Copy-Item -LiteralPath $source -Destination (Join-Path $directory 'save')
        $originalHashes["$side/$slot"] = (Get-FileHash -LiteralPath $source).Hash
    }
    Set-Content -LiteralPath (Join-Path $directory 'sdlpal.cfg') -Encoding utf8NoBOM -Value "GamePath=../PAL/`nSavePath=./`nLaunchSetting=0`nFullScreen=0`nMusic=MP3`nCD=WAV`nEnableGLSL=0`nLogFileName=verification.log"
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (-not $csc) { throw 'VS 2026 Roslyn compiler not found' }
$exe = Join-Path $out 'NativeSdlpalRelayHost.exe'
$references = @('/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll')
foreach ($name in @('Pal98Timer.exe','TimerPluginBase.dll','System.Web.Script.Serialization.dll')) { $references += '/reference:' + (Join-Path $out $name) }
& $csc /nologo /noconfig /target:exe /platform:x64 "/out:$exe" $references (Join-Path $PSScriptRoot 'native_sdlpal_relay_host.cs') (Join-Path $PSScriptRoot 'native_sdlpal_cloud_host.cs') *> (Join-Path $out 'host-build.log')
if ($LASTEXITCODE -ne 0) { throw "Native relay host build failed: $out" }
Copy-Item -LiteralPath (Join-Path $out 'Pal98Timer.exe.config') -Destination ($exe + '.config')
@{
    Package=$package
    TimerSha256=(Get-FileHash -LiteralPath (Join-Path $out 'Pal98Timer.exe')).Hash
    EngineSha256=(Get-FileHash -LiteralPath (Join-Path $out 'game-a\sdlpal.exe')).Hash
    HostSourceSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'native_sdlpal_relay_host.cs')).Hash
    CloudHostSourceSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'native_sdlpal_cloud_host.cs')).Hash
    CloudTransport=$(if ($Cloud) { 'offline file store; no production requests' } else { 'none' })
    OriginalSaveHashes=$originalHashes
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $out 'identity.json') -Encoding utf8
Push-Location -LiteralPath $out
try {
    $hostArguments = @(); if ($Cloud) { $hostArguments += '--cloud' }
    & $exe @hostArguments 2>&1 | Tee-Object -FilePath (Join-Path $out 'result.txt'); $result = $LASTEXITCODE
}
finally { Pop-Location }
foreach ($side in @('game-a','game-b')) {
    foreach ($slot in 1..5) {
        if ((Get-FileHash -LiteralPath (Join-Path $out "$side\save\$slot.rpg")).Hash -ne $originalHashes["$side/$slot"] -or
            (Get-FileHash -LiteralPath (Join-Path $package "save\$slot.rpg")).Hash -ne $originalHashes["$side/$slot"]) {
            throw "An ordinary/source save changed: $side/$slot ($out)"
        }
    }
}
Write-Output 'PASS ordinary slots 1-5 and source package saves remain unchanged'
Write-Output "Evidence: $out"
if ($result -ne 0) { throw "Native SDL relay acceptance failed: $out" }
