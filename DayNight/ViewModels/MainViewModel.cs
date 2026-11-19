using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DayNight.Models;
using DayNight.Services;

namespace DayNight.ViewModels;

/// <summary>主窗口 ViewModel：导航、调度编排（手动切换/复位/暂停）、跨页刷新。</summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly AppContext _ctx;
    private Dictionary<string, ThemeInfo> _themeById = new();

    public TimeAxisPageViewModel TimeAxis { get; }

    /// <summary>侧栏显示的产品名称与构建版本。</summary>
    public string ProductVersionText => $"晨昏线 DayNight v{typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)}";
    public RulesPageViewModel Rules { get; }
    public ThemeLibraryPageViewModel Library { get; }
    public SettingsPageViewModel Settings { get; }

    /// <summary>导航选中索引（ListBox 双向绑定，0–3）。</summary>
    [ObservableProperty]
    private int _currentPageIndex;

    /// <summary>当前页 ViewModel：ContentControl + ViewLocator 按类型解析对应页面视图。</summary>
    public object CurrentPage => CurrentPageIndex switch
    {
        1 => Rules,
        2 => Library,
        3 => Settings,
        _ => TimeAxis,
    };

    partial void OnCurrentPageIndexChanged(int value) => OnPropertyChanged(nameof(CurrentPage));

    [ObservableProperty]
    private string _footStateText = "调度运行中";

    public MainViewModel(AppContext ctx)
    {
        _ctx = ctx;
        TimeAxis = new TimeAxisPageViewModel(ctx, this);
        Rules = new RulesPageViewModel(ctx, this);
        Library = new ThemeLibraryPageViewModel(ctx, this);
        Settings = new SettingsPageViewModel(ctx);

        ctx.StateChanged += RefreshAll;
        _ = LoadThemesAsync();   // 主题扫描放后台线程，避免卡 UI
        RefreshAll();

        // 每分钟刷新时间轴指针与状态
        DispatcherTimer.Run(() =>
        {
            TimeAxis.Refresh();
            return true;
        }, TimeSpan.FromSeconds(60));
    }

    /// <summary>规则页"自动切换"开关 / 托盘共用：翻转暂停状态。</summary>
    public void TogglePause()
    {
        _ctx.SetAutoSwitchEnabled(_ctx.Engine.Paused);
    }

    public void SelectPage(int index) => CurrentPageIndex = index;

    /// <summary>后台扫描主题库，完成后回 UI 线程刷新（扫描系统目录 + 预览解码较慢，不能占 UI 线程）。</summary>
    private async Task LoadThemesAsync()
    {
        var snapshot = await Task.Run(() =>
        {
            var themes = _ctx.Catalog.Scan();
            var previews = themes.ToDictionary(t => t.FilePath, ThemeLibraryPageViewModel.LoadPreview, StringComparer.OrdinalIgnoreCase);
            return (Themes: themes, Previews: previews);
        });
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            var themes = snapshot.Themes;
            _ctx.ReadCurrentTheme();
            _themeById = themes.ToDictionary(
                t => ThemeCatalogService.NormalizePath(t.FilePath), StringComparer.OrdinalIgnoreCase);
            Library.LoadThemes(themes, snapshot.Previews);
            Rules.OnThemesLoaded(themes);
            RefreshAll();
        });
    }

    public void RefreshThemes()
    {
        _ = LoadThemesAsync();
    }

    public string GetThemeName(string themeId)
    {
        if (_themeById.TryGetValue(ThemeCatalogService.NormalizePath(themeId), out var t)) return t.Name;
        var name = System.IO.Path.GetFileNameWithoutExtension(themeId);
        return string.IsNullOrEmpty(name) ? "未命名主题" : name;
    }

    /// <summary>查找目标主题的预览信息。</summary>
    /// <param name="themeId">计划绑定的主题路径。</param>
    /// <returns>已加载的主题信息，尚未载入时为空。</returns>
    public ThemeInfo? GetPreviewTheme(string themeId)
    {
        return _themeById.GetValueOrDefault(ThemeCatalogService.NormalizePath(themeId));
    }

    /// <summary>复用主题库已解码的缩略图，避免时间轴刷新时重复读取壁纸。</summary>
    /// <param name="themeId">主题文件路径。</param>
    /// <param name="fallbackIndex">无预览时的色带索引。</param>
    /// <returns>壁纸画刷或按主题明暗选取的占位渐变。</returns>
    public IBrush GetThemePreview(string themeId, int fallbackIndex)
    {
        var item = Library?.ThemeItems.FirstOrDefault(t => string.Equals(
            ThemeCatalogService.NormalizePath(t.FilePath), ThemeCatalogService.NormalizePath(themeId), StringComparison.OrdinalIgnoreCase));
        return item?.Thumb is IImageBrushSource image
            ? new ImageBrush(image) { Stretch = Stretch.UniformToFill }
            : ThemeBrush.ForGradient(fallbackIndex, GetPreviewTheme(themeId)?.PreviewUsesLightTheme);
    }

    /// <summary>规则页行点击 → 手动切换（含保持到明天）；调用前 UI 层已弹确认。</summary>
    public async void ManualSwitchSegment(int segmentIndex, bool hold)
    {
        var segment = _ctx.Schedule.Segments[segmentIndex];
        if (!await ApplyThemeDirect(segment.ThemeId, manual: true)) return;
        var actualIndex = _ctx.Schedule.Segments.IndexOf(segment);
        if (actualIndex >= 0) _ctx.Engine.ClickSegment(actualIndex, hold);
        _ctx.Save();
        _ctx.RaiseStateChanged();
    }

    /// <summary>托盘"手动切换到下一时段主题"。</summary>
    public void ManualSwitchToNextSegment()
    {
        var idx = _ctx.Engine.NowSegmentIndex();
        var next = (idx + 1) % _ctx.Schedule.Segments.Count;
        ManualSwitchSegment(next, hold: false);
    }

    public void ResetSchedule()
    {
        _ctx.ResetSchedule();
    }

    public async void ApplySegment(int idx)
    {
        await ApplySegmentAsync(idx);
    }

    /// <summary>后台应用整套主题，成功验证后刷新状态。</summary>
    private async Task ApplySegmentAsync(int idx)
    {
        var path = _ctx.Schedule.Segments[idx].ThemeId;
        await ApplyThemeDirect(path, manual: true);
    }

    /// <summary>直接应用某主题文件（主题库点击 / 规则页手动切换共用）；后台执行防 UI 卡顿。</summary>
    /// <param name="path">主题文件路径。</param>
    /// <param name="manual">是否为用户手动操作。</param>
    /// <returns>整套主题已应用并通过状态验证时为 true。</returns>
    public async Task<bool> ApplyThemeDirect(string path, bool manual)
    {
        if (string.IsNullOrEmpty(path) || _ctx.IsApplyingTheme) return false;
        _ctx.IsApplyingTheme = true;
        _ctx.RaiseStateChanged();
        FootStateText = "正在应用整套主题…";
        try
        {
            var ok = await Task.Run(() => _ctx.ApplyService.Apply(path));
            if (!ok)
            {
                DayNight.Views.ToastWindow.Show("整套主题未能完整应用，请查看运行日志");
                return false;
            }
            LogService.Info(manual ? $"主题已切换（手动）：{path}" : $"主题已切换：{path}");
            _ctx.Save();
            RefreshAll();
            _ = LoadThemesAsync(); // 刷新「当前使用」标记
            return true;
        }
        finally
        {
            _ctx.IsApplyingTheme = false;
            _ctx.ReadCurrentTheme();
            RefreshAll();
        }
    }

    private void RefreshAll()
    {
        FootStateText = _ctx.IsApplyingTheme ? "正在应用整套主题…" : _ctx.Engine.Paused ? "调度已暂停"
            : _ctx.Engine.Manual?.IsHold == true ? "手动保持中"
            : _ctx.IsFullscreenDeferred ? "全屏中，切换已延迟" : "调度运行中";
        TimeAxis.Refresh();
        Rules.Refresh();
        foreach (var item in Library.ThemeItems)
            item.IsCurrent = string.Equals(ThemeCatalogService.NormalizePath(item.FilePath), _ctx.CurrentThemeId, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>今日时间轴页。</summary>
public partial class TimeAxisPageViewModel : ViewModelBase
{
    private readonly AppContext _ctx;
    private readonly MainViewModel _main;

    [ObservableProperty] private string _currentThemeName = "—";
    [ObservableProperty] private string _currentSpan = "";
    [ObservableProperty] private string _pillText = "调度运行中";
    [ObservableProperty] private bool _pillIsHold;
    [ObservableProperty] private string _nextCaption = "下次切换";
    [ObservableProperty] private string _nextTime = "";
    [ObservableProperty] private string _nextDetail = "";
    [ObservableProperty] private string _footText = "调度运行中";
    [ObservableProperty] private double _cursorPercent;
    [ObservableProperty] private IBrush _currentThumbBrush = Brushes.Gray;
    [ObservableProperty] private double _timelineOpacity = 1;
    [ObservableProperty] private System.Collections.ObjectModel.ObservableCollection<ColorBandItem> _bandItems = new();

    /// <summary>连接共享主题库和调度状态。</summary>
    /// <param name="ctx">应用上下文。</param>
    /// <param name="main">提供名称与预览的主视图模型。</param>
    public TimeAxisPageViewModel(AppContext ctx, MainViewModel main)
    {
        _ctx = ctx;
        _main = main;
        Refresh();
    }

    public void Refresh()
    {
        var engine = _ctx.Engine;
        TimelineOpacity = engine.Paused ? 0.55 : 1;
        var now = engine.Now;
        var currentId = _ctx.CurrentThemeId;
        var views = engine.GetSegmentViews(currentId);
        var currentRow = views.FirstOrDefault(v => v.Mark == SegmentMark.Current);
        var scheduledIndex = engine.Manual?.HeldSegmentIndex ?? engine.NowSegmentIndex();
        var segments = _ctx.Schedule.Segments;
        CurrentThemeName = currentId.Length == 0 ? "未能读取系统主题" : ResolveThemeName(currentId);
        CurrentThumbBrush = _main.GetThemePreview(currentId, currentRow?.Index ?? 0);
        CurrentSpan = currentId.Length == 0 ? "" : "当前主题未列入计划";
        if (currentRow is { Index: var index })
        {
            var end = _ctx.Schedule.GetSegmentEndTime(index);
            var endText = (index == segments.Count - 1 ? "次日 " : "") + end.ToString("HH:mm");
            CurrentSpan = $"时段 {segments[index].StartTime:HH\\:mm} – {endText}";
        }

        var hold = engine.Manual?.IsHold == true;
        PillText = engine.Paused ? "调度已暂停" : hold ? "手动保持中"
            : _ctx.IsFullscreenDeferred ? "全屏中，切换已延迟" : "调度运行中";
        PillIsHold = hold || engine.Paused;
        FootText = PillText;

        var (when, _) = engine.GetNextSwitchInfo();
        if (engine.Paused)
        {
            NextCaption = "自动切换已暂停";
            NextTime = "—";
            NextDetail = "恢复后按当前时刻对齐";
        }
        else if (hold)
        {
            NextCaption = "恢复自动";
            NextTime = when.ToString("HH:mm") + (when.Date > now.Date ? "（明天）" : "");
            NextDetail = $"保持「{CurrentThemeName}」至 {NextTime}，之后回到日程";
        }
        else
        {
            var nextIdx = (scheduledIndex + 1) % segments.Count;
            var nextName = ResolveThemeName(segments[nextIdx].ThemeId);
            NextCaption = "下次切换";
            NextTime = when.ToString("HH:mm") + (when.Date > now.Date ? "（明天）" : "");
            NextDetail = nextName == CurrentThemeName
                ? $"{LeftMinutes(when)}后进入下一时段（主题保持 {nextName}）"
                : $"{LeftMinutes(when)}后切换到 {nextName}";
        }

        BandItems.Clear();
        var total = TimeSpan.FromHours(24).TotalMinutes;
        var minute = now.TimeOfDay.TotalMinutes;
        foreach (var (i, start, endMinute) in _ctx.Schedule.GetDaySlices())
        {
            var mark = views[i].Mark;
            var light = _main.GetPreviewTheme(segments[i].ThemeId)?.PreviewUsesLightTheme;
            // 午夜前后两块属于同一条规则，但代表不同日期开始的两次时段。
            if (i == segments.Count - 1 && segments[0].StartTime != TimeOnly.MinValue)
            {
                var morning = start == 0;
                var beforeFirst = minute < segments[0].StartTime.ToTimeSpan().TotalMinutes;
                if (morning != beforeFirst)
                    mark = morning ? SegmentMark.Ended : views[i].IsHold ? SegmentMark.NextDay : SegmentMark.Upcoming;
            }
            BandItems.Add(new ColorBandItem
            {
                WidthPercent = (endMinute - start) / total * 100,
                Light = light,
                Fill = ScheduleGradient.ForSlice(_ctx.Schedule, i, light, start, endMinute, segments[i].ThemeId),
                TextBrush = ThemeBrush.TextOn(light),
                ThemeName = ResolveThemeName(segments[i].ThemeId),
                TimeText = $"{segments[i].StartTime:HH:mm} – {(i == segments.Count - 1 ? "次日 " : "")}{_ctx.Schedule.GetSegmentEndTime(i):HH:mm}",
                MarkText = mark switch { SegmentMark.Current => "当前", SegmentMark.NextDay => "下一日", _ => "" },
                ShowMark = mark is SegmentMark.Current or SegmentMark.NextDay,
                IsCurrent = mark == SegmentMark.Current,
                IsHoldDim = views[i].IsHold && mark == SegmentMark.NextDay,
            });
        }
        CursorPercent = (now.TimeOfDay.TotalMinutes / total) * 100.0;
    }

    private static string LeftMinutes(DateTime when)
    {
        var left = when - DateTime.Now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        return $"{(int)left.TotalHours} 小时 {left.Minutes} 分钟";
    }

    private string ResolveThemeName(string themeId)
    {
        return _main.GetThemeName(themeId);
    }
}

/// <summary>时间轴色带的一段。</summary>
public partial class ColorBandItem : ViewModelBase
{
    /// <summary>当前可见区间对应的主题渐变。</summary>
    public required IBrush Fill { get; init; }

    /// <summary>绑定主题声明的明暗，同时决定色块与图标。</summary>
    public bool? Light { get; init; }
    [ObservableProperty] private double _widthPercent;
    [ObservableProperty] private IBrush _textBrush = Brushes.White;
    [ObservableProperty] private string _themeName = "";
    [ObservableProperty] private string _timeText = "";
    [ObservableProperty] private string _markText = "";
    [ObservableProperty] private bool _showMark;
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _isHoldDim;

}

/// <summary>按索引取预置渐变（壁纸预览缺失时的兜底视觉）：已知主题明暗时取对应明暗族，未知或未绑定回退中性族。</summary>
public static class ThemeBrush
{
    private static readonly IBrush TextOnLightBrush = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B));

    public static IBrush ForGradient(int index, bool? light = null)
    {
        var key = light switch
        {
            true => "BandGradLight" + Math.Abs(index) % 3,
            false => "BandGradDark" + Math.Abs(index) % 3,
            _ => "BandGrad" + Math.Abs(index) % 6,
        };
        if (Application.Current?.Resources.TryGetResource(key, null, out var value) == true
            && value is IBrush brush) return brush;
        return Brushes.Gray;
    }

    /// <summary>渐变段上的标签颜色：亮渐变配深色文字，暗渐变与兜底配白色。</summary>
    public static IBrush TextOn(bool? light) => light == true ? TextOnLightBrush : Brushes.White;
}

