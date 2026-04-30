namespace IpMonitor.Api;

public class ResolvedIp
{
    public string Ip { get; set; } = "";
    public int VoteWeight { get; set; }              // 选中IP的累计加权票
    public int VoteCount { get; set; }               // 选中IP的站点数(便于显示)
    public int TotalProviders { get; set; }
    public int AvgLatencyMs { get; set; }
    public bool IsHighConfidence { get; set; }       // 是否高置信
    public string ChoiceReason { get; set; } = "";   // 选IP依据
    public string Colo { get; set; } = "";           // CF边缘节点(如 LAX), 来自直探
    public List<IpQueryResult> All { get; set; } = new();

    public string DetailText()
    {
        var lines = new List<string>();
        // 按权重降序展示
        foreach (var r in All.OrderByDescending(x => x.Endpoint.Weight).ThenBy(x => x.LatencyMs))
        {
            var tag = $"[{r.Endpoint.Tag},w{r.Endpoint.Weight}]";
            if (r.Success)
            {
                var extra = "";
                if (!string.IsNullOrEmpty(r.Colo)) extra = $" colo={r.Colo}";
                if (!string.IsNullOrEmpty(r.Loc))  extra += $" loc={r.Loc}";
                lines.Add($"  {tag,-22} {r.Ip,-16} {r.LatencyMs}ms{extra}");
            }
            else
            {
                lines.Add($"  {tag,-22} 失败: {Trim(r.Error)}");
            }
        }
        return string.Join("\n", lines);
    }

    private static string Trim(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= 50 ? s : s.Substring(0, 50);
    }
}

public static class IpResolver
{
    /// <summary>
    /// 决策优先级:
    ///   1. ChatGPT / Claude 直探有同一IP 且至少1个直探成功 → 高置信(=业务路径IP)
    ///   2. 加权投票, 最高加权 ≥ 3 → 高置信
    ///   3. 全分歧 → 取延迟最低的成功站点 → 低置信
    /// </summary>
    public static async Task<ResolvedIp> Resolve(CancellationToken ct)
    {
        var tasks = IpProvider.Endpoints
            .Select(ep => IpProvider.Query(ep, ct))
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
            result.ChoiceReason = "全部站点查询失败";
            return result;
        }

        result.AvgLatencyMs = (int)success.Average(r => r.LatencyMs);

        // 1) 业务直探: ChatGPT / Claude 任一成功 → 优先使用
        var directProbes = success.Where(r => r.Endpoint.IsDirectProbe).ToList();
        if (directProbes.Count > 0)
        {
            // 多个直探时按 IP 分组, 取数量最多的
            var directGroup = directProbes
                .GroupBy(r => r.Ip)
                .OrderByDescending(g => g.Count())
                .First();
            result.Ip = directGroup.Key;
            result.VoteWeight = directGroup.Sum(r => r.Endpoint.Weight);
            result.VoteCount = directGroup.Count();
            result.IsHighConfidence = true;
            result.Colo = directGroup.Select(r => r.Colo).FirstOrDefault(c => !string.IsNullOrEmpty(c)) ?? "";
            var tags = string.Join("+", directGroup.Select(r => r.Endpoint.Tag));
            result.ChoiceReason = $"业务直探命中: {tags} → {result.Ip}" +
                                  (string.IsNullOrEmpty(result.Colo) ? "" : $" (CF节点 {result.Colo})");
            return result;
        }

        // 2) 加权投票
        var groups = success
            .GroupBy(r => r.Ip)
            .Select(g => new
            {
                Ip = g.Key,
                Weight = g.Sum(r => r.Endpoint.Weight),
                Count = g.Count(),
                Items = g.ToList()
            })
            .OrderByDescending(x => x.Weight)
            .ThenByDescending(x => x.Count)
            .ToList();

        var top = groups[0];
        if (top.Weight >= 3)
        {
            // CF 通用(2) + 任一普通(1) = 3, 或 ≥3 个普通站点
            result.Ip = top.Ip;
            result.VoteWeight = top.Weight;
            result.VoteCount = top.Count;
            result.IsHighConfidence = true;
            result.Colo = top.Items.Select(r => r.Colo).FirstOrDefault(c => !string.IsNullOrEmpty(c)) ?? "";
            var tags = string.Join("+", top.Items.Select(r => r.Endpoint.Tag));
            result.ChoiceReason = $"加权投票胜出: {tags} (加权{top.Weight}) → {top.Ip}";
        }
        else
        {
            // 全分歧, 取延迟最低的
            var fastest = success.OrderBy(r => r.LatencyMs).First();
            result.Ip = fastest.Ip;
            result.VoteWeight = fastest.Endpoint.Weight;
            result.VoteCount = 1;
            result.IsHighConfidence = false;
            result.Colo = fastest.Colo ?? "";
            result.ChoiceReason = $"低置信: 各源不同(分流环境), 选最快的 [{fastest.Endpoint.Tag}] {fastest.LatencyMs}ms";
        }

        return result;
    }
}
