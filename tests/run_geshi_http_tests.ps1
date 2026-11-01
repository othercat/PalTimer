param(
    [Parameter(Mandatory)][string]$PalServerRoot,
    [Parameter(Mandatory)][string]$AuthSourceRoot,
    [Parameter(Mandatory)][string]$PalSharedSourceRoot
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$out = Join-Path $repo ('artifacts\http-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$server = (Resolve-Path -LiteralPath $PalServerRoot).Path
$auth = (Resolve-Path -LiteralPath $AuthSourceRoot).Path
$shared = (Resolve-Path -LiteralPath $PalSharedSourceRoot).Path
$python = Join-Path $env:USERPROFILE 'CodexTools\python\Scripts\python.exe'
if (!(Test-Path -LiteralPath $python)) { throw 'The fixed Codex CPython 3.11 environment is required.' }
& $python -c 'import sys; assert sys.version_info[:2] == (3, 11); assert sys.prefix != sys.base_prefix; print("CPython 3.11 venv ready")'
if ($LASTEXITCODE) { throw 'Unexpected Python environment' }
New-Item -ItemType Directory -Path $out | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$csc) { throw 'VS 2026 Roslyn required' }
$names = @('RankingConfiguration.cs','OnlineGameplayModels.cs','CompetitionProtocol.cs','CompetitionStorage.cs','CompetitionTransport.cs','CompetitionAuth.cs','CompetitionBuildRegistration.cs','CompetitionClient.cs','CompetitionGameSettings.cs','CompetitionPlayerIdentity.cs','LocalPlayerIdentity.cs','IdentityBehaviorSnapshot.cs','CompetitionLive.cs')
$sources = @($names | ForEach-Object {
    $path = Join-Path $repo ('PalTimerOnline\' + $_)
    if (Test-Path -LiteralPath $path) { $path } else { Join-Path $repo ('Pal98Timer\' + $_) }
})
$sources += Join-Path $repo 'Pal98Timer\SdlpalBridge.cs'
$sources += Join-Path $repo 'PalTimerOnline\GeshiConfigurationInitialization.cs'
$sources += Join-Path $shared 'PalUserData.cs'
$sources += Join-Path $PSScriptRoot 'geshi_http_host.cs'
$exe = Join-Path $out 'GeshiHttpProbe.exe'
& $csc /nologo /noconfig /target:exe /platform:x64 "/out:$exe" /reference:System.dll /reference:System.Core.dll /reference:System.Runtime.Serialization.dll /reference:System.Xml.dll /reference:System.Web.dll /reference:System.Security.dll /reference:System.Net.Http.dll "/reference:$repo\Pal98Timer\lib\System.Web.Script.Serialization.dll" $sources *> (Join-Path $out 'build.log')
if ($LASTEXITCODE) { throw "Host build failed: $out/build.log" }
Copy-Item -LiteralPath (Join-Path $repo 'Pal98Timer\lib\System.Web.Script.Serialization.dll') -Destination $out
Copy-Item -LiteralPath (Join-Path $repo 'Pal98Timer\bin\x64\Release\Pal98Timer.exe.config') -Destination ($exe + '.config')
$online = Join-Path $repo 'PalTimerOnline\bin\x64\Release\PalTimerOnline.dll'
Copy-Item -LiteralPath $online -Destination $out
# Private signing inputs remain solely inside the explicitly supplied Auth
# workspace. Only test DLL and public registration files enter ignored output.
& (Join-Path $auth 'build.ps1') -TimerExe $exe -OnlineDll (Join-Path $out 'PalTimerOnline.dll') -OutputPathFile (Join-Path $out 'auth-output-path.txt') -TestPublisher *> (Join-Path $out 'auth-build.log')
if (!$?) { throw "Test Auth build failed: $out/auth-build.log" }
$native = [IO.File]::ReadAllText((Join-Path $out 'auth-output-path.txt'))
foreach ($name in 'PalCompetitionAuth.dll','registration.public.json','PalCompetitionRegistration.public.json','publisher.public.json') {
    Copy-Item -LiteralPath (Join-Path $native $name) -Destination $out
}
[ordered]@{
    fixtureOnly=$true;timerRevision=(& git -C $repo rev-parse HEAD);serverRevision=(& git -C $server rev-parse HEAD)
    sources=@($sources | ForEach-Object { [ordered]@{name=(Split-Path $_ -Leaf);sha256=(Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant()} })
    onlineSha256=(Get-FileHash -LiteralPath $online).Hash.ToLowerInvariant()
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $out 'inputs.json') -Encoding utf8NoBOM
& $python (Join-Path $PSScriptRoot 'geshi_http_server.py') --server-root $server --output $out 2>&1 | Tee-Object -FilePath (Join-Path $out 'server-result.txt')
$result = $LASTEXITCODE
Write-Output "Evidence: $out"
if ($result) { throw 'Local native HTTP acceptance failed; preserve evidence for diagnosis.' }
