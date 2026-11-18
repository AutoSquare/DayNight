namespace DayNight.Models;

/// <summary>应用设置（持久化到 config.json）。</summary>
public sealed class AppSettings
{
    /// <summary>自动切换总开关；启动和唤醒时也必须遵守此设置。</summary>
    public bool AutoSwitchEnabled { get; set; } = true;

    /// <summary>应用外观主题：system / light / dark。</summary>
    public string ThemeMode { get; set; } = "system";

    /// <summary>开机自动启动（HKCU Run 键）。</summary>
    public bool Autostart { get; set; } = true;

    /// <summary>静默启动（仅托盘，不弹主窗口）。</summary>
    public bool SilentStart { get; set; } = true;

    /// <summary>最小化到托盘：开启时点最小化隐藏到托盘。</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>全屏时延迟切换。</summary>
    public bool DeferWhenFullscreen { get; set; } = true;

    /// <summary>启动和唤醒时立即对齐当前时段主题。</summary>
    public bool AlignOnStartAndResume { get; set; } = true;

    /// <summary>切换前显示 OSD 提示。</summary>
    public bool OsdEnabled { get; set; } = true;

    /// <summary>OSD 显示秒数（3–10）。</summary>
    public int OsdSeconds { get; set; } = 5;

    /// <summary>切换后托盘通知。</summary>
    public bool NotifyAfterSwitch { get; set; } = true;
}
