using System.IO;
using System.Text.Json;

namespace Cipanguanli.Core;

public sealed class QuarantineService
{
    private readonly string _root;

    public QuarantineService(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cipanguanli", "Quarantine");
    }

    public IReadOnlyList<QuarantineItem> List()
    {
        try
        {
            if (!Directory.Exists(_root)) return Array.Empty<QuarantineItem>();
            return Directory.EnumerateFiles(_root, "*.json", SearchOption.TopDirectoryOnly)
                .Select(ReadManifest)
                .Where(x => x is not null)
                .Cast<QuarantineItem>()
                .OrderByDescending(x => x.CreatedUtc)
                .ToArray();
        }
        catch { return Array.Empty<QuarantineItem>(); }
    }

    public async Task<QuarantineItem> QuarantineAsync(
        string sourcePath,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsProtectedSystemPath(sourcePath)) throw new InvalidOperationException("系统关键文件禁止进入隔离区。");
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath)) throw new FileNotFoundException("要隔离的文件或目录不存在。", sourcePath);

        Directory.CreateDirectory(_root);
        var id = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "_" + Guid.NewGuid().ToString("N")[..8];
        var itemDir = Path.Combine(_root, id);
        Directory.CreateDirectory(itemDir);
        var sourceIsDirectory = Directory.Exists(sourcePath);

        var migration = await MigrationService.MigrateAsync(sourcePath, itemDir, progress, cancellationToken);
        if (!migration.SourceRemoved)
            throw new IOException("目标副本已生成，但源未能移除。为避免产生‘已隔离’错觉，本次不登记为隔离项。请人工检查两份数据。");

        var item = new QuarantineItem
        {
            Id = id,
            OriginalPath = Path.GetFullPath(sourcePath),
            QuarantinePath = migration.DestinationPath,
            CreatedUtc = DateTime.UtcNow,
            IsDirectory = sourceIsDirectory,
            SizeBytes = migration.BytesMoved
        };
        WriteManifest(item);
        return item;
    }

    public async Task RestoreAsync(
        QuarantineItem item,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(item.QuarantinePath) && !Directory.Exists(item.QuarantinePath))
            throw new FileNotFoundException("隔离区中的数据已不存在。", item.QuarantinePath);
        if (File.Exists(item.OriginalPath) || Directory.Exists(item.OriginalPath))
            throw new IOException("原位置已经存在同名文件/目录，为避免覆盖，恢复已停止。请先处理原位置内容。");

        var parent = Path.GetDirectoryName(item.OriginalPath);
        if (string.IsNullOrWhiteSpace(parent)) throw new IOException("无法确定原始目录。");
        Directory.CreateDirectory(parent);
        var migration = await MigrationService.MigrateAsync(item.QuarantinePath, parent, progress, cancellationToken);
        if (!migration.SourceRemoved) throw new IOException("恢复副本已生成，但隔离区源未能移除。请人工检查，清单暂时保留。");
        TryDeleteManifest(item.Id);
        TryDeleteEmptyDirectory(Path.GetDirectoryName(item.QuarantinePath));
    }

    public void ForgetMissingItems()
    {
        foreach (var item in List())
        {
            if (!File.Exists(item.QuarantinePath) && !Directory.Exists(item.QuarantinePath)) TryDeleteManifest(item.Id);
        }
    }

    private QuarantineItem? ReadManifest(string file)
    {
        try { return JsonSerializer.Deserialize<QuarantineItem>(File.ReadAllText(file)); }
        catch { return null; }
    }

    private void WriteManifest(QuarantineItem item)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, item.Id + ".json"),
            JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void TryDeleteManifest(string id)
    {
        try
        {
            var path = Path.Combine(_root, id + ".json");
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static void TryDeleteEmptyDirectory(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
        }
        catch { }
    }

    private static bool IsProtectedSystemPath(string path)
    {
        var name = Path.GetFileName(path);
        if (new[] { "pagefile.sys", "hiberfil.sys", "swapfile.sys" }.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
        var lower = path.Replace('/', '\\').ToLowerInvariant();
        return lower.Contains("\\windows\\system32\\") || lower.Contains("\\system volume information\\");
    }
}
