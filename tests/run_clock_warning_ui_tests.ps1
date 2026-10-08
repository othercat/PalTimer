param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repo ('artifacts\clock-warning-ui-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
[IO.Directory]::CreateDirectory($out) | Out-Null
$runtime = Join-Path $repo 'Pal98Timer\bin\x64\Release'
Get-ChildItem -LiteralPath $runtime -File -Filter '*.dll' | Copy-Item -Destination $out
Copy-Item -LiteralPath (Join-Path $runtime 'Pal98Timer.exe') -Destination $out
Copy-Item -LiteralPath (Join-Path $repo 'PalTimerOnline\bin\x64\Release\PalTimerOnline.dll') -Destination $out
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$csc) { throw 'VS2026 Roslyn not found' }
$exe = Join-Path $out 'ClockWarningUiBehavior.exe'
& $csc /nologo /target:exe /platform:x64 "/out:$exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'clock_warning_ui_behavior.cs') *> (Join-Path $out 'build.log')
if ($LASTEXITCODE) { throw "UI fixture build failed: $out" }
Copy-Item -LiteralPath (Join-Path $runtime 'Pal98Timer.exe.config') -Destination ($exe + '.config')
& $exe $out 2>&1 | Tee-Object -FilePath (Join-Path $out 'result.log')
$result = $LASTEXITCODE
Write-Output "Evidence: $out"
if ($result) { throw 'Clock warning UI fixture failed' }
