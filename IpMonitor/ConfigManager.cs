using System.Text.Json;

namespace IpMonitor;

public class AppConfig
{
    public string ExpectedIp { get; set; } = "";
    public int RefreshIntervalSec { get; set; } = 30;
    public bool AutoSwitchToDirect { get; set; } = true;   // IP变化时自动切DIRECT
    public string ClashApiUrl { get; set; } = "";          // 自动检测填入
    public string ClashApiSecret { get; set; } = "";       // 自动检测填入
    public string ClashApiSource { get; set; } = "";       // 来源描述(诊断用)
}

public static class ConfigManager
{
    private static readonly string ConfigPath =
        Path.Combine(AppContext.BaseDirectory, "config.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options);
                if (cfg != null) return cfg;
            }
        }
        catch { }
        return new AppConfig();
    }

    public static void Save(AppConfig cfg)
    {
        try
        {
            var json = JsonSerializer.Serialize(cfg, Options);
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
}
