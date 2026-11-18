using System.Text;
using DayNight.Services;
using Xunit;

namespace DayNight.Tests;

/// <summary>主题应用必须核对系统与应用各自的明暗设置。</summary>
public sealed class ThemeApplyTests
{
    /// <summary>分别保留两种明暗模式，不能只解析壁纸。</summary>
    [Theory]
    [InlineData("Light", "Light", true, true)]
    [InlineData("Dark", "Dark", false, false)]
    [InlineData("Dark", "Light", false, true)]
    [InlineData("light", "dark", true, false)]
    public void ReadsBothAppearanceModes(string system, string apps, bool expectedSystem, bool expectedApps)
    {
        var path = Path.Combine(Path.GetTempPath(), $"DayNight-theme-{Guid.NewGuid():N}.theme");
        try
        {
            File.WriteAllText(path, $"[Theme]\nDisplayName=完整主题\n[VisualStyles]\nSystemMode={system}\nAppMode={apps}", Encoding.Unicode);
            var theme = ThemeFileContent.Parse(path);
            Assert.Equal(expectedSystem, theme.SystemUsesLightTheme);
            Assert.Equal(expectedApps, theme.AppsUseLightTheme);
        }
        finally { File.Delete(path); }
    }

    /// <summary>其中一个明暗模式没有生效时，不能返回应用成功。</summary>
    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    [InlineData(null, 1, false)]
    public void RejectsPartialAppearance(int? actualSystem, int? actualApps, bool expected)
    {
        var theme = new ThemeFileContent { SystemUsesLightTheme = true, AppsUseLightTheme = true };
        Assert.Equal(expected, theme.MatchesAppearance(actualSystem, actualApps, null));
    }

    /// <summary>固定强调色不匹配时拒绝成功；自动取色时允许 Windows 计算颜色。</summary>
    [Fact]
    public void ChecksFixedAccentButAllowsAutomaticColor()
    {
        var theme = new ThemeFileContent { ColorizationColor = 0xC4D0000C };
        Assert.True(theme.MatchesAppearance(null, null, 0xC4D0000C));
        Assert.False(theme.MatchesAppearance(null, null, 0xC40078D4));
        Assert.False(theme.MatchesAppearance(null, null, null));
        Assert.True((theme with { AutoColorization = true }).MatchesAppearance(null, null, 0xC40078D4));
    }

    /// <summary>路径不存在时不能返回成功，也不能改变系统的当前主题记录。</summary>
    [Fact]
    public void MissingFileDoesNotReportSuccessOrOverwriteCurrentTheme()
    {
        var catalog = new ThemeCatalogService();
        var before = catalog.GetNormalizedCurrentThemePath();
        var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.theme");
        Assert.False(new ThemeApplyService().Apply(missing));
        Assert.Equal(before, catalog.GetNormalizedCurrentThemePath());
    }
}
