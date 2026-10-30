param([string]$PalSharedSourceRoot = '', [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw 'Choose a fresh evidence directory.' }
New-Item -ItemType Directory -Path $out | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$csc) { throw 'VS 2026 Roslyn not found' }
$sources = @('RankingConfiguration.cs','GameplayIdentity.cs','CompetitionProtocol.cs','CompetitionStorage.cs','CompetitionTransport.cs','CompetitionAuth.cs','CompetitionBuildRegistration.cs','CompetitionClient.cs','CompetitionLive.cs','CompetitionGameSettings.cs','CompetitionPlayerIdentity.cs','LocalPlayerIdentity.cs','IdentityBehaviorSnapshot.cs','TournamentLockInfoReader.cs') | ForEach-Object { $component = Join-Path $repo ('PalTimerOnline\' + $_); if (Test-Path -LiteralPath $component) { $component } else { Join-Path $repo ('Pal98Timer\' + $_) } }
$sources += Join-Path $PSScriptRoot 'live_behavior.cs'
$sources += Join-Path $PSScriptRoot 'live_connectivity.cs'
$sources += @('TimingModeReader.cs', 'Dx9TimingCategory.cs', 'HObj.cs') | ForEach-Object { Join-Path $repo ('Pal98Timer\' + $_) }
if (!$PalSharedSourceRoot) { $PalSharedSourceRoot = Join-Path $repo '..\PALDLL_DX9\shared' }
$sources += Join-Path $PalSharedSourceRoot 'PalUserData.cs'
$sources += Join-Path $repo 'Pal98Timer\SdlpalBridge.cs'
$exe = Join-Path $out 'LiveBehavior.exe'
& $csc /nologo /noconfig /target:exe /platform:x64 "/out:$exe" /reference:System.dll /reference:System.Core.dll /reference:System.Runtime.Serialization.dll /reference:System.Xml.dll /reference:System.Data.dll /reference:System.Web.dll /reference:System.Security.dll /reference:System.Net.Http.dll "/reference:$repo\Pal98Timer\lib\System.Web.Script.Serialization.dll" $sources *> (Join-Path $out 'build.log')
if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $out 'build.log'); throw 'Live test build failed' }
Copy-Item -LiteralPath (Join-Path $repo 'Pal98Timer\lib\System.Web.Script.Serialization.dll') -Destination $out
Copy-Item -LiteralPath (Join-Path $repo 'Pal98Timer\App.config') -Destination ($exe + '.config')
& $exe (Join-Path $out 'data') 2>&1 | Tee-Object -FilePath (Join-Path $out 'result.txt')
$result = $LASTEXITCODE
Write-Output "Evidence: $out"
if ($result -ne 0) { throw 'Live behavior failed' }
