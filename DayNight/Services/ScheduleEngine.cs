using DayNight.Models;

namespace DayNight.Services;

/// <summary>时段在界面上的四种标记。</summary>
public enum SegmentMark
{
    /// <summary>正在生效的主题。</summary>
    Current,
    /// <summary>今天的时段已过去，不会再执行。</summary>
    Ended,
    /// <summary>今天的执行被跳过，下一日照常自动切换。</summary>
    NextDay,
    /// <summary>今天的未来时段，到点自动切换。</summary>
    Upcoming,
}

/// <summary>规则页/时间轴页共用的单段视图状态。</summary>
public sealed record SegmentView(int Index, SegmentMark Mark, bool IsHold);

/// <summary>
/// 调度核心：日程推导、手动覆盖状态机、tick 决策。所有时间均可注入以便单元测试。
/// </summary>
public sealed class ScheduleEngine
{
    /// <summary>
    /// 能把主题变化归因于运行期间手动操作的两次检查最大间隔；超过视为中断（睡眠、休眠）。
    /// </summary>
    public static readonly TimeSpan MaxWatchGap = TimeSpan.FromSeconds(3);

    private readonly ScheduleConfig _config;
    private ManualState? _manual;
    private Func<DateTime> _clock;

    public ScheduleEngine(ScheduleConfig config, ManualState? manual, Func<DateTime>? clock = null)
    {
        _config = config;
        _manual = manual is { HeldSegmentIndex: >= 0 } && manual.HeldSegmentIndex < config.Segments.Count ? manual : null;
        _clock = clock ?? (() => DateTime.Now);
    }

    public ManualState? Manual => _manual;

    /// <summary>调度总开关（规则页"自动切换"开关 / 托盘暂停），暂停期间不切换、不对齐。</summary>
    public bool Paused { get; set; }

    /// <summary>时钟当前时刻。</summary>
    public DateTime Now => _clock();

    /// <summary>当前时刻所在的段索引。</summary>
    public int NowSegmentIndex() => _config.GetSegmentIndexAt(TimeOnly.FromDateTime(Now));

    /// <summary>按指定时刻的手动状态应生效的主题路径。</summary>
    /// <param name="now">判定时刻。</param>
    /// <returns>当前段或手动选中段绑定的主题。</returns>
    public string GetEffectiveThemeId(DateTime now)
    {
        var index = _config.GetSegmentIndexAt(TimeOnly.FromDateTime(now));
        return _config.Segments[_manual?.HeldSegmentIndex ?? index].ThemeId;
    }

    /// <summary>清空手动状态（复位）。</summary>
    public void ResetManual()
    {
        _manual = null;
        LogService.Info("手动状态已复位");
    }

    /// <summary>
    /// 点击某个时段行：返回要写入的手动状态。
    /// hold 为 true 时（点击已结束/下一日行），跳过今天剩余切换、保持到该时段在明天的结束时刻。
    /// </summary>
    public ManualState ClickSegment(int index, bool hold)
    {
        var now = Now;
        // 临时保持只针对上一次手动更换的主题，规则页点行是新的手动选择，不沿用它的到期时刻。
        var previousUntil = _manual is { IsTemporary: false } ? _manual.HoldUntil : null;
        _manual = new ManualState
        {
            HeldSegmentIndex = index,
            HoldUntil = previousUntil > now ? previousUntil : hold ? _config.GetSegmentEndDateTime(index, now.AddDays(1)) : null,
        };
        LogService.Info(hold
            ? $"手动切换并保持：段 {index} 至 {_manual.HoldUntil:yyyy-MM-dd HH:mm}"
            : $"手动切换：段 {index}");
        return _manual;
    }

    /// <summary>
    /// 登记临时保持：只覆盖当前时段，保持到下一个切换点后恢复计划。
    /// 主题库手动应用，以及运行期间检测到外部更换主题时调用。
    /// </summary>
    /// <returns>登记后的手动状态。</returns>
    public ManualState HoldUntilNextSwitch()
    {
        var now = Now;
        var index = _config.GetSegmentIndexAt(TimeOnly.FromDateTime(now));
        _manual = new ManualState
        {
            HeldSegmentIndex = index,
            HoldUntil = _config.GetSegmentEndDateTime(index, _config.GetActiveSegmentStart(index, now)),
            IsTemporary = true,
        };
        LogService.Info($"手动更换主题，保持至 {_manual.HoldUntil:yyyy-MM-dd HH:mm}");
        return _manual;
    }

