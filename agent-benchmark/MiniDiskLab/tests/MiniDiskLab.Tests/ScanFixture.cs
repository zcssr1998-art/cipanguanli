using MiniDiskLab.Core.Utilities;

namespace MiniDiskLab.Tests;

/// <summary>
/// 基于系统临时目录的测试夹具：创建真实的目录与（稀疏）文件，
/// 并在释放时递归清理。
///
/// 所有内容都位于 <c>%TEMP%\MiniDiskLabTests\&lt;guid&gt;</c> 之下，
/// 绝不触碰用户数据。
/// </summary>
internal sealed class ScanFixture : IDisposable
{
    private bool _disposed;

    public ScanFixture()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "MiniDiskLabTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Root);
    }

    /// <summary>临时目录根路径。</summary>
    public string Root { get; }

    /// <summary>创建一个逻辑大小很大的稀疏文件（真实磁盘占用极小）。</summary>
    public string CreateSparseFile(string relativePath, long sizeBytes)
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        SparseFileWriter.Create(path, sizeBytes);
        return path;
    }

    /// <summary>创建一个内容很小的普通文件。</summary>
    public string CreateFile(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 清理失败不应导致测试失败。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }
}
