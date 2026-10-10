param([string]$TimerDirectory='', [switch]$ExpectOldFailure)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$runtime=if($TimerDirectory){[IO.Path]::GetFullPath($TimerDirectory)}else{Join-Path $repo 'Pal98Timer/bin/x64/Release'}
$out=Join-Path $repo ('artifacts/startup-permissions-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
[void][IO.Directory]::CreateDirectory($out)
Get-ChildItem -LiteralPath $runtime -File | Where-Object { $_.Extension -eq '.dll' -or $_.Name -eq 'Pal98Timer.exe' } | Copy-Item -Destination $out
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$csc=& $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1
$probe=Join-Path $out 'StartupPermissionBehavior.exe'
& $csc /nologo /noconfig /target:exe /platform:x64 "/out:$probe" /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll "/reference:$out/Pal98Timer.exe" (Join-Path $PSScriptRoot 'startup_permission_behavior.cs') *> (Join-Path $out 'build.log')
if($LASTEXITCODE){throw "Probe build failed: $out"}
Copy-Item -LiteralPath (Join-Path $runtime 'Pal98Timer.exe.config') -Destination ($probe+'.config')
Push-Location -LiteralPath $out
try {
    $mode=if($ExpectOldFailure){'expect-old-failure'}else{'fixed'}
    & $probe (Join-Path $out 'fixture') $mode *> (Join-Path $out 'behavior.log');$code=$LASTEXITCODE
    Get-Content -LiteralPath (Join-Path $out 'behavior.log')
    if($code){throw "Startup permission behavior failed: $out"}
}finally{Pop-Location}
Write-Output "Evidence: $out"
