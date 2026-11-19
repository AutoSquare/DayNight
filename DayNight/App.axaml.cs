using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using DayNight.Models;
using DayNight.Services;
using DayNight.ViewModels;
using DayNight.Views;
using Microsoft.Win32;

namespace DayNight;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _pauseMenuItem;
    private DispatcherTimer? _tickTimer;
    private OsdWindow? _currentOsd;
    private DateTime? _skipSegmentStart;
    private DateTime _retryAfter;
    private string? _failedTheme;
    private (DateTime date, int index)? _lastObservedSegment;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var ctx = AppContext.Load();
        ApplyThemeVariant();

        // 开机自启：按设置同步注册表（默认开启，此前仅用户切换开关时才写）
        AutostartService.Sync(ctx.Settings.Autostart);

        var mainVm = new MainViewModel(ctx);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = new MainWindow { DataContext = mainVm };
            desktop.MainWindow.Closed += (_, _) =>
            {
                // 先断开托盘的窗口引用。关闭对话框和窗口事件返回后再结束消息循环。
                desktop.MainWindow = null;
                _tickTimer?.Stop();
                CleanupTray();
                Dispatcher.UIThread.Post(() => desktop.Shutdown(), DispatcherPriority.Send);
            };
#if DEBUG
            if (Program.StartPage is >= 0 and <= 3) mainVm.SelectPage(Program.StartPage);
#endif
            if (Program.SilentStart && ctx.Settings.SilentStart)
            {
                // Hide 必须在窗口 Opened 之后，否则被随后的自动 Show 吞掉。
                // 但 Opened 在每次 Show 都会触发：吞掉首次自动显示后必须解绑，
                // 否则托盘「打开主界面」的 Show 会被再次 Hide（窗口闪现即消失）。
                void HideOnFirstOpen(object? sender, EventArgs e)
                {
                    var window = desktop.MainWindow;
                    if (window is null) return;
                    window.Opened -= HideOnFirstOpen;
                    window.Hide();
                }
                desktop.MainWindow.Opened += HideOnFirstOpen;
            }

            SetupTray(desktop, mainVm);
            var wasPaused = ctx.Engine.Paused;
            var observedManual = ctx.Engine.Manual;
            ctx.StateChanged += () =>
            {
                UpdateTrayToolTip();
                if (_pauseMenuItem is not null)
                    _pauseMenuItem.Header = ctx.Engine.Paused ? "恢复自动切换" : "暂停调度";
                if (ctx.Engine.Paused || ctx.IsApplyingTheme ||
                    (ctx.Engine.Manual is not null && !ReferenceEquals(observedManual, ctx.Engine.Manual)))
                {
                    _currentOsd?.Close();
                    _currentOsd = null;
                }
                observedManual = ctx.Engine.Manual;
                var resumed = wasPaused && !ctx.Engine.Paused;
                wasPaused = ctx.Engine.Paused;
                if (resumed) AlignNow();
            };
            ctx.ScheduleChanged += () =>
            {
                _currentOsd?.Close();
                _currentOsd = null;
                _skipSegmentStart = null;
                _retryAfter = DateTime.MinValue;
                _failedTheme = null;
                AlignNow();
            };
            ctx.RaiseStateChanged();
            StartTicking(desktop, mainVm, ctx);

            // 开机和从睡眠唤醒时立即对齐当前时段主题
            SystemEvents.PowerModeChanged += (_, e) =>
            {
                if (e.Mode != PowerModes.Resume) return;
                Dispatcher.UIThread.Post(() =>
                {
                    if (ctx.Settings.AlignOnStartAndResume) AlignNow();
                    LogService.Info("系统唤醒");
                });
            };

            // 启动对齐（无 OSD，直接应用当前段主题）
            Dispatcher.UIThread.Post(() =>
            {
                if (ctx.Settings.AlignOnStartAndResume) AlignNow();
            });

#if DEBUG
            RunUiDiagnostics(desktop, mainVm, ctx);
