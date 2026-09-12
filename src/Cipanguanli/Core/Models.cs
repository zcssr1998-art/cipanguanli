using System.IO;

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

public sealed class OldFileEntry
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Category { get; init; }
    public required string Risk { get; init; }
    public required string Note { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTime LastModified { get; init; }
    public int DaysOld => Math.Max(0, (int)(DateTime.Now - LastModified).TotalDays);
    public string SizeText => SizeFormatter.Format(SizeBytes);
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

public sealed class FolderMapNode
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Category { get; init; }
    public required string Risk { get; init; }
    public required long SizeBytes { get; init; }
    public required double SharePercent { get; init; }
    public required IReadOnlyList<FolderMapNode> Children { get; init; }
    public string SizeText => SizeFormatter.Format(SizeBytes);
    public string ShareText => $"{SharePercent:F1}%";
}

public sealed class DuplicateFileGroup
{
    public required string Hash { get; init; }
    public required long FileSizeBytes { get; init; }
    public required IReadOnlyList<string> Paths { get; init; }
    public int Count => Paths.Count;
    public long ReclaimableBytes => FileSizeBytes * Math.Max(0, Count - 1);
    public string FileSizeText => SizeFormatter.Format(FileSizeBytes);
    public string ReclaimableText => SizeFormatter.Format(ReclaimableBytes);
    public string FirstPath => Paths.Count > 0 ? Paths[0] : string.Empty;
    public string PathsText => string.Join(Environment.NewLine, Paths);
}

public sealed record ScanProgress(long FilesSeen, long BytesSeen, long LargeFilesFound, long SkippedDirectories, string CurrentPath);
public sealed record DuplicateProgress(long FilesSeen, long CandidateFiles, long HashedFiles, long GroupsFound, string CurrentPath);
public sealed record MigrationProgress(long CopiedFiles, long CopiedBytes, string CurrentPath);

public sealed class MigrationResult
{
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public required long BytesMoved { get; init; }
    public required long FilesMoved { get; init; }
    public required bool SourceRemoved { get; init; }
    public string BytesMovedText => SizeFormatter.Format(BytesMoved);
}

public sealed class ScanResult
{
    public required IReadOnlyList<LargeFileEntry> LargeFiles { get; init; }
    public required IReadOnlyList<OldFileEntry> OldFiles { get; init; }
    public required IReadOnlyList<FolderSummary> LargeFolders { get; init; }
    public required IReadOnlyList<FolderSummary> CleanupCandidates { get; init; }
    public required IReadOnlyList<FolderMapNode> FolderMapRoots { get; init; }
    public required long FilesSeen { get; init; }
    public required long BytesSeen { get; init; }
    public required long SkippedDirectories { get; init; }
    public required TimeSpan Elapsed { get; init; }
}
