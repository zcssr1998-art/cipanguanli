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
            var installed = Path.Combine(root, "Installed", "DemoApp");
            var residue = Path.Combine(root, "AppData", "Local", "OldTool", "Cache");
            var growth = Path.Combine(root, "Growth");
            var migrationTarget = Path.Combine(root, "Migrated");
            Directory.CreateDirectory(tencent);
            Directory.CreateDirectory(maya);
            Directory.CreateDirectory(duplicates);
            Directory.CreateDirectory(installed);
            Directory.CreateDirectory(residue);
            Directory.CreateDirectory(growth);
            Directory.CreateDirectory(migrationTarget);

            var movie = Path.Combine(tencent, "movie.mp4");
            var hero = Path.Combine(maya, "hero.mb");
            CreateSizedFile(movie, 2 * 1024 * 1024);
            CreateSizedFile(hero, 3 * 1024 * 1024);
            File.SetLastWriteTime(movie, DateTime.Now.AddDays(-400));
            CreateSizedFile(Path.Combine(root, "small.txt"), 64 * 1024);
            CreateSizedFile(Path.Combine(installed, "demo.bin"), 2 * 1024 * 1024);
            var residueFile = Path.Combine(residue, "cache.dat");
            CreateSizedFile(residueFile, 2 * 1024 * 1024);
            File.SetLastWriteTime(residueFile, DateTime.Now.AddDays(-300));
            Directory.SetLastWriteTime(residue, DateTime.Now.AddDays(-300));

            var similarA = Path.Combine(tencent, "trailer_copy_1080p.mp4");
            var similarB = Path.Combine(tencent, "trailer_4k_2.mp4");
            CreatePatternFile(similarA, 1300 * 1024, 0x11);
            CreatePatternFile(similarB, 1400 * 1024, 0x22);

            var dupA = Path.Combine(duplicates, "same-a.bin");
            var dupB = Path.Combine(duplicates, "same-b.bin");
            CreatePatternFile(dupA, 1200 * 1024, 0x5A);
            File.Copy(dupA, dupB);

            var growthFile = Path.Combine(growth, "growing.bin");
            CreateSizedFile(growthFile, 70L * 1024 * 1024);

            var scanner = new DiskScanner();
            var result = await scanner.ScanAsync([root], 1 * 1024 * 1024, 180, 1 * 1024 * 1024);
            var duplicateResult = await new DuplicateFinder().FindAsync([root], 1 * 1024 * 1024);

            var products = new[] { new InstalledProduct("Demo App", "Test", installed, "demo-uninstall", "SelfTest") };
            var ownership = new SoftwareOwnershipService(products);
            var rulesPath = Path.Combine(root, "rules.json");
            var rules = new LearnedRuleStore(rulesPath);
            rules.SaveRule(maya, "我的角色工程", "自测规则");
            var diagnostics = DiagnosticAnalyzer.Analyze(result, ownership, rules);
            var residues = ResidueDetector.Find(result, ownership, 1 * 1024 * 1024);
            var plans = CleanupPlanner.Build(result, duplicateResult, residues);
            var similar = SimilarMediaFinder.Find(result.LargeFiles);

            var historyPath = Path.Combine(root, "history.json");
            var history = new HistoryService(historyPath);
            var firstGrowth = history.CompareAndSave(result);
            CreateSizedFile(growthFile, 150L * 1024 * 1024);
            var result2 = await scanner.ScanAsync([root], 1 * 1024 * 1024, 180, 1 * 1024 * 1024);
            var secondGrowth = history.CompareAndSave(result2);

            var migrateSource = Path.Combine(root, "move-me.txt");
            File.WriteAllText(migrateSource, "migration-self-test");
            var migration = await MigrationService.MigrateAsync(migrateSource, migrationTarget);

            var quarantineSource = Path.Combine(root, "quarantine-me.txt");
            File.WriteAllText(quarantineSource, "quarantine-self-test");
            var quarantineService = new QuarantineService(Path.Combine(root, "QuarantineStore"));
            var quarantined = await quarantineService.QuarantineAsync(quarantineSource);
            var quarantineListed = quarantineService.List().Any(x => x.Id == quarantined.Id) && !File.Exists(quarantineSource);
            await quarantineService.RestoreAsync(quarantined);
            var quarantineRestored = File.Exists(quarantineSource) && quarantineService.List().All(x => x.Id != quarantined.Id);

            var checks = new Dictionary<string, bool>
            {
                ["found_large_files"] = result.LargeFiles.Count >= 7,
                ["sorted_descending"] = result.LargeFiles.Count > 1 && result.LargeFiles[0].SizeBytes >= result.LargeFiles[1].SizeBytes,
                ["tencent_classified"] = result.LargeFiles.Any(f => f.Category.Contains("腾讯视频", StringComparison.Ordinal)),
                ["three_d_classified"] = result.LargeFiles.Any(f => f.Category.Contains("3D", StringComparison.Ordinal)),
                ["old_file_detected"] = result.OldFiles.Any(f => f.Path.Equals(movie, StringComparison.OrdinalIgnoreCase)),
                ["space_map_created"] = result.FolderMapRoots.Count > 0,
                ["duplicate_detected"] = duplicateResult.Any(g => g.Paths.Count == 2 && g.ReclaimableBytes >= 1200 * 1024),
                ["migration_completed"] = migration.SourceRemoved && !File.Exists(migrateSource) && File.Exists(migration.DestinationPath),
                ["ownership_detected"] = diagnostics.Any(d => d.Owner == "Demo App" && d.Path.Contains("demo.bin", StringComparison.OrdinalIgnoreCase)),
                ["learned_rule_applied"] = diagnostics.Any(d => d.Owner.Contains("我的角色工程", StringComparison.Ordinal)),
                ["residue_candidate_detected"] = residues.Any(r => r.Path.Contains("OldTool", StringComparison.OrdinalIgnoreCase)),
                ["cleanup_plans_created"] = plans.Count == 3 && plans[0].Name == "保守清理",
                ["similar_media_candidate"] = similar.Any(g => g.Count >= 2 && g.FirstPath.Contains("trailer", StringComparison.OrdinalIgnoreCase)),
                ["history_first_empty"] = firstGrowth.Count == 0,
                ["growth_detected"] = secondGrowth.Any(g => g.Path.Contains("Growth", StringComparison.OrdinalIgnoreCase) && g.GrowthBytes >= 64L * 1024 * 1024),
                ["quarantine_listed"] = quarantineListed,
                ["quarantine_restored"] = quarantineRestored
            };

            var payload = new
            {
                passed = checks.Values.All(v => v),
                checks,
                filesSeen = result2.FilesSeen,
                diagnosticOwners = diagnostics.Take(8).Select(x => new { x.Path, x.Owner, x.SafetyScore, x.RecommendedAction }).ToArray(),
                residues = residues.Take(8).Select(x => new { x.Path, x.ConfidenceScore }).ToArray(),
                cleanupPlans = plans.Select(x => new { x.Name, x.EstimatedBytes, x.ItemCount }).ToArray(),
                growth = secondGrowth.Take(8).Select(x => new { x.Path, x.GrowthBytes }).ToArray(),
                duplicateGroups = duplicateResult.Select(g => new { g.Count, g.FileSizeBytes, g.ReclaimableBytes }).ToArray(),
                migration = new { migration.SourceRemoved, migration.DestinationPath, migration.BytesMoved },
                quarantine = new { quarantineListed, quarantineRestored }
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
