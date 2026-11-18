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

    /// <summary>按百分比生成星号列宽的色带：首段左圆角、末段右圆角、段间 1px 分隔。</summary>
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

        var accent = BrushHelper.AccentBrush;
        var count = vm.BandItems.Count;
        var i = 0;
        foreach (var item in vm.BandItems)
        {
            host.ColumnDefinitions.Add(new ColumnDefinition(
                new GridLength(item.WidthPercent, GridUnitType.Star)));

            var isLast = i == count - 1;
            var radius = (i == 0, isLast) switch
            {
                (true, true) => new CornerRadius(8),
                (true, false) => new CornerRadius(8, 0, 0, 8),
                (false, true) => new CornerRadius(0, 8, 8, 0),
                _ => new CornerRadius(0),
            };

            var panel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(8, 0, 10, 6),
                Spacing = 1,
            };
            panel.Children.Add(new TextBlock
            {
                Text = item.ThemeName,
                Foreground = Brushes.White,
                FontWeight = FontWeight.SemiBold,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            panel.Children.Add(new TextBlock
            {
                Text = item.TimeText,
                Foreground = Brushes.White,
                FontSize = 10,
                Opacity = 0.9,
            });

            var segment = new Border
            {
                Background = item.BandBrush,
                ClipToBounds = true,
                Opacity = item.IsHoldDim ? 0.45 : 1,
                BorderBrush = item.IsCurrent ? accent : null,
                BorderThickness = item.IsCurrent ? new Thickness(2) : default,
                CornerRadius = radius,
                Margin = isLast ? default : new Thickness(0, 0, 1, 0), // 段间分隔
            };
            Grid.SetColumn(segment, i);

            if (item.ShowMark && !string.IsNullOrEmpty(item.MarkText))
            {
                var badge = new Border
                {
                    Background = accent,
                    CornerRadius = new CornerRadius(999),
                    Padding = new Thickness(7, 1),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 6, 8, 0),
                    Child = new TextBlock
                    {
                        Text = item.MarkText,
                        Foreground = Brushes.White,
                        FontSize = 10,
                    },
                };
                segment.Child = new Panel { Children = { panel, badge } };
            }
            else
            {
                segment.Child = panel;
            }

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
                Height = 84,
                Fill = BrushHelper.SunBrush,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };
            _needle = new StackPanel
            {
                Spacing = 0,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                IsHitTestVisible = false,
                Children = { time, line },
            };
            Grid.SetRow(_needle, 0);
            Grid.SetRowSpan(_needle, 2);
            BandWrap.Children.Add(_needle);
        }

        var x = Math.Max(0, Math.Min(width - 2, width * _vm.CursorPercent / 100.0));
        _needle.Margin = new Thickness(x - 17, 0, 0, 0);
    }

    private void ShowHelp(object? sender, RoutedEventArgs e)
    {
        HelpDialogWindow.Show(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("no window"), "今日时间轴说明",
            ("当前主题卡", "显示正在生效的主题与时段；右侧是下次切换时刻，或手动保持的恢复时刻。"),
            ("全天时段分布", "今天的切换计划色带，橙色指针是当前时刻。高亮的一段是当前生效时段；弱化并标「下一日」的段今天被跳过，明天照常执行。"),
            ("手动保持中", "表示处于手动选择的主题，到恢复时刻会自动回到日程。"));
    }
}
