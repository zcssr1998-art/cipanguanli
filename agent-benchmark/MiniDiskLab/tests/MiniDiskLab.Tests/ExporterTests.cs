using System.Text;
using System.Text.Json;
using MiniDiskLab.Core.Classifiers;
using MiniDiskLab.Core.Models;
using MiniDiskLab.Core.Services;
using MiniDiskLab.Core.Utilities;

namespace MiniDiskLab.Tests;

/// <summary>
/// 导出功能测试（CSV / JSON）。
/// </summary>
public class ExporterTests : IDisposable
{
    private readonly string _workDir;

    public ExporterTests()
    {
        _workDir = Path.Combine(
            Path.GetTempPath(),
            "MiniDiskLabTests",
            "export",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workDir))
            {
                Directory.Delete(_workDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // 忽略清理失败。
        }
    }

    private static ScanSummary BuildSummary(params (string Path, long Size)[] files)
    {
        var classifier = new FileClassifier();
        var items = files
            .Select(f =>
            {
                var result = classifier.Classify(f.Path);
                return new ScanResultItem
                {
                    FileName = Path.GetFileName(f.Path),
                    FullPath = f.Path,
                    SizeBytes = f.Size,
                    Extension = Path.GetExtension(f.Path).ToLowerInvariant(),
                    Category = result.Category,
                    Description = result.Description,
                    LastWriteTime = new DateTime(2024, 5, 17, 10, 30, 0, DateTimeKind.Local),
                };
            })
            .OrderByDescending(i => i.SizeBytes)
            .ToList();

        return new ScanSummary
        {
            Items = items,
            ScannedFiles = items.Count,
            ScannedDirectories = 3,
            SkippedDirectories = 1,
            ErrorCount = 0,
            SkippedLinks = 0,
            Elapsed = TimeSpan.FromSeconds(1.5),
            WasCancelled = false,
            RootPath = @"D:\TestData",
            Threshold = new SizeThreshold(500, SizeUnit.MB),
            ExtensionStats = new List<ExtensionStat>
            {
                new(".mp4", 1, 600L * 1024 * 1024),
            },
            TopDirectories = new List<DirectoryStat>
            {
                new(@"D:\TestData\Tencent Video", 600L * 1024 * 1024, 1),
            },
        };
    }

    // ==========================================================
    //  CSV
    // ==========================================================

    [Fact]
    public void ExportCsv_CreatesFileWithHeaderAndRows()
    {
        var summary = BuildSummary(
            (@"D:\TestData\Tencent Video\Download\movie.mp4", 620L * 1024 * 1024),
            (@"D:\TestData\Random\unknown.xyz", 640L * 1024 * 1024));

        var path = Path.Combine(_workDir, "out.csv");
        ResultExporter.ExportCsv(summary, path);

        Assert.True(File.Exists(path));

        var lines = File.ReadAllLines(path, Encoding.UTF8);
        Assert.Contains("大小(字节)", lines[0]);
        Assert.Contains("完整路径", lines[0]);
        Assert.Contains(lines, l => l.Contains("movie.mp4"));
        Assert.Contains(lines, l => l.Contains("unknown.xyz"));
    }

    [Fact]
    public void ExportCsv_IncludesUtf8Bom_ForExcelCompatibility()
    {
        var summary = BuildSummary((@"D:\TestData\a.mp4", 1024L * 1024));
        var path = Path.Combine(_workDir, "bom.csv");

        ResultExporter.ExportCsv(summary, path);

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 3);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    [Fact]
    public void ExportCsv_EscapesCommasAndQuotes()
    {
        var summary = new ScanSummary
        {
            Items = new List<ScanResultItem>
            {
                new()
                {
                    FileName = "weird,name\"quoted\".bin",
                    FullPath = @"D:\TestData\weird,name""quoted"".bin",
                    SizeBytes = 2L * 1024 * 1024,
                    Extension = ".bin",
                    Category = FileCategory.Unknown,
                    Description = "包含逗号, 和 \"引号\" 的说明",
                },
            },
            RootPath = @"D:\TestData",
            Threshold = new SizeThreshold(1, SizeUnit.MB),
        };

        var path = Path.Combine(_workDir, "escape.csv");
        ResultExporter.ExportCsv(summary, path);

        var text = File.ReadAllText(path, Encoding.UTF8);

        // 含逗号的字段必须被双引号包裹，内部引号需转义为 ""。
        Assert.Contains("\"weird,name\"\"quoted\"\".bin\"", text);
    }

    [Fact]
    public void ExportCsv_EmptyResults_StillProducesValidFile()
    {
        var summary = new ScanSummary
        {
            Items = Array.Empty<ScanResultItem>(),
            RootPath = @"D:\Empty",
            Threshold = new SizeThreshold(500, SizeUnit.MB),
        };

        var path = Path.Combine(_workDir, "empty.csv");
        ResultExporter.ExportCsv(summary, path);

        Assert.True(File.Exists(path));
        Assert.Contains("大小(字节)", File.ReadAllText(path, Encoding.UTF8));
    }

