param(
    [Parameter(Mandatory=$true)][int]$ProcessId,
    [Parameter(Mandatory=$true)][long]$CreationFileTime,
    [Parameter(Mandatory=$true)][string]$ExpectedExecutable,
    [ValidateRange(5,120)][int]$Seconds=30
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$repoRoot=Split-Path -Parent $PSScriptRoot
$runtime=Join-Path $repoRoot 'Pal98Timer\bin\x64\Release'
$runRoot=Join-Path $repoRoot ('artifacts\v169-readonly-probe-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
[IO.Directory]::CreateDirectory($runRoot)|Out-Null
$vswhere=Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc=& $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe'|Select-Object -First 1
if(-not $csc){throw 'VS 2026 Roslyn was not found.'}
# Copy only the product and JSON dependency; do not load/copy cloud components.
foreach($name in @('Pal98Timer.exe','System.Web.Script.Serialization.dll')){
    Copy-Item -LiteralPath (Join-Path $runtime $name) -Destination $runRoot
}
$probe=Join-Path $runRoot 'V169ReadonlyProbe.exe'
Copy-Item -LiteralPath (Join-Path $runtime 'Pal98Timer.exe.config') -Destination ($probe+'.config')
& $csc /nologo /target:exe /platform:x64 "/out:$probe" /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'v169_readonly_probe.cs')
if($LASTEXITCODE -ne 0){throw 'Readonly probe compilation failed.'}
$outputPath=Join-Path $runRoot 'probe.json'
& $probe (Join-Path $runRoot 'Pal98Timer.exe') $ProcessId $CreationFileTime ([IO.Path]::GetFullPath($ExpectedExecutable)) $Seconds $outputPath 2>&1|Tee-Object -FilePath (Join-Path $runRoot 'probe.log')
$probeExit=$LASTEXITCODE
[ordered]@{
    schema='PAL98.ReadonlyIntegrityProbeReceipt.v1'; timestamp=(Get-Date).ToString('o'); pid=$ProcessId;
    creationFileTime=$CreationFileTime.ToString(); expectedExecutable=[IO.Path]::GetFullPath($ExpectedExecutable);
    timerSha256=(Get-FileHash -LiteralPath (Join-Path $runRoot 'Pal98Timer.exe') -Algorithm SHA256).Hash;
    manifestSha256=(Get-FileHash -LiteralPath (Join-Path $repoRoot 'Pal98Timer\release_integrity.v1.json') -Algorithm SHA256).Hash;
    exitCode=$probeExit; output=$outputPath; readOnly=$true; realCloudUsed=$false
}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $runRoot 'receipt.json') -Encoding utf8
Write-Output "Evidence: $runRoot"
if($probeExit -ne 0){throw 'Readonly probe failed; see preserved evidence.'}
