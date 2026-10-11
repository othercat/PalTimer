param([switch]$SkipBuild, [string]$PresetDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repo ('artifacts\local-configuration-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
[IO.Directory]::CreateDirectory($out) | Out-Null
$vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
$msbuild = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$csc -or !$msbuild) { throw 'VS 2026 MSBuild/Roslyn not found' }
if (!$SkipBuild) {
    & $msbuild (Join-Path $repo 'Pal98Timer.sln') /p:Configuration=Release /p:Platform=x64 /m /v:minimal /nologo *> (Join-Path $out 'build.log')
    if ($LASTEXITCODE -ne 0) { throw "Release build failed: $out" }
}
[xml]$project = Get-Content -LiteralPath (Join-Path $repo 'Pal98Timer\Pal98Timer.csproj')
$sources = @($project.Project.ItemGroup.Compile | Where-Object { $_.Include } | ForEach-Object { Join-Path $repo ('Pal98Timer\' + $_.Include) })
$references = @($project.Project.ItemGroup.Reference | Where-Object { $_.Include } | ForEach-Object {
    if ($_.HintPath) { '/reference:' + (Join-Path $repo ('Pal98Timer\' + $_.HintPath)) }
    else { '/reference:' + $_.Include.Split(',')[0] + '.dll' }
})
$runtime = Join-Path $repo 'Pal98Timer\bin\x64\Release'
$references += '/reference:System.Core.dll', ('/reference:' + (Join-Path $runtime 'TimerPluginBase.dll'))
$exe = Join-Path $out 'LocalConfigurationBehavior.exe'
& $csc /nologo /noconfig /target:exe /platform:x64 /main:LocalConfigurationBehavior "/out:$exe" $references $sources (Join-Path $PSScriptRoot 'local_configuration_behavior.cs') *> (Join-Path $out 'host-build.log')
if ($LASTEXITCODE -ne 0) { throw "Local configuration host compilation failed: $out" }
Get-ChildItem -LiteralPath $runtime -Filter '*.dll' | Copy-Item -Destination $out
Copy-Item -LiteralPath (Join-Path $runtime 'Pal98Timer.exe.config') -Destination ($exe + '.config')
Copy-Item -LiteralPath (Join-Path $repo 'KeyChanger\bin\Release\KeyChanger.exe') -Destination $out
$hostArgs = @()
if ($PresetDirectory) { $hostArgs += (Resolve-Path -LiteralPath $PresetDirectory).Path }
Push-Location -LiteralPath $out
try { & $exe @hostArgs 2>&1 | Tee-Object -FilePath (Join-Path $out 'result.log'); $result = $LASTEXITCODE }
finally { Pop-Location }
$receipt = [ordered]@{
    schema='PalTimer.LocalConfigurationBehavior.v1'; timestamp=(Get-Date).ToString('o'); hostExitCode=$result;
    head=(& git -C $repo rev-parse HEAD); realGameUsed=$false; networkUsed=$false; syntheticProcessUsed=$true;
    testHostSha256=(Get-FileHash -LiteralPath $exe).Hash;
    releaseExeSha256=(Get-FileHash -LiteralPath (Join-Path $runtime 'Pal98Timer.exe')).Hash;
    sourceHashes=@(($sources + (Join-Path $PSScriptRoot 'local_configuration_behavior.cs')) | ForEach-Object { [ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash} });
    presetHashes=@($(if ($PresetDirectory) { Get-ChildItem -LiteralPath $PresetDirectory -Filter '*.pal98config' | Get-FileHash | Select-Object Path,Hash }));
    results=(Join-Path $out 'result.log')
}
[IO.File]::WriteAllText((Join-Path $out 'receipt.json'), ($receipt | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Output "Evidence: $out"
if ($result -ne 0) { throw "Local configuration behavior failed: $out" }
