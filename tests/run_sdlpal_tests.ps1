param([string]$PalSharedSourceRoot = '', [string[]]$NativeSnapshotFile, [switch]$NoGui,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$RuntimeDirectory = '', [string]$PluginResource = '', [switch]$SkipBuild, [switch]$SplitDisplayOnly, [switch]$BestConfigurationOnly)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (!$PalSharedSourceRoot) { $PalSharedSourceRoot = Join-Path $repo '..\PALDLL_DX9\shared' }
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw 'Choose a fresh output directory for the timer tests.' }
if ($SkipBuild -and (!$RuntimeDirectory -or !$PluginResource)) { throw '-SkipBuild requires -RuntimeDirectory and -PluginResource.' }
New-Item -ItemType Directory -Path $out | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
$msbuild = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$csc -or !$msbuild) { throw 'VS 2026 MSBuild/Roslyn not found' }
$runtime = if ($RuntimeDirectory) { [IO.Path]::GetFullPath($RuntimeDirectory) } else { Join-Path $out 'build\bin' }
if (!$SkipBuild) {
    $props = Join-Path $out 'build-paths.props'
    $obj = [Security.SecurityElement]::Escape((Join-Path $out 'build\obj\'))
    [IO.File]::WriteAllText($props, ('<Project><PropertyGroup><BaseIntermediateOutputPath>' + $obj + '$(MSBuildProjectName)\</BaseIntermediateOutputPath><IntermediateOutputPath>$(BaseIntermediateOutputPath)$(Configuration)\</IntermediateOutputPath></PropertyGroup></Project>'))
    & $msbuild (Join-Path $repo 'PalTimerOnline\PalTimerOnline.csproj') /p:Configuration=Release /p:Platform=x64 "/p:PalSharedSourceRoot=$PalSharedSourceRoot" "/p:OutputPath=$runtime\" "/p:CustomBeforeMicrosoftCommonTargets=$props" /m /nodeReuse:false /v:minimal /nologo *> (Join-Path $out 'release-build.log')
    if ($LASTEXITCODE -ne 0) { throw "Release build failed: $out" }
    if (!$PluginResource) { $PluginResource = Join-Path $out 'build\obj\Pal98Timer\Release\Pal98Timer.PluginMgrForm.resources' }
}
[xml]$project = Get-Content -LiteralPath (Join-Path $repo 'Pal98Timer\Pal98Timer.csproj')
$sources = @($project.Project.ItemGroup.Compile | Where-Object { $_.Include } | ForEach-Object {
    $include = $_.Include.Replace('$(PalSharedSourceRoot)', $PalSharedSourceRoot)
    if ([IO.Path]::IsPathRooted($include)) { $include } else { Join-Path $repo ('Pal98Timer\' + $include) }
})
# Synthetic snapshots must never be discoverable by an already-running timer
# or browser. Change only transport names in temporary host source copies;
# the production assemblies and protocol implementation remain untouched.
$fixturePrefix = 'SDLPAL.GeshiFixture.' + [Guid]::NewGuid().ToString('N') + '.'
function Copy-IsolatedSource([string]$path) {
    $source = [IO.File]::ReadAllText($path)
    if (!$source.Contains('SDLPAL.LostEdit.')) { throw "Fixture transport marker missing: $path" }
    $copy = Join-Path $out ([IO.Path]::GetFileName($path))
    [IO.File]::WriteAllText($copy, $source.Replace('SDLPAL.LostEdit.', $fixturePrefix), [Text.UTF8Encoding]::new($false))
    return $copy
}
$sources = @($sources | ForEach-Object {
    if ([IO.Path]::GetFileName($_) -in @('SdlpalBridge.cs','SdlpalRelay.cs')) { Copy-IsolatedSource $_ } else { $_ }
})
$hostSource = Copy-IsolatedSource (Join-Path $PSScriptRoot 'sdlpal_behavior.cs')
[ordered]@{fixtureOnly=$true;mappingPrefix=$fixturePrefix;productSourcesModified=$false} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'isolation.json') -Encoding utf8NoBOM
$references = @($project.Project.ItemGroup.Reference | Where-Object { $_.Include } | ForEach-Object {
    if ($_.HintPath) { '/reference:' + (Join-Path $repo ('Pal98Timer\' + $_.HintPath)) }
    else { '/reference:' + $_.Include.Split(',')[0] + '.dll' }
})
$references += '/reference:System.Core.dll', '/reference:System.IO.Compression.dll', ('/reference:' + (Join-Path $runtime 'TimerPluginBase.dll'))
$exe = Join-Path $out 'SdlpalBehavior.exe'
$configFixture = Join-Path $out 'config-fixture.cs'
[IO.File]::WriteAllText($configFixture, 'using System.Reflection; [assembly:AssemblyProduct("Pal98ConfigTool Geshi")] class ConfigFixture { static void Main() {} }')
$configOriginal = Join-Path $out '隔世追凶配置工具.exe'
& $csc /nologo /noconfig /target:exe "/out:$configOriginal" $configFixture *> (Join-Path $out 'config-fixture-build.log')
if ($LASTEXITCODE -ne 0) { throw 'Configuration identity fixture failed to build' }
Move-Item -LiteralPath $configOriginal -Destination (Join-Path $out '我的 配置.exe')
& $csc /nologo /noconfig /target:exe /platform:x64 /main:SdlpalBehavior "/out:$exe" "/resource:$pluginResource,Pal98Timer.PluginMgrForm.resources" $references $sources $hostSource *> (Join-Path $out 'host-build.log')
if ($LASTEXITCODE -ne 0) { throw "SDL host build failed: $out" }
Get-ChildItem -LiteralPath $runtime -Filter '*.dll' | Copy-Item -Destination $out
Copy-Item -LiteralPath (Join-Path $runtime 'Pal98Timer.exe') -Destination $out
Copy-Item -LiteralPath (Join-Path $runtime 'PalTimerOnline.dll') -Destination $out
Copy-Item -LiteralPath (Join-Path $runtime 'Pal98Timer.exe.config') -Destination ($exe + '.config')
$arguments = @()
if ($NoGui) { $arguments += '--no-gui' }
if ($SplitDisplayOnly) { $arguments += '--split-display-only' }
if ($BestConfigurationOnly) { $arguments += '--best-configuration-only' }
foreach ($file in $NativeSnapshotFile) { $arguments += (Resolve-Path -LiteralPath $file).Path }
Push-Location -LiteralPath $out
try { & $exe @arguments 2>&1 | Tee-Object -FilePath (Join-Path $out 'result.txt'); $result = $LASTEXITCODE }
finally { Pop-Location }
Write-Output "Evidence: $out"
if ($result -ne 0) { throw "SDLPAL behavior failed: $out" }
