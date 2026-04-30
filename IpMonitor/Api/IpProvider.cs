using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace IpMonitor.Api;

/// <summary>
/// IP 查询站点定义.
/// IsTrace = true  解析 Cloudflare 的 cdn-cgi/trace 格式(逐行 key=value), 取 ip= 这行
/// IsTrace = false 解析纯文本 IP 或 JSON ({ip:..} / {origin:..})
/// </summary>
public class IpEndpoint
{
    public string Url { get; init; } = "";
    public int Weight { get; init; } = 1;       // 加权投票时的权重
    public string Tag { get; init; } = "";      // 给用户看的简短标识
    public bool IsTrace { get; init; } = false; // 是否 cdn-cgi/trace 格式
    public bool IsDirectProbe { get; init; }    // 是否业务直探(ChatGPT/Claude等), 优先级最高
}

public class IpQueryResult
{
    public IpEndpoint Endpoint { get; set; } = new();
    public string Ip { get; set; } = "";
    public int LatencyMs { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Colo { get; set; }     // CF节点代码(如 LAX), 仅 trace 端点有
    public string? Loc { get; set; }      // CF识别的国家代码(如 US)
}

public static class IpProvider
{
    /// <summary>
    /// 7 个站点: 业务直探 (ChatGPT / Claude) 权重最高, CF 通用次之, 普通 IP 服务最低
    /// </summary>
    public static readonly IpEndpoint[] Endpoints = new[]
    {
        // 业务直探 — 权重 3, 优先级最高. 国内电脑没开代理时会失败, 这是预期的
        new IpEndpoint { Url = "https://chatgpt.com/cdn-cgi/trace",         Weight = 3, Tag = "ChatGPT", IsTrace = true, IsDirectProbe = true },
        new IpEndpoint { Url = "https://claude.ai/cdn-cgi/trace",           Weight = 3, Tag = "Claude",  IsTrace = true, IsDirectProbe = true },

        // CF 通用 — 权重 2
        new IpEndpoint { Url = "https://www.cloudflare.com/cdn-cgi/trace",  Weight = 2, Tag = "CF",      IsTrace = true },

        // 普通 IP 服务 — 权重 1, 网络异常时兜底
        new IpEndpoint { Url = "https://api.ipify.org",                     Weight = 1, Tag = "ipify"     },
        new IpEndpoint { Url = "https://icanhazip.com",                     Weight = 1, Tag = "icanhazip" },
        new IpEndpoint { Url = "https://api.ip.sb/ip",                      Weight = 1, Tag = "ip.sb"     },
        new IpEndpoint { Url = "https://api.myip.com",                      Weight = 1, Tag = "myip"      },
    };

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        // 不强关代理: 让查询和业务流量走同一Clash路由, 反映真实出口IP
        // (TUN 模式下 UseProxy=false 也无效, 强关只会误导)
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IpAnchor/1.0");
        return client;
    }

    public static async Task<IpQueryResult> Query(IpEndpoint ep, CancellationToken ct)
    {
        var result = new IpQueryResult { Endpoint = ep };
        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ep.Url);
            using var resp = await Http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync(ct);
            sw.Stop();

            result.LatencyMs = (int)sw.ElapsedMilliseconds;

            if (ep.IsTrace)
            {
                ParseTrace(body, result);
            }
            else
            {
                result.Ip = ExtractPlainIp(body.Trim());
            }

            result.Success = !string.IsNullOrEmpty(result.Ip);
            if (!result.Success) result.Error = "解析失败: " + Truncate(body, 60);
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
    /// 解析 Cloudflare /cdn-cgi/trace 响应 (逐行 key=value)
    /// 例: ip=23.172.40.55  colo=LAX  loc=US
    /// </summary>
    private static void ParseTrace(string body, IpQueryResult result)
    {
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0 || eq == line.Length - 1) continue;
            var key = line.Substring(0, eq).Trim().ToLowerInvariant();
            var val = line.Substring(eq + 1).Trim();

            switch (key)
            {
                case "ip":
                    if (IPAddress.TryParse(val, out _)) result.Ip = val;
                    break;
                case "colo": result.Colo = val; break;
                case "loc":  result.Loc  = val; break;
            }
        }
    }

    private static string ExtractPlainIp(string body)
    {
        if (string.IsNullOrEmpty(body)) return "";
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
        return IPAddress.TryParse(body, out _) ? body : "";
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max);
}
