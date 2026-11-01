param([Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$RuntimeDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$repoRoot = Split-Path -Parent $PSScriptRoot
$runRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $runRoot) { throw 'Use a fresh output directory.' }
[IO.Directory]::CreateDirectory($runRoot) | Out-Null
$vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = & $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (-not $csc) { throw 'VS 2026 Roslyn was not found.' }
$runtime = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
Get-ChildItem -LiteralPath $runtime -File | Where-Object { $_.Extension -eq '.dll' -or $_.Name -eq 'Pal98Timer.exe' } | Copy-Item -Destination $runRoot
$hostExe = Join-Path $runRoot 'V169IntegrityBehavior.exe'
Copy-Item -LiteralPath (Join-Path $runtime 'Pal98Timer.exe.config') -Destination ($hostExe + '.config')
& $csc /nologo /target:exe /platform:x64 "/out:$hostExe" "/reference:$runtime\Pal98Timer.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'v169_integrity_behavior.cs')
if ($LASTEXITCODE -ne 0) { throw 'Integrity behavior harness compilation failed.' }
& $hostExe $runRoot $repoRoot 2>&1 | Tee-Object -FilePath (Join-Path $runRoot 'behavior.log')
$hostExit = $LASTEXITCODE
$receipt = [ordered]@{
    schema='PAL98.TimerV169Validation.v1'; timestamp=(Get-Date).ToString('o'); repository=$repoRoot;
    artifact=(Join-Path $runRoot 'Pal98Timer.exe'); sha256=(Get-FileHash -LiteralPath (Join-Path $runRoot 'Pal98Timer.exe') -Algorithm SHA256).Hash;
    version=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $runRoot 'Pal98Timer.exe')).FileVersion;
    hostExitCode=$hostExit; isolatedDirectory=$runRoot; realGameUsed=$false; realCloudUsed=$false;
    results=(Join-Path $runRoot 'results.json'); log=(Join-Path $runRoot 'behavior.log')
}
[IO.File]::WriteAllText((Join-Path $runRoot 'receipt.json'), ($receipt | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Write-Output "Evidence: $runRoot"
if ($hostExit -ne 0) { throw "Integrity behavior failed; preserved evidence: $runRoot" }
