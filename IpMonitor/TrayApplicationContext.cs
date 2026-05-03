using IpMonitor.Monitor;
using IpMonitor.UI;

namespace IpMonitor;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly FloatingBar _floatingBar;
    private readonly IpMonitorEngine _engine;
    private AppConfig _config;
    private IpStatus _lastStatus = new();

    // 菜单项引用, 用于运行时更新文本/勾选状态
    private ToolStripMenuItem? _miLock;

    public TrayApplicationContext()
    {
        _config = ConfigManager.Load();

        _contextMenu = new ContextMenuStrip();
        _floatingBar = new FloatingBar();
        _trayIcon = new NotifyIcon
        {
            Visible = true,
            ContextMenuStrip = _contextMenu,
            Text = "IP锚定 - 初始化中...",
            Icon = TrayIconRenderer.Render("", IpStatusKind.Unknown)
        };
        _trayIcon.DoubleClick += (_, _) => _floatingBar.ToggleVisibility();
        _floatingBar.SetExternalMenu(_contextMenu);
        _floatingBar.RefreshRequested += async (_, _) => await ManualRefresh();
        _floatingBar.Show();

        // 菜单弹出前刷新各项状态(锁定/恢复)
        _contextMenu.Opening += (_, _) => UpdateMenuStates();

        _engine = new IpMonitorEngine(_config);
        _engine.StatusUpdated += OnStatusUpdated;
        _engine.IpChanged += OnIpChanged;

        BuildMenu();
        _engine.Start();
    }

    private void BuildMenu()
    {
        _contextMenu.Items.Clear();

        var refresh = new ToolStripMenuItem("立即刷新");
        refresh.Click += async (_, _) => await ManualRefresh();
        _contextMenu.Items.Add(refresh);

        _miLock = new ToolStripMenuItem("未开始监控");
        _miLock.Click += (_, _) =>
        {
            var hasExpected = !string.IsNullOrEmpty(_config.ExpectedIp);

            if (hasExpected)
            {
                // 当前正在监控，点击取消监控
                _config.ExpectedIp = "";
                ConfigManager.Save(_config);
                _engine.UpdateConfig(_config);
                UpdateMenuStates();
                _trayIcon.ShowBalloonTip(2000, "已停止监控", "不再监控IP变化", ToolTipIcon.Info);
            }
            else
            {
                // 当前未监控，点击开始监控
                if (string.IsNullOrEmpty(_lastStatus.CurrentIp))
                {
                    MessageBox.Show("当前未检测到可信IP, 无法开始监控", "IP监控",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _config.ExpectedIp = _lastStatus.CurrentIp;
                ConfigManager.Save(_config);
                _engine.UpdateConfig(_config);
                UpdateMenuStates();
                _trayIcon.ShowBalloonTip(2000, "已开始监控",
                    $"正在监控IP: {_config.ExpectedIp}", ToolTipIcon.Info);
            }
        };
        _contextMenu.Items.Add(_miLock);

        _contextMenu.Items.Add(new ToolStripSeparator());

        var about = new ToolStripMenuItem("查看详情");
        about.Click += (_, _) =>
        {
            var head = TrayIconRenderer.BuildTooltip(_lastStatus);
            var reason = string.IsNullOrEmpty(_lastStatus.ChoiceReason) ? "" : "\n\n选IP依据: " + _lastStatus.ChoiceReason;
            var detail = "\n\n各源详情:\n" + _lastStatus.DetailText;
            var path = $"\n\n配置目录: {ConfigManager.ConfigDir}";
            MessageBox.Show(head + reason + detail + path, "IP监控 详情",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        _contextMenu.Items.Add(about);

        _contextMenu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) =>
        {
            _engine.Stop();
            _trayIcon.Visible = false;
            Application.Exit();
        };
        _contextMenu.Items.Add(exit);
    }

    private async Task ManualRefresh()
    {
        try { await _engine.TickNow(); } catch { }
    }

    /// <summary>
    /// 菜单弹出前刷新动态状态: 正在监控时显示勾选+IP, 未监控时显示"未开始监控"
    /// </summary>
    private void UpdateMenuStates()
    {
        var hasExpected = !string.IsNullOrEmpty(_config.ExpectedIp);

        if (_miLock != null)
        {
            if (hasExpected)
            {
                var drifted = !string.IsNullOrEmpty(_lastStatus.CurrentIp)
                              && _lastStatus.CurrentIp != _config.ExpectedIp;
                _miLock.Text = drifted
                    ? $"✓ ⚠ 正在监控 {_config.ExpectedIp} (当前已漂移)"
                    : $"✓ 正在监控 {_config.ExpectedIp}";
            }
            else
            {
                _miLock.Text = "  未开始监控";
            }
        }
    }

    private void OnStatusUpdated(object? sender, IpStatus st)
    {
        _lastStatus = st;
        try
        {
            _floatingBar.BeginInvoke(() =>
            {
                _floatingBar.UpdateDisplay(st);
                var oldIcon = _trayIcon.Icon;
                _trayIcon.Icon = TrayIconRenderer.Render(st.CurrentIp, st.Kind);
                _trayIcon.Text = TrayIconRenderer.BuildTooltip(st);
                if (oldIcon != null) oldIcon.Dispose();
            });
        }
        catch { }
    }

    private void OnIpChanged(object? sender, string report)
    {
        try
        {
            _floatingBar.BeginInvoke(() =>
            {
                _trayIcon.ShowBalloonTip(10000,
                    "⚠ IP已漂移",
                    report,
                    ToolTipIcon.Warning);
                // 移除提示音
            });
        }
        catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _engine.Stop();
            _floatingBar.Close();
            _floatingBar.Dispose();
            _trayIcon.Dispose();
            _contextMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}
