using System.Windows;
using System.Windows.Media;

namespace MiniDiskLab.App;

/// <summary>可用的界面主题。</summary>
public enum AppTheme
{
    Light,
    Dark,
}

/// <summary>
/// 通过替换应用级 <see cref="SolidColorBrush"/> 资源来应用浅色/深色主题。
/// 所有界面控件都通过 <c>DynamicResource</c> 引用这些画刷，
/// 因此切换主题会立即生效，无需重启。
/// </summary>
public static class ThemeManager
{
    private static readonly IReadOnlyDictionary<string, (string Light, string Dark)> Palette =
        new Dictionary<string, (string, string)>
        {
            ["WindowBackgroundBrush"] = ("#FFF7F8FA", "#FF17191C"),
            ["PanelBackgroundBrush"] = ("#FFFFFFFF", "#FF22252A"),
            ["BorderBrush"] = ("#FFD8DEE6", "#FF3A3F46"),
            ["PrimaryTextBrush"] = ("#FF1F2430", "#FFE8EAED"),
            ["SecondaryTextBrush"] = ("#FF5C6570", "#FF9AA1AA"),
            ["AccentBrush"] = ("#FF2F6FED", "#FF4C8DFF"),
            ["GridHeaderBrush"] = ("#FFEEF2F7", "#FF2B2F35"),
        };

    /// <summary>当前主题。</summary>
    public static AppTheme Current { get; private set; } = AppTheme.Light;

    /// <summary>应用主题。</summary>
    public static void Apply(AppTheme theme)
    {
        Current = theme;
        var resources = WpfApplication.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        foreach (var (key, colors) in Palette)
        {
            var hex = theme == AppTheme.Dark ? colors.Dark : colors.Light;
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
            brush.Freeze();
            resources[key] = brush;
        }
    }

    /// <summary>在浅色与深色之间切换。</summary>
    public static AppTheme Toggle()
    {
        var next = Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
        Apply(next);
        return next;
    }
}
