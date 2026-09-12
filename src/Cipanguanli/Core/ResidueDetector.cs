using System.IO;

namespace Cipanguanli.Core;

public static class ResidueDetector
{
    public static IReadOnlyList<ResidueCandidate> Find(
        ScanResult result,
        SoftwareOwnershipService ownership,
        long minimumBytes = 256L * 1024 * 1024)
    {
        var candidates = new List<ResidueCandidate>();
        foreach (var folder in result.LargeFolders)
        {
            if (folder.SizeBytes < minimumBytes) continue;
            if (ownership.Resolve(folder.Path).IsKnown) continue;
            if (IsProtectedCategory(folder.Category)) continue;

            var lower = folder.Path.Replace('/', '\\').ToLowerInvariant();
            var leaf = SafeLeaf(folder.Path).ToLowerInvariant();

            // Do not mark every descendant of the system TEMP tree as a cache just because
            // an ancestor happens to be named Temp. The folder itself must have an app-data
            // or cache-like identity. This materially reduces false positives on real machines.
            var appDataLike = lower.Contains("\\appdata\\local\\") ||
                              lower.Contains("\\appdata\\roaming\\") ||
                              lower.Contains("\\programdata\\") ||
                              lower.Contains("\\localappdata\\");
            var cacheLeaf = IsCacheLeaf(leaf);
            var genericContainer = leaf is "appdata" or "local" or "roaming" or "programdata" or "temp" or "tmp";
            if (genericContainer) continue;

            var old = DaysSinceWrite(folder.Path) >= 120;
            if (!appDataLike && !cacheLeaf) continue;
            if (!old && !cacheLeaf) continue;

            var confidence = 45;
            var reasons = new List<string>();
            if (appDataLike) { confidence += 12; reasons.Add("位于应用数据/ProgramData 区域"); }
            if (cacheLeaf) { confidence += 18; reasons.Add("当前文件夹本身具有缓存/日志特征"); }
            if (old) { confidence += 12; reasons.Add("超过 120 天未修改"); }
            if (folder.SizeBytes >= 5L * 1024 * 1024 * 1024) { confidence += 5; reasons.Add("占用较大"); }
            confidence = Math.Min(confidence, 92);

            candidates.Add(new ResidueCandidate
            {
                Path = folder.Path,
                SizeBytes = folder.SizeBytes,
                ConfidenceScore = confidence,
                Reason = string.Join("；", reasons) + "；未匹配到当前已安装软件目录。",
                SuggestedAction = confidence >= 75
                    ? "优先人工核对；确认对应软件已卸载后，可先放入隔离区。"
                    : "仅作为线索，不建议直接删除；先确认是否仍被软件使用。"
            });
        }

        return RemoveNestedDuplicates(candidates)
            .OrderByDescending(x => x.ConfidenceScore)
            .ThenByDescending(x => x.SizeBytes)
            .Take(200)
            .ToArray();
    }

    private static bool IsCacheLeaf(string leaf)
    {
        if (leaf is "cache" or "caches" or ".cache" or "shadercache" or "shadercache2" or
            "deriveddatacache" or "mediacache" or "media cache" or "logs" or "log") return true;
        return leaf.EndsWith("cache", StringComparison.OrdinalIgnoreCase) ||
               leaf.EndsWith("cache2", StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeLeaf(string path)
    {
        try { return new DirectoryInfo(path).Name; }
        catch { return Path.GetFileName(path.TrimEnd('\\', '/')); }
    }

    private static IReadOnlyList<ResidueCandidate> RemoveNestedDuplicates(IEnumerable<ResidueCandidate> input)
    {
        var selected = new List<ResidueCandidate>();
        foreach (var item in input.OrderBy(x => x.Path.Length))
        {
            if (selected.Any(parent => IsUnder(item.Path, parent.Path))) continue;
            selected.Add(item);
        }
        return selected;
    }

    private static int DaysSinceWrite(string path)
    {
        try
        {
            var time = Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : File.GetLastWriteTimeUtc(path);
            return Math.Max(0, (int)(DateTime.UtcNow - time).TotalDays);
        }
        catch { return 0; }
    }

    private static bool IsProtectedCategory(string category)
        => new[] { "3D", "工程", "数据库", "虚拟机", "云盘", "系统", "游戏" }
            .Any(x => category.Contains(x, StringComparison.OrdinalIgnoreCase));

    private static bool IsUnder(string path, string parent)
    {
        try
        {
            var child = Path.GetFullPath(path).TrimEnd('\\', '/');
            var root = Path.GetFullPath(parent).TrimEnd('\\', '/');
            return child.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
