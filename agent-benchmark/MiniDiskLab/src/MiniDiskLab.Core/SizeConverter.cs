using System.Globalization;
using MiniDiskLab.Core.Models;

namespace MiniDiskLab.Core;

/// <summary>
/// 大小格式化和单位换算。刻意设计为静态且无状态，
/// 以便核心逻辑可以在不包含 WPF 的情况下进行测试。
/// </summary>
public static class SizeConverter
{
    /// <summary>1 KB 对应的字节数（1024 进制）。</summary>
    public const long Kilobyte = 1024L;

    /// <summary>1 MB 对应的字节数。</summary>
    public const long Megabyte = Kilobyte * 1024L;

    /// <summary>1 GB 对应的字节数。</summary>
    public const long Gigabyte = Megabyte * 1024L;

    /// <summary>1 TB 对应的字节数。</summary>
    public const long Terabyte = Gigabyte * 1024L;

    /// <summary>
    /// 将某个单位的数值换算为字节。
    /// </summary>
    /// <param name="value">数值，例如 500。</param>
    /// <param name="unit">该数值的单位。</param>
    /// <returns>等价于该数值的字节数（四舍五入到最接近的整数）。</returns>
    /// <exception cref="ArgumentOutOfRangeException">当单位未知或数值为负数/NaN/无穷大时抛出。</exception>
    public static long UnitToBytes(double value, SizeUnit unit)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "阈值必须是有限数值。");
        }

        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "阈值不能为负数。");
        }

        var multiplier = unit switch
        {
            SizeUnit.KB => Kilobyte,
            SizeUnit.MB => Megabyte,
            SizeUnit.GB => Gigabyte,
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "未知的大小单位。"),
        };

        return (long)Math.Round(value * multiplier, MidpointRounding.AwayFromZero);
    }

    /// <summary>返回单位的显示标签（<c>KB</c>/<c>MB</c>/<c>GB</c>）。</summary>
    public static string UnitLabel(SizeUnit unit) => unit switch
    {
        SizeUnit.KB => "KB",
        SizeUnit.MB => "MB",
        SizeUnit.GB => "GB",
        _ => "B",
    };

    /// <summary>
    /// 将字节格式化为人类可读的字符串，并使用 1024 进制单位。
    /// 小于 1 KB 的数值显示为原始字节数。
    /// </summary>
    /// <example>
    /// <code>
    /// SizeConverter.Format(1024)          // "1.00 KB"
    /// SizeConverter.Format(1_048_576)     // "1.00 MB"
    /// SizeConverter.Format(1_073_741_824) // "1.00 GB"
    /// </code>
    /// </example>
    public static string Format(long bytes)
    {
        if (bytes < 0)
        {
            return "-" + Format(-bytes);
        }

        if (bytes < Kilobyte)
        {
            return bytes.ToString("N0", CultureInfo.InvariantCulture) + " B";
        }

        if (bytes < Megabyte)
        {
            return FormatValue(bytes / (double)Kilobyte, "KB");
        }

        if (bytes < Gigabyte)
        {
            return FormatValue(bytes / (double)Megabyte, "MB");
        }

        if (bytes < Terabyte)
        {
            return FormatValue(bytes / (double)Gigabyte, "GB");
        }

        return FormatValue(bytes / (double)Terabyte, "TB");
    }

    /// <summary>
    /// 将字节解析为最适合速览的 (数值, 单位) 组合。
    /// </summary>
    public static (double Value, string Unit) ToBestUnit(long bytes)
    {
        if (bytes < Kilobyte) return (bytes, "B");
        if (bytes < Megabyte) return (bytes / (double)Kilobyte, "KB");
        if (bytes < Gigabyte) return (bytes / (double)Megabyte, "MB");
        if (bytes < Terabyte) return (bytes / (double)Gigabyte, "GB");
        return (bytes / (double)Terabyte, "TB");
    }

    private static string FormatValue(double value, string unit) =>
        value.ToString("0.##", CultureInfo.InvariantCulture) + " " + unit;
}
