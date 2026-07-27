using System;
using System.IO;
using System.Security.Cryptography;

namespace Pal98Timer
{
    internal sealed class SRPGFlyingFlagSidecarSnapshot
    {
        public bool Present { get; private set; }
        public byte[] Payload { get; private set; }

        public SRPGFlyingFlagSidecarSnapshot(bool present, byte[] payload)
        {
            Present = present;
            Payload = payload;
        }
    }

    internal static class SRPGSidecarTransport
    {
        internal const int EnvelopeVersion = 1;
        internal const string FlyingFlagSidecarFileName = "PalDrawCard.FlyingFlagAll.v1.bin";
        internal const int MaximumPayloadBytes = 1024 * 1024;

        internal static void CaptureFlyingFlagSnapshot(string gameFolder, SRPGobj package)
        {
            if (package == null)
            {
                throw new ArgumentNullException("package");
            }

            string path = GetSidecarPath(gameFolder);
            package.FlyingFlagSidecarEnvelopeVersion = EnvelopeVersion;
            package.FlyingFlagSidecarCaptured = true;
            package.FlyingFlagSidecarPresent = File.Exists(path);
            package.FlyingFlagSidecarPayload = null;
            package.FlyingFlagSidecarSha256 = null;

            if (!package.FlyingFlagSidecarPresent)
            {
                return;
            }

            byte[] payload = File.ReadAllBytes(path);
            ValidatePayloadLength(payload);
            package.FlyingFlagSidecarPayload = payload;
            package.FlyingFlagSidecarSha256 = ComputeSha256(payload);
        }

        internal static SRPGFlyingFlagSidecarSnapshot ReadFlyingFlagSnapshot(SRPGobj package)
        {
            if (package == null)
            {
                throw new ArgumentNullException("package");
            }

            if (!package.FlyingFlagSidecarCaptured)
            {
                return null;
            }

            if (package.FlyingFlagSidecarEnvelopeVersion != EnvelopeVersion)
            {
                throw new InvalidDataException("SRPG中的飞行旗状态版本不受支持");
            }

            if (!package.FlyingFlagSidecarPresent)
            {
                if (package.FlyingFlagSidecarPayload != null || package.FlyingFlagSidecarSha256 != null)
                {
                    throw new InvalidDataException("SRPG中的飞行旗空快照格式无效");
                }
                return new SRPGFlyingFlagSidecarSnapshot(false, null);
            }

            ValidatePayloadLength(package.FlyingFlagSidecarPayload);
            if (package.FlyingFlagSidecarSha256 == null || package.FlyingFlagSidecarSha256.Length != 32)
            {
                throw new InvalidDataException("SRPG中的飞行旗状态缺少SHA-256校验值");
            }

            byte[] actualHash = ComputeSha256(package.FlyingFlagSidecarPayload);
            if (!HashesEqual(actualHash, package.FlyingFlagSidecarSha256))
            {
                throw new InvalidDataException("SRPG中的飞行旗状态SHA-256校验失败");
            }

            return new SRPGFlyingFlagSidecarSnapshot(
                true,
                (byte[])package.FlyingFlagSidecarPayload.Clone());
        }

        internal static string ApplyFlyingFlagSnapshot(
            string gameFolder,
            SRPGFlyingFlagSidecarSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            string targetPath = GetSidecarPath(gameFolder);
            if (!snapshot.Present)
            {
                if (!File.Exists(targetPath))
                {
                    return null;
                }

                string absentBackupPath = BuildBackupPath(targetPath);
                File.Move(targetPath, absentBackupPath);
                return absentBackupPath;
            }

            ValidatePayloadLength(snapshot.Payload);
            string tempPath = targetPath + ".paltimer-import-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(tempPath, snapshot.Payload);
                if (File.Exists(targetPath))
                {
                    string backupPath = BuildBackupPath(targetPath);
                    File.Replace(tempPath, targetPath, backupPath, true);
                    return backupPath;
                }

                File.Move(tempPath, targetPath);
                return null;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static string GetSidecarPath(string gameFolder)
        {
            if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
            {
                throw new DirectoryNotFoundException("找不到PAL.exe所在目录，无法处理飞行旗状态");
            }
            return Path.Combine(gameFolder, FlyingFlagSidecarFileName);
        }

        private static void ValidatePayloadLength(byte[] payload)
        {
            if (payload == null || payload.Length == 0 || payload.Length > MaximumPayloadBytes)
            {
                throw new InvalidDataException("SRPG中的飞行旗状态长度无效");
            }
        }

        private static byte[] ComputeSha256(byte[] payload)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(payload);
            }
        }

        private static bool HashesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            int difference = 0;
            for (int i = 0; i < left.Length; ++i)
            {
                difference |= left[i] ^ right[i];
            }
            return difference == 0;
        }

        private static string BuildBackupPath(string targetPath)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
            string candidate = targetPath + ".paltimer-backup-" + timestamp;
            int suffix = 1;
            while (File.Exists(candidate))
            {
                candidate = targetPath + ".paltimer-backup-" + timestamp + "-" + suffix;
                ++suffix;
            }
            return candidate;
        }
    }
}
