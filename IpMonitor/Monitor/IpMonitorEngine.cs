using IpMonitor.Api;

namespace IpMonitor.Monitor;

/// <summary>
/// 主监控引擎: 周期性查询多源IP, 比对预期IP, 触发断开.
/// 不做ICMP ping, 只做HTTP GET, 不会被目标服务器封禁.
/// </summary>
public class IpMonitorEngine
{
    public AppConfig Config { get; private set; }
    public IpStatus Status { get; private set; } = new();

    public event EventHandler<IpStatus>? StatusUpdated;
    public event EventHandler<string>? ClashSwitchedToDirect; // string=报告

    private readonly CancellationTokenSource _cts = new();
    private bool _hasSwitched; // 已切DIRECT后不重复触发, 直到IP恢复或用户手动重设预期IP

    public IpMonitorEngine(AppConfig cfg) { Config = cfg; }

    public void UpdateConfig(AppConfig cfg)
    {
        Config = cfg;
        _hasSwitched = false;
    }

    public void Start()
    {
        _ = Task.Run(() => Loop(_cts.Token));
    }

    public void Stop()
    {
        _cts.Cancel();
    }

    /// <summary>
    /// 立即刷新一次(不等定时器)
    /// </summary>
    public Task<IpStatus> TickNow() => DoOneCheck(_cts.Token);

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await DoOneCheck(ct);
            }
            catch (OperationCanceledException) { break; }
            catch { }

            try
            {
                var sec = Math.Max(10, Config.RefreshIntervalSec);
                await Task.Delay(TimeSpan.FromSeconds(sec), ct);
            }
            catch { break; }
        }
    }

    private async Task<IpStatus> DoOneCheck(CancellationToken ct)
    {
        var st = new IpStatus();
        var resolved = await IpResolver.Resolve(ct);

        st.ProviderHits = resolved.VoteCount;
        st.ProviderTotal = resolved.TotalProviders;
        st.LatencyMs = resolved.AvgLatencyMs;
        st.DetailText = resolved.DetailText();

        if (resolved.All.All(r => !r.Success))
        {
            st.Kind = IpStatusKind.NoNetwork;
            st.CurrentIp = "";
        }
        else if (!resolved.IsTrusted)
        {
            // 没有2个一致 -> 不可信
            st.Kind = IpStatusKind.Inconsistent;
            st.CurrentIp = resolved.Ip; // 最高票, 仅作展示
        }
        else
        {
            st.CurrentIp = resolved.Ip;
            // 地理位置
            try
            {
                var geo = await GeoLookup.Lookup(resolved.Ip, ct);
                if (geo != null)
                {
                    st.Country = geo.Country;
                    st.Region = geo.Region;
                    st.City = geo.City;
                    st.Isp = geo.Isp;
                }
            }
            catch { }

            // 比对预期IP
            var expected = Config.ExpectedIp?.Trim() ?? "";
            if (string.IsNullOrEmpty(expected))
            {
                st.Kind = IpStatusKind.Matched; // 未设预期, 视为正常
            }
            else if (resolved.Ip == expected)
            {
                st.Kind = IpStatusKind.Matched;
                _hasSwitched = false; // 恢复匹配, 重置触发标记
            }
            else
            {
                st.Kind = IpStatusKind.Changed;
                if (Config.AutoSwitchToDirect && !_hasSwitched)
                {
                    _hasSwitched = true;
                    var op = await ClashController.SwitchAllToDirect(Config, ct);
                    ClashSwitchedToDirect?.Invoke(this, op.Report);
                }
            }
        }

        st.UpdatedAt = DateTime.Now;
        Status = st;
        StatusUpdated?.Invoke(this, st);
        return st;
    }
}
