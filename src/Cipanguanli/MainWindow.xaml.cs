using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Cipanguanli.Core;
using Microsoft.Win32;

namespace Cipanguanli;

public partial class MainWindow : Window
{
    private readonly DiskScanner _scanner = new();
    private readonly DuplicateFinder _duplicateFinder = new();
    private readonly SoftwareOwnershipService _ownership = new();
    private readonly LearnedRuleStore _learnedRules = new();
    private readonly HistoryService _history = new();
    private readonly QuarantineService _quarantine = new();
    private readonly string? _startupPath;
    private CancellationTokenSource? _operationCts;
    private ScanResult? _lastResult;
    private IReadOnlyList<DuplicateFileGroup> _duplicateResults = Array.Empty<DuplicateFileGroup>();
    private IReadOnlyList<DiagnosticEntry> _diagnostics = Array.Empty<DiagnosticEntry>();
    private IReadOnlyList<ResidueCandidate> _residues = Array.Empty<ResidueCandidate>();
    private IReadOnlyList<SimilarMediaGroup> _similarMedia = Array.Empty<SimilarMediaGroup>();

    public ObservableCollection<ScanTarget> Targets { get; } = [];

    public MainWindow(string? startupPath = null)
    {
        _startupPath = startupPath;
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
        ExplorerMenuButton.Content = ExplorerIntegration.IsInstalled() ? "移除右键菜单" : "安装右键菜单";
        RefreshQuarantine();

        if (!string.IsNullOrWhiteSpace(_startupPath) && (Directory.Exists(_startupPath) || File.Exists(_startupPath)))
        {
            var targetPath = Directory.Exists(_startupPath) ? _startupPath! : Path.GetDirectoryName(_startupPath!)!;
            foreach (var target in Targets) target.IsSelected = false;
            var existing = Targets.FirstOrDefault(t => t.Path.Equals(targetPath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) existing.IsSelected = true;
            else Targets.Add(new ScanTarget { DisplayName = new DirectoryInfo(targetPath).Name, Path = targetPath, IsSelected = true });
            Dispatcher.BeginInvoke(() => StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        }
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
        _diagnostics = Array.Empty<DiagnosticEntry>();
        _residues = Array.Empty<ResidueCandidate>();
        LargeFilesGrid.ItemsSource = null;
        LargeFoldersGrid.ItemsSource = null;
        OldFilesGrid.ItemsSource = null;
        CleanupGrid.ItemsSource = null;
        SpaceMapTree.ItemsSource = null;
        DiagnosticGrid.ItemsSource = null;
        ResidueGrid.ItemsSource = null;
        GrowthGrid.ItemsSource = null;
        CleanupPlanGrid.ItemsSource = null;
        SimilarMediaGrid.ItemsSource = null;
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
            _diagnostics = DiagnosticAnalyzer.Analyze(result, _ownership, _learnedRules);
            _residues = ResidueDetector.Find(result, _ownership);
            var growth = _history.CompareAndSave(result);
            var plans = CleanupPlanner.Build(result, _duplicateResults, _residues);
            _similarMedia = SimilarMediaFinder.Find(result.LargeFiles);

            LargeFilesGrid.ItemsSource = result.LargeFiles;
            LargeFoldersGrid.ItemsSource = result.LargeFolders;
            OldFilesGrid.ItemsSource = result.OldFiles;
            CleanupGrid.ItemsSource = result.CleanupCandidates;
            SpaceMapTree.ItemsSource = result.FolderMapRoots;
            DiagnosticGrid.ItemsSource = _diagnostics;
            ResidueGrid.ItemsSource = _residues;
            GrowthGrid.ItemsSource = growth;
            CleanupPlanGrid.ItemsSource = plans;
            SimilarMediaGrid.ItemsSource = _similarMedia;
            FilesSeenText.Text = result.FilesSeen.ToString("N0");
            BytesSeenText.Text = SizeFormatter.Format(result.BytesSeen);
            LargeFilesText.Text = result.LargeFiles.Count.ToString("N0");
            OldFilesText.Text = result.OldFiles.Count.ToString("N0");
            CleanupText.Text = result.CleanupCandidates.Count.ToString("N0");
            StatusText.Text = $"诊断完成：{result.FilesSeen:N0} 个文件，{SizeFormatter.Format(result.BytesSeen)}；识别软件/项目归属 {_diagnostics.Count(x => x.Owner != "未识别"):N0} 项，疑似残留 {_residues.Count:N0} 项，耗时 {result.Elapsed:mm\\:ss}";
        }
        catch (OperationCanceledException) { StatusText.Text = "任务已取消。"; }
        catch (Exception ex)
        {
            StatusText.Text = "扫描失败。";
            MessageBox.Show(this, ex.ToString(), "扫描失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { EndOperation(); }
    }

    private async void FindDuplicates_Click(object sender, RoutedEventArgs e)
    {
        if (_operationCts is not null) return;
        var roots = SelectedRoots();
        if (roots.Length == 0) { MessageBox.Show(this, "请至少选择一个磁盘或目录。", "重复文件"); return; }
        if (!TryReadMb(DuplicateMinBox.Text, out var minimumBytes)) { MessageBox.Show(this, "重复文件最小值请输入大于 0 的 MB，例如 100。", "重复文件"); return; }
        BeginOperation();
        DuplicateGrid.ItemsSource = null;
        DuplicateWasteText.Text = "预计可回收：0 B";
        var progress = new Progress<DuplicateProgress>(p => StatusText.Text = $"重复检测：文件 {p.FilesSeen:N0}，候选 {p.CandidateFiles:N0}，已哈希 {p.HashedFiles:N0}，重复组 {p.GroupsFound:N0} · {p.CurrentPath}");
        try
        {
            _duplicateResults = await _duplicateFinder.FindAsync(roots, minimumBytes, progress, _operationCts!.Token);
            DuplicateGrid.ItemsSource = _duplicateResults;
            var reclaimable = _duplicateResults.Sum(x => x.ReclaimableBytes);
            DuplicateWasteText.Text = $"预计可回收：{SizeFormatter.Format(reclaimable)}";
            if (_lastResult is not null) CleanupPlanGrid.ItemsSource = CleanupPlanner.Build(_lastResult, _duplicateResults, _residues);
            StatusText.Text = $"重复检测完成：{_duplicateResults.Count:N0} 组，理论可回收 {SizeFormatter.Format(reclaimable)}。";
        }
        catch (OperationCanceledException) { StatusText.Text = "重复检测已取消。"; }
        catch (Exception ex) { MessageBox.Show(this, ex.ToString(), "重复检测失败"); }
        finally { EndOperation(); }
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

    private void ExplorerMenuButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ExplorerIntegration.IsInstalled()) ExplorerIntegration.Remove();
            else ExplorerIntegration.Install(Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序路径。"));
            ExplorerMenuButton.Content = ExplorerIntegration.IsInstalled() ? "移除右键菜单" : "安装右键菜单";
            StatusText.Text = ExplorerIntegration.IsInstalled() ? "资源管理器右键菜单已安装。" : "资源管理器右键菜单已移除。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "右键菜单"); }
    }

    private void OpenSelectedFile_Click(object sender, RoutedEventArgs e) => RevealFile(LargeFilesGrid.SelectedItem as LargeFileEntry);
    private void LargeFilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => RevealFile(LargeFilesGrid.SelectedItem as LargeFileEntry);
    private void CopySelectedFilePath_Click(object sender, RoutedEventArgs e) { if (LargeFilesGrid.SelectedItem is LargeFileEntry entry) Clipboard.SetText(entry.Path); }
    private void OpenSelectedFolder_Click(object sender, RoutedEventArgs e) { if (LargeFoldersGrid.SelectedItem is FolderSummary folder) OpenFolder(folder.Path); }
    private void CopySelectedFolderPath_Click(object sender, RoutedEventArgs e) { if (LargeFoldersGrid.SelectedItem is FolderSummary folder) Clipboard.SetText(folder.Path); }
    private void OpenSelectedOldFile_Click(object sender, RoutedEventArgs e) { if (OldFilesGrid.SelectedItem is OldFileEntry entry) RevealPath(entry.Path); }
    private void SpaceMapTree_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (SpaceMapTree.SelectedItem is FolderMapNode node) OpenFolder(node.Path); }
    private void OpenCleanupFolder_Click(object sender, RoutedEventArgs e) { if (CleanupGrid.SelectedItem is FolderSummary folder) OpenFolder(folder.Path); }
    private void OpenDuplicateFirst_Click(object sender, RoutedEventArgs e) { if (DuplicateGrid.SelectedItem is DuplicateFileGroup group && group.Paths.Count > 0) RevealPath(group.Paths[0]); }
    private void CopyDuplicatePaths_Click(object sender, RoutedEventArgs e) { if (DuplicateGrid.SelectedItem is DuplicateFileGroup group) Clipboard.SetText(group.PathsText); }

    private void OpenDiagnostic_Click(object sender, RoutedEventArgs e) { if (DiagnosticGrid.SelectedItem is DiagnosticEntry entry) RevealOrOpen(entry.Path); }

    private void ManageDiagnosticOwner_Click(object sender, RoutedEventArgs e)
    {
        if (DiagnosticGrid.SelectedItem is not DiagnosticEntry entry) return;
        if (entry.Owner == "未识别") { Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true }); return; }
        var prompt = $"准备打开【{entry.Owner}】的官方卸载/管理入口。\n\n优先通过软件/游戏平台管理，而不是直接删除安装目录。继续？";
        if (MessageBox.Show(this, prompt, "官方管理入口", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
        try
        {
            if (entry.UninstallCommand.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo(entry.UninstallCommand) { UseShellExecute = true });
            else if (!string.IsNullOrWhiteSpace(entry.UninstallCommand))
            {
                var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add("/c");
                psi.ArgumentList.Add(entry.UninstallCommand);
                Process.Start(psi);
            }
            else Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开管理入口"); }
    }

    private void LearnDiagnosticRule_Click(object sender, RoutedEventArgs e)
    {
        if (DiagnosticGrid.SelectedItem is not DiagnosticEntry entry) return;
        var category = LearnCategoryBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(category)) { MessageBox.Show(this, "先输入你希望记住的用途名称，例如“死亡搁浅素材”或“客户A工程”。", "规则学习"); return; }
        var prefix = entry.Kind == "文件" ? Path.GetDirectoryName(entry.Path) ?? entry.Path : entry.Path;
        _learnedRules.SaveRule(prefix, category, LearnNoteBox.Text.Trim());
        if (_lastResult is not null)
        {
            _diagnostics = DiagnosticAnalyzer.Analyze(_lastResult, _ownership, _learnedRules);
            DiagnosticGrid.ItemsSource = _diagnostics;
        }
        StatusText.Text = $"已记住路径规则：{prefix} → {category}";
    }

    private void OpenResidue_Click(object sender, RoutedEventArgs e) { if (ResidueGrid.SelectedItem is ResidueCandidate item) OpenFolder(item.Path); }

    private void FindSimilarMedia_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult is null) return;
        _similarMedia = SimilarMediaFinder.Find(_lastResult.LargeFiles);
        SimilarMediaGrid.ItemsSource = _similarMedia;
        StatusText.Text = $"相似媒体分析完成：{_similarMedia.Count:N0} 组候选。这里只表示相似，不代表可以删除。";
    }
    private void OpenSimilarMedia_Click(object sender, RoutedEventArgs e) { if (SimilarMediaGrid.SelectedItem is SimilarMediaGroup group && group.Paths.Count > 0) RevealPath(group.Paths[0]); }
    private void CopySimilarMediaPaths_Click(object sender, RoutedEventArgs e) { if (SimilarMediaGrid.SelectedItem is SimilarMediaGroup group) Clipboard.SetText(group.PathsText); }

