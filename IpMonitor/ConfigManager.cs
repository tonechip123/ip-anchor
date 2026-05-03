using System.Text.Json;

namespace IpMonitor;

public class AppConfig
{
    public string ExpectedIp { get; set; } = "";
    public int RefreshIntervalSec { get; set; } = 1;  // 默认1秒实时监控
}

public static class ConfigManager
{
    /// <summary>
    /// 配置目录: %APPDATA%\IpAnchor\
    /// 这样exe同目录完全干净, 拷到别的电脑exe不带配置, 各自启动自动检测Clash
    /// </summary>
    public static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "IpAnchor");

    public static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly string LegacyConfigPath =
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
            // 迁移: 老版本(<=v1.0.1)的config.json在exe同目录, 启动时一次性挪到AppData
            MigrateLegacyConfig();

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
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(cfg, Options);
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }

    private static void MigrateLegacyConfig()
    {
        try
        {
            if (!File.Exists(LegacyConfigPath)) return;
            if (File.Exists(ConfigPath)) { TryDelete(LegacyConfigPath); return; }

            Directory.CreateDirectory(ConfigDir);
            File.Move(LegacyConfigPath, ConfigPath);
        }
        catch { }
    }

    private static void TryDelete(string path)
    {
        try
        {
            // 清除Hidden/ReadOnly后再删除
            var a = File.GetAttributes(path);
            if ((a & (FileAttributes.Hidden | FileAttributes.ReadOnly)) != 0)
                File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch { }
    }
}
