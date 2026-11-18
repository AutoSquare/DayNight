using DayNight.Models;
using DayNight.Services;
using Xunit;

namespace DayNight.Tests;

/// <summary>切换前预告的绝对时刻及手动状态隔离回归。</summary>
public class UpcomingSwitchTests
{
    private static ScheduleConfig Plan() => new()
    {
        Segments = new()
        {
            new(TimeOnly.MinValue, "night.theme"),
            new(new TimeOnly(9, 0), "day.theme"),
            new(new TimeOnly(18, 0), "night.theme"),
        },
    };

    [Theory]
    [InlineData(6, false)]
    [InlineData(5, true)]
    [InlineData(1, true)]
    public void PreviewStartsOnlyInsideLeadWindow(int seconds, bool expected)
    {
        var deadline = new DateTime(2026, 11, 19, 9, 0, 0);
        var engine = new ScheduleEngine(Plan(), null, () => deadline.AddSeconds(-seconds));
        var next = engine.GetUpcomingSwitch(TimeSpan.FromSeconds(5), "night.theme");
        Assert.Equal(expected, next is not null);
        if (next is not null)
        {
            Assert.Equal(1, next.SegmentIndex);
            Assert.Equal(deadline, next.ExecuteAt);
            Assert.False(engine.Decide(false, "night.theme").HasAction);
        }
    }

    [Fact]
    public void MidnightPreviewUsesTomorrowOccurrence()
    {
        var plan = Plan();
        plan.Segments[^1].ThemeId = "evening.theme";
        var now = new DateTime(2026, 11, 19, 23, 59, 57);
        var next = new ScheduleEngine(plan, null, () => now)
            .GetUpcomingSwitch(TimeSpan.FromSeconds(5), "evening.theme");
        Assert.Equal(new UpcomingSwitch(0, now.Date.AddDays(1)), next);
    }

    [Fact]
    public void SameThemeAtMidnightNeedsNoPreview()
    {
        var now = new DateTime(2026, 11, 19, 23, 59, 57);
        Assert.Null(new ScheduleEngine(Plan(), null, () => now)
            .GetUpcomingSwitch(TimeSpan.FromSeconds(5), "NIGHT.theme"));
    }

    [Fact]
    public void PauseBlocksPreview()
    {
        var engine = new ScheduleEngine(Plan(), null, () => new DateTime(2026, 11, 19, 8, 59, 57)) { Paused = true };
        Assert.Null(engine.GetUpcomingSwitch(TimeSpan.FromSeconds(5), "night.theme"));
    }

    [Fact]
    public void HoldUntilTomorrowSuppressesTodaysPreview()
    {
        var manual = new ManualState { HeldSegmentIndex = 0, HoldUntil = new DateTime(2026, 11, 20, 9, 0, 0) };
        var engine = new ScheduleEngine(Plan(), manual, () => new DateTime(2026, 11, 19, 8, 59, 57));
        Assert.Null(engine.GetUpcomingSwitch(TimeSpan.FromSeconds(5), "night.theme"));
        Assert.Same(manual, engine.Manual);
    }

    [Fact]
    public void HoldExpiryIsPreviewedWithoutClearingHoldEarly()
    {
        var deadline = new DateTime(2026, 11, 20, 9, 0, 0);
        var now = deadline.AddSeconds(-3);
        var manual = new ManualState { HeldSegmentIndex = 0, HoldUntil = deadline };
        var engine = new ScheduleEngine(Plan(), manual, () => now);
        Assert.Equal(new UpcomingSwitch(1, deadline), engine.GetUpcomingSwitch(TimeSpan.FromSeconds(5), "night.theme"));
        Assert.Same(manual, engine.Manual);
        Assert.False(engine.Decide(false, "night.theme").HasAction);
        now = deadline;
        Assert.Equal(1, engine.Decide(false, "night.theme").SegmentIndex);
        Assert.Null(engine.Manual);
    }

    [Fact]
    public void EarlyManualSelectionDoesNotPreviewItsOwnStart()
    {
        var manual = new ManualState { HeldSegmentIndex = 1 };
        var engine = new ScheduleEngine(Plan(), manual, () => new DateTime(2026, 11, 19, 8, 59, 57));
        Assert.Null(engine.GetUpcomingSwitch(TimeSpan.FromSeconds(5), "day.theme"));
        Assert.Same(manual, engine.Manual);
    }

    [Fact]
    public void FullscreenDecisionDoesNotConsumeUpcomingSwitch()
    {
        var deadline = new DateTime(2026, 11, 19, 9, 0, 0);
        var now = deadline.AddSeconds(-3);
        var engine = new ScheduleEngine(Plan(), null, () => now);
        Assert.NotNull(engine.GetUpcomingSwitch(TimeSpan.FromSeconds(5), "night.theme"));
        now = deadline.AddMinutes(1);
        Assert.False(engine.Decide(true, "night.theme").HasAction);
        Assert.Equal(1, engine.Decide(false, "night.theme").SegmentIndex);
        Assert.Null(engine.GetUpcomingSwitch(TimeSpan.FromSeconds(5), "night.theme"));
    }
}
