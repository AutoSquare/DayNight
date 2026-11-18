using Avalonia.Controls;
using Avalonia.Interactivity;
using DayNight.ViewModels;

namespace DayNight.Views;

public partial class RulesPageView : UserControl
{
    private RulesPageViewModel? _viewModel;

    /// <summary>连接规则编辑事件与全天预览色带。</summary>
    public RulesPageView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindPreview();
        AttachedToVisualTree += (_, _) => BindPreview();
        DetachedFromVisualTree += (_, _) => UnbindPreview();
    }

    private void BindPreview()
    {
        UnbindPreview();
        _viewModel = DataContext as RulesPageViewModel;
        if (_viewModel is null) return;
        _viewModel.BandRefreshRequested += RebuildPreview;
        MiniBand.ViewModel = _viewModel;
        _viewModel.PropertyChanged += OnRuleStateChanged;
        RebuildPreview();
    }

    private void UnbindPreview()
    {
        if (_viewModel is null) return;
        _viewModel.BandRefreshRequested -= RebuildPreview;
        MiniBand.ViewModel = null;
        _viewModel.PropertyChanged -= OnRuleStateChanged;
        _viewModel = null;
    }

    private void OnRuleStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RulesPageViewModel.AutoSwitchOn)) RebuildPreview();
    }

    private void RebuildPreview() => MiniBand.InvalidateVisual();

    private void SelectTheme(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RuleItemViewModel row } || e.Source != sender ||
            _viewModel is not { IsEditing: false } rules || TopLevel.GetTopLevel(this) is not MainWindow owner) return;

        var segment = rules.DraftPlan.Segments[row.SegmentIndex];
        var hold = row.IsEnded || row.IsNextDay;
        owner.ConfirmTheme(row.ThemeName, row.ThemeInfoText,
            rules.DescribeSwitch(row.SegmentIndex, hold), () =>
            {
                if (!rules.IsEditing && rules.DraftPlan.Segments.ElementAtOrDefault(row.SegmentIndex) == segment)
                    rules.SwitchTheme(row.SegmentIndex, hold);
            });
    }

    private void OpenClock(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RuleItemViewModel row } button || e.Source != button || !row.IsEditing) return;

        var dial = new ClockDial(row.StartTimeSpan);
        var cancel = new Button { Content = "取消" };
        var accept = new Button { Content = "确定" };
        accept.Classes.Add("primary");
        accept.Classes.Add("dn-btn");
        cancel.Classes.Add("dn-btn");
        var actions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, accept }
        };
        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 8,
                Children = { new TextBlock { Text = "内圈选小时 · 外圈选分钟" }, dial, actions }
            }
        };
        cancel.Click += (_, click) =>
        {
            click.Handled = true;
            flyout.Hide();
        };
        accept.Click += (_, click) =>
        {
            click.Handled = true;
            row.SelectedTime = dial.Time;
            flyout.Hide();
        };
        flyout.Opened += (_, _) => dial.Focus();
        flyout.ShowAt(button);
    }

    private void ShowHelp(object? sender, RoutedEventArgs e)
    {
        HelpDialogWindow.Show(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("no window"), "时段规则说明",
            ("编辑与保存", "先点“编辑计划”，再拨动表盘或拖动分布条分界线。所有规则修改先加入草稿，点击“保存计划”后统一生效；“取消编辑”放弃草稿并返回浏览。"),
            ("覆盖全天", "每个开始时间都可以调整。时段按时间排序，最后一段持续至次日第一个开始时间。"),
            ("手动切换", "浏览时点击主题名称、缩略图或其右侧空白处，确认后应用该主题。"),
            ("复位与开关", "「复位自动调度」撤销手动调整，按当前时刻恢复自动。「自动切换」总开关关闭后暂停定时切换，手动切换不受影响。"));
    }
}
