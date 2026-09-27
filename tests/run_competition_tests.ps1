param([string]$Server = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repo ('artifacts\competition-tests-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $out | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$csc) { throw 'VS 2026 Roslyn not found' }
$sources = @('CompetitionProtocol.cs','CompetitionStorage.cs','CompetitionTransport.cs','CompetitionAuth.cs','CompetitionClient.cs','CompetitionGameSettings.cs','CompetitionWindows.cs','TournamentLockInfoReader.cs') | ForEach-Object { Join-Path $repo ('Pal98Timer\' + $_) }
$sources += Join-Path $PSScriptRoot 'competition_behavior.cs'
$exe = Join-Path $out 'CompetitionBehavior.exe'
& $csc /nologo /noconfig /target:exe /platform:x64 "/out:$exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.dll /reference:System.Security.dll /reference:System.Net.Http.dll "/reference:$repo\Pal98Timer\lib\System.Web.Script.Serialization.dll" $sources *> (Join-Path $out 'build.log')
if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $out 'build.log'); throw 'Competition test build failed' }
Copy-Item -LiteralPath (Join-Path $repo 'Pal98Timer\lib\System.Web.Script.Serialization.dll') -Destination $out
Copy-Item -LiteralPath (Join-Path $repo 'Pal98Timer\bin\x64\Release\Pal98Timer.exe.config') -Destination ($exe + '.config')
$arguments = @(Join-Path $out 'data')
if ($Server) { $arguments += $Server }
& $exe @arguments 2>&1 | Tee-Object -FilePath (Join-Path $out 'result.txt')
$result = $LASTEXITCODE
Write-Output "Evidence: $out"
if ($result -ne 0) { throw 'Competition behavior failed' }
