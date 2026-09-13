namespace MiniDiskLab.Core.Models;

/// <summary>
/// 扫描进度的不可变快照。由扫描线程产生，并在 UI 线程上消费。
/// </summary>
public sealed record ScanProgress
{
    /// <summary>已检查的文件数量（不区分大小）。</summary>
    public long ScannedFiles { get; init; }

    /// <summary>已递归进入的目录数量。</summary>
    public long ScannedDirectories { get; init; }

    /// <summary>大小达到/超过阈值的文件数量。</summary>
    public long LargeFileCount { get; init; }

    /// <summary>因权限不足而跳过的目录数量。</summary>
    public long SkippedDirectories { get; init; }

    /// <summary>因错误而无法读取的文件数量。</summary>
    public long ErrorCount { get; init; }

    /// <summary>因是重解析点（Symlink/Junction）而跳过的数量。</summary>
    public long SkippedLinks { get; init; }

    /// <summary>当前正在扫描的位置；扫描结束时为 <see langword="null"/>。</summary>
    public string? CurrentPath { get; init; }

    /// <summary>自扫描开始以来经过的时间。</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>格式化的扫描速率，例如 <c>1,234.5 文件/秒</c>。</summary>
    public string FilesPerSecondText
    {
        get
        {
            var seconds = Elapsed.TotalSeconds;
            if (seconds <= 0.001) return "-";
            return $"{ScannedFiles / seconds:N1} 文件/秒";
        }
    }
}

/// <summary>
/// 扫描的最终结果。
/// </summary>
public sealed class ScanSummary
{
    /// <summary>达到阈值的文件，按大小降序排列。</summary>
    public IReadOnlyList<ScanResultItem> Items { get; init; } = Array.Empty<ScanResultItem>();

    /// <summary>已检查的文件数量。</summary>
    public long ScannedFiles { get; init; }

    /// <summary>已递归进入的目录数量。</summary>
    public long ScannedDirectories { get; init; }

    /// <summary>因权限不足而跳过的目录数量。</summary>
    public long SkippedDirectories { get; init; }

    /// <summary>因读取错误而跳过的文件数量。</summary>
    public long ErrorCount { get; init; }

    /// <summary>因是重解析点而跳过的数量。</summary>
    public long SkippedLinks { get; init; }

    /// <summary>本次扫描消耗的总时间。</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>是否被用户取消。</summary>
    public bool WasCancelled { get; init; }

    /// <summary>被扫描的根目录。</summary>
    public string RootPath { get; init; } = string.Empty;

    /// <summary>扫描生效的阈值。</summary>
    public SizeThreshold Threshold { get; init; } = SizeThreshold.Default;

    /// <summary>按扩展名统计所有已扫描文件（可能包含大文件也可能不包含），按总大小降序。</summary>
    public IReadOnlyList<ExtensionStat> ExtensionStats { get; init; } = Array.Empty<ExtensionStat>();

    /// <summary>按总大小排名的前 10 个目录。</summary>
    public IReadOnlyList<DirectoryStat> TopDirectories { get; init; } = Array.Empty<DirectoryStat>();
}

/// <summary>按扩展名聚合的统计信息。</summary>
public sealed record ExtensionStat(string Extension, long FileCount, long TotalBytes)
{
    /// <summary>面向 UI 的格式化总大小。</summary>
    public string TotalSizeText => SizeConverter.Format(TotalBytes);
}

/// <summary>按目录聚合的统计信息。</summary>
public sealed record DirectoryStat(string Path, long TotalBytes, long FileCount)
{
    /// <summary>面向 UI 的格式化总大小。</summary>
    public string TotalSizeText => SizeConverter.Format(TotalBytes);
}
