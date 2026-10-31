param([Parameter(Mandatory=$true)][string]$TimerExe,[string]$OnlineDll,[string]$OutputPathFile,[switch]$TestPublisher)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$timer = (Resolve-Path -LiteralPath $TimerExe).Path
$hash = (Get-FileHash -LiteralPath $timer -Algorithm SHA256).Hash.ToLowerInvariant()
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($timer).FileVersion
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Timer requires an exact four-part file version' }
$sourceIdentity = (Get-FileHash -LiteralPath (Join-Path $root 'auth.cpp') -Algorithm SHA256).Hash + ':' + (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash + ':' + (Get-FileHash -LiteralPath (Join-Path $root 'publish-registration.ps1') -Algorithm SHA256).Hash + ':' + $TestPublisher.IsPresent
$onlineHash = ''; $onlineVersion = ''
if ($OnlineDll) {
    $online = (Resolve-Path -LiteralPath $OnlineDll).Path
    $onlineHash = (Get-FileHash -LiteralPath $online -Algorithm SHA256).Hash.ToLowerInvariant()
    $onlineVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($online).FileVersion
    if ($onlineVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Online DLL requires a full file version' }
} elseif (!$TestPublisher -and [Version]$version -ge [Version]'3.37.7.13') { throw 'Modern release requires matching OnlineDll' }
$sourceIdentity += ':' + $onlineHash + ':' + $onlineVersion + ':api1'
$composeHasher = [Security.Cryptography.SHA256]::Create()
try { $composition = [Convert]::ToHexString($composeHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($hash + ':' + $sourceIdentity))).ToLowerInvariant() }
finally { $composeHasher.Dispose() }
$private = Join-Path $root ('private\' + $hash + '\' + $composition)
$out = Join-Path $root ('build\' + $hash + '\' + $composition)
New-Item -ItemType Directory -Path $private,$out -Force | Out-Null
if ($OutputPathFile) { [IO.File]::WriteAllText($OutputPathFile,$out,[Text.UTF8Encoding]::new($false)) }
$frozenManifest = Join-Path $out 'registration.public.json'
$frozenDll = Join-Path $out 'PalCompetitionAuth.dll'
if ((Test-Path -LiteralPath $frozenManifest) -and (Test-Path -LiteralPath $frozenDll)) {
    $frozen = [IO.File]::ReadAllText($frozenManifest) | ConvertFrom-Json
    $sourceFile = Join-Path $out 'source.sha256'
    if (!(Test-Path -LiteralPath $sourceFile) -or [IO.File]::ReadAllText($sourceFile) -ne $sourceIdentity -or
        $frozen.exe_sha256 -ne $hash -or $frozen.component_sha256 -ne (Get-FileHash -LiteralPath $frozenDll -Algorithm SHA256).Hash.ToLowerInvariant()) {
        throw 'Existing component build is frozen or its inputs changed. Use new composition inputs; do not replace registered bytes.'
    }
    Write-Output ('Reusing frozen component: ' + $out)
    return
}
$rsa = [Security.Cryptography.RSA]::Create(2048)
try {
    $keyFile = Join-Path $private 'release-key.pk8'
    if (Test-Path -LiteralPath $keyFile) {
        $bytesRead = 0
        $rsa.ImportPkcs8PrivateKey([IO.File]::ReadAllBytes($keyFile),[ref]$bytesRead)
    } else {
        [IO.File]::WriteAllBytes($keyFile,$rsa.ExportPkcs8PrivateKey())
        [IO.File]::WriteAllText((Join-Path $private 'key-id.txt'),[Guid]::NewGuid().ToString('N'))
    }
    $keyId = [IO.File]::ReadAllText((Join-Path $private 'key-id.txt')).Trim()
    $public = $rsa.ExportSubjectPublicKeyInfoPem()
    $parameters = $rsa.ExportParameters($true)
    $stream = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($stream)
    # BCRYPT_RSAPRIVATE_BLOB: RSA2, bit length, exponent/modulus/prime sizes.
    foreach ($number in @([uint32]0x32415352,[uint32]2048,[uint32]$parameters.Exponent.Length,[uint32]$parameters.Modulus.Length,[uint32]$parameters.P.Length,[uint32]$parameters.Q.Length)) { $writer.Write($number) }
    foreach ($part in @($parameters.Exponent,$parameters.Modulus,$parameters.P,$parameters.Q)) { $writer.Write([byte[]]$part) }
    $blob = $stream.ToArray()
    $writer.Dispose(); $stream.Dispose()
    $header = '#pragma once' + "`n" +
        'static const char ReleaseExeSha256[] = "' + $hash + '";' + "`n" +
        'static const char ReleaseOnlineSha256[] = "' + $onlineHash + '";' + "`n" +
        'static const char ReleaseTimerVersion[] = "' + $version + '";' + "`n" +
        'static const char ReleaseKeyId[] = "' + $keyId + '";' + "`n" +
        'static const unsigned char ReleasePrivateBlob[] = {' + (($blob | ForEach-Object { '0x{0:x2}' -f $_ }) -join ',') + '};' + "`n"
    [IO.File]::WriteAllText((Join-Path $private 'release.generated.h'),$header,[Text.UTF8Encoding]::new($false))
    [Array]::Clear($blob,0,$blob.Length)
} finally { $rsa.Dispose() }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products '*' -version '[18.0,19.0)' -property installationPath
if (!$vs) { throw 'VS 2026 required' }
$devcmd = Join-Path $vs 'Common7\Tools\VsDevCmd.bat'
foreach ($p in @($root,$private,$out,$devcmd)) { if ($p -match '[\r\n&|^<>%!\"]') { throw 'Unsupported build path' } }
$command = '@echo off' + "`r`n" + 'call "' + $devcmd + '" -arch=x64 -host_arch=x64 >nul' + "`r`n" +
    'if errorlevel 1 exit /b 1' + "`r`n" +
    'cl /nologo /std:c++17 /EHsc /O2 /MT /W4 /D_UNICODE /DUNICODE /D_WIN32_WINNT=0x0601 /I"' + $private + '" /LD "' + (Join-Path $root 'auth.cpp') + '" /Fo"' + (Join-Path $out 'auth.obj') + '" /link /OUT:"' + (Join-Path $out 'PalCompetitionAuth.dll') + '" /IMPLIB:"' + (Join-Path $out 'PalCompetitionAuth.lib') + '" /SUBSYSTEM:WINDOWS,6.01 /DYNAMICBASE /NXCOMPAT /OPT:REF /OPT:ICF' + "`r`n" + 'exit /b %errorlevel%' + "`r`n"
$cmdFile = Join-Path $out 'build.cmd'
[IO.File]::WriteAllText($cmdFile,$command,[Text.Encoding]::ASCII)
& $env:ComSpec /d /c $cmdFile *> (Join-Path $out 'build.log')
if ($LASTEXITCODE -ne 0) { throw ('Native build failed; inspect private build log: ' + (Join-Path $out 'build.log')) }
$dll = Join-Path $out 'PalCompetitionAuth.dll'
$registration = [ordered]@{
    protocol='PAL98.TimerUploadAuth.v2'
    key_id=$keyId; exe_sha256=$hash; timer_version=$version
    component_sha256=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant()
    public_key_pem=$public
}
if ($onlineHash) {
    $registration.online_component_sha256 = $onlineHash
    $registration.online_component_version = $onlineVersion
    $registration.online_api_version = 1
}
[IO.File]::WriteAllText((Join-Path $out 'registration.public.json'),($registration | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
& (Join-Path $root 'publish-registration.ps1') -RegistrationPath (Join-Path $out 'registration.public.json') -OutputDirectory $out -TestOnly:$TestPublisher
[IO.File]::WriteAllText((Join-Path $out 'source.sha256'),$sourceIdentity,[Text.UTF8Encoding]::new($false))
[pscustomobject]$registration | Select-Object key_id,exe_sha256,timer_version,component_sha256 | ConvertTo-Json
Write-Output ('Output: ' + $out)
