using Avalonia;
using System;
using System.Linq;
using System.Threading;

namespace DayNight;

sealed class Program
{
    /// <summary>静默启动（仅托盘）。</summary>
    public static bool SilentStart { get; private set; }

#if DEBUG
    /// <summary>启动后直接选中的页面索引（--page N，调试/验证用）。</summary>
    public static int StartPage { get; private set; } = -1;

    /// <summary>UI 自测（--uitest [help|osd]）：自动弹帮助窗或 OSD 供外部截图验证。</summary>
    public static bool UITest { get; private set; }

    /// <summary>UI 自测目标为 OSD 弹窗。</summary>
    public static bool UITestOsd { get; private set; }

    /// <summary>UI 自测：2 秒后切暗色主题（验证外观切换链路）。</summary>
    public static bool UITestDark { get; private set; }

    /// <summary>UI 自测：1 秒后直接弹气泡通知（验证 Toast 渲染）。</summary>
    public static bool UITestToast { get; private set; }

    /// <summary>UI 自测：2 秒后触发最小化（验证最小化进托盘）。</summary>
    public static bool UITestMinTray { get; private set; }

    /// <summary>UI 自测：静默启动后模拟托盘「打开主界面」（验证 Opened 订阅已解绑、窗口不再闪现消失）。</summary>
    public static bool UITestShow { get; private set; }

    /// <summary>UI 自测：「保持到明天」完整流程（点已结束行→时间轴保持态→复位）。</summary>
    public static bool UITestHold { get; private set; }

    /// <summary>UI 自测：托盘菜单项逻辑（暂停/恢复、手动切换下一时段）。</summary>
    public static bool UITestTray { get; private set; }

    /// <summary>UI 自测：全屏检测 P/Invoke 返回状态。</summary>
    public static bool UITestFullscreen { get; private set; }
#endif

    [STAThread]
    public static void Main(string[] args)
    {
        SilentStart = args.Contains("--silent");

#if DEBUG
        if (args.Contains("--smoke"))
        {
            // M1 技术冒烟：纯命令行，不进入 UI
            Environment.Exit(SmokeTestRunner.Run());
        }
#endif

        // --apply <theme.theme>：命令行直接应用主题
        var applyFlag = Array.IndexOf(args, "--apply");
        if (applyFlag >= 0 && args.Length > applyFlag + 1)
        {
            var themePath = args[applyFlag + 1];
            var ok = new DayNight.Services.ThemeApplyService().Apply(themePath);
            Environment.Exit(ok ? 0 : 1);
        }

#if DEBUG
        var pageFlag = Array.IndexOf(args, "--page");
        if (pageFlag >= 0 && args.Length > pageFlag + 1 && int.TryParse(args[pageFlag + 1], out var page))
        {
            StartPage = page;
        }
        UITest = args.Contains("--uitest");
        var uitestFlag = Array.IndexOf(args, "--uitest");
        UITestOsd = uitestFlag >= 0 && args.Length > uitestFlag + 1 &&
                    args[uitestFlag + 1].Equals("osd", StringComparison.OrdinalIgnoreCase);
        UITestDark = uitestFlag >= 0 && args.Length > uitestFlag + 1 &&
                     args[uitestFlag + 1].Equals("dark", StringComparison.OrdinalIgnoreCase);
        UITestToast = uitestFlag >= 0 && args.Length > uitestFlag + 1 &&
                      args[uitestFlag + 1].Equals("toast", StringComparison.OrdinalIgnoreCase);
        UITestMinTray = uitestFlag >= 0 && args.Length > uitestFlag + 1 &&
                        args[uitestFlag + 1].Equals("mintray", StringComparison.OrdinalIgnoreCase);
        UITestShow = uitestFlag >= 0 && args.Length > uitestFlag + 1 &&
                     args[uitestFlag + 1].Equals("show", StringComparison.OrdinalIgnoreCase);
        UITestHold = uitestFlag >= 0 && args.Length > uitestFlag + 1 &&
                     args[uitestFlag + 1].Equals("hold", StringComparison.OrdinalIgnoreCase);
        UITestTray = uitestFlag >= 0 && args.Length > uitestFlag + 1 &&
                     args[uitestFlag + 1].Equals("tray", StringComparison.OrdinalIgnoreCase);
        UITestFullscreen = uitestFlag >= 0 && args.Length > uitestFlag + 1 &&
                           args[uitestFlag + 1].Equals("fs", StringComparison.OrdinalIgnoreCase);

#endif
        // 单实例：已有实例运行时直接退出
        using var mutex = new Mutex(true, @"Local\DayNight.SingleInstance", out var isNew);
        if (!isNew)
        {
            Console.WriteLine("晨昏线已在运行，本次启动退出。");
            return;
        }
        GC.KeepAlive(mutex);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
