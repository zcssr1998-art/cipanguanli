using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using MiniDiskLab.Core;
using MiniDiskLab.Core.Models;
using MiniDiskLab.Core.Services;
using MiniDiskLab.Core.Utilities;

// MessageBox / SaveFileDialog / SizeConverter 等别名在 GlobalUsings.cs 中统一声明，
// 因为项目同时启用了 WPF 与 WinForms。

namespace MiniDiskLab.App;

/// <summary>
/// 主窗口。界面层只负责展示与转发：扫描、分类、导出逻辑都在 Core 中。
/// </summary>
public partial class MainWindow : Window
{
    private readonly DiskScanner _scanner = new();

    /// <summary>当前扫描全部结果（未筛选）。</summary>
    private readonly List<ResultRow> _allRows = new();

    /// <summary>绑定到表格的筛选后视图。</summary>
    private readonly ObservableCollection<ResultRow> _visibleRows = new();

    /// <summary>扫描任务的取消源。真正用于终止后台线程。</summary>
    private CancellationTokenSource? _cts;

    /// <summary>后台扫描任务。</summary>
    private Task<ScanSummary>? _scanTask;

    /// <summary>上一次完成的扫描摘要（用于导出）。</summary>
    private ScanSummary? _lastSummary;

    /// <summary>进度回调的节流计时器。</summary>
    private readonly Stopwatch _scanStopwatch = new();

    /// <summary>延迟刷新 UI 的计时器（避免高频进度回调拖慢界面）。</summary>
    private readonly DispatcherTimer _uiFlushTimer;

    private ScanProgress? _pendingProgress;

    /// <summary>增量显示时缓存的待加入行。</summary>
    private readonly List<ResultRow> _pendingRows = new();

    private readonly object _pendingLock = new();

