namespace Cipanguanli.Core;

public static class CleanupPlanner
{
    public static IReadOnlyList<CleanupPlan> Build(
        ScanResult scan,
        IReadOnlyList<DuplicateFileGroup>? duplicates = null,
        IReadOnlyList<ResidueCandidate>? residues = null)
    {
        duplicates ??= Array.Empty<DuplicateFileGroup>();
        residues ??= Array.Empty<ResidueCandidate>();

        var conservativeFolders = DistinctTopLevel(scan.CleanupCandidates
            .Where(x => IsLowRisk(x.Risk) && ContainsAny(x.Category, "缓存", "临时", "下载")));
        var conservativeBytes = conservativeFolders.Sum(x => x.SizeBytes);

        var duplicateBytes = duplicates.Sum(x => x.ReclaimableBytes);
        var residueBytes = residues.Where(x => x.ConfidenceScore >= 75).Sum(x => x.SizeBytes);
        var recommendedOld = scan.OldFiles.Where(x => IsLowRisk(x.Risk) && ContainsAny(x.Category, "安装包", "压缩包", "归档", "视频"))
            .ToArray();
        var recommendedOldBytes = recommendedOld.Sum(x => x.SizeBytes);
        var recommendedBytes = conservativeBytes + duplicateBytes + residueBytes + recommendedOldBytes;

        var aggressiveFolders = DistinctTopLevel(scan.CleanupCandidates
            .Where(x => !IsDangerous(x.Risk, x.Category)));
        var aggressiveOld = scan.OldFiles.Where(x => !IsDangerous(x.Risk, x.Category)).ToArray();
        var aggressiveBytes = aggressiveFolders.Sum(x => x.SizeBytes) + aggressiveOld.Sum(x => x.SizeBytes) + duplicateBytes + residueBytes;

        return new[]
        {
            new CleanupPlan
            {
                Name = "保守清理",
                Level = "🟢 保守",
                EstimatedBytes = conservativeBytes,
                ItemCount = conservativeFolders.Count,
                Includes = "明确的临时/缓存/下载类低风险目录。",
                Warning = "仍然先看清单；关闭相关软件后再隔离，默认不永久删除。"
            },
            new CleanupPlan
            {
                Name = "推荐清理",
                Level = "🟡 推荐",
                EstimatedBytes = recommendedBytes,
                ItemCount = conservativeFolders.Count + duplicates.Count + residues.Count(x => x.ConfidenceScore >= 75) + recommendedOld.Length,
                Includes = "保守项 + 完全重复文件的冗余副本 + 高置信卸载残留 + 长期不用的低风险安装包/归档/视频。",
                Warning = "这是估算上限；目录存在嵌套时可能重复计算，执行前逐项确认。"
            },
            new CleanupPlan
            {
                Name = "激进清理",
                Level = "🔴 激进",
                EstimatedBytes = aggressiveBytes,
                ItemCount = aggressiveFolders.Count + aggressiveOld.Length + duplicates.Count + residues.Count,
                Includes = "推荐项 + 更多长期未修改文件和普通清理候选；排除系统关键、云盘、数据库、虚拟机、3D工程等高风险项。",
                Warning = "只建议在空间非常紧张时使用；优先隔离/迁移，不建议直接永久删除。"
            }
        };
    }

    private static IReadOnlyList<FolderSummary> DistinctTopLevel(IEnumerable<FolderSummary> folders)
    {
        var selected = new List<FolderSummary>();
        foreach (var folder in folders.OrderBy(x => x.Path.Length))
        {
            if (selected.Any(parent => IsUnder(folder.Path, parent.Path))) continue;
            selected.Add(folder);
        }
        return selected;
    }

    private static bool IsUnder(string path, string parent)
    {
        var p = parent.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
        return path.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLowRisk(string risk) => risk is "低" or "低-中" or "取决于内容";

    private static bool IsDangerous(string risk, string category)
    {
        if (risk.Contains("禁止", StringComparison.Ordinal) || risk == "高" || risk == "中-高") return true;
        return ContainsAny(category, "系统", "云盘", "数据库", "虚拟机", "3D", "工程");
    }

    private static bool ContainsAny(string value, params string[] terms)
        => terms.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));
}
