using System.IO;
using System.Text.Json;

namespace Cipanguanli.Core;

public sealed class CDriveHistoryService
{
    private readonly string _path;

    public CDriveHistoryService(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cipanguanli", "cdrive-history.json");
    }

    public IReadOnlyList<CDriveGrowthEntry> CompareAndSave(IEnumerable<(string Name, string Path, long SizeBytes)> entries)
    {
        var current = entries
            .Where(x => !string.IsNullOrWhiteSpace(x.Path) && x.SizeBytes >= 0)
            .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SnapshotItem { Name = g.First().Name, Path = g.Key, SizeBytes = g.Max(x => x.SizeBytes) })
            .OrderByDescending(x => x.SizeBytes)
            .Take(1200)
            .ToList();

        var previous = Load();
        var result = new List<CDriveGrowthEntry>();
        if (previous is not null)
        {
            var map = previous.Items.ToDictionary(x => x.Path, x => x.SizeBytes, StringComparer.OrdinalIgnoreCase);
            foreach (var item in current)
            {
                var before = map.GetValueOrDefault(item.Path, 0);
                var delta = item.SizeBytes - before;
                if (delta < 64L * 1024 * 1024) continue;
                if (before > 0 && delta < before * 0.05) continue;
                result.Add(new CDriveGrowthEntry
                {
                    Path = item.Path,
                    Name = item.Name,
                    PreviousBytes = before,
                    CurrentBytes = item.SizeBytes
                });
            }
        }

        Save(new Snapshot { CapturedUtc = DateTime.UtcNow, Items = current });
        return result.OrderByDescending(x => x.GrowthBytes).Take(200).ToArray();
    }

    private Snapshot? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(_path));
        }
        catch { return null; }
    }

    private void Save(Snapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private sealed class Snapshot
    {
        public DateTime CapturedUtc { get; set; }
        public List<SnapshotItem> Items { get; set; } = [];
    }

    private sealed class SnapshotItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
    }
}
