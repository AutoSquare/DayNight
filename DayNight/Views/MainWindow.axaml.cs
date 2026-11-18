using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DayNight.Services;

namespace DayNight.Views;

public partial class MainWindow : Window
{
    private Avalonia.Media.Imaging.Bitmap? _titleIcon;
    private bool _allowClose;
    private bool _closingPromptOpen;
    private Action? _confirmSwitch;
    private Control? _previousFocus;

    /// <summary>在主窗口中央显示切换确认卡片，确认后才应用主题。</summary>
    internal void ConfirmTheme(string name, string details, string note, Action onConfirm)
    {
        if (SwitchOverlay.IsVisible) return;
        _previousFocus = FocusManager?.GetFocusedElement() as Control;
        ConfirmThemeName.Text = name;
        ConfirmDetails.Text = details;
        ConfirmNote.Text = note;
        _confirmSwitch = onConfirm;
        SwitchOverlay.IsVisible = true;
        CancelSwitch.Focus();
    }

    private void CloseSwitchCard()
    {
        _confirmSwitch = null;
        SwitchOverlay.IsVisible = false;
        _previousFocus?.Focus();
        _previousFocus = null;
    }

    private void AcceptSwitch(object? sender, RoutedEventArgs e)
    {
        var apply = _confirmSwitch;
        CloseSwitchCard();
        apply?.Invoke();
    }

    private void DismissSwitch(object? sender, RoutedEventArgs e) => CloseSwitchCard();

    private void DismissBackdrop(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        CloseSwitchCard();
    }

    private void ConfirmKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        CloseSwitchCard();
    }

    /// <summary>创建主窗口并连接托盘最小化行为。</summary>
    public MainWindow()
    {
        InitializeComponent();
        // 最小化到托盘：WindowState 变化走 Avalonia PropertyChanged（Resized 在最小化时不保证触发）
        PropertyChanged += OnWindowStateChanged;
        Opened += (_, _) => UpdateTitleIcon();
        ScalingChanged += (_, _) => UpdateTitleIcon();
        Closing += OnWindowClosing;
        Closed += (_, _) => _titleIcon?.Dispose();
    }

    private void UpdateTitleIcon()
    {
        var previous = _titleIcon;
        _titleIcon = AppIconService.CreateTitleBarBitmap(RenderScaling);
        TitleBarLogo.Source = _titleIcon;
        previous?.Dispose();
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose || e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown
            || DataContext is not DayNight.ViewModels.MainViewModel vm) return;
        if (!_closingPromptOpen && !vm.Settings.MinimizeToTray && !vm.Rules.HasPendingChanges) return;
        e.Cancel = true;
        if (_closingPromptOpen) return;
        if (vm.Settings.MinimizeToTray)
        {
            _closingPromptOpen = true;
            CloseChoice choice;
            try { choice = await Dialogs.ChooseCloseAction(this); }
            finally { _closingPromptOpen = false; }
            if (choice == CloseChoice.MinimizeToTray) Hide();
            if (choice != CloseChoice.Exit) return;
        }
        RequestExit();
    }

    /// <summary>处理明确的退出请求，退出前让用户处理未保存的计划。</summary>
    internal void RequestExit()
    {
        if (_closingPromptOpen) return;
        if (DataContext is not DayNight.ViewModels.MainViewModel vm || !vm.Rules.HasPendingChanges)
        {
            ExitApplication();
            return;
        }
        _closingPromptOpen = true;
        Dialogs.UnsavedPlan(this,
            save: () => { if (vm.Rules.SaveChanges(align: false)) ExitApplication(); },
            discard: ExitApplication,
            closed: () => _closingPromptOpen = false);
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
    }

    private void TitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Avalonia.Visual source && source.GetVisualAncestors().OfType<Button>().Any()) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void TitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Avalonia.Visual source && source.GetVisualAncestors().OfType<Button>().Any()) return;
        ToggleMaximize();
    }

    private void MinimizeWindow(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeWindow(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseWindow(object? sender, RoutedEventArgs e) => Close();

    private void ShowHelp(object? sender, RoutedEventArgs e) => HelpDialogWindow.Show(this, "使用说明",
        ("今日时间轴", "查看当前主题、全天安排和下一次切换时间。"),
        ("时段规则", "设置时段与主题；关闭自动切换后，重启和唤醒仍保持关闭。"),
        ("主题库", "读取 Windows 已保存的主题，点击卡片可以手动应用。"),
        ("后台运行", "开启“最小化到托盘”后，点关闭可选择留在托盘或退出程序。"));

    private void OnWindowStateChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty
            && WindowState == WindowState.Minimized
            && AppContext.Instance?.Settings.MinimizeToTray == true)
        {
            Hide(); // 驻托盘后台运行
        }
    }
}
