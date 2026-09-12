using Cipanguanli.Core;

namespace Cipanguanli.Tests;

public sealed class MigrationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CipanguanliMoveTests_" + Guid.NewGuid().ToString("N"));

    public MigrationServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task MovesFileAndPreservesContent()
    {
        var sourceDir = Path.Combine(_root, "source");
        var destinationDir = Path.Combine(_root, "destination");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destinationDir);
        var source = Path.Combine(sourceDir, "hello.txt");
        File.WriteAllText(source, "hello-migration");

        var result = await MigrationService.MigrateAsync(source, destinationDir);

        Assert.True(result.SourceRemoved);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(result.DestinationPath));
        Assert.Equal("hello-migration", File.ReadAllText(result.DestinationPath));
    }

    [Fact]
    public async Task RejectsDestinationInsideSourceDirectory()
    {
        var source = Path.Combine(_root, "folder");
        var inside = Path.Combine(source, "inside");
        Directory.CreateDirectory(inside);
        File.WriteAllText(Path.Combine(source, "x.txt"), "x");

        await Assert.ThrowsAsync<InvalidOperationException>(() => MigrationService.MigrateAsync(source, inside));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }
}
