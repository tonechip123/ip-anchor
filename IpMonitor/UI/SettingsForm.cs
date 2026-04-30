namespace IpMonitor.UI;

public class SettingsForm : Form
{
    private readonly TextBox _expectedIpBox;
    private readonly TextBox _intervalBox;
    private readonly CheckBox _autoSwitch;
    private readonly TextBox _clashApiBox;
    private readonly TextBox _clashSecretBox;
    private readonly Label _clashSourceLabel;
    private readonly Button _ok;
    private readonly Button _cancel;
    private readonly Button _useCurrent;
    private readonly Button _redetect;
    private readonly Button _testApi;

    public AppConfig Config { get; private set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string CurrentDetectedIp { get; set; } = "";

    public event EventHandler? RedetectRequested;
    public event EventHandler? TestApiRequested;

    public SettingsForm(AppConfig cfg)
    {
        Config = new AppConfig
        {
            ExpectedIp = cfg.ExpectedIp,
            RefreshIntervalSec = cfg.RefreshIntervalSec,
            AutoSwitchToDirect = cfg.AutoSwitchToDirect,
            ClashApiUrl = cfg.ClashApiUrl,
            ClashApiSecret = cfg.ClashApiSecret,
            ClashApiSource = cfg.ClashApiSource
        };

        Text = "IP锚定 - 设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(480, 380);
        Font = new Font("Microsoft YaHei", 9f);

        int y = 14;
        Controls.Add(new Label { Text = "预期公网IP (留空=不锁定)", Location = new Point(14, y), AutoSize = true });
        y += 22;
        _expectedIpBox = new TextBox { Location = new Point(14, y), Width = 230, Text = cfg.ExpectedIp };
        Controls.Add(_expectedIpBox);
        _useCurrent = new Button { Text = "用当前检测IP", Location = new Point(254, y - 1), Width = 110 };
        _useCurrent.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(CurrentDetectedIp))
                _expectedIpBox.Text = CurrentDetectedIp;
        };
        Controls.Add(_useCurrent);
        y += 32;

        Controls.Add(new Label { Text = "刷新间隔 (秒, 最小10)", Location = new Point(14, y), AutoSize = true });
        y += 22;
        _intervalBox = new TextBox { Location = new Point(14, y), Width = 80, Text = cfg.RefreshIntervalSec.ToString() };
        Controls.Add(_intervalBox);
        y += 32;

        _autoSwitch = new CheckBox
        {
            Text = "检测到IP变化时, 自动把Clash所有Selector切到DIRECT (不杀进程, 重启Clash自动恢复)",
            Location = new Point(14, y),
            AutoSize = true,
            Checked = cfg.AutoSwitchToDirect
        };
        Controls.Add(_autoSwitch);
        y += 28;

        // Clash API 配置(自动检测填入, 也可手动改)
        Controls.Add(new Label
        {
            Text = "Clash API URL  (自动检测, 通常无需修改)",
            Location = new Point(14, y),
            AutoSize = true
        });
        y += 22;
        _clashApiBox = new TextBox { Location = new Point(14, y), Width = 280, Text = cfg.ClashApiUrl };
        Controls.Add(_clashApiBox);
        _redetect = new Button { Text = "重新检测", Location = new Point(304, y - 1), Width = 80 };
        _redetect.Click += (_, _) => RedetectRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(_redetect);
        _testApi = new Button { Text = "测试连通", Location = new Point(390, y - 1), Width = 80 };
        _testApi.Click += (_, _) =>
        {
            // 用当前对话框的值临时填入Config
            Config.ClashApiUrl = _clashApiBox.Text.Trim();
            Config.ClashApiSecret = _clashSecretBox?.Text?.Trim() ?? "";
            TestApiRequested?.Invoke(this, EventArgs.Empty);
        };
        Controls.Add(_testApi);
        y += 28;

        Controls.Add(new Label { Text = "Clash API Secret (无密钥则留空)", Location = new Point(14, y), AutoSize = true });
        y += 22;
        _clashSecretBox = new TextBox { Location = new Point(14, y), Width = 456, Text = cfg.ClashApiSecret };
        Controls.Add(_clashSecretBox);
        y += 26;

        _clashSourceLabel = new Label
        {
            Text = string.IsNullOrEmpty(cfg.ClashApiSource) ? "" : "来源: " + cfg.ClashApiSource,
            Location = new Point(14, y),
            AutoSize = false,
            Size = new Size(456, 18),
            ForeColor = Color.Gray,
            Font = new Font("Microsoft YaHei", 8f)
        };
        Controls.Add(_clashSourceLabel);
        y += 32;

        _ok = new Button { Text = "保存", Location = new Point(280, y), Width = 80 };
        _cancel = new Button { Text = "取消", Location = new Point(370, y), Width = 80 };
        _ok.Click += OnOk;
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(_ok);
        Controls.Add(_cancel);

        AcceptButton = _ok;
        CancelButton = _cancel;
    }

    /// <summary>
    /// 由外部调用, 把重新检测的结果填回表单(不关闭对话框)
    /// </summary>
    public void ApplyDetectedClash(string url, string secret, string source)
    {
        _clashApiBox.Text = url;
        _clashSecretBox.Text = secret;
        _clashSourceLabel.Text = string.IsNullOrEmpty(source) ? "" : "来源: " + source;
    }

    private void OnOk(object? sender, EventArgs e)
    {
        Config.ExpectedIp = _expectedIpBox.Text.Trim();
        if (int.TryParse(_intervalBox.Text.Trim(), out var sec))
            Config.RefreshIntervalSec = Math.Max(10, sec);
        Config.AutoSwitchToDirect = _autoSwitch.Checked;
        Config.ClashApiUrl = _clashApiBox.Text.Trim();
        Config.ClashApiSecret = _clashSecretBox.Text.Trim();
        DialogResult = DialogResult.OK;
        Close();
    }
}