    [Fact]
    public void ExportCsv_CreatesMissingParentDirectory()
    {
        var summary = BuildSummary((@"D:\TestData\a.mp4", 1024L * 1024));
        var path = Path.Combine(_workDir, "nested", "deeper", "out.csv");

        ResultExporter.ExportCsv(summary, path);

        Assert.True(File.Exists(path));
    }

    // ==========================================================
    //  JSON
    // ==========================================================

    [Fact]
    public void ExportJson_IsValidJsonWithExpectedShape()
    {
        var summary = BuildSummary(
            (@"D:\TestData\Tencent Video\Download\movie.mp4", 620L * 1024 * 1024));

        var path = Path.Combine(_workDir, "out.json");
        ResultExporter.ExportJson(summary, path);

        Assert.True(File.Exists(path));

        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var root = doc.RootElement;

        Assert.Equal(@"D:\TestData", root.GetProperty("rootPath").GetString());
        Assert.Equal(1, root.GetProperty("items").GetArrayLength());

        var item = root.GetProperty("items")[0];
        Assert.Equal("movie.mp4", item.GetProperty("fileName").GetString());
        Assert.Equal("TencentVideo", item.GetProperty("category").GetString());
        Assert.Equal("腾讯视频", item.GetProperty("categoryLabel").GetString());
        Assert.Contains("腾讯视频", item.GetProperty("description").GetString()!);
    }

    [Fact]
    public void ExportJson_IncludesStatistics()
    {
        var summary = BuildSummary((@"D:\TestData\a.mp4", 1024L * 1024));
        var path = Path.Combine(_workDir, "stats.json");

        ResultExporter.ExportJson(summary, path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var stats = doc.RootElement.GetProperty("stats");

        Assert.Equal(1, stats.GetProperty("scannedFiles").GetInt64());
        Assert.Equal(1, stats.GetProperty("largeFileCount").GetInt64());
        Assert.Equal(1, stats.GetProperty("skippedDirectories").GetInt64());
        Assert.False(stats.GetProperty("wasCancelled").GetBoolean());
    }

    [Fact]
    public void ExportJson_ChinesTextIsNotEscaped()
    {
        var summary = BuildSummary((@"D:\TestData\Downloads\windows.iso", 2L * 1024 * 1024));

        var path = Path.Combine(_workDir, "cn.json");
        ResultExporter.ExportJson(summary, path);

        var text = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("ISO", text);
        // 中文应保持原样，而不是 \uXXXX 转义。
        Assert.DoesNotContain("\\u", text);
    }

    [Fact]
    public void ExportJson_EmptyResults_ProducesEmptyArray()
    {
        var summary = new ScanSummary
        {
            Items = Array.Empty<ScanResultItem>(),
            RootPath = @"D:\Empty",
            Threshold = new SizeThreshold(500, SizeUnit.MB),
        };

        var path = Path.Combine(_workDir, "empty.json");
        ResultExporter.ExportJson(summary, path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void Exporters_RejectNullSummary()
    {
        Assert.Throws<ArgumentNullException>(
            () => ResultExporter.ExportCsv(null!, Path.Combine(_workDir, "x.csv")));
        Assert.Throws<ArgumentNullException>(
            () => ResultExporter.ExportJson(null!, Path.Combine(_workDir, "x.json")));
    }

    [Fact]
    public void Exporters_RejectEmptyPath()
    {
        var summary = BuildSummary((@"D:\TestData\a.mp4", 1024L * 1024));

        Assert.Throws<ArgumentException>(() => ResultExporter.ExportCsv(summary, "  "));
        Assert.Throws<ArgumentException>(() => ResultExporter.ExportJson(summary, "  "));
    }

    [Fact]
    public void ExportedCsvAndJson_ContainTheSameNumberOfItems()
    {
        var summary = BuildSummary(
            (@"D:\TestData\a.mp4", 3L * 1024 * 1024),
            (@"D:\TestData\b.pak", 2L * 1024 * 1024),
            (@"D:\TestData\c.iso", 4L * 1024 * 1024));

        var csvPath = Path.Combine(_workDir, "same.csv");
        var jsonPath = Path.Combine(_workDir, "same.json");

        ResultExporter.ExportCsv(summary, csvPath);
        ResultExporter.ExportJson(summary, jsonPath);

        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath, Encoding.UTF8));
        var jsonCount = doc.RootElement.GetProperty("items").GetArrayLength();

        // 只统计真正的数据行：CSV 末尾还有一行“汇总”和一行“统计”，
        // 它们同样包含扫描根路径，必须排除，否则会被误计为数据行。
        var csvLines = File.ReadAllLines(csvPath, Encoding.UTF8);
        var csvDataRows = csvLines
            .Skip(1)
            .Where(l => l.Length > 0)
            .Where(l => !l.StartsWith("汇总", StringComparison.Ordinal))
            .Where(l => !l.StartsWith("统计", StringComparison.Ordinal))
            .Count(l => l.Contains("D:\\TestData"));

        Assert.Equal(summary.Items.Count, jsonCount);
        Assert.Equal(summary.Items.Count, csvDataRows);
    }
}
