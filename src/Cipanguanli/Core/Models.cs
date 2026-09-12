namespace Cipanguanli.Core;

public sealed class ScanTarget
{
    public required string DisplayName { get; init; }
    public required string Path { get; init; }
    public bool IsSelected { get; set; }
}

public sealed record FileAnnotation(string Category, string Risk, string Note);

public sealed class LargeFileEntry
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Category { get; init; }
    public required string Risk { get; init; }
    public required string Note { get; init; }
    public required long SizeBytes { get; init; }
    public required long AllocatedBytes { get; init; }
    public required DateTime LastModified { get; init; }
    public string SizeText => SizeFormatter.Format(SizeBytes);
    public string AllocatedText => SizeFormatter.Format(AllocatedBytes);
    public string LastModifiedText => LastModified.ToString("yyyy-MM-dd HH:mm");
}

public sealed class FolderSummary
{
    public required string Path { get; init; }
    public required string Category { get; init; }
    public required string Risk { get; init; }
    public required string Note { get; init; }
    public required long SizeBytes { get; init; }
    public required long FileCount { get; init; }
    public required bool ReviewForCleanup { get; init; }
    public string SizeText => SizeFormatter.Format(SizeBytes);
}

public sealed record ScanProgress(long FilesSeen, long BytesSeen, long LargeFilesFound, long SkippedDirectories, string CurrentPath);

public sealed class ScanResult
{
    public required IReadOnlyList<LargeFileEntry> LargeFiles { get; init; }
    public required IReadOnlyList<FolderSummary> LargeFolders { get; init; }
    public required IReadOnlyList<FolderSummary> CleanupCandidates { get; init; }
    public required long FilesSeen { get; init; }
    public required long BytesSeen { get; init; }
    public required long SkippedDirectories { get; init; }
    public required TimeSpan Elapsed { get; init; }
}
