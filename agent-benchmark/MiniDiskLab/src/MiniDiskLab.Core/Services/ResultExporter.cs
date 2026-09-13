using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniDiskLab.Core.Models;

namespace MiniDiskLab.Core.Services;

/// <summary>
/// 将扫描结果写入 CSV 和 JSON。
/// </summary>
public static class ResultExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// 导出为带 UTF-8 BOM 的 CSV，以便 Excel 在中文 Windows 上能正确打开。
    /// </summary>
    public static void ExportCsv(ScanSummary summary, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        EnsureParentDirectory(outputPath);

        var sb = new StringBuilder();
        sb.AppendLine("大小(字节),大小,类型,类别,说明,文件名,完整路径,修改时间");

        foreach (var item in summary.Items)
        {
            sb.Append(Csv(item.SizeBytes.ToString(CultureInfo.InvariantCulture))).Append(',');
            sb.Append(Csv(item.SizeText)).Append(',');
            sb.Append(Csv(GetCategoryLabel(item.Category))).Append(',');
            sb.Append(Csv(item.Category.ToString())).Append(',');
            sb.Append(Csv(item.Description)).Append(',');
            sb.Append(Csv(item.FileName)).Append(',');
            sb.Append(Csv(item.FullPath)).Append(',');
            sb.Append(Csv(item.LastWriteTime?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty));
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine(string.Join(",", Csv("汇总"), Csv($"扫描目录={summary.RootPath}"), Csv($"阈值={summary.Threshold}")));
        sb.AppendLine(string.Join(",", new[]
        {
            Csv("统计"),
            Csv($"已扫描文件={summary.ScannedFiles}"),
            Csv($"大文件={summary.Items.Count}"),
            Csv($"跳过目录={summary.SkippedDirectories}"),
            Csv($"跳过链接={summary.SkippedLinks}"),
            Csv($"错误={summary.ErrorCount}"),
            Csv($"耗时={summary.Elapsed.TotalSeconds:0.00}s"),
            Csv($"已取消={summary.WasCancelled}"),
        }));

        // UTF-8 BOM 能让 Excel 识别编码。
        File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>导出为 JSON。</summary>
    public static void ExportJson(ScanSummary summary, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        EnsureParentDirectory(outputPath);

        var payload = new
        {
            generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            rootPath = summary.RootPath,
            threshold = new { value = summary.Threshold.Value, unit = summary.Threshold.Unit.ToString() },
            stats = new
            {
                scannedFiles = summary.ScannedFiles,
                scannedDirectories = summary.ScannedDirectories,
                largeFileCount = summary.Items.Count,
                skippedDirectories = summary.SkippedDirectories,
                skippedLinks = summary.SkippedLinks,
                errorCount = summary.ErrorCount,
                wasCancelled = summary.WasCancelled,
                elapsedSeconds = Math.Round(summary.Elapsed.TotalSeconds, 3),
            },
            extensionStats = summary.ExtensionStats.Select(s => new
            {
                extension = s.Extension,
                fileCount = s.FileCount,
                totalBytes = s.TotalBytes,
                totalSizeText = s.TotalSizeText,
            }),
            topDirectories = summary.TopDirectories.Select(d => new
            {
                path = d.Path,
                totalBytes = d.TotalBytes,
                fileCount = d.FileCount,
                totalSizeText = d.TotalSizeText,
            }),
            items = summary.Items.Select(i => new
            {
                fileName = i.FileName,
                fullPath = i.FullPath,
                sizeBytes = i.SizeBytes,
                sizeText = i.SizeText,
                extension = i.Extension,
                category = i.Category.ToString(),
                categoryLabel = GetCategoryLabel(i.Category),
                description = i.Description,
                lastWriteTime = i.LastWriteTime?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            }),
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        File.WriteAllText(outputPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>类别的中文显示标签。</summary>
    public static string GetCategoryLabel(FileCategory category) => category switch
    {
        FileCategory.Video => "视频",
        FileCategory.Image => "图片",
        FileCategory.Archive => "压缩包",
        FileCategory.GameResource => "游戏资源",
        FileCategory.Steam => "Steam",
        FileCategory.TencentVideo => "腾讯视频",
        FileCategory.AdobeCache => "Adobe 缓存",
        FileCategory.UnrealEngine => "Unreal Engine",
        FileCategory.Unity => "Unity",
        FileCategory.Maya => "Maya",
        FileCategory.Blender => "Blender",
        FileCategory.Model3D => "3D 模型资源",
        FileCategory.Installer => "安装包",
        FileCategory.IsoImage => "ISO 镜像",
        FileCategory.Log => "日志",
        FileCategory.TempFile => "临时文件",
        _ => "未知文件",
    };

    private static void EnsureParentDirectory(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static string Csv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsQuoting = value.Contains(',') || value.Contains('"')
            || value.Contains('\n') || value.Contains('\r');

        if (!needsQuoting)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
