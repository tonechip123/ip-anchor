using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace IpMonitor.Api;

public class IpQueryResult
{
    public string Provider { get; set; } = "";
    public string Ip { get; set; } = "";
    public int LatencyMs { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// 单个公网IP查询服务. HTTP GET, 不做ICMP ping.
/// 4 个站点轮询每30秒一次, 每站点每天约2880次, 远低于免费配额, 不会被ban.
/// </summary>
public static class IpProvider
{
    // 4个独立服务, 互不关联, 用于交叉验证
    public static readonly string[] Endpoints = new[]
    {
        "https://api.ipify.org",         // ipify - 老牌, 无限制, 纯文本
        "https://icanhazip.com",         // Cloudflare 维护, 纯文本
        "https://api.ip.sb/ip",          // IP.SB, 纯文本
        "https://api.myip.com"           // myip.com, JSON {ip,country,cc}
    };

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        // 注意: 不强制关代理. 设计意图是让IP查询和业务流量走同一路径(都经过Clash路由),
        // 这样查到的IP就是当前业务实际看到的出口IP.
        // 在TUN模式下UseProxy=false也无效(TUN是IP层劫持, 应用无法绕过), 强关反而误导.
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IpAnchor/1.0");
        return client;
    }

    public static async Task<IpQueryResult> Query(string url, CancellationToken ct)
    {
        var result = new IpQueryResult { Provider = url };
        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await Http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            var body = (await resp.Content.ReadAsStringAsync(ct)).Trim();
            sw.Stop();

            result.LatencyMs = (int)sw.ElapsedMilliseconds;
            result.Ip = ExtractIp(body);
            result.Success = !string.IsNullOrEmpty(result.Ip);
            if (!result.Success) result.Error = "无法解析IP: " + Truncate(body, 60);
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.LatencyMs = (int)sw.ElapsedMilliseconds;
            result.Error = ex.Message;
        }
        return result;
    }

    /// <summary>
    /// 兼容纯文本(ipify/icanhazip/ip.sb)和JSON(myip.com {"ip":"x.x.x.x",...})
    /// </summary>
    private static string ExtractIp(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        body = body.Trim();

        // JSON 格式
        if (body.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("ip", out var ipEl))
                    return ipEl.GetString()?.Trim() ?? "";
                if (doc.RootElement.TryGetProperty("origin", out var orEl))
                    return orEl.GetString()?.Trim() ?? "";
            }
            catch { return ""; }
        }

        // 纯文本IP
        if (IPAddress.TryParse(body, out _)) return body;
        return "";
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max);
}