/// <summary>主题库页的一项。</summary>
public partial class ThemeItemViewModel : ViewModelBase
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private string? _wallpaperPath;
    [ObservableProperty] private IBrush _thumbBrush = Brushes.Gray;
    [ObservableProperty] private Avalonia.Media.IImage? _thumb;
    [ObservableProperty] private bool _hasThumb;
    [ObservableProperty] private int _gradientIndex;

    public string FilePath { get; init; } = "";

    /// <summary>宿主页注入的应用回调。</summary>
    internal Action<string>? ApplyRequested;

    public void ApplyNow()
    {
        if (!string.IsNullOrEmpty(FilePath)) ApplyRequested?.Invoke(FilePath);
    }
}

/// <summary>主题库页。</summary>
public partial class ThemeLibraryPageViewModel : ViewModelBase
{
    private readonly AppContext _ctx;
    private readonly MainViewModel _main;

    [ObservableProperty] private System.Collections.ObjectModel.ObservableCollection<ThemeItemViewModel> _themeItems = new();
    [ObservableProperty] private string _countText = "";

    public ThemeLibraryPageViewModel(AppContext ctx, MainViewModel main)
    {
        _ctx = ctx;
        _main = main;
        // 主题列表由 MainViewModel 后台扫描后注入（LoadThemes），构造时不扫描
    }

