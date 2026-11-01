param([Parameter(Mandatory)][string]$PalSharedSourceRoot,
      [Parameter(Mandatory)][string]$Vectors,
      [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$out = [IO.Path]::GetFullPath($OutputDirectory)
if ($out -notmatch '[\\/]PALWork[\\/]artifacts[\\/](PalTimer|PALDLL_DX9)-issue-[0-9]+[\\/]' -or (Test-Path -LiteralPath $out)) {
    throw 'Choose a fresh directory in the actual Issue artifact tree.'
}
$repo = Split-Path -Parent $PSScriptRoot
$shared = (Resolve-Path -LiteralPath $PalSharedSourceRoot).Path
$vectorsPath = (Resolve-Path -LiteralPath $Vectors).Path
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$csc) { throw 'Visual Studio 18 compiler not found.' }
New-Item -ItemType Directory -Path $out | Out-Null
$sources = @('ChallengeRelayContract.cs', 'ChallengeJsonSyntax.cs', 'OriginalChallengeRuntimeContract.cs') | ForEach-Object { Join-Path $shared $_ }
$sources += Join-Path $PSScriptRoot 'original_challenge_display_behavior.cs'
$json = Join-Path $repo 'Pal98Timer\lib\System.Web.Script.Serialization.dll'
$runtime = Join-Path (Split-Path -Parent $shared) 'docs\PAL98_ORIGINAL_CHALLENGE_RUNTIME.v1.json'
$exe = Join-Path $out 'OriginalChallengeDisplayBehavior.exe'
& $csc /nologo /noconfig /target:exe /platform:x64 "/out:$exe" /r:System.dll /r:System.Core.dll "/r:$json" "/resource:$runtime,OriginalChallenges.Runtime.json" $sources *> (Join-Path $out 'build.log')
if ($LASTEXITCODE) { Get-Content -LiteralPath (Join-Path $out 'build.log'); throw 'Display contract test compilation failed.' }
Copy-Item -LiteralPath $json -Destination $out
Copy-Item -LiteralPath (Join-Path $repo 'Pal98Timer\App.config') -Destination ($exe + '.config')
& $exe $vectorsPath *> (Join-Path $out 'result.log')
Get-Content -LiteralPath (Join-Path $out 'result.log')
if ($LASTEXITCODE) { throw 'Display contract tests failed.' }
