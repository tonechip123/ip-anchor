namespace IpMonitor.Api;

public class ResolvedIp
{
    public string Ip { get; set; } = "";
    public int VoteCount { get; set; }       // 多少个provider报告了这个IP
    public int TotalProviders { get; set; }
    public int AvgLatencyMs { get; set; }    // 成功provider的平均延迟
    public bool IsTrusted { get; set; }      // VoteCount >= 2
    public List<IpQueryResult> All { get; set; } = new();

    public string DetailText()
    {
        var lines = new List<string>();
        foreach (var r in All)
        {
            var host = new Uri(r.Provider).Host;
            if (r.Success)
                lines.Add($"  {host,-20} {r.Ip,-16} {r.LatencyMs}ms");
            else
                lines.Add($"  {host,-20} 失败: {r.Error}");
        }
        return string.Join("\n", lines);
    }
}

public static class IpResolver
{
    /// <summary>
    /// 并发查询所有provider, 多数票决出"真实IP"(>=2个相同).
    /// </summary>
    public static async Task<ResolvedIp> Resolve(CancellationToken ct)
    {
        var tasks = IpProvider.Endpoints
            .Select(url => IpProvider.Query(url, ct))
            .ToArray();
        var all = await Task.WhenAll(tasks);

        var result = new ResolvedIp
        {
            TotalProviders = all.Length,
            All = all.ToList()
        };

        var success = all.Where(r => r.Success && !string.IsNullOrEmpty(r.Ip)).ToList();
        if (success.Count == 0)
            return result;

        result.AvgLatencyMs = (int)success.Average(r => r.LatencyMs);

        // 多数票
        var groups = success
            .GroupBy(r => r.Ip)
            .OrderByDescending(g => g.Count())
            .ToList();

        var top = groups[0];
        result.Ip = top.Key;
        result.VoteCount = top.Count();
        result.IsTrusted = result.VoteCount >= 2;

        return result;
    }
}
