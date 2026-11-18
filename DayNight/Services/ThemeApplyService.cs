using System.Globalization;

namespace DayNight.Services;

/// <summary>主题的预览与验证信息；整套主题由 Windows 主题管理器负责应用。</summary>
public sealed record ThemeFileContent
{
    /// <summary>主题显示名称或文件名。</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>主题声明的壁纸路径。</summary>
    public string? Wallpaper { get; init; }

    /// <summary>主题声明的系统明暗模式；未声明时为 null。</summary>
    public bool? SystemUsesLightTheme { get; init; }

    /// <summary>主题声明的应用明暗模式；未声明时为 null。</summary>
    public bool? AppsUseLightTheme { get; init; }

    /// <summary>固定强调色；自动取色主题不以该值验证最终颜色。</summary>
    public uint? ColorizationColor { get; init; }

    /// <summary>是否根据壁纸自动选择强调色。</summary>
    public bool AutoColorization { get; init; }

    /// <summary>按节读取 ini（key 不区分大小写）。</summary>
    /// <param name="filePath">主题文件路径。</param>
    /// <returns>用于预览和应用后核对的主题信息。</returns>
    public static ThemeFileContent Parse(string filePath)
    {
        var result = new ThemeFileContent { DisplayName = System.IO.Path.GetFileNameWithoutExtension(filePath) };
        string? section = null;

        foreach (var raw in ThemeCatalogService.ReadThemeLines(filePath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith('['))
            {
                section = line.Trim('[', ']');
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq < 0 || section is null) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            switch (section.ToUpperInvariant())
            {
                case "THEME":
                    if (key.Equals("DisplayName", StringComparison.OrdinalIgnoreCase) &&
                        !value.StartsWith('@') && value.Length > 0)
                        result = result with { DisplayName = value };
                    break;
                case "CONTROL PANEL\\DESKTOP":
                    if (key.Equals("Wallpaper", StringComparison.OrdinalIgnoreCase))
                        result = result with { Wallpaper = Environment.ExpandEnvironmentVariables(value) };
                    break;
                case "VISUALSTYLES":
                    bool? light = value.Equals("Light", StringComparison.OrdinalIgnoreCase) ? true
                        : value.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? false : null;
                    if (key.Equals("SystemMode", StringComparison.OrdinalIgnoreCase))
                        result = result with { SystemUsesLightTheme = light };
                    if (key.Equals("AppMode", StringComparison.OrdinalIgnoreCase))
                        result = result with { AppsUseLightTheme = light };
                    if (key.Equals("AutoColorization", StringComparison.OrdinalIgnoreCase))
                        result = result with { AutoColorization = value == "1" };
                    if (key.Equals("ColorizationColor", StringComparison.OrdinalIgnoreCase) &&
                        uint.TryParse(value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value,
                            NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var color))
                        result = result with { ColorizationColor = color };
                    break;
            }
        }
        return result;
    }

    /// <summary>核对已声明的明暗模式与固定强调色，缺失的系统读数不视为成功。</summary>
    /// <param name="systemLight">系统明暗注册表值。</param>
    /// <param name="appsLight">应用明暗注册表值。</param>
    /// <param name="color">系统实际强调色。</param>
    /// <returns>所有已声明的外观设置均生效时为 true。</returns>
    public bool MatchesAppearance(int? systemLight, int? appsLight, uint? color) =>
        (SystemUsesLightTheme is null || systemLight == (SystemUsesLightTheme.Value ? 1 : 0)) &&
        (AppsUseLightTheme is null || appsLight == (AppsUseLightTheme.Value ? 1 : 0)) &&
        (AutoColorization || ColorizationColor is null || color == ColorizationColor);
}

/// <summary>通过 Windows 主题管理器应用整套主题，并核对实际系统状态。</summary>
public sealed class ThemeApplyService
{
    private static readonly object ApplyLock = new();

    /// <summary>应用主题并验证路径、系统明暗、应用明暗与固定强调色。</summary>
    /// <param name="themeFilePath">要应用的完整主题文件。</param>
    /// <returns>Windows 应用成功且可验证状态符合主题定义时为 true。</returns>
    public bool Apply(string themeFilePath)
    {
        lock (ApplyLock)
        {
            try
            {
                var path = ThemeCatalogService.NormalizePath(themeFilePath);
                if (!File.Exists(path)) throw new FileNotFoundException("主题文件不存在", path);
                var expected = ThemeFileContent.Parse(path);
                Exception? failure = null;
                var worker = new Thread(() =>
                {
                    try { WindowsThemeManager.Apply(path); }
                    catch (Exception ex) { failure = ex; }
                }) { IsBackground = true, Name = "DayNight.ThemeManager" };
                worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
                worker.Join();
                if (failure is not null) throw new InvalidOperationException("Windows 主题管理器应用失败", failure);

                // 主题管理器会异步广播设置变化；等待读取到真实结果，最多五秒。
                var deadline = DateTime.UtcNow.AddSeconds(5);
                do
                {
                    if (MatchesAppliedTheme(path, expected))
                    {
                        LogService.Info($"整套主题已应用并验证：{path}；系统亮色={expected.SystemUsesLightTheme}；应用亮色={expected.AppsUseLightTheme}");
                        return true;
                    }
                    Thread.Sleep(100);
                } while (DateTime.UtcNow < deadline);
                LogService.Error($"主题应用后验证失败，当前主题、明暗模式或强调色不匹配：{path}");
                return false;
            }
            catch (Exception ex)
            {
                LogService.Error("整套主题应用失败", ex);
                return false;
            }
        }
    }

    private static bool MatchesAppliedTheme(string path, ThemeFileContent expected)
    {
        var actualPath = new ThemeCatalogService().GetNormalizedCurrentThemePath();
        using var personalize = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        using var dwm = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
        var color = dwm?.GetValue("ColorizationColor") is int value ? unchecked((uint)value) : (uint?)null;
        return string.Equals(path, actualPath, StringComparison.OrdinalIgnoreCase)
            && expected.MatchesAppearance(personalize?.GetValue("SystemUsesLightTheme") as int?,
                personalize?.GetValue("AppsUseLightTheme") as int?, color);
    }
}
