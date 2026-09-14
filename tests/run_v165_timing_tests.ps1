param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$repoRoot = Split-Path -Parent $PSScriptRoot
$runRoot = Join-Path $repoRoot ('artifacts\v165-timing-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
[IO.Directory]::CreateDirectory($runRoot) | Out-Null
$vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (-not $msbuild -or -not $csc) { throw 'VS 2026 MSBuild/Roslyn was not found.' }
if (-not $SkipBuild) {
    & $msbuild (Join-Path $repoRoot 'Pal98Timer.sln') /p:Configuration=Release /p:Platform=x64 /m /v:minimal /nologo *> (Join-Path $runRoot 'build.log')
    if ($LASTEXITCODE -ne 0) { throw "Release build failed: $runRoot\build.log" }
}
$runtime = Join-Path $repoRoot 'Pal98Timer\bin\x64\Release'
Get-ChildItem -LiteralPath $runtime -File | Where-Object { $_.Extension -eq '.dll' -or $_.Name -eq 'Pal98Timer.exe' } |
    Copy-Item -Destination $runRoot
$hostExe = Join-Path $runRoot 'V165TimingBehavior.exe'
Copy-Item -LiteralPath (Join-Path $runtime 'Pal98Timer.exe.config') -Destination ($hostExe + '.config')
& $csc /nologo /target:exe /platform:x64 "/out:$hostExe" "/reference:$runtime\Pal98Timer.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'v165_timing_behavior.cs')
if ($LASTEXITCODE -ne 0) { throw 'Timing behavior harness compilation failed.' }
& $hostExe $runRoot $repoRoot 2>&1 | Tee-Object -FilePath (Join-Path $runRoot 'behavior.log')
$hostExit = $LASTEXITCODE
$receipt = [ordered]@{
    schema='PAL98.TimerV165Validation.v1'; timestamp=(Get-Date).ToString('o'); repository=$repoRoot;
    artifact=(Join-Path $runRoot 'Pal98Timer.exe'); sha256=(Get-FileHash -LiteralPath (Join-Path $runRoot 'Pal98Timer.exe') -Algorithm SHA256).Hash;
    version=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $runRoot 'Pal98Timer.exe')).FileVersion;
    hostExitCode=$hostExit; isolatedDirectory=$runRoot; realGameUsed=$false; visibleGuiUsed=$false;
    results=(Join-Path $runRoot 'results.json'); log=(Join-Path $runRoot 'behavior.log')
}
[IO.File]::WriteAllText((Join-Path $runRoot 'receipt.json'), ($receipt | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Write-Output "Evidence: $runRoot"
if ($hostExit -ne 0) { throw "Timing behavior failed; preserved evidence: $runRoot" }
