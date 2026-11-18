namespace DayNight.Services;

/// <summary>轻量滚动文件日志：按天一个文件，写入 %APPDATA%\DayNight\logs。</summary>
public static class LogService
{
    private static readonly object Gate = new();
    private static string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DayNight", "logs");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message} :: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(_dir);
                var file = Path.Combine(_dir, $"{DateOnly.FromDateTime(DateTime.Now):yyyyMMdd}.log");
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不应影响主流程
        }
    }
}
