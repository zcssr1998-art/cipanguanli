using System.IO;
using System.Text.Json;

namespace Cipanguanli.Core;

public sealed class HistoryService
{
    private readonly string _path;

    public HistoryService(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cipanguanli", "history.json");
    }

    public IReadOnlyList<GrowthEntry> CompareAndSave(ScanResult current)
    {
        var previous = Load();
        var currentSnapshot = BuildSnapshot(current);
        var growth = Compare(previous, currentSnapshot);
        Save(currentSnapshot);
        return growth;
    }

    internal IReadOnlyList<GrowthEntry> Compare(HistorySnapshot? previous, HistorySnapshot current)
    {
        if (previous is null) return Array.Empty<GrowthEntry>();
        var previousMap = previous.Folders.ToDictionary(x => x.Path, x => x.SizeBytes, StringComparer.OrdinalIgnoreCase);
        var results = new List<GrowthEntry>();
        foreach (var folder in current.Folders)
        {
            var before = previousMap.GetValueOrDefault(folder.Path, 0);
            var delta = folder.SizeBytes - before;
            var meaningful = delta >= 64L * 1024 * 1024 && (before == 0 || delta >= before * 0.05);
            if (!meaningful) continue;
            results.Add(new GrowthEntry
            {
                Path = folder.Path,
                PreviousBytes = before,
                CurrentBytes = folder.SizeBytes
            });
        }
        return results.OrderByDescending(x => x.GrowthBytes).Take(200).ToArray();
    }

    private HistorySnapshot BuildSnapshot(ScanResult result)
    {
        return new HistorySnapshot
        {
            CapturedUtc = DateTime.UtcNow,
            TotalBytes = result.BytesSeen,
            Folders = result.LargeFolders
                .OrderByDescending(x => x.SizeBytes)
                .Take(1500)
                .Select(x => new HistoryFolderEntry { Path = x.Path, SizeBytes = x.SizeBytes })
                .ToList()
        };
    }

    private HistorySnapshot? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return JsonSerializer.Deserialize<HistorySnapshot>(File.ReadAllText(_path));
        }
        catch { return null; }
    }

    private void Save(HistorySnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    internal sealed class HistorySnapshot
    {
        public DateTime CapturedUtc { get; set; }
        public long TotalBytes { get; set; }
        public List<HistoryFolderEntry> Folders { get; set; } = [];
    }

    internal sealed class HistoryFolderEntry
    {
        public string Path { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
    }
}
