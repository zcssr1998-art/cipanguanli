namespace Cipanguanli.Core;

public sealed class CDriveFinding
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Path { get; init; }
    public required long SizeBytes { get; init; }
    public required long ReclaimableBytes { get; init; }
    public required int SafetyScore { get; init; }
    public required string Risk { get; init; }
    public required string Detail { get; init; }
    public required string RecommendedAction { get; init; }
    public required string ActionKey { get; init; }
    public required bool CanQuarantine { get; init; }
    public required bool IsProtected { get; init; }
    public string SizeText => SizeFormatter.Format(SizeBytes);
    public string ReclaimableText => SizeFormatter.Format(ReclaimableBytes);
    public string SafetyText => $"{SafetyScore}/100";
}

public sealed class CDriveAppDataEntry
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required long SizeBytes { get; init; }
    public required string Owner { get; init; }
    public required string InstallLocation { get; init; }
    public required string InstallDrive { get; init; }
    public required bool InstalledOutsideSystemDrive { get; init; }
    public required string Note { get; init; }
    public string SizeText => SizeFormatter.Format(SizeBytes);
    public string InstallState => InstalledOutsideSystemDrive ? "装在其他盘，但写入 C 盘" : "C盘/未知";
}

public sealed class CDriveVirtualDiskEntry
{
    public required string Kind { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required long SizeBytes { get; init; }
    public required string Detail { get; init; }
    public required string RecommendedAction { get; init; }
    public string SizeText => SizeFormatter.Format(SizeBytes);
}

public sealed class CDriveGrowthEntry
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required long PreviousBytes { get; init; }
    public required long CurrentBytes { get; init; }
    public long GrowthBytes => CurrentBytes - PreviousBytes;
    public string PreviousText => SizeFormatter.Format(PreviousBytes);
    public string CurrentText => SizeFormatter.Format(CurrentBytes);
    public string GrowthText => GrowthBytes >= 0 ? "+" + SizeFormatter.Format(GrowthBytes) : "-" + SizeFormatter.Format(-GrowthBytes);
}

public sealed class CDriveTargetPlan
{
    public required long TargetBytes { get; init; }
    public required long PlannedBytes { get; init; }
    public required int RiskPenalty { get; init; }
    public required IReadOnlyList<CDriveFinding> Items { get; init; }
    public required string Mode { get; init; }
    public bool MeetsTarget => PlannedBytes >= TargetBytes;
    public string TargetText => SizeFormatter.Format(TargetBytes);
    public string PlannedText => SizeFormatter.Format(PlannedBytes);
    public string Summary => MeetsTarget
        ? $"预计可释放 {PlannedText}，达到 {TargetText} 目标。"
        : $"当前低风险候选约 {PlannedText}，不足 {TargetText}；不建议为了凑数去碰高风险系统数据。";
}

public sealed class CDriveReport
{
    public required string Root { get; init; }
    public required long TotalBytes { get; init; }
    public required long FreeBytes { get; init; }
    public required long UsedBytes { get; init; }
    public required IReadOnlyList<CDriveFinding> Findings { get; init; }
    public required IReadOnlyList<CDriveAppDataEntry> AppDataEntries { get; init; }
    public required IReadOnlyList<CDriveVirtualDiskEntry> VirtualDisks { get; init; }
    public required IReadOnlyList<CDriveGrowthEntry> Growth { get; init; }
    public required DateTime CapturedUtc { get; init; }
    public long ConservativeReclaimableBytes => Findings.Where(x => x.SafetyScore >= 85 && x.ReclaimableBytes > 0 && !x.IsProtected).Sum(x => x.ReclaimableBytes);
    public long RecommendedReclaimableBytes => Findings.Where(x => x.SafetyScore >= 65 && x.ReclaimableBytes > 0 && !x.IsProtected).Sum(x => x.ReclaimableBytes);
    public string TotalText => SizeFormatter.Format(TotalBytes);
    public string FreeText => SizeFormatter.Format(FreeBytes);
    public string UsedText => SizeFormatter.Format(UsedBytes);
    public string ConservativeText => SizeFormatter.Format(ConservativeReclaimableBytes);
    public string RecommendedText => SizeFormatter.Format(RecommendedReclaimableBytes);
}

public sealed class CDriveWriteActivityEntry
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required long BytesWritten { get; init; }
    public required string LastPath { get; init; }
    public string BytesWrittenText => SizeFormatter.Format(BytesWritten);
}
