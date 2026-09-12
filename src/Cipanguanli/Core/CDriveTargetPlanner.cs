namespace Cipanguanli.Core;

public static class CDriveTargetPlanner
{
    public static CDriveTargetPlan Build(IEnumerable<CDriveFinding> findings, long targetBytes, bool emergency = false)
    {
        targetBytes = Math.Max(0, targetBytes);
        var minimumSafety = emergency ? 85 : 65;
        var allowedActionKeys = emergency
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "storage-settings", "disk-cleanup", "component-cleanup", "quarantine",
                "open-location", "docker-prune-review"
            }
            : null;

        var candidates = findings
            .Where(x => x.ReclaimableBytes > 0 && !x.IsProtected && x.SafetyScore >= minimumSafety)
            .Where(x => allowedActionKeys is null || allowedActionKeys.Contains(x.ActionKey))
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Path) ? x.Id : x.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.SafetyScore).ThenByDescending(x => x.ReclaimableBytes).First())
            .OrderByDescending(x => x.SafetyScore)
            .ThenByDescending(x => x.ReclaimableBytes)
            .ToList();

        var picked = new List<CDriveFinding>();
        long total = 0;
        var penalty = 0;
        foreach (var item in candidates)
        {
            if (targetBytes > 0 && total >= targetBytes) break;
            picked.Add(item);
            total += item.ReclaimableBytes;
            var risk = Math.Clamp(100 - item.SafetyScore, 0, 100);
            penalty += risk * risk;
        }

        return new CDriveTargetPlan
        {
            TargetBytes = targetBytes,
            PlannedBytes = total,
            RiskPenalty = penalty,
            Items = picked,
            Mode = emergency ? "紧急救援" : "目标式清理"
        };
    }
}
