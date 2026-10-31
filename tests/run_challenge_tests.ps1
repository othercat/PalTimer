param([Parameter(Mandatory=$true)][string]$OutputDirectory, [Parameter(Mandatory=$true)][string]$RuntimeDirectory, [string]$Server = '', [switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = [IO.Path]::GetFullPath($OutputDirectory)
if ($out -notmatch '[\\/]PALWork[\\/]artifacts[\\/](PalTimer|so-lostedit)-issue-[0-9]+[\\/]' -or (Test-Path -LiteralPath $out)) { throw 'Choose a fresh directory in the actual Issue artifact tree.' }
New-Item -ItemType Directory -Path $out | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
$sources = @(Get-ChildItem -LiteralPath (Join-Path $repo 'PalTimerOnline') -Filter '*.cs' | Where-Object Name -ne 'AssemblyInfo.cs' | ForEach-Object FullName)
$sources += @('SdlpalBridge.cs','RankingConfiguration.cs','TournamentLockInfoReader.cs') | ForEach-Object { Join-Path $repo ('Pal98Timer\' + $_) }
$sources += Join-Path $PSScriptRoot 'challenge_behavior.cs'
$sources += Join-Path $PSScriptRoot 'challenge_v1_consumer_fixture.cs'
$exe = Join-Path $out 'ChallengeBehavior.exe'
& $csc /nologo /noconfig /target:exe /platform:x64 /main:ChallengeBehavior "/out:$exe" /reference:System.dll /reference:System.Core.dll /reference:System.Runtime.Serialization.dll /reference:System.Xml.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.dll /reference:System.Security.dll /reference:System.Net.Http.dll "/reference:$RuntimeDirectory\Pal98Timer.exe" "/reference:$repo\Pal98Timer\lib\System.Web.Script.Serialization.dll" $sources *> (Join-Path $out 'build.log')
if ($LASTEXITCODE) { Get-Content -LiteralPath (Join-Path $out 'build.log'); throw 'Challenge tests did not build.' }
Copy-Item -LiteralPath (Join-Path $repo 'Pal98Timer\lib\System.Web.Script.Serialization.dll'), (Join-Path $RuntimeDirectory 'Pal98Timer.exe'), (Join-Path $RuntimeDirectory 'TimerPluginBase.dll') -Destination $out
Copy-Item -LiteralPath (Join-Path $repo 'Pal98Timer\App.config') -Destination ($exe + '.config')
if ($BuildOnly) { Write-Output "Challenge test host: $exe"; return }
$arguments = @((Join-Path $out 'data')); if ($Server) { $arguments += $Server }
& $exe @arguments *> (Join-Path $out 'result.txt')
Get-Content -LiteralPath (Join-Path $out 'result.txt') -Tail 12
if ($LASTEXITCODE) { throw 'Challenge tests failed.' }
