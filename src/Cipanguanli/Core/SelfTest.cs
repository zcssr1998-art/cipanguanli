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
            Directory.CreateDirectory(tencent);
            Directory.CreateDirectory(maya);

            CreateSizedFile(Path.Combine(tencent, "movie.mp4"), 2 * 1024 * 1024);
            CreateSizedFile(Path.Combine(maya, "hero.mb"), 3 * 1024 * 1024);
            CreateSizedFile(Path.Combine(root, "small.txt"), 64 * 1024);

            var scanner = new DiskScanner();
            var result = await scanner.ScanAsync([root], 1 * 1024 * 1024);
            var checks = new Dictionary<string, bool>
            {
                ["found_two_large_files"] = result.LargeFiles.Count == 2,
                ["sorted_descending"] = result.LargeFiles.Count == 2 && result.LargeFiles[0].SizeBytes >= result.LargeFiles[1].SizeBytes,
                ["tencent_classified"] = result.LargeFiles.Any(f => f.Category.Contains("腾讯视频", StringComparison.Ordinal)),
                ["three_d_classified"] = result.LargeFiles.Any(f => f.Category.Contains("3D", StringComparison.Ordinal)),
                ["folder_aggregation"] = result.LargeFolders.Any(f => f.Path.Contains("Tencent Video", StringComparison.OrdinalIgnoreCase) && f.SizeBytes >= 2 * 1024 * 1024),
                ["cleanup_candidate"] = result.CleanupCandidates.Any(f => f.Category.Contains("腾讯视频", StringComparison.Ordinal))
            };

            var payload = new
            {
                passed = checks.Values.All(v => v),
                checks,
                filesSeen = result.FilesSeen,
                largeFiles = result.LargeFiles.Select(f => new { f.Name, f.Category, f.SizeBytes }).ToArray(),
                largeFolders = result.LargeFolders.Take(10).Select(f => new { f.Path, f.Category, f.SizeBytes }).ToArray()
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
}
