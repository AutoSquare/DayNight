using DayNight.Models;

namespace DayNight.Services;

/// <summary>应用级门面：配置、日程、引擎与各服务的单例装配，页面 ViewModel 均从这里取状态。</summary>
public sealed class AppContext
{
    public static AppContext Instance { get; private set; } = null!;

    public AppSettings Settings { get; private set; }
    public ScheduleConfig Schedule { get; private set; }
    public ScheduleEngine Engine { get; private set; }
    public ThemeCatalogService Catalog { get; } = new();
    public ThemeApplyService ApplyService { get; } = new();

    /// <summary>最近一次从 Windows 读取的主题路径。读取不到时为空。</summary>
    public string CurrentThemeId { get; private set; } = string.Empty;

    /// <summary>状态变化（切换/手动/复位/暂停）后触发，各页面据此刷新。</summary>
    public event Action? StateChanged;

    /// <summary>规则编辑或复位后请求取消旧倒计时并立即重新对齐。</summary>
    public event Action? ScheduleChanged;

    /// <summary>整套主题应用期间占用调度通道，避免自动与手动任务重复入队。</summary>
    public bool IsApplyingTheme { get; set; }

    /// <summary>自动应用当前是否因全屏或演示模式延迟，供界面显示真实状态。</summary>
    public bool IsFullscreenDeferred { get; set; }

    /// <summary>保存新计划或复位结果，撤销旧手动状态，并通知调度入口。</summary>
    public void ResetSchedule()
    {
        Engine.ResetManual();
        Save();
        RaiseStateChanged();
        ScheduleChanged?.Invoke();
    }

    /// <summary>先将草稿快照写入配置，成功后一次性替换有效日程并撤销旧手动状态。</summary>
    public bool TrySaveSchedule(ScheduleConfig snapshot, bool align = true)
    {
        if (!snapshot.Validate() || snapshot.Segments.Any(s => string.IsNullOrWhiteSpace(s.ThemeId))) return false;
        if (!ConfigStore.Save(new ConfigStore.Config { Settings = Settings, Schedule = snapshot, Manual = null })) return false;
        Schedule.Segments = snapshot.Segments.Select(s => new ThemeSegment(s.StartTime, s.ThemeId)).ToList();
        Engine.ResetManual();
        RaiseStateChanged();
        if (align) ScheduleChanged?.Invoke();
        return true;
    }

    private AppContext(AppSettings settings, ScheduleConfig schedule, ManualState? manual)
    {
        Settings = settings;
        Schedule = schedule;
        Engine = new ScheduleEngine(schedule, manual) { Paused = !settings.AutoSwitchEnabled };
        ReadCurrentTheme();
    }

    public static AppContext Load()
    {
        var config = ConfigStore.Load();
        Instance = new AppContext(config.Settings, config.Schedule, config.Manual);

        // 首次运行（无配置文件）：建默认五段日程，全部绑当前系统主题，开箱即有完整视图
        var firstRun = !File.Exists(ConfigStore.ConfigPath);
        if (firstRun && !string.IsNullOrEmpty(Instance.CurrentThemeId))
        {
            var theme = Instance.CurrentThemeId;
            Instance.Schedule.Segments = new List<Models.ThemeSegment>
            {
                new(new TimeOnly(0, 0), theme),
                new(new TimeOnly(6, 30), theme),
                new(new TimeOnly(9, 0), theme),
                new(new TimeOnly(17, 30), theme),
                new(new TimeOnly(20, 0), theme),
            };
            Instance.Save();
            LogService.Info($"首次运行：已创建默认五段日程并绑定当前主题 {theme}");
        }
        return Instance;
    }

    public void Save()
    {
        Settings.AutoSwitchEnabled = !Engine.Paused;
        ConfigStore.Save(new ConfigStore.Config
        {
            Settings = Settings,
            Schedule = Schedule,
            Manual = Engine.Manual,
        });
    }

    /// <summary>统一保存页面与托盘的总开关状态，再通知界面和调度器。</summary>
    /// <param name="enabled">是否允许自动切换。</param>
    public void SetAutoSwitchEnabled(bool enabled)
    {
        Engine.Paused = !enabled;
        Save();
        RaiseStateChanged();
    }

    /// <summary>从系统读取实际主题，供本次调度判断与界面共用。</summary>
    /// <returns>规范化的当前主题路径。</returns>
    public string ReadCurrentTheme()
    {
        CurrentThemeId = Catalog.GetNormalizedCurrentThemePath();
        return CurrentThemeId;
    }

    /// <summary>触发 StateChanged（在 UI 线程上调用方自行调度）。</summary>
    public void RaiseStateChanged() => StateChanged?.Invoke();
}
