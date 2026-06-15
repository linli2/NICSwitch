/// 配置文件管理
/// config.json 位置: %APPDATA%/NICSwitch/config.json
/// JSON 使用小写字段名（兼容旧版 Rust 格式），不转义中文

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NICSwitch;

public class Config
{
    [JsonPropertyName("show_physical_only")]
    public bool ShowPhysicalOnly { get; set; } = false;

    [JsonPropertyName("auto_refresh_interval")]
    public int AutoRefreshInterval { get; set; } = 5;

    [JsonPropertyName("profiles")]
    public List<Profile> Profiles { get; set; } = new();
}

public class Profile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("actions")]
    public List<ProfileAction> Actions { get; set; } = new();
}

public class ProfileAction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";
}

public static class ConfigManager
{
    private static readonly string ConfigDir;
    private static readonly string ConfigPath;
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true, // 读入时兼容大小写（新旧格式都能读）
    };
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 保留中文，不转义
        // [JsonPropertyName] 确保写入小写字段名
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
            var cfg = JsonSerializer.Deserialize<Config>(json, ReadOptions);
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
            var json = JsonSerializer.Serialize(config, WriteOptions);
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