using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Pal98Timer
{
    internal sealed class IntegrityModule
    {
        internal string Name, Path;
        internal long Address;
        internal uint Size;
        internal bool Ambiguous;
        internal readonly List<IntegrityModule> OtherInstances = new List<IntegrityModule>();
    }
    internal sealed class IntegrityProcessReader : IDisposable
    {
        // No VM_WRITE, VM_OPERATION, PROCESS_ALL_ACCESS, or debug privilege.
        internal const uint ReadAccess = 0x00101010; // SYNCHRONIZE | QUERY_LIMITED_INFORMATION | VM_READ
        private IntPtr handle;
        internal readonly PalLiveProcessIdentity Identity;
        internal IntegrityProcessReader(int pid)
        {
            handle = OpenProcess(ReadAccess, false, pid);
            if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            Identity = PalLiveProcessIdentity.ReadHandle(handle, pid);
            if (Identity == null) { Dispose(); throw new IOException("目标进程已退出或身份读取失败"); }
        }
        internal bool IsCurrent() { return Identity.SameInstance(PalLiveProcessIdentity.ReadHandle(handle, Identity.Pid)); }
        internal Dictionary<string, IntegrityModule> Modules()
        {
            if (!IsCurrent()) throw new IOException("目标进程已退出");
            IntPtr snapshot = CreateToolhelp32Snapshot(0x18, (uint)Identity.Pid); // MODULE | MODULE32, including the 32-bit PAL target.
            if (snapshot == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var modules = new Dictionary<string, IntegrityModule>(StringComparer.OrdinalIgnoreCase);
                var entry = new ModuleEntry { size = (uint)Marshal.SizeOf(typeof(ModuleEntry)) };
                if (!Module32First(snapshot, ref entry)) throw new Win32Exception(Marshal.GetLastWin32Error());
                do
                {
                    RecordModule(modules, new IntegrityModule { Name = entry.name, Path = System.IO.Path.GetFullPath(entry.path), Address = entry.address.ToInt64(), Size = entry.moduleSize });
                    entry.size = (uint)Marshal.SizeOf(typeof(ModuleEntry));
                } while (Module32Next(snapshot, ref entry));
                if (!IsCurrent()) throw new IOException("目标进程已变化");
                return modules;
            }
            finally { PalLiveProcessIdentity.Close(snapshot); }
        }
        private static void RecordModule(IDictionary<string, IntegrityModule> modules, IntegrityModule value)
        {
            IntegrityModule existing;
            if (!modules.TryGetValue(value.Name, out existing)) { modules.Add(value.Name, value); return; }
            // Preserve all physical instances: proxy/system DLL pairs legitimately
            // share a name. Only identical path/address/size entries coalesce.
            if (SameModule(existing, value) || existing.OtherInstances.Any(item => SameModule(item, value))) return;
            existing.OtherInstances.Add(value); existing.Ambiguous = true;
        }
        private static bool SameModule(IntegrityModule first, IntegrityModule second)
        {
            return first.Address == second.Address && first.Size == second.Size &&
                string.Equals(first.Path, second.Path, StringComparison.OrdinalIgnoreCase);
        }
        internal static IntegrityModule ModuleAtPath(IDictionary<string, IntegrityModule> modules, string name, string expectedPath)
        {
            IntegrityModule first;
            if (!modules.TryGetValue(name, out first)) throw new IOException("模块尚未加载：" + name);
            IntegrityModule match = null;
            foreach (var candidate in new[] { first }.Concat(first.OtherInstances))
            {
                if (!string.Equals(candidate.Path, expectedPath, StringComparison.OrdinalIgnoreCase)) continue;
                if (match != null && !SameModule(match, candidate))
                    throw new IOException("核验模块同一路径存在不同加载身份：" + name);
                if (candidate.Address <= 0 || candidate.Size == 0) throw new IOException("核验模块身份尚不可读：" + name);
                match = candidate;
            }
            return match;
        }
        internal byte[] Read(long address, int length)
        {
            if (address <= 0 || length <= 0 || length > 4096 || address > long.MaxValue - length)
                throw new IOException("核验内存范围无效");
            if (!IsCurrent()) throw new IOException("目标进程已退出");
            var bytes = new byte[length]; IntPtr count;
            if (!ReadProcessMemory(handle, new IntPtr(address), bytes, (IntPtr)length, out count) || count.ToInt64() != length)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!IsCurrent()) throw new IOException("目标进程已变化");
            return bytes;
        }
        internal bool VerifyRegion(ReleaseIntegrityRegion region, IDictionary<string, IntegrityModule> modules)
        {
            if (region.pointer_rva.HasValue)
            {
                // The pointer slot and expected bytes are fixed by the embedded
                // build manifest. A target process cannot nominate another slot.
                if (region.rva.HasValue) throw new IOException("核验区域地址定义重复");
                long slot = ModuleAddress(modules, region.module, region.pointer_rva.Value, 4);
                byte[] pointer = Read(slot, 4);
                long indirect = BitConverter.ToUInt32(pointer, 0);
                int length = region.expected.Length / 2;
                if (indirect < 65536 || (ulong)indirect + (uint)length > 0x100000000UL)
                    throw new IOException("随机跳板地址尚不可读或超出32位范围");
                byte[] expectedIndirect = ExpectedBytesAtAddress(region, modules, indirect);
                byte[] actual = Read(indirect, expectedIndirect.Length);
                if (!pointer.SequenceEqual(Read(slot, 4))) throw new IOException("核验期间随机跳板指针发生变化");
                return actual.SequenceEqual(expectedIndirect);
            }
            long address;
            byte[] expected = ExpectedBytes(region, modules, out address);
            return Read(address, expected.Length).SequenceEqual(expected);
        }
        private static long ModuleAddress(IDictionary<string, IntegrityModule> modules, string name, uint rva, int length)
        {
            IntegrityModule module;
            if (length <= 0 || !modules.TryGetValue(name, out module) || module.Ambiguous || module.Address <= 0 ||
                (ulong)rva + (uint)length > module.Size || module.Address > long.MaxValue - rva - length)
                throw new IOException("核验模块或代码区尚不可读：" + name);
            return module.Address + rva;
        }
        internal static byte[] ExpectedBytes(ReleaseIntegrityRegion region, IDictionary<string, IntegrityModule> modules, out long address)
        {
            if (!region.rva.HasValue || region.pointer_rva.HasValue) throw new IOException("固定区域需要唯一的模块RVA");
            address = ModuleAddress(modules, region.module, region.rva.Value, region.expected.Length / 2);
            return ExpectedBytesAtAddress(region, modules, address);
        }
        internal static byte[] ExpectedBytesAtAddress(ReleaseIntegrityRegion region, IDictionary<string, IntegrityModule> modules, long address)
        {
            byte[] expected = Enumerable.Range(0, region.expected.Length / 2).Select(i => Convert.ToByte(region.expected.Substring(i * 2, 2), 16)).ToArray();
            if (address <= 0 || address > long.MaxValue - expected.Length) throw new IOException("核验区域地址溢出");
            foreach (var fixup in region.fixups ?? new ReleaseIntegrityFixup[0])
            {
                if (fixup.offset < 0 || (long)fixup.offset + 4 > expected.Length) throw new IOException("核验重定位超出区域");
                long destination = ModuleAddress(modules, fixup.module, fixup.rva, 1);
                uint value;
                if (fixup.kind == "abs32")
                {
                    if (destination < 0 || destination > uint.MaxValue) throw new IOException("32 位重定位超出范围");
                    value = (uint)destination;
                }
                else
                {
                    long displacement = destination - (address + fixup.offset + 4);
                    if (displacement < int.MinValue || displacement > int.MaxValue) throw new IOException("相对跳转超出范围");
                    value = unchecked((uint)(int)displacement);
                }
                Buffer.BlockCopy(BitConverter.GetBytes(value), 0, expected, fixup.offset, 4);
            }
            return expected;
        }
        public void Dispose() { IntPtr old = handle; handle = IntPtr.Zero; PalLiveProcessIdentity.Close(old); }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ModuleEntry
        {
            internal uint size, moduleId, processId, globalUsage, processUsage;
            internal IntPtr address;
            internal uint moduleSize;
            internal IntPtr moduleHandle;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string path;
        }
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Module32First(IntPtr snapshot, ref ModuleEntry entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Module32Next(IntPtr snapshot, ref ModuleEntry entry);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr read);
    }
}