    private async void MigrateSelectedFile_Click(object sender, RoutedEventArgs e) { if (LargeFilesGrid.SelectedItem is LargeFileEntry entry) await MigratePathAsync(entry.Path, entry.Risk); }
    private async void MigrateSelectedFolder_Click(object sender, RoutedEventArgs e) { if (LargeFoldersGrid.SelectedItem is FolderSummary folder) await MigratePathAsync(folder.Path, folder.Risk); }
    private async void MigrateSelectedOldFile_Click(object sender, RoutedEventArgs e) { if (OldFilesGrid.SelectedItem is OldFileEntry entry) await MigratePathAsync(entry.Path, entry.Risk); }

    private async void QuarantineSelectedFile_Click(object sender, RoutedEventArgs e) { if (LargeFilesGrid.SelectedItem is LargeFileEntry entry) await QuarantinePathAsync(entry.Path, entry.Risk); }
    private async void QuarantineSelectedFolder_Click(object sender, RoutedEventArgs e) { if (LargeFoldersGrid.SelectedItem is FolderSummary folder) await QuarantinePathAsync(folder.Path, folder.Risk); }
    private async void QuarantineSelectedOldFile_Click(object sender, RoutedEventArgs e) { if (OldFilesGrid.SelectedItem is OldFileEntry entry) await QuarantinePathAsync(entry.Path, entry.Risk); }
    private async void QuarantineDiagnostic_Click(object sender, RoutedEventArgs e) { if (DiagnosticGrid.SelectedItem is DiagnosticEntry entry) await QuarantinePathAsync(entry.Path, entry.Risk); }
    private async void QuarantineResidue_Click(object sender, RoutedEventArgs e) { if (ResidueGrid.SelectedItem is ResidueCandidate item) await QuarantinePathAsync(item.Path, "中"); }

