using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Cipanguanli.Core;

public sealed class SoftwareOwnershipService
{
    private readonly IReadOnlyList<InstalledProduct> _products;

    public SoftwareOwnershipService(IReadOnlyList<InstalledProduct>? products = null)
    {
        _products = products ?? DiscoverInstalledProducts();
    }

    public IReadOnlyList<InstalledProduct> Products => _products;

    public OwnershipMatch Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return OwnershipMatch.Unknown;
        var full = SafeFullPath(path);
        InstalledProduct? best = null;
        var bestLength = -1;

        foreach (var product in _products)
        {
            if (string.IsNullOrWhiteSpace(product.InstallLocation)) continue;
            var root = SafeFullPath(product.InstallLocation).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (root.Length < 3) continue;
            if (!IsInsideOrEqual(full, root)) continue;
            if (root.Length <= bestLength) continue;
            best = product;
            bestLength = root.Length;
        }

        if (best is null) return OwnershipMatch.Unknown;
        return new OwnershipMatch(best.DisplayName, best.Publisher, best.InstallLocation,
            best.UninstallCommand, best.Source, "高");
    }

    public static IReadOnlyList<InstalledProduct> DiscoverInstalledProducts()
    {
        var products = new List<InstalledProduct>();
        ReadRegistryProducts(products, RegistryHive.LocalMachine, RegistryView.Registry64);
        ReadRegistryProducts(products, RegistryHive.LocalMachine, RegistryView.Registry32);
        ReadRegistryProducts(products, RegistryHive.CurrentUser, RegistryView.Registry64);
        ReadRegistryProducts(products, RegistryHive.CurrentUser, RegistryView.Registry32);
        ReadSteam(products);
        ReadEpic(products);

        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.DisplayName))
            .GroupBy(p => $"{p.Source}|{p.DisplayName}|{NormalizeLocation(p.InstallLocation)}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(p => p.InstallLocation?.Length ?? 0)
            .ToArray();
    }

    private static void ReadRegistryProducts(List<InstalledProduct> target, RegistryHive hive, RegistryView view)
    {
        const string uninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(uninstallKey);
            if (root is null) return;
            foreach (var name in root.GetSubKeyNames())
            {
                try
                {
                    using var key = root.OpenSubKey(name);
                    if (key is null) continue;
                    var displayName = key.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName)) continue;
                    var install = key.GetValue("InstallLocation") as string ?? string.Empty;
                    var publisher = key.GetValue("Publisher") as string ?? string.Empty;
                    var uninstall = key.GetValue("UninstallString") as string ?? string.Empty;
                    target.Add(new InstalledProduct(displayName.Trim(), publisher.Trim(), install.Trim(), uninstall.Trim(), "Windows 安装项", name));
                }
                catch { }
            }
        }
        catch { }
    }

    private static void ReadSteam(List<InstalledProduct> target)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var steamPath = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(steamPath)) roots.Add(steamPath.Replace('/', '\\'));
        }
        catch { }

        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(pf86)) roots.Add(Path.Combine(pf86, "Steam"));

        foreach (var root in roots.ToArray())
        {
            var libraryFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile)) continue;
            try
            {
                var text = File.ReadAllText(libraryFile);
                foreach (Match match in Regex.Matches(text, "\\\"path\\\"\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                {
                    var value = match.Groups[1].Value.Replace(@"\\", @"\");
                    if (!string.IsNullOrWhiteSpace(value)) roots.Add(value);
                }
            }
            catch { }
        }

        foreach (var root in roots)
        {
            var steamApps = Path.Combine(root, "steamapps");
            if (!Directory.Exists(steamApps)) continue;
            IEnumerable<string> manifests;
            try { manifests = Directory.EnumerateFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly).ToArray(); }
            catch { continue; }

            foreach (var manifest in manifests)
            {
                try
                {
                    var text = File.ReadAllText(manifest);
                    var displayName = ReadAcf(text, "name");
                    var installDir = ReadAcf(text, "installdir");
                    var appId = ReadAcf(text, "appid");
                    if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(installDir)) continue;
                    var installLocation = Path.Combine(steamApps, "common", installDir);
                    target.Add(new InstalledProduct(displayName, "Valve / Steam", installLocation,
                        string.IsNullOrWhiteSpace(appId) ? string.Empty : $"steam://uninstall/{appId}", "Steam", appId));
                }
                catch { }
            }
        }
    }

    private static void ReadEpic(List<InstalledProduct> target)
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData)) return;
        var manifests = Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) return;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(manifests, "*.item", SearchOption.TopDirectoryOnly).ToArray(); }
        catch { return; }

        foreach (var file in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                var displayName = ReadJsonString(root, "DisplayName");
                var install = ReadJsonString(root, "InstallLocation");
                var id = ReadJsonString(root, "CatalogItemId");
                if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(install)) continue;
                target.Add(new InstalledProduct(displayName, "Epic Games", install, string.Empty, "Epic Games", id));
            }
            catch { }
        }
    }

    private static string ReadJsonString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static string ReadAcf(string text, string key)
    {
        var match = Regex.Match(text, $"\\\"{Regex.Escape(key)}\\\"\\s*\\\"([^\\\"]*)\\\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string SafeFullPath(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return path.TrimEnd('\\', '/'); }
    }

    private static bool IsInsideOrEqual(string path, string root)
    {
        if (path.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLocation(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return SafeFullPath(path).ToLowerInvariant();
    }
}
