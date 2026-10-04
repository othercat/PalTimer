param([Parameter(Mandatory=$true)][string]$AuthDirectory,
    [string]$TimerDirectory = '', [string]$OnlineDll = '')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$out=Join-Path $repo ('artifacts/online-module-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$runtime=if($TimerDirectory){[IO.Path]::GetFullPath($TimerDirectory)}else{Join-Path $repo 'Pal98Timer/bin/x64/Release'}
if(!$OnlineDll){$OnlineDll=Join-Path $repo 'PalTimerOnline/bin/x64/Release/PalTimerOnline.dll'}
$valid=Join-Path $out 'valid'
[void][IO.Directory]::CreateDirectory($valid)
Get-ChildItem -LiteralPath $runtime -File -Filter '*.dll' | Copy-Item -Destination $valid
Copy-Item -LiteralPath (Join-Path $runtime 'Pal98Timer.exe') -Destination $valid
Copy-Item -LiteralPath $OnlineDll -Destination $valid
foreach($name in @('PalCompetitionAuth.dll','PalCompetitionRegistration.public.json')) {
    Copy-Item -LiteralPath (Join-Path $AuthDirectory $name) -Destination $valid
}
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$csc=& $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1
$probe=Join-Path $valid 'OnlineModuleLoaderBehavior.exe'
& $csc /nologo /noconfig /target:exe /platform:x64 "/out:$probe" /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll "/reference:$valid/System.Web.Script.Serialization.dll" "/reference:$valid/Pal98Timer.exe" (Join-Path $PSScriptRoot 'online_module_loader_behavior.cs') *> (Join-Path $out 'build.log')
if($LASTEXITCODE){throw "Probe build failed: $out"}
Copy-Item -LiteralPath (Join-Path $runtime 'Pal98Timer.exe.config') -Destination ($probe+'.config')
foreach($case in @('valid','missing','online-changed','auth-changed','exe-changed','signature-changed','wrong-api')) {
    $dir=Join-Path $out $case
    if($case -ne 'valid') { [void][IO.Directory]::CreateDirectory($dir); Get-ChildItem -LiteralPath $valid -File | Copy-Item -Destination $dir }
    if($case -eq 'missing') { Rename-Item -LiteralPath (Join-Path $dir 'PalTimerOnline.dll') -NewName 'PalTimerOnline.dll.disabled-fixture' }
    $name=switch($case){'online-changed'{'PalTimerOnline.dll'} 'auth-changed'{'PalCompetitionAuth.dll'} 'exe-changed'{'Pal98Timer.exe'}}
    if($name){ $path=Join-Path $dir $name;[IO.File]::WriteAllBytes($path,([IO.File]::ReadAllBytes($path)+[byte[]]@(0))) }
    if($case -eq 'signature-changed' -or $case -eq 'wrong-api') {
        $path=Join-Path $dir 'PalCompetitionRegistration.public.json'
        $doc=[IO.File]::ReadAllText($path)|ConvertFrom-Json
        if($case -eq 'wrong-api'){$doc.build.online_api_version=2}else{$doc.signature_base64=[Convert]::ToBase64String([byte[]]::new(256))}
        [IO.File]::WriteAllText($path,($doc|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
    }
    Push-Location -LiteralPath $dir
    try { $expected=if($case -eq 'valid'){'valid'}else{'invalid'}; & (Join-Path $dir 'OnlineModuleLoaderBehavior.exe') $expected *> (Join-Path $out ($case+'.log')); $code=$LASTEXITCODE }
    finally { Pop-Location }
    Get-Content -LiteralPath (Join-Path $out ($case+'.log'))
    if($code){throw "Online module scenario failed: $case"}
}
Write-Output "PASS 7 real-loader scenarios; evidence: $out"
