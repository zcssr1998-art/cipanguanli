using System.IO;
using System.Text.Json;
using System.Windows;
using Cipanguanli.Core;

namespace Cipanguanli;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var reportPath = Path.Combine(Environment.CurrentDirectory, "selftest-report.json");
        var reportArg = e.Args.FirstOrDefault(a => a.StartsWith("--report=", StringComparison.OrdinalIgnoreCase));
        if (reportArg is not null) reportPath = reportArg["--report=".Length..].Trim('"');

        if (e.Args.Any(a => a.Equals("--c-drive-self-test", StringComparison.OrdinalIgnoreCase)))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var passed = await CDriveSelfTest.RunAsync(reportPath);
            Shutdown(passed ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var passed = await SelfTest.RunAsync(reportPath);
            Shutdown(passed ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--ui-smoke", StringComparison.OrdinalIgnoreCase)))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var launcher = new LauncherWindow();
                MainWindow = launcher;
                launcher.Show();
                await Task.Delay(350);
                launcher.Close();

                var main = new MainWindow();
                MainWindow = main;
                main.Show();
                await Task.Delay(500);
                main.Close();

                var cdrive = new CDriveWindow();
                MainWindow = cdrive;
                cdrive.Show();
                await Task.Delay(650);
                cdrive.Close();

                await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { passed = true, test = "ui-smoke-v0.4", windows = new[] { "launcher", "main", "c-drive" } }, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0);
            }
            catch (Exception ex)
            {
                try { await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { passed = false, test = "ui-smoke-v0.4", error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true })); }
                catch { }
                Shutdown(1);
            }
            return;
        }

        if (e.Args.Any(a => a.Equals("--c-drive", StringComparison.OrdinalIgnoreCase)))
        {
            var cdrive = new CDriveWindow();
            MainWindow = cdrive;
            cdrive.Show();
            return;
        }

        string? startupPath = null;
        for (var i = 0; i < e.Args.Length; i++)
        {
            if (!e.Args[i].Equals("--scan-path", StringComparison.OrdinalIgnoreCase) || i + 1 >= e.Args.Length) continue;
            startupPath = e.Args[i + 1].Trim('"');
            break;
        }
        var inline = e.Args.FirstOrDefault(a => a.StartsWith("--scan-path=", StringComparison.OrdinalIgnoreCase));
        if (inline is not null) startupPath = inline["--scan-path=".Length..].Trim('"');

        if (!string.IsNullOrWhiteSpace(startupPath))
        {
            var mainWindow = new MainWindow(startupPath);
            MainWindow = mainWindow;
            mainWindow.Show();
            return;
        }

        var launcherWindow = new LauncherWindow();
        MainWindow = launcherWindow;
        launcherWindow.Show();
    }
}
