# IP锚定 (ip-anchor)

> Windows 桌面悬浮窗 — 多源验证当前公网 IP，检测到 IP 漂移立即把 Clash 切到 DIRECT 模式，**不杀进程、不动注册表、不影响本地网络**。

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-blue.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)]()

## 用途

如果你长期通过 Clash / mihomo 等代理客户端走固定地区节点（例如长期使用洛杉矶节点），**节点出口 IP 一旦悄悄漂移到其他城市/国家**（节点维护、IP 池轮换、配置异常等），可能导致依赖 IP 稳定性的应用产生异常。本工具持续监控公网 IP 是否仍是你"锚定"的那一个，一旦漂移：

1. 弹窗 + 提示音报警
2. 自动调用 Clash REST API 把所有 Selector 切到 DIRECT —— 流量立即走本机直连，停止使用漂移后的代理出口
3. 提供"一键恢复"菜单（或重启 Clash 自动恢复）

**全程不杀进程、不修改系统代理注册表，本地网络不会断。**

## 工作原理

1. **每 30 秒**（可配，最小 10s）并发查询 4 个独立站点：
   - `api.ipify.org`
   - `icanhazip.com`
   - `api.ip.sb`
   - `api.myip.com`
2. **多数票决**：≥2 个站点返回相同 IP → 认定为"真实公网 IP"。否则标记 *多源不一致*，避免单一服务故障误触发。
3. **地理位置**：用 `ip-api.com` 中文返回反查 `国家+省市+ISP`。
4. **比对锚定 IP**：
   - 相同 → 状态绿点 ✓
   - 不同 → 红点 + 弹窗 + 自动切 Clash 到 DIRECT
5. **不做 ICMP ping**：所有查询都是 HTTPS GET，4 站点 × 每天 2880 次远低于免费配额，不会被封禁。
6. **直连查询**：HTTP 客户端 `UseProxy = false`，绕过系统代理，避免查到的是代理节点 IP。

## 自动识别 Clash

启动时自动扫描 Clash 进程（`clash` / `mihomo` / `Clash for Windows` / `Clash Verge` 等），从其配置目录里解析 `external-controller` 和 `secret`，免去手动配置。**软件可任意拷贝到其他电脑直接运行**。

如自动识别失败，右键菜单 →「重新检测 Clash API」或「设置...」手动填入。

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
- 🔴 红 = IP 已漂移（已自动切 DIRECT）
- ⚫ 灰 = 网络异常

## 操作

- **左键单击悬浮窗** → 立即刷新一次
- **拖动悬浮窗** → 移动位置
- **双击托盘图标** → 显示/隐藏悬浮窗
- **右键菜单**：
  - 立即刷新
  - **锁定当前IP为预期** ← 第一次使用先点这个
  - 清除预期IP
  - 手动切到 DIRECT（临时断开代理）
  - **恢复 Clash 代理**（切回原节点）
  - 重新检测 Clash API
  - 测试 Clash API 连通性
  - 设置...
  - 查看详情
  - 退出

## 编译

环境：.NET 9 SDK + Windows 10/11

```bash
git clone https://github.com/tonechip123/ip-anchor.git
cd ip-anchor

dotnet build IpMonitor/IpMonitor.csproj -c Release

# 发布单文件 exe (~108MB, 内嵌 .NET 9 运行时)
dotnet publish IpMonitor/IpMonitor.csproj -c Release -r win-x64 \
    --self-contained -p:PublishSingleFile=true
# 输出: IpMonitor/bin/Release/net9.0-windows/win-x64/publish/IpAnchor.exe
```

## 项目结构

```
ip-anchor/
└── IpMonitor/
    ├── Api/
    │   ├── IpProvider.cs       4个IP查询站点 (HTTPS GET, 直连不走代理)
    │   ├── IpResolver.cs       并发查询 + 多数票决
    │   └── GeoLookup.cs        ip-api.com 反查地理位置
    ├── Monitor/
    │   ├── ClashAutoDetect.cs  扫描进程 + 解析yaml, 自动识别URL/Secret
    │   ├── ClashController.cs  REST API 切 DIRECT / 恢复 / 测试连通
    │   ├── IpMonitorEngine.cs  主监控循环
    │   └── IpStatus.cs         状态DTO
    ├── UI/
    │   ├── FloatingBar.cs      悬浮窗
    │   └── SettingsForm.cs     设置对话框
    ├── Program.cs              入口 + 全局异常捕获
    ├── TrayApplicationContext.cs  托盘主逻辑
    ├── TrayIconRenderer.cs     16×16 动态图标
    ├── ConfigManager.cs        config.json 持久化
    └── monitor.ico
```

## 安全说明

- 所有 IP 查询走 HTTPS，不会泄露真实 IP 给中间人
- 不杀任何进程、不修改 Windows 注册表
- 仅通过 Clash 官方 REST API 切换 Selector，**等同于你在 Clash GUI 里手动切 DIRECT**
- 不需要管理员权限
- 数据全部留在 exe 同目录（`config.json` / `logs/`），不写入系统目录
- 重启 Clash 自动恢复原节点选择（Clash 自身的持久化行为）

## 协议

[MIT](LICENSE)
