using HFrame.OS;
using System;
using System.IO;
using System.Text;

namespace Pal98Timer
{
    internal sealed class Pal98DialogueMarkers
    {
        internal ushort CanonicalWaterPearlDialogueId;
        internal int CanonicalWaterPearlScriptIndex;
        internal ushort DaliReturnDialogueId;
        internal int DaliReturnScriptIndex;
    }

    internal static class Pal98DialogueMarkerResolver
    {
        internal const string CanonicalWaterPearlText = "得到水灵珠";
        internal const string DaliReturnText = "糟．．希望灵儿不会有事才好";

        internal static Pal98DialogueMarkers Resolve(string sssPath, string messagePath)
        {
            if (string.IsNullOrEmpty(sssPath) || string.IsNullOrEmpty(messagePath))
            {
                throw new InvalidDataException("SSS.MKF 或 M.MSG 路径为空。");
            }

            return Resolve(File.ReadAllBytes(sssPath), File.ReadAllBytes(messagePath));
        }

        internal static Pal98DialogueMarkers Resolve(byte[] sss, byte[] messages)
        {
            if (sss == null || messages == null)
            {
                throw new InvalidDataException("SSS.MKF 或 M.MSG 内容为空。");
            }
            if (sss.Length < 24)
            {
                throw new InvalidDataException("SSS.MKF 文件头不完整。");
            }

            uint headerLength = ReadUInt32(sss, 0);
            if (headerLength < 24 || headerLength > sss.Length || headerLength % 4 != 0)
            {
                throw new InvalidDataException("SSS.MKF 文件头偏移表无效。");
            }

            int offsetCount = checked((int)(headerLength / 4));
            uint previousOffset = 0;
            for (int i = 0; i < offsetCount; ++i)
            {
                uint currentOffset = ReadUInt32(sss, i * 4);
                if (currentOffset < headerLength || currentOffset > sss.Length ||
                    (i > 0 && currentOffset < previousOffset))
                {
                    throw new InvalidDataException("SSS.MKF 文件记录偏移无效。");
                }
                previousOffset = currentOffset;
            }

            int dialogueOffsetsStart = checked((int)ReadUInt32(sss, 12));
            int dialogueOffsetsEnd = checked((int)ReadUInt32(sss, 16));
            int scriptStart = dialogueOffsetsEnd;
            int scriptEnd = checked((int)ReadUInt32(sss, 20));
            int dialogueOffsetsLength = dialogueOffsetsEnd - dialogueOffsetsStart;
            int scriptLength = scriptEnd - scriptStart;
            if (dialogueOffsetsLength < 8 || dialogueOffsetsLength % 4 != 0 ||
                scriptLength < 8 || scriptLength % 8 != 0)
            {
                throw new InvalidDataException("SSS.MKF 对话偏移记录或脚本记录长度无效。");
            }

            int dialogueCount = dialogueOffsetsLength / 4 - 1;
            uint previousMessageOffset = 0;
            for (int i = 0; i <= dialogueCount; ++i)
            {
                uint currentMessageOffset = ReadUInt32(sss, dialogueOffsetsStart + i * 4);
                if (currentMessageOffset > messages.Length ||
                    (i > 0 && currentMessageOffset < previousMessageOffset))
                {
                    throw new InvalidDataException("SSS.MKF 中的 M.MSG 对话偏移无效。");
                }
                previousMessageOffset = currentMessageOffset;
            }

            Encoding gbk = Encoding.GetEncoding(
                936,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            ushort canonicalDialogueId = FindUniqueDialogueId(
                sss,
                messages,
                dialogueOffsetsStart,
                dialogueCount,
                CanonicalWaterPearlText,
                gbk);
            ushort daliReturnDialogueId = FindUniqueDialogueId(
                sss,
                messages,
                dialogueOffsetsStart,
                dialogueCount,
                DaliReturnText,
                gbk);
            if (canonicalDialogueId == daliReturnDialogueId)
            {
                throw new InvalidDataException("水灵珠的两个剧情标记解析到了同一条对话。");
            }

            return new Pal98DialogueMarkers
            {
                CanonicalWaterPearlDialogueId = canonicalDialogueId,
                CanonicalWaterPearlScriptIndex = FindUniqueScriptReference(
                    sss,
                    scriptStart,
                    scriptLength,
                    canonicalDialogueId,
                    CanonicalWaterPearlText),
                DaliReturnDialogueId = daliReturnDialogueId,
                DaliReturnScriptIndex = FindUniqueScriptReference(
                    sss,
                    scriptStart,
                    scriptLength,
                    daliReturnDialogueId,
                    DaliReturnText)
            };
        }

        private static ushort FindUniqueDialogueId(
            byte[] sss,
            byte[] messages,
            int dialogueOffsetsStart,
            int dialogueCount,
            string targetText,
            Encoding encoding)
        {
            int foundId = -1;
            for (int i = 0; i < dialogueCount; ++i)
            {
                int start = checked((int)ReadUInt32(sss, dialogueOffsetsStart + i * 4));
                int end = checked((int)ReadUInt32(sss, dialogueOffsetsStart + (i + 1) * 4));
                string text = encoding.GetString(messages, start, end - start).TrimEnd('\0');
                if (!string.Equals(text, targetText, StringComparison.Ordinal))
                {
                    continue;
                }
                if (foundId >= 0)
                {
                    throw new InvalidDataException("M.MSG 中存在重复的剧情对话：" + targetText);
                }
                foundId = i;
            }

            if (foundId < 0 || foundId > ushort.MaxValue)
            {
                throw new InvalidDataException("M.MSG 中未唯一找到剧情对话：" + targetText);
            }
            return checked((ushort)foundId);
        }

        private static int FindUniqueScriptReference(
            byte[] sss,
            int scriptStart,
            int scriptLength,
            ushort dialogueId,
            string targetText)
        {
            int foundIndex = -1;
            int scriptCount = scriptLength / 8;
            for (int i = 0; i < scriptCount; ++i)
            {
                int offset = scriptStart + i * 8;
                if (BitConverter.ToUInt16(sss, offset) != 0xFFFF ||
                    BitConverter.ToUInt16(sss, offset + 2) != dialogueId)
                {
                    continue;
                }
                if (foundIndex >= 0)
                {
                    throw new InvalidDataException("SSS.MKF 中存在重复的剧情脚本引用：" + targetText);
                }
                foundIndex = i;
            }

            if (foundIndex < 0)
            {
                throw new InvalidDataException("SSS.MKF 中未唯一找到剧情脚本引用：" + targetText);
            }
            return foundIndex;
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            if (offset < 0 || offset > data.Length - 4)
            {
                throw new InvalidDataException("SSS.MKF 偏移超出文件范围。");
            }
            return BitConverter.ToUInt32(data, offset);
        }
    }

