using System.IO;
using System.Text.RegularExpressions;

namespace Cipanguanli.Core;

public static class SimilarMediaFinder
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".m4v",
        ".jpg", ".jpeg", ".png", ".webp", ".tif", ".tiff"
    };

    public static IReadOnlyList<SimilarMediaGroup> Find(IEnumerable<LargeFileEntry> files)
    {
        var media = files.Where(f => MediaExtensions.Contains(Path.GetExtension(f.Path))).ToArray();
        return media
            .GroupBy(f => NormalizeStem(Path.GetFileNameWithoutExtension(f.Path)), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1)
            .SelectMany(SplitBySize)
            .Where(g => g.Count > 1)
            .Select(g => new SimilarMediaGroup
            {
                Key = NormalizeStem(Path.GetFileNameWithoutExtension(g[0].Path)),
                Paths = g.Select(x => x.Path).ToArray(),
                TotalBytes = g.Sum(x => x.SizeBytes),
                Reason = "文件名去掉 copy/副本/分辨率/序号等后相近，且体积接近。这里只是相似候选，不代表内容完全相同。"
            })
            .OrderByDescending(g => g.TotalBytes)
            .ToArray();
    }

    private static IEnumerable<List<LargeFileEntry>> SplitBySize(IGrouping<string, LargeFileEntry> group)
    {
        var remaining = group.OrderBy(x => x.SizeBytes).ToList();
        while (remaining.Count > 0)
        {
            var seed = remaining[0];
            var bucket = remaining.Where(x => Ratio(seed.SizeBytes, x.SizeBytes) <= 1.25).ToList();
            foreach (var item in bucket) remaining.Remove(item);
            yield return bucket;
        }
    }

    private static double Ratio(long a, long b)
    {
        var min = Math.Max(1d, Math.Min(a, b));
        return Math.Max(a, b) / min;
    }

    public static string NormalizeStem(string name)
    {
        var value = name.ToLowerInvariant();
        value = Regex.Replace(value, @"(2160p|1440p|1080p|720p|4k|8k|hdr|hevc|x265|x264)", " ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"(copy|副本|复制|final|最终|new|新版)", " ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"[\s_\-\.\(\)\[\]]*\d{1,3}$", " ");
        value = Regex.Replace(value, @"[^\p{L}\p{N}]+", " ").Trim();
        return value;
    }
}
