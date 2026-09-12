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
                var window = new MainWindow();
                MainWindow = window;
                window.Show();
                await Task.Delay(700);
                window.Close();
                await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { passed = true, test = "ui-smoke" }, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0);
            }
            catch (Exception ex)
            {
                try
                {
                    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { passed = false, test = "ui-smoke", error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { }
                Shutdown(1);
            }
            return;
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
