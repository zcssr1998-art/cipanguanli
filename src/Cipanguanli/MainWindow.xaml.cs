using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Cipanguanli.Core;
using Microsoft.Win32;

namespace Cipanguanli;

public partial class MainWindow : Window
{
    private readonly DiskScanner _scanner = new();
    private readonly DuplicateFinder _duplicateFinder = new();
    private CancellationTokenSource? _operationCts;
    private ScanResult? _lastResult;
    private IReadOnlyList<DuplicateFileGroup> _duplicateResults = Array.Empty<DuplicateFileGroup>();

    public ObservableCollection<ScanTarget> Targets { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                var selected = drive.DriveType == DriveType.Fixed;
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : $"{drive.Name}  {drive.VolumeLabel}";
                Targets.Add(new ScanTarget { DisplayName = label, Path = drive.RootDirectory.FullName, IsSelected = selected });
            }
            catch { }
        }

        var admin = IsAdministrator();
        AdminStatusText.Text = admin ? "管理员权限：已开启" : "管理员权限：未开启";
        AdminButton.Visibility = admin ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void StartScan_Click(object sender, RoutedEventArgs e)
    {
        if (_operationCts is not null) return;
        var roots = SelectedRoots();
        if (roots.Length == 0)
        {
            MessageBox.Show(this, "请至少选择一个磁盘或目录。", "磁盘管理", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryReadGb(ThresholdBox.Text, out var thresholdBytes) ||
            !TryReadGb(OldMinBox.Text, out var oldMinBytes) ||
            !int.TryParse(OldDaysBox.Text.Trim(), out var oldDays) || oldDays <= 0 || oldDays > 100_000)
        {
            MessageBox.Show(this, "参数无效。示例：大文件阈值 5 GB、旧文件天数 180、旧文件最小 1 GB。", "磁盘管理", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BeginOperation();
        _lastResult = null;
        LargeFilesGrid.ItemsSource = null;
        LargeFoldersGrid.ItemsSource = null;
        OldFilesGrid.ItemsSource = null;
        CleanupGrid.ItemsSource = null;
        SpaceMapTree.ItemsSource = null;
        FilesSeenText.Text = "0";
        BytesSeenText.Text = "0 B";
        LargeFilesText.Text = "0";
        OldFilesText.Text = "0";
        CleanupText.Text = "0";

        var progress = new Progress<ScanProgress>(p =>
        {
            FilesSeenText.Text = p.FilesSeen.ToString("N0");
            BytesSeenText.Text = SizeFormatter.Format(p.BytesSeen);
            LargeFilesText.Text = p.LargeFilesFound.ToString("N0");
            StatusText.Text = string.IsNullOrWhiteSpace(p.CurrentPath)
                ? $"已检查 {p.FilesSeen:N0} 个文件"
                : $"正在扫描：{p.CurrentPath}    ·    跳过目录 {p.SkippedDirectories:N0}";
        });

        try
        {
            var result = await _scanner.ScanAsync(roots, thresholdBytes, oldDays, oldMinBytes, progress, _operationCts!.Token);
            _lastResult = result;
            LargeFilesGrid.ItemsSource = result.LargeFiles;
            LargeFoldersGrid.ItemsSource = result.LargeFolders;
            OldFilesGrid.ItemsSource = result.OldFiles;
            CleanupGrid.ItemsSource = result.CleanupCandidates;
            SpaceMapTree.ItemsSource = result.FolderMapRoots;
            FilesSeenText.Text = result.FilesSeen.ToString("N0");
            BytesSeenText.Text = SizeFormatter.Format(result.BytesSeen);
            LargeFilesText.Text = result.LargeFiles.Count.ToString("N0");
            OldFilesText.Text = result.OldFiles.Count.ToString("N0");
            CleanupText.Text = result.CleanupCandidates.Count.ToString("N0");
            StatusText.Text = $"扫描完成：{result.FilesSeen:N0} 个文件，{SizeFormatter.Format(result.BytesSeen)}，大文件 {result.LargeFiles.Count:N0} 个，旧文件 {result.OldFiles.Count:N0} 个，耗时 {result.Elapsed:mm\\:ss}";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "任务已取消。";
        }
        catch (Exception ex)
        {
            StatusText.Text = "扫描失败。";
            MessageBox.Show(this, ex.ToString(), "扫描失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndOperation();
        }
    }

    private async void FindDuplicates_Click(object sender, RoutedEventArgs e)
    {
        if (_operationCts is not null) return;
        var roots = SelectedRoots();
        if (roots.Length == 0)
        {
            MessageBox.Show(this, "请至少选择一个磁盘或目录。", "重复文件", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!TryReadMb(DuplicateMinBox.Text, out var minimumBytes))
        {
            MessageBox.Show(this, "重复文件最小值请输入大于 0 的 MB，例如 100。", "重复文件", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BeginOperation();
        DuplicateGrid.ItemsSource = null;
        DuplicateWasteText.Text = "预计可回收：0 B";
        var progress = new Progress<DuplicateProgress>(p =>
        {
            StatusText.Text = $"重复检测：已检查 {p.FilesSeen:N0} 个文件，候选 {p.CandidateFiles:N0}，已哈希 {p.HashedFiles:N0}，重复组 {p.GroupsFound:N0} · {p.CurrentPath}";
        });

        try
        {
            _duplicateResults = await _duplicateFinder.FindAsync(roots, minimumBytes, progress, _operationCts!.Token);
            DuplicateGrid.ItemsSource = _duplicateResults;
            var reclaimable = _duplicateResults.Sum(x => x.ReclaimableBytes);
            DuplicateWasteText.Text = $"预计可回收：{SizeFormatter.Format(reclaimable)}";
            StatusText.Text = $"重复检测完成：{_duplicateResults.Count:N0} 组，理论可回收 {SizeFormatter.Format(reclaimable)}。这里只识别，不自动删除。";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "重复检测已取消。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "重复检测失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndOperation();
        }
    }

    private void StopScan_Click(object sender, RoutedEventArgs e) => _operationCts?.Cancel();

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择要扫描的目录", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        if (Targets.Any(t => t.Path.Equals(dialog.FolderName, StringComparison.OrdinalIgnoreCase))) return;
        Targets.Add(new ScanTarget { DisplayName = new DirectoryInfo(dialog.FolderName).Name, Path = dialog.FolderName, IsSelected = true });
    }

    private void AdminButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return;
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            Close();
        }
        catch { }
    }

    private void OpenSelectedFile_Click(object sender, RoutedEventArgs e) => RevealFile(LargeFilesGrid.SelectedItem as LargeFileEntry);
    private void LargeFilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => RevealFile(LargeFilesGrid.SelectedItem as LargeFileEntry);
    private void CopySelectedFilePath_Click(object sender, RoutedEventArgs e)
    {
        if (LargeFilesGrid.SelectedItem is LargeFileEntry entry) Clipboard.SetText(entry.Path);
    }

    private void OpenSelectedFolder_Click(object sender, RoutedEventArgs e)
    {
        if (LargeFoldersGrid.SelectedItem is FolderSummary folder) OpenFolder(folder.Path);
    }
    private void CopySelectedFolderPath_Click(object sender, RoutedEventArgs e)
    {
        if (LargeFoldersGrid.SelectedItem is FolderSummary folder) Clipboard.SetText(folder.Path);
    }

    private void OpenSelectedOldFile_Click(object sender, RoutedEventArgs e)
    {
        if (OldFilesGrid.SelectedItem is OldFileEntry entry) RevealPath(entry.Path);
    }

    private void SpaceMapTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SpaceMapTree.SelectedItem is FolderMapNode node) OpenFolder(node.Path);
    }

    private void OpenCleanupFolder_Click(object sender, RoutedEventArgs e)
    {
        if (CleanupGrid.SelectedItem is FolderSummary folder) OpenFolder(folder.Path);
    }

    private void OpenDuplicateFirst_Click(object sender, RoutedEventArgs e)
    {
        if (DuplicateGrid.SelectedItem is DuplicateFileGroup group && group.Paths.Count > 0) RevealPath(group.Paths[0]);
    }

    private void CopyDuplicatePaths_Click(object sender, RoutedEventArgs e)
    {
        if (DuplicateGrid.SelectedItem is DuplicateFileGroup group) Clipboard.SetText(group.PathsText);
    }

    private async void MigrateSelectedFile_Click(object sender, RoutedEventArgs e)
    {
        if (LargeFilesGrid.SelectedItem is LargeFileEntry entry) await MigratePathAsync(entry.Path, entry.Risk);
    }

    private async void MigrateSelectedFolder_Click(object sender, RoutedEventArgs e)
    {
        if (LargeFoldersGrid.SelectedItem is FolderSummary folder) await MigratePathAsync(folder.Path, folder.Risk);
    }

    private async void MigrateSelectedOldFile_Click(object sender, RoutedEventArgs e)
    {
        if (OldFilesGrid.SelectedItem is OldFileEntry entry) await MigratePathAsync(entry.Path, entry.Risk);
    }

    private async Task MigratePathAsync(string sourcePath, string risk)
    {
        if (_operationCts is not null) return;
        if (risk.Contains("禁止", StringComparison.Ordinal))
        {
            MessageBox.Show(this, "这是系统关键文件，程序拒绝迁移。", "安全保护", MessageBoxButton.OK, MessageBoxImage.Stop);
            return;
        }

        var dialog = new OpenFolderDialog { Title = "选择迁移到的目标目录", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        var warning = risk is "高" or "中-高"
            ? $"该项目风险等级为【{risk}】。\n\n迁移会在验证目标副本后移除源文件/目录。确定继续？"
            : "迁移会在验证目标副本后移除源文件/目录。确定继续？";
        if (MessageBox.Show(this, warning, "确认迁移", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        BeginOperation();
        var progress = new Progress<MigrationProgress>(p =>
        {
            StatusText.Text = $"正在迁移：{p.CopiedFiles:N0} 个文件 / {SizeFormatter.Format(p.CopiedBytes)} · {p.CurrentPath}";
        });

        try
        {
            var result = await MigrationService.MigrateAsync(sourcePath, dialog.FolderName, progress, _operationCts!.Token);
            if (result.SourceRemoved)
            {
                StatusText.Text = $"迁移完成：{result.FilesMoved:N0} 个文件，{result.BytesMovedText} → {result.DestinationPath}。建议重新扫描刷新列表。";
                MessageBox.Show(this, $"迁移完成。\n\n目标：{result.DestinationPath}\n大小：{result.BytesMovedText}\n\n建议重新扫描刷新结果。", "迁移完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText.Text = "目标副本已校验，但源目录删除失败，因此保留了两份。";
                MessageBox.Show(this, $"目标副本已复制并校验，但源目录无法删除。\n\n为避免数据丢失，程序保留了源和目标两份。\n目标：{result.DestinationPath}", "迁移部分完成", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "迁移已取消；删除源之前取消时，源数据会保留。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "迁移失败", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "迁移失败；源数据默认保留。";
        }
        finally
        {
            EndOperation();
        }
    }

    private void ExportFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult is null || _lastResult.LargeFiles.Count == 0)
        {
            MessageBox.Show(this, "当前没有可导出的结果。", "磁盘管理", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog { Filter = "CSV 文件 (*.csv)|*.csv", FileName = "磁盘大文件.csv" };
        if (dialog.ShowDialog(this) != true) return;

        var sb = new StringBuilder();
        sb.AppendLine("大小(字节),实际占用(字节),类型,风险,修改时间,路径,说明");
        foreach (var file in _lastResult.LargeFiles)
        {
            sb.Append(file.SizeBytes).Append(',')
              .Append(file.AllocatedBytes).Append(',')
              .Append(Csv(file.Category)).Append(',')
              .Append(Csv(file.Risk)).Append(',')
              .Append(Csv(file.LastModifiedText)).Append(',')
              .Append(Csv(file.Path)).Append(',')
              .Append(Csv(file.Note)).AppendLine();
        }
        File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
        StatusText.Text = $"已导出：{dialog.FileName}";
    }

    private string[] SelectedRoots() => Targets.Where(t => t.IsSelected).Select(t => t.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private void BeginOperation()
    {
        _operationCts = new CancellationTokenSource();
        StartButton.IsEnabled = false;
        DuplicateButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        ScanProgress.Visibility = Visibility.Visible;
        ThresholdBox.IsEnabled = false;
        OldDaysBox.IsEnabled = false;
        OldMinBox.IsEnabled = false;
        DuplicateMinBox.IsEnabled = false;
    }

    private void EndOperation()
    {
        _operationCts?.Dispose();
        _operationCts = null;
        StartButton.IsEnabled = true;
        DuplicateButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        ScanProgress.Visibility = Visibility.Collapsed;
        ThresholdBox.IsEnabled = true;
        OldDaysBox.IsEnabled = true;
        OldMinBox.IsEnabled = true;
        DuplicateMinBox.IsEnabled = true;
    }

    private static void RevealFile(LargeFileEntry? entry)
    {
        if (entry is not null) RevealPath(entry.Path);
    }

    private static void RevealPath(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private static void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private static bool TryReadGb(string text, out long bytes) => TryReadSize(text, 1024d * 1024d * 1024d, out bytes);
    private static bool TryReadMb(string text, out long bytes) => TryReadSize(text, 1024d * 1024d, out bytes);

    private static bool TryReadSize(string text, double multiplier, out long bytes)
    {
        bytes = 0;
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
            !double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
        if (value <= 0 || value > 1024 * 1024) return false;
        bytes = checked((long)(value * multiplier));
        return bytes > 0;
    }

    private static string Csv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r')) return value;
        return '"' + value.Replace("\"", "\"\"") + '"';
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

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) => _operationCts?.Cancel();
}
