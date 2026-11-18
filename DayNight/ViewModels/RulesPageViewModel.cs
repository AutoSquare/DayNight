using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DayNight.Models;
using DayNight.Services;

namespace DayNight.ViewModels;

/// <summary>规则页：管理编辑草稿、保存与撤销，不让编辑过程影响有效调度。</summary>
public partial class RulesPageViewModel : ViewModelBase
{
    private readonly AppContext _ctx;
    private readonly MainViewModel _main;
    private readonly ScheduleDraft _draft;
    private List<ThemeInfo> _themes = new();
    private bool _suppressAutoSwitch;

    /// <summary>稳定的规则行集合，刷新状态时不移除正在编辑的控件。</summary>
    public ObservableCollection<RuleItemViewModel> RuleItems { get; } = new();

    /// <summary>编辑中的日程，仅由本页读取。</summary>
    public ScheduleConfig DraftPlan => _draft.Plan;

    /// <summary>草稿时间或主题变化后通知分布条刷新。</summary>
    public event Action? BandRefreshRequested;

    [ObservableProperty] private bool _autoSwitchOn;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _hasPendingChanges;
    [ObservableProperty] private string _validateText = "计划已保存，正在按此计划调度";
    [ObservableProperty] private string _scheduleStatusText = "";
    [ObservableProperty] private bool _hasManualHold;

    /// <summary>基于有效配置创建独立编辑会话。</summary>
    public RulesPageViewModel(AppContext ctx, MainViewModel main)
    {
        _ctx = ctx;
        _main = main;
        _draft = new ScheduleDraft(ctx.Schedule);
        Refresh();
    }

    [RelayCommand]
    private void EditPlan() => IsEditing = true;

    partial void OnIsEditingChanged(bool value) => Refresh();

    partial void OnAutoSwitchOnChanged(bool value)
    {
        if (!_suppressAutoSwitch) _ctx.SetAutoSwitchEnabled(value);
    }

    /// <summary>更新主题选项并保留草稿。</summary>
    public void OnThemesLoaded(List<ThemeInfo> themes)
    {
        _themes = themes;
        Refresh();
    }

    /// <summary>刷新有效状态；不会用有效配置覆盖尚未保存的编辑。</summary>
    public void Refresh()
    {
        _suppressAutoSwitch = true;
        AutoSwitchOn = !_ctx.Engine.Paused;
        _suppressAutoSwitch = false;
        HasPendingChanges = _draft.IsDirty;
        var views = _ctx.Engine.GetSegmentViews(_ctx.CurrentThemeId);
        while (RuleItems.Count > DraftPlan.Segments.Count) RuleItems.RemoveAt(RuleItems.Count - 1);
        while (RuleItems.Count < DraftPlan.Segments.Count) RuleItems.Add(new RuleItemViewModel(this, RuleItems.Count));
        for (var i = 0; i < RuleItems.Count; i++)
        {
            var segment = DraftPlan.Segments[i];
            var info = _themes.FirstOrDefault(t => string.Equals(t.FilePath, segment.ThemeId, StringComparison.OrdinalIgnoreCase));
            RuleItems[i].Update(segment, _themes, _main.GetThemeName(segment.ThemeId),
                info?.WallpaperDescription ?? "系统主题", _main.GetThemePreview(segment.ThemeId, i),
                !HasPendingChanges ? views[i].Mark : SegmentMark.Upcoming);
        }
        ValidateText = HasPendingChanges ? "有未保存的修改 · 保存后生效" : "计划已保存";
        HasManualHold = _ctx.Engine.Manual?.IsHold == true;
        ScheduleStatusText = _ctx.Engine.Manual?.HoldUntil is { } until
            ? $"手动保持至 {until:MM-dd HH:mm}；复位可撤销保持。编辑草稿不影响当前运行计划。" : "";
        BandRefreshRequested?.Invoke();
    }

    /// <summary>提交选择器的时间到草稿，无效值保留原时间并提示。</summary>
    public void SetTime(int index, TimeSpan? selected)
    {
        if (!IsEditing) return;
        if (selected is not { } value || value < TimeSpan.Zero || value >= TimeSpan.FromDays(1) ||
            !DraftPlan.TrySetStartTime(index, TimeOnly.FromTimeSpan(value)))
        {
            Refresh();
            ValidateText = "请选择未被其他时段占用的整分钟";
            return;
        }
        Refresh();
    }

    /// <summary>将主题选择写入草稿。</summary>
    public void SetTheme(int index, string path)
    {
        if (!IsEditing) return;
        DraftPlan.Segments[index].ThemeId = path;
        Refresh();
    }

    /// <summary>删除草稿的一段，模型维持全天覆盖。</summary>
    public void DeleteSegment(int index)
    {
        if (IsEditing && DraftPlan.TryRemoveSegment(index)) Refresh();
    }

    /// <summary>调整草稿内的分布条边界，返回当前时间供拖动提示使用。</summary>
    public string MoveBoundary(int index, double fraction)
    {
        if (IsEditing && _draft.MoveBoundary(index, fraction)) Refresh();
        return DraftPlan.Segments[index].StartTime.ToString("HH:mm");
    }

