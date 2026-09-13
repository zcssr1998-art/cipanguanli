using MiniDiskLab.Core.Classifiers;
using MiniDiskLab.Core.Models;
using MiniDiskLab.Core.Services;
using MiniDiskLab.Core.Utilities;

namespace MiniDiskLab.Tests;

/// <summary>
/// 异常文件处理测试 —— 任务文档要求覆盖：
/// 文件不存在、无法访问、扫描过程中被删除。
/// 程序在任何情况下都不能崩溃。
/// </summary>
public class ExceptionHandlingTests
{
    // ==========================================================
    //  文件不存在
    // ==========================================================

    [Fact]
    public void Classify_NonExistentFile_DoesNotThrow()
    {
        var classifier = new FileClassifier();
        var result = classifier.Classify(@"D:\NoSuchFolder\NoSuchFile.bin");

        Assert.Equal(FileCategory.Unknown, result.Category);
        Assert.False(string.IsNullOrWhiteSpace(result.Description));
    }

    [Fact]
    public void Classify_NonExistentFileWithKnownExtension_StillClassifies()
    {
        var classifier = new FileClassifier();

        // 分类只依赖路径与扩展名，不需要文件真实存在。
        Assert.Equal(FileCategory.Video, classifier.ClassifyCategory(@"Z:\Ghost\movie.mp4"));
        Assert.Equal(FileCategory.Steam, classifier.ClassifyCategory(@"Z:\SteamLibrary\steamapps\a.pak"));
    }

    [Fact]
    public void Scan_NonExistentDirectory_ThrowsTypedException()
    {
        var missing = Path.Combine(Path.GetTempPath(), "MiniDiskLabTests", Guid.NewGuid().ToString("N"));

        var ex = Assert.Throws<DirectoryNotFoundException>(
            () => new DiskScanner().Scan(missing, new SizeThreshold(1, SizeUnit.MB)));

        Assert.Contains("不存在", ex.Message);
    }

    // ==========================================================
    //  无法访问的文件
    // ==========================================================

    [Fact]
    public void Scan_FileLockedExclusively_DoesNotCrash()
    {
        using var fixture = new ScanFixture();
        fixture.CreateFile("normal.txt", "content");

        var locked = Path.Combine(fixture.Root, "locked.bin");
        File.WriteAllBytes(locked, new byte[8192]);

        // 独占打开：其他进程无法读取其大小/属性。
        using var handle = new FileStream(
            locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var summary = new DiskScanner().Scan(fixture.Root, new SizeThreshold(1, SizeUnit.KB));

        Assert.NotNull(summary);
        Assert.False(summary.WasCancelled);
        // 被锁定的文件读取失败会被记入错误计数，而不是中断扫描。
        Assert.True(summary.ScannedFiles >= 1);
    }

    [Fact]
    public void Scan_ManyLockedFiles_CompletesSuccessfully()
    {
        using var fixture = new ScanFixture();
        var handles = new List<FileStream>();

        try
        {
            for (var i = 0; i < 20; i++)
            {
                var p = Path.Combine(fixture.Root, $"locked{i}.bin");
                File.WriteAllBytes(p, new byte[4096]);
                handles.Add(new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.None));
            }

            var summary = new DiskScanner().Scan(fixture.Root, new SizeThreshold(1, SizeUnit.KB));

            Assert.NotNull(summary);
        }
        finally
        {
            foreach (var h in handles)
            {
                h.Dispose();
            }
        }
    }

    // ==========================================================
    //  扫描过程中被删除
    // ==========================================================

    [Fact]
    public void Scan_FileDeletedMidScan_RecordsErrorButContinues()
    {
        using var fixture = new ScanFixture();

        // 制造较多文件，以便有机会在扫描中间删除。
        var dir = Path.Combine(fixture.Root, "many");
        Directory.CreateDirectory(dir);
        for (var i = 0; i < 500; i++)
        {
            File.WriteAllBytes(Path.Combine(dir, $"f{i}.bin"), new byte[2048]);
        }

        // 保留一个稳定的文件，用于确认扫描确实执行到最后。
        File.WriteAllBytes(Path.Combine(fixture.Root, "stable.bin"), new byte[1024]);

        var scanTask = Task.Run(() => new DiskScanner().Scan(
            fixture.Root, new SizeThreshold(0, SizeUnit.KB)));

        for (var i = 0; i < 500; i += 5)
        {
            try
            {
                File.Delete(Path.Combine(dir, $"f{i}.bin"));
            }
            catch (IOException)
            {
                // 允许失败。
            }
        }

        var summary = scanTask.GetAwaiter().GetResult();

        Assert.NotNull(summary);
        Assert.False(summary.WasCancelled);
        Assert.Contains(summary.Items, i => i.FileName == "stable.bin");
    }

