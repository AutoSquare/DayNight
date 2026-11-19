using DayNight.Services;
using Xunit;

namespace DayNight.Tests;

/// <summary>自启登记项路径核验回归测试：登记值必须指向当前进程才算“已开启”。</summary>
public sealed class AutostartServiceTests
{
    [Theory]
    [InlineData("\"C:\\App\\DayNight.exe\" --silent", "C:\\App\\DayNight.exe")]
    [InlineData("\"C:\\My App\\DayNight.exe\"", "C:\\My App\\DayNight.exe")]
    [InlineData("C:\\App\\DayNight.exe --silent", "C:\\App\\DayNight.exe")]
    [InlineData("C:\\App\\DayNight.exe", "C:\\App\\DayNight.exe")]
    [InlineData("   \"C:\\App\\DayNight.exe\" --silent   ", "C:\\App\\DayNight.exe")]
    public void ExtractExePathParsesQuotedAndBareValues(string value, string expected)
        => Assert.Equal(expected, AutostartService.ExtractExePath(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"")]
    [InlineData("\"\"")]
    public void ExtractExePathReturnsNullForInvalidValues(string? value)
        => Assert.Null(AutostartService.ExtractExePath(value));

    /// <summary>登记当前进程路径（带 --silent 参数、带引号）应识别为本程序。</summary>
    [Fact]
    public void PointsToCurrentExeMatchesOwnPathWithArgs()
        => Assert.True(AutostartService.PointsToCurrentExe($"\"{Environment.ProcessPath}\" --silent"));

    /// <summary>Windows 路径大小写不敏感：全大写的本程序路径仍应匹配。</summary>
    [Fact]
    public void PointsToCurrentExeMatchesOwnPathIgnoringCase()
        => Assert.True(AutostartService.PointsToCurrentExe($"\"{Environment.ProcessPath?.ToUpperInvariant()}\" --silent"));

    [Fact]
    public void PointsToCurrentExeRejectsForeignPath()
        => Assert.False(AutostartService.PointsToCurrentExe("\"C:\\Somewhere Else\\DayNight.exe\" --silent"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\"\" --silent")]
    public void PointsToCurrentExeRejectsBlankValues(string? value)
        => Assert.False(AutostartService.PointsToCurrentExe(value));

    /// <summary>含非法字符的路径不应抛出异常，按不匹配处理。</summary>
    [Fact]
    public void PointsToCurrentExeRejectsInvalidPath()
        => Assert.False(AutostartService.PointsToCurrentExe("\"C:\\b<a>d\\DayNight.exe\" --silent"));
}
