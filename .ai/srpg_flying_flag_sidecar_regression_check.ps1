$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$helperPath = Join-Path $repoRoot "Pal98Timer\SRPGSidecarTransport.cs"
$packagePath = Join-Path $repoRoot "Pal98Timer\仙剑98柔情.cs"
$dx9Path = Join-Path $repoRoot "Pal98Timer\仙剑98柔情DX9.cs"
$unhappyPath = Join-Path $repoRoot "Pal98Timer\仙剑98柔情不欢乐模式.cs"
$projectPath = Join-Path $repoRoot "Pal98Timer\Pal98Timer.csproj"
$rulePath = Join-Path $repoRoot "docs\SRPG_FLYING_FLAG_SIDECAR_RULE.md"

$helper = Get-Content -LiteralPath $helperPath -Raw -Encoding UTF8
$package = Get-Content -LiteralPath $packagePath -Raw -Encoding UTF8
$dx9 = Get-Content -LiteralPath $dx9Path -Raw -Encoding UTF8
$unhappy = Get-Content -LiteralPath $unhappyPath -Raw -Encoding UTF8
$project = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
$rule = Get-Content -LiteralPath $rulePath -Raw -Encoding UTF8

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Needle,
        [Parameter(Mandatory = $true)][string]$Area
    )

    if (-not $Text.Contains($Needle)) {
        throw "$Area is missing SRPG sidecar marker: $Needle"
    }
}

foreach ($needle in @(
    'internal const int EnvelopeVersion = 1;',
    'internal const string FlyingFlagSidecarFileName = "PalDrawCard.FlyingFlagAll.v1.bin";',
    'CaptureFlyingFlagSnapshot',
    'ReadFlyingFlagSnapshot',
    'ApplyFlyingFlagSnapshot',
    'ComputeSha256',
    'File.Replace(tempPath, targetPath, backupPath, true);',
    'File.Move(targetPath, absentBackupPath);',
    '.paltimer-backup-'
)) {
    Assert-Contains -Text $helper -Needle $needle -Area "SRPGSidecarTransport.cs"
}

foreach ($needle in @(
    '[OptionalField(VersionAdded = 2)]',
    'public int FlyingFlagSidecarEnvelopeVersion;',
    'public bool FlyingFlagSidecarCaptured;',
    'public bool FlyingFlagSidecarPresent;',
    'public byte[] FlyingFlagSidecarPayload;',
    'public byte[] FlyingFlagSidecarSha256;'
)) {
    Assert-Contains -Text $package -Needle $needle -Area "SRPGobj"
}

foreach ($entry in @(
    @{ Name = "PAL98DX9"; Text = $dx9 },
    @{ Name = "PAL98UNHAPPY"; Text = $unhappy }
)) {
    foreach ($needle in @(
        'SRPGSidecarTransport.CaptureFlyingFlagSnapshot(palfolder, so);',
        'SRPGSidecarTransport.ReadFlyingFlagSnapshot(so);',
        '此SRPG包含飞行旗完整快照，请先启动PAL.exe再导入',
        'SRPGSidecarTransport.ApplyFlyingFlagSnapshot(GetPalFolder(), flyingFlagSnapshot);',
        'LoadedSrpgRequiresGameRestart = true;',
        '请保持计时器开启，只关闭并重新启动PAL.exe',
        'GetLoadGameSuccessMessage()',
        'GetLoadGameSuccessButtonText()'
    )) {
        Assert-Contains -Text $entry.Text -Needle $needle -Area $entry.Name
    }

    $loadStart = $entry.Text.IndexOf('private void LoadGame(string fn = "SRPG.bin", string rn = "1.RPG")', [StringComparison]::Ordinal)
    $loadEnd = $entry.Text.IndexOf('public void SetTimerFromString', $loadStart, [StringComparison]::Ordinal)
    if ($loadStart -lt 0 -or $loadEnd -lt 0) {
        throw "$($entry.Name) LoadGame block could not be located"
    }
    $loadBlock = $entry.Text.Substring($loadStart, $loadEnd - $loadStart)
    $readIndex = $loadBlock.IndexOf('SRPGSidecarTransport.ReadFlyingFlagSnapshot(so)', [StringComparison]::Ordinal)
    $tempIndex = $loadBlock.IndexOf('string tmppath = rn;', [StringComparison]::Ordinal)
    $applyIndex = $loadBlock.IndexOf('SRPGSidecarTransport.ApplyFlyingFlagSnapshot', [StringComparison]::Ordinal)
    $queueIndex = $loadBlock.IndexOf('WillCopyRPG = tmppath;', [StringComparison]::Ordinal)
    if ($readIndex -lt 0 -or $tempIndex -lt 0 -or $readIndex -gt $tempIndex) {
        throw "$($entry.Name) must validate the sidecar before creating or queueing the RPG import"
    }
    if ($applyIndex -lt 0 -or $queueIndex -lt 0 -or $applyIndex -gt $queueIndex) {
        throw "$($entry.Name) must apply the validated sidecar before queueing the RPG import"
    }
}

