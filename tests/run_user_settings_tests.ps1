param([string]$TimerDirectory='', [string]$KeyboardExe='')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$runtime=if($TimerDirectory){[IO.Path]::GetFullPath($TimerDirectory)}else{Join-Path $repo 'Pal98Timer/bin/x64/Release'}
$keyboard=if($KeyboardExe){[IO.Path]::GetFullPath($KeyboardExe)}else{Join-Path $repo 'KeyChanger/bin/Release/KeyChanger.exe'}
$out=Join-Path $repo ('artifacts/user-settings-tests-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
[void][IO.Directory]::CreateDirectory($out)
Get-ChildItem -LiteralPath $runtime -File | Where-Object { $_.Extension -eq '.dll' -or $_.Name -eq 'Pal98Timer.exe' } | Copy-Item -Destination $out
Copy-Item -LiteralPath $keyboard -Destination $out
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$csc=& $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1
$probe=Join-Path $out 'UserSettingsBehavior.exe'
& $csc /nologo /noconfig /target:exe /platform:x64 "/out:$probe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/reference:$out/Pal98Timer.exe" (Join-Path $PSScriptRoot 'user_settings_behavior.cs') *> (Join-Path $out 'build.log')
if($LASTEXITCODE){Get-Content (Join-Path $out 'build.log');throw "Probe build failed: $out"}
Copy-Item -LiteralPath (Join-Path $runtime 'Pal98Timer.exe.config') -Destination ($probe+'.config')
Push-Location -LiteralPath $out
try {
    & $probe (Join-Path $out 'fixture') (Join-Path $out 'KeyChanger.exe') *> (Join-Path $out 'behavior.log');$code=$LASTEXITCODE
    Get-Content -LiteralPath (Join-Path $out 'behavior.log')
    if($code){throw "User settings behavior failed: $out"}
}finally{Pop-Location}
Write-Output "Evidence: $out"
