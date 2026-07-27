param(
    [string]$GameDirectory = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$helperPath = Join-Path $repoRoot "Pal98Timer\Pal98WaterSpiritPearlSplit.cs"
$kernel32Path = Join-Path $repoRoot "Pal98Timer\Kernel32.cs"
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
    'internal const string DaliReturnText = "糟．．希望灵儿不会有事才好";',
    'File.ReadAllBytes(sssPath)',
    'File.ReadAllBytes(messagePath)',
    'CurrentScriptStatePointerOffset = 0x500;',
    'internal const int NormalExchangeArea = 267;',
    'internal const int NormalExchangeX = 1040;',
    'internal const int NormalExchangeY = 1640;',
    'internal void ObserveGameState(int area, int x, int y, int waterSpiritPearlCount)',
    'lowWord == 0xFFFF',
    'return NormalExchangeIncreaseSeen ||',
    'if (processId == AttachedProcessId)',
    'Pal98CurrentScriptStateReader.TryRead(processHandle, baseAddress, out scriptState)'
)) {
    Assert-Contains -Text $helper -Needle $needle -Area "Pal98WaterSpiritPearlSplit.cs"
}

foreach ($entry in @(
    @{ Name = "PAL98"; Text = $package },
    @{ Name = "PAL98DX9"; Text = $dx9 }
)) {
    foreach ($needle in @(
        'private readonly Pal98WaterSpiritPearlSplit WaterSpiritPearlSplit',
        'WaterSpiritPearlSplit.CanComplete(GameObj.GetItemCount(0x109))',
        'WaterSpiritPearlSplit.Observe(',
        'GameObj.Area,',
        'GameObj.X,',
        'GameObj.Y,',
        'WaterSpiritPearlSplit.ResetRouteState();',
        'WaterSpiritPearlSplit.Attach(PID, gameDirectory);',
        'WaterSpiritPearlSplit.Detach();'
    )) {
        Assert-Contains -Text $entry.Text -Needle $needle -Area $entry.Name
    }
}

if ($unhappy.Contains('WaterSpiritPearlSplit') -or $unhappy.Contains('GetItemCount(0x109)')) {
    throw "PAL98UNHAPPY must remain outside the water-spirit-pearl split change"
}

Assert-Contains -Text $timerCore -Needle 'protected int CheckInterval = 70;' -Area "TimerCore.cs"
Assert-Contains -Text $project -Needle '<Compile Include="Pal98WaterSpiritPearlSplit.cs" />' -Area "Pal98Timer.csproj"

