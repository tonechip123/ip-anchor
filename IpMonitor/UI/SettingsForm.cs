namespace IpMonitor.UI;

public class SettingsForm : Form
{
    private readonly TextBox _expectedIpBox;
    private readonly TextBox _intervalBox;
    private readonly Button _ok;
    private readonly Button _cancel;
    private readonly Button _useCurrent;

    public AppConfig Config { get; private set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string CurrentDetectedIp { get; set; } = "";

    public SettingsForm(AppConfig cfg)
    {
        Config = new AppConfig
        {
            ExpectedIp = cfg.ExpectedIp,
            RefreshIntervalSec = cfg.RefreshIntervalSec
        };

        Text = "IP监控 - 设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(480, 220);
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

        Controls.Add(new Label { Text = "刷新间隔 (秒, 最小1)", Location = new Point(14, y), AutoSize = true });
        y += 22;
        _intervalBox = new TextBox { Location = new Point(14, y), Width = 80, Text = cfg.RefreshIntervalSec.ToString() };
        Controls.Add(_intervalBox);
        y += 40;

        _ok = new Button { Text = "保存", Location = new Point(280, y), Width = 80 };
        _cancel = new Button { Text = "取消", Location = new Point(370, y), Width = 80 };
        _ok.Click += OnOk;
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(_ok);
        Controls.Add(_cancel);

        AcceptButton = _ok;
        CancelButton = _cancel;
    }

    private void OnOk(object? sender, EventArgs e)
    {
        Config.ExpectedIp = _expectedIpBox.Text.Trim();
        if (int.TryParse(_intervalBox.Text.Trim(), out var sec))
            Config.RefreshIntervalSec = Math.Max(1, sec);
        DialogResult = DialogResult.OK;
        Close();
    }
}
