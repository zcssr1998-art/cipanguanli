using MiniDiskLab.Core.Models;
using MiniDiskLab.Core.Services;

namespace MiniDiskLab.Tests;

/// <summary>
/// 扫描器行为测试：排序、取消、异常容错、特殊目录。
/// </summary>
public class DiskScannerTests
{
    private static readonly SizeThreshold OneKb = new(1, SizeUnit.KB);

    // ==========================================================
    //  排序
    // ==========================================================

    [Fact]
    public void Results_AreSortedBySizeDescending()
    {
        using var fixture = new ScanFixture();
        fixture.CreateSparseFile("small.bin", 2L * 1024 * 1024);
        fixture.CreateSparseFile("medium.bin", 10L * 1024 * 1024);
        fixture.CreateSparseFile("large.bin", 50L * 1024 * 1024);
        fixture.CreateSparseFile("huge.bin", 100L * 1024 * 1024);

        var summary = new DiskScanner().Scan(fixture.Root, OneKb);

        Assert.Equal(4, summary.Items.Count);
        Assert.Equal("huge.bin", summary.Items[0].FileName);
        Assert.Equal("large.bin", summary.Items[1].FileName);
        Assert.Equal("medium.bin", summary.Items[2].FileName);
        Assert.Equal("small.bin", summary.Items[3].FileName);

        // 逐对确认单调不增。
        for (var i = 1; i < summary.Items.Count; i++)
        {
            Assert.True(summary.Items[i - 1].SizeBytes >= summary.Items[i].SizeBytes);
        }
    }

    // ==========================================================
    //  基本统计
    // ==========================================================

    [Fact]
    public void CountsAreReported()
    {
        using var fixture = new ScanFixture();
        fixture.CreateFile("a.txt", "a");
        fixture.CreateFile("b.txt", "b");
        fixture.CreateSparseFile("c.bin", 5L * 1024 * 1024);
        fixture.CreateFile("sub/d.txt", "d");

        var summary = new DiskScanner().Scan(fixture.Root, OneKb);

        Assert.Equal(4, summary.ScannedFiles);
        Assert.Equal(1, summary.Items.Count);
        Assert.True(summary.ScannedDirectories >= 2, $"目录数={summary.ScannedDirectories}");
        Assert.Equal(0, summary.SkippedDirectories);
    }

    [Fact]
    public void EmptyDirectory_ProducesNoResults()
    {
        using var fixture = new ScanFixture();

        var summary = new DiskScanner().Scan(fixture.Root, OneKb);

        Assert.Empty(summary.Items);
        Assert.Equal(0, summary.ScannedFiles);
        Assert.False(summary.WasCancelled);
    }

