using Cipanguanli.Core;

namespace Cipanguanli.Tests;

public sealed class DiskScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CipanguanliTests_" + Guid.NewGuid().ToString("N"));

    public DiskScannerTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task FindsThresholdFilesOldFilesAndBuildsMap()
    {
        var tencent = Path.Combine(_root, "Tencent Video", "QLDownload");
        var maya = Path.Combine(_root, "Projects", "Maya");
        Directory.CreateDirectory(tencent);
        Directory.CreateDirectory(maya);
        var movie = Path.Combine(tencent, "movie.mp4");
        CreateFile(movie, 6 * 1024 * 1024);
        File.SetLastWriteTime(movie, DateTime.Now.AddDays(-400));
        CreateFile(Path.Combine(maya, "hero.mb"), 8 * 1024 * 1024);
        CreateFile(Path.Combine(_root, "small.txt"), 32 * 1024);

        var result = await new DiskScanner().ScanAsync([_root], 5 * 1024 * 1024, 180, 5 * 1024 * 1024);

        Assert.Equal(2, result.LargeFiles.Count);
        Assert.True(result.LargeFiles[0].SizeBytes >= result.LargeFiles[1].SizeBytes);
        Assert.Contains(result.LargeFiles, f => f.Category.Contains("腾讯视频"));
        Assert.Contains(result.LargeFiles, f => f.Category.Contains("3D"));
        Assert.Contains(result.OldFiles, f => f.Path.Equals(movie, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.CleanupCandidates, f => f.Category.Contains("腾讯视频"));
        Assert.NotEmpty(result.FolderMapRoots);
    }

    [Fact]
    public async Task AggregatesFolderSize()
    {
        var folder = Path.Combine(_root, "Movies");
        Directory.CreateDirectory(folder);
        CreateFile(Path.Combine(folder, "a.mp4"), 3 * 1024 * 1024);
        CreateFile(Path.Combine(folder, "b.mp4"), 3 * 1024 * 1024);

        var result = await new DiskScanner().ScanAsync([_root], 2 * 1024 * 1024);
        var summary = Assert.Single(result.LargeFolders, f => f.Path.Equals(folder, StringComparison.OrdinalIgnoreCase));
        Assert.True(summary.SizeBytes >= 6 * 1024 * 1024);
        Assert.Contains("视频", summary.Category);
    }

    private static void CreateFile(string path, long length)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.SetLength(length);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }
}
