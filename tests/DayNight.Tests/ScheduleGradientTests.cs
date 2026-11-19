using Avalonia.Media;
using DayNight.Models;
using DayNight.ViewModels;
using Xunit;

namespace DayNight.Tests;

/// <summary>时间轴各段在切换边界、窄段与午夜两侧的明暗可辨性。</summary>
public class ScheduleGradientTests
{
    /// <summary>最亮夜色与最暗日色也有足够反差，过渡只发生在段内。</summary>
    [Fact]
    public void LightAndDarkBands_KeepAtLeastSevenToOneContrast()
    {
        var schedule = new ScheduleConfig();
        var light = ScheduleGradient.ForSlice(schedule, 0, true, 0, 1440);
        var dark = ScheduleGradient.ForSlice(schedule, 0, false, 0, 1440);
        var darkestDay = light.GradientStops.Min(s => Luminance(s.Color));
        var brightestNight = dark.GradientStops.Max(s => Luminance(s.Color));
        Assert.True((darkestDay + 0.05) / (brightestNight + 0.05) >= 7);
        Assert.Equal(0, light.StartPoint.Point.X);
        Assert.Equal(1, dark.EndPoint.Point.X);
        Assert.Equal(6, light.GradientStops.Count);
        Assert.Equal(6, light.GradientStops.Select(s => s.Color).Distinct().Count());
    }

    /// <summary>一天从黎明走到黄昏：亮族首尾两端都落在暖端，中间才是蓝天。</summary>
    [Fact]
    public void DayBand_StartsAndEndsWarmWithBlueInBetween()
    {
        var schedule = new ScheduleConfig();
        var stops = ScheduleGradient.ForSlice(schedule, 0, true, 0, 1440).GradientStops;
        static bool Warm(Color color) => color.R > color.B + 12;
        Assert.True(Warm(stops[0].Color), "黎明应偏暖");
        Assert.True(Warm(stops[^1].Color), "黄昏应偏暖");
        Assert.Contains(stops, s => s.Color.B > s.Color.R + 12);
    }

    /// <summary>23:30 的半小时短段与零点后的长段共用整套夜色。</summary>
    [Fact]
    public void OvernightSlices_KeepNightFillRightUpToBothBoundaries()
    {
        var schedule = new ScheduleConfig { Segments = [new(new TimeOnly(7, 0), "day"), new(new TimeOnly(23, 30), "night")] };
        var morning = ScheduleGradient.ForSlice(schedule, 1, false, 0, 420);
        var evening = ScheduleGradient.ForSlice(schedule, 1, false, 1410, 1440);
        static double Phase(LinearGradientBrush brush, double x) =>
            (x - brush.StartPoint.Point.X) / (brush.EndPoint.Point.X - brush.StartPoint.Point.X);
        Assert.Equal(0, Phase(evening, 0));
        Assert.Equal(30d / 450, Phase(evening, 1), 8);
        Assert.Equal(Phase(evening, 1), Phase(morning, 0), 8);
        Assert.Equal(1, Phase(morning, 1), 8);
        Assert.Equal(morning.GradientStops.Select(s => s.Color), evening.GradientStops.Select(s => s.Color));
    }

    /// <summary>同明暗的多个主题各自换一档色相，同一主题每次都稳定。</summary>
    [Fact]
    public void SameLightnessThemes_GetStableDistinctHues()
    {
        var shifts = new HashSet<double>();
        foreach (var key in new[] { @"C:\themes\morning.json", @"C:\themes\forest.json", @"C:\themes\ocean.json", @"C:\themes\mono.json" })
        {
            shifts.Add(ScheduleGradient.HueShift(key));
            Assert.Equal(ScheduleGradient.HueShift(key), ScheduleGradient.HueShift(key));
        }
        Assert.True(shifts.Count > 1, "多个同明暗主题不应共用同一套配色");
        Assert.All(shifts, shift => Assert.InRange(Math.Abs(shift), 0, 18));

        var forest = @"C:\themes\forest.json";
        var ocean = @"C:\themes\ocean.json";
        var a = ScheduleGradient.Palette(true, forest);
        var b = ScheduleGradient.Palette(true, ocean);
        Assert.Equal(ScheduleGradient.HueShift(forest) != ScheduleGradient.HueShift(ocean), !a.SequenceEqual(b));

        var plain = ScheduleGradient.Palette(true, null);
        Assert.Equal(6, plain.Length);
        Assert.Equal(3, ScheduleGradient.Palette(false, null).Length);
    }

    /// <summary>无论两个主题各落在哪一档，最暗日色与最亮夜色都保持 7:1。</summary>
    [Fact]
    public void DayAndNightBands_KeepContrastAcrossEveryHueTint()
    {
        var schedule = new ScheduleConfig();
        var shifts = new HashSet<double>();
        var dayDarkest = new List<double>();
        var nightBrightest = new List<double>();
        for (var i = 0; shifts.Count < 5 && i < 5000; i++)
        {
            var key = $"theme-{i}";
            var shift = ScheduleGradient.HueShift(key);
            if (!shifts.Add(shift)) continue;
            dayDarkest.Add(ScheduleGradient.ForSlice(schedule, 0, true, 0, 1440, key).GradientStops.Min(s => Luminance(s.Color)));
            nightBrightest.Add(ScheduleGradient.ForSlice(schedule, 0, false, 0, 1440, key).GradientStops.Max(s => Luminance(s.Color)));
        }
        Assert.Equal(5, shifts.Count);
        var ratio = (dayDarkest.Min() + 0.05) / (nightBrightest.Max() + 0.05);
        Assert.True(ratio >= 7, $"最差组合仅 {ratio:F2}:1");
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
}
