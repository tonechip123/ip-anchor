using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace IpMonitor.Monitor;

/// <summary>
/// Clash 控制器 - 通过 REST API 切换 Clash 模式 + selector, 不杀进程不动注册表.
///
/// 关键: 用户的 Clash 一般是 rule 模式, 仅切 selector 不影响 RULE 命中的连接.
/// 所以"断开"必须 PATCH /configs 把整个 mode 切到 direct, 这是 Clash 三种模式之一,
/// direct 模式下所有流量绕过规则全部直连. 这是最有效且最简单的"临时断开代理".
/// 同时也切所有 selector 到 DIRECT 作为双保险.
///
/// "恢复" 把 mode 切回原值 + selector 切回原节点.
/// </summary>
public static class ClashController
{
    private static readonly HttpClient Http = CreateHttp();

    private class StateBackup
    {
        public string OriginalMode = "rule";
        public bool ModeWasSwitched;
        public Dictionary<string, string> OriginalSelections = new();
    }

    private static StateBackup? _backup;
    public static bool HasBackup => _backup != null;

    private static HttpClient CreateHttp()
    {
        var handler = new HttpClientHandler { UseProxy = false };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) };
    }

    public class OperationResult
    {
        public bool Success;
        public int Switched;
        public int Total;
        public string Report = "";
    }

    /// <summary>
    /// 临时断开代理: PATCH mode 到 direct + 切所有 selector 到 DIRECT. 备份原状态.
    /// </summary>
    public static async Task<OperationResult> SwitchAllToDirect(AppConfig cfg, CancellationToken ct)
    {
        var r = new OperationResult();
        var lines = new List<string>();

        // 1) 拿当前 mode
        var currentMode = await GetCurrentMode(cfg, ct);
        if (currentMode == null)
        {
            r.Report = "✗ 无法连接 Clash API: " + cfg.ClashApiUrl + "\n   (检查端口/secret或 Clash 是否开着 external-controller)";
            return r;
        }

        // 2) 拿所有 selector
        var selectors = await ListSelectors(cfg, ct);
        if (selectors == null)
        {
            r.Report = "✗ 无法获取 Clash 代理列表";
            return r;
        }

        // 3) 备份
        _backup = new StateBackup { OriginalMode = currentMode };
        foreach (var s in selectors)
        {
            if (s.Now != "DIRECT" && !string.IsNullOrEmpty(s.Now))
                _backup.OriginalSelections[s.Name] = s.Now;
        }

        lines.Add($"原始状态: mode={currentMode}, selectors备份 {_backup.OriginalSelections.Count} 个");
        lines.Add("");

        // 4) 关键步骤: 切 mode 到 direct (无视所有规则, 全部直连)
        if (currentMode != "direct")
        {
            if (await PatchMode(cfg, "direct", ct))
            {
                _backup.ModeWasSwitched = true;
                r.Switched++;
                r.Total++;
                lines.Add($"✓ Clash 模式: {currentMode} → direct (全部规则失效, 流量走本机直连)");
            }
            else
            {
                lines.Add($"✗ 切换 mode 到 direct 失败 (PATCH /configs)");
                r.Total++;
            }
        }
        else
        {
            lines.Add("- Clash 已经是 direct 模式, 无需切换 mode");
        }

        // 5) 双保险: 同时把所有 selector 切到 DIRECT
        var targets = selectors.Where(s => s.AllOptions.Contains("DIRECT") && s.Now != "DIRECT").ToList();
        r.Total += targets.Count;
        foreach (var s in targets)
        {
            if (await SwitchOne(cfg, s.Name, "DIRECT", ct))
            {
                r.Switched++;
                lines.Add($"  ✓ {s.Name}: {s.Now} → DIRECT");
            }
            else
            {
                lines.Add($"  ✗ {s.Name}: 切换失败");
            }
        }

        r.Success = r.Switched > 0;
        r.Report = string.Join("\n", lines) +
                   "\n\n现在浏览器访问 https://chatgpt.com/cdn-cgi/trace 应该看到本机直连IP" +
                   "\n如需恢复代理: 右键 → 恢复Clash代理, 或重启Clash";
        return r;
    }

    /// <summary>
    /// 一键恢复: PATCH mode 回原值 + 切 selector 回原节点
    /// </summary>
    public static async Task<OperationResult> RestoreFromBackup(AppConfig cfg, CancellationToken ct)
    {
        var r = new OperationResult();
        if (_backup == null)
        {
            r.Report = "无备份可恢复.\n如需恢复代理: 在 Clash 里手动选回原模式和节点, 或重启 Clash.";
            return r;
        }

        var lines = new List<string>();

        // 1) 恢复 mode
        if (_backup.ModeWasSwitched)
        {
            r.Total++;
            if (await PatchMode(cfg, _backup.OriginalMode, ct))
            {
                r.Switched++;
                lines.Add($"✓ Clash 模式: direct → {_backup.OriginalMode}");
            }
            else
            {
                lines.Add($"✗ 模式恢复失败 (目标: {_backup.OriginalMode})");
            }
        }

        // 2) 恢复 selector
        r.Total += _backup.OriginalSelections.Count;
        foreach (var kv in _backup.OriginalSelections.ToList())
        {
            if (await SwitchOne(cfg, kv.Key, kv.Value, ct))
            {
                r.Switched++;
                lines.Add($"  ✓ {kv.Key} → {kv.Value}");
            }
            else
            {
                lines.Add($"  ✗ {kv.Key} → {kv.Value} (失败)");
            }
        }

        r.Success = r.Switched > 0;
        r.Report = lines.Count > 0
            ? string.Join("\n", lines)
            : "无需恢复(原本就是当前状态)";
        if (r.Success) _backup = null;
        return r;
    }

    /// <summary>
    /// 测试 Clash API 连通性. 返回友好诊断信息.
    /// </summary>
    public static async Task<string> TestConnection(AppConfig cfg, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(cfg.ClashApiUrl))
            return "未配置 Clash API URL";
        try
        {
            var url = cfg.ClashApiUrl.TrimEnd('/') + "/version";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuth(req, cfg);
            using var resp = await Http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                var version = (await resp.Content.ReadAsStringAsync(ct)).Trim();
                var mode = await GetCurrentMode(cfg, ct) ?? "未知";
                var selectors = await ListSelectors(cfg, ct);
                var sCount = selectors?.Count(s => s.AllOptions.Contains("DIRECT")) ?? 0;
                return $"✓ 连接成功\nURL: {cfg.ClashApiUrl}\n版本: {version}\n当前模式: {mode}\n可控Selector: {sCount} 个";
            }
            if ((int)resp.StatusCode == 401)
                return $"✗ 401 未授权\nSecret 不匹配, 请检查 ClashApiSecret 设置";
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

    private static async Task<string?> GetCurrentMode(AppConfig cfg, CancellationToken ct)
    {
        try
        {
            var url = cfg.ClashApiUrl.TrimEnd('/') + "/configs";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuth(req, cfg);
            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("mode", out var modeEl))
                return modeEl.GetString()?.ToLowerInvariant();
        }
        catch { }
        return null;
    }

    private static async Task<bool> PatchMode(AppConfig cfg, string newMode, CancellationToken ct)
    {
        try
        {
            var url = cfg.ClashApiUrl.TrimEnd('/') + "/configs";
            var body = JsonSerializer.Serialize(new { mode = newMode });
            using var req = new HttpRequestMessage(new HttpMethod("PATCH"), url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            AddAuth(req, cfg);
            using var resp = await Http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static async Task<List<SelectorInfo>?> ListSelectors(AppConfig cfg, CancellationToken ct)
    {
        try
        {
            var url = cfg.ClashApiUrl.TrimEnd('/') + "/proxies";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuth(req, cfg);
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
            AddAuth(req, cfg);
            using var resp = await Http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static void AddAuth(HttpRequestMessage req, AppConfig cfg)
    {
        if (!string.IsNullOrEmpty(cfg.ClashApiSecret))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ClashApiSecret);
    }
}