    /// <summary>
    /// 运行期间实际主题被外部更换（如个性化设置）：视同手动切换，登记到下一个切换点的临时保持。
    /// 启动、唤醒或本程序刚应用过主题时不满足登记条件，其后的差异仍按对齐处理。
    /// </summary>
    /// <param name="previousTheme">上一次读取到的主题路径；为空表示没有可比对的读数。</param>
    /// <param name="actualTheme">本次读取到的主题路径。</param>
    /// <param name="sinceLastCheck">距上一次检查经过的时间。</param>
    /// <returns>已登记临时保持时为 true。</returns>
    public bool NoteExternalChange(string? previousTheme, string? actualTheme, TimeSpan sinceLastCheck)
    {
        // 上一次读数之后程序没有持续运行（启动、睡眠、休眠），其间的变化不能归因于用户的即时操作。
        if (string.IsNullOrEmpty(previousTheme) || string.IsNullOrEmpty(actualTheme)) return false;
        if (sinceLastCheck < TimeSpan.Zero || sinceLastCheck > MaxWatchGap) return false;
        if (string.Equals(previousTheme, actualTheme, StringComparison.OrdinalIgnoreCase)) return false;
        // 变成计划目标时引擎本就不会切回，登记只会让界面误显示手动保持。
        if (string.Equals(ThemeCatalogService.NormalizePath(actualTheme),
                ThemeCatalogService.NormalizePath(GetEffectiveThemeId(Now)), StringComparison.OrdinalIgnoreCase)) return false;
        // 已在保持中：现有保持覆盖的时间更长，继续沿用；提前生效状态则由临时保持替换。
        if (_manual is { IsHold: true, HoldUntil: { } until } && until > Now) return false;
        HoldUntilNextSwitch();
        return true;
    }

    /// <summary>
    /// 下一主题变化时刻与目标说明：
    /// 无保持 → 当前（或覆盖段）的段尾；保持中 → HoldUntil。
    /// </summary>
    public (DateTime when, string reason) GetNextSwitchInfo()
    {
        var now = Now;
        if (_manual is { } m)
        {
            if (m.HoldUntil is { } until) return (until, "恢复自动");
            // Override 模式：被点段的段尾（跨过其起始时刻后自然清除）
            var startDate = m.HeldSegmentIndex == _config.GetSegmentIndexAt(TimeOnly.FromDateTime(now))
                ? _config.GetActiveSegmentStart(m.HeldSegmentIndex, now) : now;
            var end = _config.GetSegmentEndDateTime(m.HeldSegmentIndex, startDate);
            if (end <= now) end = end.AddDays(1);
            return (end, "切换到下一段主题");
        }
        var idx = _config.GetSegmentIndexAt(TimeOnly.FromDateTime(now));
        var when = _config.GetSegmentEndDateTime(idx, _config.GetActiveSegmentStart(idx, now));
        var nextIdx = (idx + 1) % _config.Segments.Count;
        return (when, $"切换到「{_config.Segments[nextIdx].ThemeId}」所属时段");
    }

    /// <summary>
    /// tick 决策：返回需要执行的动作。
    /// SwitchTo：应用该段主题；Null：无事可做。
    /// </summary>
    public TickDecision Decide(bool fullscreen, string? lastAppliedThemePath)
    {
        if (Paused || !_config.Validate()) return TickDecision.None();
        if (_manual is { } invalid && (invalid.HeldSegmentIndex < 0 || invalid.HeldSegmentIndex >= _config.Segments.Count))
            _manual = null;
        var now = Now;
        var nowTime = TimeOnly.FromDateTime(now);
        var idx = _config.GetSegmentIndexAt(nowTime);

        // 保持中：越过恢复时刻 → 清除手动，落回自然判定
        if (_manual is { } m && m.HoldUntil is { } until && now >= until)
        {
            _manual = null;
            LogService.Info("保持期结束，恢复自动调度");
        }

        // 保持（未到期）：不动作
        if (_manual is { HoldUntil: not null }) return TickDecision.None();

        // Override 模式：当前时刻已进入被点段 → 手动状态自然清除
        if (_manual is { } o && !o.IsHold && (idx == o.HeldSegmentIndex || nowTime >= _config.Segments[o.HeldSegmentIndex].StartTime))
        {
            _manual = null;
        }

        if (fullscreen)
        {
            return TickDecision.None();
        }

        // 自然/Override 判定：当前应生效的主题
        var effectiveIdx = _manual?.HeldSegmentIndex ?? idx;
        var target = GetEffectiveThemeId(now);
        if (string.Equals(ThemeCatalogService.NormalizePath(target),
                           ThemeCatalogService.NormalizePath(lastAppliedThemePath ?? string.Empty),
                           StringComparison.OrdinalIgnoreCase))
        {
            return TickDecision.None();
        }
        return TickDecision.SwitchTo(effectiveIdx, _manual?.HoldUntil is not null);
    }

