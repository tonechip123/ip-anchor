using System.Net;
using System.Text.Json;

namespace IpMonitor.Api;

public class GeoInfo
{
    public string Country { get; set; } = "";
    public string Region { get; set; } = "";
    public string City { get; set; } = "";
    public string Isp { get; set; } = "";
}

/// <summary>
/// IP地理位置查询. 主用 ip-api.com (45次/分钟免费, 无key).
/// 不强制关代理: 让查询和业务流量走相同路由(Clash或直连均可), 反映真实环境.
/// </summary>
public static class GeoLookup
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IpAnchor/1.0");
        return client;
    }

    public static async Task<GeoInfo?> Lookup(string ip, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ip) || !IPAddress.TryParse(ip, out _)) return null;
        try
        {
            // lang=zh-CN 返回中文地名
            var url = $"http://ip-api.com/json/{ip}?lang=zh-CN&fields=status,country,regionName,city,isp";
            var body = await Http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("status", out var st) || st.GetString() != "success")
                return null;
            return new GeoInfo
            {
                Country = root.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "",
                Region = root.TryGetProperty("regionName", out var r) ? r.GetString() ?? "" : "",
                City = root.TryGetProperty("city", out var ci) ? ci.GetString() ?? "" : "",
                Isp = root.TryGetProperty("isp", out var i) ? i.GetString() ?? "" : ""
            };
        }
        catch
        {
            return null;
        }
    }
}
