using System.IO;
using System.Text.Json;

namespace Cipanguanli.Core;

internal static class SelfTest
{
    public static async Task<bool> RunAsync(string reportPath)
    {
        var root = Path.Combine(Path.GetTempPath(), "CipanguanliSelfTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var tencent = Path.Combine(root, "Tencent Video", "QLDownload");
            var maya = Path.Combine(root, "Projects", "Maya", "Hero");
            var duplicates = Path.Combine(root, "Duplicates");
            var migrationTarget = Path.Combine(root, "Migrated");
            Directory.CreateDirectory(tencent);
            Directory.CreateDirectory(maya);
            Directory.CreateDirectory(duplicates);
            Directory.CreateDirectory(migrationTarget);

            var movie = Path.Combine(tencent, "movie.mp4");
            var hero = Path.Combine(maya, "hero.mb");
            CreateSizedFile(movie, 2 * 1024 * 1024);
            CreateSizedFile(hero, 3 * 1024 * 1024);
            File.SetLastWriteTime(movie, DateTime.Now.AddDays(-400));
            CreateSizedFile(Path.Combine(root, "small.txt"), 64 * 1024);

            var dupA = Path.Combine(duplicates, "same-a.bin");
            var dupB = Path.Combine(duplicates, "same-b.bin");
            CreatePatternFile(dupA, 1200 * 1024, 0x5A);
            File.Copy(dupA, dupB);

            var scanner = new DiskScanner();
            var result = await scanner.ScanAsync([root], 1 * 1024 * 1024, 180, 1 * 1024 * 1024);
            var duplicateResult = await new DuplicateFinder().FindAsync([root], 1 * 1024 * 1024);

            var migrateSource = Path.Combine(root, "move-me.txt");
            File.WriteAllText(migrateSource, "migration-self-test");
            var migration = await MigrationService.MigrateAsync(migrateSource, migrationTarget);

            var checks = new Dictionary<string, bool>
            {
                ["found_large_files"] = result.LargeFiles.Count >= 4,
                ["sorted_descending"] = result.LargeFiles.Count > 1 && result.LargeFiles[0].SizeBytes >= result.LargeFiles[1].SizeBytes,
                ["tencent_classified"] = result.LargeFiles.Any(f => f.Category.Contains("腾讯视频", StringComparison.Ordinal)),
                ["three_d_classified"] = result.LargeFiles.Any(f => f.Category.Contains("3D", StringComparison.Ordinal)),
                ["old_file_detected"] = result.OldFiles.Any(f => f.Path.Equals(movie, StringComparison.OrdinalIgnoreCase)),
                ["folder_aggregation"] = result.LargeFolders.Any(f => f.Path.Contains("Tencent Video", StringComparison.OrdinalIgnoreCase) && f.SizeBytes >= 2 * 1024 * 1024),
                ["cleanup_candidate"] = result.CleanupCandidates.Any(f => f.Category.Contains("腾讯视频", StringComparison.Ordinal)),
                ["space_map_created"] = result.FolderMapRoots.Count > 0,
                ["duplicate_detected"] = duplicateResult.Any(g => g.Paths.Count == 2 && g.ReclaimableBytes >= 1200 * 1024),
                ["migration_completed"] = migration.SourceRemoved && !File.Exists(migrateSource) && File.Exists(migration.DestinationPath)
            };

            var payload = new
            {
                passed = checks.Values.All(v => v),
                checks,
                filesSeen = result.FilesSeen,
                largeFiles = result.LargeFiles.Take(10).Select(f => new { f.Name, f.Category, f.SizeBytes }).ToArray(),
                oldFiles = result.OldFiles.Take(10).Select(f => new { f.Name, f.DaysOld, f.SizeBytes }).ToArray(),
                duplicateGroups = duplicateResult.Select(g => new { g.Count, g.FileSizeBytes, g.ReclaimableBytes }).ToArray(),
                migration = new { migration.SourceRemoved, migration.DestinationPath, migration.BytesMoved }
            };
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            return checks.Values.All(v => v);
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            return false;
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void CreateSizedFile(string path, long bytes)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.SetLength(bytes);
    }

    private static void CreatePatternFile(string path, int bytes, byte value)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = Enumerable.Repeat(value, 64 * 1024).Select(x => (byte)x).ToArray();
        var remaining = bytes;
        while (remaining > 0)
        {
            var count = Math.Min(remaining, buffer.Length);
            stream.Write(buffer, 0, count);
            remaining -= count;
        }
    }
}
