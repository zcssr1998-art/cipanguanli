// ============================================================
//  全局 using 与类型别名
//
//  本项目同时启用了 WPF 与 WinForms（后者仅用于
//  FolderBrowserDialog）。两个框架存在大量同名类型
//  （MessageBox / Application / Color / ColorConverter /
//  SaveFileDialog / SizeConverter ...），会产生 CS0104
//  “不明确的引用”。这里统一给出别名，指定一律使用 WPF 版本。
// ============================================================

global using MessageBox = System.Windows.MessageBox;

global using Color = System.Windows.Media.Color;

global using ColorConverter = System.Windows.Media.ColorConverter;

global using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

global using SizeConverter = MiniDiskLab.Core.SizeConverter;

global using WpfApplication = System.Windows.Application;

global using WinFormsDialogResult = System.Windows.Forms.DialogResult;

global using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;

// 控件层面同样存在 WPF / WinForms 同名类型，统一指向 WPF 版本。
global using TextBox = System.Windows.Controls.TextBox;

global using Button = System.Windows.Controls.Button;

global using ComboBox = System.Windows.Controls.ComboBox;

global using ComboBoxItem = System.Windows.Controls.ComboBoxItem;

global using ProgressBar = System.Windows.Controls.ProgressBar;

global using TextBlock = System.Windows.Controls.TextBlock;

global using DataGrid = System.Windows.Controls.DataGrid;

global using DataGridTextColumn = System.Windows.Controls.DataGridTextColumn;

global using Grid = System.Windows.Controls.Grid;
