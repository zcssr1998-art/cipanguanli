using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;

namespace MiniDiskLab.App;

/// <summary>
/// 界面层自检：真实构建 <see cref="MainWindow"/>（解析 XAML、应用全部
/// DynamicResource 画刷与样式、建立 DataGrid 列与筛选下拉框），
/// 然后逐个核对任务文档要求的界面元素是否存在。
///
/// 窗口不会显示出来，因此可以在无桌面 / 无法做 GUI 自动化的环境中运行，
/// 同时仍然验证了“界面能否正常构造”这一关键点。
/// </summary>
internal static class UiSmokeCheck
{
    public static int Run(TextWriter log)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // 无控制台时忽略。
        }

        var failures = 0;

        void Check(string name, bool ok, string detail = "")
        {
            if (!ok)
            {
                failures++;
            }

            log.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " :: " + detail : "")}");
        }

        log.WriteLine("MiniDiskLab UI smoke check");
        log.WriteLine($"  运行时 : {Environment.Version} / {CultureInfo.CurrentUICulture.Name}");
        log.WriteLine();

        MainWindow? window = null;

        try
        {
            // 这一步会真正加载 MainWindow.xaml —— 任何 XAML 解析错误、
            // 缺失的 StaticResource、错误的控件类型都会在此抛出。
            window = new MainWindow();

            log.WriteLine($"[1/4] MainWindow 构造成功，标题=\"{window.Title}\"");
            Check("MainWindow XAML 可成功加载", true);
            Check("窗口标题非空", !string.IsNullOrWhiteSpace(window.Title), window.Title);
            Check("窗口允许调整大小", window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip,
                window.ResizeMode.ToString());

            log.WriteLine();
            log.WriteLine("[2/4] 核对任务文档要求的界面元素");

            // 任务文档第四部分要求的控件。
            Check("目录选择框 (PathTextBox)", Find<TextBox>(window, "PathTextBox") is not null);
            Check("“选择…”按钮 (BrowseButton)", Find<Button>(window, "BrowseButton") is not null);
            Check("阈值输入框 (ThresholdTextBox)", Find<TextBox>(window, "ThresholdTextBox") is not null);
            Check("阈值单位下拉 (UnitComboBox)", Find<ComboBox>(window, "UnitComboBox") is not null);
            Check("开始扫描按钮 (StartButton)", Find<Button>(window, "StartButton") is not null);
            Check("取消按钮 (CancelButton)", Find<Button>(window, "CancelButton") is not null);
            Check("状态显示 (StatusText)", Find<TextBlock>(window, "StatusText") is not null);
            Check("进度条 (ScanProgressBar)", Find<ProgressBar>(window, "ScanProgressBar") is not null);
            Check("已扫描计数 (ScannedCountText)", Find<TextBlock>(window, "ScannedCountText") is not null);
            Check("大文件计数 (LargeFileCountText)", Find<TextBlock>(window, "LargeFileCountText") is not null);
            Check("跳过目录计数 (SkippedCountText)", Find<TextBlock>(window, "SkippedCountText") is not null);
            Check("结果表格 (ResultsGrid)", Find<DataGrid>(window, "ResultsGrid") is not null);
            Check("导出 CSV 按钮 (ExportCsvButton)", Find<Button>(window, "ExportCsvButton") is not null);

            // 单位下拉必须有 KB / MB / GB 三项。
            if (Find<ComboBox>(window, "UnitComboBox") is ComboBox unitBox)
            {
                var items = unitBox.Items.OfType<ComboBoxItem>().Select(i => i.Content?.ToString()).ToList();
                Check("单位下拉含 KB/MB/GB",
                    items.Contains("KB") && items.Contains("MB") && items.Contains("GB"),
                    string.Join("/", items));
            }

            // 表格列必须包含：大小 / 类型 / 说明 / 文件名 / 完整路径。
            if (Find<DataGrid>(window, "ResultsGrid") is DataGrid grid)
            {
                var headers = grid.Columns.Select(c => c.Header?.ToString()).ToList();
                var required = new[] { "大小", "类型", "说明", "文件名", "完整路径" };
                var missing = required.Where(r => !headers.Contains(r)).ToList();
                Check("表格列包含 大小/类型/说明/文件名/完整路径", missing.Count == 0,
                    missing.Count == 0 ? string.Join("|", headers) : "缺少: " + string.Join(",", missing));
            }

            log.WriteLine();
            log.WriteLine("[3/4] 核对主题资源可解析（浅色 / 深色）");

            var brushKeys = new[]
            {
                "WindowBackgroundBrush", "PanelBackgroundBrush", "BorderBrush",
                "PrimaryTextBrush", "SecondaryTextBrush", "AccentBrush", "GridHeaderBrush",
            };

            foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
            {
                ThemeManager.Apply(theme);
                var unresolved = brushKeys
                    .Where(k => System.Windows.Application.Current.TryFindResource(k) is null)
                    .ToList();
                Check($"{theme} 主题画刷全部可解析", unresolved.Count == 0,
                    unresolved.Count == 0 ? $"{brushKeys.Length} brushes" : "缺失: " + string.Join(",", unresolved));
            }

            // 恢复浅色，避免影响设置文件。
            ThemeManager.Apply(AppTheme.Light);

            log.WriteLine();
            log.WriteLine("[4/4] 核对数值与文本初始化");
            if (Find<TextBox>(window, "ThresholdTextBox") is TextBox thresholdBox)
            {
                Check("阈值默认值可解析为数字",
                    double.TryParse(thresholdBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out _),
                    thresholdBox.Text);
            }

            if (Find<ComboBox>(window, "CategoryFilterComboBox") is ComboBox filterBox)
            {
                Check("类型筛选下拉已填充", filterBox.Items.Count > 0, $"{filterBox.Items.Count} 项");
            }
        }
        catch (Exception ex)
        {
            failures++;
            log.WriteLine();
            log.WriteLine("界面自检发生异常：");
            log.WriteLine(ex.ToString());
        }
        finally
        {
            // 窗口从未显示；显式关闭以免留下 DispatcherTimer。
            try
            {
                window?.Close();
            }
            catch
            {
                // 忽略。
            }
        }

        log.WriteLine();
        log.WriteLine(failures == 0
            ? "===== 界面自检结果: 全部 PASS ====="
            : $"===== 界面自检结果: {failures} 项 FAIL =====");

        return failures == 0 ? App.ExitSuccess : App.ExitFailure;
    }

    /// <summary>按名称查找已命名控件（在逻辑树中递归）。</summary>
    private static T? Find<T>(FrameworkElement root, string name) where T : FrameworkElement
    {
        if (root.FindName(name) is T direct)
        {
            return direct;
        }

        return FindInTree<T>(root, name);
    }

    private static T? FindInTree<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typed && typed.Name == name)
            {
                return typed;
            }

            var found = FindInTree<T>(child, name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
