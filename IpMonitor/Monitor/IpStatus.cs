namespace IpMonitor.Monitor;

public enum IpStatusKind
{
    Unknown,        // 未检测
    Matched,        // 与预期一致
    Inconsistent,   // 多源结果不一致(无2个相同)
    Changed,        // IP已切换(与预期不同)
    NoNetwork       // 全部失败
}

public class IpStatus
{
    public string CurrentIp { get; set; } = "";
    public string Country { get; set; } = "";
    public string Region { get; set; } = "";
    public string City { get; set; } = "";
    public string Isp { get; set; } = "";
    public int LatencyMs { get; set; }            // 平均HTTP延迟
    public int ProviderHits { get; set; }         // 多少个provider命中"主要IP"
    public int ProviderTotal { get; set; }
    public IpStatusKind Kind { get; set; } = IpStatusKind.Unknown;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string DetailText { get; set; } = "";

    public string GeoSummary()
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(Country)) parts.Add(Country);
        if (!string.IsNullOrEmpty(Region) && Region != Country) parts.Add(Region);
        if (!string.IsNullOrEmpty(City) && City != Region) parts.Add(City);
        return parts.Count > 0 ? string.Join(" ", parts) : "未知";
    }
}
