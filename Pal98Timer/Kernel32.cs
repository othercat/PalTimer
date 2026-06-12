using System;
using System.Collections.Generic;
//using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace HFrame.OS
{
    public static class Kernel32
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(
          IntPtr hProcess,
          IntPtr lpBaseAddress,
          [Out] byte[] lpBuffer,
          int size,
          out int lpNumberOfBytesRead
         );


        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte[] lpBuffer,
            int size,
            out int lpNumberOfBytesWritten
            );


        public const int ERROR_ACCESS_DENIED = 5;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        public static int GetLastWin32Error()
        {
            return Marshal.GetLastWin32Error();
        }

        [DllImport("kernel32.dll ")]
        public static extern bool CloseHandle(int hProcess);

    }
}
