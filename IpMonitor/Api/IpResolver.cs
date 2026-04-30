namespace IpMonitor.Api;

public class ResolvedIp
{
    public string Ip { get; set; } = "";
    public int VoteCount { get; set; }       // 多少个provider报告了这个IP(选中的IP)
    public int TotalProviders { get; set; }
    public int AvgLatencyMs { get; set; }    // 成功provider的平均延迟
    public bool IsHighConfidence { get; set; } // 至少2个provider给出相同IP
    public string ChoiceReason { get; set; } = ""; // 描述如何选出当前IP
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
    /// 并发查询所有provider, 选出"代表IP".
    /// 优先级:
    ///   1. 多数票 (>=2个provider返回相同IP) -> 高置信
    ///   2. 全部不同(分流环境) -> 取延迟最低的那个 -> 低置信
    /// 永远给出一个IP, 不再标记为"不一致"错误.
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
        {
            result.ChoiceReason = "全部provider查询失败";
            return result;
        }

        result.AvgLatencyMs = (int)success.Average(r => r.LatencyMs);

        // 按 IP 分组按票数排序
        var groups = success
            .GroupBy(r => r.Ip)
            .OrderByDescending(g => g.Count())
            .ToList();

        var top = groups[0];
        if (top.Count() >= 2)
        {
            // 高置信: 至少2个站点投同一个IP
            result.Ip = top.Key;
            result.VoteCount = top.Count();
            result.IsHighConfidence = true;
            result.ChoiceReason = $"高置信: {top.Count()}/{result.TotalProviders} 个站点一致";
        }
        else
        {
            // 低置信: 全部分歧(典型于Clash TUN分流环境)
            // 取延迟最低的成功站点 -> 最近 -> 最可能是主要业务出口
            var fastest = success.OrderBy(r => r.LatencyMs).First();
            result.Ip = fastest.Ip;
            result.VoteCount = 1;
            result.IsHighConfidence = false;
            result.ChoiceReason = $"低置信: 各站点结果不同(分流环境), 选延迟最低的 {new Uri(fastest.Provider).Host} ({fastest.LatencyMs}ms)";
        }

        return result;
    }
}
