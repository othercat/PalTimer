#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$TimerExe,
    [Parameter(Mandatory=$true)][string]$OnlineDll,
    [string]$OutputPathFile,
    [switch]$TestPublisher,
    [switch]$PreflightOnly
)
$ErrorActionPreference = 'Stop'
$expectedPublisher = '7e251fc559f27aa7e7493b461025d4302a75d6abb2040dfdd0cdd7aebc88e08e'
if (-not $IsWindows -or -not [Environment]::Is64BitProcess) { throw 'Use Windows x64 PowerShell 7.2 or newer.' }
$timer = (Resolve-Path -LiteralPath $TimerExe).Path
$online = (Resolve-Path -LiteralPath $OnlineDll).Path
$inputHashes = @{}
foreach ($path in @($timer,$online)) {
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($path).FileVersion
    if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Each input must have an exact four-part file version.' }
    $inputHashes[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio Installer/vswhere is missing.' }
$vs = & $vswhere -latest -products '*' -version '[18.0,19.0)' -property installationPath
if (-not $vs) { throw 'VS 2026 (18.x) is required by the original build.ps1.' }
$toolsetPath = Join-Path $vs 'VC\Auxiliary\Build\Microsoft.VCToolsVersion.default.txt'
if (-not (Test-Path -LiteralPath $toolsetPath)) { throw 'Install the VS 2026 C++ desktop workload, x64 tools and Windows SDK.' }
$toolset = [IO.File]::ReadAllText($toolsetPath).Trim()
$compiler = Join-Path $vs ('VC\Tools\MSVC\' + $toolset + '\bin\Hostx64\x64\cl.exe')
if (-not (Test-Path -LiteralPath $compiler)) { throw 'VS x64 C++ compiler missing.' }
if (-not $TestPublisher) {
    $keyPath = Join-Path $PSScriptRoot 'private\publisher\publisher-key.pk8'
    if (-not (Test-Path -LiteralPath $keyPath -PathType Leaf)) {
        throw 'FORMAL_PUBLISHER_MISSING: the existing publisher private key was intentionally excluded from this source package. Formal signing requires an explicitly authorized key transfer. No build or new publisher was created. Use -TestPublisher only for a clearly marked local test build.'
    }
    $rsa = [Security.Cryptography.RSA]::Create()
    $keyBytes = $null
    try {
        $keyBytes = [IO.File]::ReadAllBytes($keyPath)
        $read = 0
        $rsa.ImportPkcs8PrivateKey($keyBytes,[ref]$read)
        if ($read -ne $keyBytes.Length) { throw 'Unexpected extra key data.' }
        $public = $rsa.ExportSubjectPublicKeyInfoPem().Replace("`r`n","`n").TrimEnd([char]10) + "`n"
        $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::ASCII.GetBytes($public))).ToLowerInvariant()
        if ($fingerprint -cne $expectedPublisher) { throw 'Formal publisher fingerprint mismatch. Existing trust must not be replaced.' }
    } finally {
        if ($keyBytes) { [Array]::Clear($keyBytes,0,$keyBytes.Length) }
        $rsa.Dispose()
    }
}
if ($PreflightOnly) {
    Write-Output ('PREFLIGHT PASS: Windows x64, VS 2026, exact input versions; mode=' + $(if ($TestPublisher) {'TEST ONLY'} else {'FORMAL EXISTING PUBLISHER'}))
    return
}
if (-not $OutputPathFile) {
    $OutputPathFile = Join-Path $PSScriptRoot ('build-output-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8) + '.txt')
}
$OutputPathFile = [IO.Path]::GetFullPath($OutputPathFile)
if (Test-Path -LiteralPath $OutputPathFile) { throw 'OutputPathFile already exists; choose a fresh path.' }
if (-not (Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($OutputPathFile)) -PathType Container)) { throw 'OutputPathFile parent directory must already exist.' }
if ($TestPublisher) { Write-Output 'TEST ONLY: the result is not trusted by the production timer and must not be shipped as a formal companion.' }
& (Join-Path $PSScriptRoot 'build.ps1') -TimerExe $timer -OnlineDll $online -OutputPathFile $OutputPathFile -TestPublisher:$TestPublisher
$out = [IO.File]::ReadAllText($OutputPathFile).Trim()
foreach ($path in $inputHashes.Keys) {
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $inputHashes[$path]) { throw 'Input binary changed during build.' }
}
$candidate = Get-Content -LiteralPath (Join-Path $out 'PalCompetitionRegistration.public.json') -Raw | ConvertFrom-Json
$publisher = Get-Content -LiteralPath (Join-Path $out 'publisher.public.json') -Raw | ConvertFrom-Json
if ($candidate.build.exe_sha256 -cne $inputHashes[$timer] -or $candidate.build.online_component_sha256 -cne $inputHashes[$online] -or
    $candidate.build.component_sha256 -cne (Get-FileHash -LiteralPath (Join-Path $out 'PalCompetitionAuth.dll')).Hash.ToLowerInvariant()) {
    throw 'Output registration hash mismatch.'
}
if ($TestPublisher) {
    if ($publisher.display_name -cne 'fixture only' -or $publisher.publisher_id -ceq $expectedPublisher) { throw 'Test publisher identity mismatch.' }
} elseif ($publisher.publisher_id -cne $expectedPublisher -or $candidate.publisher_id -cne $expectedPublisher) {
    throw 'Formal output publisher identity mismatch.'
}
Write-Output ('BUILD PASS: input hashes unchanged; EXE/Online/Auth registration hashes match; mode=' + $(if ($TestPublisher) {'TEST ONLY'} else {'FORMAL EXISTING PUBLISHER'}))
