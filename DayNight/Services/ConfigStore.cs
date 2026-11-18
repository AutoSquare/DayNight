using System.Text.Json;
using DayNight.Models;

namespace DayNight.Services;

/// <summary>config.json 读写（%APPDATA%\DayNight\config.json）。</summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DayNight");
    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public sealed class Config
    {
        public AppSettings Settings { get; set; } = new();
        public ScheduleConfig Schedule { get; set; } = new();
        public ManualState? Manual { get; set; }

        /// <summary>修复非法旧日程并撤销已失去对应关系的手动状态，不改变总开关。</summary>
        public bool RepairSchedule()
        {
            var repaired = Schedule.Repair();
            if (repaired || Manual is { } m && (m.HeldSegmentIndex < 0 || m.HeldSegmentIndex >= Schedule.Segments.Count))
            {
                Manual = null;
                return true;
            }
            return false;
        }
    }

    public static Config Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new Config();
            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<Config>(json, Options) ?? new Config();
            if (config.RepairSchedule())
            {
                var backup = ConfigPath + $".before-schedule-repair-{DateTime.Now:yyyyMMddHHmmssfff}.bak";
                File.Copy(ConfigPath, backup);
                Save(config);
                LogService.Warn($"已修复旧版非法时段并清除失效手动保持，原配置备份：{backup}");
            }
            return config;
        }
        catch (Exception ex)
        {
            LogService.Error("读取配置失败，使用默认配置", ex);
            return new Config();
        }
    }

    /// <summary>原子保存配置；失败返回 false，不覆盖原有配置。</summary>
    public static bool Save(Config config)
    {
        var temporary = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(temporary, JsonSerializer.Serialize(config, Options));
            File.Move(temporary, ConfigPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error("保存配置失败", ex);
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { /* 下次保存使用独立临时文件，不影响原配置。 */ }
        }
    }
}
