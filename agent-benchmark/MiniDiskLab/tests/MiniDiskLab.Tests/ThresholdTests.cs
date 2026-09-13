using MiniDiskLab.Core.Classifiers;
using MiniDiskLab.Core.Models;
using MiniDiskLab.Core.Services;
using MiniDiskLab.Core.Utilities;

namespace MiniDiskLab.Tests;

/// <summary>
/// 阈值判断的测试 —— 任务文档要求明确覆盖 499/500/501 MB 边界。
/// </summary>
public class ThresholdTests
{
    private static readonly SizeThreshold FiveHundredMb = new(500, SizeUnit.MB);

    [Fact]
    public void Threshold_500Mb_InBytes()
    {
        Assert.Equal(524_288_000L, FiveHundredMb.ToBytes());
    }

    // ==========================================================
    //  核心边界：499 / 500 / 501 MB
    // ==========================================================

    [Fact]
    public void FileAt499Mb_IsExcluded()
    {
        using var fixture = new ScanFixture();
        var path = fixture.CreateSparseFile("boundary_499MB.bin", 499L * 1024 * 1024);

        var summary = new DiskScanner().Scan(fixture.Root, FiveHundredMb);

        Assert.DoesNotContain(summary.Items, i => i.FullPath == path);
        Assert.Empty(summary.Items);
    }

    [Fact]
    public void FileAt500Mb_IsIncluded()
    {
        using var fixture = new ScanFixture();
        var path = fixture.CreateSparseFile("boundary_500MB.bin", 500L * 1024 * 1024);

        var summary = new DiskScanner().Scan(fixture.Root, FiveHundredMb);

        Assert.Single(summary.Items);
        Assert.Equal(path, summary.Items[0].FullPath);
    }

    [Fact]
    public void FileAt501Mb_IsIncluded()
    {
        using var fixture = new ScanFixture();
        var path = fixture.CreateSparseFile("boundary_501MB.bin", 501L * 1024 * 1024);

        var summary = new DiskScanner().Scan(fixture.Root, FiveHundredMb);

        Assert.Single(summary.Items);
        Assert.Equal(path, summary.Items[0].FullPath);
    }

    [Fact]
    public void AllThreeBoundaries_OnlyTwoAreReturned()
    {
        using var fixture = new ScanFixture();
        fixture.CreateSparseFile("b499.bin", 499L * 1024 * 1024);
        fixture.CreateSparseFile("b500.bin", 500L * 1024 * 1024);
        fixture.CreateSparseFile("b501.bin", 501L * 1024 * 1024);

        var summary = new DiskScanner().Scan(fixture.Root, FiveHundredMb);

        Assert.Equal(2, summary.Items.Count);
        Assert.DoesNotContain(summary.Items, i => i.FileName == "b499.bin");
        Assert.Contains(summary.Items, i => i.FileName == "b500.bin");
        Assert.Contains(summary.Items, i => i.FileName == "b501.bin");
    }

    // ==========================================================
    //  其它阈值
    // ==========================================================

    [Fact]
    public void OneByteBelowThreshold_IsExcluded()
    {
        using var fixture = new ScanFixture();
        fixture.CreateSparseFile("just_under.bin", 500L * 1024 * 1024 - 1);

        var summary = new DiskScanner().Scan(fixture.Root, FiveHundredMb);

        Assert.Empty(summary.Items);
    }

    [Fact]
    public void OneByteAboveThreshold_IsIncluded()
    {
        using var fixture = new ScanFixture();
        fixture.CreateSparseFile("just_over.bin", 500L * 1024 * 1024 + 1);

        var summary = new DiskScanner().Scan(fixture.Root, FiveHundredMb);

        Assert.Single(summary.Items);
    }

    [Fact]
    public void ZeroThreshold_IncludesEveryFile()
    {
        using var fixture = new ScanFixture();
        fixture.CreateFile("small1.txt", "abc");
        fixture.CreateFile("small2.txt", "def");

        var summary = new DiskScanner().Scan(fixture.Root, new SizeThreshold(0, SizeUnit.KB));

        Assert.Equal(2, summary.Items.Count);
    }

    [Fact]
    public void GigabyteThreshold_Works()
    {
        using var fixture = new ScanFixture();
        fixture.CreateSparseFile("big.bin", 2L * 1024 * 1024 * 1024);
        fixture.CreateSparseFile("small.bin", 900L * 1024 * 1024);

        var summary = new DiskScanner().Scan(fixture.Root, new SizeThreshold(1, SizeUnit.GB));

        Assert.Single(summary.Items);
        Assert.Equal("big.bin", summary.Items[0].FileName);
    }

    [Fact]
    public void HugeThreshold_ReturnsNothing()
    {
        using var fixture = new ScanFixture();
        fixture.CreateSparseFile("a.bin", 600L * 1024 * 1024);

        var summary = new DiskScanner().Scan(fixture.Root, new SizeThreshold(99, SizeUnit.GB));

        Assert.Empty(summary.Items);
        Assert.True(summary.ScannedFiles > 0, "扫描本身仍应执行");
    }

    [Fact]
    public void ThresholdToString_IsHumanReadable()
    {
        Assert.Equal("500 MB", new SizeThreshold(500, SizeUnit.MB).ToString());
        Assert.Equal("1.5 GB", new SizeThreshold(1.5, SizeUnit.GB).ToString());
        Assert.Equal("0.5 KB", new SizeThreshold(0.5, SizeUnit.KB).ToString());
    }
}
