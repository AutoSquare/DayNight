namespace DayNight.Models;

/// <summary>规则编辑草稿。与调度中的计划深度隔离，只有显式保存才生成提交快照。</summary>
public sealed class ScheduleDraft
{
    private readonly ScheduleConfig _saved;

    /// <summary>从当前有效日程创建编辑副本。</summary>
    public ScheduleDraft(ScheduleConfig saved)
    {
        _saved = saved;
        Plan = Copy(saved);
    }

    /// <summary>仅供编辑的时段列表，不被调度器读取。</summary>
    public ScheduleConfig Plan { get; private set; }

    /// <summary>草稿与当前有效计划是否存在内容差异。</summary>
    public bool IsDirty => !Plan.Segments.Select(s => (s.StartTime, s.ThemeId))
        .SequenceEqual(_saved.Segments.Select(s => (s.StartTime, s.ThemeId)));

    /// <summary>丢弃所有草稿修改，恢复当前有效计划。</summary>
    public void Discard() => Plan = Copy(_saved);

    /// <summary>返回可提交的独立副本；非法或缺少主题的草稿不能保存。</summary>
    public ScheduleConfig CreateSnapshot()
    {
        if (!Plan.Validate() || Plan.Segments.Any(s => string.IsNullOrWhiteSpace(s.ThemeId)))
            throw new InvalidOperationException("请为每个时段选择主题，并确保时间严格递增。");
        return Copy(Plan);
    }

    /// <summary>按全天横向比例拖动切换点，五分钟吸附且不跨越相邻段。</summary>
    public bool MoveBoundary(int index, double fraction)
    {
        if (index < 0 || index >= Plan.Segments.Count || !double.IsFinite(fraction)) return false;
        var minimum = index == 0 ? 0 : (int)Plan.Segments[index - 1].StartTime.ToTimeSpan().TotalMinutes + 1;
        var maximum = index + 1 < Plan.Segments.Count
            ? (int)Plan.Segments[index + 1].StartTime.ToTimeSpan().TotalMinutes - 1 : 1439;
        var firstGrid = (int)Math.Ceiling(minimum / 5d) * 5;
        var lastGrid = maximum / 5 * 5;
        var minute = (int)Math.Round(Math.Clamp(fraction, 0, 1) * 1440 / 5, MidpointRounding.AwayFromZero) * 5;
        minute = firstGrid <= lastGrid ? Math.Clamp(minute, firstGrid, lastGrid) : Math.Clamp(minute, minimum, maximum);
        return Plan.TrySetStartTime(index, TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minute)));
    }

    private static ScheduleConfig Copy(ScheduleConfig source) => new()
    {
        Segments = source.Segments.Select(s => new ThemeSegment(s.StartTime, s.ThemeId)).ToList()
    };
}
