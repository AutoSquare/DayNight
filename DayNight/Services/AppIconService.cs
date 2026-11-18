using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DayNight.Services;

/// <summary>提供与标题栏显示缩放匹配的既有 Logo 位图。</summary>
public static class AppIconService
{
    private static readonly int[] TitleBarPixelSizes = [20, 25, 30, 35, 40, 50, 60, 64, 128, 256];

    /// <summary>按 20 DIP 标题栏图标的物理像素需求选择位图，调用者负责释放。</summary>
    /// <param name="renderScaling">窗口当前的显示缩放倍率。</param>
    /// <returns>像素尺寸不小于显示需求的最小可用位图。</returns>
    public static Bitmap CreateTitleBarBitmap(double renderScaling)
    {
        var requiredPixels = 20 * (double.IsFinite(renderScaling) && renderScaling > 0 ? renderScaling : 1);
        var size = TitleBarPixelSizes.FirstOrDefault(value => value >= requiredPixels, 256);
        using var stream = AssetLoader.Open(new Uri($"avares://DayNight/Assets/daynight-{size}.png"));
        return new Bitmap(stream);
    }
}
