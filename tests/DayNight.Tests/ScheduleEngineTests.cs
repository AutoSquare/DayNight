using DayNight.Models;
using DayNight.Services;
using Xunit;

namespace DayNight.Tests;

/// <summary>ScheduleEngine 调度语义（四态推导 / 保持到段尾 / 边界时刻）单元测试。</summary>
public class ScheduleEngineTests
{
    private static ScheduleConfig BuildConfig() => new()
    {
        Segments = new List<ThemeSegment>
        {
            new(new TimeOnly(0, 0), "C:\\themes\\midnight.theme"),
            new(new TimeOnly(6, 30), "C:\\themes\\dawn.theme"),
            new(new TimeOnly(9, 0), "C:\\themes\\day.theme"),
            new(new TimeOnly(17, 30), "C:\\themes\\dusk.theme"),
            new(new TimeOnly(20, 0), "C:\\themes\\midnight.theme"),
        },
    };

    private static DateTime At(int hour, int minute, int day = 18) => new(2026, 11, day, hour, minute, 0);

    [Theory]
    [InlineData(@"C:\themes\DUSK.theme", 3)]
    [InlineData(@"C:\themes\external.theme", -1)]
    [InlineData("", -1)]
    public void CurrentBadgeFollowsWindowsAtBoundary(string actualTheme, int expectedCurrent)
    {
        var engine = new ScheduleEngine(BuildConfig(), null, () => At(9, 0));
        var views = engine.GetSegmentViews(actualTheme);
        Assert.Equal(expectedCurrent, views.SingleOrDefault(v => v.Mark == SegmentMark.Current)?.Index ?? -1);
        Assert.Equal(2, engine.Decide(false, actualTheme).SegmentIndex);
    }

    [Fact]
    public void NowSegmentIndex_AtBoundaries()
    {
        var engine = new ScheduleEngine(BuildConfig(), null, () => At(9, 0));
        Assert.Equal(2, engine.NowSegmentIndex()); // 09:00 整点属于日间段

        var engine2 = new ScheduleEngine(BuildConfig(), null, () => At(17, 29));
        Assert.Equal(2, engine2.NowSegmentIndex());

        var engine3 = new ScheduleEngine(BuildConfig(), null, () => At(17, 30));
        Assert.Equal(3, engine3.NowSegmentIndex());

        var engine4 = new ScheduleEngine(BuildConfig(), null, () => At(23, 59));
        Assert.Equal(4, engine4.NowSegmentIndex());
    }

    [Fact]
    public void NaturalView_PastEnded_CurrentMarked_FutureUpcoming()
    {
        var engine = new ScheduleEngine(BuildConfig(), null, () => At(14, 20));
        var views = engine.GetSegmentViews(@"C:\themes\day.theme");
        Assert.Equal(SegmentMark.Ended, views[0].Mark);
        Assert.Equal(SegmentMark.Ended, views[1].Mark);
        Assert.Equal(SegmentMark.Current, views[2].Mark);
        Assert.Equal(SegmentMark.Upcoming, views[3].Mark);
        Assert.Equal(SegmentMark.Upcoming, views[4].Mark);
    }

    [Fact]
    public void ClickEndedSegment_HoldsToSegmentEndTomorrow()
    {
        var engine = new ScheduleEngine(BuildConfig(), null, () => At(17, 29));
        engine.ClickSegment(0, hold: true); // 点 00:00 深夜（已结束）

        var manual = engine.Manual;
        Assert.NotNull(manual);
        Assert.True(manual!.IsHold);
        // 恢复时刻 = 00:00 段尾在明天 = 明天 06:30
        Assert.Equal(At(6, 30, day: 19), manual.HoldUntil);
    }

    [Fact]
    public void HoldView_CurrentOnClicked_OthersNextDayOrEnded()
    {
        var engine = new ScheduleEngine(BuildConfig(), null, () => At(14, 20));
        engine.ClickSegment(1, hold: true); // 点 06:30 晨光（已结束）

        var views = engine.GetSegmentViews(@"C:\themes\dawn.theme");
        Assert.Equal(SegmentMark.Ended, views[0].Mark);
        Assert.Equal(SegmentMark.Current, views[1].Mark);
        Assert.Equal(SegmentMark.NextDay, views[2].Mark);
        Assert.Equal(SegmentMark.NextDay, views[3].Mark);
        Assert.Equal(SegmentMark.NextDay, views[4].Mark);
    }

