$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$out=Join-Path $repo ('artifacts/process-identity-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
[void][IO.Directory]::CreateDirectory($out)
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$csc=& $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1
if (!$csc) { throw 'VS2026 C# compiler not found' }
$exe=Join-Path $out 'ProcessIdentityBehavior.exe'
& $csc /nologo /target:exe /platform:x64 /reference:System.dll /reference:System.Core.dll "/out:$exe" (Join-Path $repo 'Pal98Timer/PalLiveProcessIdentity.cs') (Join-Path $PSScriptRoot 'process_identity_behavior.cs') *> (Join-Path $out 'build.log')
if ($LASTEXITCODE) { throw "Build failed: $out" }
& $exe *> (Join-Path $out 'result.log')
$result=$LASTEXITCODE
Get-Content (Join-Path $out 'result.log')
Write-Output "Evidence: $out"
if ($result) { throw "Identity tests failed: $result" }
