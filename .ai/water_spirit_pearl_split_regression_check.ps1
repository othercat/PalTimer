param(
    [string]$GameDirectory = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$helperPath = Join-Path $repoRoot "Pal98Timer\Pal98WaterSpiritPearlSplit.cs"
$packagePath = Join-Path $repoRoot "Pal98Timer\仙剑98柔情.cs"
$dx9Path = Join-Path $repoRoot "Pal98Timer\仙剑98柔情DX9.cs"
$unhappyPath = Join-Path $repoRoot "Pal98Timer\仙剑98柔情不欢乐模式.cs"
$timerCorePath = Join-Path $repoRoot "Pal98Timer\TimerCore.cs"
$projectPath = Join-Path $repoRoot "Pal98Timer\Pal98Timer.csproj"

$helper = Get-Content -LiteralPath $helperPath -Raw -Encoding UTF8
$package = Get-Content -LiteralPath $packagePath -Raw -Encoding UTF8
$dx9 = Get-Content -LiteralPath $dx9Path -Raw -Encoding UTF8
$unhappy = Get-Content -LiteralPath $unhappyPath -Raw -Encoding UTF8
$timerCore = Get-Content -LiteralPath $timerCorePath -Raw -Encoding UTF8
$project = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Needle,
        [Parameter(Mandatory = $true)][string]$Area
    )

    if (-not $Text.Contains($Needle)) {
        throw "$Area is missing water-spirit-pearl marker: $Needle"
    }
}

foreach ($needle in @(
    'internal const int NormalExchangeArea = 267;',
    'internal const int NormalExchangeObjectX = 1088;',
    'internal const int NormalExchangeObjectY = 1616;',
    'internal const int NormalExchangeTriggerMode = 2;',
    'internal const int DaliReturnArea = 204;',
    'internal const int DaliReturnX = 1168;',
    'internal const int DaliReturnY = 760;',
    'internal const int DaliReturnRequiredCount = 1;',
    'IsManualTriggerPosition(',
    'waterSpiritPearlCount > NormalExchangeBaselineCount',
    'waterSpiritPearlCount >= DaliReturnRequiredCount'
)) {
    Assert-Contains -Text $helper -Needle $needle -Area "Pal98WaterSpiritPearlSplit.cs"
}

foreach ($forbidden in @(
    'Pal98Dialogue',
    'CurrentScript',
    'ReadProcessMemory',
    'File.',
    'Encoding.',
    'ResourcesResolved',
    'ResolutionError',
    'internal void Attach('
)) {
    if ($helper.Contains($forbidden)) {
        throw "Pal98WaterSpiritPearlSplit.cs still contains removed script/resource dependency: $forbidden"
    }
}

foreach ($entry in @(
    @{ Name = "PAL98"; Text = $package },
    @{ Name = "PAL98DX9"; Text = $dx9 }
)) {
    foreach ($needle in @(
        'private readonly Pal98WaterSpiritPearlSplit WaterSpiritPearlSplit',
        'WaterSpiritPearlSplit.CanComplete()',
        'WaterSpiritPearlSplit.Observe(',
        'GameObj.Area,',
        'GameObj.rX,',
        'GameObj.rY,',
        'GameObj.PartyDirection,',
        'GameObj.GetItemCount(0x109));',
        'WaterSpiritPearlSplit.ResetRouteState();',
        'WaterSpiritPearlSplit.Detach();'
    )) {
        Assert-Contains -Text $entry.Text -Needle $needle -Area $entry.Name
    }
    foreach ($forbidden in @('AttachWaterSpiritPearlSplit', 'WaterSpiritPearlSplit.Attach(')) {
        if ($entry.Text.Contains($forbidden)) {
            throw "$($entry.Name) still contains removed resource-attach path: $forbidden"
        }
    }
}

foreach ($needle in @(
    'public const int PartyDirectionOffset = 0x26E;',
    'public const int rXOffset = 0x274;',
    'public const int rYOffset = 0x276;',
    'PartyDirection = Readm<short>(this.handle, BaseAddr + PartyDirectionOffset);',
    'rX = Readm<short>(this.handle, BaseAddr + rXOffset);',
    'rY = Readm<short>(this.handle, BaseAddr + rYOffset);'
)) {
    Assert-Contains -Text $package -Needle $needle -Area "PAL98 shared GameObject"
}

if ($unhappy.Contains('WaterSpiritPearlSplit') -or $unhappy.Contains('GetItemCount(0x109)')) {
    throw "PAL98UNHAPPY must remain outside the water-spirit-pearl split change"
}

