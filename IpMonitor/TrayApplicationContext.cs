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

        _engine = new IpMonitorEngine(_config);
        _engine.StatusUpdated += OnStatusUpdated;
        _engine.ClashSwitchedToDirect += OnClashSwitched;

        BuildMenu();
        _engine.Start();

        // 启动时自动检测Clash API(如果尚未配置)
        if (string.IsNullOrEmpty(_config.ClashApiUrl))
            _ = Task.Run(() => AutoDetectClashOnStartup());
    }

    private async Task AutoDetectClashOnStartup()
    {
        try
        {
            var info = await ClashAutoDetect.DetectAsync(CancellationToken.None);
            if (info.Reachable)
            {
                _config.ClashApiUrl = info.ApiUrl;
                _config.ClashApiSecret = info.Secret;
                _config.ClashApiSource = info.Source;
                ConfigManager.Save(_config);
                _engine.UpdateConfig(_config);

                _floatingBar.BeginInvoke(() =>
                {
                    _trayIcon.ShowBalloonTip(3000, "已自动识别Clash",
                        $"API: {info.ApiUrl}\n来源: {Path.GetFileName(info.Source)}",
                        ToolTipIcon.Info);
                });
            }
        }
        catch { }
    }

    private void BuildMenu()
    {
        _contextMenu.Items.Clear();

        var refresh = new ToolStripMenuItem("立即刷新");
        refresh.Click += async (_, _) => await ManualRefresh();
        _contextMenu.Items.Add(refresh);

        var lockCurrent = new ToolStripMenuItem("锁定当前IP为预期");
        lockCurrent.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(_lastStatus.CurrentIp))
            {
                MessageBox.Show("当前未检测到可信IP, 无法锁定", "IP锚定",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _config.ExpectedIp = _lastStatus.CurrentIp;
            ConfigManager.Save(_config);
            _engine.UpdateConfig(_config);
            _trayIcon.ShowBalloonTip(2000, "已锁定IP",
                $"预期IP设为: {_config.ExpectedIp}", ToolTipIcon.Info);
        };
        _contextMenu.Items.Add(lockCurrent);

        var unlock = new ToolStripMenuItem("清除预期IP(停止监测)");
        unlock.Click += (_, _) =>
        {
            _config.ExpectedIp = "";
            ConfigManager.Save(_config);
            _engine.UpdateConfig(_config);
        };
        _contextMenu.Items.Add(unlock);

        _contextMenu.Items.Add(new ToolStripSeparator());

        var switchNow = new ToolStripMenuItem("手动切到DIRECT (临时断开代理)");
        switchNow.Click += async (_, _) =>
        {
            var op = await ClashController.SwitchAllToDirect(_config, CancellationToken.None);
            MessageBox.Show(op.Report, "切到DIRECT 完成",
                MessageBoxButtons.OK,
                op.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        };
        _contextMenu.Items.Add(switchNow);

        var restore = new ToolStripMenuItem("恢复Clash代理 (切回原节点)");
        restore.Click += async (_, _) =>
        {
            var op = await ClashController.RestoreFromBackup(_config, CancellationToken.None);
            MessageBox.Show(op.Report, "恢复代理 完成",
                MessageBoxButtons.OK,
                op.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        };
        _contextMenu.Items.Add(restore);

        _contextMenu.Items.Add(new ToolStripSeparator());

        var redetect = new ToolStripMenuItem("重新检测Clash API");
        redetect.Click += async (_, _) => await ManualRedetect();
        _contextMenu.Items.Add(redetect);

        var testApi = new ToolStripMenuItem("测试Clash API连通性");
        testApi.Click += async (_, _) =>
        {
            var msg = await ClashController.TestConnection(_config, CancellationToken.None);
            MessageBox.Show(msg, "Clash API 连通性",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        _contextMenu.Items.Add(testApi);

        _contextMenu.Items.Add(new ToolStripSeparator());

        var settings = new ToolStripMenuItem("设置...");
        settings.Click += OnSettingsClick;
        _contextMenu.Items.Add(settings);

        var about = new ToolStripMenuItem("查看详情");
        about.Click += (_, _) =>
        {
            var head = TrayIconRenderer.BuildTooltip(_lastStatus);
            var reason = string.IsNullOrEmpty(_lastStatus.ChoiceReason) ? "" : "\n\n选IP依据: " + _lastStatus.ChoiceReason;
            var detail = "\n\n各源详情:\n" + _lastStatus.DetailText;
            var clash = $"\n\nClash API: {(_config.ClashApiUrl == "" ? "未识别" : _config.ClashApiUrl)}";
            var path = $"\n\n配置目录: {ConfigManager.ConfigDir}";
            MessageBox.Show(head + reason + detail + clash + path, "IP锚定 详情",
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

    private async Task ManualRedetect()
    {
        try
        {
            var info = await ClashAutoDetect.DetectAsync(CancellationToken.None);
            if (info.Reachable)
            {
                _config.ClashApiUrl = info.ApiUrl;
                _config.ClashApiSecret = info.Secret;
                _config.ClashApiSource = info.Source;
                ConfigManager.Save(_config);
                _engine.UpdateConfig(_config);
                MessageBox.Show(
                    $"✓ 自动检测成功\nURL: {info.ApiUrl}\nSecret: {(string.IsNullOrEmpty(info.Secret) ? "(无)" : "已识别")}\n来源: {info.Source}",
                    "Clash API 已识别", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(
                    $"✗ 自动检测失败\n{info.Error}\n\n请在'设置'里手动填入URL和Secret.\nClash for Windows的端口和密钥在 设置→API核心端口 显示, 或在 data/config.yaml 里查看.",
                    "Clash API 检测失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("检测异常: " + ex.Message, "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
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

    private void OnClashSwitched(object? sender, string report)
    {
        try
        {
            _floatingBar.BeginInvoke(() =>
            {
                _trayIcon.ShowBalloonTip(10000,
                    "⚠ IP已变化, 已切到DIRECT",
                    $"当前IP: {_lastStatus.CurrentIp}\n预期: {_config.ExpectedIp}\n区域: {_lastStatus.GeoSummary()}\n\n{report}\n\n如需恢复代理: 右键→恢复Clash代理, 或重启Clash",
                    ToolTipIcon.Warning);
                System.Media.SystemSounds.Exclamation.Play();
            });
        }
        catch { }
    }

    private async void OnSettingsClick(object? sender, EventArgs e)
    {
        using var form = new SettingsForm(_config) { CurrentDetectedIp = _lastStatus.CurrentIp };
        form.RedetectRequested += async (_, _) =>
        {
            var info = await ClashAutoDetect.DetectAsync(CancellationToken.None);
            form.ApplyDetectedClash(info.ApiUrl, info.Secret,
                info.Reachable ? info.Source : (info.Error ?? "检测失败"));
        };
        form.TestApiRequested += async (_, _) =>
        {
            var msg = await ClashController.TestConnection(form.Config, CancellationToken.None);
            MessageBox.Show(msg, "Clash API 连通性", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        if (form.ShowDialog() == DialogResult.OK)
        {
            _config = form.Config;
            ConfigManager.Save(_config);
            _engine.UpdateConfig(_config);
        }
        await Task.CompletedTask;
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
