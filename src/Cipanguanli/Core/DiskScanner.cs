using System.Diagnostics;

namespace Cipanguanli.Core;

public sealed class DiskScanner
{
    private sealed class FolderNode
    {
        public required string Path { get; init; }
        public required string Root { get; init; }
        public string? ParentPath { get; init; }
        public long DirectBytes { get; set; }
        public long TotalBytes { get; set; }
        public long DirectFileCount { get; set; }
        public long TotalFileCount { get; set; }
        public long ThreeDBytes { get; set; }
        public long VideoBytes { get; set; }
        public long ArchiveBytes { get; set; }
        public long AiModelBytes { get; set; }
    }

    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public Task<ScanResult> ScanAsync(
        IEnumerable<string> roots,
        long thresholdBytes,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (thresholdBytes <= 0) throw new ArgumentOutOfRangeException(nameof(thresholdBytes));
        var normalizedRoots = roots
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(NormalizeRoot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedRoots.Length == 0) throw new ArgumentException("At least one scan root is required.", nameof(roots));
        return Task.Run(() => Scan(normalizedRoots, thresholdBytes, progress, cancellationToken), cancellationToken);
    }

    private static ScanResult Scan(
        IReadOnlyList<string> roots,
        long thresholdBytes,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var files = new List<LargeFileEntry>();
        var nodes = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);
        long filesSeen = 0;
        long bytesSeen = 0;
        long skippedDirectories = 0;
        long largeFilesFound = 0;
        var lastProgress = Stopwatch.StartNew();

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root))
            {
                skippedDirectories++;
                continue;
            }

            EnsureNode(nodes, root, null, root);
            var stack = new Stack<(DirectoryInfo Directory, string Root)>();
            stack.Push((new DirectoryInfo(root), root));

            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (directory, scanRoot) = stack.Pop();
                var currentPath = directory.FullName;

                try
                {
                    foreach (var entry in directory.EnumerateFileSystemInfos("*", Options))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (entry is DirectoryInfo childDirectory)
                        {
                            EnsureNode(nodes, childDirectory.FullName, currentPath, scanRoot);
                            stack.Push((childDirectory, scanRoot));
                            continue;
                        }

                        if (entry is not FileInfo file) continue;

                        long size;
                        FileAttributes attributes;
                        DateTime lastWrite;
                        try
                        {
                            size = file.Length;
                            attributes = file.Attributes;
                            lastWrite = file.LastWriteTime;
                        }
                        catch (IOException)
                        {
                            continue;
                        }
                        catch (UnauthorizedAccessException)
                        {
                            continue;
                        }

                        filesSeen++;
                        bytesSeen += size;

                        if (nodes.TryGetValue(currentPath, out var currentNode))
                        {
                            currentNode.DirectBytes += size;
                            currentNode.DirectFileCount++;
                            var extension = file.Extension;
                            if (FileClassifier.IsThreeDExtension(extension)) currentNode.ThreeDBytes += size;
                            if (FileClassifier.IsVideoExtension(extension)) currentNode.VideoBytes += size;
                            if (FileClassifier.IsArchiveExtension(extension)) currentNode.ArchiveBytes += size;
                            if (FileClassifier.IsAiModelExtension(extension)) currentNode.AiModelBytes += size;
                        }

                        if (size >= thresholdBytes)
                        {
                            var annotation = FileClassifier.ClassifyFile(file.FullName);
                            files.Add(new LargeFileEntry
                            {
                                Name = file.Name,
                                Path = file.FullName,
                                SizeBytes = size,
                                AllocatedBytes = NativeDiskSize.GetAllocatedSize(file.FullName, size, attributes),
                                LastModified = lastWrite,
                                Category = annotation.Category,
                                Risk = annotation.Risk,
                                Note = annotation.Note
                            });
                            largeFilesFound++;
                        }

                        if (filesSeen % 1500 == 0 || lastProgress.ElapsedMilliseconds >= 250)
                        {
                            progress?.Report(new ScanProgress(filesSeen, bytesSeen, largeFilesFound, skippedDirectories, currentPath));
                            lastProgress.Restart();
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    skippedDirectories++;
                }
                catch (IOException)
                {
                    skippedDirectories++;
                }
                catch (System.Security.SecurityException)
                {
                    skippedDirectories++;
                }
            }
        }

        foreach (var node in nodes.Values)
        {
            node.TotalBytes = node.DirectBytes;
            node.TotalFileCount = node.DirectFileCount;
        }

        foreach (var node in nodes.Values.OrderByDescending(n => Depth(n.Path)).ThenByDescending(n => n.Path.Length))
        {
            if (node.ParentPath is null || !nodes.TryGetValue(node.ParentPath, out var parent)) continue;
            parent.TotalBytes += node.TotalBytes;
            parent.TotalFileCount += node.TotalFileCount;
            parent.ThreeDBytes += node.ThreeDBytes;
            parent.VideoBytes += node.VideoBytes;
            parent.ArchiveBytes += node.ArchiveBytes;
            parent.AiModelBytes += node.AiModelBytes;
        }

        var folderThreshold = Math.Max(1L, thresholdBytes / 2);
        var folders = nodes.Values
            .Where(n => n.TotalBytes >= folderThreshold && !IsRoot(n.Path, n.Root))
            .Select(n =>
            {
                var info = FileClassifier.ClassifyFolder(n.Path, n.TotalBytes, n.ThreeDBytes, n.VideoBytes, n.ArchiveBytes, n.AiModelBytes);
                return new FolderSummary
                {
                    Path = n.Path,
                    SizeBytes = n.TotalBytes,
                    FileCount = n.TotalFileCount,
                    Category = info.Category,
                    Risk = info.Risk,
                    Note = info.Note,
                    ReviewForCleanup = info.CleanupCandidate
                };
            })
            .OrderByDescending(x => x.SizeBytes)
            .Take(1000)
            .ToList();

        files.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        var cleanup = folders.Where(f => f.ReviewForCleanup).OrderByDescending(f => f.SizeBytes).Take(300).ToList();

        sw.Stop();
        progress?.Report(new ScanProgress(filesSeen, bytesSeen, largeFilesFound, skippedDirectories, string.Empty));
        return new ScanResult
        {
            LargeFiles = files,
            LargeFolders = folders,
            CleanupCandidates = cleanup,
            FilesSeen = filesSeen,
            BytesSeen = bytesSeen,
            SkippedDirectories = skippedDirectories,
            Elapsed = sw.Elapsed
        };
    }

    private static void EnsureNode(Dictionary<string, FolderNode> nodes, string path, string? parent, string root)
    {
        var normalized = NormalizeRoot(path);
        if (nodes.ContainsKey(normalized)) return;
        nodes[normalized] = new FolderNode { Path = normalized, ParentPath = parent is null ? null : NormalizeRoot(parent), Root = NormalizeRoot(root) };
    }

    private static string NormalizeRoot(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (!string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full;
    }

    private static bool IsRoot(string path, string root) => string.Equals(NormalizeRoot(path), NormalizeRoot(root), StringComparison.OrdinalIgnoreCase);

    private static int Depth(string path)
    {
        var depth = 0;
        foreach (var c in path)
            if (c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar) depth++;
        return depth;
    }
}
