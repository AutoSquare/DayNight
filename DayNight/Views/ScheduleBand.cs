using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DayNight.ViewModels;

namespace DayNight.Views;

/// <summary>全天草稿分布条。指针始终由本控件捕获，刷新草稿不会中断拖动。</summary>
public sealed class ScheduleBand : Control
{
    private int _dragIndex = -1;
    private TimeSpan _originalTime;
    private double _pressX;
    private const double Inset = 5;
    private const double TrackY = 25;
    private const double TrackHeight = 16;

    /// <summary>提供草稿数据和边界编辑入口。</summary>
    public RulesPageViewModel? ViewModel { get; set; }

    /// <summary>启用键盘焦点，支持 Esc 取消本次拖动。</summary>
    public ScheduleBand() { Focusable = true; ClipToBounds = true; }

    private double BarWidth => Math.Max(1, Bounds.Width - Inset * 2);
    private double BoundaryX(int index) => Inset + ViewModel!.RuleItems[index].StartTimeSpan.TotalMinutes / 1440 * BarWidth;

    /// <summary>绘制按真实时间比例分配的色带、拖动手柄和刻度。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (ViewModel is not { } vm) return;
        var rows = vm.RuleItems;
        var track = new Rect(Inset, TrackY, BarWidth, TrackHeight);
        using (context.PushClip(new RoundedRect(track, 5)))
        {
            foreach (var (index, start, end) in vm.DraftPlan.GetDaySlices())
            {
                var width = BarWidth * (end - start) / 1440;
                var x = Inset + BarWidth * start / 1440;
                var gap = start == 0 ? 0 : Math.Min(2, width / 4);
                var slice = new Rect(x + gap, TrackY, width - gap, TrackHeight);
                var themeId = vm.DraftPlan.Segments[index].ThemeId;
                context.DrawRectangle(ScheduleGradient.ForSlice(vm.DraftPlan, index, vm.LightnessOf(index), start, end, themeId), null, slice);
            }
        }
        for (var i = 0; vm.IsEditing && i < rows.Count; i++)
        {
            var x = BoundaryX(i);
            context.DrawRectangle(BrushHelper.CardSolidBrush, new Pen(BrushHelper.AccentBrush, 1.5), new Rect(x - 4, TrackY - 4, 8, TrackHeight + 8), 4, 4);
            context.DrawLine(new Pen(BrushHelper.AccentBrush, 1), new Point(x, TrackY + 4), new Point(x, TrackY + TrackHeight - 4));
            if (_dragIndex == i)
            {
                var labelX = Math.Clamp(x - 27, 0, Math.Max(0, Bounds.Width - 54));
                context.DrawRectangle(BrushHelper.AccentBrush, null, new Rect(labelX, 1, 54, 22), 5, 5);
                DrawText(context, rows[i].StartTimeText, labelX + 8, 3, BrushHelper.CardSolidBrush);
            }
        }
        for (var hour = 0; hour <= 24; hour += 6)
        {
            var x = Inset + BarWidth * hour / 24;
            context.DrawLine(new Pen(BrushHelper.StrokeBrush), new Point(x, 43), new Point(x, 47));
            DrawText(context, $"{hour:00}:00", Math.Clamp(x - 15, 0, Math.Max(0, Bounds.Width - 32)), 50, BrushHelper.Text2Brush);
        }
    }

    private static void DrawText(DrawingContext context, string text, double x, double y, IBrush brush) =>
        context.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), 11, brush), new Point(x, y));

    private int HitBoundary(Point point)
    {
        if (ViewModel is null || point.Y < TrackY - 8 || point.Y > TrackY + TrackHeight + 8) return -1;
        return Enumerable.Range(0, ViewModel.RuleItems.Count)
            .Where(i => Math.Abs(BoundaryX(i) - point.X) <= 12)
            .OrderBy(i => Math.Abs(BoundaryX(i) - point.X)).DefaultIfEmpty(-1).First();
    }

    /// <summary>按下分界手柄后开始一次可取消的草稿拖动。</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragIndex = HitBoundary(e.GetPosition(this));
        if (_dragIndex < 0 || ViewModel is null) return;
        Focus();
        _pressX = e.GetPosition(this).X;
        _originalTime = ViewModel.RuleItems[_dragIndex].StartTimeSpan;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    /// <summary>移动手柄同步预览时间，不保存或应用主题。</summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        Cursor = new Cursor(_dragIndex >= 0 || HitBoundary(point) >= 0 ? StandardCursorType.SizeWestEast : StandardCursorType.Arrow);
        if (_dragIndex < 0 || ViewModel is null) return;
        if (Math.Abs(point.X - _pressX) >= 2)
            ViewModel.MoveBoundary(_dragIndex, (point.X - Inset) / BarWidth);
        e.Handled = true;
    }

    /// <summary>松开仅结束拖动，草稿仍等待用户保存。</summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragIndex < 0) return;
        _dragIndex = -1;
        e.Pointer.Capture(null);
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>捕获丢失时结束拖动，防止后续悬停继续修改。</summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragIndex = -1;
        InvalidateVisual();
    }

    /// <summary>Esc 恢复本次拖动之前的草稿时间。</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || _dragIndex < 0) return;
        ViewModel?.SetTime(_dragIndex, _originalTime);
        _dragIndex = -1;
        InvalidateVisual();
        e.Handled = true;
    }
}
