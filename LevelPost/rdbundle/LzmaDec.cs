using System;
using System.Runtime.InteropServices;

namespace rdbundle
{
    static class LzmaDec
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private unsafe delegate int FDec64(byte* dst, long destlen, byte* src, long srclen);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int FDec32(byte* dst, int destlen, byte* src, int srclen);

        //[DllImport("lzmadec32.dll", CallingConvention=CallingConvention.StdCall)]
        //private static extern unsafe int mydec(byte* dst, int destlen, byte* src, int srclen);

        const uint MEM_COMMIT = 0x1000;
        const uint MEM_RESERVE = 0x2000;
        const uint MEM_DECOMMIT = 0x4000;
        const uint PAGE_EXECUTE_READWRITE = 0x40;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress,
        UIntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll")]
        static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress,
        UIntPtr dwSize, uint dwFreeType);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        public static unsafe void LzmaDecode(byte[] src, byte[] dst)
        {
            /*
            int ret;
            fixed (byte* srcp = src)
            fixed (byte* dstp = dst) {
                Debug.WriteLine(((uint)dstp + dst.Length).ToString("X"));
                ret = mydec(dstp, dst.Length, srcp, src.Length);
            }
            if (ret != 0)
                throw new Exception("LzmaDecode failed " + ret);
            return;
            */
            if (Environment.Is64BitProcess)
            {
                byte[] libFile = LevelPost.Properties.Resources.lzmadec;
                var memSize = (UIntPtr)0x6000;
                var memBase = VirtualAllocEx(GetCurrentProcess(), IntPtr.Zero, memSize,
                    MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE);
                if (memBase == (IntPtr)0)
                    throw new System.ComponentModel.Win32Exception();

                Marshal.Copy(libFile, 0x400, memBase + 0x1000, 0x2a00); // .text
                Marshal.Copy(libFile, 0x2e00, memBase + 0x4000, 0x400); // .rdata
                Marshal.Copy(libFile, 0x3200, memBase + 0x5000, 0x200); // .pdata

                var kernel32 = GetModuleHandle("kernel32");
                Marshal.Copy(BitConverter.GetBytes((ulong)GetProcAddress(kernel32, "HeapFree")), 0, memBase + 0x4000, 8);
                Marshal.Copy(BitConverter.GetBytes((ulong)GetProcAddress(kernel32, "GetProcessHeap")), 0, memBase + 0x4008, 8);
                Marshal.Copy(BitConverter.GetBytes((ulong)GetProcAddress(kernel32, "HeapAlloc")), 0, memBase + 0x4010, 8);

                var f = (FDec64)Marshal.GetDelegateForFunctionPointer(memBase + 0x2550, typeof(FDec64));
                int ret;
                fixed (byte* srcp = src)
                fixed (byte* dstp = dst)
                    ret = f(dstp, dst.Length, srcp, src.Length);

                VirtualFreeEx(GetCurrentProcess(), memBase, memSize, MEM_DECOMMIT);
                if (ret != 0)
                    throw new Exception("LzmaDecode failed " + ret);
            }
            else
            {
                byte[] libFile = LevelPost.Properties.Resources.lzmadec32;
                var memSize = (UIntPtr)0x6000;
                var memBase = VirtualAllocEx(GetCurrentProcess(), IntPtr.Zero, memSize,
                    MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE);
                if (memBase == (IntPtr)0)
                    throw new System.ComponentModel.Win32Exception();

                Marshal.Copy(libFile, 0x400, memBase + 0x1000, 0x2c00); // .text
                Marshal.Copy(libFile, 0x3000, memBase + 0x4000, 0x200); // .rdata
                Marshal.Copy(libFile, 0x3200, memBase + 0x5000, 0x200); // .reloc

                // relocations
                foreach (var ofs in new int[] { 0x3af5, 0x3afc, 0x3b15, 0x3b1c, 0x3b3b, 0x3b43 })
                    *(uint*)(memBase + ofs) += (uint)memBase - 0x10000000;

                var kernel32 = GetModuleHandle("kernel32");
                Marshal.Copy(BitConverter.GetBytes((uint)GetProcAddress(kernel32, "HeapFree")), 0, memBase + 0x4000, 4);
                Marshal.Copy(BitConverter.GetBytes((uint)GetProcAddress(kernel32, "GetProcessHeap")), 0, memBase + 0x4004, 4);
                Marshal.Copy(BitConverter.GetBytes((uint)GetProcAddress(kernel32, "HeapAlloc")), 0, memBase + 0x4008, 4);

                var f = (FDec32)Marshal.GetDelegateForFunctionPointer(memBase + 0x3b30, typeof(FDec32));
                int ret;
                fixed (byte* srcp = src)
                fixed (byte* dstp = dst)
                    ret = f(dstp, dst.Length, srcp, src.Length);

                VirtualFreeEx(GetCurrentProcess(), memBase, memSize, MEM_DECOMMIT);
                if (ret != 0)
                    throw new Exception("LzmaDecode failed " + ret);
            }
        }
    }
}
