using IpMonitor.Monitor;

namespace IpMonitor.UI;

/// <summary>
/// 悬浮窗 - 默认屏幕右侧中间靠边, 显示当前IP+区域+延迟+状态.
/// </summary>
public class FloatingBar : Form
{
    private readonly Label _ipLabel;
    private readonly Label _regionLabel;
    private readonly Label _latencyLabel;
    private readonly Label _statusDot;
    private Point _dragStart;
    private bool _didDrag;
    private ContextMenuStrip? _lastMenu;
    private ContextMenuStrip? _externalMenu;

    public event EventHandler? RefreshRequested;

    public FloatingBar()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(245, 245, 240);
        Size = new Size(320, 40);
        MinimumSize = new Size(220, 40);

        var screen = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(screen.Right - Width, screen.Top + screen.Height / 2 - Height / 2);

        // 状态点
        _statusDot = new Label
        {
            AutoSize = false,
            Size = new Size(12, 12),
            Location = new Point(6, 14),
            BackColor = Color.Gray,
            BorderStyle = BorderStyle.None
        };

        // IP (第1行)
        _ipLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Consolas", 11f, FontStyle.Bold),
            ForeColor = Color.Black,
            Location = new Point(22, 3),
            Text = "--.--.--.--"
        };

        // 区域+ISP (第2行)
        _regionLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei", 8f, FontStyle.Regular),
            ForeColor = Color.FromArgb(80, 80, 80),
            Location = new Point(22, 21),
            Text = "未连接"
        };

        // 延迟 (右侧, 跨两行垂直居中)
        _latencyLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Consolas", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(120, 120, 120),
            Location = new Point(220, 12),
            Text = "--ms"
        };

        Controls.AddRange(new Control[] { _statusDot, _ipLabel, _regionLabel, _latencyLabel });

        // 拖动 + 左键单击=立即刷新 + 右键=菜单
        void onDown(object? s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _dragStart = e.Location; _didDrag = false; } }
        void onMove(object? s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (!_didDrag && (Math.Abs(e.X - _dragStart.X) > 5 || Math.Abs(e.Y - _dragStart.Y) > 5))
                    _didDrag = true;
                if (_didDrag)
                    Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y);
            }
        }
        void onUp(object? s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !_didDrag)
                RefreshRequested?.Invoke(this, EventArgs.Empty);
            _didDrag = false;
        }
        void onRight(object? s, MouseEventArgs e) { if (e.Button == MouseButtons.Right) ShowCombinedMenu(); }

        foreach (Control c in Controls)
        {
            c.MouseDown += onDown;
            c.MouseMove += onMove;
            c.MouseUp += onUp;
            c.MouseClick += onRight;
        }
        MouseDown += onDown;
        MouseMove += onMove;
        MouseUp += onUp;
    }

    public void SetExternalMenu(ContextMenuStrip menu) { _externalMenu = menu; }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) ShowCombinedMenu();
        base.OnMouseClick(e);
    }

    private void ShowCombinedMenu()
    {
        _lastMenu?.Dispose();
        var menu = new ContextMenuStrip();
        _lastMenu = menu;

        var hide = new ToolStripMenuItem("隐藏悬浮窗");
        hide.Click += (_, _) => Hide();
        menu.Items.Add(hide);

        var pin = new ToolStripMenuItem(TopMost ? "✓ 固定前台" : "  固定前台");
        pin.Click += (_, _) => { TopMost = !TopMost; };
        menu.Items.Add(pin);

        menu.Items.Add(new ToolStripSeparator());

        if (_externalMenu != null)
        {
            foreach (ToolStripItem item in _externalMenu.Items)
            {
                if (item is ToolStripMenuItem mi)
                {
                    var clone = new ToolStripMenuItem(mi.Text);
                    var src = mi;
                    clone.Click += (_, _) => src.PerformClick();
                    foreach (ToolStripItem sub in mi.DropDownItems)
                    {
                        if (sub is ToolStripMenuItem subMi)
                        {
                            var subClone = new ToolStripMenuItem(subMi.Text);
                            var subSrc = subMi;
                            subClone.Click += (_, _) => subSrc.PerformClick();
                            clone.DropDownItems.Add(subClone);
                        }
                    }
                    menu.Items.Add(clone);
                }
                else if (item is ToolStripSeparator)
                {
                    menu.Items.Add(new ToolStripSeparator());
                }
            }
        }

        var pos = Cursor.Position;
        var screen = Screen.FromPoint(pos).WorkingArea;
        menu.Show(pos);
        if (menu.Right > screen.Right) menu.Left = pos.X - menu.Width;
        if (menu.Bottom > screen.Bottom) menu.Top = pos.Y - menu.Height;
    }

    public void ToggleVisibility()
    {
        if (Visible) Hide();
        else { Show(); TopMost = true; BringToFront(); }
    }

    public void UpdateDisplay(IpStatus st)
    {
        var ip = string.IsNullOrEmpty(st.CurrentIp) ? "等待中..." : st.CurrentIp;
        _ipLabel.Text = ip;

        // 区域行: 国家+省+市 +(ISP简称); 低置信场景仍显示地理, 后缀加分歧标记
        string baseRegion = st.GeoSummary() + (string.IsNullOrEmpty(st.Isp) ? "" : "  " + ShortenIsp(st.Isp));
        string regionText = st.Kind switch
        {
            IpStatusKind.NoNetwork => "网络异常",
            IpStatusKind.LowConfidence => baseRegion + $"  ⚡分流{st.ProviderHits}/{st.ProviderTotal}",
            IpStatusKind.MatchedLowConf => baseRegion + "  ⚡分流",
            _ => baseRegion
        };
        _regionLabel.Text = regionText;

        _latencyLabel.Text = st.LatencyMs > 0 ? $"{st.LatencyMs}ms" : "--ms";

        // 状态点颜色
        _statusDot.BackColor = st.Kind switch
        {
            IpStatusKind.Matched         => Color.FromArgb(50, 200, 50),    // 绿: 匹配且高置信
            IpStatusKind.MatchedLowConf  => Color.FromArgb(120, 200, 120),  // 浅绿: 匹配但低置信
            IpStatusKind.LowConfidence   => Color.FromArgb(255, 165, 0),    // 橙: 多源分歧
            IpStatusKind.Changed         => Color.FromArgb(220, 50, 50),    // 红: 已漂移
            IpStatusKind.NoNetwork       => Color.FromArgb(120, 120, 120),  // 灰: 无网
            _ => Color.Gray
        };

        _ipLabel.ForeColor = st.Kind == IpStatusKind.Changed ? Color.FromArgb(180, 0, 0) : Color.Black;

        // 强制重排让 AutoSize 标签更新自己的Width
        _ipLabel.PerformLayout();
        _regionLabel.PerformLayout();

        // 计算宽度: 取IP行和区域行最右侧, 延迟放最右
        var contentRight = Math.Max(_ipLabel.Right, _regionLabel.Right);
        _latencyLabel.Location = new Point(contentRight + 12, 12);
        var totalWidth = _latencyLabel.Right + 8;
        Width = Math.Max(MinimumSize.Width, totalWidth);
    }

    private static string ShortenIsp(string isp)
    {
        if (string.IsNullOrEmpty(isp)) return "";
        // 长ISP名截断, 避免悬浮窗过宽; 鼠标悬停tooltip可看完整
        if (isp.Length <= 18) return isp;
        return isp.Substring(0, 16) + "...";
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
            return cp;
        }
    }
}