    internal sealed class Pal98WaterSpiritPearlGate
    {
        private readonly ushort CanonicalWaterPearlDialogueId;
        private readonly ushort DaliReturnDialogueId;
        private bool MarkerSeen;
        private bool MarkerExited;

        internal Pal98WaterSpiritPearlGate(Pal98DialogueMarkers markers)
        {
            if (markers == null)
            {
                throw new ArgumentNullException("markers");
            }
            CanonicalWaterPearlDialogueId = markers.CanonicalWaterPearlDialogueId;
            DaliReturnDialogueId = markers.DaliReturnDialogueId;
        }

        internal void ObserveScriptState(ulong scriptState)
        {
            ushort lowWord = unchecked((ushort)(scriptState & 0xFFFF));
            ushort dialogueId = unchecked((ushort)((scriptState >> 16) & 0xFFFF));
            bool isTargetMarker = lowWord == 0xFFFF &&
                (dialogueId == CanonicalWaterPearlDialogueId || dialogueId == DaliReturnDialogueId);
            if (isTargetMarker)
            {
                MarkerSeen = true;
            }
            else if (MarkerSeen)
            {
                MarkerExited = true;
            }
        }

        internal bool CanComplete(int waterSpiritPearlCount)
        {
            return MarkerExited && waterSpiritPearlCount > 0;
        }

