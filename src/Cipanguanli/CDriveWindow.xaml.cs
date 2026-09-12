using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Windows;
using Cipanguanli.Core;

namespace Cipanguanli;

public partial class CDriveWindow : Window
{
    private readonly CDriveAnalyzer _analyzer = new();
    private readonly QuarantineService _quarantine = new();
    private readonly CDriveWriteMonitor _writeMonitor = new();
    private CancellationTokenSource? _analysisCts;
    private CancellationTokenSource? _monitorCts;
    private CDriveReport? _report;

    public CDriveWindow() => InitializeComponent();

    private void Window_Loaded(object sender, RoutedEventArgs e)
        => AdminStatusText.Text = IsAdministrator() ? "管理员：已开启" : "管理员：未开启";

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (_analysisCts is not null) return;
        _analysisCts = new CancellationTokenSource();
        AnalyzeButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        BusyProgress.Visibility = Visibility.Visible;
        ClearReport();
        try
        {
            var progress = new Progress<string>(text => StatusText.Text = text);
            _report = await _analyzer.AnalyzeAsync("C:\\", true, progress, _analysisCts.Token);
            BindReport(_report);
            StatusText.Text = $"C盘体检完成：专项 {_report.Findings.Count:N0} 项，AppData 大目录 {_report.AppDataEntries.Count:N0} 项，虚拟磁盘 {_report.VirtualDisks.Count:N0} 个。";
        }
        catch (OperationCanceledException) { StatusText.Text = "C盘体检已取消。"; }
        catch (Exception ex)
        {
            StatusText.Text = "C盘体检失败。";
            MessageBox.Show(this, ex.ToString(), "C盘体检失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _analysisCts?.Dispose();
            _analysisCts = null;
            AnalyzeButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            BusyProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _analysisCts?.Cancel();

    private void ClearReport()
    {
        FindingsGrid.ItemsSource = null;
        AppDataGrid.ItemsSource = null;
        VirtualDiskGrid.ItemsSource = null;
        GrowthGrid.ItemsSource = null;
        TargetPlanGrid.ItemsSource = null;
        TotalText.Text = UsedText.Text = FreeText.Text = ConservativeText.Text = RecommendedText.Text = "-";
    }

    private void BindReport(CDriveReport report)
    {
        FindingsGrid.ItemsSource = report.Findings;
        AppDataGrid.ItemsSource = report.AppDataEntries;
        VirtualDiskGrid.ItemsSource = report.VirtualDisks;
        GrowthGrid.ItemsSource = report.Growth;
        TotalText.Text = report.TotalText;
        UsedText.Text = report.UsedText;
        FreeText.Text = report.FreeText;
        ConservativeText.Text = report.ConservativeText;
        RecommendedText.Text = report.RecommendedText;
    }

    private void OpenFinding_Click(object sender, RoutedEventArgs e)
    {
        if (FindingsGrid.SelectedItem is CDriveFinding item) Reveal(item.Path);
    }

    private async void ExecuteFinding_Click(object sender, RoutedEventArgs e)
    {
        if (FindingsGrid.SelectedItem is CDriveFinding item) await ExecuteFindingAsync(item);
    }

    private async void ExecutePlanItem_Click(object sender, RoutedEventArgs e)
    {
        if (TargetPlanGrid.SelectedItem is CDriveFinding item) await ExecuteFindingAsync(item);
    }

    private async Task ExecuteFindingAsync(CDriveFinding item)
    {
        if (item.ActionKey == "hibernate-options")
        {
            var answer = MessageBox.Show(this,
                $"休眠文件当前约 {item.SizeText}。\n\n【是】改为 reduced：通常保留快速启动，但关闭完整休眠。\n【否】完全关闭休眠：释放更多空间，但休眠与快速启动可能受影响。\n【取消】不操作。",
                "休眠文件设置", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Cancel) return;
            try { StatusText.Text = await WindowsMaintenanceService.ConfigureHibernationAsync(answer == MessageBoxResult.Yes); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "休眠设置失败"); }
            return;
        }

        var warning = item.IsProtected
            ? $"【{item.Name}】属于保护/高风险区域。软件不会直接删除，只会打开官方管理入口或系统设置。继续？"
            : $"准备对【{item.Name}】执行：{item.RecommendedAction}\n\n预计可释放：{item.ReclaimableText}\n安全分：{item.SafetyText}\n\n继续？";
        if (MessageBox.Show(this, warning, "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { StatusText.Text = await WindowsMaintenanceService.ExecuteAsync(item); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "系统操作失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void QuarantineFinding_Click(object sender, RoutedEventArgs e)
    {
        if (FindingsGrid.SelectedItem is not CDriveFinding item) return;
        if (!item.CanQuarantine || item.IsProtected)
        {
            MessageBox.Show(this, "这个项目不允许整项隔离。请使用官方管理入口或打开目录逐项判断。", "安全保护");
            return;
        }
        if (MessageBox.Show(this, $"把【{item.Name}】移动到可恢复隔离区？\n\n{item.Path}\n{item.SizeText}", "进入隔离区", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            BusyProgress.Visibility = Visibility.Visible;
            var progress = new Progress<MigrationProgress>(p => StatusText.Text = $"隔离中：{p.CopiedFiles:N0} 个文件 / {SizeFormatter.Format(p.CopiedBytes)} · {p.CurrentPath}");
            await _quarantine.QuarantineAsync(item.Path, progress);
            StatusText.Text = "隔离完成；建议重新体检确认空间变化。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "隔离失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { BusyProgress.Visibility = Visibility.Collapsed; }
    }

    private void OpenAppData_Click(object sender, RoutedEventArgs e)
    {
        if (AppDataGrid.SelectedItem is CDriveAppDataEntry item) Reveal(item.Path);
    }

    private void OpenVirtualDisk_Click(object sender, RoutedEventArgs e)
    {
        if (VirtualDiskGrid.SelectedItem is CDriveVirtualDiskEntry item) Reveal(item.Path);
    }

    private async void DockerReport_Click(object sender, RoutedEventArgs e)
    {
        try { StatusText.Text = await WindowsMaintenanceService.ExecuteAsync(ActionOnly("docker-prune-review")); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Docker"); }
    }

    private async void WslReport_Click(object sender, RoutedEventArgs e)
    {
        try { StatusText.Text = await WindowsMaintenanceService.ExecuteAsync(ActionOnly("wsl-review")); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "WSL"); }
    }

    private void BuildTargetPlan_Click(object sender, RoutedEventArgs e) => BuildPlan(false);
    private void BuildEmergencyPlan_Click(object sender, RoutedEventArgs e) => BuildPlan(true);

    private void BuildPlan(bool emergency)
    {
        if (_report is null) { MessageBox.Show(this, "先执行一次 C 盘体检。", "目标式清理"); return; }
        if (!TryReadGb(TargetGbBox.Text, out var targetBytes)) { MessageBox.Show(this, "目标请输入大于 0 的 GB，例如 50。", "目标式清理"); return; }
        var plan = CDriveTargetPlanner.Build(_report.Findings, targetBytes, emergency);
        TargetPlanGrid.ItemsSource = plan.Items;
        TargetPlanText.Text = $"{plan.Mode}：{plan.Summary} 风险惩罚分 {plan.RiskPenalty:N0}；共 {plan.Items.Count:N0} 项。";
    }

    private async void StartMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (_monitorCts is not null) return;
        if (!CDriveWriteMonitor.IsElevated)
        {
            if (MessageBox.Show(this, "实时写入追踪需要管理员权限。是否以管理员模式重新打开 C盘专清？", "需要管理员权限", MessageBoxButton.YesNo) == MessageBoxResult.Yes) RelaunchAsAdmin();
            return;
        }
        _monitorCts = new CancellationTokenSource();
        StartMonitorButton.IsEnabled = false;
        StopMonitorButton.IsEnabled = true;
        var progress = new Progress<IReadOnlyList<CDriveWriteActivityEntry>>(items =>
        {
            WriteActivityGrid.ItemsSource = items;
            WriteStatusText.Text = $"已捕获 {items.Count:N0} 个进程；累计写入约 {SizeFormatter.Format(items.Sum(x => x.BytesWritten))}。";
        });
        try { await _writeMonitor.MonitorAsync(progress, _monitorCts.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "实时写入追踪失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally
        {
            _monitorCts?.Dispose();
            _monitorCts = null;
            StartMonitorButton.IsEnabled = true;
            StopMonitorButton.IsEnabled = false;
            WriteStatusText.Text += " 追踪已停止。";
        }
    }

    private void StopMonitor_Click(object sender, RoutedEventArgs e) => _monitorCts?.Cancel();
    private void OpenStorage_Click(object sender, RoutedEventArgs e) => WindowsMaintenanceService.OpenStorageSettings();
    private void RelaunchAdmin_Click(object sender, RoutedEventArgs e) => RelaunchAsAdmin();

    private void RelaunchAsAdmin()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return;
            Process.Start(new ProcessStartInfo(exe, "--c-drive") { UseShellExecute = true, Verb = "runas" });
            Close();
        }
        catch { }
    }

    private static CDriveFinding ActionOnly(string actionKey) => new()
    {
        Id = actionKey, Name = actionKey, Category = "工具", Path = string.Empty, SizeBytes = 0,
        ReclaimableBytes = 0, SafetyScore = 90, Risk = "低", Detail = string.Empty,
        RecommendedAction = string.Empty, ActionKey = actionKey, CanQuarantine = false, IsProtected = false
    };

    private static void Reveal(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private static bool TryReadGb(string text, out long bytes)
    {
        bytes = 0;
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && !double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
        if (value <= 0 || value > 100_000) return false;
        bytes = (long)(value * 1024d * 1024 * 1024);
        return bytes > 0;
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _analysisCts?.Cancel();
        _monitorCts?.Cancel();
    }
}
