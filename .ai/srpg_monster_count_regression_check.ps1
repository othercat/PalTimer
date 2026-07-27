$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$kernelPaths = @(
    @{ Name = "PAL98"; Path = Join-Path $repoRoot "Pal98Timer\仙剑98柔情.cs" },
    @{ Name = "PAL98DX9"; Path = Join-Path $repoRoot "Pal98Timer\仙剑98柔情DX9.cs" },
    @{ Name = "PAL98UNHAPPY"; Path = Join-Path $repoRoot "Pal98Timer\仙剑98柔情不欢乐模式.cs" }
)

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Needle,
        [Parameter(Mandatory = $true)][string]$Area
    )

    if (-not $Text.Contains($Needle)) {
        throw "$Area is missing monster-count snapshot marker: $Needle"
    }
}

foreach ($kernel in $kernelPaths) {
    $text = Get-Content -LiteralPath $kernel.Path -Raw -Encoding UTF8
    Assert-Contains -Text $text -Needle 'exdata["TotalMonsterCount"] = TotalMonsterCount;' -Area $kernel.Name

    $loadStart = $text.IndexOf('private void LoadGame(string fn = "SRPG.bin", string rn = "1.RPG")', [StringComparison]::Ordinal)
    $timerStart = $text.IndexOf('public void SetTimerFromString(string json)', $loadStart, [StringComparison]::Ordinal)
    $timerEnd = $text.IndexOf('private string GetTimeName()', $timerStart, [StringComparison]::Ordinal)
    if ($loadStart -lt 0 -or $timerStart -lt 0 -or $timerEnd -lt 0) {
        throw "$($kernel.Name) SRPG load/timer blocks could not be located"
    }

    $loadBlock = $text.Substring($loadStart, $timerStart - $loadStart)
    Assert-Contains -Text $loadBlock -Needle 'SetTimerFromString(so.TimerStr);' -Area "$($kernel.Name) LoadGame"
    if ($loadBlock.Contains('savedMonsterCount')) {
        throw "$($kernel.Name) still overrides the SRPG monster-count snapshot with the local value"
    }

    $timerBlock = $text.Substring($timerStart, $timerEnd - $timerStart)
    $presenceCheck = 'if (ho.ToDic().ContainsKey("TotalMonsterCount"))'
    $restore = 'TotalMonsterCount = ho.GetValue<int>("TotalMonsterCount");'
    Assert-Contains -Text $timerBlock -Needle $presenceCheck -Area "$($kernel.Name) SetTimerFromString"
    Assert-Contains -Text $timerBlock -Needle $restore -Area "$($kernel.Name) SetTimerFromString"
    if ($timerBlock.IndexOf($presenceCheck, [StringComparison]::Ordinal) -gt
        $timerBlock.IndexOf($restore, [StringComparison]::Ordinal)) {
        throw "$($kernel.Name) restores the monster count before checking old-SRPG field presence"
    }
}

$hobjPath = Join-Path $repoRoot "Pal98Timer\HObj.cs"
$cscPath = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $cscPath)) {
    throw "C# compiler not found: $cscPath"
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("paltimer-srpg-monster-test-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
$harnessPath = Join-Path $tempRoot "MonsterCountHarness.cs"
$exePath = Join-Path $tempRoot "MonsterCountHarness.exe"

try {
    @'
using System;
using HFrame.ENT;

internal static class MonsterCountHarness
{
    private static int Restore(string json, int localValue)
    {
        HObj data = new HObj(json);
        if (data.ToDic().ContainsKey("TotalMonsterCount"))
        {
            localValue = data.GetValue<int>("TotalMonsterCount");
        }
        return localValue;
    }

    private static int Main()
    {
        if (Restore("{\"TotalMonsterCount\":12}", 3) != 12) return 1;
        if (Restore("{\"TotalMonsterCount\":0}", 7) != 0) return 2;
        if (Restore("{}", 9) != 9) return 3;
        Console.WriteLine("PASS: new SRPG restores TotalMonsterCount and old SRPG preserves the local value.");
        return 0;
    }
}
'@ | Set-Content -LiteralPath $harnessPath -Encoding UTF8

    & $cscPath /nologo /codepage:65001 /target:exe /out:$exePath /reference:System.Data.dll $hobjPath $harnessPath
    if ($LASTEXITCODE -ne 0) { throw "monster-count compatibility harness compilation failed" }
    & $exePath
    if ($LASTEXITCODE -ne 0) { throw "monster-count compatibility harness failed" }
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath($tempRoot)
    $systemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTemp.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTemp)) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}
