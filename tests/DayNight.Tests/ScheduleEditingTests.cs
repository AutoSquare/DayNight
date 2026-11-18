using DayNight.Models;
using DayNight.Services;
using Xunit;

namespace DayNight.Tests;

/// <summary>覆盖用户报告的首段被改写、保持阻塞与跨日边界回归。</summary>
public class ScheduleEditingTests
{
    /// <summary>验证通知忙碌与真正全屏、演示模式的判定边界。</summary>
    [Theory]
    [InlineData(FullscreenDetector.QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY, false, false)]
    [InlineData(FullscreenDetector.QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY, true, true)]
    [InlineData(FullscreenDetector.QUERY_USER_NOTIFICATION_STATE.QUNS_ACCEPTS_NOTIFICATIONS, true, true)]
    [InlineData(FullscreenDetector.QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN, false, true)]
    [InlineData(FullscreenDetector.QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE, false, true)]
    public void FullscreenPolicy_DoesNotTreatBusyAloneAsFullscreen(FullscreenDetector.QUERY_USER_NOTIFICATION_STATE state, bool foreground, bool expected)
    {
        Assert.Equal(expected, FullscreenDetector.ShouldDefer(state, foreground));
    }

    /// <summary>从单段开始添加后仍从零点覆盖全天。</summary>
    [Fact]
    public void AddingSegment_PreservesMidnightAndSplitsAtWholeMinute()
    {
        var schedule = new ScheduleConfig();
        schedule.Segments[0].ThemeId = "day.theme";
        Assert.True(schedule.TryAddSegment());
        Assert.Equal(TimeOnly.MinValue, schedule.Segments[0].StartTime);
        Assert.Equal(new TimeOnly(12, 0), schedule.Segments[1].StartTime);
        Assert.Equal("day.theme", schedule.Segments[1].ThemeId);
        Assert.True(schedule.Validate());
    }

    /// <summary>末段无法继续拆分时保留原计划。</summary>
    [Fact]
    public void AddingSegment_WhenLastMinuteIsOccupied_DoesNotCorruptPlan()
    {
        var schedule = TwoSegments(new TimeOnly(23, 59));
        Assert.False(schedule.TryAddSegment());
        Assert.Equal(2, schedule.Segments.Count);
        Assert.True(schedule.Validate());
    }

    /// <summary>删除首段后保留其余起点，且不可删空。</summary>
    [Fact]
    public void RemovingFirstSegment_PreservesRemainingStart()
    {
        var schedule = TwoSegments(new TimeOnly(20, 0));
        Assert.True(schedule.TryRemoveSegment(0));
        Assert.True(schedule.Validate());
        Assert.Equal("night.theme", schedule.Segments[0].ThemeId);
        Assert.Equal(new TimeOnly(20, 0), schedule.Segments[0].StartTime);
        Assert.False(schedule.TryRemoveSegment(0));
    }

    /// <summary>表盘允许第一段跨过其他起点，保留主题，拒绝碰撞。</summary>
    [Fact]
    public void EditingFirstStart_ReordersWithThemeAndRejectsDuplicate()
    {
        var schedule = TwoSegments(new TimeOnly(0, 14));
        Assert.False(schedule.TrySetStartTime(0, new TimeOnly(0, 14)));
        Assert.True(schedule.TrySetStartTime(0, new TimeOnly(7, 0)));
        Assert.Equal("day.theme", schedule.Segments[1].ThemeId);
        Assert.Equal(new TimeOnly(0, 14), schedule.Segments[0].StartTime);
        Assert.False(schedule.Repair());
    }

    /// <summary>午夜沿用昨晚主题，到首个切换点才触发提示和应用。</summary>
    [Fact]
    public void OvernightSegment_ContinuesUntilFirstStartToday()
    {
        var schedule = TwoSegments(new TimeOnly(19, 0));
        schedule.Segments[0].StartTime = new TimeOnly(7, 0);
        var now = new DateTime(2026, 11, 18, 23, 59, 59);
        var morning = now.Date.AddDays(1).AddHours(7);
        var engine = new ScheduleEngine(schedule, null, () => now);
        Assert.Null(engine.GetUpcomingSwitch(TimeSpan.FromSeconds(5), "night.theme"));
        foreach (var time in new[] { now, morning.Date, morning.AddSeconds(-1) })
        {
            now = time;
            Assert.Equal(1, engine.NowSegmentIndex());
            Assert.Equal(morning, engine.GetNextSwitchInfo().when);
            Assert.False(engine.Decide(false, "night.theme").HasAction);
        }
        Assert.Equal(SegmentMark.Upcoming, engine.GetSegmentViews("night.theme")[0].Mark);
        Assert.Equal(morning, engine.GetUpcomingSwitch(TimeSpan.FromSeconds(5), "night.theme")?.ExecuteAt);
        now = morning;
        Assert.Equal(0, engine.Decide(false, "night.theme").SegmentIndex);
        Assert.Equal(now.Date.AddHours(19), engine.GetNextSwitchInfo().when);
    }

