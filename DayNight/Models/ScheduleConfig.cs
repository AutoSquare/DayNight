namespace DayNight.Models;

/// <summary>每日循环计划。各段按起点排序，最后一段延续到次日第一个起点。</summary>
public sealed class ScheduleConfig
{
    public List<ThemeSegment> Segments { get; set; } = new()
    {
        new ThemeSegment(TimeOnly.MinValue, string.Empty),
    };

    /// <summary>查找最近已开始的时段，今日首个起点之前沿用昨日末段。</summary>
    public int GetSegmentIndexAt(TimeOnly time)
    {
        for (int i = Segments.Count - 1; i >= 0; i--)
            if (time >= Segments[i].StartTime) return i;
        return Math.Max(0, Segments.Count - 1);
    }

    /// <summary>指定时段结束于循环中的下一个起点。</summary>
    public TimeOnly GetSegmentEndTime(int index) => Segments[(index + 1) % Segments.Count].StartTime;

    /// <summary>返回当前这一次时段的实际起始日期时间，供跨日调度与跳过提示共用。</summary>
    public DateTime GetActiveSegmentStart(int index, DateTime now)
    {
        var start = now.Date.Add(Segments[index].StartTime.ToTimeSpan());
        return start > now ? start.AddDays(-1) : start;
    }

    /// <summary>以该段开始的日期计算结束时刻，末段结束于次日。</summary>
    public DateTime GetSegmentEndDateTime(int index, DateTime startDate) =>
        startDate.Date.AddDays(index == Segments.Count - 1 ? 1 : 0).Add(GetSegmentEndTime(index).ToTimeSpan());

    /// <summary>在零点到次日零点的时间轴上展开循环，返回段索引以及可见的分钟范围。</summary>
    public IEnumerable<(int Index, int Start, int End)> GetDaySlices()
    {
        var first = (int)Segments[0].StartTime.ToTimeSpan().TotalMinutes;
        if (first > 0)
            yield return (Segments.Count - 1, 0, first);
        for (int i = 0; i < Segments.Count; i++)
        {
            var start = (int)Segments[i].StartTime.ToTimeSpan().TotalMinutes;
            var end = i + 1 == Segments.Count ? 1440 : (int)GetSegmentEndTime(i).ToTimeSpan().TotalMinutes;
            yield return (i, start, end);
        }
    }

    /// <summary>将最后一个起点到次日首个起点之间的时段平分，保留已有切换点。</summary>
    public bool TryAddSegment()
    {
        if (!Validate()) return false;
        var last = Segments[^1];
        var start = (int)last.StartTime.ToTimeSpan().TotalMinutes;
        var length = 1440 + (int)Segments[0].StartTime.ToTimeSpan().TotalMinutes - start;
        if (length < 2) return false;
        Segments.Add(new ThemeSegment(last.StartTime.AddMinutes(length / 2), last.ThemeId));
        Segments.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        return true;
    }

    /// <summary>删除一个切换点，其前一时段自动延续，至少保留一段。</summary>
    public bool TryRemoveSegment(int index)
    {
        if (Segments.Count <= 1 || index < 0 || index >= Segments.Count) return false;
        Segments.RemoveAt(index);
        return true;
    }

    /// <summary>将所选主题移动至未被占用的整分钟，按新的切换顺序排列。</summary>
    public bool TrySetStartTime(int index, TimeOnly time)
    {
        if (index < 0 || index >= Segments.Count || time.Ticks % TimeSpan.TicksPerMinute != 0) return false;
        var segment = Segments[index];
        if (Segments.Any(s => s != segment && s.StartTime == time)) return false;
        segment.StartTime = time;
        Segments.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        return true;
    }

    /// <summary>将旧配置对齐到整分钟，排序并合并重复起点。</summary>
    public bool Repair()
    {
        if (Validate()) return false;
        foreach (var segment in Segments)
            segment.StartTime = new TimeOnly(segment.StartTime.Hour, segment.StartTime.Minute);
        Segments = Segments.OrderBy(s => s.StartTime).GroupBy(s => s.StartTime).Select(g => g.Last()).ToList();
        if (Segments.Count == 0) Segments.Add(new ThemeSegment(TimeOnly.MinValue, string.Empty));
        return true;
    }

    /// <summary>计划至少包含一个整分钟起点，各起点严格递增。</summary>
    public bool Validate()
    {
        for (int i = 0; i < Segments.Count; i++)
        {
            var time = Segments[i].StartTime;
            if (time.Ticks % TimeSpan.TicksPerMinute != 0 || (i > 0 && time <= Segments[i - 1].StartTime)) return false;
        }
        return Segments.Count > 0;
    }
}
