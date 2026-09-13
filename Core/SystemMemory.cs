using System;
using System.Runtime.InteropServices;

namespace CustomLauncher.Core
{
    public static class SystemMemory
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length, Load;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
                TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

        public static bool TryRead(out long totalBytes, out long availableBytes)
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };

            if (GlobalMemoryStatusEx(ref status) && status.TotalPhysical > 0)
            {
                totalBytes = (long)status.TotalPhysical;
                availableBytes = (long)status.AvailablePhysical;
                return true;
            }

            GCMemoryInfo info = GC.GetGCMemoryInfo();
            totalBytes = info.TotalAvailableMemoryBytes;
            availableBytes = Math.Max(0, info.TotalAvailableMemoryBytes - info.MemoryLoadBytes);
            return totalBytes > 0;
        }
    }
}
