param([string]$PalSharedSourceRoot = '', [string[]]$NativeSnapshotFile)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (!$PalSharedSourceRoot) { $PalSharedSourceRoot = Join-Path $repo '..\PALDLL_DX9\shared' }
$out = Join-Path $repo ('artifacts\sdlpal-tests-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $out | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
$msbuild = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$csc -or !$msbuild) { throw 'VS 2026 MSBuild/Roslyn not found' }
& $msbuild (Join-Path $repo 'Pal98Timer.sln') /p:Configuration=Release /p:Platform=x64 "/p:PalSharedSourceRoot=$PalSharedSourceRoot" /m /nodeReuse:false /v:minimal /nologo *> (Join-Path $out 'release-build.log')
if ($LASTEXITCODE -ne 0) { throw "Release build failed: $out" }
[xml]$project = Get-Content -LiteralPath (Join-Path $repo 'Pal98Timer\Pal98Timer.csproj')
$sources = @($project.Project.ItemGroup.Compile | Where-Object { $_.Include } | ForEach-Object {
    $include = $_.Include.Replace('$(PalSharedSourceRoot)', $PalSharedSourceRoot)
    if ([IO.Path]::IsPathRooted($include)) { $include } else { Join-Path $repo ('Pal98Timer\' + $include) }
})
$references = @($project.Project.ItemGroup.Reference | Where-Object { $_.Include } | ForEach-Object {
    if ($_.HintPath) { '/reference:' + (Join-Path $repo ('Pal98Timer\' + $_.HintPath)) }
    else { '/reference:' + $_.Include.Split(',')[0] + '.dll' }
})
$runtime = Join-Path $repo 'Pal98Timer\bin\x64\Release'
$references += '/reference:System.Core.dll', '/reference:System.IO.Compression.dll', ('/reference:' + (Join-Path $runtime 'TimerPluginBase.dll'))
$exe = Join-Path $out 'SdlpalBehavior.exe'
& $csc /nologo /noconfig /target:exe /platform:x64 /main:SdlpalBehavior "/out:$exe" $references $sources (Join-Path $PSScriptRoot 'sdlpal_behavior.cs') *> (Join-Path $out 'host-build.log')
if ($LASTEXITCODE -ne 0) { throw "SDL host build failed: $out" }
Get-ChildItem -LiteralPath $runtime -Filter '*.dll' | Copy-Item -Destination $out
Copy-Item -LiteralPath (Join-Path $runtime 'Pal98Timer.exe.config') -Destination ($exe + '.config')
$arguments = @()
foreach ($file in $NativeSnapshotFile) { $arguments += (Resolve-Path -LiteralPath $file).Path }
Push-Location -LiteralPath $out
try { & $exe @arguments 2>&1 | Tee-Object -FilePath (Join-Path $out 'result.txt'); $result = $LASTEXITCODE }
finally { Pop-Location }
Write-Output "Evidence: $out"
if ($result -ne 0) { throw "SDLPAL behavior failed: $out" }
