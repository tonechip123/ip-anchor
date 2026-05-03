# IP监控 (ip-monitor)

> Windows 桌面悬浮窗 — 多源验证当前公网 IP，实时检测 IP 漂移并报警。

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-blue.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)]()

## 用途

针对 **ChatGPT / Claude 等境外 AI 账号**的 IP 风控场景：长期通过代理走固定地区节点（如美国洛杉矶）登录账号，**节点出口 IP 一旦漂移**（节点维护、IP 池轮换、配置异常）会被平台判定异常 → 弹验证码 / 封号 / 禁用。

本工具直接探测 **chatgpt.com / claude.ai** 的 cdn-cgi trace 接口，拿到的就是这两个平台看到你的真实出口 IP。锚定它，一旦漂移：

1. 弹窗报警
2. 悬浮窗状态点变红并闪烁
3. 显示当前 IP 和预期 IP 的差异

**全程不修改系统设置，纯监控工具。**

## 工作原理

1. **每 1 秒**（可配）并发查询 7 个站点，按权重分三级：

   | 等级 | 站点 | 权重 | 用途 |
   |---|---|---|---|
   | **业务直探** | `chatgpt.com/cdn-cgi/trace` | 3 | ChatGPT 看到的真实出口 IP |
   | **业务直探** | `claude.ai/cdn-cgi/trace` | 3 | Claude 看到的真实出口 IP |
   | **CF 通用** | `www.cloudflare.com/cdn-cgi/trace` | 2 | Cloudflare 边缘节点（含 colo 节点代码 LAX/SJC 等） |
   | **普通 IP** | `api.ipify.org` | 1 | 兜底 |
   | **普通 IP** | `icanhazip.com` | 1 | 兜底 |
   | **普通 IP** | `api.ip.sb/ip` | 1 | 兜底 |
   | **普通 IP** | `api.myip.com` | 1 | 兜底 |

2. **决策优先级**：
   - **业务直探任一成功** → 用其 IP（这就是 GPT/Claude 看到的 IP，最权威）
   - 否则 **加权投票**：累计加权 ≥ 3 即高置信
   - 全分歧 → 取延迟最低的成功站点（低置信，仅展示不报警）
3. **地理位置**：用 `ip-api.com` 中文返回反查 `国家+省市+ISP`。
4. **比对锚定 IP**：
   - 相同 → 状态绿点 ✓
   - 不同 → 红点闪烁 + 弹窗报警
5. **不做 ICMP ping**：所有查询都是 HTTPS GET，4 站点 × 每天 86400 次远低于免费配额，不会被封禁。
6. **直连查询**：HTTP 客户端 `UseProxy = false`，绕过系统代理，避免查到的是代理节点 IP。

## 显示

托盘悬浮窗（默认屏幕右侧中间）：

```
● 23.172.40.55              285ms
  美国 加州 洛杉矶  AS-CHOOPA
↑     ↑                      ↑
状态点 IP+区域+ISP            平均延迟
```

状态点颜色：
- 🟢 绿 = 与锚定 IP 一致
- 🟡 黄 = 多源结果不一致（暂不可信）
- 🔴 红闪烁 = IP 已漂移（报警中）
- ⚫ 灰 = 网络异常

## 操作

- **左键单击悬浮窗** → 立即刷新一次
- **拖动悬浮窗** → 移动位置
- **双击托盘图标** → 显示/隐藏悬浮窗
- **右键菜单**：
  - 立即刷新
  - **锁定当前IP为预期** ← 第一次使用先点这个
  - 清除预期IP
  - 设置...
  - 查看详情
  - 退出

## 编译

环境：.NET 9 SDK + Windows 10/11

```bash
git clone https://github.com/tonechip123/ip-monitor.git
cd ip-monitor

dotnet build IpMonitor/IpMonitor.csproj -c Release

# 发布单文件 exe (~108MB, 内嵌 .NET 9 运行时)
dotnet publish IpMonitor/IpMonitor.csproj -c Release -r win-x64 \
    --self-contained -p:PublishSingleFile=true
# 输出: IpMonitor/bin/Release/net9.0-windows/win-x64/publish/IpMonitor.exe
```

## 项目结构

```
ip-monitor/
└── IpMonitor/
    ├── Api/
    │   ├── IpProvider.cs       7个IP查询站点 (HTTPS GET, 直连不走代理)
    │   ├── IpResolver.cs       并发查询 + 多数票决
    │   └── GeoLookup.cs        ip-api.com 反查地理位置
    ├── Monitor/
    │   ├── IpMonitorEngine.cs  主监控循环 (1秒轮询)
    │   └── IpStatus.cs         状态DTO
    ├── UI/
    │   ├── FloatingBar.cs      悬浮窗 (含闪烁效果)
    │   └── SettingsForm.cs     设置对话框
    ├── Program.cs              入口 + 全局异常捕获
    ├── TrayApplicationContext.cs  托盘主逻辑
    ├── TrayIconRenderer.cs     16×16 动态图标
    ├── ConfigManager.cs        config.json 持久化
    └── monitor.ico
```

## 安全说明

- 所有 IP 查询走 HTTPS，不会泄露真实 IP 给中间人
- 不需要管理员权限
- 配置和日志写在 `%APPDATA%\IpMonitor\`，exe 同目录纯净
- 卸载只需删 exe + 该目录

## 协议

[MIT](LICENSE)