    private async Task QuarantinePathAsync(string path, string risk)
    {
        if (_operationCts is not null) return;
        if (risk.Contains("禁止", StringComparison.Ordinal)) { MessageBox.Show(this, "系统关键内容禁止隔离。", "安全保护", MessageBoxButton.OK, MessageBoxImage.Stop); return; }
        var text = risk is "高" or "中-高"
            ? $"该项目风险为【{risk}】。隔离会把它移出原位置，相关软件/工程可能立即失效。\n\n确定仍要进入可恢复隔离区？"
            : "隔离会把项目移出原位置，但会记录原路径并支持恢复。确定继续？";
        if (MessageBox.Show(this, text, "进入隔离区", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        BeginOperation();
        try
        {
            var progress = new Progress<MigrationProgress>(p => StatusText.Text = $"正在隔离：{p.CopiedFiles:N0} 个文件 / {SizeFormatter.Format(p.CopiedBytes)} · {p.CurrentPath}");
            var item = await _quarantine.QuarantineAsync(path, progress, _operationCts!.Token);
            RefreshQuarantine();
            StatusText.Text = $"已进入隔离区：{item.OriginalPath}。可在“隔离区”页恢复。";
        }
        catch (OperationCanceledException) { StatusText.Text = "隔离已取消。"; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "隔离失败"); StatusText.Text = "隔离失败，源数据默认保留。"; }
        finally { EndOperation(); }
    }

    private void RefreshQuarantine_Click(object sender, RoutedEventArgs e) => RefreshQuarantine();
    private void RefreshQuarantine()
    {
        _quarantine.ForgetMissingItems();
        QuarantineGrid.ItemsSource = _quarantine.List();
    }

    private async void RestoreQuarantine_Click(object sender, RoutedEventArgs e)
    {
        if (_operationCts is not null || QuarantineGrid.SelectedItem is not QuarantineItem item) return;
        if (MessageBox.Show(this, $"恢复到原位置？\n\n{item.OriginalPath}", "恢复隔离项", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        BeginOperation();
        try
        {
            var progress = new Progress<MigrationProgress>(p => StatusText.Text = $"正在恢复：{p.CopiedFiles:N0} 个文件 / {SizeFormatter.Format(p.CopiedBytes)}");
            await _quarantine.RestoreAsync(item, progress, _operationCts!.Token);
            RefreshQuarantine();
            StatusText.Text = $"恢复完成：{item.OriginalPath}";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "恢复失败"); }
        finally { EndOperation(); }
    }

    private async Task MigratePathAsync(string sourcePath, string risk)
    {
        if (_operationCts is not null) return;
        if (risk.Contains("禁止", StringComparison.Ordinal)) { MessageBox.Show(this, "这是系统关键文件，程序拒绝迁移。", "安全保护", MessageBoxButton.OK, MessageBoxImage.Stop); return; }
        var dialog = new OpenFolderDialog { Title = "选择迁移到的目标目录", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        var warning = risk is "高" or "中-高" ? $"风险等级【{risk}】。迁移后软件/工程可能找不到原路径。确定继续？" : "迁移会在验证目标副本后移除源文件/目录。确定继续？";
        if (MessageBox.Show(this, warning, "确认迁移", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        BeginOperation();
        var progress = new Progress<MigrationProgress>(p => StatusText.Text = $"正在迁移：{p.CopiedFiles:N0} 个文件 / {SizeFormatter.Format(p.CopiedBytes)} · {p.CurrentPath}");
        try
        {
            var result = await MigrationService.MigrateAsync(sourcePath, dialog.FolderName, progress, _operationCts!.Token);
            StatusText.Text = result.SourceRemoved ? $"迁移完成：{result.BytesMovedText} → {result.DestinationPath}" : "目标副本已校验，但源删除失败，因此保留两份。";
            MessageBox.Show(this, result.SourceRemoved ? $"迁移完成。\n\n目标：{result.DestinationPath}\n大小：{result.BytesMovedText}" : $"目标副本已生成，但源未删除。\n目标：{result.DestinationPath}", "迁移结果");
        }
        catch (OperationCanceledException) { StatusText.Text = "迁移已取消。"; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "迁移失败"); StatusText.Text = "迁移失败；源数据默认保留。"; }
        finally { EndOperation(); }
    }

    private void ExportFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult is null || _lastResult.LargeFiles.Count == 0) { MessageBox.Show(this, "当前没有可导出的结果。", "磁盘管理"); return; }
        var dialog = new SaveFileDialog { Filter = "CSV 文件 (*.csv)|*.csv", FileName = "磁盘大文件.csv" };
        if (dialog.ShowDialog(this) != true) return;
        var sb = new StringBuilder();
        sb.AppendLine("大小(字节),实际占用(字节),类型,风险,修改时间,路径,说明");
        foreach (var file in _lastResult.LargeFiles)
        {
            sb.Append(file.SizeBytes).Append(',').Append(file.AllocatedBytes).Append(',').Append(Csv(file.Category)).Append(',').Append(Csv(file.Risk)).Append(',').Append(Csv(file.LastModifiedText)).Append(',').Append(Csv(file.Path)).Append(',').Append(Csv(file.Note)).AppendLine();
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
        _operationCts?.Dispose(); _operationCts = null;
        StartButton.IsEnabled = true; DuplicateButton.IsEnabled = true; StopButton.IsEnabled = false; ScanProgress.Visibility = Visibility.Collapsed;
        ThresholdBox.IsEnabled = true; OldDaysBox.IsEnabled = true; OldMinBox.IsEnabled = true; DuplicateMinBox.IsEnabled = true;
    }

    private static void RevealFile(LargeFileEntry? entry) { if (entry is not null) RevealPath(entry.Path); }
    private static void RevealOrOpen(string path) { if (File.Exists(path)) RevealPath(path); else OpenFolder(path); }
    private static void RevealPath(string path) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    private static void OpenFolder(string path) { if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
    private static bool TryReadGb(string text, out long bytes) => TryReadSize(text, 1024d * 1024d * 1024d, out bytes);
    private static bool TryReadMb(string text, out long bytes) => TryReadSize(text, 1024d * 1024d, out bytes);
    private static bool TryReadSize(string text, double multiplier, out long bytes)
    {
        bytes = 0;
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && !double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
        if (value <= 0 || value > 1024 * 1024) return false;
        bytes = checked((long)(value * multiplier)); return bytes > 0;
    }
    private static string Csv(string value) { if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r')) return value; return '"' + value.Replace("\"", "\"\"") + '"'; }
    private static bool IsAdministrator()
    {
        try { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }
    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) => _operationCts?.Cancel();
}
