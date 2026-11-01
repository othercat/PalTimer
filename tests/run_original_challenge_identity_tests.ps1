param([Parameter(Mandatory)][string]$RuntimeDirectory,[Parameter(Mandatory)][string]$NativeSnapshot,
      [Parameter(Mandatory)][string]$InstalledFixture,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a fresh evidence directory'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$game=Join-Path $OutputDirectory 'game';New-Item -ItemType Directory -Path (Join-Path $game 'palmod') | Out-Null
foreach($relative in @('config.ini','challenge-rules.ini','palmod/challenge-selection.v1.json')) {
    Copy-Item -LiteralPath (Join-Path $InstalledFixture $relative) -Destination (Join-Path $game $relative)
}
$csc='C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$repo=Split-Path -Parent $PSScriptRoot
$exe=Join-Path $OutputDirectory 'identity.exe'
& $csc /nologo /noconfig /target:exe /platform:x64 "/out:$exe" /r:System.dll /r:System.Core.dll "/r:$repo\Pal98Timer\lib\System.Web.Script.Serialization.dll" (Join-Path $PSScriptRoot 'original_challenge_identity_host.cs') *> (Join-Path $OutputDirectory 'build.log')
if($LASTEXITCODE){Get-Content (Join-Path $OutputDirectory 'build.log');throw 'Identity test build failed'}
Copy-Item -LiteralPath (Join-Path $repo 'Pal98Timer/lib/System.Web.Script.Serialization.dll') -Destination $OutputDirectory
& $exe (Join-Path $RuntimeDirectory 'Pal98Timer.exe') $game $NativeSnapshot *> (Join-Path $OutputDirectory 'result.log')
Get-Content (Join-Path $OutputDirectory 'result.log') -Tail 20
if($LASTEXITCODE){throw 'Identity checks failed'}
