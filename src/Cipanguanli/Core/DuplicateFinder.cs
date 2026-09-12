using System.IO;
using System.Security.Cryptography;

namespace Cipanguanli.Core;

public sealed class DuplicateFinder
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public Task<IReadOnlyList<DuplicateFileGroup>> FindAsync(
        IEnumerable<string> roots,
        long minimumFileSizeBytes,
        IProgress<DuplicateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (minimumFileSizeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(minimumFileSizeBytes));
        var normalizedRoots = roots
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedRoots.Length == 0) throw new ArgumentException("At least one existing root is required.", nameof(roots));
        return Task.Run(() => Find(normalizedRoots, minimumFileSizeBytes, progress, cancellationToken), cancellationToken);
    }

    private static IReadOnlyList<DuplicateFileGroup> Find(
        IReadOnlyList<string> roots,
        long minimumFileSizeBytes,
        IProgress<DuplicateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var bySize = new Dictionary<long, List<string>>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long filesSeen = 0;
        long candidates = 0;
        long hashed = 0;
        long groupsFound = 0;

        foreach (var root in roots)
        {
            var stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(root));
            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = stack.Pop();
                try
                {
                    foreach (var entry in directory.EnumerateFileSystemInfos("*", Options))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (entry is DirectoryInfo child)
                        {
                            stack.Push(child);
                            continue;
                        }
                        if (entry is not FileInfo file) continue;

                        filesSeen++;
                        long size;
                        try { size = file.Length; }
                        catch { continue; }
                        if (size < minimumFileSizeBytes) continue;
                        if (!seenPaths.Add(file.FullName)) continue;

                        if (!bySize.TryGetValue(size, out var list))
                        {
                            list = [];
                            bySize[size] = list;
                        }
                        list.Add(file.FullName);
                        candidates++;

                        if (filesSeen % 2000 == 0)
                            progress?.Report(new DuplicateProgress(filesSeen, candidates, hashed, groupsFound, directory.FullName));
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
                catch (System.Security.SecurityException) { }
            }
        }

        var results = new List<DuplicateFileGroup>();
        foreach (var sizeGroup in bySize.Where(x => x.Value.Count > 1).OrderByDescending(x => x.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var byHash = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var path in sizeGroup.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var hash = HashFile(path, cancellationToken);
                    hashed++;
                    if (!byHash.TryGetValue(hash, out var list))
                    {
                        list = [];
                        byHash[hash] = list;
                    }
                    list.Add(path);
                    progress?.Report(new DuplicateProgress(filesSeen, candidates, hashed, groupsFound, path));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            foreach (var hashGroup in byHash.Where(x => x.Value.Count > 1))
            {
                results.Add(new DuplicateFileGroup
                {
                    Hash = hashGroup.Key,
                    FileSizeBytes = sizeGroup.Key,
                    Paths = hashGroup.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
                });
                groupsFound++;
            }
        }

        progress?.Report(new DuplicateProgress(filesSeen, candidates, hashed, groupsFound, string.Empty));
        return results.OrderByDescending(x => x.ReclaimableBytes).ToArray();
    }

    private static string HashFile(string path, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, FileOptions.SequentialScan);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
