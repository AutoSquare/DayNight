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
        var previousUntil = _manual?.HoldUntil;
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
        var target = _config.Segments[effectiveIdx].ThemeId;
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
        bool Matches(int i) => actual.Length > 0 && string.Equals(actual,
            ThemeCatalogService.NormalizePath(_config.Segments[i].ThemeId), StringComparison.OrdinalIgnoreCase);
        int current = -1;
        if (_manual is { } manual && Matches(manual.HeldSegmentIndex))
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
            else if (i == (_manual?.HeldSegmentIndex ?? idx)) mark = SegmentMark.Upcoming;
            else if (_manual is { } m)
            {
                if (m.IsHold) mark = i < m.HeldSegmentIndex ? SegmentMark.Ended : SegmentMark.NextDay;
                else mark = i < m.HeldSegmentIndex ? SegmentMark.Ended : SegmentMark.Upcoming;
            }
            else
            {
                mark = _config.Segments[i].StartTime <= nowTime ? SegmentMark.Ended : SegmentMark.Upcoming;
            }
            views.Add(new SegmentView(i, mark, _manual?.IsHold ?? false));
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