    /// <summary>为手动切换确认框说明下一次恢复调度的时间。</summary>
    public string DescribeSwitch(int index, bool hold)
    {
        var now = _ctx.Engine.Now;
        var until = _ctx.Engine.Manual?.HoldUntil;
        if (until > now) return $"保持该主题至 {until:MM-dd HH:mm}。";
        if (hold) return $"保持该主题至 {_ctx.Schedule.GetSegmentEndDateTime(index, now.AddDays(1)):MM-dd HH:mm}。";
        return "应用该时段的整套 Windows 主题。";
    }

    /// <summary>在浏览模式应用已保存时段，复用整套主题切换与保持逻辑。</summary>
    public void SwitchTheme(int index, bool hold)
    {
        if (!IsEditing) _main.ManualSwitchSegment(index, hold);
    }

    /// <summary>显式保存整个草稿；落盘失败保留草稿与原有效日程。</summary>
    public bool SaveChanges(bool align = true)
    {
        try
        {
            if (!HasPendingChanges) return true;
            if (!_ctx.TrySaveSchedule(_draft.CreateSnapshot(), align))
            {
                ValidateText = "保存失败，原计划继续运行；请重试或查看日志";
                return false;
            }
            _draft.Discard();
            IsEditing = false;
            ValidateText = "计划已保存并生效";
            return true;
        }
        catch (InvalidOperationException ex)
        {
            ValidateText = ex.Message;
            return false;
        }
    }

    [RelayCommand]
    private void SavePlan() => SaveChanges();

    [RelayCommand]
    private void DiscardChanges()
    {
        _draft.Discard();
        IsEditing = false;
    }

    [RelayCommand]
    private void AddSegment()
    {
        if (!IsEditing) return;
        if (DraftPlan.TryAddSegment()) Refresh();
        else ValidateText = "最后一段不足两分钟，无法继续拆分";
    }

    [RelayCommand]
    private void ResetSchedule() => _ctx.ResetSchedule();
}

/// <summary>规则草稿的一行；时间选择确认后只修改草稿。</summary>
public partial class RuleItemViewModel : ViewModelBase
{
    private readonly RulesPageViewModel _owner;
    private bool _suppress;

    /// <summary>该行在草稿中的索引。</summary>
    public int SegmentIndex { get; }

    /// <summary>绑定到所属草稿页。</summary>
    public RuleItemViewModel(RulesPageViewModel owner, int index) { _owner = owner; SegmentIndex = index; }

    [ObservableProperty] private TimeSpan _startTimeSpan;
    [ObservableProperty] private TimeSpan? _selectedTime;
    [ObservableProperty] private string _startTimeText = "";
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _themeName = "";
    [ObservableProperty] private string _themeInfoText = "";
    [ObservableProperty] private string _coverText = "";
    [ObservableProperty] private string _markText = "";
    [ObservableProperty] private bool _showMark;
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _isEnded;
    [ObservableProperty] private bool _isNextDay;
    [ObservableProperty] private IBrush _thumbBrush = Brushes.Gray;
    [ObservableProperty] private IReadOnlyList<ThemeInfo> _themes = Array.Empty<ThemeInfo>();
    [ObservableProperty] private ThemeInfo? _selectedTheme;

    partial void OnSelectedTimeChanged(TimeSpan? value) { if (!_suppress) _owner.SetTime(SegmentIndex, value); }
    partial void OnSelectedThemeChanged(ThemeInfo? value) { if (!_suppress && value is not null) _owner.SetTheme(SegmentIndex, value.FilePath); }

    /// <summary>就地刷新绑定，防止定时更新关闭正在操作的选择器。</summary>
    public void Update(ThemeSegment segment, IReadOnlyList<ThemeInfo> themes, string name, string info, IBrush preview, SegmentMark mark)
    {
        _suppress = true;
        StartTimeSpan = segment.StartTime.ToTimeSpan();
        SelectedTime = StartTimeSpan;
        StartTimeText = segment.StartTime.ToString("HH:mm");
        IsEditing = _owner.IsEditing;
        Themes = themes;
        SelectedTheme = themes.FirstOrDefault(t => string.Equals(t.FilePath, segment.ThemeId, StringComparison.OrdinalIgnoreCase));
        ThemeName = name;
        ThemeInfoText = info;
        ThumbBrush = preview;
        CoverText = SegmentIndex + 1 < _owner.DraftPlan.Segments.Count
            ? $"至 {_owner.DraftPlan.Segments[SegmentIndex + 1].StartTime:HH:mm}" : $"至次日 {_owner.DraftPlan.Segments[0].StartTime:HH:mm}";
        MarkText = mark switch { SegmentMark.Current => "当前", SegmentMark.Ended => "已结束", SegmentMark.NextDay => "下一日", _ => "" };
        ShowMark = mark != SegmentMark.Upcoming;
        IsCurrent = mark == SegmentMark.Current;
        IsEnded = mark == SegmentMark.Ended;
        IsNextDay = mark == SegmentMark.NextDay;
        _suppress = false;
    }

    [RelayCommand]
    private void Delete() => _owner.DeleteSegment(SegmentIndex);
}
