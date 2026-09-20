$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repo ('artifacts\cloud-session-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $out | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$csc) { throw 'VS 2026 Roslyn not found' }
$cloudDll = Join-Path $repo 'Pal98Timer\lib\PalCloudLib.dll'
Get-ChildItem -LiteralPath (Join-Path $repo 'Pal98Timer\bin\x64\Release') -Filter '*.dll' | Copy-Item -Destination $out
$exe = Join-Path $out 'CloudSessionBehavior.exe'
& $csc /nologo /target:exe /platform:x64 "/out:$exe" "/reference:$cloudDll" /reference:System.dll /reference:System.Core.dll (Join-Path $repo 'Pal98Timer\CloudSessionRunner.cs') (Join-Path $repo 'Pal98Timer\LegacyCloudStepClient.cs') (Join-Path $PSScriptRoot 'cloud_session_behavior.cs') *> (Join-Path $out 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Build failed: $out" }
& $exe 2>&1 | Tee-Object -FilePath (Join-Path $out 'result.txt')
$result = $LASTEXITCODE
Write-Output "Evidence: $out"
if ($result -ne 0) { throw 'Offline cloud host failed' }