Assert-Contains -Text $timerCore -Needle 'protected int CheckInterval = 70;' -Area "TimerCore.cs"
Assert-Contains -Text $project -Needle '<Compile Include="Pal98WaterSpiritPearlSplit.cs" />' -Area "Pal98Timer.csproj"

$frameworkRoot = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$cscPath = Join-Path $frameworkRoot "csc.exe"
if (-not (Test-Path -LiteralPath $cscPath)) {
    throw "C# compiler not found: $cscPath"
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("paltimer-water-pearl-test-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
$harnessPath = Join-Path $tempRoot "WaterPearlHarness.cs"
$exePath = Join-Path $tempRoot "WaterPearlHarness.exe"

try {
    @'
using System;
using System.IO;

namespace Pal98Timer
{
    internal static class WaterPearlHarness
    {
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void AssertNormalTriggerPoint(
            int partyX,
            int partyY,
            int partyDirection,
            string pointName)
        {
            Pal98WaterSpiritPearlGate gate = new Pal98WaterSpiritPearlGate();
            gate.ObserveGameState(
                Pal98WaterSpiritPearlGate.NormalExchangeArea,
                partyX,
                partyY,
                partyDirection,
                3);
            Assert(!gate.CanComplete(), pointName + " completed before the item-count increase");
            gate.ObserveGameState(
                Pal98WaterSpiritPearlGate.NormalExchangeArea,
                partyX,
                partyY,
                partyDirection,
                4);
            Assert(gate.CanComplete(), pointName + " did not accept N-to-N+1");
        }

        private static int ReadUInt32(byte[] data, int offset)
        {
            return checked((int)BitConverter.ToUInt32(data, offset));
        }

        private static int ReadUInt16(byte[] data, int offset)
        {
            return BitConverter.ToUInt16(data, offset);
        }

        private static void ValidateRealPositionEvidence(byte[] sss)
        {
            int eventObjectStart = ReadUInt32(sss, 0);
            int sceneStart = ReadUInt32(sss, 4);
            int scriptStart = ReadUInt32(sss, 16);
            int scriptEnd = ReadUInt32(sss, 20);
            int scriptCount = (scriptEnd - scriptStart) / 8;

            int normalSceneOffset = checked(sceneStart + (Pal98WaterSpiritPearlGate.NormalExchangeArea - 1) * 8);
            int firstEventIndex = ReadUInt16(sss, normalSceneOffset + 6);
            int nextEventIndex = ReadUInt16(sss, normalSceneOffset + 14);
            int pearlEventOffset = -1;
            for (int eventIndex = firstEventIndex; eventIndex < nextEventIndex; ++eventIndex)
            {
                int eventOffset = checked(eventObjectStart + eventIndex * 32);
                int triggerScript = ReadUInt16(sss, eventOffset + 8);
                bool grantsWaterPearl = false;
                for (int i = 0; i < 64 && triggerScript + i < scriptCount; ++i)
                {
                    int instructionOffset = checked(scriptStart + (triggerScript + i) * 8);
                    int opcode = ReadUInt16(sss, instructionOffset);
                    if (opcode == 0x001F && ReadUInt16(sss, instructionOffset + 2) == 0x0109)
                    {
                        grantsWaterPearl = true;
                        break;
                    }
                    if (opcode == 0x0000) break;
                }

                if (!grantsWaterPearl) continue;
                Assert(pearlEventOffset < 0, "multiple scene-267 events grant item 0x0109");
                pearlEventOffset = eventOffset;
            }

            Assert(pearlEventOffset >= 0, "scene 267 has no bounded trigger path that grants item 0x0109");
            Assert(ReadUInt16(sss, pearlEventOffset + 8) == 0x8861,
                "normal exchange event trigger script is no longer 0x8861");
            Assert(ReadUInt16(sss, pearlEventOffset + 10) == 0x87DE,
                "normal exchange event initial auto script is no longer 0x87DE");
            Assert(ReadUInt16(sss, pearlEventOffset + 14) ==
                Pal98WaterSpiritPearlGate.NormalExchangeTriggerMode,
                "normal exchange event trigger mode no longer matches timer constants");
            Assert(ReadUInt16(sss, pearlEventOffset + 16) == 535,
                "normal exchange event sprite is no longer child Li");

            const int normalFinalPositionScript = 0x87E8;
            int normalFinalPositionOffset = checked(scriptStart + normalFinalPositionScript * 8);
            Assert(ReadUInt16(sss, normalFinalPositionOffset) == 0x0010,
                "normal exchange final auto script no longer walks child Li into position");
            int normalHalfTile = ReadUInt16(sss, normalFinalPositionOffset + 6);
            int normalX = ReadUInt16(sss, normalFinalPositionOffset + 2) * 32 + normalHalfTile * 16;
            int normalY = ReadUInt16(sss, normalFinalPositionOffset + 4) * 16 + normalHalfTile * 8;
            Assert(normalX == Pal98WaterSpiritPearlGate.NormalExchangeObjectX &&
                normalY == Pal98WaterSpiritPearlGate.NormalExchangeObjectY,
                "effective child-Li exchange coordinate no longer matches timer constants");

            const int daliPartyPositionScript = 0x76F6;
            Assert(daliPartyPositionScript < scriptCount, "Dali return party-position script is outside SSS.MKF");
            int daliOffset = checked(scriptStart + daliPartyPositionScript * 8);
            Assert(ReadUInt16(sss, daliOffset) == 0x0046,
                "Dali return script no longer sets the party position at 0x76F6");
            int daliHalfTile = ReadUInt16(sss, daliOffset + 6);
            int daliX = ReadUInt16(sss, daliOffset + 2) * 32 + daliHalfTile * 16;
            int daliY = ReadUInt16(sss, daliOffset + 4) * 16 + daliHalfTile * 8;
            Assert(Pal98WaterSpiritPearlGate.DaliReturnArea == 0x00CC &&
                daliX == Pal98WaterSpiritPearlGate.DaliReturnX &&
                daliY == Pal98WaterSpiritPearlGate.DaliReturnY,
                "effective Dali return coordinate no longer matches timer constants");
        }

        public static int Main(string[] args)
        {
            const int N = 3;

            Pal98WaterSpiritPearlGate earlyCountGate = new Pal98WaterSpiritPearlGate();
            earlyCountGate.ObserveGameState(1, 0, 0, 0, 0);
            earlyCountGate.ObserveGameState(1, 0, 0, 0, N);
            Assert(!earlyCountGate.CanComplete(), "an arbitrary pre-ten-years count completed the split");

            AssertNormalTriggerPoint(1104, 1608, 0, "south-facing first trigger point");
            AssertNormalTriggerPoint(1104, 1624, 1, "west-facing first trigger point");
            AssertNormalTriggerPoint(1072, 1624, 2, "live north-facing trigger point");
            AssertNormalTriggerPoint(1072, 1608, 3, "east-facing first trigger point");
            AssertNormalTriggerPoint(1056, 1648, 2, "trigger-mode-2 farthest accepted point");
            AssertNormalTriggerPoint(1072, 1631, 2, "PAL map-cell equivalent trigger point");

            Pal98WaterSpiritPearlGate normalOutsideGate = new Pal98WaterSpiritPearlGate();
            normalOutsideGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.NormalExchangeArea,
                1024,
                1632,
                2,
                N);
            normalOutsideGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.NormalExchangeArea,
                1024,
                1632,
                2,
                N + 1);
            Assert(!normalOutsideGate.CanComplete(), "trigger mode 2 accepted search point index 9");

            Pal98WaterSpiritPearlGate oldViewportGate = new Pal98WaterSpiritPearlGate();
            oldViewportGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.NormalExchangeArea,
                912,
                1512,
                2,
                N);
            oldViewportGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.NormalExchangeArea,
                912,
                1512,
                2,
                N + 1);
            Assert(!oldViewportGate.CanComplete(), "old viewport coordinates completed the normal split");

            Pal98WaterSpiritPearlGate reentryGate = new Pal98WaterSpiritPearlGate();
            reentryGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.NormalExchangeArea,
                1072,
                1624,
                2,
                0);
            reentryGate.ObserveGameState(1, 0, 0, 0, N);
            reentryGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.NormalExchangeArea,
                1072,
                1624,
                2,
                N);
            Assert(!reentryGate.CanComplete(), "out-of-range gain survived trigger-range re-entry");
            reentryGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.NormalExchangeArea,
                1072,
                1624,
                2,
                N + 1);
            Assert(reentryGate.CanComplete(), "re-entry baseline did not accept its later increase");

            Pal98WaterSpiritPearlGate daliGate = new Pal98WaterSpiritPearlGate();
            daliGate.ObserveGameState(1, 0, 0, 0, N + 1);
            Assert(!daliGate.CanComplete(), "回梦无痕 item count completed before reaching Dali");
            daliGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.DaliReturnArea,
                Pal98WaterSpiritPearlGate.DaliReturnX,
                Pal98WaterSpiritPearlGate.DaliReturnY,
                0,
                N + 1);
            Assert(daliGate.CanComplete(), "fixed Dali return position plus a present pearl did not complete");

            Pal98WaterSpiritPearlGate daliMissingItemGate = new Pal98WaterSpiritPearlGate();
            daliMissingItemGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.DaliReturnArea,
                Pal98WaterSpiritPearlGate.DaliReturnX,
                Pal98WaterSpiritPearlGate.DaliReturnY,
                0,
                Pal98WaterSpiritPearlGate.DaliReturnRequiredCount - 1);
            Assert(!daliMissingItemGate.CanComplete(), "Dali return position completed without a pearl");

            Pal98WaterSpiritPearlGate daliRestartGate = new Pal98WaterSpiritPearlGate();
            daliRestartGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.DaliReturnArea,
                Pal98WaterSpiritPearlGate.DaliReturnX,
                Pal98WaterSpiritPearlGate.DaliReturnY,
                0,
                Pal98WaterSpiritPearlGate.DaliReturnRequiredCount);
            Assert(daliRestartGate.CanComplete(), "restart-normalized count 1 did not complete at fixed Dali position");

            Pal98WaterSpiritPearlGate daliOutsideGate = new Pal98WaterSpiritPearlGate();
            daliOutsideGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.DaliReturnArea,
                Pal98WaterSpiritPearlGate.DaliReturnX + 1,
                Pal98WaterSpiritPearlGate.DaliReturnY,
                0,
                Pal98WaterSpiritPearlGate.DaliReturnRequiredCount);
            Assert(!daliOutsideGate.CanComplete(), "Dali return gate accepted an adjacent actual-map position");
            daliOutsideGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.DaliReturnArea + 1,
                Pal98WaterSpiritPearlGate.DaliReturnX,
                Pal98WaterSpiritPearlGate.DaliReturnY,
                0,
                Pal98WaterSpiritPearlGate.DaliReturnRequiredCount);
            Assert(!daliOutsideGate.CanComplete(), "Dali return coordinates completed in the wrong scene");

            Pal98WaterSpiritPearlGate daliViewportGate = new Pal98WaterSpiritPearlGate();
            daliViewportGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.DaliReturnArea,
                Pal98WaterSpiritPearlGate.DaliReturnX - 160,
                Pal98WaterSpiritPearlGate.DaliReturnY - 112,
                0,
                Pal98WaterSpiritPearlGate.DaliReturnRequiredCount);
            Assert(!daliViewportGate.CanComplete(), "viewport-relative Dali coordinates completed the split");

            daliGate.Reset();
            Assert(!daliGate.CanComplete(), "route reset retained the Dali position latch");

            if (args.Length == 1)
            {
                ValidateRealPositionEvidence(File.ReadAllBytes(args[0]));
                Console.WriteLine(
                    "REAL: normal=area {0}/child ({1},{2})/trigger mode {3}, Dali=area {4}/fixed ({5},{6})/count >= {7}",
                    Pal98WaterSpiritPearlGate.NormalExchangeArea,
                    Pal98WaterSpiritPearlGate.NormalExchangeObjectX,
                    Pal98WaterSpiritPearlGate.NormalExchangeObjectY,
                    Pal98WaterSpiritPearlGate.NormalExchangeTriggerMode,
                    Pal98WaterSpiritPearlGate.DaliReturnArea,
                    Pal98WaterSpiritPearlGate.DaliReturnX,
                    Pal98WaterSpiritPearlGate.DaliReturnY,
                    Pal98WaterSpiritPearlGate.DaliReturnRequiredCount);
            }

            Console.WriteLine("PASS: arbitrary N, child-Li manual trigger points, trigger-mode-2 boundary, re-entry baseline, fixed Dali actual coordinate with restart-normalized count 1, reset, and zero script/resource runtime dependency.");
            return 0;
        }
    }
}
'@ | Set-Content -LiteralPath $harnessPath -Encoding UTF8

    & $cscPath /nologo /codepage:65001 /target:exe /out:$exePath $helperPath $harnessPath
    if ($LASTEXITCODE -ne 0) {
        throw "water-spirit-pearl harness compilation failed with exit code $LASTEXITCODE"
    }

    $harnessArguments = @()
    if (-not [string]::IsNullOrWhiteSpace($GameDirectory)) {
        $sssPath = Join-Path $GameDirectory "SSS.MKF"
        if (-not (Test-Path -LiteralPath $sssPath)) {
            throw "GameDirectory does not contain SSS.MKF: $GameDirectory"
        }
        $harnessArguments = @($sssPath)
    }

    & $exePath @harnessArguments
    if ($LASTEXITCODE -ne 0) {
        throw "water-spirit-pearl harness failed with exit code $LASTEXITCODE"
    }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

Write-Host "PASS: water-spirit-pearl source wiring uses only existing 70ms scene/actual-coordinate/direction/inventory snapshots and leaves PAL98UNHAPPY unchanged."
