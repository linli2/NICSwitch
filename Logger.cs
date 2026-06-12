/// 文件日志模块
/// 写入 %APPDATA%/NICSwitch/nicswitch.log

namespace NICSwitch;

public static class Logger
{
    private static readonly string LogPath;
    private static readonly object Lock = new();

    static Logger()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NICSwitch");
        Directory.CreateDirectory(dir);
        LogPath = Path.Combine(dir, "nicswitch.log");

        // 日志轮转（超过1MB重命名）
        if (File.Exists(LogPath))
        {
            var fi = new FileInfo(LogPath);
            if (fi.Length > 1_024 * 1_024)
            {
                var old = LogPath + ".old";
                try { File.Move(LogPath, old, overwrite: true); } catch { }
            }
        }

        Info("═══════════════════════════════════════");
        Info($"NICSwitch v1.0 启动 (C# .NET)");
        Info($"日志目录: {dir}");
        Info("═══════════════════════════════════════");
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Debug(string msg) => Write("DEBUG", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    private static void Write(string level, string msg)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var pid = Environment.ProcessId;
        var line = $"[{timestamp}] [{level}] [PID:{pid}] {msg}";

        lock (Lock)
        {
            try
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch
            {
                // 日志写失败也算了
            }
        }
    }
}