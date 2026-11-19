using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using DayNight.Services;
using DayNight.ViewModels;
using DayNight.Views;

namespace DayNight;

public partial class App
{
    private void RunUiDiagnostics(IClassicDesktopStyleApplicationLifetime desktop, MainViewModel mainVm, AppContext ctx)
    {
        // UI 自测：--uitest osd → 弹 OSD；--uitest dark → 2 秒后切暗色；默认弹帮助窗
        if (Program.UITestFullscreen)
        {
            var fsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            fsTimer.Tick += (_, _) =>
            {
                fsTimer.Stop();
                var fullscreen = FullscreenDetector.IsFullscreen();
                LogService.Info($"[uitest] 全屏检测 IsFullscreen={fullscreen}（普通桌面应为 False）");
                var decision = ctx.Engine.Decide(fullscreen, ctx.ReadCurrentTheme());
                LogService.Info($"[uitest] 全屏决策 HasAction={decision.HasAction}");
            };
            fsTimer.Start();
        }
        else if (Program.UITestHold)
        {
            var holdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            holdTimer.Tick += (_, _) =>
            {
                holdTimer.Stop();
                var views = ctx.Engine.GetSegmentViews(ctx.ReadCurrentTheme());
                var ended = views.FirstOrDefault(v => v.Mark == Services.SegmentMark.Ended);
                if (ended is null)
                {
                    LogService.Error("[uitest] 当前没有已结束时段");
                    return;
                }
                LogService.Info($"[uitest] 点击已结束段 {ended.Index}（hold=true）");
                mainVm.ManualSwitchSegment(ended.Index, hold: true);
                var manual = ctx.Engine.Manual;
                LogService.Info($"[uitest] 手动状态 段={manual?.HeldSegmentIndex} 保持到={manual?.HoldUntil:yyyy-MM-dd HH:mm} IsHold={manual?.IsHold}");
                var after = ctx.Engine.GetSegmentViews(ctx.ReadCurrentTheme());
                LogService.Info("[uitest] 四态：" + string.Join(", ", after.Select(v => $"段{v.Index}={v.Mark}")));
                mainVm.SelectPage(0);
            };
            holdTimer.Start();

            var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
            resetTimer.Tick += (_, _) =>
            {
                resetTimer.Stop();
                mainVm.ResetSchedule();
                LogService.Info($"[uitest] 已复位 手动状态={ctx.Engine.Manual?.HeldSegmentIndex.ToString() ?? "null"}");
            };
            resetTimer.Start();
        }
        else if (Program.UITestTray)
        {
            var trayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            trayTimer.Tick += (_, _) =>
            {
                trayTimer.Stop();
                LogService.Info($"[uitest] 暂停前 Paused={ctx.Engine.Paused}");
                mainVm.TogglePause();
                LogService.Info($"[uitest] 点「暂停调度」后 Paused={ctx.Engine.Paused}");
                var beforeIdx = ctx.Engine.NowSegmentIndex();
                mainVm.ManualSwitchToNextSegment();
                LogService.Info($"[uitest] 点「手动切换到下一时段」：从段{beforeIdx} → 手动段={ctx.Engine.Manual?.HeldSegmentIndex}");
                mainVm.TogglePause();
                LogService.Info($"[uitest] 点「恢复自动切换」后 Paused={ctx.Engine.Paused}");
            };
            trayTimer.Start();
        }
        else if (Program.UITestMinTray)
        {
            var minTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            minTimer.Tick += (_, _) =>
            {
                minTimer.Stop();
                desktop.MainWindow!.WindowState = WindowState.Minimized; // 触发 OnResized→Hide 进托盘
                LogService.Info("[uitest] 已触发最小化");
            };
            minTimer.Start();
        }
        else if (Program.UITestShow)
        {
            // 需配合 --silent + SilentStart=true：先静默驻托盘，再走托盘同款 ShowMainWindow。
            var showTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            showTimer.Tick += (_, _) =>
            {
                showTimer.Stop();
                LogService.Info("[uitest] 模拟托盘「打开主界面」");
                ShowMainWindow(desktop);
            };
            showTimer.Start();

            var checkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            checkTimer.Tick += (_, _) =>
            {
                checkTimer.Stop();
                var visible = desktop.MainWindow?.IsVisible == true;
                LogService.Info($"[uitest] 打开后 IsVisible={visible}（预期 True；False 表示窗口被静默订阅再次隐藏）");
            };
            checkTimer.Start();

            var exitTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            exitTimer.Tick += (_, _) =>
            {
                exitTimer.Stop();
                LogService.Info("[uitest] 验证结束，自动退出");
                desktop.Shutdown();
            };
            exitTimer.Start();
        }
        else if (Program.UITestToast)
        {
            var toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            toastTimer.Tick += (_, _) =>
            {
                toastTimer.Stop();
                ShowSwitchedTheme(ctx.ReadCurrentTheme());
                LogService.Info("[uitest] Toast 已弹出");
            };
            toastTimer.Start();
        }
        else if (Program.UITestDark)
        {
            var darkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            darkTimer.Tick += (_, _) =>
            {
                darkTimer.Stop();
                mainVm.Settings.IsDarkTheme = true; // 与三段按钮同一状态切换路径
                LogService.Info("[uitest] 已切暗色主题");
            };
            darkTimer.Start();
        }
        else if (Program.UITestOsd)
        {
            var osdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            osdTimer.Tick += (_, _) =>
            {
                osdTimer.Stop();
                var idx = ctx.Engine.NowSegmentIndex();
                var seg = ctx.Schedule.Segments[idx];
                _currentOsd = CreateOsd(mainVm, seg.ThemeId, DateTime.Now.AddSeconds(4),
                    onDone: () =>
                    {
                        _currentOsd = null;
                        LogService.Info("[uitest] OSD 倒计时完成回调已触发");
                        ToastWindow.Show("[uitest] OSD 完成，气泡正常");
                    },
                    onSkip: () =>
                    {
                        _currentOsd = null;
                        LogService.Info("[uitest] OSD 跳过回调已触发");
                    });
                _currentOsd.Show();
            };
            osdTimer.Start();
        }
        else if (Program.UITest)
        {
            Window? helpShell = null;
            var openTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            openTimer.Tick += (_, _) =>
            {
                openTimer.Stop();
                helpShell = Dialogs.Help(desktop.MainWindow!, "今日时间轴说明",
                    ("当前主题卡", "显示正在生效的主题与时段；右侧是下次切换时刻，或手动保持的恢复时刻。"),
                    ("全天时段分布", "今天的切换计划色带，橙色指针是当前时刻。"),
                    ("手动保持中", "表示处于手动选择的主题，到恢复时刻会自动回到日程。"));
            };
            openTimer.Start();

            var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            closeTimer.Tick += (_, _) =>
            {
                closeTimer.Stop();
                helpShell?.Close();
            };
            closeTimer.Start();
        }
    }
}
