using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using MiniDiskLab.Core;
using MiniDiskLab.Core.Models;
using MiniDiskLab.Core.Services;
using MiniDiskLab.Core.Utilities;

namespace MiniDiskLab.App;

/// <summary>
/// 无界面（headless）自检：
///
/// 1. 在系统临时目录下物化测试数据；
/// 2. 运行一次真实扫描；
/// 3. 校验阈值边界、排序、分类说明；
/// 4. 导出 CSV + JSON 并验证文件内容；
/// 5. 写出一份 JSON 报告。
///
/// 该路径完全不依赖 GUI，因此可以在 CI / 远程会话中验证真实行为。
/// </summary>
internal static class HeadlessSelfTest
{
    public sealed class CheckResult
    {
        public string Name { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public string Detail { get; set; } = string.Empty;
    }

    public static int Run(string[] args, TextWriter log)
    {
        var dataDir = GetArg(args, "--data")
            ?? Path.Combine(Path.GetTempPath(), "MiniDiskLabSelfTest", "test_data");
        var reportPath = GetArg(args, "--report")
            ?? Path.Combine(Path.GetTempPath(), "MiniDiskLabSelfTest", "selftest-report.json");

        var checks = new List<CheckResult>();

        void Check(string name, bool passed, string detail = "")
        {
            checks.Add(new CheckResult { Name = name, Passed = passed, Detail = detail });
            log.WriteLine($"  [{(passed ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " :: " + detail : "")}");
        }

        try
        {
            log.WriteLine("MiniDiskLab headless self-test");
            log.WriteLine($"  数据目录 : {dataDir}");
            log.WriteLine($"  报告路径 : {reportPath}");
            log.WriteLine();

            // --- 1. 准备测试数据 ---
            log.WriteLine("[1/6] 生成测试数据 (稀疏文件)");
            var report = TestDataFactory.Create(dataDir);
            log.WriteLine($"      创建 {report.CreatedPaths.Count} 个文件，稀疏文件 {report.SparseFileCount} 个");
            Check("测试数据已创建", report.CreatedPaths.Count > 0,
                $"{report.CreatedPaths.Count} files, {report.SparseFileCount} sparse");

            // 验证稀疏文件确实没有占满磁盘：逻辑总量 vs 实际占用
            var logicalMb = report.LogicalTotalBytes / (1024.0 * 1024.0);
            var actualBytes = MeasureDirectoryBytes(dataDir);
            var actualMb = actualBytes / (1024.0 * 1024.0);
            log.WriteLine($"      逻辑大小 {logicalMb:N0} MB / 实际占用 {actualMb:N0} MB");
            Check("稀疏文件未占用等量真实磁盘", actualBytes < report.LogicalTotalBytes / 2,
                $"logical={logicalMb:N0}MB actual={actualMb:N0}MB");

            // --- 2. 扫描 ---
            log.WriteLine();
            log.WriteLine("[2/6] 执行扫描 (阈值 500 MB)");
            var scanner = new DiskScanner();
            var threshold = new SizeThreshold(500, SizeUnit.MB);

            var progressReports = new List<ScanProgress>();
            var streamedItems = new List<ScanResultItem>();
            // 注意：不要用 Progress<T> —— 它会把回调 post 到捕获的
            // SynchronizationContext（无界面场景下为线程池），
            // 回调可能在检查时还没有执行完。这里用同步实现。
            var progress = new SynchronousProgress<ScanProgress>(progressReports.Add);

            var summary = scanner.Scan(
                dataDir,
                threshold,
                CancellationToken.None,
                progress,
                item => streamedItems.Add(item));

            log.WriteLine($"      已扫描文件 {summary.ScannedFiles}，发现大文件 {summary.Items.Count}，"
                        + $"跳过目录 {summary.SkippedDirectories}，耗时 {summary.Elapsed.TotalSeconds:0.00}s");
            Check("扫描完成且找到大文件", summary.Items.Count > 0, $"{summary.Items.Count} items");
            Check("扫描未抛出异常且未取消", !summary.WasCancelled);
            Check("进度回调有输出", progressReports.Count > 0, $"{progressReports.Count} reports");
            Check("大文件增量回调与结果数一致", streamedItems.Count == summary.Items.Count,
                $"{streamedItems.Count} vs {summary.Items.Count}");

            // --- 3. 阈值边界 ---
            log.WriteLine();
            log.WriteLine("[3/6] 校验阈值边界 (499/500/501 MB)");
            var sizes = summary.Items.ToDictionary(i => i.FileName, i => i.SizeBytes, StringComparer.OrdinalIgnoreCase);
            var has499 = sizes.ContainsKey("boundary_499MB.bin");
            var has500 = sizes.ContainsKey("boundary_500MB.bin");
            var has501 = sizes.ContainsKey("boundary_501MB.bin");
            Check("499MB 低于阈值被排除", !has499);
            Check("500MB 等于阈值被包含", has500);
            Check("501MB 高于阈值被包含", has501);

            // --- 4. 排序 ---
            log.WriteLine();
            log.WriteLine("[4/6] 校验按大小降序排序");
            var sorted = true;
            for (var i = 1; i < summary.Items.Count; i++)
            {
                if (summary.Items[i - 1].SizeBytes < summary.Items[i].SizeBytes)
                {
                    sorted = false;
                    break;
                }
            }
            Check("结果按文件大小降序", sorted);
            if (summary.Items.Count > 0)
            {
                var biggest = summary.Items[0];
                log.WriteLine($"      最大文件: {biggest.FileName} ({biggest.SizeText}) - {biggest.Description}");
            }

            // --- 5. 分类器 ---
            log.WriteLine();
            log.WriteLine("[5/6] 校验分类与自动说明");
            ExpectCategory(summary, "movie.mp4", FileCategory.TencentVideo, "腾讯视频", Check);
            ExpectCategory(summary, "data.pak", FileCategory.Steam, "Steam", Check);
            ExpectCategory(summary, "body.ma", FileCategory.Maya, "Maya", Check);
            ExpectCategory(summary, "windows.iso", FileCategory.IsoImage, "ISO", Check);
            ExpectCategory(summary, "cache.tmp", FileCategory.AdobeCache, "Adobe", Check);
            ExpectCategory(summary, "pakchunk0.pak", FileCategory.UnrealEngine, "Unreal", Check);
            ExpectCategory(summary, "unknown.xyz", FileCategory.Unknown, "未知", Check);

            var allHaveDescription = summary.Items.All(i => !string.IsNullOrWhiteSpace(i.Description));
            Check("每个结果都有自动说明", allHaveDescription);

            // --- 6. 导出 ---
            log.WriteLine();
            log.WriteLine("[6/6] 导出 CSV 与 JSON");
            var csvPath = Path.Combine(Path.GetDirectoryName(reportPath)!, "selftest-export.csv");
            var jsonPath = Path.Combine(Path.GetDirectoryName(reportPath)!, "selftest-export.json");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);

            ResultExporter.ExportCsv(summary, csvPath);
            ResultExporter.ExportJson(summary, jsonPath);

            var csvExists = File.Exists(csvPath) && new FileInfo(csvPath).Length > 0;
            var jsonExists = File.Exists(jsonPath) && new FileInfo(jsonPath).Length > 0;
            Check("CSV 已生成且非空", csvExists, csvPath);
            Check("JSON 已生成且非空", jsonExists, jsonPath);

            if (csvExists)
            {
                var csvText = File.ReadAllText(csvPath, Encoding.UTF8);
                var dataLines = csvText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
                Check("CSV 行数包含全部结果", dataLines >= summary.Items.Count + 1,
                    $"{dataLines} lines for {summary.Items.Count} items");

                // 必须读原始字节：File.ReadAllText 会吞掉 BOM。
                var head = new byte[3];
                using (var fs = File.OpenRead(csvPath))
                {
                    _ = fs.Read(head, 0, 3);
                }

                var hasBom = head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
                Check("CSV 含 UTF-8 BOM (Excel 兼容)", hasBom,
                    hasBom ? "EF BB BF" : $"first bytes={head[0]:X2} {head[1]:X2} {head[2]:X2}");
            }

            if (jsonExists)
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath, Encoding.UTF8));
                var itemCount = doc.RootElement.GetProperty("items").GetArrayLength();
                Check("JSON 项数与扫描结果一致", itemCount == summary.Items.Count,
                    $"{itemCount} vs {summary.Items.Count}");
            }

            // --- 取消功能 ---
            log.WriteLine();
            log.WriteLine("[附加] 校验取消功能");
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var cancelled = scanner.Scan(dataDir, threshold, cts.Token);
                Check("预先取消时能立即停止", cancelled.WasCancelled);
            }

            // --- 异常文件 ---
            log.WriteLine();
            log.WriteLine("[附加] 校验异常文件不导致崩溃");
            var missing = Path.Combine(dataDir, "Random", "does_not_exist.bin");
            var missingClassifier = new Core.Classifiers.FileClassifier();
            var cls = missingClassifier.Classify(missing);
            Check("不存在的文件仍能分类而不抛异常", !string.IsNullOrEmpty(cls.Description));

            // 扫描中文件消失：先扫描一个临时目录，扫描过程中删除文件。
            var churnDir = Path.Combine(dataDir, "__churn__");
            Directory.CreateDirectory(churnDir);
            for (var i = 0; i < 40; i++)
            {
                File.WriteAllText(Path.Combine(churnDir, $"f{i}.bin"), new string('x', 1024));
            }
            var churnTask = Task.Run(() =>
            {
                try
                {
                    return scanner.Scan(dataDir, threshold, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    log.WriteLine("      churn scan threw: " + ex.Message);
                    return null;
                }
            });
            for (var i = 0; i < 40; i += 2)
            {
                try { File.Delete(Path.Combine(churnDir, $"f{i}.bin")); } catch { /* 允许失败 */ }
            }
            churnTask.Wait();
            Check("扫描过程中删除文件不导致崩溃", churnTask.Result is not null);
            try { Directory.Delete(churnDir, recursive: true); } catch { /* 允许失败 */ }

            // --- 空目录 ---
            var emptyDir = Path.Combine(dataDir, "__empty__");
            Directory.CreateDirectory(emptyDir);
            var emptySummary = scanner.Scan(emptyDir, threshold);
            Check("空目录扫描返回 0 结果", emptySummary.Items.Count == 0 && emptySummary.ScannedFiles == 0);

            // --- 汇总 ---
            var failed = checks.Count(c => !c.Passed);
            var passed = checks.Count - failed;

            log.WriteLine();
            log.WriteLine($"===== 自检结果: {passed}/{checks.Count} PASS, {failed} FAIL =====");

            WriteReport(reportPath, dataDir, checks, summary, csvPath, jsonPath, log);

            return failed == 0 ? App.ExitSuccess : App.ExitFailure;
        }
        catch (Exception ex)
        {
            log.WriteLine();
            log.WriteLine("自检过程中发生未处理异常:");
            log.WriteLine(ex.ToString());
            checks.Add(new CheckResult
            {
                Name = "未处理异常",
                Passed = false,
                Detail = ex.ToString(),
            });
            TryWriteReport(reportPath, dataDir, checks, log);
            return App.ExitFailure;
        }
    }

    private static void ExpectCategory(
        ScanSummary summary,
        string fileName,
        FileCategory expected,
        string descriptionKeyword,
        Action<string, bool, string> check)
    {
        var item = summary.Items.FirstOrDefault(i =>
            string.Equals(i.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        if (item is null)
        {
            check($"分类: {fileName}", false, "结果中未找到该文件");
            return;
        }

        var categoryOk = item.Category == expected;
        var descOk = item.Description.Contains(descriptionKeyword, StringComparison.OrdinalIgnoreCase);
        check(
            $"分类: {fileName} -> {expected}",
            categoryOk && descOk,
            $"实际 category={item.Category}, description=\"{item.Description}\"");
    }

    private static long MeasureDirectoryBytes(string dir)
    {
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    // GetAllocatedSize 反映磁盘实际占用（稀疏文件会小很多）。
                    var info = new FileInfo(f);
                    total += GetAllocatedSize(f, info.Length);
                }
                catch
                {
                    // 忽略单个文件的错误。
                }
            }
        }
        catch
        {
            // 忽略。
        }

        return total;
    }

    /// <summary>
    /// 通过 (已分配簇数 × 簇大小) 近似磁盘实际占用。
    /// 使用 GetCompressedFileSize P/Invoke 的成本较高，这里用
    /// FileInfo.Length 减去稀疏空洞的方式不可行，
    /// 因此改用 Win32 GetCompressedFileSize。
    /// </summary>
    private static long GetAllocatedSize(string path, long logicalLength)
    {
        const uint INVALID_FILE_SIZE = 0xFFFFFFFF;

        try
        {
            var low = GetCompressedFileSizeW(path, out var high);
            if (low == INVALID_FILE_SIZE && Marshal.GetLastWin32Error() != 0)
            {
                return logicalLength;
            }

            return ((long)high << 32) | low;
        }
        catch
        {
            return logicalLength;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

    private static void WriteReport(
        string reportPath,
        string dataDir,
        List<CheckResult> checks,
        ScanSummary summary,
        string csvPath,
        string jsonPath,
        TextWriter log)
    {
        try
        {
            var payload = new
            {
                tool = "MiniDiskLab.HeadlessSelfTest",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                machine = Environment.MachineName,
                runtime = Environment.Version.ToString(),
                dataDirectory = dataDir,
                passed = checks.All(c => c.Passed),
                passedCount = checks.Count(c => c.Passed),
                failedCount = checks.Count(c => !c.Passed),
                totalChecks = checks.Count,
                checks = checks.Select(c => new { c.Name, c.Passed, c.Detail }),
                scan = new
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
                artifacts = new { csv = csvPath, json = jsonPath },
            };

            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(
                reportPath,
                JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            log.WriteLine($"报告已写出: {reportPath}");
        }
        catch (Exception ex)
        {
            log.WriteLine("写出报告失败: " + ex.Message);
        }
    }

    private static void TryWriteReport(string reportPath, string dataDir, List<CheckResult> checks, TextWriter log)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(
                reportPath,
                JsonSerializer.Serialize(new
                {
                    tool = "MiniDiskLab.HeadlessSelfTest",
                    passed = false,
                    dataDirectory = dataDir,
                    checks = checks.Select(c => new { c.Name, c.Passed, c.Detail }),
                }, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch
        {
            log.WriteLine("无法写出报告文件。");
        }
    }

    private static string? GetArg(string[] args, string name)
    {
        foreach (var a in args)
        {
            if (a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return a[(name.Length + 1)..];
            }
        }

        return null;
    }
}

/// <summary>
/// 直接在调用线程上执行的 <see cref="IProgress{T}"/> 实现 ——
/// 无界面场景下需要确定性行为时使用（<see cref="Progress{T}"/> 会异步 post）。
/// </summary>
internal sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SynchronousProgress(Action<T> handler) => _handler = handler;

    public void Report(T value) => _handler(value);
}
