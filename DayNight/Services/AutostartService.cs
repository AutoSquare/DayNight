using Microsoft.Win32;

namespace DayNight.Services;

/// <summary>开机自启：HKCU Run 键（无需管理员权限）。</summary>
public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DayNight";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string;
    }

    public static void Enable()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            LogService.Warn("无法获取程序路径，开机自启未写入");
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, $"\"{exe}\" --silent");
        LogService.Info("开机自启已开启");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, false);
            LogService.Info("开机自启已关闭");
        }
    }

    /// <summary>按设置同步注册表状态。</summary>
    public static void Sync(bool enabled)
    {
        if (enabled) Enable(); else Disable();
    }
}