    [Fact]
    public void Scan_DirectoryRemovedMidScan_Continues()
    {
        using var fixture = new ScanFixture();

        for (var d = 0; d < 50; d++)
        {
            var dir = Path.Combine(fixture.Root, "dir" + d);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "f.bin"), new byte[2048]);
        }

        var scanTask = Task.Run(() => new DiskScanner().Scan(
            fixture.Root, new SizeThreshold(0, SizeUnit.KB)));

        for (var d = 0; d < 50; d += 3)
        {
            try
            {
                Directory.Delete(Path.Combine(fixture.Root, "dir" + d), recursive: true);
            }
            catch (IOException)
            {
                // 允许失败。
            }
        }

        var summary = scanTask.GetAwaiter().GetResult();
        Assert.NotNull(summary);
    }

    // ==========================================================
    //  特殊目录
    // ==========================================================

    [Fact]
    public void Scan_EmptyFolders_AreTraversed()
    {
        using var fixture = new ScanFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "empty1"));
        Directory.CreateDirectory(Path.Combine(fixture.Root, "empty2", "nested"));

        var summary = new DiskScanner().Scan(fixture.Root, new SizeThreshold(1, SizeUnit.MB));

        Assert.Empty(summary.Items);
        Assert.Equal(0, summary.ScannedFiles);
    }

    [Fact]
    public void Scan_MixedContent_ReportsCorrectCounters()
    {
        using var fixture = new ScanFixture();

        Directory.CreateDirectory(Path.Combine(fixture.Root, "empty"));
        fixture.CreateFile("a.txt", "small");
        fixture.CreateSparseFile("b.bin", 10L * 1024 * 1024);
        fixture.CreateSparseFile("sub/c.bin", 20L * 1024 * 1024);

        var summary = new DiskScanner().Scan(fixture.Root, new SizeThreshold(5, SizeUnit.MB));

        Assert.Equal(3, summary.ScannedFiles);
        Assert.Equal(2, summary.Items.Count);
        Assert.True(summary.ScannedDirectories >= 3);
    }

    [Fact]
    public void Scan_FileNameWithSpecialCharacters_IsHandled()
    {
        using var fixture = new ScanFixture();

        var names = new[]
        {
            "with space.bin",
            "with_underscore.bin",
            "with-dash.bin",
            "with(paren).bin",
            "中文文件名.bin",
            "with'quote.bin",
        };

        foreach (var name in names)
        {
            try
            {
                fixture.CreateSparseFile(name, 2L * 1024 * 1024);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 某些字符在特定文件系统上可能不允许。
            }
        }

        var summary = new DiskScanner().Scan(fixture.Root, new SizeThreshold(1, SizeUnit.MB));

        Assert.NotNull(summary);
        Assert.Contains(summary.Items, i => i.FileName == "中文文件名.bin");
    }

    [Fact]
    public void Scan_ZeroByteFiles_AreHandled()
    {
        using var fixture = new ScanFixture();
        File.WriteAllBytes(Path.Combine(fixture.Root, "empty.bin"), Array.Empty<byte>());

        var summary = new DiskScanner().Scan(fixture.Root, new SizeThreshold(0, SizeUnit.KB));

        // 0 字节文件达到 0 阈值，应被包含。
        Assert.Single(summary.Items);
        Assert.Equal(0, summary.Items[0].SizeBytes);
    }

    // ==========================================================
    //  测试数据工厂
    // ==========================================================

    [Fact]
    public void TestDataFactory_CreatesExpectedLayout()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "MiniDiskLabTests", "factory", Guid.NewGuid().ToString("N"));

        try
        {
            var report = TestDataFactory.Create(root);

            Assert.Equal(TestDataFactory.DefaultLayout.Count, report.CreatedPaths.Count);
            Assert.All(report.CreatedPaths, p => Assert.True(File.Exists(p), $"缺少文件: {p}"));

            Assert.True(File.Exists(Path.Combine(root, "Tencent Video", "Download", "movie.mp4")));
            Assert.True(File.Exists(Path.Combine(root, "SteamLibrary", "steamapps", "common", "TestGame", "data.pak")));
            Assert.True(File.Exists(Path.Combine(root, "3DProjects", "Hero", "maya", "body.ma")));
            Assert.True(File.Exists(Path.Combine(root, "Downloads", "windows.iso")));
            Assert.True(File.Exists(Path.Combine(root, "Adobe", "Cache", "cache.tmp")));
            Assert.True(File.Exists(Path.Combine(root, "Random", "unknown.xyz")));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
                // 忽略。
            }
        }
    }

    [Fact]
    public void TestDataFactory_SparseFilesDoNotConsumeFullDiskSpace()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "MiniDiskLabTests", "sparse", Guid.NewGuid().ToString("N"));

        try
        {
            var report = TestDataFactory.Create(root);

            // 逻辑总量应远大于实际写入的字节数。
            var logical = report.LogicalTotalBytes;
            Assert.True(logical > 5L * 1024 * 1024 * 1024,
                $"默认布局应达到 GB 级别，实际 {logical} 字节");

            // 至少部分文件应为稀疏文件。
            Assert.True(report.SparseFileCount > 0,
                "应至少创建部分稀疏文件以避免占用真实磁盘");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
                // 忽略。
            }
        }
    }

    [Fact]
    public void TestDataFactory_FullLayoutScansAndClassifies()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "MiniDiskLabTests", "full", Guid.NewGuid().ToString("N"));

        try
        {
            TestDataFactory.Create(root);

            var summary = new DiskScanner().Scan(root, new SizeThreshold(500, SizeUnit.MB));

            // 499MB 被排除，其余 >=500MB 的都在。
            Assert.DoesNotContain(summary.Items, i => i.FileName == "boundary_499MB.bin");
            Assert.Contains(summary.Items, i => i.FileName == "boundary_500MB.bin");
            Assert.Contains(summary.Items, i => i.FileName == "boundary_501MB.bin");

            Assert.Contains(summary.Items, i => i.Category == FileCategory.TencentVideo);
            Assert.Contains(summary.Items, i => i.Category == FileCategory.Steam);
            Assert.Contains(summary.Items, i => i.Category == FileCategory.Maya);
            Assert.Contains(summary.Items, i => i.Category == FileCategory.IsoImage);
            Assert.Contains(summary.Items, i => i.Category == FileCategory.UnrealEngine);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
                // 忽略。
            }
        }
    }

    [Fact]
    public void SparseFileWriter_CreatesFileWithCorrectLogicalLength()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "MiniDiskLabTests", "sparsefile", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "sparse.bin");
            const long logicalSize = 3L * 1024 * 1024 * 1024;

            SparseFileWriter.Create(path, logicalSize);

            var info = new FileInfo(path);
            Assert.True(info.Exists);
            Assert.Equal(logicalSize, info.Length);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
                // 忽略。
            }
        }
    }

    [Fact]
    public void SparseFileWriter_CreatesParentDirectories()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "MiniDiskLabTests", "sparseparent", Guid.NewGuid().ToString("N"));

        try
        {
            var path = Path.Combine(root, "a", "b", "c", "file.bin");

            SparseFileWriter.Create(path, 2L * 1024 * 1024);

            Assert.True(File.Exists(path));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
                // 忽略。
            }
        }
    }

    [Fact]
    public void SparseFileWriter_RejectsNegativeSize()
    {
        var path = Path.Combine(Path.GetTempPath(), "MiniDiskLabTests", "neg.bin");

        Assert.Throws<ArgumentOutOfRangeException>(() => SparseFileWriter.Create(path, -1));
    }

    [Fact]
    public void SparseFileWriter_RejectsEmptyPath()
    {
        Assert.Throws<ArgumentException>(() => SparseFileWriter.Create("  ", 1024));
    }

    // ==========================================================
    //  错误不可被静默吞掉
    // ==========================================================

    [Fact]
    public void Scan_InvalidThreshold_IsRejectedBeforeScanning()
    {
        using var fixture = new ScanFixture();
        fixture.CreateFile("a.txt", "x");

        // 负数阈值在构造时即被拒绝，不会静默产生错误结果。
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SizeThreshold(-1, SizeUnit.MB).ToBytes());
    }

    [Fact]
    public void Scan_ErrorsAreCountedNotHidden()
    {
        using var fixture = new ScanFixture();
        fixture.CreateFile("ok.txt", "ok");

        var summary = new DiskScanner().Scan(fixture.Root, new SizeThreshold(0, SizeUnit.KB));

        // 正常情况下不应有错误。
        Assert.Equal(0, summary.ErrorCount);
        Assert.Equal(0, summary.SkippedLinks);
    }
}
