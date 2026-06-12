/// NICSwitch - Windows 系统托盘网卡切换工具
/// C# .NET 9 原生实现

using NICSwitch;

// ── 高 DPI 支持 ───────────────────────────────────
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

// ── 初始化日志 ───────────────────────────────────
Logger.Info("═══════════════════════════════════════");
Logger.Info("NICSwitch 启动");

var adapters = NicManager.ListAdapters();
Logger.Info($"发现 {adapters.Count} 个网卡");

// ── 运行托盘图标 ─────────────────────────────────
// 会在 Application.Run() 中阻塞，直到退出
using var tray = new TrayManager();

// 消息循环（WinForms 自动处理）
Application.Run();

Logger.Info("程序退出");
