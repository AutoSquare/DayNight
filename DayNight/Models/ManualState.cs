namespace DayNight.Models;

/// <summary>
/// 手动覆盖状态（持久化）。两种模式：
/// Override（HoldUntil 为 null）：点击了当前/未来时段，该段主题提前生效，到该段起始时刻自然清除；
/// Hold（HoldUntil 有值）：点击了已结束/下一日时段，跳过今天剩余切换，保持到该时段在明天的结束时刻。
/// </summary>
public sealed class ManualState
{
    /// <summary>被手动选中的段索引。</summary>
    public int HeldSegmentIndex { get; set; }

    /// <summary>保持结束时刻；null 表示 Override 模式（到该段起始时刻自然清除）。</summary>
    public DateTime? HoldUntil { get; set; }

    /// <summary>是否处于"保持到明天"模式。</summary>
    public bool IsHold => HoldUntil.HasValue;

    public static ManualState? Create(int segmentIndex, bool hold, DateTime now, TimeOnly segmentEndTime)
    {
        var state = new ManualState { HeldSegmentIndex = segmentIndex };
        if (hold)
        {
            // 段尾在明天：DateOnly.FromDateTime(now) 加一天 + 段尾时刻
            var endDate = DateOnly.FromDateTime(now).AddDays(1);
            state.HoldUntil = endDate.ToDateTime(segmentEndTime);
        }
        return state;
    }
}