    [Fact]
    public void Hold_SkipsSwitchUntilSegmentEnd()
    {
        var engine = new ScheduleEngine(BuildConfig(), null, () => At(14, 20));
        engine.ClickSegment(1, hold: true);

        // 保持期内（17:30/20:00/次日 00:00/06:30）均不切换
        foreach (var time in new[] { At(17, 30), At(20, 0), At(0, 0, day: 19), At(6, 30, day: 19) })
        {
            engine.TickForTest(time, fullscreen: false, lastApplied: "hold-theme");
            Assert.NotNull(engine.Manual);
        }

        // 明天 09:00 恢复自动：清除保持
        engine.TickForTest(At(9, 0, day: 19), fullscreen: false, lastApplied: "hold-theme");
        Assert.Null(engine.Manual);
    }

    [Fact]
    public void OverrideMode_ClickFutureSegment_ClearsAtItsStart()
    {
        var engine = new ScheduleEngine(BuildConfig(), null, () => At(14, 20));
        engine.ClickSegment(3, hold: false); // 点 17:30 黄昏（未来段）

        // 14:20–17:30 之间保持 override
        engine.TickForTest(At(15, 0), fullscreen: false, lastApplied: "C:\\themes\\dusk.theme");
        Assert.NotNull(engine.Manual);

        // 17:30 到点：清除 override（此时已应用黄昏主题，自然判定无切换）
        engine.TickForTest(At(17, 30), fullscreen: false, lastApplied: "C:\\themes\\dusk.theme");
        Assert.Null(engine.Manual);
    }

    [Fact]
    public void NaturalSwitch_TriggeredWhenAppliedDiffers()
    {
        var engine = new ScheduleEngine(BuildConfig(), null, () => At(17, 30));
        var decision = engine.Decide(fullscreen: false, lastAppliedThemePath: "C:\\themes\\day.theme");
        Assert.True(decision.HasAction);
        Assert.Equal(3, decision.SegmentIndex);
    }

    [Fact]
    public void NoSwitch_WhenAppliedMatches()
    {
        var engine = new ScheduleEngine(BuildConfig(), null, () => At(17, 30));
        var decision = engine.Decide(fullscreen: false, lastAppliedThemePath: "C:\\themes\\dusk.theme");
        Assert.False(decision.HasAction);
    }

    [Fact]
    public void PausedBlocksSwitch()
    {
        var engine = new ScheduleEngine(BuildConfig(), null, () => At(17, 30)) { Paused = true };
        var decision = engine.Decide(fullscreen: false, lastAppliedThemePath: "C:\\themes\\day.theme");
        Assert.False(decision.HasAction);
    }

    [Fact]
    public void FullscreenBlocksSwitch()
    {
        var engine = new ScheduleEngine(BuildConfig(), null, () => At(17, 30));
        var decision = engine.Decide(fullscreen: true, lastAppliedThemePath: "C:\\themes\\day.theme");
        Assert.False(decision.HasAction);
    }

    [Fact]
    public void ResetManual_RestoresNaturalView()
    {
        var engine = new ScheduleEngine(BuildConfig(), null, () => At(14, 20));
        engine.ClickSegment(1, hold: true);
        engine.ResetManual();
        Assert.Null(engine.Manual);
        var views = engine.GetSegmentViews(@"C:\themes\day.theme");
        Assert.Equal(SegmentMark.Current, views[2].Mark); // 14:20 → 日间段
    }

    [Fact]
    public void Validate_RejectsGapsAndOverlaps()
    {
        var config = BuildConfig();
        Assert.True(config.Validate());

        config.Segments[2].StartTime = new TimeOnly(6, 0); // 与前段重叠
        Assert.False(config.Validate());

        var config2 = BuildConfig();
        config2.Segments[0].StartTime = new TimeOnly(0, 30); // 首段非 00:00
        Assert.True(config2.Validate());
    }
}
