using System.Reflection;
using System.Text;
using DayNight.Services;
using DayNight.Models;
using Xunit;

namespace DayNight.Tests;

/// <summary>真实主题格式的名称、编码与预览回归测试。</summary>
public sealed class ThemeCatalogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DayNight-tests", Guid.NewGuid().ToString("N"));

    /// <summary>建立隔离的主题样本目录。</summary>
    public ThemeCatalogTests() => Directory.CreateDirectory(_directory);

    /// <summary>兼容节的编码名称不能覆盖主节名称。</summary>
    [Fact]
    public void CompatibilitySectionsDoNotOverwriteName()
    {
        var theme = Parse("[Theme]\nDisplayName=日间主题\n[Theme.W]\nDisplayName=+ZeWV9E47mJg-", Encoding.Unicode);
        Assert.Equal("日间主题", theme.Name);
    }

    /// <summary>Windows 中文 ANSI 文件的名称与中文壁纸路径均可读。</summary>
    [Fact]
    public void AnsiThemePreservesChineseNameAndWallpaper()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var wallpaper = Path.Combine(_directory, "壁纸.png");
        File.WriteAllBytes(wallpaper, [1]);
        var theme = Parse($"[Theme]\nDisplayName=日间主题\n[Control Panel\\Desktop]\nWallpaper={wallpaper}", Encoding.GetEncoding(936));
        Assert.Equal("日间主题", theme.Name);
        Assert.Equal(wallpaper, theme.WallpaperPath);
        Assert.Equal(wallpaper, ThemeFileContent.Parse(theme.FilePath).Wallpaper);
    }

    /// <summary>精确读取 Wallpaper 键，不将 WallpaperStyle 当作图片。</summary>
    [Fact]
    public void SlideshowCountsDistinctExistingItems()
    {
        File.WriteAllBytes(Path.Combine(_directory, "one.jpg"), [1]);
        File.WriteAllBytes(Path.Combine(_directory, "two.jpg"), [1]);
        var theme = Parse("[Theme]\nDisplayName=相册\n[Control Panel\\Desktop]\nWallpaperStyle=10\nWallpaper=one.jpg\n[Slideshow]\nItem0Path=one.jpg\nItem1Path=two.jpg\nItem2Path=one.jpg\nItem3Path=missing.jpg", Encoding.UTF8);
        Assert.Equal(2, theme.WallpaperCount);
        Assert.Equal(Path.Combine(_directory, "one.jpg"), theme.WallpaperPath);
    }

    /// <summary>内置主题从资源加载中文显示名。</summary>
    [Fact]
    public void BuiltInThemeResolvesResourceName()
    {
        var theme = Parse("[Theme]\nDisplayName=@%SystemRoot%\\System32\\themeui.dll,-2060", Encoding.Unicode);
        Assert.NotEqual("sample", theme.Name);
        Assert.DoesNotContain("@", theme.Name);
    }

    private ThemeInfo Parse(string text, Encoding encoding)
    {
        var path = Path.Combine(_directory, "sample.theme");
        File.WriteAllText(path, text, encoding);
        return (ThemeInfo)typeof(ThemeCatalogService).GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [path])!;
    }

    /// <summary>移除本测试创建的临时目录。</summary>
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