Assert-Contains -Text $project -Needle '<Compile Include="SRPGSidecarTransport.cs" />' -Area "Pal98Timer.csproj"
foreach ($needle in @(
    '云服务器继续原样上传/下载',
    '不需要服务器代码或接口变更',
    '旧 SRPG 没有 `FlyingFlagSidecarCaptured` 标记',
    '不读取、覆盖、移动或删除目标游戏目录中的 sidecar',
    '只关闭并重新启动 PAL.exe'
)) {
    Assert-Contains -Text $rule -Needle $needle -Area "SRPG_FLYING_FLAG_SIDECAR_RULE.md"
}

$frameworkRoot = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$cscPath = Join-Path $frameworkRoot "csc.exe"
if (-not (Test-Path -LiteralPath $cscPath)) {
    throw "C# compiler not found: $cscPath"
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("paltimer-srpg-sidecar-test-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
$harnessPath = Join-Path $tempRoot "SidecarHarness.cs"
$exePath = Join-Path $tempRoot "SidecarHarness.exe"
$legacyDir = Join-Path $tempRoot "legacy"
$newDir = Join-Path $tempRoot "new"
New-Item -ItemType Directory -Path $legacyDir, $newDir | Out-Null

try {
    @'
using System;
using System.IO;

namespace Pal98Timer
{
    public class SRPGobj
    {
        public byte[] RPG;
        public string TimerStr;
        public int FlyingFlagSidecarEnvelopeVersion;
        public bool FlyingFlagSidecarCaptured;
        public bool FlyingFlagSidecarPresent;
        public byte[] FlyingFlagSidecarPayload;
        public byte[] FlyingFlagSidecarSha256;
    }

    internal static class SidecarHarness
    {
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; ++i)
            {
                if (left[i] != right[i]) return false;
            }
            return true;
        }

        public static int Main()
        {
            string root = Path.Combine(Path.GetTempPath(), "paltimer-sidecar-harness-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                SRPGobj absentPackage = new SRPGobj();
                SRPGSidecarTransport.CaptureFlyingFlagSnapshot(root, absentPackage);
                Assert(absentPackage.FlyingFlagSidecarCaptured, "absent snapshot was not marked captured");
                Assert(!absentPackage.FlyingFlagSidecarPresent, "absent snapshot was marked present");
                Assert(absentPackage.FlyingFlagSidecarPayload == null, "absent snapshot contains payload");

                byte[] payload = new byte[] { 0x50, 0x46, 0x46, 0x41, 1, 2, 3, 4, 5 };
                string sidecarPath = Path.Combine(root, SRPGSidecarTransport.FlyingFlagSidecarFileName);
                File.WriteAllBytes(sidecarPath, payload);
                SRPGobj presentPackage = new SRPGobj();
                SRPGSidecarTransport.CaptureFlyingFlagSnapshot(root, presentPackage);
                Assert(presentPackage.FlyingFlagSidecarPresent, "present snapshot was not marked present");
                Assert(presentPackage.FlyingFlagSidecarSha256.Length == 32, "SHA-256 length is not 32");

                SRPGFlyingFlagSidecarSnapshot presentSnapshot =
                    SRPGSidecarTransport.ReadFlyingFlagSnapshot(presentPackage);
                Assert(presentSnapshot != null && presentSnapshot.Present, "present snapshot could not be read");
                Assert(BytesEqual(payload, presentSnapshot.Payload), "present payload changed during read");

                SRPGobj oldPackage = new SRPGobj();
                Assert(SRPGSidecarTransport.ReadFlyingFlagSnapshot(oldPackage) == null,
                    "old package must not request sidecar changes");

                presentPackage.FlyingFlagSidecarSha256[0] ^= 0xFF;
                bool corruptRejected = false;
                try
                {
                    SRPGSidecarTransport.ReadFlyingFlagSnapshot(presentPackage);
                }
                catch (InvalidDataException)
                {
                    corruptRejected = true;
                }
                Assert(corruptRejected, "corrupt SHA-256 was accepted");

                byte[] oldTarget = new byte[] { 9, 8, 7 };
                File.WriteAllBytes(sidecarPath, oldTarget);
                string replaceBackup = SRPGSidecarTransport.ApplyFlyingFlagSnapshot(root, presentSnapshot);
                Assert(File.Exists(replaceBackup), "replace did not create a backup");
                Assert(BytesEqual(oldTarget, File.ReadAllBytes(replaceBackup)), "replace backup content changed");
                Assert(BytesEqual(payload, File.ReadAllBytes(sidecarPath)), "replace target content is wrong");

                SRPGFlyingFlagSidecarSnapshot absentSnapshot =
                    SRPGSidecarTransport.ReadFlyingFlagSnapshot(absentPackage);
                string absentBackup = SRPGSidecarTransport.ApplyFlyingFlagSnapshot(root, absentSnapshot);
                Assert(!File.Exists(sidecarPath), "absent snapshot did not move target away");
                Assert(File.Exists(absentBackup), "absent snapshot did not create a backup");
                Assert(BytesEqual(payload, File.ReadAllBytes(absentBackup)), "absent backup content changed");

                Console.WriteLine("PASS: SRPG sidecar capture, old-package compatibility, hash rejection, replace backup, and absent snapshot behavior.");
                return 0;
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
'@ | Set-Content -LiteralPath $harnessPath -Encoding UTF8

    & $cscPath /nologo /codepage:65001 /target:exe /out:$exePath $helperPath $harnessPath
    if ($LASTEXITCODE -ne 0) {
        throw "sidecar harness compilation failed with exit code $LASTEXITCODE"
    }

    & $exePath
    if ($LASTEXITCODE -ne 0) {
        throw "sidecar harness failed with exit code $LASTEXITCODE"
    }

    $legacyCompatPath = Join-Path $legacyDir "SrpgCompat.cs"
    $newCompatPath = Join-Path $newDir "SrpgCompat.cs"
    $legacyExePath = Join-Path $legacyDir "Pal98Timer.exe"
    $newExePath = Join-Path $newDir "Pal98Timer.exe"
    $oldStreamPath = Join-Path $tempRoot "old-srpg.bin"
    $newStreamPath = Join-Path $tempRoot "new-srpg.bin"

    @'
using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Pal98Timer
{
    [Serializable]
    public class SRPGobj
    {
        public byte[] RPG;
        public string TimerStr;
    }

    internal static class SrpgCompat
    {
        public static int Main(string[] args)
        {
            if (args.Length != 2) return 2;
            if (args[0] == "write")
            {
                SRPGobj package = new SRPGobj { RPG = new byte[] { 1, 2, 3 }, TimerStr = "legacy" };
                using (FileStream stream = new FileStream(args[1], FileMode.Create))
                {
                    new BinaryFormatter().Serialize(stream, package);
                }
                return 0;
            }

            using (FileStream stream = new FileStream(args[1], FileMode.Open))
            {
                SRPGobj package = (SRPGobj)new BinaryFormatter().Deserialize(stream);
                if (package.RPG == null || package.RPG.Length != 3 || package.TimerStr != "new") return 3;
            }
            Console.WriteLine("PASS: legacy PalTimer can deserialize a new SRPG while ignoring sidecar fields.");
            return 0;
        }
    }
}
'@ | Set-Content -LiteralPath $legacyCompatPath -Encoding UTF8

    @'
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace Pal98Timer
{
    [Serializable]
    public class SRPGobj
    {
        public byte[] RPG;
        public string TimerStr;
        [OptionalField(VersionAdded = 2)] public int FlyingFlagSidecarEnvelopeVersion;
        [OptionalField(VersionAdded = 2)] public bool FlyingFlagSidecarCaptured;
        [OptionalField(VersionAdded = 2)] public bool FlyingFlagSidecarPresent;
        [OptionalField(VersionAdded = 2)] public byte[] FlyingFlagSidecarPayload;
        [OptionalField(VersionAdded = 2)] public byte[] FlyingFlagSidecarSha256;
    }

    internal static class SrpgCompat
    {
        public static int Main(string[] args)
        {
            if (args.Length != 2) return 2;
            if (args[0] == "write")
            {
                SRPGobj package = new SRPGobj
                {
                    RPG = new byte[] { 1, 2, 3 },
                    TimerStr = "new",
                    FlyingFlagSidecarEnvelopeVersion = 1,
                    FlyingFlagSidecarCaptured = true,
                    FlyingFlagSidecarPresent = true,
                    FlyingFlagSidecarPayload = new byte[] { 4, 5, 6 },
                    FlyingFlagSidecarSha256 = new byte[32]
                };
                using (FileStream stream = new FileStream(args[1], FileMode.Create))
                {
                    new BinaryFormatter().Serialize(stream, package);
                }
                return 0;
            }

            using (FileStream stream = new FileStream(args[1], FileMode.Open))
            {
                SRPGobj package = (SRPGobj)new BinaryFormatter().Deserialize(stream);
                if (package.RPG == null || package.RPG.Length != 3 || package.TimerStr != "legacy") return 3;
                if (package.FlyingFlagSidecarCaptured || package.FlyingFlagSidecarEnvelopeVersion != 0) return 4;
            }
            Console.WriteLine("PASS: new PalTimer deserializes an old SRPG with no sidecar action requested.");
            return 0;
        }
    }
}
'@ | Set-Content -LiteralPath $newCompatPath -Encoding UTF8

    & $cscPath /nologo /codepage:65001 /target:exe /out:$legacyExePath $legacyCompatPath
    if ($LASTEXITCODE -ne 0) { throw "legacy SRPG compatibility harness compilation failed" }
    & $cscPath /nologo /codepage:65001 /target:exe /out:$newExePath $newCompatPath
    if ($LASTEXITCODE -ne 0) { throw "new SRPG compatibility harness compilation failed" }

    & $legacyExePath write $oldStreamPath
    if ($LASTEXITCODE -ne 0) { throw "legacy SRPG writer failed" }
    & $newExePath read $oldStreamPath
    if ($LASTEXITCODE -ne 0) { throw "new PalTimer could not read a legacy SRPG" }
    & $newExePath write $newStreamPath
    if ($LASTEXITCODE -ne 0) { throw "new SRPG writer failed" }
    & $legacyExePath read $newStreamPath
    if ($LASTEXITCODE -ne 0) { throw "legacy PalTimer could not read a new SRPG" }
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath($tempRoot)
    $systemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTemp.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTemp)) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}
