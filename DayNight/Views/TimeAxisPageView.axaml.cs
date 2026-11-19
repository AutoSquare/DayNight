using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using DayNight.ViewModels;

namespace DayNight.Views;

public partial class TimeAxisPageView : UserControl
{
    private TimeAxisPageViewModel? _vm;
    private StackPanel? _needle;
    private Border? _needleCasing;

    // 指针两侧的 1px 衬边：在暖色黄昏段上，橙色指针与底色亮度太接近，靠衬边才能始终看清。
    private static readonly IBrush NeedleCasingOnLight = new SolidColorBrush(Color.FromArgb(120, 27, 27, 27));
    private static readonly IBrush NeedleCasingOnDark = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));

    public TimeAxisPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChangedHandler;
        DetachedFromVisualTree += (_, _) => Unsubscribe();
        BandWrap.SizeChanged += (_, _) => UpdateNeedle();
    }

    private void OnDataContextChangedHandler(object? sender, EventArgs e)
    {
        Unsubscribe();
        if (DataContext is not TimeAxisPageViewModel vm) return;
        _vm = vm;
        vm.PropertyChanged += OnVmPropertyChanged;
        // 刷新时 BandItems 是 Clear+Add（集合变化），不是属性替换 → 必须订阅 CollectionChanged
        vm.BandItems.CollectionChanged += OnBandCollectionChanged;
        RebuildBand(vm);
        UpdateNeedle();
    }

    private void Unsubscribe()
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.BandItems.CollectionChanged -= OnBandCollectionChanged;
            _vm = null;
        }
    }

    private void OnBandCollectionChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_vm is not null) RebuildBand(_vm);
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_vm is null) return;
        if (e.PropertyName == nameof(TimeAxisPageViewModel.BandItems)) RebuildBand(_vm);
        if (e.PropertyName == nameof(TimeAxisPageViewModel.CursorPercent)) UpdateNeedle();
    }

    /// <summary>按真实时长排列色块，每段使用独立的主题色、分隔线和起止时间。</summary>
    private void RebuildBand(TimeAxisPageViewModel vm)
    {
        var host = BandHost;
        host.ColumnDefinitions.Clear();
        host.Children.Clear();
        if (_needle is not null)
        {
            BandWrap.Children.Remove(_needle);
            _needle = null;   // 重建后重新添加，保持指针在最上层
        }

        var i = 0;
        foreach (var item in vm.BandItems)
        {
            host.ColumnDefinitions.Add(new ColumnDefinition(
                new GridLength(item.WidthPercent, GridUnitType.Star)));

            var title = new TextBlock
            {
                Text = item.ThemeName, Foreground = item.TextBrush, FontWeight = FontWeight.SemiBold,
                FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var range = new TextBlock
            {
                Text = item.TimeText, Foreground = item.TextBrush, FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var labels = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom, Spacing = 1,
                Margin = new Thickness(12, 0, 8, 7), Children = { title, range },
            };
            // Windows 字体中的晴天、晴夜和系统显示图标，在窄段也能辨认主题类型。
            var glyph = new TextBlock
            {
                Text = item.Light switch { true => "\uE706", false => "\uE708", null => "\uE7F4" },
                FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = item.TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var segment = new Panel
            {
                Background = item.Fill, ClipToBounds = true,
                Children = { labels, glyph },
            };
            if (item.IsHoldDim)
                labels.Opacity = 0.72;
            if (i > 0)
            {
                var divider = new Border { HorizontalAlignment = HorizontalAlignment.Left };
                divider.Bind(Border.BackgroundProperty, this.GetResourceObservable("DnCardSolidBrush"));
                segment.Children.Add(divider);
                segment.SizeChanged += (_, e) => divider.Width = Math.Min(2, e.NewSize.Width / 4);
            }
            var badge = new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 7, 8, 0),
                Background = item.Light == true ? new SolidColorBrush(Color.Parse("#334D70")) : new SolidColorBrush(Color.Parse("#DFE8F5")),
                Child = new TextBlock { Text = item.MarkText, FontSize = 10,
                    Foreground = item.Light == true ? Brushes.White : new SolidColorBrush(Color.Parse("#22304B")) },
            };
            if (item.ShowMark) segment.Children.Add(badge);
            var currentDot = new Ellipse { Width = 5, Height = 5, Fill = item.TextBrush,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 4, 0) };
            if (item.IsCurrent) segment.Children.Add(currentDot);

            segment.SizeChanged += (_, e) =>
            {
                var wide = e.NewSize.Width >= 158;
                labels.IsVisible = e.NewSize.Width >= 72;
                range.IsVisible = wide;
                glyph.IsVisible = wide || (e.NewSize.Width < 72 && e.NewSize.Width >= 13);
                glyph.FontSize = wide ? 15 : 11;
                glyph.HorizontalAlignment = wide ? HorizontalAlignment.Left : HorizontalAlignment.Center;
                glyph.VerticalAlignment = wide ? VerticalAlignment.Top : VerticalAlignment.Center;
                glyph.Margin = wide ? new Thickness(12, 7, 0, 0) : default;
                badge.IsVisible = e.NewSize.Width >= 72;
                currentDot.IsVisible = e.NewSize.Width < 72;
            };
            ToolTip.SetTip(segment, $"{item.ThemeName}   {item.TimeText}   {item.MarkText}".TrimEnd());
            Grid.SetColumn(segment, i);

            host.Children.Add(segment);
            i++;
        }
        UpdateNeedle();
    }

    /// <summary>当前时刻指针：太阳橙竖线 + 时间标签，Margin 像素定位（叠在色带上）。</summary>
    private void UpdateNeedle()
    {
        if (_vm is null) return;
        var width = BandWrap.Bounds.Width;
        if (width < 10) return;

        if (_needle is null)
        {
            var time = new TextBlock
            {
                Text = DateTime.Now.ToString("HH:mm"),
                FontSize = 10.5,
                FontWeight = FontWeight.Bold,
                Foreground = BrushHelper.SunBrush,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };
            var line = new Rectangle
            {
                Width = 2,
                Height = 80,
                Fill = BrushHelper.SunBrush,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };
            _needleCasing = new Border
            {
                Background = NeedleCasingOnLight,
                Padding = new Thickness(1, 0, 1, 0),
                CornerRadius = new CornerRadius(1),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Child = line,
            };
            _needle = new StackPanel
            {
                Spacing = 0,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                IsHitTestVisible = false,
                Children = { time, _needleCasing },
            };
            Grid.SetRow(_needle, 0);
            Grid.SetRowSpan(_needle, 2);
            BandWrap.Children.Add(_needle);
        }

        var x = Math.Max(0, Math.Min(width - 2, width * _vm.CursorPercent / 100.0));
        _needle.Margin = new Thickness(x - 17, 0, 0, 0);
        if (_needleCasing is not null)
            _needleCasing.Background = CursorOnDarkSegment(_vm) ? NeedleCasingOnDark : NeedleCasingOnLight;
    }

    /// <summary>判断当前时刻落在深色段还是浅色段，用来决定指针衬边的明暗。</summary>
    private static bool CursorOnDarkSegment(TimeAxisPageViewModel vm)
    {
        var boundary = 0.0;
        foreach (var item in vm.BandItems)
        {
            boundary += item.WidthPercent;
            if (vm.CursorPercent <= boundary) return item.Light == false;
        }
        return false;
    }

    private void ShowHelp(object? sender, RoutedEventArgs e)
    {
        HelpDialogWindow.Show(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("no window"), "今日时间轴说明",
            ("当前主题卡", "显示正在生效的主题与时段；右侧是下次切换时刻，或手动保持的恢复时刻。"),
            ("全天时段分布", "今天的切换计划色带，橙色指针是当前时刻。高亮的一段是当前生效时段；弱化并标「下一日」的段今天被跳过，明天照常执行。"),
            ("手动保持中", "表示处于手动选择的主题，到恢复时刻会自动回到日程。"));
    }
}
