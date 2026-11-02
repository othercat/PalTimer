param([Parameter(Mandatory)][string]$RuntimeDirectory,
      [Parameter(Mandatory)][string]$ConfigFixture,
      [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputDirectory) {throw 'Choose a fresh evidence directory.'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1
if (!$csc) {throw 'VS 2026 Roslyn not found.'}
$exe = Join-Path $OutputDirectory 'EffectiveConfigurationTests.exe'
& $csc /nologo /noconfig /target:exe /platform:x64 "/out:$exe" /r:System.dll /r:System.Core.dll "/r:$RuntimeDirectory/System.Web.Script.Serialization.dll" (Join-Path $PSScriptRoot 'effective_configuration_host.cs') *> (Join-Path $OutputDirectory 'build.log')
if ($LASTEXITCODE) {Get-Content (Join-Path $OutputDirectory 'build.log'); throw 'Test build failed.'}
Copy-Item -LiteralPath (Join-Path $RuntimeDirectory 'System.Web.Script.Serialization.dll') -Destination $OutputDirectory
& $exe (Join-Path $RuntimeDirectory 'Pal98Timer.exe') $ConfigFixture (Join-Path $OutputDirectory 'game') *> (Join-Path $OutputDirectory 'result.log')
Get-Content (Join-Path $OutputDirectory 'result.log')
if ($LASTEXITCODE) {throw 'Effective configuration checks failed.'}
