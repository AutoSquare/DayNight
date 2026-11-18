using System.Runtime.InteropServices;
using System.Text;

namespace DayNight.Services;

/// <summary>
/// 全屏检测：优先用 SHQueryUserNotificationState（覆盖 D3D 独占全屏、演示模式），
/// 再用「前台窗口是否铺满所在显示器」兜底（覆盖浏览器视频全屏、展台放映、无边框全屏等场景）。
/// </summary>
public static class FullscreenDetector
{
    private static bool? _lastFullscreen;
    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE state);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder name, int count);

    public enum QUERY_USER_NOTIFICATION_STATE
    {
        QUNS_NOT_PRESENT = 1,
        QUNS_BUSY = 2,
        QUNS_RUNNING_D3D_FULL_SCREEN = 3,
        QUNS_PRESENTATION_MODE = 4,
        QUNS_ACCEPTS_NOTIFICATIONS = 5,
        QUNS_QUIET_TIME = 6,
        QUNS_APP = 7,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    public static bool IsFullscreen()
    {
        var quns = QueryState();
        var foregroundFullscreen = IsForegroundWindowFullscreen();

        // 只在状态变化时写日志，避免每分钟刷屏
        var fullscreen = ShouldDefer(quns, foregroundFullscreen);
        if (_lastFullscreen != fullscreen)
        {
            _lastFullscreen = fullscreen;
            LogService.Info($"[全屏] QUNS={quns} 前台铺满={foregroundFullscreen} → 延迟切换={fullscreen}");
        }

        return fullscreen;
    }

    /// <summary>独占全屏、演示模式或前台铺满屏幕才延迟；通知忙碌状态本身可能来自后台覆盖层。</summary>
    public static bool ShouldDefer(QUERY_USER_NOTIFICATION_STATE state, bool foregroundFullscreen) =>
        foregroundFullscreen || state is QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
            or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;

    /// <summary>读取原始状态（诊断用）。</summary>
    public static QUERY_USER_NOTIFICATION_STATE QueryState()
    {
        try
        {
            var hr = SHQueryUserNotificationState(out var s);
            return hr != 0 ? QUERY_USER_NOTIFICATION_STATE.QUNS_NOT_PRESENT : s;
        }
        catch
        {
            return QUERY_USER_NOTIFICATION_STATE.QUNS_NOT_PRESENT;
        }
    }

    /// <summary>前台窗口是否铺满所在显示器（排除桌面/任务栏自身）。</summary>
    private static bool IsForegroundWindowFullscreen()
    {
        try
        {
            var hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return false;

            var sb = new StringBuilder(256);
            GetClassNameW(hWnd, sb, sb.Capacity);
            var cls = sb.ToString();
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                return false; // 桌面与任务栏不算全屏
            }

            if (!GetWindowRect(hWnd, out var wr)) return false;

            var hMonitor = MonitorFromWindow(hWnd, 2 /* MONITOR_DEFAULTTONEAREST */);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfoW(hMonitor, ref mi)) return false;

            var m = mi.rcMonitor;
            const int tolerance = 2;
            return wr.Left <= m.Left + tolerance
                && wr.Top <= m.Top + tolerance
                && wr.Right >= m.Right - tolerance
                && wr.Bottom >= m.Bottom - tolerance;
        }
        catch
        {
            return false;
        }
    }
}
