param([Parameter(Mandatory)][string]$RuntimeDirectory,
      [Parameter(Mandatory)][string]$Fixture,
      [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw 'Choose a fresh Issue evidence directory.' }
New-Item -ItemType Directory -Path $out | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$compiler = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$compiler) { throw 'VS 2026 Roslyn is required.' }
$exe = Join-Path $out 'RunStatisticsBehavior.exe'
& $compiler /nologo /noconfig /target:exe /platform:x64 "/out:$exe" /reference:System.dll /reference:System.Core.dll "/reference:$repo\Pal98Timer\lib\System.Web.Script.Serialization.dll" (Join-Path $repo 'Pal98Timer\RunStatistics.cs') (Join-Path $PSScriptRoot 'run_statistics_behavior.cs') *> (Join-Path $out 'build.log')
if ($LASTEXITCODE) { Get-Content -LiteralPath (Join-Path $out 'build.log') -Tail 15; throw 'Statistics test host build failed.' }
Copy-Item -LiteralPath (Join-Path $repo 'Pal98Timer\lib\System.Web.Script.Serialization.dll') -Destination $out
& $exe $RuntimeDirectory $Fixture $out *> (Join-Path $out 'result.txt')
if ($LASTEXITCODE) { Get-Content -LiteralPath (Join-Path $out 'result.txt'); throw 'Statistics check failed.' }
Get-Content -LiteralPath (Join-Path $out 'result.txt')