#endif
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyThemeVariant()
    {
        RequestedThemeVariant = AppContext.Instance.Settings.ThemeMode switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    private void StartTicking(IClassicDesktopStyleApplicationLifetime desktop, MainViewModel vm, AppContext ctx)
    {
        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += (_, _) => Tick(desktop, vm, ctx);
        _tickTimer.Start();
    }

    private void Tick(IClassicDesktopStyleApplicationLifetime desktop, MainViewModel vm, AppContext ctx)
    {
        try
        {
            var now = ctx.Engine.Now;
            var idx = ctx.Schedule.GetSegmentIndexAt(TimeOnly.FromDateTime(now));
            var segmentStart = ctx.Schedule.GetActiveSegmentStart(idx, now);
            var occurrence = (segmentStart, idx);
            var previousTheme = ctx.CurrentThemeId;
            var actualTheme = ctx.ReadCurrentTheme();
            if (_lastObservedSegment != occurrence || !string.Equals(previousTheme, actualTheme, StringComparison.OrdinalIgnoreCase))
            {
                _lastObservedSegment = occurrence;
                ctx.RaiseStateChanged();
            }

            if (ctx.Engine.Paused || ctx.IsApplyingTheme) return;

            var fullscreen = ctx.Settings.DeferWhenFullscreen && FullscreenDetector.IsFullscreen();
            if (ctx.IsFullscreenDeferred != fullscreen)
            {
                ctx.IsFullscreenDeferred = fullscreen;
                ctx.RaiseStateChanged();
            }
            if (fullscreen && _currentOsd is not null)
            {
                _currentOsd.Close();
                _currentOsd = null;
            }
            var manual = ctx.Engine.Manual;
            var decision = DecideSwitch(ctx, fullscreen, actualTheme);
            if (manual != ctx.Engine.Manual)
            {
                ctx.Save();
                ctx.RaiseStateChanged();
            }
            if (fullscreen || _currentOsd is not null) return;
            if (decision.HasAction)
            {
                // 全屏结束或错过切换点：允许完整倒计时后补切。
                if (_skipSegmentStart == segmentStart && ctx.Engine.Manual is null) return;
                RunSwitchFlow(vm, decision.SegmentIndex, segmentStart,
                    ctx.Engine.Now.AddSeconds(ctx.Settings.OsdSeconds));
                return;
            }
            if (!ctx.Settings.OsdEnabled) return;
            var upcoming = ctx.Engine.GetUpcomingSwitch(TimeSpan.FromSeconds(ctx.Settings.OsdSeconds), actualTheme);
            if (upcoming is null || _skipSegmentStart == upcoming.ExecuteAt) return;
            RunSwitchFlow(vm, upcoming.SegmentIndex, upcoming.ExecuteAt, upcoming.ExecuteAt);
        }
        catch (Exception ex)
        {
            LogService.Error("调度 tick 异常", ex);
        }
    }

    private void RunSwitchFlow(MainViewModel vm, int segmentIdx, DateTime occurrence, DateTime deadline)
    {
        var ctx = AppContext.Instance;
        var segment = ctx.Schedule.Segments[segmentIdx];

        if (!ctx.Settings.OsdEnabled)
        {
            _ = ApplySegmentAsync(segmentIdx);
            return;
        }

        _currentOsd?.Close();
        var path = segment.ThemeId;
        _currentOsd = CreateOsd(vm, path, deadline,
            onDone: () =>
            {
                _currentOsd = null;
                var fullscreen = ctx.Settings.DeferWhenFullscreen && FullscreenDetector.IsFullscreen();
                var previousManual = ctx.Engine.Manual;
                var decision = DecideSwitch(ctx, fullscreen, ctx.ReadCurrentTheme());
                if (previousManual != ctx.Engine.Manual)
                {
                    ctx.Save();
                    ctx.RaiseStateChanged();
                }
                if (decision.HasAction && decision.SegmentIndex == segmentIdx &&
                    ReferenceEquals(ctx.Schedule.Segments[segmentIdx], segment) && segment.ThemeId == path)
                    _ = ApplySegmentAsync(segmentIdx);
            },
            onSkip: () =>
            {
                _currentOsd = null;
                _skipSegmentStart = occurrence;
                LogService.Info("本次切换已按用户要求跳过");
            });
        _currentOsd.Show();
    }

    private OsdWindow CreateOsd(MainViewModel vm, string path, DateTime deadline, Action onDone, Action onSkip)
    {
        var theme = vm.GetPreviewTheme(path);
        var window = new OsdWindow(theme?.Name ?? vm.GetThemeName(path), theme?.WallpaperPath, deadline, onDone, onSkip);
        if (AppContext.Instance.Settings.ThemeMode == "system" && theme?.PreviewUsesLightTheme is bool light)
            window.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        return window;
    }

    private TickDecision DecideSwitch(AppContext ctx, bool fullscreen, string actualTheme)
    {
        // Windows 可能只应用了部分外观。仅对失败主题保留重试，新的时段照常触发。
        var decision = ctx.Engine.Decide(fullscreen, string.Equals(actualTheme, _failedTheme,
            StringComparison.OrdinalIgnoreCase) ? null : actualTheme);
        if (decision.HasAction && ctx.Engine.Now < _retryAfter &&
            string.Equals(ctx.Schedule.Segments[decision.SegmentIndex].ThemeId, _failedTheme, StringComparison.OrdinalIgnoreCase))
            return TickDecision.None();
        return decision;
    }

    private async Task ApplySegmentAsync(int segmentIdx)
    {
        var ctx = AppContext.Instance;
        if (ctx.Engine.Paused || ctx.IsApplyingTheme) return;
        var path = ctx.Schedule.Segments[segmentIdx].ThemeId;
        if (string.IsNullOrEmpty(path))
        {
            LogService.Warn($"段 {segmentIdx} 未绑定主题，跳过应用");
            _failedTheme = path;
            _retryAfter = ctx.Engine.Now.AddSeconds(30);
            return;
        }
        ctx.IsApplyingTheme = true;
        ctx.RaiseStateChanged();
        try
        {
            var ok = await Task.Run(() => ctx.ApplyService.Apply(path));
            if (!ok)
            {
                _failedTheme = path;
                _retryAfter = ctx.Engine.Now.AddSeconds(30);
                ToastWindow.Show("整套主题未能完整应用，30 秒后重试；详情见运行日志");
                return;
            }
            _failedTheme = null;
            _skipSegmentStart = null;
            LogService.Info($"主题已切换：{path}");
            UpdateTrayToolTip();
            ctx.Save();
            ctx.RaiseStateChanged();
            if (ctx.Settings.NotifyAfterSwitch) ShowSwitchedTheme(path);
        }
        finally
        {
            ctx.IsApplyingTheme = false;
            ctx.ReadCurrentTheme();
            ctx.RaiseStateChanged();
        }
    }

    /// <summary>启动/唤醒/复位时的立即对齐：直接应用当前段主题，不走 OSD；后台执行防 UI 未响应。</summary>
    private async void AlignNow()
    {
        var ctx = AppContext.Instance;
        if (ctx.Engine.Paused || ctx.IsApplyingTheme) return;
        // 全屏时同样延迟对齐（启动/唤醒期间正在看全屏视频不该被切）
        if (ctx.Settings.DeferWhenFullscreen && FullscreenDetector.IsFullscreen())
        {
            LogService.Info("启动/唤醒对齐已按全屏延迟，等退出全屏后由下一轮检查补切");
            return;
        }
        var decision = DecideSwitch(ctx, false, ctx.ReadCurrentTheme());
        if (!decision.HasAction) return;
        _currentOsd?.Close();
        _currentOsd = null;
        await ApplySegmentAsync(decision.SegmentIndex);
    }

    private static void ShowSwitchedTheme(string path)
    {
        var name = ThemeCatalogService.GetDisplayName(path);
        ToastWindow.Show(string.IsNullOrWhiteSpace(name) ? "主题已切换" : $"已切换到「{name}」");
    }

    private void UpdateTrayToolTip()
    {
        if (_trayIcon is null) return;
        if (AppContext.Instance.Engine.Paused)
        {
            _trayIcon.ToolTipText = "晨昏线（已暂停）";
            return;
        }
        var (when, _) = AppContext.Instance.Engine.GetNextSwitchInfo();
        _trayIcon.ToolTipText = $"晨昏线\n{when:HH:mm} 下一次切换";
    }

    // ---------- 托盘 ----------

    private void SetupTray(IClassicDesktopStyleApplicationLifetime desktop, MainViewModel vm)
    {
        var openItem = new NativeMenuItem("打开主界面");
        openItem.Click += (_, _) => ShowMainWindow(desktop);

        _pauseMenuItem = new NativeMenuItem("暂停调度");
        _pauseMenuItem.Click += (_, _) => TogglePause();

        var skipItem = new NativeMenuItem("跳过本次切换");
        skipItem.Click += (_, _) => _currentOsd?.Skip();

        var switchNextItem = new NativeMenuItem("手动切换到下一时段主题");
        switchNextItem.Click += (_, _) => Dispatcher.UIThread.Post(() => vm.ManualSwitchToNextSegment());

        var exitItem = new NativeMenuItem("退出");
        exitItem.Click += (_, _) =>
        {
            ShowMainWindow(desktop);
            if (desktop.MainWindow is MainWindow window) window.RequestExit();
        };

        var menu = new NativeMenu();
        menu.Add(openItem);
        menu.Add(_pauseMenuItem);
        menu.Add(skipItem);
        menu.Add(switchNextItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exitItem);

        using var iconStream = AssetLoader.Open(new Uri("avares://DayNight/Assets/daynight.ico"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "晨昏线",
            Menu = menu,
            IsVisible = true,
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow(desktop);
        var icons = new TrayIcons();
        icons.Add(_trayIcon);
        TrayIcon.SetIcons(this, icons);
    }

    private void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var win = desktop.MainWindow;
        if (win is null) return;
        win.Show();
        win.WindowState = WindowState.Normal;
        win.Activate();
    }

    private void TogglePause()
    {
        var ctx = AppContext.Instance;
        ctx.SetAutoSwitchEnabled(ctx.Engine.Paused);
        if (_pauseMenuItem is not null)
        {
            _pauseMenuItem.Header = ctx.Engine.Paused ? "恢复自动切换" : "暂停调度";
        }
        if (_trayIcon is not null)
        {
            _trayIcon.ToolTipText = ctx.Engine.Paused ? "晨昏线（已暂停）" : "晨昏线";
        }
        LogService.Info(ctx.Engine.Paused ? "调度已暂停（托盘）" : "调度已恢复（托盘）");
        Dispatcher.UIThread.Post(() => ctx.RaiseStateChanged());
    }

    private void CleanupTray()
    {
        if (_trayIcon is null) return;
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
    }
}
