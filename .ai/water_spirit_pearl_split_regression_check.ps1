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
    'internal const string CanonicalWaterPearlText = "得到水灵珠";',
    'internal const string DaliReturnText = "糟．．希望灵儿不会有事才好";',
    'File.ReadAllBytes(sssPath)',
    'File.ReadAllBytes(messagePath)',
    'CurrentScriptStatePointerOffset = 0x500;',
    'lowWord == 0xFFFF',
    'return MarkerExited && waterSpiritPearlCount > 0;',
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
        'WaterSpiritPearlSplit.Observe(PalHandle, GameObj.BaseAddr);',
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

$observeStart = $helper.IndexOf('internal void Observe(IntPtr processHandle, int baseAddress)', [StringComparison]::Ordinal)
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

        public static int Main(string[] args)
        {
            Fixture relocated = BuildFixture(
                new string[]
                {
                    "无关对话一",
                    Pal98DialogueMarkerResolver.DaliReturnText,
                    "无关对话二",
                    "无关对话三",
                    Pal98DialogueMarkerResolver.CanonicalWaterPearlText
                },
                new ushort[] { 4, 0, 1 });
            Pal98DialogueMarkers markers = Pal98DialogueMarkerResolver.Resolve(relocated.Sss, relocated.Messages);
            Assert(markers.CanonicalWaterPearlDialogueId == 4, "canonical dialogue ID was not resolved dynamically");
            Assert(markers.DaliReturnDialogueId == 1, "Dali-return dialogue ID was not resolved dynamically");
            Assert(markers.CanonicalWaterPearlScriptIndex == 0, "canonical script reference is wrong");
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
            }
            finally
            {
                if (Directory.Exists(attachRoot)) Directory.Delete(attachRoot, true);
            }

            Pal98WaterSpiritPearlGate normalGate = new Pal98WaterSpiritPearlGate(markers);
            Assert(!normalGate.CanComplete(1), "an early/random pearl completed the split");
            normalGate.ObserveScriptState(ScriptState(0xFFFF, markers.CanonicalWaterPearlDialogueId));
            Assert(!normalGate.CanComplete(1), "the split completed before leaving the canonical dialogue");
            normalGate.ObserveScriptState(ScriptState(0x001F, 0x0109));
            Assert(normalGate.CanComplete(1), "canonical dialogue followed by the pearl did not complete the split");
            normalGate.Reset();
            Assert(!normalGate.CanComplete(1), "route reset retained the marker latch");

            Pal98WaterSpiritPearlGate skippedRouteGate = new Pal98WaterSpiritPearlGate(markers);
            skippedRouteGate.ObserveScriptState(ScriptState(0xFFFF, markers.DaliReturnDialogueId));
            Assert(!skippedRouteGate.CanComplete(1), "the split completed inside the Dali-return dialogue");
            skippedRouteGate.ObserveScriptState(ScriptState(0x0000, 0x0000));
            Assert(skippedRouteGate.CanComplete(1), "Dali-return dialogue plus an existing pearl did not complete the split");

            Pal98WaterSpiritPearlGate unrelatedGate = new Pal98WaterSpiritPearlGate(markers);
            unrelatedGate.ObserveScriptState(ScriptState(0xFFFF, 0));
            unrelatedGate.ObserveScriptState(ScriptState(0x0000, 0));
            Assert(!unrelatedGate.CanComplete(1), "an unrelated dialogue armed the split");

            Fixture duplicateDialogue = BuildFixture(
                new string[]
                {
                    Pal98DialogueMarkerResolver.CanonicalWaterPearlText,
                    Pal98DialogueMarkerResolver.CanonicalWaterPearlText,
                    Pal98DialogueMarkerResolver.DaliReturnText
                },
                new ushort[] { 0, 2 });
            AssertInvalid(duplicateDialogue, "duplicate dialogue text was accepted");

            Fixture duplicateScript = BuildFixture(
                new string[]
                {
                    Pal98DialogueMarkerResolver.CanonicalWaterPearlText,
                    Pal98DialogueMarkerResolver.DaliReturnText
                },
                new ushort[] { 0, 0, 1 });
            AssertInvalid(duplicateScript, "duplicate script reference was accepted");

            Fixture malformed = BuildFixture(
                new string[]
                {
                    Pal98DialogueMarkerResolver.CanonicalWaterPearlText,
                    Pal98DialogueMarkerResolver.DaliReturnText
                },
                new ushort[] { 0, 1 });
            malformed.Sss[0] = 0;
            malformed.Sss[1] = 0;
            malformed.Sss[2] = 0;
            malformed.Sss[3] = 0;
            AssertInvalid(malformed, "malformed MKF header was accepted");

            if (args.Length == 2)
            {
                Pal98DialogueMarkers realMarkers = Pal98DialogueMarkerResolver.Resolve(args[0], args[1]);
                Console.WriteLine(
                    "REAL: canonical=0x{0:X4}/script=0x{1:X4}, Dali=0x{2:X4}/script=0x{3:X4}",
                    realMarkers.CanonicalWaterPearlDialogueId,
                    realMarkers.CanonicalWaterPearlScriptIndex,
                    realMarkers.DaliReturnDialogueId,
                    realMarkers.DaliReturnScriptIndex);
            }

            Console.WriteLine("PASS: dynamic resource resolution, malformed/duplicate rejection, early-item guard, canonical route, skipped route, reset, and unrelated-dialogue guard.");
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
