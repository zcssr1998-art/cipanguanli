using System.IO;

namespace Cipanguanli.Core;

public static class MigrationService
{
    public static Task<MigrationResult> MigrateAsync(
        string sourcePath,
        string destinationDirectory,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Migrate(sourcePath, destinationDirectory, progress, cancellationToken), cancellationToken);
    }

    private static MigrationResult Migrate(
        string sourcePath,
        string destinationDirectory,
        IProgress<MigrationProgress>? progress,
        CancellationToken cancellationToken)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        destinationDirectory = Path.GetFullPath(destinationDirectory);
        var sourceIsFile = File.Exists(sourcePath);
        var sourceIsDirectory = Directory.Exists(sourcePath);
        if (!sourceIsFile && !sourceIsDirectory) throw new FileNotFoundException("源文件或目录不存在。", sourcePath);
        Directory.CreateDirectory(destinationDirectory);

        if (sourceIsDirectory && IsInside(destinationDirectory, sourcePath))
            throw new InvalidOperationException("目标目录不能位于源目录内部。请选择其他磁盘或上级目录。");

        var name = sourceIsFile ? Path.GetFileName(sourcePath) : new DirectoryInfo(sourcePath).Name;
        var destinationPath = GetUniqueDestination(destinationDirectory, name, sourceIsFile);
        cancellationToken.ThrowIfCancellationRequested();

        if (SameVolume(sourcePath, destinationPath))
        {
            long bytes;
            long files;
            if (sourceIsFile)
            {
                bytes = new FileInfo(sourcePath).Length;
                files = 1;
                File.Move(sourcePath, destinationPath);
            }
            else
            {
                (bytes, files) = MeasureDirectory(sourcePath, cancellationToken);
                Directory.Move(sourcePath, destinationPath);
            }
            progress?.Report(new MigrationProgress(files, bytes, destinationPath));
            return new MigrationResult
            {
                SourcePath = sourcePath,
                DestinationPath = destinationPath,
                BytesMoved = bytes,
                FilesMoved = files,
                SourceRemoved = true
            };
        }

        if (sourceIsFile)
            return CopyFileThenDelete(sourcePath, destinationPath, progress, cancellationToken);
        return CopyDirectoryThenDelete(sourcePath, destinationPath, progress, cancellationToken);
    }

    private static MigrationResult CopyFileThenDelete(string source, string destination, IProgress<MigrationProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        File.Copy(source, destination, overwrite: false);
        var sourceLength = new FileInfo(source).Length;
        var destinationLength = new FileInfo(destination).Length;
        if (sourceLength != destinationLength)
            throw new IOException("复制后的文件大小校验失败，源文件保留未删除。");
        File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
        ct.ThrowIfCancellationRequested();
        File.Delete(source);
        progress?.Report(new MigrationProgress(1, sourceLength, destination));
        return new MigrationResult
        {
            SourcePath = source,
            DestinationPath = destination,
            BytesMoved = sourceLength,
            FilesMoved = 1,
            SourceRemoved = true
        };
    }

    private static MigrationResult CopyDirectoryThenDelete(string source, string destination, IProgress<MigrationProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        long bytes = 0;
        long files = 0;
        var stack = new Stack<(string Source, string Destination)>();
        stack.Push((source, destination));

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();
            Directory.CreateDirectory(current.Destination);

            foreach (var directory in Directory.EnumerateDirectories(current.Source))
            {
                ct.ThrowIfCancellationRequested();
                var info = new DirectoryInfo(directory);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                stack.Push((directory, Path.Combine(current.Destination, info.Name)));
            }

            foreach (var file in Directory.EnumerateFiles(current.Source))
            {
                ct.ThrowIfCancellationRequested();
                var sourceInfo = new FileInfo(file);
                if ((sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                var destinationFile = Path.Combine(current.Destination, sourceInfo.Name);
                File.Copy(file, destinationFile, overwrite: false);
                var destinationInfo = new FileInfo(destinationFile);
                if (destinationInfo.Length != sourceInfo.Length)
                    throw new IOException($"文件校验失败：{file}。源目录保留未删除。");
                File.SetLastWriteTimeUtc(destinationFile, sourceInfo.LastWriteTimeUtc);
                bytes += sourceInfo.Length;
                files++;
                progress?.Report(new MigrationProgress(files, bytes, file));
            }
        }

        ct.ThrowIfCancellationRequested();
        var (destinationBytes, destinationFiles) = MeasureDirectory(destination, ct);
        if (destinationBytes != bytes || destinationFiles != files)
            throw new IOException("迁移目录完整性校验失败，源目录保留未删除。");

        var sourceRemoved = false;
        try
        {
            Directory.Delete(source, recursive: true);
            sourceRemoved = true;
        }
        catch
        {
            // Copy is verified. Keep both copies rather than risking data loss.
        }

        return new MigrationResult
        {
            SourcePath = source,
            DestinationPath = destination,
            BytesMoved = bytes,
            FilesMoved = files,
            SourceRemoved = sourceRemoved
        };
    }

    private static (long Bytes, long Files) MeasureDirectory(string path, CancellationToken ct)
    {
        long bytes = 0;
        long files = 0;
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(path));
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();
            try
            {
                foreach (var directory in current.EnumerateDirectories())
                {
                    if ((directory.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(directory);
                }
                foreach (var file in current.EnumerateFiles())
                {
                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    bytes += file.Length;
                    files++;
                }
            }
            catch (UnauthorizedAccessException) { }
        }
        return (bytes, files);
    }

    private static bool SameVolume(string source, string destination)
    {
        var a = Path.GetPathRoot(source)?.TrimEnd('\\', '/');
        var b = Path.GetPathRoot(destination)?.TrimEnd('\\', '/');
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInside(string candidate, string parent)
    {
        var normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUniqueDestination(string destinationDirectory, string name, bool isFile)
    {
        var candidate = Path.Combine(destinationDirectory, name);
        if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;

        var baseName = isFile ? Path.GetFileNameWithoutExtension(name) : name;
        var extension = isFile ? Path.GetExtension(name) : string.Empty;
        for (var i = 1; i < 10_000; i++)
        {
            candidate = Path.Combine(destinationDirectory, $"{baseName}_迁移_{i}{extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        throw new IOException("目标目录中重名文件过多，无法生成安全的新名称。");
    }
}
