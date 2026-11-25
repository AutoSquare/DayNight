using DayNight.Models;
using DayNight.Services;
using Xunit;

namespace DayNight.Tests;

/// <summary>主题库手动应用与运行期间外部更换主题的临时保持，以及手动操作与补切的区分。</summary>
public class ManualThemeChangeTests
{
    private static readonly ScheduleConfig Plan = new()
    {
        Segments =
        [
            new(new TimeOnly(0, 0), @"C:\themes\morning.theme"),
            new(new TimeOnly(6, 0), @"C:\themes\day.theme"),
            new(new TimeOnly(20, 0), @"C:\themes\night.theme"),
        ],
    };

    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1);

    private static DateTime At(int hour, int minute, int day = 18) => new(2026, 11, day, hour, minute, 0);

    /// <summary>临时保持期间不切换，到下一个切换点先预告再按计划切往下一段。</summary>
    [Fact]
    public void TemporaryHold_BlocksSwitchUntilNextSwitchPoint()
    {
        var now = At(21, 0);
        var engine = new ScheduleEngine(Plan, null, () => now);

        var manual = engine.HoldUntilNextSwitch();

        Assert.True(manual.IsTemporary);
        Assert.Equal(2, manual.HeldSegmentIndex);
        Assert.Equal(At(0, 0, day: 19), manual.HoldUntil);
        var (when, reason) = engine.GetNextSwitchInfo();
        Assert.Equal(manual.HoldUntil, when);
        Assert.Equal("恢复自动", reason);
        Assert.False(engine.Decide(false, @"C:\themes\other.theme").HasAction);

        now = new DateTime(2026, 11, 18, 23, 59, 56);
        var upcoming = engine.GetUpcomingSwitch(TimeSpan.FromSeconds(5), @"C:\themes\other.theme");
        Assert.Equal(new UpcomingSwitch(0, At(0, 0, day: 19)), upcoming);

        now = At(0, 0, day: 19);
        var decision = engine.Decide(false, @"C:\themes\other.theme");
        Assert.Null(engine.Manual);
        Assert.True(decision.HasAction);
        Assert.Equal(0, decision.SegmentIndex);
    }

    /// <summary>临时保持只覆盖当前时段，其余时段仍按计划标记，不出现「下一日」。</summary>
    [Theory]
    [InlineData(@"C:\themes\night.theme", 2)]
    [InlineData(@"C:\themes\other.theme", -1)]
    public void TemporaryHold_KeepsNaturalMarksForOtherSegments(string actual, int current)
    {
        var engine = new ScheduleEngine(Plan, null, () => At(14, 20));
        engine.HoldUntilNextSwitch();

        var views = engine.GetSegmentViews(actual);

        Assert.Equal(current, views.SingleOrDefault(v => v.Mark == SegmentMark.Current)?.Index ?? -1);
        Assert.Equal(SegmentMark.Ended, views[0].Mark);
        Assert.DoesNotContain(views, v => v.Mark == SegmentMark.NextDay);
        Assert.All(views, v => Assert.False(v.IsHold));
    }

    /// <summary>规则页点行是新的手动选择，不沿用临时保持的到期时刻。</summary>
    [Fact]
    public void SegmentClick_ReplacesTemporaryHold()
    {
        var engine = new ScheduleEngine(Plan, null, () => At(21, 0));
        var temporaryUntil = engine.HoldUntilNextSwitch().HoldUntil;

        var hold = engine.ClickSegment(0, hold: true);

        Assert.False(hold.IsTemporary);
        Assert.Equal(At(6, 0, day: 19), hold.HoldUntil);
        Assert.NotEqual(temporaryUntil, hold.HoldUntil);
    }

    /// <summary>运行期间主题被换成计划外主题，登记到下一个切换点的临时保持。</summary>
    [Fact]
    public void ExternalChange_RegistersTemporaryHold()
    {
        var now = At(14, 20);
        var engine = new ScheduleEngine(Plan, null, () => now);

        Assert.True(engine.NoteExternalChange(@"C:\themes\day.theme", @"C:\themes\other.theme", Step));

        var manual = engine.Manual;
        Assert.NotNull(manual);
        Assert.True(manual!.IsTemporary);
        Assert.Equal(1, manual.HeldSegmentIndex);
        Assert.Equal(At(20, 0), manual.HoldUntil);
        Assert.False(engine.Decide(false, @"C:\themes\other.theme").HasAction);
        now = At(20, 0);
        Assert.Equal(2, engine.Decide(false, @"C:\themes\other.theme").SegmentIndex);
    }

    /// <summary>刚启动没有上一次读数，或两次检查间隔过长（睡眠、休眠）时，主题差异按补切处理。</summary>
    [Theory]
    [InlineData("", 1)]
    [InlineData(@"C:\themes\day.theme", 5)]
    public void ExternalChange_NotObservedWhileRunning_IsTreatedAsCatchUp(string? previous, int seconds)
    {
        var engine = new ScheduleEngine(Plan, null, () => At(14, 20));

        Assert.False(engine.NoteExternalChange(previous, @"C:\themes\other.theme", TimeSpan.FromSeconds(seconds)));
        Assert.Null(engine.Manual);
        Assert.Equal(1, engine.Decide(false, @"C:\themes\other.theme").SegmentIndex);
    }

    /// <summary>主题没有变化，或换回了计划目标时无需登记。</summary>
    [Theory]
    [InlineData(@"C:\themes\day.theme", @"c:\THEMES\day.theme")]
    [InlineData(@"C:\themes\other.theme", @"C:\themes\day.theme")]
    public void ExternalChange_UnchangedOrBackToPlan_IsIgnored(string previous, string actual)
    {
        var engine = new ScheduleEngine(Plan, null, () => At(14, 20));

        Assert.False(engine.NoteExternalChange(previous, actual, Step));
        Assert.Null(engine.Manual);
        Assert.False(engine.Decide(false, actual).HasAction);
    }

    /// <summary>已有保持到明天的手动状态时继续沿用，不被临时保持缩短。</summary>
    [Fact]
    public void ExternalChange_KeepsLongHold()
    {
        var engine = new ScheduleEngine(Plan, null, () => At(14, 20));
        var holdUntil = engine.ClickSegment(0, hold: true).HoldUntil;

        Assert.False(engine.NoteExternalChange(@"C:\themes\day.theme", @"C:\themes\other.theme", Step));
        Assert.Equal(holdUntil, engine.Manual?.HoldUntil);
        Assert.False(engine.Manual?.IsTemporary);
    }

    /// <summary>提前生效状态不覆盖外部更换的主题，由临时保持替换。</summary>
    [Fact]
    public void ExternalChange_ReplacesOverride()
    {
        var engine = new ScheduleEngine(Plan, null, () => At(14, 20));
        engine.ClickSegment(2, hold: false);
        Assert.Null(engine.Manual?.HoldUntil);

        Assert.True(engine.NoteExternalChange(@"C:\themes\night.theme", @"C:\themes\other.theme", Step));
        Assert.True(engine.Manual?.IsTemporary);
        Assert.Equal(At(20, 0), engine.Manual?.HoldUntil);
    }

    /// <summary>临时保持写入配置后重新读取仍然有效，到下一个切换点恢复计划。</summary>
    [Fact]
    public void TemporaryHold_SurvivesReload()
    {
        var now = At(14, 20);
        var engine = new ScheduleEngine(Plan, null, () => now);
        engine.HoldUntilNextSwitch();
        var json = System.Text.Json.JsonSerializer.Serialize(new ConfigStore.Config { Schedule = Plan, Manual = engine.Manual });

        var loaded = System.Text.Json.JsonSerializer.Deserialize<ConfigStore.Config>(json)!;
        Assert.False(loaded.RepairSchedule());
        Assert.True(loaded.Manual?.IsTemporary);

        var reloaded = new ScheduleEngine(loaded.Schedule, loaded.Manual, () => now);
        Assert.False(reloaded.Decide(false, @"C:\themes\other.theme").HasAction);
        now = At(20, 0);
        Assert.Equal(2, reloaded.Decide(false, @"C:\themes\other.theme").SegmentIndex);
    }

    /// <summary>不含新字段的旧配置按保持到明天读取。</summary>
    [Fact]
    public void LegacyConfigWithoutFlag_ReadsAsLongHold()
    {
        var manual = System.Text.Json.JsonSerializer.Deserialize<ManualState>(
            @"{""HeldSegmentIndex"":0,""HoldUntil"":""2026-11-19T06:00:00""}")!;

        Assert.False(manual.IsTemporary);
        Assert.True(manual.IsHold);
    }
}