$observeStart = $helper.IndexOf('internal void Observe(', [StringComparison]::Ordinal)
$observeEnd = $helper.IndexOf('internal bool CanComplete', $observeStart, [StringComparison]::Ordinal)
if ($observeStart -lt 0 -or $observeEnd -lt 0) {
    throw "water-spirit-pearl hot-loop Observe method could not be located"
}
$observeBlock = $helper.Substring($observeStart, $observeEnd - $observeStart)
foreach ($forbidden in @('File.', 'Directory.', 'ReadAllBytes', 'Encoding.')) {
    if ($observeBlock.Contains($forbidden)) {
        throw "70ms Observe path contains forbidden file/resource work: $forbidden"
    }
}

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
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pal98Timer
{
    internal static class WaterPearlHarness
    {
        private sealed class Fixture
        {
            internal byte[] Sss;
            internal byte[] Messages;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static ulong ScriptState(ushort lowWord, ushort operand)
        {
            return (ulong)lowWord | ((ulong)operand << 16);
        }

        private static byte[] UInt32Bytes(uint value)
        {
            return BitConverter.GetBytes(value);
        }

        private static Fixture BuildFixture(string[] dialogues, ushort[] scriptDialogueIds)
        {
            Encoding gbk = Encoding.GetEncoding(936);
            List<byte> messageBytes = new List<byte>();
            List<uint> dialogueOffsets = new List<uint>();
            dialogueOffsets.Add(0);
            foreach (string dialogue in dialogues)
            {
                messageBytes.AddRange(gbk.GetBytes(dialogue));
                dialogueOffsets.Add((uint)messageBytes.Count);
            }

            List<byte> dialogueOffsetRecord = new List<byte>();
            foreach (uint offset in dialogueOffsets)
            {
                dialogueOffsetRecord.AddRange(UInt32Bytes(offset));
            }

            List<byte> scriptRecord = new List<byte>();
            foreach (ushort dialogueId in scriptDialogueIds)
            {
                scriptRecord.AddRange(BitConverter.GetBytes((ushort)0xFFFF));
                scriptRecord.AddRange(BitConverter.GetBytes(dialogueId));
                scriptRecord.AddRange(new byte[4]);
            }

            const uint headerLength = 24;
            uint dialogueRecordEnd = headerLength + (uint)dialogueOffsetRecord.Count;
            uint scriptRecordEnd = dialogueRecordEnd + (uint)scriptRecord.Count;
            List<byte> sss = new List<byte>();
            foreach (uint offset in new uint[]
            {
                headerLength,
                headerLength,
                headerLength,
                headerLength,
                dialogueRecordEnd,
                scriptRecordEnd
            })
            {
                sss.AddRange(UInt32Bytes(offset));
            }
            sss.AddRange(dialogueOffsetRecord);
            sss.AddRange(scriptRecord);

            return new Fixture { Sss = sss.ToArray(), Messages = messageBytes.ToArray() };
        }

        private static void AssertInvalid(Fixture fixture, string message)
        {
            bool rejected = false;
            try
            {
                Pal98DialogueMarkerResolver.Resolve(fixture.Sss, fixture.Messages);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Assert(rejected, message);
        }

        private static void ValidateRealNormalExchange(byte[] sss)
        {
            const int sceneIndex = Pal98WaterSpiritPearlGate.NormalExchangeArea - 1;
            int eventObjectStart = checked((int)BitConverter.ToUInt32(sss, 0));
            int sceneStart = checked((int)BitConverter.ToUInt32(sss, 4));
            int scriptStart = checked((int)BitConverter.ToUInt32(sss, 16));
            int scriptEnd = checked((int)BitConverter.ToUInt32(sss, 20));
            int scriptCount = (scriptEnd - scriptStart) / 8;
            int sceneOffset = checked(sceneStart + sceneIndex * 8);
            int nextSceneOffset = checked(sceneOffset + 8);
            Assert(nextSceneOffset <= sss.Length - 8, "normal exchange scene record is outside SSS.MKF");

            int firstEventIndex = BitConverter.ToUInt16(sss, sceneOffset + 6);
            int nextEventIndex = BitConverter.ToUInt16(sss, nextSceneOffset + 6);
            int pearlEventOffset = -1;
            for (int eventIndex = firstEventIndex; eventIndex < nextEventIndex; ++eventIndex)
            {
                int eventOffset = checked(eventObjectStart + eventIndex * 32);
                Assert(eventOffset >= eventObjectStart && eventOffset <= sceneStart - 32,
                    "normal exchange event object is outside SSS.MKF record 0");
                int triggerScript = BitConverter.ToUInt16(sss, eventOffset + 8);
                bool grantsWaterPearl = false;
                for (int i = 0; i < 64 && triggerScript + i < scriptCount; ++i)
                {
                    int instructionOffset = checked(scriptStart + (triggerScript + i) * 8);
                    ushort opcode = BitConverter.ToUInt16(sss, instructionOffset);
                    if (opcode == 0x001F && BitConverter.ToUInt16(sss, instructionOffset + 2) == 0x0109)
                    {
                        grantsWaterPearl = true;
                        break;
                    }
                    if (opcode == 0x0000)
                    {
                        break;
                    }
                }

                if (!grantsWaterPearl)
                {
                    continue;
                }
                Assert(pearlEventOffset < 0, "multiple scene-267 events grant item 0x0109");
                pearlEventOffset = eventOffset;
            }

            Assert(pearlEventOffset >= 0, "scene 267 has no bounded trigger path that grants item 0x0109");
            Assert(BitConverter.ToUInt16(sss, pearlEventOffset + 14) == 2,
                "water-pearl exchange event no longer uses search-normal trigger mode 2");
            int autoScript = BitConverter.ToUInt16(sss, pearlEventOffset + 10);
            Assert(autoScript >= 0 && autoScript < scriptCount, "water-pearl exchange auto script is invalid");
            int autoInstructionOffset = checked(scriptStart + autoScript * 8);
            Assert(BitConverter.ToUInt16(sss, autoInstructionOffset) == 0x0010,
                "water-pearl exchange auto script no longer begins with a walk-to instruction");
            int tileX = BitConverter.ToUInt16(sss, autoInstructionOffset + 2);
            int tileY = BitConverter.ToUInt16(sss, autoInstructionOffset + 4);
            int halfTile = BitConverter.ToUInt16(sss, autoInstructionOffset + 6);
            int worldX = tileX * 32 + halfTile * 16;
            int worldY = tileY * 16 + halfTile * 8;
            Assert(worldX == Pal98WaterSpiritPearlGate.NormalExchangeX &&
                worldY == Pal98WaterSpiritPearlGate.NormalExchangeY,
                "effective resource exchange coordinate no longer matches the timer constants");
        }

        public static int Main(string[] args)
        {
            Fixture relocated = BuildFixture(
                new string[]
                {
                    "无关对话一",
                    Pal98DialogueMarkerResolver.DaliReturnText,
                    "无关对话二",
                    "无关对话三"
                },
                new ushort[] { 3, 0, 1 });
            Pal98DialogueMarkers markers = Pal98DialogueMarkerResolver.Resolve(relocated.Sss, relocated.Messages);
            Assert(markers.DaliReturnDialogueId == 1, "Dali-return dialogue ID was not resolved dynamically");
            Assert(markers.DaliReturnScriptIndex == 2, "Dali-return script reference is wrong");

            string attachRoot = Path.Combine(Path.GetTempPath(), "paltimer-water-pearl-attach-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(attachRoot);
            try
            {
                File.WriteAllBytes(Path.Combine(attachRoot, "SSS.MKF"), relocated.Sss);
                File.WriteAllBytes(Path.Combine(attachRoot, "M.MSG"), relocated.Messages);
                Pal98WaterSpiritPearlSplit split = new Pal98WaterSpiritPearlSplit();
                split.Attach(123, attachRoot);
                Assert(split.ResourcesResolved, "first process attach did not resolve resources");
                File.Delete(Path.Combine(attachRoot, "SSS.MKF"));
                File.Delete(Path.Combine(attachRoot, "M.MSG"));
                split.Attach(123, attachRoot);
                Assert(split.ResourcesResolved, "same-process attach re-read resources instead of using the cache");
                split.Attach(124, attachRoot);
                Assert(!split.ResourcesResolved, "new-process attach reused the previous process resource cache");
                split.Observe(IntPtr.Zero, 0, Pal98WaterSpiritPearlGate.NormalExchangeArea,
                    Pal98WaterSpiritPearlGate.NormalExchangeX,
                    Pal98WaterSpiritPearlGate.NormalExchangeY, 0);
                split.Observe(IntPtr.Zero, 0, Pal98WaterSpiritPearlGate.NormalExchangeArea,
                    Pal98WaterSpiritPearlGate.NormalExchangeX,
                    Pal98WaterSpiritPearlGate.NormalExchangeY, 1);
                Assert(split.CanComplete(1), "normal coordinate route was disabled by missing Dali resources");
            }
            finally
            {
                if (Directory.Exists(attachRoot)) Directory.Delete(attachRoot, true);
            }

            Pal98WaterSpiritPearlGate normalZeroToOneGate = new Pal98WaterSpiritPearlGate(markers);
            normalZeroToOneGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.NormalExchangeArea,
                Pal98WaterSpiritPearlGate.NormalExchangeX,
                Pal98WaterSpiritPearlGate.NormalExchangeY, 0);
            Assert(!normalZeroToOneGate.CanComplete(0), "region entry completed before an inventory increase");
            normalZeroToOneGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.NormalExchangeArea,
                Pal98WaterSpiritPearlGate.NormalExchangeX,
                Pal98WaterSpiritPearlGate.NormalExchangeY, 1);
            Assert(normalZeroToOneGate.CanComplete(1), "normal 0-to-1 exchange did not complete the split");

            Pal98WaterSpiritPearlGate priorGrantThenExchangeGate = new Pal98WaterSpiritPearlGate(markers);
            const int PriorRandomOrSpecialCount = 3;
            priorGrantThenExchangeGate.ObserveGameState(1, 0, 0, 0);
            priorGrantThenExchangeGate.ObserveGameState(1, 0, 0, PriorRandomOrSpecialCount);
            Assert(!priorGrantThenExchangeGate.CanComplete(PriorRandomOrSpecialCount),
                "an arbitrary pre-exchange random/special count completed the split");
            priorGrantThenExchangeGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.NormalExchangeArea,
                Pal98WaterSpiritPearlGate.NormalExchangeX - Pal98WaterSpiritPearlGate.NormalExchangeXRadius,
                Pal98WaterSpiritPearlGate.NormalExchangeY - Pal98WaterSpiritPearlGate.NormalExchangeYRadius,
                PriorRandomOrSpecialCount);
            Assert(!priorGrantThenExchangeGate.CanComplete(PriorRandomOrSpecialCount),
                "the arbitrary pre-exchange count completed when captured as the region baseline");
            priorGrantThenExchangeGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.NormalExchangeArea,
                Pal98WaterSpiritPearlGate.NormalExchangeX + Pal98WaterSpiritPearlGate.NormalExchangeXRadius,
                Pal98WaterSpiritPearlGate.NormalExchangeY + Pal98WaterSpiritPearlGate.NormalExchangeYRadius,
                PriorRandomOrSpecialCount + 1);
            Assert(priorGrantThenExchangeGate.CanComplete(PriorRandomOrSpecialCount + 1),
                "the later normal N-to-N+1 exchange did not complete the split");
            priorGrantThenExchangeGate.Reset();
            Assert(!priorGrantThenExchangeGate.CanComplete(PriorRandomOrSpecialCount + 1),
                "route reset retained the coordinate latch");

            Pal98WaterSpiritPearlGate reentryGate = new Pal98WaterSpiritPearlGate(markers);
            reentryGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.NormalExchangeArea,
                Pal98WaterSpiritPearlGate.NormalExchangeX,
                Pal98WaterSpiritPearlGate.NormalExchangeY, 0);
            reentryGate.ObserveGameState(1, 0, 0, 1);
            reentryGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.NormalExchangeArea,
                Pal98WaterSpiritPearlGate.NormalExchangeX,
                Pal98WaterSpiritPearlGate.NormalExchangeY, 1);
            Assert(!reentryGate.CanComplete(1), "an out-of-region item gain survived region re-entry as a false split");
            reentryGate.ObserveGameState(
                Pal98WaterSpiritPearlGate.NormalExchangeArea,
                Pal98WaterSpiritPearlGate.NormalExchangeX,
                Pal98WaterSpiritPearlGate.NormalExchangeY, 2);
            Assert(reentryGate.CanComplete(2), "a later in-region increase after re-entry did not complete");

            Pal98WaterSpiritPearlGate skippedRouteGate = new Pal98WaterSpiritPearlGate(markers);
            const int PearlCountBeforeDreamlessTrace = 3;
            skippedRouteGate.ObserveGameState(1, 0, 0, PearlCountBeforeDreamlessTrace);
            skippedRouteGate.ObserveGameState(1, 0, 0, PearlCountBeforeDreamlessTrace + 1);
            Assert(!skippedRouteGate.CanComplete(PearlCountBeforeDreamlessTrace + 1),
                "the 回梦无痕 N-to-N+1 grant completed before the Dali-return dialogue");
            skippedRouteGate.ObserveScriptState(ScriptState(0xFFFF, markers.DaliReturnDialogueId));
            Assert(!skippedRouteGate.CanComplete(PearlCountBeforeDreamlessTrace + 1),
                "the split completed inside the Dali-return dialogue");
            skippedRouteGate.ObserveScriptState(ScriptState(0x0000, 0x0000));
            Assert(skippedRouteGate.CanComplete(PearlCountBeforeDreamlessTrace + 1),
                "回梦无痕 N-to-N+1 plus the exited Dali-return dialogue did not complete the split");

            Pal98WaterSpiritPearlGate missingItemGate = new Pal98WaterSpiritPearlGate(markers);
            missingItemGate.ObserveScriptState(ScriptState(0xFFFF, markers.DaliReturnDialogueId));
            missingItemGate.ObserveScriptState(ScriptState(0x0000, 0x0000));
            Assert(!missingItemGate.CanComplete(0), "Dali-return dialogue completed without a water pearl");

            Pal98WaterSpiritPearlGate unrelatedGate = new Pal98WaterSpiritPearlGate(markers);
            unrelatedGate.ObserveScriptState(ScriptState(0xFFFF, 0));
            unrelatedGate.ObserveScriptState(ScriptState(0x0000, 0));
            Assert(!unrelatedGate.CanComplete(1), "an unrelated dialogue armed the split");

            Fixture duplicateDialogue = BuildFixture(
                new string[]
                {
                    Pal98DialogueMarkerResolver.DaliReturnText,
                    Pal98DialogueMarkerResolver.DaliReturnText
                },
                new ushort[] { 0 });
            AssertInvalid(duplicateDialogue, "duplicate dialogue text was accepted");

            Fixture duplicateScript = BuildFixture(
                new string[]
                {
                    Pal98DialogueMarkerResolver.DaliReturnText
                },
                new ushort[] { 0, 0 });
            AssertInvalid(duplicateScript, "duplicate script reference was accepted");

            Fixture malformed = BuildFixture(
                new string[]
                {
                    Pal98DialogueMarkerResolver.DaliReturnText
                },
                new ushort[] { 0 });
            malformed.Sss[0] = 0;
            malformed.Sss[1] = 0;
            malformed.Sss[2] = 0;
            malformed.Sss[3] = 0;
            AssertInvalid(malformed, "malformed MKF header was accepted");

            if (args.Length == 2)
            {
                byte[] realSss = File.ReadAllBytes(args[0]);
                ValidateRealNormalExchange(realSss);
                Pal98DialogueMarkers realMarkers = Pal98DialogueMarkerResolver.Resolve(
                    realSss,
                    File.ReadAllBytes(args[1]));
                Console.WriteLine(
                    "REAL: normal=area {0}/center ({1},{2})/radius ({3},{4}), Dali=0x{5:X4}/script=0x{6:X4}",
                    Pal98WaterSpiritPearlGate.NormalExchangeArea,
                    Pal98WaterSpiritPearlGate.NormalExchangeX,
                    Pal98WaterSpiritPearlGate.NormalExchangeY,
                    Pal98WaterSpiritPearlGate.NormalExchangeXRadius,
                    Pal98WaterSpiritPearlGate.NormalExchangeYRadius,
                    realMarkers.DaliReturnDialogueId,
                    realMarkers.DaliReturnScriptIndex);
            }

            Console.WriteLine("PASS: dynamic Dali resolution, malformed/duplicate rejection, arbitrary pre-exchange N guard, normal 0-to-1 and N-to-N+1 exchanges, 回梦无痕 N-to-N+1 plus Dali fallback, re-entry reset, route reset, and unrelated-dialogue guard.");
            return 0;
        }
    }
}
'@ | Set-Content -LiteralPath $harnessPath -Encoding UTF8

    & $cscPath /nologo /codepage:65001 /target:exe /out:$exePath $kernel32Path $helperPath $harnessPath
    if ($LASTEXITCODE -ne 0) {
        throw "water-spirit-pearl harness compilation failed with exit code $LASTEXITCODE"
    }

    $harnessArguments = @()
    if (-not [string]::IsNullOrWhiteSpace($GameDirectory)) {
        $sssPath = Join-Path $GameDirectory "SSS.MKF"
        $messagePath = Join-Path $GameDirectory "M.MSG"
        if (-not (Test-Path -LiteralPath $sssPath) -or -not (Test-Path -LiteralPath $messagePath)) {
            throw "GameDirectory does not contain SSS.MKF and M.MSG: $GameDirectory"
        }
        $harnessArguments = @($sssPath, $messagePath)
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

Write-Host "PASS: water-spirit-pearl source wiring keeps resource I/O out of the 70ms Observe path and leaves PAL98UNHAPPY unchanged."
