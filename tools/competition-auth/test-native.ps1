param([string]$TimerRoot = 'D:\Workspace\KnowledgeRoots\PAL\othercat\PalTimer', [string]$PalSharedSourceRoot = '', [Parameter(Mandatory=$true)][string]$TimerDirectory, [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$timer = (Resolve-Path -LiteralPath $TimerRoot).Path
if (!$PalSharedSourceRoot) { $PalSharedSourceRoot = Join-Path $timer '..\PALDLL_DX9\shared' }
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw 'Choose a fresh native-test evidence directory.' }
$binaries = (Resolve-Path -LiteralPath $TimerDirectory).Path
New-Item -ItemType Directory -Path $out | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
$sources = @('RankingConfiguration.cs','GameplayIdentity.cs','CompetitionProtocol.cs','CompetitionStorage.cs','CompetitionTransport.cs','CompetitionAuth.cs','CompetitionBuildRegistration.cs','CompetitionClient.cs','CompetitionGameSettings.cs','TournamentLockInfoReader.cs') | ForEach-Object { $component=Join-Path $timer ('PalTimerOnline\'+$_); if(Test-Path -LiteralPath $component){$component}else{Join-Path $timer ('Pal98Timer\'+$_)} }
$sources += Join-Path $PSScriptRoot 'OnlineProbe.cs'
$sources += @('CompetitionPlayerIdentity.cs','LocalPlayerIdentity.cs','IdentityBehaviorSnapshot.cs') | ForEach-Object { Join-Path $timer ('PalTimerOnline\'+$_) }
$onlineModels = Join-Path $timer 'PalTimerOnline\OnlineGameplayModels.cs'
if (Test-Path -LiteralPath $onlineModels) {
    $sources = @($sources | Where-Object { (Split-Path $_ -Leaf) -ne 'GameplayIdentity.cs' }) + $onlineModels
}
$sdlBridge = Join-Path $timer 'Pal98Timer\SdlpalBridge.cs'
if (Test-Path -LiteralPath $sdlBridge) { $sources += $sdlBridge }
$sources += Join-Path $timer 'PalTimerOnline\CompetitionLive.cs'
$sources += Join-Path $PalSharedSourceRoot 'PalUserData.cs'
$exe = Join-Path $out 'NativeProbe.exe'
& $csc /nologo /noconfig /target:exe /platform:x64 "/out:$exe" /reference:System.dll /reference:System.Core.dll /reference:System.Runtime.Serialization.dll /reference:System.Xml.dll /reference:System.Web.dll /reference:System.Security.dll /reference:System.Net.Http.dll "/reference:$timer\Pal98Timer\lib\System.Web.Script.Serialization.dll" $sources *> (Join-Path $out 'csc.log')
if ($LASTEXITCODE -ne 0) { throw ('Fixture compilation failed: ' + (Join-Path $out 'csc.log')) }
Copy-Item -LiteralPath (Join-Path $timer 'Pal98Timer\lib\System.Web.Script.Serialization.dll') -Destination $out
Copy-Item -LiteralPath (Join-Path $binaries 'Pal98Timer.exe.config') -Destination ($exe + '.config')
Copy-Item -LiteralPath (Join-Path $binaries 'PalTimerOnline.dll') -Destination $out
$online=Join-Path $out 'PalTimerOnline.dll'
& (Join-Path $PSScriptRoot 'build.ps1') -TimerExe $exe -OnlineDll $online -OutputPathFile (Join-Path $out 'component-path.txt') -TestPublisher
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$nativeOut = [IO.File]::ReadAllText((Join-Path $out 'component-path.txt'))
Copy-Item -LiteralPath (Join-Path $nativeOut 'PalCompetitionAuth.dll') -Destination $out
Copy-Item -LiteralPath (Join-Path $nativeOut 'registration.public.json') -Destination $out
Copy-Item -LiteralPath (Join-Path $nativeOut 'PalCompetitionRegistration.public.json') -Destination $out
Copy-Item -LiteralPath (Join-Path $nativeOut 'publisher.public.json') -Destination $out
& $exe (Join-Path $out 'local') *> (Join-Path $out 'local.log')
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $out 'local.log'); throw 'Native positive/negative checks failed' }
$modified = Join-Path $out 'ModifiedProbe.exe'
$data = [IO.File]::ReadAllBytes($exe)
[IO.File]::WriteAllBytes($modified,($data + [byte[]]@(0)))
Copy-Item -LiteralPath ($exe + '.config') -Destination ($modified + '.config')
& $modified (Join-Path $out 'modified') reject-host *> (Join-Path $out 'modified.log')
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $out 'modified.log'); throw 'Modified host not rejected' }
# Changed or missing online component must fail independently of host identity.
$onlineBytes=[IO.File]::ReadAllBytes($online)
[IO.File]::WriteAllBytes($online,($onlineBytes+[byte[]]@(0)))
& $exe (Join-Path $out 'changed-online') reject-host *> (Join-Path $out 'changed-online.log')
if ($LASTEXITCODE -ne 0) { throw 'Modified online DLL not rejected' }
[IO.File]::WriteAllBytes($online,$onlineBytes)
Move-Item -LiteralPath $online -Destination ($online+'.fixture-disabled')
& $exe (Join-Path $out 'missing-online') reject-host *> (Join-Path $out 'missing-online.log')
if ($LASTEXITCODE -ne 0) { throw 'Missing online DLL not rejected' }
Move-Item -LiteralPath ($online+'.fixture-disabled') -Destination $online
Get-Content -LiteralPath (Join-Path $out 'local.log') -Tail 3
Get-Content -LiteralPath (Join-Path $out 'modified.log')
Write-Output ('Evidence: ' + $out)