    /// <summary>查询提前窗口内的下一次实际主题变化；不会提前清除手动状态。</summary>
    /// <param name="leadTime">提示提前量。</param>
    /// <param name="lastAppliedThemePath">当前实际应用的主题。</param>
    /// <returns>目标段与绝对执行时刻；没有变化时为空。</returns>
    public UpcomingSwitch? GetUpcomingSwitch(TimeSpan leadTime, string? lastAppliedThemePath)
    {
        if (Paused || !_config.Validate() || leadTime <= TimeSpan.Zero) return null;
        var now = Now;
        var limit = now.Add(leadTime);
        var candidates = new SortedSet<DateTime>();
        for (var date = now.Date; date <= limit.Date; date = date.AddDays(1))
            foreach (var segment in _config.Segments)
            {
                var start = date.Add(segment.StartTime.ToTimeSpan());
                if (start > now && start <= limit) candidates.Add(start);
            }
        if (_manual?.HoldUntil is { } until && until > now && until <= limit)
            candidates.Add(until);
        foreach (var at in candidates)
        {
            if (_manual?.HoldUntil is { } heldUntil && at < heldUntil) continue;
            var index = _config.GetSegmentIndexAt(TimeOnly.FromDateTime(at));
            if (_manual is { HoldUntil: null } manual &&
                TimeOnly.FromDateTime(at) < _config.Segments[manual.HeldSegmentIndex].StartTime)
                index = manual.HeldSegmentIndex;
            var target = _config.Segments[index].ThemeId;
            if (!string.Equals(ThemeCatalogService.NormalizePath(target),
                    ThemeCatalogService.NormalizePath(lastAppliedThemePath ?? string.Empty),
                    StringComparison.OrdinalIgnoreCase))
                return new UpcomingSwitch(index, at);
        }
        return null;
    }

    /// <summary>按系统主题标记当前行，同一主题跨多段时优先标记手动段或最近开始的匹配段。</summary>
    /// <param name="actualTheme">从 Windows 读取的当前主题路径，读取失败时为空。</param>
    /// <returns>按计划顺序排列的时段状态。</returns>
    public List<SegmentView> GetSegmentViews(string? actualTheme)
    {
        var now = Now;
        var nowTime = TimeOnly.FromDateTime(now);
        var idx = _config.GetSegmentIndexAt(nowTime);
        var actual = ThemeCatalogService.NormalizePath(actualTheme ?? string.Empty);
        // 临时保持只覆盖当前时段：其余时段仍按计划标记，当前标记照常跟随实际主题。
        var held = _manual is { IsTemporary: true } ? null : _manual;
        bool Matches(int i) => actual.Length > 0 && string.Equals(actual,
            ThemeCatalogService.NormalizePath(_config.Segments[i].ThemeId), StringComparison.OrdinalIgnoreCase);
        int current = -1;
        if (held is { } manual && Matches(manual.HeldSegmentIndex))
            current = manual.HeldSegmentIndex;
        else
            for (int offset = 0; offset < _config.Segments.Count; offset++)
            {
                int candidate = (idx - offset + _config.Segments.Count) % _config.Segments.Count;
                if (!Matches(candidate)) continue;
                current = candidate;
                break;
            }
        var views = new List<SegmentView>();
        for (int i = 0; i < _config.Segments.Count; i++)
        {
            SegmentMark mark;
            if (i == current) mark = SegmentMark.Current;
            else if (i == (held?.HeldSegmentIndex ?? idx)) mark = SegmentMark.Upcoming;
            else if (held is { } m)
            {
                if (m.IsHold) mark = i < m.HeldSegmentIndex ? SegmentMark.Ended : SegmentMark.NextDay;
                else mark = i < m.HeldSegmentIndex ? SegmentMark.Ended : SegmentMark.Upcoming;
            }
            else
            {
                mark = _config.Segments[i].StartTime <= nowTime ? SegmentMark.Ended : SegmentMark.Upcoming;
            }
            views.Add(new SegmentView(i, mark, held?.IsHold ?? false));
        }
        return views;
    }

    /// <summary>测试辅助：以指定时刻执行一次决策判定。</summary>
    public void TickForTest(DateTime time, bool fullscreen, string? lastApplied)
    {
        var original = _clock;
        _clock = () => time;
        try
        {
            Decide(fullscreen, lastApplied);
        }
        finally
        {
            _clock = original;
        }
    }

}

/// <summary>一次 tick 的决策结果。</summary>
public readonly record struct TickDecision(bool HasAction, int SegmentIndex, bool IsHold)
{
    public static TickDecision None() => new(false, -1, false);
    public static TickDecision SwitchTo(int segmentIndex, bool isHold) => new(true, segmentIndex, isHold);
}

/// <summary>提前提示对应的目标段和绝对执行时刻。</summary>
public sealed record UpcomingSwitch(int SegmentIndex, DateTime ExecuteAt);
