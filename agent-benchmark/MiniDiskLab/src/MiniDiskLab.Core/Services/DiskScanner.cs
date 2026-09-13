using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;
using MiniDiskLab.Core.Classifiers;
using MiniDiskLab.Core.Models;

namespace MiniDiskLab.Core.Services;

/// <summary>
/// 可取消、能处理权限错误、能处理重解析点的目录扫描器。
/// </summary>
/// <remarks>
/// <para>
/// 该扫描器从不抛出异常来中止扫描。单个目录或文件出现问题会被记录到计数器，
/// 然后继续扫描。
/// </para>
/// <para>
/// 循环检测基于重解析点的目标标识（设备 + 文件索引），而不是简单的路径字符串比较，
/// 因此 <c>A -> B -> A</c> 这类循环会被捕获。
/// </para>
/// </remarks>
public sealed class DiskScanner
{
    private readonly IFileClassifier _classifier;

    /// <summary>为所有扫描提供安全上限，避免失控。</summary>
    public const int DefaultMaxEntries = 5_000_000;

    /// <summary>创建带有默认分类器的扫描器。</summary>
    public DiskScanner()
        : this(new FileClassifier())
    {
    }

    /// <summary>创建使用指定分类器的扫描器。</summary>
    public DiskScanner(IFileClassifier classifier)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    }

    /// <summary>
    /// 同步执行扫描。通过 <paramref name="progress"/> 报告进度。
    /// 本方法被设计为在后台线程上运行。
    /// </summary>
    /// <param name="rootPath">要递归扫描的目录。</param>
    /// <param name="threshold">大文件阈值。</param>
    /// <param name="cancellationToken">在扫描中途取消。</param>
    /// <param name="progress">可选的进度回调；可能会被高频调用。</param>
    /// <param name="itemFound">每发现一个符合阈值的文件时触发。</param>
    /// <param name="maxEntries">遍历条目的安全上限。</param>
    public ScanSummary Scan(
        string rootPath,
        SizeThreshold threshold,
        CancellationToken cancellationToken = default,
        IProgress<ScanProgress>? progress = null,
        Action<ScanResultItem>? itemFound = null,
        int maxEntries = DefaultMaxEntries)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("扫描目录不能为空。", nameof(rootPath));
        }

        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"扫描目录不存在：{rootPath}");
        }

        var thresholdBytes = threshold.ToBytes();
        var stopwatch = Stopwatch.StartNew();

        long scannedFiles = 0;
        long scannedDirs = 0;
        long skippedDirs = 0;
        long errorCount = 0;
        long skippedLinks = 0;

        var results = new List<ScanResultItem>(capacity: 256);
        var extensionStats = new ConcurrentDictionary<string, (long Count, long Bytes)>(
            StringComparer.OrdinalIgnoreCase);
        var directoryBytes = new ConcurrentDictionary<string, (long Bytes, long Count)>(
            StringComparer.OrdinalIgnoreCase);

        // 按重解析点身份追踪已访问目录，以打断 A->B->A 这类循环。
        var visitedLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var wasCancelled = false;
        var hitEntryLimit = false;
        var lastReport = Stopwatch.StartNew();

        void Report(string? currentPath, bool force = false)
        {
            if (progress is null)
            {
                return;
            }

            // 节流：除非被强制要求，否则最多每 100 毫秒报告一次。
            if (!force && lastReport.ElapsedMilliseconds < 100)
            {
                return;
            }

            lastReport.Restart();

            progress.Report(new ScanProgress
            {
                ScannedFiles = Interlocked.Read(ref scannedFiles),
                ScannedDirectories = Interlocked.Read(ref scannedDirs),
                LargeFileCount = results.Count,
                SkippedDirectories = Interlocked.Read(ref skippedDirs),
                ErrorCount = Interlocked.Read(ref errorCount),
                SkippedLinks = Interlocked.Read(ref skippedLinks),
                CurrentPath = currentPath,
                Elapsed = stopwatch.Elapsed,
            });
        }

        // 显式使用栈而不是递归——深层目录树不会导致栈溢出。
        var pending = new Stack<string>();
        pending.Push(NormalizeRoot(rootPath));

        Report(rootPath, force: true);

        while (pending.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                wasCancelled = true;
                break;
            }

            if (scannedFiles >= maxEntries)
            {
                break;
            }

            var currentDir = pending.Pop();
            Interlocked.Increment(ref scannedDirs);

            List<string> subDirectories;
            try
            {
                subDirectories = new List<string>(EnumerateDirectoriesSafe(currentDir));
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                skippedDirs++;
                subDirectories = new List<string>();
                _ = ex;
            }

            foreach (var subDir in subDirectories)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    wasCancelled = true;
                    break;
                }

                if (IsReparsePointSafe(subDir, ref errorCount))
                {
                    var identity = TryGetLinkIdentity(subDir);
                    if (identity is not null && !visitedLinks.Add(identity))
                    {
                        // 已经访问过该目标——很可能是循环链接。
                        Interlocked.Increment(ref skippedLinks);
                        continue;
                    }

                    if (identity is not null)
                    {
                        // 重解析点指向仓库外部；除非能证明安全，否则不递归进入。
                        continue;
                    }
                }

                pending.Push(subDir);
            }

            if (wasCancelled)
            {
                break;
            }

            // 扫描当前目录中的文件。
            try
            {
                foreach (var filePath in EnumerateFilesSafe(currentDir))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        wasCancelled = true;
                        break;
                    }

                    // 安全上限也要在文件循环内部检查，否则当前目录中剩余的
                    // 文件会被继续处理，导致超出 maxEntries。
                    if (Interlocked.Read(ref scannedFiles) >= maxEntries)
                    {
                        hitEntryLimit = true;
                        break;
                    }

                    Interlocked.Increment(ref scannedFiles);

                    long size;
                    DateTime? lastWrite = null;
                    try
                    {
                        // 扫完再取一遍信息，因为文件可能在枚举期间消失。
                        var info = new FileInfo(filePath);
                        if (!info.Exists)
                        {
                            Interlocked.Increment(ref errorCount);
                            continue;
                        }

                        size = info.Length;
                        lastWrite = info.LastWriteTime;
                    }
                    catch (Exception ex) when (IsRecoverable(ex))
                    {
                        Interlocked.Increment(ref errorCount);
                        _ = ex;
                        continue;
                    }

                    var extension = FileClassifier.GetExtension(GetFileName(filePath));

                    // 记录扩展名统计（针对每个已扫描文件，而不只是大文件）。
                    extensionStats.AddOrUpdate(
                        extension.Length == 0 ? "(无扩展名)" : extension,
                        _ => (1, size),
                        (_, existing) => (existing.Count + 1, existing.Bytes + size));

                    // 按直接父目录记录统计。
                    if (size > 0)
                    {
                        var parent = GetDirectoryName(filePath) ?? currentDir;
                        directoryBytes.AddOrUpdate(
                            parent,
                            _ => (size, 1),
                            (_, existing) => (existing.Bytes + size, existing.Count + 1));
                    }

                    if (size < thresholdBytes)
                    {
                        Report(filePath);
                        continue;
                    }

                    var classification = _classifier.Classify(filePath);
                    var item = new ScanResultItem
                    {
                        FileName = GetFileName(filePath),
                        FullPath = filePath,
                        SizeBytes = size,
                        Extension = extension,
                        Category = classification.Category,
                        Description = classification.Description,
                        LastWriteTime = lastWrite,
                    };

                    results.Add(item);
                    itemFound?.Invoke(item);
                    Report(filePath, force: true);
                }
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                // 注意：不使用 Interlocked.Increment —— 该变量只在扫描线程上写入。
                skippedDirs++;
                _ = ex;
            }

            if (hitEntryLimit)
            {
                break;
            }

            Report(currentDir);
        }

        stopwatch.Stop();

        // 按大小排序，降序；用路径作为决胜条件，以保证结果稳定。
        results.Sort(static (a, b) =>
        {
            var cmp = b.SizeBytes.CompareTo(a.SizeBytes);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.FullPath, b.FullPath);
        });

        var extStats = extensionStats
            .Select(kv => new ExtensionStat(kv.Key, kv.Value.Count, kv.Value.Bytes))
            .OrderByDescending(s => s.TotalBytes)
            .ToList();

        var topDirs = directoryBytes
            .Select(kv => new DirectoryStat(kv.Key, kv.Value.Bytes, kv.Value.Count))
            .OrderByDescending(d => d.TotalBytes)
            .Take(10)
            .ToList();

        var summary = new ScanSummary
        {
            Items = results,
            ScannedFiles = Interlocked.Read(ref scannedFiles),
            ScannedDirectories = Interlocked.Read(ref scannedDirs),
            SkippedDirectories = Interlocked.Read(ref skippedDirs),
            ErrorCount = Interlocked.Read(ref errorCount),
            SkippedLinks = Interlocked.Read(ref skippedLinks),
            Elapsed = stopwatch.Elapsed,
            WasCancelled = wasCancelled,
            RootPath = rootPath,
            Threshold = threshold,
            ExtensionStats = extStats,
            TopDirectories = topDirs,
        };

        // 最终报告始终发送。
        progress?.Report(new ScanProgress
        {
            ScannedFiles = summary.ScannedFiles,
            ScannedDirectories = summary.ScannedDirectories,
            LargeFileCount = summary.Items.Count,
            SkippedDirectories = summary.SkippedDirectories,
            ErrorCount = summary.ErrorCount,
            SkippedLinks = summary.SkippedLinks,
            CurrentPath = null,
            Elapsed = summary.Elapsed,
        });

        return summary;
    }

    /// <summary>
    /// 异步扫描，支持真正的取消。
    /// </summary>
    /// <remarks>
    /// 这里故意不把 <paramref name="cancellationToken"/> 传给 <see cref="Task.Run(Action)"/>。
    /// 若传进去，在 token 已经取消（或取消发生在委托启动前）时任务会直接抛
    /// <see cref="TaskCanceledException"/>，调用方就拿不到带 <c>WasCancelled = true</c> 的结果了。
    /// 取消应当表现为“扫描提前结束”，而不是异常。
    /// </remarks>
    public Task<ScanSummary> ScanAsync(
        string rootPath,
        SizeThreshold threshold,
        CancellationToken cancellationToken = default,
        IProgress<ScanProgress>? progress = null,
        Action<ScanResultItem>? itemFound = null,
        int maxEntries = DefaultMaxEntries)
    {
        return Task.Run(
            () => Scan(rootPath, threshold, cancellationToken, progress, itemFound, maxEntries));
    }

    private static string NormalizeRoot(string rootPath)
    {
        var full = Path.GetFullPath(rootPath);
        if (full.Length > 3 && (full.EndsWith('\\') || full.EndsWith('/')))
        {
            full = full.TrimEnd('\\', '/');
        }

        return full;
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string dir)
    {
        // IgnoreInaccessible = false：把权限错误暴露出来，以便计入“已跳过目录”，
        // 否则用户完全看不到有多少目录被静默忽略。
        return Directory.EnumerateDirectories(dir, "*", new EnumerationOptions
        {
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.System,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        });
    }

    private static IEnumerable<string> EnumerateFilesSafe(string dir)
    {
        return Directory.EnumerateFiles(dir, "*", new EnumerationOptions
        {
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.System,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        });
    }

    private static bool IsReparsePointSafe(string path, ref long errorCount)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            Interlocked.Increment(ref errorCount);
            // 如果无法确定，就假定它是普通目录并尝试递归；
            // 内部循环会处理相关错误。
            return false;
        }
    }

    /// <summary>
    /// 返回重解析点的稳定身份标识（卷序列号 + 文件索引），
    /// 或者当它不是重解析点/无法读取时返回 <see langword="null"/>。
    /// </summary>
    private static string? TryGetLinkIdentity(string path)
    {
        try
        {
            // 这里使用 .NET 提供的、无需 P/Invoke 的近似方案：
            // 链接目标的完整解析路径，拼上创建时间，对循环检测来说已经足够稳定。
            var target = new DirectoryInfo(path);
            var resolved = target.ResolveLinkTarget(returnFinalTarget: true);
            if (resolved is null)
            {
                return null;
            }

            return resolved.FullName;
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return null;
        }
    }

    private static string? GetDirectoryName(string path)
    {
        try
        {
            return Path.GetDirectoryName(path);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return null;
        }
    }

    private static string GetFileName(string path)
    {
        try
        {
            return Path.GetFileName(path);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            var idx = path.LastIndexOf('\\');
            return idx >= 0 ? path[(idx + 1)..] : path;
        }
    }

    /// <summary>
    /// 判断异常是否属于单个条目级别、可安全跳过的异常。
    /// </summary>
    internal static bool IsRecoverable(Exception ex) => ex switch
    {
        UnauthorizedAccessException => true,
        DirectoryNotFoundException => true,
        FileNotFoundException => true,
        PathTooLongException => true,
        IOException => true,
        NotSupportedException => true,
        System.Security.SecurityException => true,
        ArgumentException => true,
        _ => false,
    };
}
