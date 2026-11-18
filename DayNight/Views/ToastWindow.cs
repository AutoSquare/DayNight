using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DayNight.Views;

/// <summary>
/// 右下角气泡通知：无边框小卡，短暂停留后自动关闭。
/// 替代系统托盘气泡（Avalonia TrayIcon 无通知 API），样式与原型一致。
/// </summary>
public sealed class ToastWindow : Window
{
    public ToastWindow(string message)
    {
        var card = new Border
        {
            Background = BrushHelper.CardSolidBrush,
            BorderBrush = BrushHelper.AccentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new Ellipse
                    {
                        Width = 8, Height = 8,
                        Fill = BrushHelper.SunBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = message,
                        FontSize = 13,
                        Foreground = BrushHelper.TextBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };

        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        TransparencyBackgroundFallback = BrushHelper.CardSolidBrush;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        Content = card;
    }

    /// <summary>在主屏右下角显示并自动关闭。</summary>
    public void ShowAndAutoClose(int milliseconds = 2600)
    {
        Opened += (_, _) =>
        {
            try
            {
                var screen = Screens.ScreenFromWindow(this) ?? Screens.All[0];
                var wa = screen.WorkingArea;
                Position = new PixelPoint(
                    wa.X + wa.Width - (int)Width - 24,
                    wa.Y + wa.Height - (int)Height - 24);
            }
            catch
            {
                // 定位失败不致命
            }
        };
        Show();
        DispatcherTimer.RunOnce(() => Close(), TimeSpan.FromMilliseconds(milliseconds));
    }

    /// <summary>便捷入口：UI 线程弹出气泡。</summary>
    public static void Show(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                new ToastWindow(message).ShowAndAutoClose();
            }
            catch (Exception ex)
            {
                DayNight.Services.LogService.Warn($"气泡通知失败：{ex.Message}");
            }
        });
    }
}
