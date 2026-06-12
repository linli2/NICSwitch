/// 配置文件管理
/// config.json 位置: %APPDATA%/NICSwitch/config.json

using System.Text.Json;

namespace NICSwitch;

public class Config
{
    public bool ShowPhysicalOnly { get; set; } = false;
    public int AutoRefreshInterval { get; set; } = 5;
    public List<Profile> Profiles { get; set; } = new();
}

public class Profile
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<ProfileAction> Actions { get; set; } = new();
}

public class ProfileAction
{
    public string Name { get; set; } = "";
    public string Action { get; set; } = ""; // "enable" or "disable"
}

public static class ConfigManager
{
    private static readonly string ConfigDir;
    private static readonly string ConfigPath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    static ConfigManager()
    {
        ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NICSwitch");
        Directory.CreateDirectory(ConfigDir);
        ConfigPath = Path.Combine(ConfigDir, "config.json");
    }

    public static string ConfigDirectory => ConfigDir;

    public static Config Load()
    {
        if (!File.Exists(ConfigPath))
        {
            Logger.Info("配置文件不存在，创建默认配置");
            var cfg = new Config();
            AddExampleProfiles(cfg);
            Save(cfg);
            return cfg;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var cfg = JsonSerializer.Deserialize<Config>(json);
            if (cfg != null) return cfg;
        }
        catch (Exception ex)
        {
            Logger.Error($"配置文件解析失败: {ex.Message}");
        }

        var fallback = new Config();
        Save(fallback);
        return fallback;
    }

    public static void Save(Config config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            Logger.Error($"保存配置失败: {ex.Message}");
        }
    }

    private static void AddExampleProfiles(Config config)
    {
        config.Profiles = new()
        {
            new Profile
            {
                Name = "🔀 切换到内网",
                Description = "关闭 WLAN，启用 以太网",
                Actions = new()
                {
                    new() { Name = "WLAN", Action = "disable" },
                    new() { Name = "以太网", Action = "enable" },
                }
            },
            new Profile
            {
                Name = "🔀 切换到无线",
                Description = "关闭 以太网，启用 WLAN",
                Actions = new()
                {
                    new() { Name = "以太网", Action = "disable" },
                    new() { Name = "WLAN", Action = "enable" },
                }
            },
        };
    }
}