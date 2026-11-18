using DayNight.Models;
using Xunit;

namespace DayNight.Tests;

/// <summary>草稿必须隔离正在执行的计划，所有输入路径共享同一约束。</summary>
public class ScheduleDraftTests
{
    private static ScheduleConfig Plan() => new() { Segments = [new(TimeOnly.MinValue, "day.theme"), new(new TimeOnly(18, 0), "night.theme")] };

    /// <summary>编辑时间、主题和段数不会污染已保存计划。</summary>
    [Fact]
    public void Edits_AreIsolatedUntilSave()
    {
        var saved = Plan();
        var draft = new ScheduleDraft(saved);
        draft.Plan.TrySetStartTime(1, new TimeOnly(17, 30));
        draft.Plan.Segments[0].ThemeId = "other.theme";
        draft.Plan.TryAddSegment();
        Assert.True(draft.IsDirty);
        Assert.Equal(2, saved.Segments.Count);
        Assert.Equal(new TimeOnly(18, 0), saved.Segments[1].StartTime);
        Assert.Equal("day.theme", saved.Segments[0].ThemeId);
    }

    /// <summary>撤销恢复有效计划而不是恢复上一次未提交编辑。</summary>
    [Fact]
    public void Discard_RestoresSavedPlan()
    {
        var draft = new ScheduleDraft(Plan());
        draft.Plan.TryRemoveSegment(0);
        draft.Discard();
        Assert.False(draft.IsDirty);
        Assert.Equal(2, draft.Plan.Segments.Count);
    }

    /// <summary>保存快照不会随后续草稿编辑变化。</summary>
    [Fact]
    public void Snapshot_IsDetachedAndRejectsInvalidPlan()
    {
        var draft = new ScheduleDraft(Plan());
        var snapshot = draft.CreateSnapshot();
        draft.Plan.Segments[1].ThemeId = "changed.theme";
        Assert.Equal("night.theme", snapshot.Segments[1].ThemeId);
        draft.Plan.Segments[0].StartTime = draft.Plan.Segments[1].StartTime;
        Assert.Throws<InvalidOperationException>(() => draft.CreateSnapshot());
    }

    /// <summary>拖动按五分钟吸附，并避免越过首尾或相邻段。</summary>
    [Theory]
    [InlineData(0.5007, 12, 0)]
    [InlineData(-1, 0, 5)]
    [InlineData(2, 23, 55)]
    public void Drag_SnapsAndClamps(double fraction, int hour, int minute)
    {
        var draft = new ScheduleDraft(Plan());
        Assert.True(draft.MoveBoundary(1, fraction));
        Assert.Equal(new TimeOnly(hour, minute), draft.Plan.Segments[1].StartTime);
        Assert.True(draft.Plan.Validate());
        Assert.True(draft.MoveBoundary(0, 0.3));
        Assert.True(draft.Plan.Validate());
    }

    /// <summary>第一个手柄也能设置非零起点，保存快照保留新切换时刻。</summary>
    [Fact]
    public void DragFirstBoundary_PreservesOvernightCoverage()
    {
        var draft = new ScheduleDraft(Plan());
        Assert.True(draft.MoveBoundary(0, 0.25));
        var saved = draft.CreateSnapshot();
        Assert.Equal(new TimeOnly(6, 0), saved.Segments[0].StartTime);
        Assert.Equal(1, saved.GetSegmentIndexAt(TimeOnly.MinValue));
    }

    /// <summary>窄于吸附步长的相邻时段仍保留合法的一分钟间隔。</summary>
    [Fact]
    public void Drag_NarrowIntervalDoesNotCrossNeighbours()
    {
        var plan = Plan();
        plan.Segments = [new(TimeOnly.MinValue,"a"), new(new TimeOnly(0,1),"b"), new(new TimeOnly(0,2),"c"),new(new TimeOnly(0,3),"d")];
        var draft = new ScheduleDraft(plan);
        draft.MoveBoundary(2, 0.8);
        Assert.Equal(new TimeOnly(0, 2), draft.Plan.Segments[2].StartTime);
        Assert.True(draft.Plan.Validate());
    }
}
