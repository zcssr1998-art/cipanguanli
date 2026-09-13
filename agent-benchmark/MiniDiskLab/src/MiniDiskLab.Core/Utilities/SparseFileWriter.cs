using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MiniDiskLab.Core.Utilities;

/// <summary>
/// 在 NTFS 上创建稀疏文件。用于在不占用真实磁盘空间的情况下
/// 生成大文件测试数据。
/// </summary>
[SupportedOSPlatform("windows")]
public static class SparseFileWriter
{
    private const uint FSCTL_SET_SPARSE = 0x000900C4;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandleLike hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    /// <summary>
    /// 创建一个逻辑大小为 <paramref name="logicalSizeBytes"/> 的稀疏文件。
    /// 磁盘上的实际占用会小得多。
    /// </summary>
    /// <returns>如果该文件真的是稀疏文件则返回 true；如果已回退为普通文件则返回 false。</returns>
    public static bool Create(string path, long logicalSizeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (logicalSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalSizeBytes));
        }

        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent);
        }

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

        var markedSparse = false;
        try
        {
            markedSparse = DeviceIoControl(
                new SafeFileHandleLike(fs.SafeFileHandle.DangerousGetHandle()),
                FSCTL_SET_SPARSE,
                IntPtr.Zero,
                0,
                IntPtr.Zero,
                0,
                out _,
                IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            markedSparse = false;
        }

        // 设置逻辑长度。若已标记为稀疏，则不会分配真实数据块。
        fs.SetLength(logicalSizeBytes);

        if (markedSparse)
        {
            return true;
        }

        // 回退方案：写入一个很小的真实内容，这样即使不支持稀疏文件，
        // 大小也仍然是正确的。
        return false;
    }

    /// <summary>
    /// 创建普通文件并写入指定字节的内容。用于小型/普通文件。
    /// </summary>
    public static void CreateContent(string path, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllBytes(path, content);
    }

    /// <summary>
    /// 一个最小化的 SafeHandle 包装器，使 <see cref="DllImportAttribute"/> 调用
    /// 能接收原生句柄，而无需引入 Microsoft.Win32.SafeHandles 的依赖。
    /// </summary>
    private sealed class SafeFileHandleLike : SafeHandleLikeBase
    {
        public SafeFileHandleLike(IntPtr handle)
            : base(invalidHandleValue: IntPtr.Zero, ownsHandle: false)
        {
            SetHandle(handle);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle() => true;
    }

    /// <summary>以最小的表面积实现自定义安全句柄。</summary>
    private abstract class SafeHandleLikeBase : SafeHandle
    {
        protected SafeHandleLikeBase(IntPtr invalidHandleValue, bool ownsHandle)
            : base(invalidHandleValue, ownsHandle)
        {
        }
    }
}
