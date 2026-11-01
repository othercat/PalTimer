param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$PalSharedSourceRoot = ''
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = [IO.Path]::GetFullPath($OutputDirectory)
if ($out.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Build outside the source checkout, in the owning Issue artifacts directory.' }
if (Test-Path -LiteralPath $out) { throw 'Choose a fresh output directory; published binaries must remain immutable.' }
if (!$PalSharedSourceRoot) { $PalSharedSourceRoot = Join-Path $repo '..\PALDLL_DX9\shared' }
$shared = (Resolve-Path -LiteralPath $PalSharedSourceRoot).Path
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'Visual Studio 2026 MSBuild is required.' }
New-Item -ItemType Directory -Path (Join-Path $out 'bin'),(Join-Path $out 'logs') | Out-Null
foreach ($name in @('TimerPluginBase','Pal98Timer','PalTimerOnline','KeyChanger')) {
    $platform = if ($name -in @('Pal98Timer','PalTimerOnline')) { 'x64' } else { 'AnyCPU' }
    $log = Join-Path $out ('logs\'+$name+'.log')
    & $msbuild (Join-Path $repo ($name+'\'+$name+'.csproj')) /nologo /v:minimal /m /nodeReuse:false /p:Configuration=Release "/p:Platform=$platform" /p:BuildProjectReferences=false "/p:OutputPath=$out\bin\" "/p:BaseIntermediateOutputPath=$out\obj\$name\" "/p:IntermediateOutputPath=$out\obj\$name\" "/p:ReferencePath=$out\bin\" "/p:PalSharedSourceRoot=$shared" *> $log
    if ($LASTEXITCODE) { Get-Content -LiteralPath $log -Tail 30; throw "Build failed: $name" }
    Write-Output "Built $name ($platform)"
}
$products = @(Get-ChildItem -LiteralPath (Join-Path $out 'bin') -File | Where-Object { $_.Extension -in @('.exe','.dll','.config') } | Sort-Object Name | ForEach-Object {
    [ordered]@{name=$_.Name;size=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant();version=[Diagnostics.FileVersionInfo]::GetVersionInfo($_.FullName).FileVersion}
})
$dependencies = @('PalUserData.cs','ChallengeRelayContract.cs','ChallengeJsonSyntax.cs','OriginalChallengeRuntimeContract.cs','..\docs\PAL98_ORIGINAL_CHALLENGE_RUNTIME.v1.json') | ForEach-Object {
    $p=Join-Path $shared $_
    [ordered]@{name=$_;sha256=(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$record = [ordered]@{
    schema='PalTimer.UnifiedBuild.v1';commit=(& git -C $repo rev-parse HEAD);source_dirty=[bool](& git -C $repo status --porcelain);
    version=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $out 'bin\Pal98Timer.exe')).FileVersion;
    editions=@('PALDLL_DX9','sdlpal-lostedit');products=$products;shared_sources=$dependencies;
    authentication='Freeze EXE/Online first, then sign their one shared Auth composition. Production approval and legacy-cloud core registrations remain separate.'
}
[IO.File]::WriteAllText((Join-Path $out 'release.json'),($record | ConvertTo-Json -Depth 6)+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
Write-Output "Unified binaries: $out\bin"
