param([string]$Server = '', [string]$PalSharedSourceRoot = '', [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$RuntimeDirectory = '', [switch]$SnapshotBridgeOnly)
$ErrorActionPreference = 'Stop'
if ($SnapshotBridgeOnly -and (!$RuntimeDirectory -or $Server)) { throw '-SnapshotBridgeOnly requires -RuntimeDirectory and no live server.' }
$repo = Split-Path -Parent $PSScriptRoot
# Keep nested credential/temp fixtures below legacy framework path limits even in worktrees.
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw 'Choose a fresh evidence directory.' }
New-Item -ItemType Directory -Path $out | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$csc) { throw 'VS 2026 Roslyn not found' }
$sources = @('RankingConfiguration.cs','GameplayIdentity.cs','CompetitionProtocol.cs','CompetitionStorage.cs','CompetitionTransport.cs','CompetitionAuth.cs','CompetitionBuildRegistration.cs','CompetitionClient.cs','CompetitionGameSettings.cs','CompetitionPlayerIdentity.cs','LocalPlayerIdentity.cs','IdentityBehaviorSnapshot.cs','CompetitionWindows.cs') | ForEach-Object { $component = Join-Path $repo ('PalTimerOnline\' + $_); if (Test-Path -LiteralPath $component) { $component } else { Join-Path $repo ('Pal98Timer\' + $_) } }
$sources += Join-Path $PSScriptRoot 'competition_behavior.cs'
$sources += Join-Path $PSScriptRoot 'original_challenge_online_behavior.cs'
$sources += Join-Path $PSScriptRoot 'identity_behavior.cs'
$sources += Join-Path $PSScriptRoot 'maintenance_initialization.cs'
$sources += Join-Path $PSScriptRoot 'online_snapshot_speed_behavior.cs'
$sources += Join-Path $repo 'PalTimerOnline\GeshiConfigurationInitialization.cs'
$sources += @('TimingModeReader.cs', 'Dx9TimingCategory.cs', 'HObj.cs') | ForEach-Object { Join-Path $repo ('Pal98Timer\' + $_) }
if (!$PalSharedSourceRoot) { $PalSharedSourceRoot = Join-Path $repo '..\PALDLL_DX9\shared' }
$sources += Join-Path $PalSharedSourceRoot 'PalUserData.cs'
$sources += Join-Path $repo 'Pal98Timer\OriginalChallengeIdentity.cs'
$sources += @('ChallengeRelayContract.cs','ChallengeJsonSyntax.cs','OriginalChallengeRuntimeContract.cs') | ForEach-Object { Join-Path $PalSharedSourceRoot $_ }
$sources += Join-Path $repo 'Pal98Timer\SdlpalBridge.cs'
$sources += Join-Path $repo 'PalTimerOnline\CompetitionLive.cs'
$exe = Join-Path $out 'CompetitionBehavior.exe'
& $csc /nologo /noconfig /target:exe /platform:x64 "/out:$exe" /reference:System.dll /reference:System.Core.dll /reference:System.Runtime.Serialization.dll /reference:System.Xml.dll /reference:System.Data.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.dll /reference:System.Security.dll /reference:System.Net.Http.dll "/reference:$repo\Pal98Timer\lib\System.Web.Script.Serialization.dll" $sources *> (Join-Path $out 'build.log')
if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $out 'build.log'); throw 'Competition test build failed' }
Copy-Item -LiteralPath (Join-Path $repo 'Pal98Timer\lib\System.Web.Script.Serialization.dll') -Destination $out
Copy-Item -LiteralPath (Join-Path $repo 'Pal98Timer\App.config') -Destination ($exe + '.config')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "ranking-configuration-vectors.json") -Destination $out
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "fixtures\identity-registration.json") -Destination $out
$arguments = @(Join-Path $out 'data')
if ($Server) { $arguments += $Server }
if ($SnapshotBridgeOnly) { $arguments += '--snapshot-bridge-only' }
$priorRuntime = $env:PALTIMER_TEST_RUNTIME
try {
    $env:PALTIMER_TEST_RUNTIME = if ($RuntimeDirectory) { (Resolve-Path -LiteralPath $RuntimeDirectory).Path } else { $null }
    & $exe @arguments 2>&1 | Tee-Object -FilePath (Join-Path $out 'result.txt')
    $result = $LASTEXITCODE
} finally { $env:PALTIMER_TEST_RUNTIME = $priorRuntime }
Write-Output "Evidence: $out"
if ($result -ne 0) { throw 'Competition behavior failed' }
