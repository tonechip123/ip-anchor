using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace IpMonitor.Monitor;

public class ClashApiInfo
{
    public string ApiUrl { get; set; } = "";
    public string Secret { get; set; } = "";
    public string Source { get; set; } = "";   // 来源描述(给用户看)
    public bool Reachable { get; set; }        // /version 真的能通吗
    public string? Error { get; set; }
}

/// <summary>
/// 自动识别本机 Clash 的 API URL 和 Secret.
/// 策略: 找运行中的Clash进程 -> 推断配置目录 -> 正则提取yaml里的external-controller/secret -> HTTP验证.
/// 软件可以拷贝到任何电脑, 无需手填.
/// </summary>
public static class ClashAutoDetect
{
    private static readonly string[] ProcessNames = new[]
    {
        "clash", "mihomo", "Clash for Windows",
        "clash-verge", "clash-verge-rev", "verge-mihomo",
        "ClashN", "clash-windows", "clash-win64"
    };

    private static readonly Regex YamlExternal =
        new(@"^\s*external-controller\s*:\s*['""]?([^'""\r\n#]+?)['""]?\s*(?:#.*)?$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private static readonly Regex YamlSecret =
        new(@"^\s*secret\s*:\s*['""]?([^'""\r\n#]*?)['""]?\s*(?:#.*)?$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>
    /// 自动探测. 验证连通后才返回. 如全部失败返回带Error的对象.
    /// </summary>
    public static async Task<ClashApiInfo> DetectAsync(CancellationToken ct)
    {
        var candidates = CollectCandidates();
        if (candidates.Count == 0)
            return new ClashApiInfo { Error = "未找到运行中的Clash进程" };

        using var http = CreateHttp();
        var lastError = "";

        foreach (var c in candidates)
        {
            try
            {
                var url = c.ApiUrl.TrimEnd('/') + "/version";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(c.Secret))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", c.Secret);
                using var resp = await http.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode)
                {
                    c.Reachable = true;
                    return c;
                }
                lastError = $"{c.ApiUrl} 返回 {(int)resp.StatusCode}";
            }
            catch (Exception ex) { lastError = ex.Message; }
        }

        // 全失败, 把第一个候选返回(带错误), 让用户在设置里看到
        var first = candidates[0];
        first.Error = lastError;
        return first;
    }

    /// <summary>
    /// 通过Clash进程 + 候选配置文件路径, 收集所有可能的(ApiUrl, Secret).
    /// </summary>
    private static List<ClashApiInfo> CollectCandidates()
    {
        var results = new List<ClashApiInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in ProcessNames)
        {
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch { continue; }

            foreach (var p in procs)
            {
                try
                {
                    string? exePath = null;
                    try { exePath = p.MainModule?.FileName; }
                    catch { /* 拒绝访问也不致命 */ }

                    foreach (var configPath in EnumerateConfigPaths(exePath))
                    {
                        if (!seen.Add(configPath)) continue;
                        var info = ParseConfigFile(configPath);
                        if (info != null) results.Add(info);
                    }
                }
                finally { p.Dispose(); }
            }
        }

        return results;
    }

    private static IEnumerable<string> EnumerateConfigPaths(string? exePath)
    {
        // CFW 绿色版/便携版: <exe-dir>/data/
        if (!string.IsNullOrEmpty(exePath))
        {
            var dir = Path.GetDirectoryName(exePath);
            if (!string.IsNullOrEmpty(dir))
            {
                yield return Path.Combine(dir, "data", "config.yaml");
                yield return Path.Combine(dir, "data", "cfw-settings.yaml");
                yield return Path.Combine(dir, "config.yaml");
                yield return Path.Combine(dir, "config.yml");

                var parent = Path.GetDirectoryName(dir);
                if (!string.IsNullOrEmpty(parent))
                {
                    yield return Path.Combine(parent, "data", "config.yaml");
                    yield return Path.Combine(parent, "config.yaml");
                }
            }
        }

        // CFW/Mihomo 标准位置
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".config", "clash", "config.yaml");
        yield return Path.Combine(home, ".config", "mihomo", "config.yaml");

        // Clash Verge / Verge Rev (AppData)
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(appData, "clash-verge", "config.yaml");
        yield return Path.Combine(appData, "io.github.clash-verge-rev.clash-verge-rev", "profiles", "verge.yaml");

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localAppData, "clash-verge", "config.yaml");
    }

    private static ClashApiInfo? ParseConfigFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path);
            var mExt = YamlExternal.Match(text);
            if (!mExt.Success) return null;

            var addr = mExt.Groups[1].Value.Trim();
            // 容错: "127.0.0.1:9090" 或 ":9090" -> 补全
            if (addr.StartsWith(":")) addr = "127.0.0.1" + addr;
            if (!addr.Contains(":")) return null;

            var apiUrl = addr.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                ? addr
                : "http://" + addr;

            var mSec = YamlSecret.Match(text);
            var secret = mSec.Success ? mSec.Groups[1].Value.Trim() : "";

            return new ClashApiInfo
            {
                ApiUrl = apiUrl,
                Secret = secret,
                Source = path
            };
        }
        catch { return null; }
    }

    private static HttpClient CreateHttp()
    {
        var handler = new HttpClientHandler { UseProxy = false };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
    }
}