    /// <summary>规则分布与时间轴共用相同的可见区间，跨午夜的两块属于同一主题。</summary>
    [Fact]
    public void OvernightPlan_CoversEveryMinuteAfterReload()
    {
        var schedule = TwoSegments(new TimeOnly(19, 0));
        schedule.Segments[0].StartTime = new TimeOnly(7, 0);
        var json = System.Text.Json.JsonSerializer.Serialize(new ConfigStore.Config { Schedule = schedule });
        var loaded = System.Text.Json.JsonSerializer.Deserialize<ConfigStore.Config>(json)!;
        Assert.False(loaded.RepairSchedule());
        Assert.Equal(new[] { (1, 0, 420), (0, 420, 1140), (1, 1140, 1440) }, loaded.Schedule.GetDaySlices());
        foreach (var (index, start, end) in loaded.Schedule.GetDaySlices())
            for (var minute = start; minute < end; minute++)
                Assert.Equal(index, loaded.Schedule.GetSegmentIndexAt(TimeOnly.MinValue.AddMinutes(minute)));
        var midnight = new DateTime(2026, 11, 19);
        Assert.Equal(midnight.AddHours(-5), loaded.Schedule.GetActiveSegmentStart(1, midnight));
    }

    /// <summary>单段可从任意分钟开始，拆分跨午夜时段后起点仍有序。</summary>
    [Fact]
    public void SingleSegment_SplitsAcrossMidnight()
    {
        var schedule = new ScheduleConfig();
        schedule.Segments[0].StartTime = new TimeOnly(20, 15);
        Assert.Equal(0, schedule.GetSegmentIndexAt(TimeOnly.MinValue));
        Assert.True(schedule.TryAddSegment());
        Assert.Equal(new TimeOnly(8, 15), schedule.Segments[0].StartTime);
        Assert.Equal(new TimeOnly(20, 15), schedule.Segments[1].StartTime);
    }

    /// <summary>复现用户配置中的 11:59:30 首段与失效保持，并保留暂停偏好。</summary>
    [Fact]
    public void LegacyInvalidConfig_RepairsPrecisionAndClearsStaleHold_PreservesSwitchPreference()
    {
        var config = new ConfigStore.Config
        {
            Schedule = TwoSegments(new TimeOnly(23, 56)),
            Manual = new ManualState { HeldSegmentIndex = 0, HoldUntil = new DateTime(2026, 11, 19, 23, 55, 0) },
            Settings = new AppSettings { AutoSwitchEnabled = false }
        };
        config.Schedule.Segments[0].StartTime = new TimeOnly(11, 59, 30);
        Assert.True(config.RepairSchedule());
        Assert.True(config.Schedule.Validate());
        Assert.Equal(new TimeOnly(11, 59), config.Schedule.Segments[0].StartTime);
        Assert.Equal(new TimeOnly(23, 56), config.Schedule.Segments[1].StartTime);
        Assert.Null(config.Manual);
        Assert.False(config.Settings.AutoSwitchEnabled);
        Assert.False(config.RepairSchedule());
    }

    /// <summary>合法日程中用户主动设置的保持不被迁移清除。</summary>
    [Fact]
    public void ValidConfig_PreservesIntentionalHold()
    {
        var config = new ConfigStore.Config { Schedule = TwoSegments(new TimeOnly(20, 0)), Manual = new ManualState { HeldSegmentIndex = 0, HoldUntil = DateTime.Today.AddDays(1) } };
        Assert.False(config.RepairSchedule());
        Assert.NotNull(config.Manual);
    }

    /// <summary>午夜边界在次日零点触发，不能提前一秒。</summary>
    [Fact]
    public void MidnightTransition_IsExactlyNextDay_Not235959()
    {
        var schedule = TwoSegments(new TimeOnly(20, 0));
        var now = new DateTime(2026, 11, 18, 23, 59, 59);
        var engine = new ScheduleEngine(schedule, null, () => now);
        Assert.Equal(now.Date.AddDays(1), engine.GetNextSwitchInfo().when);
        Assert.False(engine.Decide(false, "night.theme").HasAction);
        now = now.Date.AddDays(1);
        Assert.Equal(0, engine.Decide(false, "night.theme").SegmentIndex);
    }

    /// <summary>跨日后重复点击保持也不延长原恢复时刻。</summary>
    [Fact]
    public void RepeatedHold_DoesNotPostponeRecovery()
    {
        var now = new DateTime(2026, 11, 18, 21, 0, 0);
        var engine = new ScheduleEngine(TwoSegments(new TimeOnly(20, 0)), null, () => now);
        var until = engine.ClickSegment(0, true).HoldUntil;
        now = now.AddDays(1).AddHours(-2);
        Assert.Equal(until, engine.ClickSegment(1, true).HoldUntil);
    }

    /// <summary>无效旧手动索引不能使调度持续异常。</summary>
    [Fact]
    public void InvalidManualIndex_DoesNotBreakTick()
    {
        var engine = new ScheduleEngine(TwoSegments(new TimeOnly(20, 0)), new ManualState { HeldSegmentIndex = 8 }, () => new DateTime(2026, 11, 18, 21, 0, 0));
        Assert.Equal(1, engine.Decide(false, "day.theme").SegmentIndex);
    }

    private static ScheduleConfig TwoSegments(TimeOnly second) => new()
    {
        Segments = [new(TimeOnly.MinValue, "day.theme"), new(second, "night.theme")]
    };
}
