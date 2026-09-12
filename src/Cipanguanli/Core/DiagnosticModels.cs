namespace Cipanguanli.Core;

public sealed record InstalledProduct(
    string DisplayName,
    string Publisher,
    string InstallLocation,
    string UninstallCommand,
    string Source,
    string ProductId = "");

public sealed record OwnershipMatch(
    string DisplayName,
    string Publisher,
    string InstallLocation,
    string UninstallCommand,
    string Source,
    string Confidence)
{
    public static OwnershipMatch Unknown { get; } = new("未识别", "", "", "", "本地规则", "低");
    public bool IsKnown => !DisplayName.Equals("未识别", StringComparison.Ordinal);
}

public sealed class DiagnosticEntry
{
    public required string Kind { get; init; }
    public required string Path { get; init; }
    public required long SizeBytes { get; init; }
    public required string Category { get; init; }
    public required string Owner { get; init; }
    public required string OwnerSource { get; init; }
    public required string OwnershipConfidence { get; init; }
    public required string Risk { get; init; }
    public required int SafetyScore { get; init; }
    public required string RecommendedAction { get; init; }
    public required string RelationshipHint { get; init; }
    public required string CompressionEstimate { get; init; }
    public required string Note { get; init; }
    public required string UninstallCommand { get; init; }
    public string SizeText => SizeFormatter.Format(SizeBytes);
    public string SafetyText => $"{SafetyScore}/100";
}

public sealed class ResidueCandidate
{
    public required string Path { get; init; }
    public required long SizeBytes { get; init; }
    public required int ConfidenceScore { get; init; }
    public required string Reason { get; init; }
    public required string SuggestedAction { get; init; }
    public string SizeText => SizeFormatter.Format(SizeBytes);
    public string ConfidenceText => $"{ConfidenceScore}/100";
}

public sealed class GrowthEntry
{
    public required string Path { get; init; }
    public required long PreviousBytes { get; init; }
    public required long CurrentBytes { get; init; }
    public long GrowthBytes => CurrentBytes - PreviousBytes;
    public string PreviousText => SizeFormatter.Format(PreviousBytes);
    public string CurrentText => SizeFormatter.Format(CurrentBytes);
    public string GrowthText => "+" + SizeFormatter.Format(Math.Max(0, GrowthBytes));
}

public sealed class CleanupPlan
{
    public required string Name { get; init; }
    public required string Level { get; init; }
    public required long EstimatedBytes { get; init; }
    public required int ItemCount { get; init; }
    public required string Includes { get; init; }
    public required string Warning { get; init; }
    public string EstimatedText => SizeFormatter.Format(EstimatedBytes);
}

public sealed class QuarantineItem
{
    public required string Id { get; init; }
    public required string OriginalPath { get; init; }
    public required string QuarantinePath { get; init; }
    public required DateTime CreatedUtc { get; init; }
    public required bool IsDirectory { get; init; }
    public required long SizeBytes { get; init; }
    public string SizeText => SizeFormatter.Format(SizeBytes);
    public string CreatedText => CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}

public sealed class LearnedRule
{
    public required string PathPrefix { get; init; }
    public required string Category { get; init; }
    public required string Note { get; init; }
    public required DateTime CreatedUtc { get; init; }
}

public sealed class SimilarMediaGroup
{
    public required string Key { get; init; }
    public required IReadOnlyList<string> Paths { get; init; }
    public required long TotalBytes { get; init; }
    public required string Reason { get; init; }
    public int Count => Paths.Count;
    public string TotalText => SizeFormatter.Format(TotalBytes);
    public string FirstPath => Paths.FirstOrDefault() ?? string.Empty;
    public string PathsText => string.Join(Environment.NewLine, Paths);
}