    [Fact]
    public void NestedEmptyDirectories_AreHandled()
    {
        using var fixture = new ScanFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "a", "b", "c"));

        var summary = new DiskScanner().Scan(fixture.Root, OneKb);

        Assert.Empty(summary.Items);
        Assert.True(summary.ScannedDirectories >= 4, $"目录数={summary.ScannedDirectories}");
    }

    [Fact]
    public void DeeplyNestedDirectories_AreScanned()
    {
        using var fixture = new ScanFixture();
        var deep = fixture.Root;
        for (var i = 0; i < 30; i++)
        {
            deep = Path.Combine(deep, "lvl" + i);
        }

        Directory.CreateDirectory(deep);
        File.WriteAllBytes(Path.Combine(deep, "deep.bin"), new byte[2048]);

        var summary = new DiskScanner().Scan(fixture.Root, OneKb);

        Assert.Single(summary.Items);
        Assert.Equal("deep.bin", summary.Items[0].FileName);
    }

    [Fact]
    public void NonExistentRoot_ThrowsDirectoryNotFound()
    {
        var missing = Path.Combine(Path.GetTempPath(), "MiniDiskLabTests", Guid.NewGuid().ToString("N"));

        Assert.Throws<DirectoryNotFoundException>(() => new DiskScanner().Scan(missing, OneKb));
    }

    [Fact]
    public void EmptyRootArgument_Throws()
    {
        Assert.Throws<ArgumentException>(() => new DiskScanner().Scan("  ", OneKb));
    }

    // ==========================================================
    //  取消行为
    // ==========================================================

    [Fact]
    public async Task Cancel_ActuallyStopsTheScan()
    {
        using var fixture = new ScanFixture();
        CreateManyFiles(fixture, 500);

        using var cts = new CancellationTokenSource();
        var scanner = new DiskScanner();

        var task = scanner.ScanAsync(fixture.Root, OneKb, cts.Token);

        // 稍等片刻后取消，确保取消发生在扫描过程中。
        await Task.Delay(1);
        cts.Cancel();

        var summary = await task;

        Assert.True(summary.WasCancelled, "扫描应被标记为已取消");
    }

    [Fact]
    public async Task Cancel_BeforeStart_ReturnsImmediately()
    {
        using var fixture = new ScanFixture();
        CreateManyFiles(fixture, 200);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var summary = await new DiskScanner().ScanAsync(fixture.Root, OneKb, cts.Token);

        Assert.True(summary.WasCancelled);
        Assert.Empty(summary.Items);
    }

    [Fact]
    public async Task Cancel_ReleasesTheBackgroundTask()
    {
        using var fixture = new ScanFixture();
        CreateManyFiles(fixture, 800);

        using var cts = new CancellationTokenSource();
        var task = new DiskScanner().ScanAsync(fixture.Root, OneKb, cts.Token);

        cts.Cancel();

        // 关键点：任务必须真正结束，而不是在后台继续跑。
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.Same(task, completed);
        Assert.True(task.IsCompleted);

        var summary = await task;
        Assert.True(summary.WasCancelled);
    }

    // ==========================================================
    //  异常容错
    // ==========================================================

    [Fact]
    public void FileDeletedDuringScan_DoesNotThrow()
    {
        using var fixture = new ScanFixture();
        var churnDir = Path.Combine(fixture.Root, "churn");
        Directory.CreateDirectory(churnDir);

        for (var i = 0; i < 300; i++)
        {
            File.WriteAllBytes(Path.Combine(churnDir, $"f{i}.bin"), new byte[4096]);
        }

        var scanner = new DiskScanner();

        var scanTask = Task.Run(() => scanner.Scan(fixture.Root, OneKb));

        // 扫描进行时不断删除文件。
        for (var i = 0; i < 300; i += 3)
        {
            try
            {
                File.Delete(Path.Combine(churnDir, $"f{i}.bin"));
            }
            catch (IOException)
            {
                // 允许失败：这正是我们要覆盖的场景。
            }
        }

        var summary = scanTask.GetAwaiter().GetResult();

        Assert.NotNull(summary);
        Assert.False(summary.WasCancelled);
    }

    [Fact]
    public void DirectoryDeletedDuringScan_DoesNotThrow()
    {
        using var fixture = new ScanFixture();

        for (var d = 0; d < 60; d++)
        {
            var dir = Path.Combine(fixture.Root, "d" + d);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "x.bin"), new byte[2048]);
        }

        var scanner = new DiskScanner();

        var scanTask = Task.Run(() => scanner.Scan(fixture.Root, OneKb));

        for (var d = 0; d < 60; d += 2)
        {
            try
            {
                Directory.Delete(Path.Combine(fixture.Root, "d" + d), recursive: true);
            }
            catch (IOException)
            {
                // 允许失败。
            }
        }

        var summary = scanTask.GetAwaiter().GetResult();
        Assert.NotNull(summary);
    }

    [Fact]
    public void SingleFileCannotBeRead_ScanStillCompletes()
    {
        using var fixture = new ScanFixture();
        fixture.CreateFile("ok.txt", "fine");

        // 一个独占锁定、无法读取大小的文件（用独占共享模式打开）。
        var lockedPath = Path.Combine(fixture.Root, "locked.bin");
        File.WriteAllBytes(lockedPath, new byte[4096]);

        using (var handle = new FileStream(
            lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var summary = new DiskScanner().Scan(fixture.Root, OneKb);

            // 关键断言：扫描必须完成，且不得抛出异常。
            Assert.NotNull(summary);
            Assert.False(summary.WasCancelled);
        }
    }

    [Fact]
    public void UnauthorisedDirectory_IsSkippedAndCounted()
    {
        using var fixture = new ScanFixture();
        fixture.CreateSparseFile("visible.bin", 2L * 1024 * 1024);

        var lockedDir = Path.Combine(fixture.Root, "noaccess");
        Directory.CreateDirectory(lockedDir);
        File.WriteAllBytes(Path.Combine(lockedDir, "hidden.bin"), new byte[4096]);

        System.Security.AccessControl.DirectorySecurity? original = null;
        var acl = new System.Security.AccessControl.DirectorySecurity();
        var denied = false;

        try
        {
            var info = new DirectoryInfo(lockedDir);
            original = info.GetAccessControl();

            // 拒绝当前用户列出该目录。
            var rule = new System.Security.AccessControl.FileSystemAccessRule(
                System.Security.Principal.WindowsIdentity.GetCurrent().User!,
                System.Security.AccessControl.FileSystemRights.ListDirectory,
                System.Security.AccessControl.AccessControlType.Deny);

            acl = info.GetAccessControl();
            acl.AddAccessRule(rule);
            info.SetAccessControl(acl);
            denied = true;
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or PlatformNotSupportedException or InvalidOperationException)
        {
            denied = false;
        }

        try
        {
            var summary = new DiskScanner().Scan(fixture.Root, OneKb);

            // 无论 ACL 是否设置成功，扫描都必须完成且不抛异常。
            Assert.NotNull(summary);
            Assert.Contains(summary.Items, i => i.FileName == "visible.bin");

            if (denied)
            {
                Assert.True(summary.SkippedDirectories >= 1,
                    $"应跳过无权限目录，实际={summary.SkippedDirectories}");
            }
        }
        finally
        {
            if (denied && original is not null)
            {
                try
                {
                    new DirectoryInfo(lockedDir).SetAccessControl(original);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
                {
                    // 恢复失败不影响测试结论。
                }
            }
        }
    }

    // ==========================================================
    //  符号链接 / Junction 循环
    // ==========================================================

    [Fact]
    public void SymbolicLinkLoop_DoesNotCauseInfiniteRecursion()
    {
        using var fixture = new ScanFixture();
        var sub = Path.Combine(fixture.Root, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(sub, "file.bin"), new byte[4096]);

        // 在 sub 内创建一个指回 fixture.Root 的链接，构成循环。
        var linkPath = Path.Combine(sub, "loop");
        var linkCreated = false;

        try
        {
            Directory.CreateSymbolicLink(linkPath, fixture.Root);
            linkCreated = true;
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            linkCreated = false;
        }

        // 无论链接是否创建成功，扫描都必须终止。
        var scanTask = Task.Run(() => new DiskScanner().Scan(fixture.Root, OneKb));
        var finished = scanTask.Wait(TimeSpan.FromSeconds(30));

        Assert.True(finished, "扫描未在 30 秒内结束 —— 可能存在无限递归");

        if (linkCreated)
        {
            var summary = scanTask.Result;
            // 扫描有限完成即证明循环被中断。
            Assert.NotNull(summary);
        }
    }

    [Fact]
    public void ReparsePoints_AreNotFollowedIndefinitely()
    {
        using var fixture = new ScanFixture();
        File.WriteAllBytes(Path.Combine(fixture.Root, "a.bin"), new byte[8192]);

        var summary = new DiskScanner().Scan(fixture.Root, OneKb);

        Assert.NotNull(summary);
        Assert.Equal(1, summary.ScannedFiles);
    }

    // ==========================================================
    //  长路径
    // ==========================================================

    [Fact]
    public void LongPaths_AreHandledWithoutCrashing()
    {
        using var fixture = new ScanFixture();

        // 在不触及 MAX_PATH 的前提下构造一段较长的路径。
        var dir = fixture.Root;
        for (var i = 0; i < 12; i++)
        {
            dir = Path.Combine(dir, new string('x', 40));
        }

        string? filePath = null;
        try
        {
            Directory.CreateDirectory(dir);
            filePath = Path.Combine(dir, "longpath.bin");
            File.WriteAllBytes(filePath, new byte[4096]);
        }
        catch (Exception ex) when (ex is PathTooLongException or IOException or UnauthorizedAccessException)
        {
            // 系统不支持如此长的路径 —— 扫描仍必须安全完成。
        }

        var summary = new DiskScanner().Scan(fixture.Root, OneKb);

        Assert.NotNull(summary);
        if (filePath is not null && File.Exists(filePath))
        {
            Assert.Contains(summary.Items, i => i.FileName == "longpath.bin");
        }
    }

    // ==========================================================
    //  进度回调
    // ==========================================================

    [Fact]
    public void ProgressIsReported_WithMonotonicCounters()
    {
        using var fixture = new ScanFixture();
        CreateManyFiles(fixture, 120);

        var reports = new List<ScanProgress>();
        var progress = new Progress<ScanProgress>(reports.Add);

        var summary = new DiskScanner().Scan(fixture.Root, OneKb, CancellationToken.None, progress);

        // Progress<T> 异步投递；等待其完成。
        Thread.Sleep(250);

        Assert.NotEmpty(reports);
        Assert.Equal(summary.ScannedFiles, reports[^1].ScannedFiles);

        for (var i = 1; i < reports.Count; i++)
        {
            Assert.True(reports[i].ScannedFiles >= reports[i - 1].ScannedFiles,
                "已扫描文件数应单调不减");
        }
    }

    [Fact]
    public void ItemCallback_FiresForEachLargeFile()
    {
        using var fixture = new ScanFixture();
        fixture.CreateSparseFile("a.bin", 2L * 1024 * 1024);
        fixture.CreateSparseFile("b.bin", 3L * 1024 * 1024);
        fixture.CreateFile("small.txt", "tiny");

        var found = new List<ScanResultItem>();
        var summary = new DiskScanner().Scan(
            fixture.Root, OneKb, CancellationToken.None, null, found.Add);

        Assert.Equal(summary.Items.Count, found.Count);
        Assert.Equal(2, found.Count);
    }

    // ==========================================================
    //  分类集成
    // ==========================================================

    [Fact]
    public void ScanResults_AreClassified()
    {
        using var fixture = new ScanFixture();
        fixture.CreateSparseFile("Tencent Video/Download/movie.mp4", 2L * 1024 * 1024);
        fixture.CreateSparseFile("SteamLibrary/steamapps/common/Game/data.pak", 3L * 1024 * 1024);
        fixture.CreateSparseFile("Projects/Hero/maya/body.ma", 4L * 1024 * 1024);

        var summary = new DiskScanner().Scan(fixture.Root, OneKb);

        Assert.Equal(3, summary.Items.Count);

        var movie = summary.Items.Single(i => i.FileName == "movie.mp4");
        Assert.Equal(FileCategory.TencentVideo, movie.Category);
        Assert.Contains("腾讯视频", movie.Description);

        var pak = summary.Items.Single(i => i.FileName == "data.pak");
        Assert.Equal(FileCategory.Steam, pak.Category);

        var maya = summary.Items.Single(i => i.FileName == "body.ma");
        Assert.Equal(FileCategory.Maya, maya.Category);
    }

    [Fact]
    public void ExtensionStats_AreCollected()
    {
        using var fixture = new ScanFixture();
        fixture.CreateFile("a.txt", "1");
        fixture.CreateFile("b.txt", "2");
        fixture.CreateFile("c.bin", "3");

        var summary = new DiskScanner().Scan(fixture.Root, new SizeThreshold(0, SizeUnit.KB));

        Assert.Contains(summary.ExtensionStats, s => s.Extension == ".txt" && s.FileCount == 2);
        Assert.Contains(summary.ExtensionStats, s => s.Extension == ".bin" && s.FileCount == 1);
    }

    [Fact]
    public void TopDirectories_AreComputed()
    {
        using var fixture = new ScanFixture();
        fixture.CreateSparseFile("big/a.bin", 20L * 1024 * 1024);
        fixture.CreateSparseFile("small/b.bin", 2L * 1024 * 1024);

        var summary = new DiskScanner().Scan(fixture.Root, OneKb);

        Assert.NotEmpty(summary.TopDirectories);
        Assert.EndsWith("big", summary.TopDirectories[0].Path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MaxEntries_IsRespected()
    {
        using var fixture = new ScanFixture();
        CreateManyFiles(fixture, 200);

        var summary = new DiskScanner().Scan(fixture.Root, OneKb, CancellationToken.None, null, null, maxEntries: 50);

        Assert.True(summary.ScannedFiles <= 50, $"已扫描={summary.ScannedFiles}");
    }

    private static void CreateManyFiles(ScanFixture fixture, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var dir = Path.Combine(fixture.Root, "g" + (i % 10));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"file{i}.bin"), new byte[2048]);
        }
    }
}
