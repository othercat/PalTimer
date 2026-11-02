param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$out = [IO.Path]::GetFullPath($OutputDirectory)
if ($out -notmatch '[\\/]PALWork[\\/]artifacts[\\/]so-lostedit-issue-[1-9][0-9]*[\\/]') { throw 'Use the owning Issue artifacts directory.' }
if (Test-Path -LiteralPath $out) { throw 'Choose a fresh output directory.' }
[void][IO.Directory]::CreateDirectory($out)
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$csc) { throw 'Visual Studio 2026 is required.' }
$source = Join-Path $PSScriptRoot 'RuntimeLauncher.cs'
& $csc /nologo /target:winexe /platform:x64 /optimize+ "/out:$out\PalTimerAuto.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll $source *> (Join-Path $out 'build.log')
if ($LASTEXITCODE) { throw "Launcher build failed: $out" }
[IO.File]::WriteAllText((Join-Path $out 'PalTimerAuto.exe.config'), '<?xml version="1.0"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.7.2"/></startup></configuration>', [Text.UTF8Encoding]::new($false))
& $csc /nologo /target:exe /platform:x64 "/main:LauncherTests" "/out:$out\LauncherTests.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll $source (Join-Path $PSScriptRoot 'LauncherTests.cs') *> (Join-Path $out 'test-build.log')
if ($LASTEXITCODE) { throw "Launcher test build failed: $out" }
& (Join-Path $out 'LauncherTests.exe') 2>&1 | Tee-Object -FilePath (Join-Path $out 'tests.log')
if ($LASTEXITCODE) { throw "Launcher tests failed: $out" }
