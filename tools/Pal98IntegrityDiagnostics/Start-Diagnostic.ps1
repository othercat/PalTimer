param(
    [string]$GameRoot,
    [string]$TimerExe,
    [ValidateRange(5,240)][int]$Seconds=240,
    [string]$ReportDirectory,
    [switch]$NoPause
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$run=$null
$resultCode=0
try {
    if(-not $ReportDirectory){$ReportDirectory=Join-Path $PSScriptRoot 'Reports'}
    $run=Join-Path ([IO.Path]::GetFullPath($ReportDirectory)) ((Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
    [IO.Directory]::CreateDirectory($run)|Out-Null
    $sessionId=[Diagnostics.Process]::GetCurrentProcess().SessionId
    $processes=@(Get-CimInstance Win32_Process -Filter "Name='PAL.EXE' OR Name='Pal98Timer.exe'" | Where-Object {$_.SessionId -eq $sessionId})
    $games=@($processes|Where-Object {$_.Name -ieq 'PAL.EXE' -and $_.ExecutablePath})
    if($GameRoot){
        $GameRoot=[IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
        $games=@($games|Where-Object {[IO.Path]::GetDirectoryName($_.ExecutablePath) -ieq $GameRoot})
    }
    if($games.Count -ne 1){throw '请先打开需要检查的游戏，并只保留一个 PAL.EXE；本工具不会启动或关闭游戏。'}
    $palExe=[IO.Path]::GetFullPath($games[0].ExecutablePath)
    $GameRoot=[IO.Path]::GetDirectoryName($palExe)
    $game=Get-Process -Id $games[0].ProcessId
    $creation=$game.StartTime.ToUniversalTime().ToFileTimeUtc()
    if(-not $TimerExe){
        $timers=@($processes|Where-Object {$_.Name -ieq 'Pal98Timer.exe' -and $_.ExecutablePath})
        if($timers.Count -ne 1){throw '请打开出现提示的计时器，并只保留一个 Pal98Timer.exe；也可使用 -TimerExe 指定实际计时器文件。'}
        $TimerExe=$timers[0].ExecutablePath
    }
    $TimerExe=[IO.Path]::GetFullPath($TimerExe)
    if(-not (Test-Path -LiteralPath $TimerExe -PathType Leaf)){throw '找不到实际计时器 EXE。'}
    $system=Get-CimInstance Win32_OperatingSystem
    $computer=Get-CimInstance Win32_ComputerSystem
    $framework=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction SilentlyContinue
    $frameworkRelease=$null
    if($framework -and $framework.PSObject.Properties['Release']){$frameworkRelease=$framework.Release}
    $info=[ordered]@{
        schema='PAL98.IntegrityDiagnostic.Environment.v1'; timestamp=(Get-Date).ToString('o')
        windows=$system.Caption; build=$system.BuildNumber; osArchitecture=$system.OSArchitecture
        systemType=$computer.SystemType; model=$computer.Model; manufacturer=$computer.Manufacturer
        hostProcessArchitecture=$env:PROCESSOR_ARCHITECTURE
        frameworkRelease=$frameworkRelease
        pid=$game.Id; creationFileTime=$creation.ToString([Globalization.CultureInfo]::InvariantCulture)
        gameExe=$palExe; gameRoot=$GameRoot; timerExe=$TimerExe
        timerSHA256=(Get-FileHash -LiteralPath $TimerExe -Algorithm SHA256).Hash
        palDllSHA256=(Get-FileHash -LiteralPath (Join-Path $GameRoot 'PAL.dll') -Algorithm SHA256).Hash
        readOnly=$true; cloudContacted=$false
    }
    $info|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $run 'environment.json') -Encoding utf8
    Write-Host '正在只读检查实际运行的游戏。最多四分钟，不修改游戏、配置、计时器或成绩。'
    Write-Host ('游戏：'+$GameRoot)
    Write-Host ('计时器：'+$TimerExe)
    Write-Host '请保持游戏运行。只需停在标题画面，不需要开始跑图。'
    $probe=Join-Path $PSScriptRoot 'Pal98IntegrityProbe.exe'
    $output=Join-Path $run 'diagnostic.json'
    & $probe $TimerExe $game.Id $creation $palExe $Seconds $output *> (Join-Path $run 'probe.log')
    if($LASTEXITCODE -ne 0){throw ('诊断读取失败；详见 '+(Join-Path $run 'probe.log'))}
    $data=Get-Content -LiteralPath $output -Raw -Encoding utf8|ConvertFrom-Json
    $final=$data.final
    $text=@(
        'PAL98 核验诊断（只读）'
        ('游戏目录：'+$GameRoot)
        ('计时器文件：'+$TimerExe)
        ('实际扫描结果：'+$final.files)
        ('实际代码结果：'+$final.code)
        ('提示：'+$final.summary)
        ('说明：'+$final.detail)
    )
    if($final.PSObject.Properties['file_verifier_state']){
        $text+=('独立文件结果：'+$final.file_verifier_state)
        $text+=('独立文件说明：'+$final.file_verifier_detail)
        $text+=('首轮扫描完成：'+$final.scan_complete)
    }
    $text+=@(
        ''
        '两个图形组合的候选文件都列在 JSON 中；未启用组合有差异是正常的，不能据此单独认定不匹配。'
        '是否异常应看独立文件结果、所选 graphics_chain、实际代码结果和说明。'
        '本报告不证明存档来源或比赛合法性；未进行云激活验证。'
        '请将本次报告目录内的 environment.json、diagnostic.json、probe.log 和 result.txt 一并发回。'
    )
    $text|Set-Content -LiteralPath (Join-Path $run 'result.txt') -Encoding utf8
    $text|Write-Host
} catch {
    $resultCode=1
    $message=$_.Exception.GetBaseException().Message
    Write-Host ('诊断未完成：'+$message) -ForegroundColor Yellow
    if($run){$message|Set-Content -LiteralPath (Join-Path $run 'error.txt') -Encoding utf8}
} finally {
    if($run){Write-Host ('报告目录：'+$run)}
    if(-not $NoPause){Read-Host '按回车关闭'|Out-Null}
}
exit $resultCode
