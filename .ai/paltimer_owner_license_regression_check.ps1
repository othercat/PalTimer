$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$licensePath = Join-Path $repoRoot 'LICENSE'
$readmePath = Join-Path $repoRoot 'README.md'
$aboutPath = Join-Path $repoRoot 'Pal98Timer\AboutForm.cs'
$profileDocPath = Join-Path $repoRoot 'docs\PAL98DX9_PROFILE_CORES.md'

foreach ($path in @($licensePath, $readmePath, $aboutPath, $profileDocPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required PalTimer owner/license file is missing: $path"
    }
}

$license = Get-Content -LiteralPath $licensePath -Raw
$readme = Get-Content -LiteralPath $readmePath -Raw
$about = Get-Content -LiteralPath $aboutPath -Raw
$profileDoc = Get-Content -LiteralPath $profileDocPath -Raw

if (-not $license.Contains('GNU GENERAL PUBLIC LICENSE') -or
    -not $license.Contains('Version 2, June 1991')) {
    throw 'LICENSE is not the GNU GPL version 2 text.'
}
if (-not $readme.Contains('Repository Owner and current maintainer: `othercat`') -or
    -not $readme.Contains('GPL-2.0-only')) {
    throw 'README does not declare othercat ownership/maintenance and GPL-2.0-only.'
}
if (-not $about.Contains('https://github.com/othercat/PalTimer') -or
    -not $about.Contains('GNU GPL v2 only')) {
    throw 'About dialog does not expose the canonical othercat repository and GPLv2 identity.'
}
if (-not $profileDoc.Contains('仓库 Owner/当前维护者为 `othercat`') -or
    -not $profileDoc.Contains('GPL-2.0-only')) {
    throw 'Profile core documentation does not carry the corrected owner/license classification.'
}
if ($readme.Contains('本仓库尚无明确 `LICENSE`') -or
    $profileDoc.Contains('还需仓库 Owner 明确 PalTimer 的许可证')) {
    throw 'An obsolete publication-license blocker remains in current PalTimer documentation.'
}

Write-Output 'PASS: PalTimer canonical owner is othercat and repository license is GPL-2.0-only'
