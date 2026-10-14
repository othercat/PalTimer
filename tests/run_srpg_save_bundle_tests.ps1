param([string]$TimerAssembly)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repo ('artifacts\srpg-save-bundle-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
[void][IO.Directory]::CreateDirectory($out)
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products '*' -version '[18.0,19.0)' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'VS 2026 C++ tools not found' }
$csc = Join-Path $vs 'MSBuild\Current\Bin\Roslyn\csc.exe'
$msbuild = Join-Path $vs 'MSBuild\Current\Bin\MSBuild.exe'
if (!$TimerAssembly) {
    $bin = Join-Path $out 'bin'
    & $msbuild (Join-Path $repo 'Pal98Timer.sln') /t:Pal98Timer /p:Configuration=Release /p:Platform=x64 "/p:OutDir=$bin\" /m /v:minimal /nologo *> (Join-Path $out 'build.log')
    if ($LASTEXITCODE -ne 0) { throw "Release x64 build failed: $out" }
    $TimerAssembly = Join-Path $bin 'Pal98Timer.exe'
}
$TimerAssembly = (Resolve-Path -LiteralPath $TimerAssembly).Path
$fixtureSource = Join-Path $out 'FixtureDll.cs'
[IO.File]::WriteAllText($fixtureSource, '[assembly:System.Reflection.AssemblyFileVersion("1.7.2.1")] public class Fixture {}')
$fixtureDll = Join-Path $out 'Fixture.dll'
& $csc /nologo /target:library "/out:$fixtureDll" $fixtureSource
if ($LASTEXITCODE -ne 0) { throw 'Fixture assembly failed' }
$fixtureRoot = Join-Path $out 'fixtures'
foreach ($entry in @(@('srpg_random_skill_bundle_host.cs','RandomBundle.exe'), @('srpg_save_bundle_host.cs','ExistingBundle.exe'))) {
    $exe = Join-Path $out $entry[1]
    & $csc /nologo /target:exe /platform:x64 "/out:$exe" /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot $entry[0]) *> (Join-Path $out ($entry[1] + '.build.log'))
    if ($LASTEXITCODE -ne 0) { throw "Host build failed: $out" }
    if ($entry[0] -eq 'srpg_random_skill_bundle_host.cs') { & $exe $TimerAssembly $fixtureRoot $fixtureDll 2>&1 | Tee-Object (Join-Path $out 'random-result.txt') }
    else { & $exe $TimerAssembly (Join-Path $fixtureRoot 'legacy-bundle-fixture') 2>&1 | Tee-Object (Join-Path $out 'existing-result.txt') }
    if ($LASTEXITCODE -ne 0) { throw "Bundle host failed: $out" }
}
# Exercise real timer restoration and pause/resume in an isolated directory.
# The host substitutes only live attachment/readiness; it never starts PAL.exe.
$timeOut = Join-Path $out 'timing'
[void][IO.Directory]::CreateDirectory($timeOut)
$timerBin = Split-Path -Parent $TimerAssembly
foreach ($name in @('Pal98Timer.exe','PalCloudLib.dll','System.Web.Script.Serialization.dll','TimerPluginBase.dll')) {
    Copy-Item -LiteralPath (Join-Path $timerBin $name) -Destination (Join-Path $timeOut $name)
}
$timeExe = Join-Path $timeOut 'RelayTime.exe'
Copy-Item -LiteralPath (Join-Path $timerBin 'Pal98Timer.exe.config') -Destination ($timeExe + '.config')
& $csc /nologo /target:exe /platform:x64 "/out:$timeExe" "/reference:$TimerAssembly" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'srpg_relay_time_host.cs') *> (Join-Path $timeOut 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Relay timer host build failed: $timeOut" }
& $timeExe $timeOut *> (Join-Path $timeOut 'result.log')
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $timeOut 'result.log') -Tail 15; throw "Relay time restoration failed: $timeOut" }
Get-Content -LiteralPath (Join-Path $timeOut 'result.log') -Tail 1
# Validate emitted bytes against the current producer's real C++ decoder.
$palDllRepo = Join-Path (Split-Path -Parent $repo) 'PALDLL_DX9'
Import-Module (Join-Path $vs 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll')
Enter-VsDevShell -VsInstallPath $vs -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null
Push-Location -LiteralPath $out
try {
    & cl.exe /nologo /EHsc /std:c++17 "/I$palDllRepo" /Fe:Consumer.exe (Join-Path $PSScriptRoot 'srpg_random_skill_consumer_host.cpp') (Join-Path $palDllRepo 'random_skill_run_state.cpp') (Join-Path $palDllRepo 'simple_json.cpp') *> consumer-build.log
    if ($LASTEXITCODE -ne 0) { throw "PALDLL consumer build failed: $out" }
    foreach ($relative in @('A\3.RPG','B\1.RPG')) {
        $rpg = Join-Path $fixtureRoot $relative
        $sha = (Get-FileHash -LiteralPath $rpg -Algorithm SHA256).Hash.ToLowerInvariant()
        & .\Consumer.exe ($rpg + '.pal98-random-skills.json') ([IO.Path]::GetFileName($rpg)) $sha 2>&1 | Tee-Object -FilePath consumer-result.txt -Append
        if ($LASTEXITCODE -ne 0) { throw 'PALDLL rejected imported state' }
    }
} finally { Pop-Location }
[ordered]@{schema='PalTimer.SrpgRandomStateTest.v1'; head=(& git -C $repo rev-parse HEAD); assembly=$TimerAssembly; assemblySha256=(Get-FileHash -LiteralPath $TimerAssembly).Hash; producerHead=(& git -C $palDllRepo rev-parse HEAD); realGame=$false; network=$false; result='PASS'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'receipt.json') -Encoding utf8
Write-Output "Evidence: $out"
