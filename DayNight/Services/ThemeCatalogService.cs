using System.Runtime.InteropServices;
using System.Text;
using DayNight.Models;

namespace DayNight.Services;

/// <summary>扫描已保存和系统内置主题，按 INI 节读取名称及预览。</summary>
public sealed class ThemeCatalogService
{
    private static readonly string UserThemesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Themes");

    /// <summary>系统内置主题目录。</summary>
    public static readonly string SystemThemesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Resources", "Themes");

    /// <summary>枚举已保存主题，并保留当前正在使用的自定义主题。</summary>
    public List<ThemeInfo> Scan()
    {
        var current = GetNormalizedCurrentThemePath();
        var files = EnumerateThemeFiles().ToList();
        if (File.Exists(current)) files.Add(current);
        var list = new List<ThemeInfo>();
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var isCurrent = string.Equals(NormalizePath(file), current, StringComparison.OrdinalIgnoreCase);
            // Custom.theme 是 Windows 的未保存快照，不是另一套已安装主题。
            if (!isCurrent && string.Equals(NormalizePath(file),
                    NormalizePath(Path.Combine(UserThemesDir, "Custom.theme")), StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var info = Parse(file);
                info.IsCurrent = isCurrent;
                list.Add(info);
            }
            catch (Exception ex)
            {
                LogService.Warn($"解析主题文件失败 {file}: {ex.Message}");
            }
        }
        return list;
    }

    /// <summary>读取注册表中当前主题的规范化路径。</summary>
    public string GetNormalizedCurrentThemePath()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes");
            return key?.GetValue("CurrentTheme") is string value ? NormalizePath(value) : string.Empty;
        }
        catch (Exception ex)
        {
            LogService.Error("读取当前主题注册表失败", ex);
            return string.Empty;
        }
    }

    private static IEnumerable<string> EnumerateThemeFiles()
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
        foreach (var dir in new[] { UserThemesDir, SystemThemesDir })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.theme", options).Order(StringComparer.OrdinalIgnoreCase))
                yield return file;
        }
    }

    // UTF-16/UTF-8 BOM 由 StreamReader 识别；无 BOM 先校验 UTF-8，再回退到 Windows ANSI。
    internal static IEnumerable<string> ReadThemeLines(string filePath)
    {
        try
        {
            return File.ReadAllLines(filePath, new UTF8Encoding(false, true));
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return File.ReadAllLines(filePath, Encoding.GetEncoding((int)GetACP()));
        }
    }

    /// <summary>按主题库的名称规则读取单个主题，可在主题库尚未加载时用于切换通知。</summary>
    /// <param name="filePath">已应用主题的文件路径。</param>
    /// <returns>主题显示名，文件无法读取时为空。</returns>
    public static string? GetDisplayName(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try { return Parse(filePath).Name; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            LogService.Warn($"读取主题显示名失败：{ex.Message}");
            return null;
        }
    }

    private static ThemeInfo Parse(string filePath)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? values = null;
        foreach (var raw in ReadThemeLines(filePath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var section = line[1..^1];
                if (!sections.TryGetValue(section, out values))
                    sections[section] = values = new(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq > 0 && values is not null) values[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        string Get(string section, string key) =>
            sections.TryGetValue(section, out var entries) && entries.TryGetValue(key, out var value) ? value : "";
        string? ImagePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var expanded = Environment.ExpandEnvironmentVariables(value.Trim('"'));
            var full = Path.GetFullPath(expanded, Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            return File.Exists(full) ? full : null;
        }

        var name = Get("Theme", "DisplayName");
        if (name.StartsWith('@'))
        {
            var buffer = new StringBuilder(1024);
            name = SHLoadIndirectString(Environment.ExpandEnvironmentVariables(name), buffer, (uint)buffer.Capacity, IntPtr.Zero) == 0
                ? buffer.ToString() : "";
        }
        if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(filePath);
        var wallpaper = ImagePath(Get(@"Control Panel\Desktop", "Wallpaper"));
        var images = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var slideshow = sections.TryGetValue("Slideshow", out var slide);
        if (slideshow)
        {
            foreach (var (key, value) in slide!)
            {
                if (key.StartsWith("Item", StringComparison.OrdinalIgnoreCase) && key.EndsWith("Path", StringComparison.OrdinalIgnoreCase)
                    && ImagePath(value) is { } image) images.Add(image);
            }
        }
        if (wallpaper is not null) images.Add(wallpaper);
        wallpaper ??= images.FirstOrDefault();
        static bool? LightMode(string mode) => mode.ToUpperInvariant() switch
        {
            "LIGHT" => true,
            "DARK" => false,
            _ => null,
        };
        return new ThemeInfo
        {
            FilePath = filePath, Name = name, WallpaperPath = wallpaper,
            PreviewUsesLightTheme = LightMode(Get("VisualStyles", "AppMode")) ?? LightMode(Get("VisualStyles", "SystemMode")),
            WallpaperCount = images.Count, IsSlideshow = slideshow,
            IsSpotlight = Get(@"Control Panel\Desktop", "WindowsSpotlight") == "1",
        };
    }

    /// <summary>展开环境变量并规范化主题标识；空标识保留为空。</summary>
    /// <param name="path">主题路径。</param>
    /// <returns>可按忽略大小写方式比较的绝对路径。</returns>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)).TrimEnd('\\'); }
        catch { return path; }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string source, StringBuilder output, uint length, IntPtr reserved);
}
