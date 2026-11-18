namespace DayNight.Models;

/// <summary>日程中的一段：从 <see cref="StartTime"/> 起，到下一段起始时刻结束。</summary>
public sealed class ThemeSegment
{
    /// <summary>起始时刻（TimeOnly 的 JSON 序列化 .NET 8 原生支持）。</summary>
    public TimeOnly StartTime { get; set; } = new(0, 0);

    /// <summary>该时段绑定的主题（.theme 文件全路径）。</summary>
    public string ThemeId { get; set; } = string.Empty;

    public ThemeSegment() { }

    public ThemeSegment(TimeOnly start, string themeId)
    {
        StartTime = start;
        ThemeId = themeId;
    }
}
