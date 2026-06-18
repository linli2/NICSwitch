/// 网卡管理模块
/// 通过 netsh 命令枚举和切换 Windows 网卡

using System.Diagnostics;

namespace NICSwitch;

public enum NicState
{
    Enabled,
    Disabled,
    Unknown,
}

public record NetworkAdapter
{
    public string Name { get; init; } = "";
    public NicState State { get; init; } = NicState.Unknown;
    public bool IsHardware { get; init; } = false;
}

public static class NicManager
{
    /// 获取所有网卡
    public static List<NetworkAdapter> ListAdapters()
    {
        var (exitCode, stdout, stderr) = RunNetsh("interface show interface");
        if (exitCode != 0)
        {
            Logger.Error($"netsh list 失败(exit={exitCode}): {stderr}");
            return new();
        }
        return ParseNetshOutput(stdout);
    }

    /// 启用网卡
    public static bool EnableAdapter(string name)
    {
        Logger.Info($"启用网卡: {name}");
        var (exitCode, _, stderr) = RunNetsh($"interface set interface name=\"{name}\" admin=ENABLED");
        if (exitCode == 0) return true;

        var errMsg = stderr.ToLower();
        if (errMsg.Contains("access is denied") || errMsg.Contains("拒绝访问"))
        {
            Logger.Error("需要管理员权限！请以管理员身份运行");
        }
        else
        {
            Logger.Error($"启用失败(exit={exitCode}): {stderr.Trim()}");
        }
        return false;
    }

    /// 禁用网卡
    public static bool DisableAdapter(string name)
    {
        Logger.Info($"禁用网卡: {name}");
        var (exitCode, _, stderr) = RunNetsh($"interface set interface name=\"{name}\" admin=DISABLED");
        if (exitCode == 0) return true;

        var errMsg = stderr.ToLower();
        if (errMsg.Contains("access is denied") || errMsg.Contains("拒绝访问"))
        {
            Logger.Error("需要管理员权限！请以管理员身份运行");
        }
        else
        {
            Logger.Error($"禁用失败(exit={exitCode}): {stderr.Trim()}");
        }
        return false;
    }

    /// 切换网卡启用/禁用
    public static bool ToggleAdapter(string name, NicState currentState)
    {
        return currentState switch
        {
            NicState.Enabled => DisableAdapter(name),
            NicState.Disabled => EnableAdapter(name),
            _ => false,
        };
    }

    // ── 内部 ──────────────────────────────────────

    public static (int exitCode, string stdout, string stderr) RunNetsh(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                // 使用系统默认编码（中文 Windows 为 GBK），
                // 不要硬编码 UTF-8，否则中文输出会乱码
                StandardOutputEncoding = System.Text.Encoding.Default,
                StandardErrorEncoding = System.Text.Encoding.Default,
            };

            using var proc = Process.Start(psi)!;
            proc.WaitForExit(10000); // 10s 超时

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            return (proc.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (-1, "", $"异常: {ex.Message}");
        }
    }

    /// 解析 netsh interface show interface 输出
    private static List<NetworkAdapter> ParseNetshOutput(string output)
    {
        var adapters = new List<NetworkAdapter>();
        bool started = false;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            // 分隔线
            if (trimmed.StartsWith("---"))
            {
                started = true;
                continue;
            }

            if (!started) continue;

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 4) continue;

            // Admin State      (parts[0])
            var adminStateStr = parts[0].ToLowerInvariant();
            var state = adminStateStr switch
            {
                "enabled" or "已启用" or "已啟用" => NicState.Enabled,
                "disabled" or "已禁用" or "已停用" => NicState.Disabled,
                _ => NicState.Unknown,
            };

            // Interface Name (parts[3..])
            var name = string.Join(" ", parts[3..]);

            // Type (parts[2])
            var nicType = parts.Length > 2 ? parts[2] : "";
            var isHardware = nicType.Equals("Dedicated", StringComparison.OrdinalIgnoreCase)
                          || nicType == "专用"
                          || nicType == "專用";

            adapters.Add(new NetworkAdapter
            {
                Name = name,
                State = state,
                IsHardware = isHardware,
            });
        }

        return adapters;
    }
}