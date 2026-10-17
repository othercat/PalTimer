// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Pal98Timer
{
    internal sealed class SdlpalRelayPackage
    {
        public string schema { get; set; } = "SDLPAL.GeshiRelay.v1";
        public string identity { get; set; }
        public string timer { get; set; }
        public string saveSha256 { get; set; }
        public string saveBase64 { get; set; }
        internal byte[] Validate(string expectedIdentity)
        {
            if (schema != "SDLPAL.GeshiRelay.v1" || identity != expectedIdentity || string.IsNullOrEmpty(timer) || timer.Length > 262144 ||
                saveBase64 == null || saveBase64.Length > 4 * 1024 * 1024 || !SdlpalBridge.Digest(saveSha256))
                throw new InvalidDataException("接力包版本、资源、引擎或速度配置不一致");
            byte[] data = Convert.FromBase64String(saveBase64);
            if (data.Length < 1024 || data.Length > 3 * 1024 * 1024 || Hash(data) != saveSha256)
                throw new InvalidDataException("接力存档损坏或超过容量");
            return data;
        }
        internal static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        internal static string ReadBounded(string path)
        {
            using (var file = File.OpenRead(path))
            {
                if (file.Length > 5 * 1024 * 1024) throw new InvalidDataException("接力包超过容量");
                using (var reader = new StreamReader(file, new UTF8Encoding(false, true))) return reader.ReadToEnd();
            }
        }
        internal static void AtomicWrite(string path, byte[] bytes)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    internal sealed class SdlpalControlResult
    {
        internal SdlpalSnapshot Snapshot;
        internal byte[] SavedBytes;
    }
    internal static class SdlpalControl
    {
        // The mutex covers staging, acknowledgement, and copying the finished
        // save, so two timer instances cannot exchange each other's slot 250.
        internal static SdlpalControlResult Execute(SdlpalBridge bridge, uint command, SdlpalSnapshot expected, byte[] loadBytes = null)
        {
            if (command != 1 && command != 2 || command == 2 && loadBytes == null) throw new ArgumentException("无效接力命令");
            using (var mutex = new Mutex(false, "Local\\SDLPAL.LostEdit.ControlLock.v1." + expected.Pid))
            {
                bool held = false;
                try
                {
                    try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                    if (!held) throw new IOException("另一个计时器正在存读接力档");
                    SdlpalSnapshot current;
                    if (bridge == null || !bridge.TryRead(out current) || current.Pid != expected.Pid || current.Created != expected.Created ||
                        current.epoch != expected.epoch || current.IdentityKey != expected.IdentityKey || !current.canControl)
                        throw new InvalidOperationException("请先回到游戏探索画面，并确认资源和存档未变化");
                    using (var map = MemoryMappedFile.OpenExisting("Local\\SDLPAL.LostEdit.Control.v1." + current.Pid, MemoryMappedFileRights.ReadWrite))
                    using (var view = map.CreateViewAccessor(0, 128, MemoryMappedFileAccess.ReadWrite))
                    {
                        byte[] magic = new byte[8]; view.ReadArray(0, magic, 0, 8);
                        if (Encoding.ASCII.GetString(magic) != "SDLCMD1\0" || view.ReadUInt32(8) != 1 || view.ReadUInt32(12) != 128 ||
                            view.ReadUInt32(16) != current.Pid || view.ReadUInt64(24) != current.Created)
                            throw new InvalidDataException("游戏接力接口身份无效");
                        string save = Path.Combine(current.savePath, "250.rpg");
                        if (command == 2) SdlpalRelayPackage.AtomicWrite(save, loadBytes);
                        byte[] random = new byte[8]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(random);
                        ulong request = BitConverter.ToUInt64(random, 0) | 1UL;
                        uint sequence = unchecked((view.ReadUInt32(32) + 2) & ~1U);
                        view.Write(32, sequence | 1); Thread.MemoryBarrier();
                        view.Write(36, command); view.Write(40, request); view.Write(48, current.epoch);
                        ulong deadline = SdlpalBridge.GetTickCount64() + 2000;
                        view.Write(56, deadline); Thread.MemoryBarrier(); view.Write(32, sequence);
                        while (SdlpalBridge.GetTickCount64() <= deadline + 300)
                        {
                            ulong ack = view.ReadUInt64(64); Thread.MemoryBarrier();
                            if (ack == request)
                            {
                                uint status = view.ReadUInt32(72); ulong epoch = view.ReadUInt64(80);
                                Thread.MemoryBarrier(); if (view.ReadUInt64(64) != ack) continue;
                                if (status != 1) throw new IOException("游戏拒绝接力操作，状态 " + status + "（2忙/3存档已变化/4无效/5写入失败/6过期）");
                                while (SdlpalBridge.GetTickCount64() <= deadline + 1000)
                                {
                                    SdlpalSnapshot after;
                                    if (bridge.TryRead(out after) && after.epoch == epoch && after.Pid == current.Pid && after.Created == current.Created && after.IdentityKey == current.IdentityKey)
                                    {
                                        byte[] saved = null;
                                        if (command == 1)
                                        {
                                            using (var file = File.OpenRead(save))
                                            {
                                                if (file.Length < 1024 || file.Length > 3 * 1024 * 1024) throw new InvalidDataException("游戏存档长度无效");
                                                saved = new byte[file.Length]; int offset = 0, count;
                                                while (offset < saved.Length && (count = file.Read(saved, offset, saved.Length - offset)) > 0) offset += count;
                                                if (offset != saved.Length) throw new EndOfStreamException();
                                            }
                                        }
                                        return new SdlpalControlResult { Snapshot = after, SavedBytes = saved };
                                    }
                                    Thread.Sleep(20);
                                }
                                throw new IOException("游戏已经确认操作，但新快照尚未同步；计时保持暂停，请核对游戏");
                            }
                            Thread.Sleep(20);
                        }
                        throw new IOException("接力请求超时，未自动重试；计时保持暂停，请核对游戏后再操作");
                    }
                }
                finally { if (held) mutex.ReleaseMutex(); }
            }
        }
    }
}