    public MainWindow()
    {
        InitializeComponent();

        // 记住上次使用的目录与阈值。
        var settings = AppSettingsStore.Load();
        PathTextBox.Text = settings.LastDirectory ?? string.Empty;
        ThresholdTextBox.Text = settings.LastThresholdValue.ToString("0.##");
        UnitComboBox.SelectedIndex = (int)settings.LastThresholdUnit;

        ResultsGrid.ItemsSource = _visibleRows;
        CategoryFilterComboBox.ItemsSource = BuildCategoryFilterItems();
        CategoryFilterComboBox.SelectedIndex = 0;

        // 用计时器合并进度更新：扫描线程可以每秒产生上千次回调，
        // 直接跨线程刷新控件会拖慢扫描并让界面卡顿。
        _uiFlushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _uiFlushTimer.Tick += (_, _) => FlushPendingUpdates();
        _uiFlushTimer.Start();

        ThemeManager.Apply(AppTheme.Light);
        UpdateThresholdHint();

        Loaded += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(PathTextBox.Text))
            {
                FooterText.Text = $"上次扫描目录：{PathTextBox.Text}";
            }
        };

        Closed += MainWindow_Closed;
    }

    // ==========================================================
    //  配置区交互
    // ==========================================================

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        // WPF 没有内置文件夹选择对话框，使用 WinForms 的实现。
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择要扫描的文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(PathTextBox.Text) ? PathTextBox.Text : string.Empty,
        };

        if (dialog.ShowDialog() == WinFormsDialogResult.OK)
        {
            PathTextBox.Text = dialog.SelectedPath;
            UpdateThresholdHint();
        }
    }

    private void DriveScanButton_Click(object sender, RoutedEventArgs e)
    {
        var path = PathTextBox.Text.Trim();
        if (path.Length >= 2 && path[1] == ':')
        {
            PathTextBox.Text = path[..2] + "\\";
            return;
        }

        var root = Path.GetPathRoot(Directory.GetCurrentDirectory());
        if (!string.IsNullOrEmpty(root))
        {
            PathTextBox.Text = root;
        }
    }

    private void ThresholdTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateThresholdHint();

    private void UnitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateThresholdHint();

    private void UpdateThresholdHint()
    {
        if (ThresholdHintText is null || UnitComboBox is null || ThresholdTextBox is null)
        {
            return;
        }

        var unit = (SizeUnit)Math.Max(0, UnitComboBox.SelectedIndex);
        if (double.TryParse(ThresholdTextBox.Text, out var value) && value >= 0)
        {
            try
            {
                var bytes = SizeConverter.UnitToBytes(value, unit);
                ThresholdHintText.Text = $"= {new SizeThreshold(value, unit)}  ({SizeConverter.Format(bytes)})";
                return;
            }
            catch (ArgumentOutOfRangeException)
            {
                // 落到下面的提示。
            }
        }

        ThresholdHintText.Text = "请输入有效的数值";
    }

    // ==========================================================
    //  扫描
    // ==========================================================

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scanTask is { IsCompleted: false })
        {
            return;
        }

        var rootPath = PathTextBox.Text.Trim();
        if (rootPath.Length == 0)
        {
            MessageBox.Show(this, "请先选择要扫描的目录。", "MiniDiskLab",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Directory.Exists(rootPath))
        {
            MessageBox.Show(this, $"目录不存在：\n{rootPath}", "MiniDiskLab",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var unit = (SizeUnit)Math.Max(0, UnitComboBox.SelectedIndex);
        if (!double.TryParse(ThresholdTextBox.Text, out var thresholdValue) || thresholdValue < 0)
        {
            MessageBox.Show(this, "阈值必须是大于等于 0 的数值。", "MiniDiskLab",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SizeThreshold threshold;
        try
        {
            threshold = new SizeThreshold(thresholdValue, unit);
            _ = threshold.ToBytes();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            MessageBox.Show(this, ex.Message, "MiniDiskLab",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AppSettingsStore.Save(new AppSettings
        {
            LastDirectory = rootPath,
            LastThresholdValue = thresholdValue,
            LastThresholdUnit = unit,
        });

        ResetForNewScan();
        SetScanningState(isScanning: true);

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var progress = new Progress<ScanProgress>(OnProgress);
        var itemCallback = new Action<ScanResultItem>(OnItemFound);

        _scanStopwatch.Restart();

        try
        {
            _scanTask = _scanner.ScanAsync(rootPath, threshold, token, progress, itemCallback);
            var summary = await _scanTask;

            _lastSummary = summary;
            ApplyFinalState(summary);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "扫描已取消。";
            FooterText.Text = "扫描被用户取消。";
        }
        catch (Exception ex)
        {
            // 不隐藏错误，明确告知用户。
            StatusText.Text = "扫描失败。";
            FooterText.Text = "扫描失败：" + ex.Message;
            MessageBox.Show(this,
                $"扫描过程中发生错误：\n\n{ex.Message}",
                "MiniDiskLab", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _scanStopwatch.Stop();
            SetScanningState(isScanning: false);
            FlushPendingUpdates(force: true);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is null || _cts.IsCancellationRequested)
        {
            return;
        }

        // 真正取消后台扫描，而不只是把按钮变灰。
        _cts.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = "正在取消扫描…";
        FooterText.Text = "已请求取消，正在等待后台扫描线程安全退出…";
    }

    private void ResetForNewScan()
    {
        _allRows.Clear();
        _visibleRows.Clear();
        _lastSummary = null;
        lock (_pendingLock)
        {
            _pendingRows.Clear();
        }

        _pendingProgress = null;
        ScannedCountText.Text = "已扫描 0 个文件";
        LargeFileCountText.Text = "发现 0 个大文件";
        SkippedCountText.Text = "跳过 0 个无权限目录";
        SpeedText.Text = "0.0 文件/秒";
        ElapsedText.Text = "耗时 0.0s";
        ScanProgressBar.Value = 0;
        ScanProgressBar.IsIndeterminate = true;
        StatusText.Text = "扫描状态：正在准备…";
        SetExportButtonsEnabled(false);
    }

    private void SetScanningState(bool isScanning)
    {
        StartButton.IsEnabled = !isScanning;
        CancelButton.IsEnabled = isScanning;
        BrowseButton.IsEnabled = !isScanning;
        DriveScanButton.IsEnabled = !isScanning;
        ThresholdTextBox.IsEnabled = !isScanning;
        UnitComboBox.IsEnabled = !isScanning;
        PathTextBox.IsEnabled = !isScanning;
        CreateTestDataButton.IsEnabled = !isScanning;

        if (!isScanning)
        {
            ScanProgressBar.IsIndeterminate = false;
            ScanProgressBar.Value = 100;
        }
    }

    private void SetExportButtonsEnabled(bool enabled)
    {
        ExportCsvButton.IsEnabled = enabled;
        ExportJsonButton.IsEnabled = enabled;
        ExportExtensionStatsButton.IsEnabled = enabled;
        TopDirectoriesButton.IsEnabled = enabled;
    }

    // ==========================================================
    //  进度处理（来自后台线程）
    // ==========================================================

    private void OnProgress(ScanProgress progress)
    {
        // 只保存最新快照，由 UI 计时器统一刷新，避免 UI 线程被淹没。
        _pendingProgress = progress;
    }

    private void OnItemFound(ScanResultItem item)
    {
        // 后台线程回调：先入队，稍后在 UI 线程加入表格。
        lock (_pendingLock)
        {
            _pendingRows.Add(new ResultRow(item));
        }
    }

    private void FlushPendingUpdates(bool force = false)
    {
        var hadWork = false;

        // 1. 增量加入新发现的大文件。
        List<ResultRow>? rowsToAdd = null;
        lock (_pendingLock)
        {
            if (_pendingRows.Count > 0)
            {
                rowsToAdd = new List<ResultRow>(_pendingRows);
                _pendingRows.Clear();
            }
        }

        if (rowsToAdd is not null)
        {
            hadWork = true;
            foreach (var row in rowsToAdd)
            {
                _allRows.Add(row);
            }

            // 每次新增后重新排序，保持“从大到小”始终可见。
            ResortRows();
            ApplyFilter();
        }

        // 2. 刷新计数与进度文本。
        var p = _pendingProgress;
        if (p is not null)
        {
            hadWork = true;
            _pendingProgress = null;

            ScannedCountText.Text = $"已扫描 {p.ScannedFiles:N0} 个文件";
            LargeFileCountText.Text = $"发现 {p.LargeFileCount:N0} 个大文件";
            SkippedCountText.Text = $"跳过 {p.SkippedDirectories:N0} 个无权限目录"
                                  + (p.SkippedLinks > 0 ? $"（链接 {p.SkippedLinks:N0}）" : string.Empty);
            SpeedText.Text = p.FilesPerSecondText;
            ElapsedText.Text = $"耗时 {p.Elapsed.TotalSeconds:0.0}s";
            StatusText.Text = p.CurrentPath is null
                ? "扫描状态：正在收尾…"
                : $"正在扫描 {CompressPath(p.CurrentPath)}";
        }

        if (force)
        {
            _uiFlushTimer.Stop();
        }

        _ = hadWork;
    }

    private static string CompressPath(string path)
    {
        // 过长的路径会撑爆状态栏，从中段省略。
        const int max = 110;
        if (path.Length <= max)
        {
            return path;
        }

        var head = path[..(max / 2 - 2)];
        var tail = path[^(max / 2 - 1)..];
        return $"{head}…{tail}";
    }

    private void ApplyFinalState(ScanSummary summary)
    {
        // 兜底：把仍在队列中的增量结果补齐。
        FlushPendingUpdates(force: true);
        _allRows.Clear();
        foreach (var item in summary.Items)
        {
            _allRows.Add(new ResultRow(item));
        }

        ResortRows();
        ApplyFilter();

        SetExportButtonsEnabled(summary.Items.Count > 0);

        StatusText.Text = summary.WasCancelled
            ? $"扫描已取消（已处理 {summary.ScannedFiles:N0} 个文件）。"
            : $"扫描完成，用时 {summary.Elapsed.TotalSeconds:0.00} 秒。";

        ScannedCountText.Text = $"已扫描 {summary.ScannedFiles:N0} 个文件";
        LargeFileCountText.Text = $"发现 {summary.Items.Count:N0} 个大文件";
        SkippedCountText.Text = $"跳过 {summary.SkippedDirectories:N0} 个无权限目录"
                              + (summary.SkippedLinks > 0 ? $"（链接 {summary.SkippedLinks:N0}）" : string.Empty);
        SpeedText.Text = summary.Elapsed.TotalSeconds > 0.001
            ? $"{summary.ScannedFiles / summary.Elapsed.TotalSeconds:N1} 文件/秒"
            : "-";
        ElapsedText.Text = $"耗时 {summary.Elapsed.TotalSeconds:0.00}s";

        var errorNote = summary.ErrorCount > 0 ? $"，{summary.ErrorCount:N0} 个文件读取失败" : string.Empty;
        FooterText.Text =
            $"扫描目录 {summary.RootPath} ｜ 阈值 {summary.Threshold} ｜ "
            + $"发现 {summary.Items.Count:N0} 个大文件{errorNote}"
            + (summary.WasCancelled ? " ｜ 已取消" : string.Empty);
    }

    private void ResortRows()
    {
        _allRows.Sort(static (a, b) =>
        {
            var cmp = b.SizeBytes.CompareTo(a.SizeBytes);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.FullPath, b.FullPath);
        });
    }

    // ==========================================================
    //  搜索与筛选（额外挑战功能）
    // ==========================================================

    private List<string> BuildCategoryFilterItems()
    {
        var items = new List<string> { "全部类型" };
        items.AddRange(Enum.GetValues<FileCategory>()
            .Select(ResultExporter.GetCategoryLabel)
            .Distinct()
            .OrderBy(x => x, StringComparer.CurrentCulture));
        return items;
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void CategoryFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void ClearFilterButton_Click(object sender, RoutedEventArgs e)
    {
        SearchTextBox.Text = string.Empty;
        CategoryFilterComboBox.SelectedIndex = 0;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var keyword = SearchTextBox?.Text?.Trim() ?? string.Empty;
        var categorySelection = CategoryFilterComboBox?.SelectedItem as string ?? "全部类型";

        _visibleRows.Clear();
        foreach (var row in _allRows)
        {
            if (keyword.Length > 0
                && row.SearchText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (categorySelection != "全部类型"
                && !string.Equals(row.CategoryLabel, categorySelection, StringComparison.CurrentCulture))
            {
                continue;
            }

            _visibleRows.Add(row);
        }

        if (_allRows.Count > 0)
        {
            var filtered = _visibleRows.Count != _allRows.Count
                ? $"（筛选后 {_visibleRows.Count:N0} / {_allRows.Count:N0}）"
                : string.Empty;
            ResultsGrid.Columns[0].Header = $"大小{filtered}";
        }
    }

    // ==========================================================
    //  导出
    // ==========================================================

    private void ExportCsvButton_Click(object sender, RoutedEventArgs e) => ExportCurrent(
        "CSV 文件 (*.csv)|*.csv",
        ".csv",
        (summary, path) => ResultExporter.ExportCsv(summary, path));

    private void ExportJsonButton_Click(object sender, RoutedEventArgs e) => ExportCurrent(
        "JSON 文件 (*.json)|*.json",
        ".json",
        (summary, path) => ResultExporter.ExportJson(summary, path));

    private void ExportCurrent(string filter, string extension, Action<ScanSummary, string> write)
    {
        if (_lastSummary is null)
        {
            MessageBox.Show(this, "还没有可导出的扫描结果。", "MiniDiskLab",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = filter,
            DefaultExt = extension,
            FileName = $"MiniDiskLab_{DateTime.Now:yyyyMMdd_HHmmss}{extension}",
            AddExtension = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            // 导出的是筛选后的结果，符合用户在界面上的所见。
            var summary = BuildExportSummary();
            write(summary, dialog.FileName);
            FooterText.Text = $"已导出 {summary.Items.Count:N0} 条结果到 {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导出失败：\n\n{ex.Message}", "MiniDiskLab",
                MessageBoxButton.OK, MessageBoxImage.Error);
            FooterText.Text = "导出失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 用当前可见（筛选后）的行构造导出用的摘要，
    /// 统计信息仍来自原始扫描。
    /// </summary>
    private ScanSummary BuildExportSummary()
    {
        var source = _lastSummary!;
        return new ScanSummary
        {
            Items = _visibleRows.Select(r => r.Item).ToList(),
            ScannedFiles = source.ScannedFiles,
            ScannedDirectories = source.ScannedDirectories,
            SkippedDirectories = source.SkippedDirectories,
            ErrorCount = source.ErrorCount,
            SkippedLinks = source.SkippedLinks,
            Elapsed = source.Elapsed,
            WasCancelled = source.WasCancelled,
            RootPath = source.RootPath,
            Threshold = source.Threshold,
            ExtensionStats = source.ExtensionStats,
            TopDirectories = source.TopDirectories,
        };
    }

    private void ExportExtensionStatsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastSummary is null || _lastSummary.ExtensionStats.Count == 0)
        {
            MessageBox.Show(this, "没有扩展名统计数据。", "MiniDiskLab",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV 文件 (*.csv)|*.csv",
            DefaultExt = ".csv",
            FileName = $"MiniDiskLab_扩展名统计_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var lines = new List<string> { "扩展名,文件数,总大小(字节),总大小" };
            lines.AddRange(_lastSummary.ExtensionStats.Select(s =>
                $"{Csv(s.Extension)},{s.FileCount},{s.TotalBytes},{Csv(s.TotalSizeText)}"));
            File.WriteAllLines(dialog.FileName, lines, new System.Text.UTF8Encoding(true));
            FooterText.Text = $"已导出 {_lastSummary.ExtensionStats.Count} 种扩展名统计到 {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导出失败：\n\n{ex.Message}", "MiniDiskLab",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TopDirectoriesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastSummary is null || _lastSummary.TopDirectories.Count == 0)
        {
            MessageBox.Show(this, "没有目录统计数据。", "MiniDiskLab",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var text = string.Join(Environment.NewLine,
            _lastSummary.TopDirectories.Select((d, i) =>
                $"{i + 1,2}. {d.TotalSizeText,-12} {d.FileCount,7:N0} 个文件  {d.Path}"));

        MessageBox.Show(this,
            $"Top {_lastSummary.TopDirectories.Count} 最大目录：{Environment.NewLine}{Environment.NewLine}{text}",
            "MiniDiskLab - Top 10 最大目录",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string Csv(string value) =>
        value.Contains(',') || value.Contains('"')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;

    // ==========================================================
    //  额外功能
    // ==========================================================

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var theme = ThemeManager.Toggle();
        ThemeButton.Content = theme == AppTheme.Light ? "深色模式" : "浅色模式";
    }

    private void CreateTestDataButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "将在目标目录下创建 test_data/ 测试数据（使用稀疏文件，不会大量占用磁盘）。\n\n"
            + "本操作只创建文件，绝不会删除任何现有文件。\n\n是否继续？",
            "MiniDiskLab - 生成测试数据",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            var baseDir = AppContext.BaseDirectory;
            var targetDir = Path.Combine(FindRepositoryRoot(baseDir) ?? baseDir, "test_data");
            var report = TestDataFactory.Create(targetDir);

            PathTextBox.Text = targetDir;
            FooterText.Text =
                $"已在 {targetDir} 创建 {report.CreatedPaths.Count} 个测试文件"
                + $"（稀疏文件 {report.SparseFileCount} 个，逻辑大小 {SizeConverter.Format(report.LogicalTotalBytes)}）。"
                + "现在可以点击“开始扫描”。";

            MessageBox.Show(this,
                $"测试数据已创建：\n{targetDir}\n\n"
                + $"文件数：{report.CreatedPaths.Count}\n"
                + $"稀疏文件：{report.SparseFileCount}\n"
                + $"逻辑总大小：{SizeConverter.Format(report.LogicalTotalBytes)}（实际占用远小于此）",
                "MiniDiskLab", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"创建测试数据失败：\n\n{ex.Message}", "MiniDiskLab",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>向上查找仓库中 agent-benchmark/MiniDiskLab 目录。</summary>
    private static string? FindRepositoryRoot(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src"))
                && Directory.Exists(Path.Combine(dir.FullName, "tests")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        // 关窗时必须终止后台扫描，避免进程残留。
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已释放，忽略。
        }

        _uiFlushTimer.Stop();

        AppSettingsStore.Save(new AppSettings
        {
            LastDirectory = PathTextBox.Text,
            LastThresholdValue = double.TryParse(ThresholdTextBox.Text, out var v) ? v : 500,
            LastThresholdUnit = (SizeUnit)Math.Max(0, UnitComboBox.SelectedIndex),
        });
    }
}
