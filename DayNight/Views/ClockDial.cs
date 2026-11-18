using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace DayNight.Views;

/// <summary>开始时间选择表盘，独立暂存小时和分钟，供弹层确认时读取。</summary>
internal sealed class ClockDial : Control
{
    private const double HourRadius = 77;
    private const double MinuteRadius = 116;
    private const double DialSize = 264;
    private static readonly Point Center = new(DialSize / 2, DialSize / 2);
    private static readonly Typeface Face = new("Microsoft YaHei UI");
    private bool _editingHours;
    private IPointer? _pointer;

    /// <summary>两圈当前选定的时间。</summary>
    public TimeSpan Time { get; private set; }

    /// <summary>从当前草稿的开始时刻初始化表盘。</summary>
    public ClockDial(TimeSpan initial)
    {
        Time = initial;
        Width = Height = DialSize;
        Focusable = true;
        ToolTip.SetTip(this, "上下键调小时，左右键调分钟");
        RefreshSelection();
    }

    private static Point OnCircle(double radius, int step, int count)
    {
        var angle = step * Math.Tau / count;
        return new Point(Center.X + radius * Math.Sin(angle), Center.Y - radius * Math.Cos(angle));
    }

    /// <summary>绘制两个刻度圈、选中位置和完整时间。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        DrawRing(context, MinuteRadius, 60, 5, Time.Minutes, BrushHelper.Text2Brush);
        DrawRing(context, HourRadius, 24, 3, Time.Hours, BrushHelper.AccentBrush);
        Label(context, Time.ToString(@"hh\:mm"), Center, 22, BrushHelper.TextBrush);
    }

    private static void DrawRing(DrawingContext context, double radius, int count, int labelEvery,
        int selected, IBrush ink)
    {
        context.DrawEllipse(null, new Pen(BrushHelper.StrokeBrush, 1), Center, radius, radius);
        for (var i = 0; i < count; i++)
        {
            var point = OnCircle(radius, i, count);
            if (i == selected) continue;
            if (i % labelEvery == 0)
            {
                // 紧邻选中标记的小刻度保留为点，避免数字相叠。
                var distance = Math.Min(Math.Abs(i - selected), count - Math.Abs(i - selected));
                if (distance * Math.Tau * radius / count >= 25)
                {
                    context.DrawEllipse(BrushHelper.CardSolidBrush, null, point, 11, 11);
                    Label(context, i.ToString("00"), point, 12, ink);
                    continue;
                }
            }
            context.DrawEllipse(ink, null, point, 1.4, 1.4);
        }
        var handle = OnCircle(radius, selected, count);
        context.DrawEllipse(BrushHelper.AccentBrush, null, handle, 14, 14);
        Label(context, selected.ToString("00"), handle, 12, BrushHelper.CardSolidBrush);
    }

    private static void Label(DrawingContext context, string value, Point at, double fontSize, IBrush ink)
    {
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, fontSize, ink);
        context.DrawText(text, new Point(at.X - text.Width / 2, at.Y - text.Height / 2));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_pointer != null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var position = e.GetPosition(this);
        var distance = Point.Distance(position, Center);
        if (distance < HourRadius - 19 || distance > MinuteRadius + 16) return;
        _editingHours = distance < (HourRadius + MinuteRadius) / 2;
        _pointer = e.Pointer;
        e.Pointer.Capture(this);
        Focus();
        SelectAt(position);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer == _pointer) SelectAt(e.GetPosition(this));
    }

    private void SelectAt(Point position)
    {
        var offset = position - Center;
        if (Point.Distance(position, Center) < 8) return;
        var turns = Math.Atan2(offset.X, -offset.Y) / Math.Tau;
        var count = _editingHours ? 24 : 60;
        var step = ((int)Math.Round(turns * count, MidpointRounding.AwayFromZero) + count) % count;
        Time = _editingHours ? new TimeSpan(step, Time.Minutes, 0) : new TimeSpan(Time.Hours, step, 0);
        RefreshSelection();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pointer != e.Pointer) return;
        SelectAt(e.GetPosition(this));
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_pointer == e.Pointer) _pointer = null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var hours = Time.Hours;
        var minutes = Time.Minutes;
        switch (e.Key)
        {
            case Key.Up: hours = (hours + 1) % 24; break;
            case Key.Down: hours = (hours + 23) % 24; break;
            case Key.Right: minutes = (minutes + 1) % 60; break;
            case Key.Left: minutes = (minutes + 59) % 60; break;
            default: return;
        }
        Time = new TimeSpan(hours, minutes, 0);
        RefreshSelection();
        e.Handled = true;
    }

    private void RefreshSelection()
    {
        AutomationProperties.SetName(this, $"开始时间 {Time:hh\\:mm}，上下键调小时，左右键调分钟");
        InvalidateVisual();
    }
}
