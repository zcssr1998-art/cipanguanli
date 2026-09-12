using System.IO;
using System.Windows;
using Cipanguanli.Core;

namespace Cipanguanli;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            var reportPath = Path.Combine(Environment.CurrentDirectory, "selftest-report.json");
            var reportArg = e.Args.FirstOrDefault(a => a.StartsWith("--report=", StringComparison.OrdinalIgnoreCase));
            if (reportArg is not null) reportPath = reportArg["--report=".Length..].Trim('"');
            var passed = await SelfTest.RunAsync(reportPath);
            Shutdown(passed ? 0 : 1);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
