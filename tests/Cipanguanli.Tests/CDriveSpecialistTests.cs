using Cipanguanli.Core;

namespace Cipanguanli.Tests;

public sealed class CDriveSpecialistTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CipanguanliCDriveTests_" + Guid.NewGuid().ToString("N"));

    public CDriveSpecialistTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void TargetPlannerPrefersSaferItems()
    {
        var findings = new[]
        {
            Finding("shader", 8L << 30, 94, "quarantine"),
            Finding("temp", 5L << 30, 92, "storage-settings"),
            Finding("old", 30L << 30, 68, "open-location"),
            Finding("hiber", 20L << 30, 58, "hibernate-options")
        };
        var plan = CDriveTargetPlanner.Build(findings, 10L << 30);
        Assert.True(plan.MeetsTarget);
        Assert.Contains(plan.Items, x => x.Id == "shader");
        Assert.Contains(plan.Items, x => x.Id == "temp");
        Assert.DoesNotContain(plan.Items, x => x.Id == "hiber");
    }

    [Fact]
    public void EmergencyPlannerExcludesManualReviewItems()
    {
        var findings = new[]
        {
            Finding("cache", 6L << 30, 95, "quarantine"),
            Finding("manual", 50L << 30, 96, "open-location"),
            Finding("system", 100L << 30, 100, "storage-settings", protectedPath: true)
        };
        var plan = CDriveTargetPlanner.Build(findings, 20L << 30, emergency: true);
        Assert.Single(plan.Items);
        Assert.Equal("cache", plan.Items[0].Id);
        Assert.False(plan.MeetsTarget);
    }

    [Fact]
    public void ProtectedItemIsNeverSelected()
    {
        var plan = CDriveTargetPlanner.Build(new[]
        {
            Finding("installer", 80L << 30, 100, "apps-settings", protectedPath: true),
            Finding("safe", 2L << 30, 90, "storage-settings")
        }, 50L << 30);
        Assert.DoesNotContain(plan.Items, x => x.Id == "installer");
        Assert.False(plan.MeetsTarget);
    }

    [Fact]
    public void CDriveHistoryReportsMeaningfulGrowth()
    {
        var historyPath = Path.Combine(_root, "history.json");
        var service = new CDriveHistoryService(historyPath);
        var path = Path.Combine(_root, "Cache");
        var first = service.CompareAndSave(new[] { ("Cache", path, 100L * 1024 * 1024) });
        Assert.Empty(first);
        var second = service.CompareAndSave(new[] { ("Cache", path, 300L * 1024 * 1024) });
        var growth = Assert.Single(second);
        Assert.Equal(200L * 1024 * 1024, growth.GrowthBytes);
    }

    private static CDriveFinding Finding(string id, long bytes, int score, string actionKey, bool protectedPath = false)
        => new()
        {
            Id = id,
            Name = id,
            Category = "test",
            Path = id,
            SizeBytes = bytes,
            ReclaimableBytes = protectedPath ? 0 : bytes,
            SafetyScore = score,
            Risk = "test",
            Detail = "",
            RecommendedAction = "",
            ActionKey = actionKey,
            CanQuarantine = actionKey == "quarantine",
            IsProtected = protectedPath
        };

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }
}
