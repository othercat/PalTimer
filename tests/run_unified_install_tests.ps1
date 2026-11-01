param([Parameter(Mandatory)][string]$OutputDirectory, [Parameter(Mandatory)][string]$PayloadDirectory)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Use a fresh Issue artifact directory.'}
[void][IO.Directory]::CreateDirectory($out)
$install=Join-Path $PSScriptRoot '..\scripts\install-unified.ps1'
$checks=0
function Check($condition,$message){if(!$condition){throw $message};$script:checks++;Write-Output "PASS $message"}
$manifest=Get-Content -LiteralPath (Join-Path $PayloadDirectory 'payload.json') -Raw | ConvertFrom-Json
foreach($edition in @('Original','SDL')){
    # Synthetic layout only: this is not a game package and Pal.exe is never executed.
    $game=Join-Path $out $edition
    $relative=if($edition -eq 'Original'){'Tools\仙剑98自动计时器'}else{'Tools\Timer'}
    $target=Join-Path $game $relative
    [void][IO.Directory]::CreateDirectory($target)
    [IO.File]::WriteAllText((Join-Path $game 'Pal.exe'),'fixture, not executable')
    & $install -GameDirectory $game -Edition $edition -PayloadDirectory $PayloadDirectory
    $default=if($edition -eq 'Original'){'Pal98Dx9Automatic'}else{'SdlpalGeshi'}
    Check ([IO.File]::ReadAllText((Join-Path $target 'LastCore')) -ceq $default) "$edition first install selects its separate core"
    foreach($file in $manifest.files){Check ((Get-FileHash -LiteralPath (Join-Path $target $file.name)).Hash -ieq $file.sha256) "$edition receives shared $($file.name) bytes"}
    foreach($name in @('LastCore','skip_node','config.txt','PB.txt')){[IO.File]::WriteAllText((Join-Path $target $name),'keep-user-value')}
    [IO.File]::WriteAllText((Join-Path $target 'Pal98Timer.exe'),'old-exe')
    & $install -GameDirectory $game -Edition $edition -PayloadDirectory $PayloadDirectory
    foreach($name in @('LastCore','skip_node','config.txt','PB.txt')){Check ([IO.File]::ReadAllText((Join-Path $target $name)) -ceq 'keep-user-value') "$edition preserves $name"}
    $backup=Get-ChildItem -LiteralPath (Join-Path $target 'Rollback') -Directory | Sort-Object Name | Select-Object -Last 1
    Check ([IO.File]::ReadAllText((Join-Path $backup.FullName 'Pal98Timer.exe')) -ceq 'old-exe') "$edition retains the previous EXE for rollback"
}
$bad=Join-Path $out 'bad-payload';Copy-Item -LiteralPath $PayloadDirectory -Destination $bad -Recurse
[IO.File]::AppendAllText((Join-Path $bad 'PalTimerOnline.dll'),'changed')
$before=(Get-FileHash -LiteralPath (Join-Path $target 'PalTimerOnline.dll')).Hash
$failed=$false
try{& $install -GameDirectory $game -Edition SDL -PayloadDirectory $bad}catch{$failed=$_.Exception.Message -like 'Payload checksum mismatch:*'}
Check $failed 'rejects a mixed or damaged component before replacing files'
Check ((Get-FileHash -LiteralPath (Join-Path $target 'PalTimerOnline.dll')).Hash -eq $before) 'rejected payload preserves the installed component'
Write-Output "PASS unified install total=$checks; synthetic layouts only, network=none"
