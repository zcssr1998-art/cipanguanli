using System.IO;
using System.Text.Json;

namespace Cipanguanli.Core;

public sealed class LearnedRuleStore
{
    private readonly string _path;
    private readonly object _gate = new();

    public LearnedRuleStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cipanguanli", "learned-rules.json");
    }

    public IReadOnlyList<LearnedRule> Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path)) return Array.Empty<LearnedRule>();
                return JsonSerializer.Deserialize<List<LearnedRule>>(File.ReadAllText(_path)) ?? [];
            }
            catch { return Array.Empty<LearnedRule>(); }
        }
    }

    public LearnedRule? Resolve(string path)
    {
        var full = Normalize(path);
        return Load()
            .Where(r => IsUnder(full, Normalize(r.PathPrefix)))
            .OrderByDescending(r => Normalize(r.PathPrefix).Length)
            .FirstOrDefault();
    }

    public void SaveRule(string pathPrefix, string category, string note)
    {
        if (string.IsNullOrWhiteSpace(pathPrefix)) throw new ArgumentException("路径不能为空。", nameof(pathPrefix));
        if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("用途名称不能为空。", nameof(category));
        lock (_gate)
        {
            var list = Load().ToList();
            var normalized = Normalize(pathPrefix);
            list.RemoveAll(r => Normalize(r.PathPrefix).Equals(normalized, StringComparison.OrdinalIgnoreCase));
            list.Add(new LearnedRule
            {
                PathPrefix = pathPrefix,
                Category = category.Trim(),
                Note = note.Trim(),
                CreatedUtc = DateTime.UtcNow
            });
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd('\\', '/'); }
        catch { return path.TrimEnd('\\', '/'); }
    }

    private static bool IsUnder(string path, string root)
    {
        if (path.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
        return path.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
