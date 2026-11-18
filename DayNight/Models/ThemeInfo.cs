namespace DayNight.Models;

/// <summary>主题库中的一套主题。</summary>
public sealed class ThemeInfo
{
    /// <summary>.theme 文件全路径，作为唯一标识。</summary>
    public required string FilePath { get; init; }

    /// <summary>显示名（取自主题文件的 DisplayName，缺省用文件名）。</summary>
    public required string Name { get; init; }

    /// <summary>壁纸文件路径（可能为空，如纯色/幻灯片主题）。</summary>
    public string? WallpaperPath { get; init; }

    /// <summary>预览使用的应用明暗模式，未声明时回退到主题的系统模式。</summary>
    public bool? PreviewUsesLightTheme { get; init; }

    /// <summary>主题内声明的壁纸数量（幻灯片主题取目录图片数，普通主题为 1 或 0）。</summary>
    public int WallpaperCount { get; init; }

    /// <summary>主题是否声明幻灯片。</summary>
    public bool IsSlideshow { get; init; }

    /// <summary>主题是否使用 Windows 聚焦。</summary>
    public bool IsSpotlight { get; init; }

    /// <summary>供主题卡片与规则共用的壁纸说明。</summary>
    public string WallpaperDescription => IsSpotlight ? "Windows 聚焦"
        : IsSlideshow ? (WallpaperCount > 0 ? $"{WallpaperCount} 张壁纸 · 幻灯片" : "幻灯片壁纸")
        : WallpaperCount > 0 ? $"{WallpaperCount} 张壁纸" : "无壁纸预览";

    /// <summary>是否为当前系统正在使用的主题。</summary>
    public bool IsCurrent { get; set; }
}