    public void ApplyThemeNow(string filePath) => _ = _main.ApplyThemeDirect(filePath, manual: true);

    /// <summary>展示后台扫描和解码完成的主题快照。</summary>
    /// <param name="themes">主题元数据。</param>
    /// <param name="previews">按文件路径索引的预览图。</param>
    public void LoadThemes(List<ThemeInfo> themes, IReadOnlyDictionary<string, Avalonia.Media.IImage?> previews)
    {
        ThemeItems.Clear();
        foreach (var t in themes)
        {
            var thumb = previews[t.FilePath];
            var item = new ThemeItemViewModel
            {
                Name = t.Name,
                CountText = t.WallpaperDescription,
                IsCurrent = t.IsCurrent,
                WallpaperPath = t.WallpaperPath,
                Thumb = thumb,
                HasThumb = thumb is not null,
                ThumbBrush = ThemeBrush.ForGradient(Math.Abs(t.Name.GetHashCode()), t.PreviewUsesLightTheme),
                FilePath = t.FilePath,
            };
            item.ApplyRequested = ApplyThemeNow;
            ThemeItems.Add(item);
        }
        CountText = $"共 {themes.Count} 套，来自 Windows 已保存和系统内置主题";
    }

    internal static Avalonia.Media.IImage? LoadPreview(ThemeInfo t)
    {
        try
        {
            if (t.WallpaperPath is null || !File.Exists(t.WallpaperPath)) return null;
            using var fs = File.OpenRead(t.WallpaperPath);
            return Avalonia.Media.Imaging.Bitmap.DecodeToWidth(fs, 480);
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    private void Rescan() => _main.RefreshThemes();
}

/// <summary>设置页。</summary>
public partial class SettingsPageViewModel : ViewModelBase
{
    private readonly AppContext _ctx;

    public SettingsPageViewModel(AppContext ctx)
    {
        _ctx = ctx;
        var s = ctx.Settings;
        _isSystemTheme = s.ThemeMode == "system";
        _isLightTheme = s.ThemeMode == "light";
        _isDarkTheme = s.ThemeMode == "dark";
        // 自启开关显示注册表真实状态：登记项必须指向本程序才显示“开”
        _autostart = AutostartService.IsEnabled();
        var autostartHolder = AutostartService.ForeignHolderPath();
        _autostartNote = autostartHolder is null ? "" : $"自启项正被另一个副本占用（{autostartHolder}），重新打开开关可接管";
        _silentStart = s.SilentStart;
        _minimizeToTray = s.MinimizeToTray;
        _deferWhenFullscreen = s.DeferWhenFullscreen;
        _alignOnStartAndResume = s.AlignOnStartAndResume;
        _osdEnabled = s.OsdEnabled;
        _osdSeconds = s.OsdSeconds;
        _notifyAfterSwitch = s.NotifyAfterSwitch;
    }

    [ObservableProperty] private bool _isSystemTheme;
    [ObservableProperty] private bool _isLightTheme;
    [ObservableProperty] private bool _isDarkTheme;
    [ObservableProperty] private bool _autostart;
    // 自启项被其他程序副本占用时的提示文案；空串不占位
    [ObservableProperty] private string _autostartNote = "";
    [ObservableProperty] private bool _silentStart;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _deferWhenFullscreen;
    [ObservableProperty] private bool _alignOnStartAndResume;
    [ObservableProperty] private bool _osdEnabled;
    [ObservableProperty] private int _osdSeconds;
    [ObservableProperty] private bool _notifyAfterSwitch;

    partial void OnIsSystemThemeChanged(bool value) { if (value) SetThemeMode("system"); }
    partial void OnIsLightThemeChanged(bool value) { if (value) SetThemeMode("light"); }
    partial void OnIsDarkThemeChanged(bool value) { if (value) SetThemeMode("dark"); }

    /// <summary>三段式选择按钮组命令（字符串参数，经 RelayCommand&lt;string&gt; 安全转换）。</summary>
    [RelayCommand]
    private void SelectTheme(string mode) => SetThemeMode(mode);

    private void SetThemeMode(string mode)
    {
        _ctx.Settings.ThemeMode = mode;
        IsSystemTheme = mode == "system";
        IsLightTheme = mode == "light";
        IsDarkTheme = mode == "dark";
        Application.Current!.RequestedThemeVariant = mode switch
        {
            "light" => Avalonia.Styling.ThemeVariant.Light,
            "dark" => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default,
        };
        _ctx.Save();
    }

    partial void OnAutostartChanged(bool value)
    {
        _ctx.Settings.Autostart = value;
        // 显式操作：开 = 接管登记项（覆盖旧值），关 = 仅删除指向本程序的项
        if (value) AutostartService.Enable(); else AutostartService.Disable();
        _ctx.Save();
        if (value) AutostartNote = "";
    }

    partial void OnSilentStartChanged(bool value) { _ctx.Settings.SilentStart = value; _ctx.Save(); }
    partial void OnMinimizeToTrayChanged(bool value) { _ctx.Settings.MinimizeToTray = value; _ctx.Save(); }
    partial void OnDeferWhenFullscreenChanged(bool value) { _ctx.Settings.DeferWhenFullscreen = value; _ctx.Save(); }
    partial void OnAlignOnStartAndResumeChanged(bool value) { _ctx.Settings.AlignOnStartAndResume = value; _ctx.Save(); }
    partial void OnOsdEnabledChanged(bool value) { _ctx.Settings.OsdEnabled = value; _ctx.Save(); }
    partial void OnOsdSecondsChanged(int value) { _ctx.Settings.OsdSeconds = value; _ctx.Save(); }
    partial void OnNotifyAfterSwitchChanged(bool value) { _ctx.Settings.NotifyAfterSwitch = value; _ctx.Save(); }

    [RelayCommand]
    private void OpenLogs()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DayNight", "logs");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogService.Error("打开日志目录失败", ex);
        }
    }
}

