using Avalonia;
using Avalonia.Media;
using DayNight.Models;

namespace DayNight.ViewModels;

/// <summary>按完整时段铺设横向渐变，跨午夜时只绘制当天可见的部分。</summary>
internal static class ScheduleGradient
{
    /// <summary>同一天光族内允许的色相偏移档位（度），按主题标识取模选取。</summary>
    private const double HueStep = 6;

    /// <summary>色相偏移档位数量（含不动档），奇数保证有一档不偏色。</summary>
    private const int HueLevels = 5;

    // 亮族＝一整天的日光叙事：晨光暖奶油 → 上午天光 → 午后蓝天 → 暮色丁香 → 夕阳玫瑰 → 黄昏金橙。
    // 首末两端都在暖端，中间走过蓝天，读起来才是「从日出到日落」，而不是一段单色；
    // 后两档压在最后 30% 里，日落感才落在日段收尾处，而不是把整条带都染暖。
    private static readonly Color[] Light =
    [
        Color.Parse("#FFEDD0"), Color.Parse("#C6E2FB"), Color.Parse("#A9CCF0"),
        Color.Parse("#D0C4EA"), Color.Parse("#F2BCC4"), Color.Parse("#FFB867"),
    ];

    private static readonly double[] LightStops = [0, 0.28, 0.55, 0.70, 0.86, 1];

    // 暗族＝夜色：深夜海军蓝 → 靛蓝 → 将破晓的紫罗兰，白字始终足够清晰。
    private static readonly Color[] Dark = [Color.Parse("#1B2142"), Color.Parse("#2A2C5A"), Color.Parse("#382759")];

    private static readonly double[] DarkStops = [0, 0.55, 1];

    private static readonly Color[] Unknown = [Color.Parse("#38475C"), Color.Parse("#415168"), Color.Parse("#4B5C73")];

    private static readonly double[] UnknownStops = [0, 0.5, 1];

    /// <summary>以原始切换点作为渐变起点，使午夜前后的同一时段接续取色。</summary>
    internal static LinearGradientBrush ForSlice(ScheduleConfig schedule, int index, bool? light, int start, int end, string? themeKey = null)
    {
        var ruleStart = schedule.Segments[index].StartTime.ToTimeSpan().TotalMinutes;
        var ruleEnd = schedule.GetSegmentEndTime(index).ToTimeSpan().TotalMinutes;
        var duration = ruleEnd > ruleStart ? ruleEnd - ruleStart : 1440 + ruleEnd - ruleStart;
        var elapsed = (start - ruleStart + 1440) % 1440;
        var visible = end - start;
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(-elapsed / visible, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint((duration - elapsed) / visible, 0, RelativeUnit.Relative),
            GradientStops = BuildStops(light, themeKey)
        };
    }

    /// <summary>取同族基色，再按主题标识做小幅色相偏移，使同明暗的多个主题各自成色。</summary>
    internal static Color[] Palette(bool? light, string? themeKey)
    {
        var colors = BaseColors(light);
        var shift = HueShift(themeKey);
        return shift == 0 ? colors : colors.Select(color => RotateColor(color, shift)).ToArray();
    }

    /// <summary>由主题标识稳定地取一档色相偏移；未绑定主题（空标识）保持基色。</summary>
    internal static double HueShift(string? themeKey)
    {
        if (string.IsNullOrEmpty(themeKey)) return 0;
        // FNV-1a：与运行时字符串哈希无关，同一主题在任何进程里都落在同一档。
        var hash = 2166136261u;
        foreach (var ch in themeKey)
            hash = (hash ^ ch) * 16777619u;
        var level = (int)(hash % HueLevels) - HueLevels / 2;
        return level * HueStep;
    }

    private static Color[] BaseColors(bool? light) => light switch { true => Light, false => Dark, null => Unknown };

    private static double[] BaseOffsets(bool? light) => light switch { true => LightStops, false => DarkStops, null => UnknownStops };

    private static Color RotateColor(Color color, double degrees)
    {
        var hsl = color.ToHsl();
        var hue = (hsl.H + degrees + 360) % 360;
        var wanted = Luminance(color);
        var lightness = hsl.L;
        var candidate = new HslColor(hsl.A, hue, hsl.S, lightness).ToRgb();
        // 色相一变，感知亮度也跟着变；小幅回调明度把亮度锁在基色附近，日夜 7:1 才不会被色相漂移吃掉。
        for (var i = 0; i < 40 && Math.Abs(Luminance(candidate) - wanted) > wanted * 0.012 + 0.0015; i++)
        {
            lightness = Math.Clamp(lightness + (Luminance(candidate) > wanted ? -0.005 : 0.005), 0, 1);
            candidate = new HslColor(hsl.A, hue, hsl.S, lightness).ToRgb();
        }
        return candidate;
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private static GradientStops BuildStops(bool? light, string? themeKey)
    {
        var colors = Palette(light, themeKey);
        var offsets = BaseOffsets(light);
        var stops = new GradientStops();
        for (var i = 0; i < colors.Length; i++)
            stops.Add(new GradientStop(colors[i], offsets[i]));
        return stops;
    }
}
