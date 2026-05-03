# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述
IP监控工具 - 多源验证公网IP，实时检测IP漂移并报警。专为 Claude/ChatGPT 等境外AI服务的IP稳定性监控设计。

**技术栈**: .NET 9.0 Windows Forms，单文件发布（~108MB，含运行时）

## 开发命令

### 构建和发布
```bash
# 开发构建
cd IpMonitor
dotnet build -c Release

# 单文件发布（推荐）
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ../publish

# 更新项目根目录的可执行文件
taskkill /F /IM IpMonitor.exe  # 先关闭运行中的进程
Copy-Item publish\IpMonitor.exe .
```

### 测试运行
```bash
# 直接运行编译后的程序
.\IpMonitor\bin\Release\net9.0-windows\IpMonitor.exe

# 或运行发布版本
.\publish\IpMonitor.exe
```

## 核心架构

### 数据流
```
IpMonitorEngine (每1秒)
  ↓
IpResolver.Resolve() → 并发查询7个站点 (Task.WhenAll)
  ↓
加权投票决策 (业务直探优先 > 加权≥3 > 延迟最低)
  ↓
GeoLookup.Lookup() → 地理位置反查
  ↓
比对预期IP → 触发 StatusUpdated / IpChanged 事件
  ↓
TrayApplicationContext → 更新UI (悬浮窗 + 托盘图标)
```

### IP决策算法 (IpResolver.cs)
1. **业务直探优先** (权重3): Claude/ChatGPT 的 cdn-cgi/trace 接口，任一成功直接使用
2. **加权投票** (权重≥3): CF通用(2) + 普通站点(1)，累计加权≥3为高置信
3. **延迟兜底** (低置信): 全分歧时选延迟最低的站点，仅展示不报警

### 事件驱动模型
- `IpMonitorEngine.StatusUpdated`: 每次检测后触发，更新UI显示
- `IpMonitorEngine.IpChanged`: IP漂移时触发，显示报警（红色闪烁 + 弹窗）
- `ContextMenuStrip.Opening`: 菜单弹出前触发，更新勾选状态

### UI组件职责
- **TrayApplicationContext**: 托盘主逻辑，管理菜单、事件订阅、配置更新
- **FloatingBar**: 悬浮窗，显示IP/地理位置/延迟，IP漂移时红色闪烁（500ms间隔）
- **SettingsForm**: 设置对话框，修改预期IP和刷新间隔

## 核心功能
1. **多源IP验证**：并发查询7个站点（Claude/ChatGPT直探 + CF通用 + 普通IP站点），加权投票决策
2. **实时监控**：默认1秒刷新一次，检测IP变化
3. **IP漂移报警**：悬浮窗红色闪烁（500ms间隔）+ 系统托盘弹窗，无提示音
4. **悬浮窗显示**：显示当前IP、地理位置、ISP、延迟、状态点

## 技术栈
- .NET 9.0 Windows Forms
- 单文件发布（~108MB，含运行时）
- 异步并发查询（Task.WhenAll）

## 重要约定

### 1. 配置管理
- 配置文件路径：`%APPDATA%\IpAnchor\config.json`（历史遗留，暂不改名）
- 配置格式：
  ```json
  {
    "expectedIp": "23.172.40.55",
    "refreshIntervalSec": 1
  }
  ```
- **默认刷新间隔**：1秒（不要设置为30秒）
- 配置序列化：使用 `JsonNamingPolicy.CamelCase`

### 2. 菜单勾选状态
- 锁定/清除IP后必须立即调用 `UpdateMenuStates()` 更新菜单状态
- 菜单弹出前会自动调用 `UpdateMenuStates()`（通过 `Opening` 事件）
- 勾选逻辑：`hasExpected = !string.IsNullOrEmpty(_config.ExpectedIp)`
- **设置功能**: 右键菜单"设置..."打开 SettingsForm，可修改预期IP和刷新间隔，保存后自动更新引擎配置

### 3. 启动行为
- **禁止启动时弹出任何对话框**（MessageBox.Show）
- 调试信息使用 `ShowBalloonTip` 或日志，不要阻塞UI线程
- 构造函数中不要使用阻塞调用

### 4. 图标和资源
- 应用图标：`app.ico`（蓝色圆形背景 + 白色"IP"文字）
- 版本号：当前 1.0.4
- 产品名称：IP监控（不是"IP锚定"）

### 5. 编译和发布
- 编译命令：
  ```bash
  dotnet publish -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -o ../publish
  ```
- 发布后需要：
  1. 关闭正在运行的进程：`taskkill /F /IM IpMonitor.exe`
  2. 复制到项目根目录：`Copy-Item publish\IpMonitor.exe .`
  3. 提交并推送到 GitHub

### 6. Git 提交规范
- 提交信息格式：`类型：简短描述`
- 类型：修复、重构、新增、优化等
- 必须包含 Co-Authored-By 行

## 代码组织

### 关键文件说明

**核心逻辑层**:
- `IpProvider.cs`: 定义7个IP查询站点（Claude/ChatGPT直探、CF通用、普通站点），配置权重和优先级
- `IpResolver.cs`: 并发查询所有站点，实现加权投票算法，决策最终IP
- `IpMonitorEngine.cs`: 主监控循环，每1秒调用IpResolver，比对预期IP，触发事件
- `GeoLookup.cs`: 地理位置反查（ip-api.com），获取国家/城市/ISP信息

**UI层**:
- `TrayApplicationContext.cs`: 托盘应用主逻辑，管理菜单、订阅引擎事件、处理用户操作
- `FloatingBar.cs`: 悬浮窗，显示IP状态，IP漂移时红色闪烁（Timer 500ms间隔）
- `SettingsForm.cs`: 设置对话框，修改预期IP和刷新间隔
- `TrayIconRenderer.cs`: 动态渲染托盘图标，根据状态显示不同颜色点

**配置和入口**:
- `ConfigManager.cs`: JSON配置文件读写（`%APPDATA%\IpAnchor\config.json`）
- `Program.cs`: 程序入口，初始化ApplicationContext
- `IpStatus.cs`: 状态数据模型（IP、地理位置、延迟、置信度等）

**资源文件**:
- `app.ico`: 应用图标（蓝色圆形 + 白色"IP"文字）
- `IpMonitor.csproj`: 项目配置，版本号1.0.4，产品名"IP监控"


## 已移除的功能
- ❌ Clash 自动控制（自动切换 DIRECT + 一键恢复）
- ❌ ClashController.cs 和 ClashAutoDetect.cs
- ❌ 所有 Clash 相关配置和菜单项
- ❌ 启动提示音

## 注意事项
1. 不要在构造函数中使用 `MessageBox.Show()`，会阻塞初始化
2. 锁定/清除IP后必须调用 `UpdateMenuStates()` 更新菜单
3. 配置文件的刷新间隔默认为1秒，不要改为30秒
4. 图标文件名是 `app.ico`，不是 `monitor.ico`
5. 产品名称统一为"IP监控"，不要用"IP锚定"
