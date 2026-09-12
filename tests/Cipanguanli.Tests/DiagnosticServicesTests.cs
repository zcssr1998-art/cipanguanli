using Cipanguanli.Core;

namespace Cipanguanli.Tests;

public sealed class DiagnosticServicesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CipanguanliDiagTests_" + Guid.NewGuid().ToString("N"));

    public DiagnosticServicesTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void OwnershipUsesLongestInstallPrefix()
    {
        var products = new[]
        {
            new InstalledProduct("Parent", "Test", Path.Combine(_root, "Apps"), "", "Test"),
            new InstalledProduct("Specific Game", "Test", Path.Combine(_root, "Apps", "Game"), "steam://uninstall/1", "Steam")
        };
        var service = new SoftwareOwnershipService(products);
        var match = service.Resolve(Path.Combine(_root, "Apps", "Game", "Content", "movie.pak"));
        Assert.Equal("Specific Game", match.DisplayName);
        Assert.Equal("Steam", match.Source);
    }

    [Fact]
    public void LearnedRuleUsesLongestPrefix()
    {
        var store = new LearnedRuleStore(Path.Combine(_root, "rules.json"));
        store.SaveRule(Path.Combine(_root, "Projects"), "工程", "parent");
        store.SaveRule(Path.Combine(_root, "Projects", "Hero"), "角色工程", "specific");
        var rule = store.Resolve(Path.Combine(_root, "Projects", "Hero", "a.mb"));
        Assert.NotNull(rule);
        Assert.Equal("角色工程", rule!.Category);
    }

    [Fact]
    public void CleanupPlannerCreatesThreeLevels()
    {
        var scan = EmptyScan(
            cleanup: [new FolderSummary { Path = Path.Combine(_root, "Cache"), Category = "临时/缓存文件", Risk = "低-中", Note = "", SizeBytes = 1024, FileCount = 2, ReviewForCleanup = true }],
            old: [new OldFileEntry { Name = "old.zip", Path = Path.Combine(_root, "old.zip"), Category = "压缩包/归档", Risk = "低-中", Note = "", SizeBytes = 2048, LastModified = DateTime.Now.AddDays(-400) }]);
        var plans = CleanupPlanner.Build(scan);
        Assert.Equal(3, plans.Count);
        Assert.True(plans[1].EstimatedBytes >= plans[0].EstimatedBytes);
    }

    [Fact]
    public async Task QuarantineCanRestore()
    {
        var source = Path.Combine(_root, "data.txt");
        await File.WriteAllTextAsync(source, "hello");
        var service = new QuarantineService(Path.Combine(_root, "Q"));
        var item = await service.QuarantineAsync(source);
        Assert.False(File.Exists(source));
        Assert.Single(service.List());
        await service.RestoreAsync(item);
        Assert.True(File.Exists(source));
        Assert.Empty(service.List());
    }

    [Fact]
    public void SimilarMediaNormalizesCopiesAndResolution()
    {
        Assert.Equal(SimilarMediaFinder.NormalizeStem("trailer_copy_1080p"), SimilarMediaFinder.NormalizeStem("trailer_4k_2"));
    }

    private static ScanResult EmptyScan(IReadOnlyList<FolderSummary>? cleanup = null, IReadOnlyList<OldFileEntry>? old = null)
        => new()
        {
            LargeFiles = Array.Empty<LargeFileEntry>(),
            OldFiles = old ?? Array.Empty<OldFileEntry>(),
            LargeFolders = cleanup ?? Array.Empty<FolderSummary>(),
            CleanupCandidates = cleanup ?? Array.Empty<FolderSummary>(),
            FolderMapRoots = Array.Empty<FolderMapNode>(),
            FilesSeen = 0,
            BytesSeen = 0,
            SkippedDirectories = 0,
            Elapsed = TimeSpan.Zero
        };

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }
}
