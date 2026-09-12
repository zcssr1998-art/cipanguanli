using System.IO;

namespace Cipanguanli.Core;

public static class DiagnosticAnalyzer
{
    public static IReadOnlyList<DiagnosticEntry> Analyze(
        ScanResult result,
        SoftwareOwnershipService ownership,
        LearnedRuleStore? learnedRules = null)
    {
        var items = new List<DiagnosticEntry>();
        foreach (var file in result.LargeFiles)
        {
            items.Add(BuildFile(file, ownership.Resolve(file.Path), learnedRules?.Resolve(file.Path)));
        }
        foreach (var folder in result.LargeFolders.Take(500))
        {
            items.Add(BuildFolder(folder, ownership.Resolve(folder.Path), learnedRules?.Resolve(folder.Path)));
        }
        return items.OrderByDescending(x => x.SizeBytes).ToArray();
    }

    private static DiagnosticEntry BuildFile(LargeFileEntry file, OwnershipMatch owner, LearnedRule? learned)
    {
        var category = learned?.Category ?? file.Category;
        var note = learned is null ? file.Note : $"用户规则：{learned.Note}；原识别：{file.Category}。";
        var score = SafetyScore(file.Risk, category, owner.IsKnown);
        return new DiagnosticEntry
        {
            Kind = "文件",
            Path = file.Path,
            SizeBytes = file.SizeBytes,
            Category = category,
            Owner = learned is not null ? $"用户标注：{learned.Category}" : owner.DisplayName,
            OwnerSource = learned is not null ? "用户规则" : owner.Source,
            OwnershipConfidence = learned is not null ? "最高" : owner.Confidence,
            Risk = file.Risk,
            SafetyScore = score,
            RecommendedAction = Action(file.Risk, category, owner),
            RelationshipHint = Relationship(file.Path, category, owner),
            CompressionEstimate = CompressionEstimate(file.Path, category),
            Note = note,
            UninstallCommand = owner.UninstallCommand
        };
    }

    private static DiagnosticEntry BuildFolder(FolderSummary folder, OwnershipMatch owner, LearnedRule? learned)
    {
        var category = learned?.Category ?? folder.Category;
        return new DiagnosticEntry
        {
            Kind = "文件夹",
            Path = folder.Path,
            SizeBytes = folder.SizeBytes,
            Category = category,
            Owner = learned is not null ? $"用户标注：{learned.Category}" : owner.DisplayName,
            OwnerSource = learned is not null ? "用户规则" : owner.Source,
            OwnershipConfidence = learned is not null ? "最高" : owner.Confidence,
            Risk = folder.Risk,
            SafetyScore = SafetyScore(folder.Risk, category, owner.IsKnown),
            RecommendedAction = Action(folder.Risk, category, owner),
            RelationshipHint = Relationship(folder.Path, category, owner),
            CompressionEstimate = FolderCompressionEstimate(category),
            Note = learned is null ? folder.Note : $"用户规则：{learned.Note}；原识别：{folder.Category}。",
            UninstallCommand = owner.UninstallCommand
        };
    }

    private static int SafetyScore(string risk, string category, bool ownerKnown)
    {
        var score = risk switch
        {
            var x when x.Contains("禁止", StringComparison.Ordinal) => 0,
            "高" => 15,
            "中-高" => 30,
            "中" => 50,
            "低-中" => 70,
            "低" => 88,
            _ => 40
        };

        if (ownerKnown) score -= 12;
        if (ContainsAny(category, "缓存", "临时", "下载", "归档", "安装包")) score += 12;
        if (ContainsAny(category, "3D", "数据库", "云盘", "系统", "虚拟机")) score -= 15;
        return Math.Clamp(score, 0, 100);
    }

    private static string Action(string risk, string category, OwnershipMatch owner)
    {
        if (risk.Contains("禁止", StringComparison.Ordinal)) return "禁止直接操作";
        if (owner.IsKnown && owner.Source is "Steam" or "Epic Games") return "通过游戏平台卸载/移动优先";
        if (owner.IsKnown && category.Contains("游戏", StringComparison.OrdinalIgnoreCase)) return "通过软件官方卸载入口处理";
        if (ContainsAny(category, "3D", "工程", "数据库", "虚拟机", "云盘")) return "优先迁移/归档，不直接删除";
        if (risk == "高") return "不要直接删除，先确认归属";
        if (ContainsAny(category, "缓存", "临时")) return "可先进入隔离区，确认后再永久清理";
        if (ContainsAny(category, "安装包", "压缩包", "归档", "下载")) return "人工确认后可清理或隔离";
        return "人工核对；不确定时先隔离或迁移";
    }

    private static string Relationship(string path, string category, OwnershipMatch owner)
    {
        if (owner.IsKnown) return $"位于【{owner.DisplayName}】安装/资源目录内，单独删除可能破坏软件完整性。";
        if (ContainsAny(category, "3D", "工程", "美术")) return "可能属于 Maya/ZBrush/Substance/UE/Unity 等项目资产或缓存；建议按完整项目处理。";
        if (ContainsAny(category, "数据库", "虚拟机")) return "属于结构化数据容器，单文件删除风险高。";
        if (ContainsAny(category, "云盘")) return "可能参与同步；本地删除可能触发云端删除。";

        var parent = Path.GetDirectoryName(path) ?? string.Empty;
        try
        {
            if (Directory.Exists(parent))
            {
                if (Directory.EnumerateFiles(parent, "*.uproject", SearchOption.TopDirectoryOnly).Any()) return "与 Unreal 项目文件位于同级目录，按项目依赖处理。";
                if (Directory.EnumerateFiles(parent, "ProjectSettings.asset", SearchOption.AllDirectories).Take(1).Any()) return "目录中检测到 Unity ProjectSettings，按 Unity 工程处理。";
            }
        }
        catch { }
        return "未发现明确工程/软件依赖证据；仍需结合上级目录和用途判断。";
    }

    private static string CompressionEstimate(string path, string category)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (new[] { ".zip", ".7z", ".rar", ".mp4", ".mkv", ".jpg", ".jpeg", ".png", ".gguf", ".safetensors", ".ckpt" }.Contains(ext)) return "低（约 0–8%）";
        if (new[] { ".txt", ".log", ".csv", ".json", ".xml", ".obj", ".ma" }.Contains(ext)) return "高（约 40–85%）";
        if (new[] { ".wav", ".exr", ".tif", ".tiff", ".abc", ".fbx", ".mb" }.Contains(ext)) return "中（约 15–55%）";
        if (ContainsAny(category, "视频", "压缩包", "AI 模型")) return "低";
        return "未知/需实测";
    }

    private static string FolderCompressionEstimate(string category)
    {
        if (ContainsAny(category, "视频", "AI 模型", "压缩包")) return "整体收益偏低";
        if (ContainsAny(category, "3D", "工程", "美术")) return "中等，工程缓存可能较高";
        if (ContainsAny(category, "缓存", "临时")) return "不建议压缩，优先清理";
        return "需按内部文件构成估算";
    }

    private static bool ContainsAny(string value, params string[] terms)
        => terms.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));
}