        internal void Reset()
        {
            MarkerSeen = false;
            MarkerExited = false;
        }
    }

    internal static class Pal98CurrentScriptStateReader
    {
        internal const int CurrentScriptStatePointerOffset = 0x500;

        internal static bool TryRead(IntPtr processHandle, int baseAddress, out ulong scriptState)
        {
            scriptState = 0;
            if (processHandle == IntPtr.Zero || baseAddress == 0)
            {
                return false;
            }

            uint scriptStateAddress;
            byte[] pointerBuffer = new byte[4];
            int bytesRead;
            long pointerAddress = (long)unchecked((uint)baseAddress) + CurrentScriptStatePointerOffset;
            if (!Kernel32.ReadProcessMemory(
                    processHandle,
                    new IntPtr(pointerAddress),
                    pointerBuffer,
                    pointerBuffer.Length,
                    out bytesRead) ||
                bytesRead != pointerBuffer.Length)
            {
                return false;
            }

            scriptStateAddress = BitConverter.ToUInt32(pointerBuffer, 0);
            if (scriptStateAddress == 0)
            {
                return false;
            }

            byte[] stateBuffer = new byte[8];
            if (!Kernel32.ReadProcessMemory(
                    processHandle,
                    new IntPtr((long)scriptStateAddress),
                    stateBuffer,
                    stateBuffer.Length,
                    out bytesRead) ||
                bytesRead != stateBuffer.Length)
            {
                return false;
            }

            scriptState = BitConverter.ToUInt64(stateBuffer, 0);
            return true;
        }
    }

    internal sealed class Pal98WaterSpiritPearlSplit
    {
        private int AttachedProcessId = -1;
        private Pal98WaterSpiritPearlGate Gate;

        internal bool ResourcesResolved { get; private set; }
        internal string ResolutionError { get; private set; }

        internal void Attach(int processId, string gameDirectory)
        {
            if (processId == AttachedProcessId)
            {
                return;
            }

            Detach();
            AttachedProcessId = processId;
            try
            {
                if (processId < 0 || string.IsNullOrEmpty(gameDirectory))
                {
                    throw new InvalidDataException("无法确定 PAL.exe 游戏目录。");
                }

                Pal98DialogueMarkers markers = Pal98DialogueMarkerResolver.Resolve(
                    Path.Combine(gameDirectory, "SSS.MKF"),
                    Path.Combine(gameDirectory, "M.MSG"));
                Gate = new Pal98WaterSpiritPearlGate(markers);
                ResourcesResolved = true;
                ResolutionError = "";
            }
            catch (Exception ex)
            {
                Gate = null;
                ResourcesResolved = false;
                ResolutionError = ex.Message;
            }
        }

        internal void Observe(IntPtr processHandle, int baseAddress)
        {
            if (!ResourcesResolved || Gate == null)
            {
                return;
            }

            ulong scriptState;
            if (Pal98CurrentScriptStateReader.TryRead(processHandle, baseAddress, out scriptState))
            {
                Gate.ObserveScriptState(scriptState);
            }
        }

        internal bool CanComplete(int waterSpiritPearlCount)
        {
            return ResourcesResolved && Gate != null && Gate.CanComplete(waterSpiritPearlCount);
        }

        internal void ResetRouteState()
        {
            if (Gate != null)
            {
                Gate.Reset();
            }
        }

        internal void Detach()
        {
            AttachedProcessId = -1;
            Gate = null;
            ResourcesResolved = false;
            ResolutionError = "";
        }
    }
}
