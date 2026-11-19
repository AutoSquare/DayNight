using Microsoft.Win32;

namespace DayNight.Services;

/// <summary>开机自启：HKCU Run 键（无需管理员权限）。登记项必须指向本程序才算“已开启”；其他程序写入的值不抢占、不删除。</summary>
public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DayNight";

    /// <summary>开机自启是否指向本程序：值存在且其中登记的 exe 路径与当前进程一致。</summary>
    public static bool IsEnabled() => PointsToCurrentExe(GetRegisteredValue());

    /// <summary>显式开启：写入本程序路径（接管任何旧登记项）。</summary>
    public static void Enable()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            LogService.Warn("无法获取程序路径，开机自启未写入");
            return;
        }
        var tookOver = GetRegisteredValue() is { } existing && !PointsToCurrentExe(existing);
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, $"\"{exe}\" --silent");
        LogService.Info(tookOver ? "开机自启已开启（接管原登记项）" : "开机自启已开启");
    }

    /// <summary>显式关闭：仅删除指向本程序的自启项，其他程序写入的值不动。</summary>
    public static void Disable()
    {
        var existing = GetRegisteredValue();
        if (existing is null) return;
        if (!PointsToCurrentExe(existing))
        {
            LogService.Warn($"开机自启项指向其他程序（{ExtractExePath(existing)}），未删除");
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.DeleteValue(ValueName, false);
        LogService.Info("开机自启已关闭");
    }

    /// <summary>启动时按设置同步：缺失则补写；指向其他程序时不抢占，仅记日志（设置页如实显示为关）。</summary>
    public static void Sync(bool enabled)
    {
        if (!enabled)
        {
            Disable();
            return;
        }
        if (IsEnabled()) return;
        var existing = GetRegisteredValue();
        if (existing is not null)
        {
            LogService.Warn($"开机自启项指向其他程序（{ExtractExePath(existing)}），未抢占，设置页显示为关闭");
            return;
        }
        Enable();
    }

    private static string? GetRegisteredValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) as string;
    }

    /// <summary>登记项指向其他程序时返回其 exe 路径，否则 null（设置页提示用）。</summary>
    internal static string? ForeignHolderPath()
    {
        var value = GetRegisteredValue();
        return value is null || PointsToCurrentExe(value) ? null : ExtractExePath(value);
    }

    /// <summary>登记值是否指向当前进程的 exe；忽略大小写与路径分隔符差异，非法路径一律视为不匹配。</summary>
    internal static bool PointsToCurrentExe(string? runValue)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;
        var registered = ExtractExePath(runValue);
        if (registered is null) return false;
        try
        {
            return string.Equals(Path.GetFullPath(exe), Path.GetFullPath(registered),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>从 Run 值提取 exe 路径：带引号取首对引号内，否则取首个空白前的整段。</summary>
    internal static string? ExtractExePath(string? runValue)
    {
        if (string.IsNullOrWhiteSpace(runValue)) return null;
        var v = runValue.Trim();
        if (v[0] == '"')
        {
            var end = v.IndexOf('"', 1);
            return end > 1 ? v[1..end] : null;
        }
        var space = v.IndexOf(' ');
        return space < 0 ? v : v[..space];
    }
}
