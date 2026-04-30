using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace IpMonitor.Monitor;

/// <summary>
/// Clash控制器 - 只通过REST API操作, 不杀进程不动注册表.
/// 设计目标: 临时把流量切到DIRECT, 不影响本地网络, 用户重启Clash自动恢复, 也可手动一键恢复.
/// </summary>
public static class ClashController
{
    private static readonly HttpClient Http = CreateHttp();

    /// <summary>
    /// 上一次断开时, 每个Selector原本选中的节点. 用于恢复.
    /// </summary>
    private static Dictionary<string, string> _backupSelections = new();

    private static HttpClient CreateHttp()
    {
        var handler = new HttpClientHandler { UseProxy = false };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) };
    }

    public class OperationResult
    {
        public bool Success;
        public int Switched;     // 实际切换的Selector数
        public int Total;        // 探测到的Selector总数
        public string Report = "";
    }

    /// <summary>
    /// 临时断开: 把所有Selector切到DIRECT. 备份原选择.
    /// </summary>
    public static async Task<OperationResult> SwitchAllToDirect(AppConfig cfg, CancellationToken ct)
    {
        var r = new OperationResult();
        var lines = new List<string>();

        var selectors = await ListSelectors(cfg, ct);
        if (selectors == null)
        {
            r.Report = "无法连接Clash API: " + cfg.ClashApiUrl;
            return r;
        }

        // 只对包含DIRECT选项的Selector生效
        var targets = selectors.Where(s => s.AllOptions.Contains("DIRECT")).ToList();
        r.Total = targets.Count;
        if (targets.Count == 0)
        {
            r.Report = "未发现含DIRECT选项的Selector";
            return r;
        }

        // 备份原选择(不覆盖已经是DIRECT的)
        _backupSelections.Clear();
        foreach (var s in targets)
        {
            if (!string.IsNullOrEmpty(s.Now) && s.Now != "DIRECT")
                _backupSelections[s.Name] = s.Now;
        }

        foreach (var s in targets)
        {
            if (s.Now == "DIRECT") { lines.Add($"  {s.Name}: 已是DIRECT, 跳过"); continue; }
            if (await SwitchOne(cfg, s.Name, "DIRECT", ct))
            {
                r.Switched++;
                lines.Add($"  {s.Name}: {s.Now} → DIRECT");
            }
            else
            {
                lines.Add($"  {s.Name}: 切换失败");
            }
        }

        r.Success = r.Switched > 0;
        r.Report = $"已把 {r.Switched}/{r.Total} 个Selector切到DIRECT, 流量直连本机.\n" +
                   $"备份了 {_backupSelections.Count} 个原节点选择, 可一键恢复.\n\n" +
                   string.Join("\n", lines);
        return r;
    }

    /// <summary>
    /// 一键恢复: 把每个Selector切回断开前的节点.
    /// </summary>
    public static async Task<OperationResult> RestoreFromBackup(AppConfig cfg, CancellationToken ct)
    {
        var r = new OperationResult();
        if (_backupSelections.Count == 0)
        {
            r.Report = "无备份可恢复(可能是首次启动, 或Clash已自动恢复).\n如需恢复代理, 请在Clash里手动选回节点.";
            return r;
        }

        var lines = new List<string>();
        r.Total = _backupSelections.Count;
        foreach (var kv in _backupSelections.ToList())
        {
            if (await SwitchOne(cfg, kv.Key, kv.Value, ct))
            {
                r.Switched++;
                lines.Add($"  {kv.Key} → {kv.Value}");
            }
            else
            {
                lines.Add($"  {kv.Key} → {kv.Value} (失败)");
            }
        }

        r.Success = r.Switched > 0;
        r.Report = $"已恢复 {r.Switched}/{r.Total} 个Selector的原节点选择.\n\n" + string.Join("\n", lines);
        if (r.Success) _backupSelections.Clear();
        return r;
    }

    public static bool HasBackup => _backupSelections.Count > 0;

    /// <summary>
    /// 测试Clash API连通性. 返回友好诊断信息.
    /// </summary>
    public static async Task<string> TestConnection(AppConfig cfg, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(cfg.ClashApiUrl))
            return "未配置Clash API URL";
        try
        {
            var url = cfg.ClashApiUrl.TrimEnd('/') + "/version";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(cfg.ClashApiSecret))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ClashApiSecret);
            using var resp = await Http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                var selectors = await ListSelectors(cfg, ct);
                var sCount = selectors?.Count(s => s.AllOptions.Contains("DIRECT")) ?? 0;
                return $"✓ 连接成功\nURL: {cfg.ClashApiUrl}\n版本: {body.Trim()}\n可控Selector: {sCount} 个";
            }
            if ((int)resp.StatusCode == 401)
                return $"✗ 401 未授权\nSecret不匹配, 请检查 ClashApiSecret 设置";
            return $"✗ HTTP {(int)resp.StatusCode}";
        }
        catch (Exception ex)
        {
            return $"✗ 连接失败\nURL: {cfg.ClashApiUrl}\n错误: {ex.Message}";
        }
    }

    // ---------- 内部 API 调用 ----------

    private class SelectorInfo
    {
        public string Name = "";
        public string Now = "";
        public List<string> AllOptions = new();
    }

    private static async Task<List<SelectorInfo>?> ListSelectors(AppConfig cfg, CancellationToken ct)
    {
        try
        {
            var url = cfg.ClashApiUrl.TrimEnd('/') + "/proxies";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(cfg.ClashApiSecret))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ClashApiSecret);
            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("proxies", out var proxies)) return null;

            var list = new List<SelectorInfo>();
            foreach (var prop in proxies.EnumerateObject())
            {
                if (!prop.Value.TryGetProperty("type", out var typeEl)) continue;
                if (typeEl.GetString() != "Selector") continue;

                var info = new SelectorInfo { Name = prop.Name };
                if (prop.Value.TryGetProperty("now", out var nowEl))
                    info.Now = nowEl.GetString() ?? "";
                if (prop.Value.TryGetProperty("all", out var allEl))
                    foreach (var item in allEl.EnumerateArray())
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrEmpty(s)) info.AllOptions.Add(s);
                    }
                list.Add(info);
            }
            return list;
        }
        catch { return null; }
    }

    private static async Task<bool> SwitchOne(AppConfig cfg, string selectorName, string targetNode, CancellationToken ct)
    {
        try
        {
            var url = $"{cfg.ClashApiUrl.TrimEnd('/')}/proxies/{Uri.EscapeDataString(selectorName)}";
            using var req = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { name = targetNode }),
                    Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrEmpty(cfg.ClashApiSecret))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ClashApiSecret);
            using var resp = await Http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
