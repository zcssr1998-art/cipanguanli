using System.Text;

namespace MiniDiskLab.Core.Utilities;

/// <summary>
/// 用于从应用（和测试）中创建可预测测试数据的辅助方法。
/// </summary>
public static class TestDataFactory
{
    /// <summary>
    /// 与任务文档相匹配的文件布局定义。
    /// </summary>
    public static IReadOnlyList<TestDataFile> DefaultLayout { get; } = new[]
    {
        new TestDataFile("Tencent Video/Download/movie.mp4", 620L * 1024 * 1024),
        new TestDataFile("Tencent Video/Cache/preview.mp4", 8L * 1024 * 1024),
        new TestDataFile("SteamLibrary/steamapps/common/TestGame/data.pak", 1_450L * 1024 * 1024),
        new TestDataFile("SteamLibrary/steamapps/common/TestGame/content.bin", 300L * 1024 * 1024),
        new TestDataFile("3DProjects/Hero/maya/body.ma", 512L * 1024 * 1024),
        new TestDataFile("3DProjects/Hero/blender/hero.blend", 550L * 1024 * 1024),
        new TestDataFile("3DProjects/Hero/textures/hero_diffuse.png", 12L * 1024 * 1024),
        new TestDataFile("Downloads/windows.iso", 2_200L * 1024 * 1024),
        new TestDataFile("Downloads/setup.exe", 260L * 1024 * 1024),
        new TestDataFile("Adobe/Cache/cache.tmp", 700L * 1024 * 1024),
        new TestDataFile("Adobe/Media Cache/comp.pek", 480L * 1024 * 1024),
        new TestDataFile("UnrealProjects/Game/Content/Paks/pakchunk0.pak", 900L * 1024 * 1024),
        new TestDataFile("UnityProjects/MyGame/Library/artifacts/cache.asset", 150L * 1024 * 1024),
        new TestDataFile("Random/unknown.xyz", 640L * 1024 * 1024),
        new TestDataFile("Random/small.txt", 2048),
        new TestDataFile("Random/archive.zip", 780L * 1024 * 1024),
        new TestDataFile("Random/server.log", 1L * 1024 * 1024),
        new TestDataFile("Mixed/boundary_499MB.bin", 499L * 1024 * 1024),
        new TestDataFile("Mixed/boundary_500MB.bin", 500L * 1024 * 1024),
        new TestDataFile("Mixed/boundary_501MB.bin", 501L * 1024 * 1024),
    };

    /// <summary>
    /// 在 <paramref name="rootDirectory"/> 下物化布局。
    /// 超过 <paramref name="sparseThresholdBytes"/> 的文件会尽可能以稀疏文件形式创建。
    /// </summary>
    /// <param name="rootDirectory">目标根目录（通常为 <c>test_data</c>）。</param>
    /// <param name="layout">要创建的文件；为 null 时使用 <see cref="DefaultLayout"/>。</param>
    /// <param name="sparseThresholdBytes">超过该大小的文件会以稀疏文件形式创建。</param>
    /// <returns>一份包含实际创建文件的报告。</returns>
    public static TestDataReport Create(
        string rootDirectory,
        IReadOnlyList<TestDataFile>? layout = null,
        long sparseThresholdBytes = 1L * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        layout ??= DefaultLayout;
        Directory.CreateDirectory(rootDirectory);

        var createdPaths = new List<string>();
        long logicalTotal = 0;
        var sparseCount = 0;

        foreach (var file in layout)
        {
            var fullPath = Path.Combine(rootDirectory, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (file.SizeBytes >= sparseThresholdBytes)
            {
                if (SparseFileWriter.Create(fullPath, file.SizeBytes))
                {
                    sparseCount++;
                }
            }
            else
            {
                var payload = BuildSmallPayload(fullPath, file.SizeBytes);
                SparseFileWriter.CreateContent(fullPath, payload);
            }

            createdPaths.Add(fullPath);
            logicalTotal += file.SizeBytes;
        }

        return new TestDataReport
        {
            RootDirectory = rootDirectory,
            LogicalTotalBytes = logicalTotal,
            SparseFileCount = sparseCount,
            CreatedPaths = createdPaths,
        };
    }

    /// <summary>
    /// 创建一个空目录——用于测试空文件夹处理。
    /// </summary>
    public static string CreateEmptyDirectory(string parent, string name)
    {
        var path = Path.Combine(parent, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static byte[] BuildSmallPayload(string fullPath, long sizeBytes)
    {
        if (sizeBytes <= 0)
        {
            return Array.Empty<byte>();
        }

        // 保持小文件体积小、内容可读；向日志文件写入内容行。
        var text = new StringBuilder();
        var header = $"# MiniDiskLab test payload for {Path.GetFileName(fullPath)}\n";
        text.Append(header);
        var line = "MiniDiskLab test data line - safe to delete.\n";
        while (text.Length < sizeBytes)
        {
            text.Append(line);
        }

        var bytes = Encoding.UTF8.GetBytes(text.ToString());
        if (bytes.Length > sizeBytes)
        {
            Array.Resize(ref bytes, (int)sizeBytes);
        }

        return bytes;
    }
}

/// <summary>预期的测试文件。</summary>
/// <param name="RelativePath">相对于测试数据根目录的路径，使用正斜杠。</param>
/// <param name="SizeBytes">逻辑文件大小。</param>
public sealed record TestDataFile(string RelativePath, long SizeBytes);

/// <summary>物化测试数据的结果。</summary>
public sealed class TestDataReport
{
    /// <summary>测试数据根目录。</summary>
    public string RootDirectory { get; init; } = string.Empty;

    /// <summary>所有文件的逻辑字节数之和。</summary>
    public long LogicalTotalBytes { get; init; }

    /// <summary>真正以稀疏文件形式创建的文件数量。</summary>
    public int SparseFileCount { get; init; }

    /// <summary>已创建文件的完整路径。</summary>
    public IReadOnlyList<string> CreatedPaths { get; init; } = Array.Empty<string>();
}
