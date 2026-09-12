using System.Diagnostics;
using System.IO;

namespace Cipanguanli.Core;

public static class WindowsMaintenanceService
{
    public static async Task<string> ExecuteAsync(CDriveFinding finding, CancellationToken cancellationToken = default)
    {
        switch (finding.ActionKey)
        {
            case "storage-settings":
                OpenUri("ms-settings:storagesense");
                return "已打开 Windows 存储设置。";
            case "apps-settings":
                OpenUri("ms-settings:appsfeatures");
                return "已打开 Windows 应用管理。";
            case "pagefile-settings":
                Start("SystemPropertiesAdvanced.exe");
                return "已打开系统高级设置；分页文件请在“性能 → 高级 → 虚拟内存”中调整。";
            case "restore-settings":
                Start("SystemPropertiesProtection.exe");
                return "已打开系统保护/还原点设置。";
            case "driver-manager":
                Start("devmgmt.msc");
                return "已打开设备管理器。旧驱动包应通过 PnPUtil/设备管理器处理，不要直接删除 DriverStore。";
            case "component-cleanup":
                await RunElevatedAndWaitAsync("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup", cancellationToken);
                return "DISM Component Cleanup 已执行完成或已由系统返回。建议重新分析 C 盘。";
            case "empty-recycle-bin":
                await RunElevatedAndWaitAsync("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"Clear-RecycleBin -DriveLetter C -Force -ErrorAction SilentlyContinue\"", cancellationToken);
                return "已请求清空 C 盘回收站。";
            case "open-location":
            case "quarantine":
                Reveal(finding.Path);
                return "已打开文件位置。";
            case "docker-prune-review":
                StartShell("docker system df && echo. && echo 请先核对 RECLAIMABLE，再决定是否 prune。 && pause");
                return "已打开 Docker 空间报告窗口。";
            case "wsl-review":
                StartShell("wsl --list --verbose && echo. && echo 建议先在发行版内清理，再执行 wsl --shutdown。 && pause");
                return "已打开 WSL 状态窗口。";
            default:
                if (Directory.Exists(finding.Path) || File.Exists(finding.Path))
                {
                    Reveal(finding.Path);
                    return "已打开文件位置。";
                }
                return "这个项目没有可自动执行的安全动作。";
        }
    }

    public static async Task<string> ConfigureHibernationAsync(bool reduced, CancellationToken cancellationToken = default)
    {
        var args = reduced ? "/h /type reduced" : "/h off";
        await RunElevatedAndWaitAsync("powercfg.exe", args, cancellationToken);
        return reduced
            ? "已请求把休眠文件设为 reduced。完整休眠将不可用，但通常保留快速启动能力。"
            : "已请求关闭休眠。hiberfil.sys 应被释放；完整休眠与快速启动可能受到影响。";
    }

    public static void OpenStorageSettings() => OpenUri("ms-settings:storagesense");

    private static void OpenUri(string uri)
        => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    private static void Start(string file)
        => Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });

    private static void StartShell(string command)
    {
        Process.Start(new ProcessStartInfo("cmd.exe", "/k " + command)
        {
            UseShellExecute = true
        });
    }

    private static void Reveal(string path)
    {
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        else if (Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private static async Task RunElevatedAndWaitAsync(string file, string arguments, CancellationToken ct)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = true,
            Verb = "runas"
        }) ?? throw new InvalidOperationException("无法启动系统维护命令。");
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) throw new InvalidOperationException($"系统命令退出码：{process.ExitCode}。可能被取消、权限不足或系统不支持该操作。");
    }
}
