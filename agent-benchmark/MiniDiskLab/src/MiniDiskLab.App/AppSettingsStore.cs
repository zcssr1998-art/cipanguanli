using System.IO;
using System.Text.Json;
using MiniDiskLab.Core.Models;

namespace MiniDiskLab.App;

/// <summary>跨会话持久化的界面设置。</summary>
public sealed class AppSettings
{
    /// <summary>上次扫描的目录。</summary>
    public string? LastDirectory { get; set; }

    /// <summary>上次使用的阈值数值。</summary>
    public double LastThresholdValue { get; set; } = 500;

    /// <summary>上次使用的阈值单位（默认为 MB）。</summary>
    public SizeUnit LastThresholdUnit { get; set; } = SizeUnit.MB;
}

/// <summary>
/// 在 <c>%LOCALAPPDATA%\MiniDiskLab\settings.json</c> 中读写设置。
/// 任何失败都静默降级为默认值——设置不是关键功能，
/// 不应因为写不了配置文件就阻塞应用。
/// </summary>
public static class AppSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MiniDiskLab",
        "settings.json");

    /// <summary>加载设置；失败时返回默认值。</summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    /// <summary>保存设置；失败时静默忽略。</summary>
    public static void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(
                SettingsPath,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 设置持久化失败不影响主流程。
        }
    }
}
