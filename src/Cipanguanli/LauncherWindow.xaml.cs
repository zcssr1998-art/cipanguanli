using System.Windows;

namespace Cipanguanli;

public partial class LauncherWindow : Window
{
    public LauncherWindow()
    {
        InitializeComponent();
    }

    private void OpenCDrive_Click(object sender, RoutedEventArgs e)
    {
        var window = new CDriveWindow();
        Application.Current.MainWindow = window;
        window.Show();
        Close();
    }

    private void OpenMain_Click(object sender, RoutedEventArgs e)
    {
        var window = new MainWindow();
        Application.Current.MainWindow = window;
        window.Show();
        Close();
    }
}
