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
    private CancellationTokenSource? _cts;
    private ScanResult? _lastResult;

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
        if (_cts is not null) return;
        var roots = Targets.Where(t => t.IsSelected).Select(t => t.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roots.Length == 0)
        {
            MessageBox.Show(this, "请至少选择一个磁盘或目录。", "磁盘管理", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryReadThreshold(out var thresholdBytes))
        {
            MessageBox.Show(this, "阈值请输入大于 0 的数字，例如 5。", "磁盘管理", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _cts = new CancellationTokenSource();
        SetScanningState(true);
        _lastResult = null;
        LargeFilesGrid.ItemsSource = null;
        LargeFoldersGrid.ItemsSource = null;
        CleanupGrid.ItemsSource = null;
        FilesSeenText.Text = "0";
        BytesSeenText.Text = "0 B";
        LargeFilesText.Text = "0";
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
            var result = await _scanner.ScanAsync(roots, thresholdBytes, progress, _cts.Token);
            _lastResult = result;
            LargeFilesGrid.ItemsSource = result.LargeFiles;
            LargeFoldersGrid.ItemsSource = result.LargeFolders;
            CleanupGrid.ItemsSource = result.CleanupCandidates;
            FilesSeenText.Text = result.FilesSeen.ToString("N0");
            BytesSeenText.Text = SizeFormatter.Format(result.BytesSeen);
            LargeFilesText.Text = result.LargeFiles.Count.ToString("N0");
            CleanupText.Text = result.CleanupCandidates.Count.ToString("N0");
            StatusText.Text = $"扫描完成：{result.FilesSeen:N0} 个文件，{SizeFormatter.Format(result.BytesSeen)}，大文件 {result.LargeFiles.Count:N0} 个，跳过目录 {result.SkippedDirectories:N0}，耗时 {result.Elapsed:mm\\:ss}";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "扫描已取消。";
        }
        catch (Exception ex)
        {
            StatusText.Text = "扫描失败。";
            MessageBox.Show(this, ex.ToString(), "扫描失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetScanningState(false);
        }
    }

    private void StopScan_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

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

    private static void RevealFile(LargeFileEntry? entry)
    {
        if (entry is null) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{entry.Path}\"") { UseShellExecute = true });
    }

    private void CopySelectedFilePath_Click(object sender, RoutedEventArgs e)
    {
        if (LargeFilesGrid.SelectedItem is LargeFileEntry entry) Clipboard.SetText(entry.Path);
    }

    private void OpenSelectedFolder_Click(object sender, RoutedEventArgs e)
    {
        if (LargeFoldersGrid.SelectedItem is not FolderSummary folder) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder.Path}\"") { UseShellExecute = true });
    }

    private void CopySelectedFolderPath_Click(object sender, RoutedEventArgs e)
    {
        if (LargeFoldersGrid.SelectedItem is FolderSummary folder) Clipboard.SetText(folder.Path);
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

    private bool TryReadThreshold(out long bytes)
    {
        bytes = 0;
        var text = ThresholdBox.Text.Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var gb) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out gb)) return false;
        if (gb <= 0 || gb > 1024 * 1024) return false;
        bytes = checked((long)(gb * 1024d * 1024d * 1024d));
        return bytes > 0;
    }

    private void SetScanningState(bool scanning)
    {
        StartButton.IsEnabled = !scanning;
        StopButton.IsEnabled = scanning;
        ScanProgress.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        ThresholdBox.IsEnabled = !scanning;
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

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) => _cts?.Cancel();
}
