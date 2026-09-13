using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace MiniDiskLab.App;

/// <summary>
/// 应用程序入口。同时支持无界面模式（供自动化冒烟测试使用）。
/// </summary>
/// <remarks>
/// 因为项目同时启用了 WPF 与 WinForms（后者仅用于文件夹选择对话框），
/// 这里统一使用完全限定名，避免 <c>Application</c> 产生歧义。
/// </remarks>
public partial class App : System.Windows.Application
{
    /// <summary>
    /// 无界面自检模式。用法：
    /// <c>MiniDiskLab.exe --self-test --data=&lt;目录&gt; --report=&lt;json路径&gt;</c>
    /// 该模式不会创建窗口，只运行核心扫描 / 分类 / 导出流程。
    /// </summary>
    public const string SelfTestSwitch = "--self-test";

    /// <summary>
    /// 界面自检模式。用法：<c>MiniDiskLab.exe --ui-check</c>
    /// 会真实构建 <see cref="MainWindow"/>（加载 XAML、解析全部 DynamicResource、
    /// 建立 DataGrid 列与筛选下拉框），但不显示窗口，然后以退出码报告结果。
    /// 用于在无法进行 GUI 自动化的环境中验证界面层是否能正常构造。
    /// </summary>
    public const string UiCheckSwitch = "--ui-check";

    /// <summary>无界面自检时使用的默认退出码（成功）。</summary>
    public const int ExitSuccess = 0;

    /// <summary>无界面自检时使用的默认退出码（失败）。</summary>
    public const int ExitFailure = 2;

    protected override void OnStartup(StartupEventArgs e)
    {
        // .NET 8 在中文 Windows 下默认仍会使用当前区域设置；显式固定，
        // 保证大小格式化在测试与生产环境中一致。
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(
                XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag)));

        if (e.Args.Any(a => string.Equals(a, SelfTestSwitch, StringComparison.OrdinalIgnoreCase)))
        {
            // 强制以 UTF-8 输出，避免中文在重定向到文件/管道时变成乱码。
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch (IOException)
            {
                // 没有附加控制台时忽略。
            }

            var exitCode = HeadlessSelfTest.Run(e.Args, Console.Out);
            Shutdown(exitCode);
            return;
        }

        if (e.Args.Any(a => string.Equals(a, UiCheckSwitch, StringComparison.OrdinalIgnoreCase)))
        {
            var code = UiSmokeCheck.Run(Console.Out);
            Shutdown(code);
            return;
        }

        base.OnStartup(e);
    }
}
