using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace DayNight.Views;

internal enum CloseChoice { Cancel, MinimizeToTray, Exit }

/// <summary>
/// 主窗口的帮助、关闭选择与未保存计划对话框。
/// 纯代码构造，避免为每种弹窗建独立 axaml。
/// </summary>
public static class Dialogs
{
    private static Window BuildShell(string title, out StackPanel bodyHost, out Button confirmButton, out StackPanel foot,
        string confirmText, bool showCancel)
    {
        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(22, 18, 22, 6),
        };

        bodyHost = new StackPanel { Margin = new Thickness(22, 0, 22, 18), Spacing = 8 };

        confirmButton = new Button
        {
            Content = confirmText,
            Classes = { "dn-btn", "primary" },
        };
        foot = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(22, 12),
        };
        Button? cancelButton = showCancel ? new Button { Content = "取消", IsCancel = true, Classes = { "dn-btn" } } : null;
        if (cancelButton is not null) foot.Children.Add(cancelButton);
        foot.Children.Add(confirmButton);

        var card = new Border
        {
            Background = BrushHelper.CardSolidBrush, // 不透明实底（对齐原型 card-solid），透窗上不发黑
            BorderBrush = BrushHelper.StrokeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new StackPanel { Children = { titleBlock, bodyHost, foot } },
        };

        var shell = new Window
        {
            Title = title,
            WindowDecorations = WindowDecorations.None,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            TransparencyBackgroundFallback = BrushHelper.CardBrush, // 透明不可用时退化为卡片底色，绝不发黑
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            CanResize = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Width = 440,
            Topmost = true,
            Content = card, // 窗口即卡片：无边框、无黑底、无遮罩
        };

        if (cancelButton is not null) cancelButton.Click += (_, _) => shell.Close();
        return shell;
    }

    /// <summary>帮助说明窗：加粗小标题 + 正文的分节说明。</summary>
    public static Window Help(Window owner, string title, params (string Head, string Text)[] sections)
    {
        var shell = BuildShell(title, out var body, out var confirmButton, out _, "知道了", showCancel: false);
        confirmButton.Click += (_, _) => shell.Close();
        foreach (var (head, text) in sections)
        {
            body.Children.Add(new TextBlock
            {
                Text = head,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = BrushHelper.TextBrush,
            });
            body.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Foreground = BrushHelper.Text2Brush,
                Margin = new Thickness(0, 2, 0, 10),
            });
        }
        CenterOnOwner(shell, owner);
        shell.Show(owner);
        return shell;
    }

    /// <summary>离开应用时明确处理未保存草稿，保存失败保留主窗口。</summary>
    public static void UnsavedPlan(Window owner, Action save, Action discard, Action closed)
    {
        var shell = BuildShell("计划尚未保存", out var body, out var confirm, out _, "保存并退出", showCancel: true);
        body.Children.Add(new TextBlock { Text = "修改还没有生效。退出前是否保存切换计划？", TextWrapping = TextWrapping.Wrap });
        var discardButton = new Button { Content = "不保存退出", Classes = { "dn-btn" } };
        body.Children.Add(discardButton);
        confirm.Click += (_, _) => { shell.Close(); save(); };
        discardButton.Click += (_, _) => { shell.Close(); discard(); };
        shell.Closed += (_, _) => closed();
        CenterOnOwner(shell, owner);
        shell.ShowDialog(owner);
    }

    internal static Task<CloseChoice> ChooseCloseAction(Window owner)
    {
        var shell = BuildShell("关闭晨昏线", out var body, out var tray, out var foot, "最小化到托盘", showCancel: true);
        body.Children.Add(new TextBlock { Text = "最小化后可继续按计划切换主题。退出程序将停止自动切换。", TextWrapping = TextWrapping.Wrap });
        var exit = new Button { Content = "退出程序", Classes = { "dn-btn" } };
        foot.Children.Insert(1, exit);
        exit.Click += (_, _) => shell.Close(CloseChoice.Exit);
        tray.Click += (_, _) => shell.Close(CloseChoice.MinimizeToTray);
        shell.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        shell.Opened += (_, _) => tray.Focus();
        return shell.ShowDialog<CloseChoice>(owner);
    }

    private static void CenterOnOwner(Window shell, Window owner)
    {
        shell.Opened += (_, _) =>
        {
            try
            {
                var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.All[0];
                var wa = screen.WorkingArea;
                shell.Position = new PixelPoint(
                    wa.X + (int)((wa.Width - shell.Width) / 2),
                    wa.Y + (int)((wa.Height - shell.Height) / 2));
            }
            catch
            {
                // 居中失败不致命
            }
        };
    }
}

public static class HelpDialogWindow
{
    public static void Show(Window owner, string title, params (string Head, string Text)[] sections)
        => Dialogs.Help(owner, title, sections);
}

/// <summary>主题字典 brushes 快捷访问。</summary>
internal static class BrushHelper
{
    public static IBrush CardBrush => Get("DnCardBrush");
    public static IBrush CardSolidBrush => Get("DnCardSolidBrush");
    public static IBrush StrokeBrush => Get("DnStrokeBrush");
    public static IBrush TextBrush => Get("DnTextBrush");
    public static IBrush Text2Brush => Get("DnText2Brush");
    public static IBrush AccentBrush => Get("DnAccentBrush");
    public static IBrush SunBrush => Get("DnSunBrush");

    private static IBrush Get(string key)
    {
        var app = Application.Current;
        if (app is null) return Brushes.Transparent;
        // 必须带主题变体查找，否则 ThemeDictionaries 内的键解析不到（会拿到 Transparent）
        if (app.Resources.TryGetResource(key, app.ActualThemeVariant, out var value) == true && value is IBrush b)
            return b;
        return Brushes.Transparent;
    }
}
