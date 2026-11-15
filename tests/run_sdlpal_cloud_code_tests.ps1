param([Parameter(Mandatory=$true)][string]$TimerAssembly,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
if($out -notmatch '[\\/]PALWork[\\/]artifacts[\\/]PalTimer-issue-[0-9]+[\\/]'){throw 'Use the owning Issue artifact directory.'}
if(Test-Path -LiteralPath $out){throw 'Choose a fresh test output directory.'}
New-Item -ItemType Directory -Path $out | Out-Null
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc=& $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
$exe=Join-Path $out 'CloudCodeTests.exe'
& $csc /nologo /target:exe /platform:x64 "/out:$exe" (Join-Path $PSScriptRoot 'sdlpal_cloud_code_tests.cs') *> (Join-Path $out 'build.log')
if($LASTEXITCODE){throw 'Cloud code probe compile failed'}
& $exe $TimerAssembly (Join-Path $out 'result.txt')
if($LASTEXITCODE){throw 'Cloud code checks failed'}
Get-Content -LiteralPath (Join-Path $out 'result.txt')
