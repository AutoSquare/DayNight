using DayNight.Services;

namespace DayNight;

/// <summary>在真实 Windows 上验证浅色、深色整套主题应用，并恢复运行前的主题。</summary>
public static class SmokeTestRunner
{
    /// <summary>依次应用内置浅色和深色主题，核对路径、明暗模式与固定强调色。</summary>
    /// <returns>全部验证和恢复均成功时返回 0，否则返回 1。</returns>
    public static int Run()
    {
        var catalog = new ThemeCatalogService();
        var apply = new ThemeApplyService();
        var original = catalog.GetNormalizedCurrentThemePath();
        if (!File.Exists(original))
        {
            Console.WriteLine("无法确定恢复用的原主题，未执行切换测试。");
            return 1;
        }
        var result = 0;
        try
        {
            var themes = catalog.Scan();
            Console.WriteLine($"主题库：{themes.Count} 套");
            foreach (var file in new[] { "aero.theme", "dark.theme" })
            {
                var path = Path.Combine(ThemeCatalogService.SystemThemesDir, file);
                var success = apply.Apply(path);
                Console.WriteLine($"{file}：整套主题应用与状态验证 {(success ? "通过" : "失败")}");
                if (!success) result = 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"冒烟失败：{ex.Message}");
            result = 1;
        }
        finally
        {
            var restored = apply.Apply(original);
            Console.WriteLine($"恢复原主题：{(restored ? "通过" : "失败")}（{original}）");
            if (!restored) result = 1;
        }
        return result;
    }
}
