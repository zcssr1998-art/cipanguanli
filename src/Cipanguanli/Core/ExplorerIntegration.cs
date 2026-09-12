using Microsoft.Win32;

namespace Cipanguanli.Core;

public static class ExplorerIntegration
{
    private const string DirectoryKey = @"Software\Classes\Directory\shell\CipanguanliAnalyze";
    private const string DriveKey = @"Software\Classes\Drive\shell\CipanguanliAnalyze";

    public static void Install(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) throw new ArgumentException("无法确定程序路径。", nameof(executablePath));
        WriteKey(DirectoryKey, executablePath, "%V");
        WriteKey(DriveKey, executablePath, "%V");
    }

    public static void Remove()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(DirectoryKey, false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(DriveKey, false); } catch { }
    }

    public static bool IsInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DirectoryKey);
            return key is not null;
        }
        catch { return false; }
    }

    private static void WriteKey(string keyPath, string executablePath, string argument)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(null, "用 Cipanguanli 分析此位置");
        key.SetValue("Icon", executablePath);
        using var command = key.CreateSubKey("command");
        command.SetValue(null, $"\"{executablePath}\" --scan-path \"{argument}\"");
    }
}
