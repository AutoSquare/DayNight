using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace DayNight.Views;

/// <summary>
/// 切换前的屏幕居中提示卡：无边框、半透明、置顶、鼠标点击穿透。
/// 倒计时结束触发 onDone（执行切换）；用户可通过托盘菜单"跳过本次切换"取消。
/// </summary>
public partial class OsdWindow : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLongW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLongW(IntPtr hWnd, int nIndex, int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint key, byte alpha, uint flags);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint LWA_ALPHA = 0x2;

    private readonly Action _onDone;
    private readonly Action _onSkip;
    private DispatcherTimer? _timer;
    private readonly DateTime _deadline;
    private readonly double _totalSeconds;
    private bool _finished;
    private bool _opened;

    /// <summary>创建以当前时刻起算的提示卡，用于补切与预览。</summary>
    public OsdWindow(string themeName, string? wallpaper, int seconds, Action onDone, Action onSkip)
        : this(themeName, wallpaper, DateTime.Now.AddSeconds(Math.Max(1, seconds)), onDone, onSkip)
    {
    }

    /// <summary>创建在指定绝对时刻结束的切换提示卡。</summary>
    public OsdWindow(string themeName, string? wallpaper, DateTime deadline, Action onDone, Action onSkip)
    {
        InitializeComponent();
        Card.SizeChanged += (_, e) => UpdateOutline(e.NewSize);
        // SizeToContent 在打开后完成最终测量，需随实际尺寸重新定位，避免按系统默认高度居中。
        SizeChanged += (_, _) => { if (_opened) PositionCard(); };
        ScalingChanged += (_, _) => { if (_opened) PositionCard(); };
        _onDone = onDone;
        _onSkip = onSkip;
        _deadline = deadline;
        _totalSeconds = Math.Max(0.001, (deadline - DateTime.Now).TotalSeconds);

        // 直接赋值控件（OSD 内容生命周期短，无需 MVVM 通知）
        ThemeNameText.Text = themeName;
        CountText.Text = $"{Math.Max(1, (int)Math.Ceiling(_totalSeconds))} 秒后自动切换";
        Progress.Value = 100;
        if (!string.IsNullOrEmpty(wallpaper) && File.Exists(wallpaper))
        {
            try
            {
                using var fs = File.OpenRead(wallpaper);
                ThumbImage.Source = Bitmap.DecodeToWidth(fs, 480);
                ThumbImage.IsVisible = true;
            }
            catch
            {
                ThumbImage.IsVisible = false;
            }
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _opened = true;
        PositionCard();

        // 鼠标点击穿透：WS_EX_LAYERED 必须随后设置 alpha，否则整个窗口不显示（Win32 规则）
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            var style = GetWindowLongW(handle, GWL_EXSTYLE);
            SetWindowLongW(handle, GWL_EXSTYLE, style | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            SetLayeredWindowAttributes(handle, 0, 255, LWA_ALPHA);
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) => UpdateCountdown();
        _timer.Start();
        UpdateCountdown();
    }

    private void UpdateOutline(Size size)
    {
        var bounds = new Rect(size);
        var inset = Card.Padding.Left;
        if (size.Width <= 2 * inset || size.Height <= 2 * inset) return;
        var radius = Card.CornerRadius.TopLeft;
        // 内外弧同半径：直边仍为 1 DIP，弧线在 45° 处多覆盖约 0.4 DIP。
        Outline.Data = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(bounds, radius, radius),
            new RectangleGeometry(bounds.Deflate(inset), radius, radius));
    }

    private void PositionCard()
    {
        // 工作区坐标为物理像素，窗口尺寸为 DIP；按目标屏幕缩放换算。
        var screen = Screens.Primary ?? Screens.All[0];
        var wa = screen.WorkingArea;
        var scale = screen.Scaling;
        Position = new PixelPoint(
            wa.X + (int)Math.Round((wa.Width - Bounds.Width * scale) / 2),
            wa.Y + (int)Math.Round(wa.Height * .44 - Bounds.Height * scale / 2));
        Services.LogService.Info($"[OSD] 布局：缩放 {scale}，位置 {Position}，尺寸 {Bounds.Width}x{Bounds.Height} DIP，截止 {_deadline:HH:mm:ss.fff}");
    }

    private void UpdateCountdown()
    {
        var remaining = (_deadline - DateTime.Now).TotalSeconds;
        if (remaining <= 0)
        {
            Finish();
            return;
        }
        CountText.Text = $"{(int)Math.Ceiling(remaining)} 秒后自动切换";
        Progress.Value = Math.Clamp(remaining / _totalSeconds * 100, 0, 100);
    }

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        _timer?.Stop();
        Close();
        _onDone();
    }

    /// <summary>托盘"跳过本次切换"：关闭提示，不执行切换。</summary>
    public void Skip()
    {
        if (_finished) return;
        _finished = true;
        _timer?.Stop();
        Close();
        _onSkip();
    }

    protected override void OnClosed(EventArgs e)
    {
        _finished = true;
        _timer?.Stop();
        base.OnClosed(e);
    }
}
