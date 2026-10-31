param(
    [Parameter(Mandatory=$true)][string]$RegistrationPath,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [switch]$TestOnly
)
$ErrorActionPreference = 'Stop'
# This publisher key stays on the build machine. It is NEVER embedded in the
# native component, public source, player archive or administrator material.
$publisherDirectory = Join-Path $PSScriptRoot $(if ($TestOnly) { 'private\test-publisher' } else { 'private\publisher' })
New-Item -ItemType Directory -Path $publisherDirectory -Force | Out-Null
$guard = [IO.File]::Open((Join-Path $publisherDirectory 'build.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
$rsa = [Security.Cryptography.RSA]::Create(2048)
$hasher = [Security.Cryptography.SHA256]::Create()
function Normalize-PublicPem([string]$Value) { return $Value.Replace("`r`n","`n").TrimEnd([char]10) + "`n" }
function Digest-PublicText([string]$Value) { return ([BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::ASCII.GetBytes($Value)))).Replace('-','').ToLowerInvariant() }
try {
    $keyPath = Join-Path $publisherDirectory 'publisher-key.pk8'
    if (Test-Path -LiteralPath $keyPath) {
        $read = 0
        $rsa.ImportPkcs8PrivateKey([IO.File]::ReadAllBytes($keyPath),[ref]$read)
    } else {
        [IO.File]::WriteAllBytes($keyPath,$rsa.ExportPkcs8PrivateKey())
    }
    $public = Normalize-PublicPem $rsa.ExportSubjectPublicKeyInfoPem()
    $publisherId = Digest-PublicText $public
    $build = [IO.File]::ReadAllText($RegistrationPath) | ConvertFrom-Json -AsHashtable
    if (($build.Count -ne 6 -and $build.Count -ne 9) -or $build.protocol -ne 'PAL98.TimerUploadAuth.v2') { throw 'Only exact public release registration is accepted' }
    $build.public_key_pem = Normalize-PublicPem $build.public_key_pem
    $candidateProtocol = if ($build.Count -eq 9) { 'PAL98.TimerBuildCandidate.v2' } else { 'PAL98.TimerBuildCandidate.v1' }
    $lines = @($candidateProtocol,$build.protocol,$publisherId,$build.key_id,
        $build.exe_sha256,$build.timer_version,$build.component_sha256,(Digest-PublicText $build.public_key_pem))
    if ($candidateProtocol -eq 'PAL98.TimerBuildCandidate.v2') {
        if ($build.online_component_sha256 -notmatch '^[a-f0-9]{64}$' -or $build.online_api_version -ne 1 -or $build.online_component_version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Invalid online composition' }
        $lines += @($build.online_component_sha256,$build.online_component_version,[string]$build.online_api_version)
    }
    $bytes = [Text.Encoding]::ASCII.GetBytes(($lines -join "`n") + "`n")
    $signature = $rsa.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $candidate = [ordered]@{protocol=$candidateProtocol;publisher_id=$publisherId;build=$build;signature_base64=[Convert]::ToBase64String($signature)}
    $publisher = [ordered]@{protocol='PAL98.TimerBuildPublisher.v1';publisher_id=$publisherId;display_name=$(if ($TestOnly) { 'fixture only' } else { 'othercat' });public_key_pem=$public}
    $utf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'PalCompetitionRegistration.public.json'),($candidate | ConvertTo-Json -Depth 6),$utf8)
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'publisher.public.json'),($publisher | ConvertTo-Json -Depth 3),$utf8)
} finally {
    $hasher.Dispose()
    $rsa.Dispose()
    $guard.Dispose()
}
