using System.Reflection;
using System.Text.Json;
using DayNight.Models;
using DayNight.Services;
using Xunit;
using DayNightContext = DayNight.Services.AppContext;

namespace DayNight.Tests;

/// <summary>重启后的调度开关恢复回归测试。</summary>
public sealed class SettingsPersistenceTests
{
    /// <summary>关闭自动切换后，序列化往返与引擎初始化均保持暂停。</summary>
    [Fact]
    public void DisabledAutoSwitchSurvivesReloadBeforeFirstDecision()
    {
        var config = JsonSerializer.Deserialize<ConfigStore.Config>("""{"Settings":{"AutoSwitchEnabled":false}}""")!;
        var reloaded = JsonSerializer.Deserialize<ConfigStore.Config>(JsonSerializer.Serialize(config))!;
        var ctor = typeof(DayNightContext).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var context = (DayNightContext)ctor.Invoke([reloaded.Settings, reloaded.Schedule, reloaded.Manual]);
        Assert.True(context.Engine.Paused);
        Assert.False(context.Engine.Decide(false, "different.theme").HasAction);
    }
}
