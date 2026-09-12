using System.IO;
using System.Runtime.InteropServices;

namespace Cipanguanli.Core;

// Allocated-size handling is adapted from the MIT-licensed ValleySoft DiskAnalyzer project.
// See THIRD_PARTY_NOTICES.md for attribution and license text.
internal static class NativeDiskSize
{
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetCompressedFileSizeW", CharSet = CharSet.Unicode)]
    private static extern uint GetCompressedFileSize(string fileName, out uint fileSizeHigh);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetDiskFreeSpaceW", CharSet = CharSet.Unicode)]
    private static extern bool GetDiskFreeSpace(string rootPathName, out uint sectorsPerCluster, out uint bytesPerSector,
        out uint numberOfFreeClusters, out uint totalNumberOfClusters);

    public static long GetAllocatedSize(string path, long logicalSize, FileAttributes attributes)
    {
        if ((attributes & (FileAttributes.Offline | FileAttributes.ReparsePoint | RecallOnDataAccess)) != 0)
            return 0;

        var cluster = GetClusterSize(path);
        var fallback = Align(logicalSize, cluster);

        if ((attributes & (FileAttributes.Compressed | FileAttributes.SparseFile)) == 0)
            return fallback;

        try
        {
            var nativePath = path.Length >= 260 && !path.StartsWith("\\\\?\\", StringComparison.Ordinal) ? "\\\\?\\" + path : path;
            var low = GetCompressedFileSize(nativePath, out var high);
            var error = Marshal.GetLastWin32Error();
            if (low == uint.MaxValue && error != 0) return fallback;
            return ((long)high << 32) + low;
        }
        catch
        {
            return fallback;
        }
    }

    private static long GetClusterSize(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrWhiteSpace(root) && GetDiskFreeSpace(root, out var sectors, out var bytes, out _, out _))
                return Math.Max(1, (long)sectors * bytes);
        }
        catch { }
        return 4096;
    }

    private static long Align(long size, long cluster) => size <= 0 ? 0 : ((size + cluster - 1) / cluster) * cluster;
}
