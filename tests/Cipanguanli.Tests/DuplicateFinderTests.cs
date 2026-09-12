using Cipanguanli.Core;

namespace Cipanguanli.Tests;

public sealed class DuplicateFinderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CipanguanliDupTests_" + Guid.NewGuid().ToString("N"));

    public DuplicateFinderTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task FindsOnlyByteIdenticalFiles()
    {
        var a = Path.Combine(_root, "a.bin");
        var b = Path.Combine(_root, "b.bin");
        var c = Path.Combine(_root, "c.bin");
        File.WriteAllBytes(a, Enumerable.Repeat((byte)0x2A, 128 * 1024).ToArray());
        File.Copy(a, b);
        File.WriteAllBytes(c, Enumerable.Repeat((byte)0x2B, 128 * 1024).ToArray());

        var groups = await new DuplicateFinder().FindAsync([_root], 64 * 1024);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Count);
        Assert.Equal(128 * 1024, group.ReclaimableBytes);
        Assert.Contains(a, group.Paths);
        Assert.Contains(b, group.Paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }
}
