using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using IpMonitor.Monitor;

namespace IpMonitor;

public static class TrayIconRenderer
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// 16x16托盘图标: 第1行"IP", 第2行最后2位数字(如.248), 颜色随状态.
    /// </summary>
    public static Icon Render(string ip, IpStatusKind kind)
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        var bg = kind switch
        {
            IpStatusKind.Matched => Color.FromArgb(0, 120, 0),
            IpStatusKind.Inconsistent => Color.FromArgb(180, 130, 0),
            IpStatusKind.Changed => Color.FromArgb(200, 0, 0),
            IpStatusKind.NoNetwork => Color.FromArgb(80, 80, 80),
            _ => Color.FromArgb(40, 40, 40)
        };
        g.Clear(bg);

        using var font = new Font("Consolas", 6f, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString("IP", font, Brushes.White, -1, 0);

        // 取IP最后一段, 如 8.147.70.248 -> "248"
        var tail = ExtractLastOctet(ip);
        g.DrawString(tail, font, Brushes.White, -1, 8);

        var hIcon = bmp.GetHicon();
        var temp = Icon.FromHandle(hIcon);
        var icon = (Icon)temp.Clone();
        temp.Dispose();
        DestroyIcon(hIcon);
        return icon;
    }

    private static string ExtractLastOctet(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return "--";
        var idx = ip.LastIndexOf('.');
        if (idx < 0 || idx == ip.Length - 1) return "--";
        var tail = ip.Substring(idx + 1);
        return tail.Length <= 3 ? tail : tail.Substring(0, 3);
    }

    public static string BuildTooltip(IpStatus st)
    {
        var status = st.Kind switch
        {
            IpStatusKind.Matched => "✓ IP一致",
            IpStatusKind.Inconsistent => "⚠ 多源不一致",
            IpStatusKind.Changed => "✗ IP已变化",
            IpStatusKind.NoNetwork => "✗ 网络异常",
            _ => "初始化中"
        };
        var ip = string.IsNullOrEmpty(st.CurrentIp) ? "--" : st.CurrentIp;
        var loc = st.GeoSummary();
        var line1 = $"IP: {ip}  ({status})";
        var line2 = $"区域: {loc}";
        var line3 = $"延迟: {st.LatencyMs}ms  命中: {st.ProviderHits}/{st.ProviderTotal}";
        var tip = $"{line1}\n{line2}\n{line3}";
        return tip.Length > 127 ? tip[..127] : tip;
    }
}
