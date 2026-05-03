# IpMonitor 项目规则

## 项目概述
IP监控工具 - 多源验证公网IP，实时检测IP漂移并报警。专为 Claude/ChatGPT 等境外AI服务的IP稳定性监控设计。

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

## 文件结构
```
IpMonitor/
├── Api/
│   ├── IpProvider.cs       # 7个IP查询站点定义
│   ├── IpResolver.cs       # 并发查询+加权投票
│   └── GeoLookup.cs        # 地理位置反查
├── Monitor/
│   ├── IpMonitorEngine.cs  # 主监控循环（1秒轮询）
│   └── IpStatus.cs         # 状态数据模型
├── UI/
│   ├── FloatingBar.cs      # 悬浮窗（含红色闪烁效果）
│   └── SettingsForm.cs     # 设置对话框
├── Program.cs              # 程序入口
├── TrayApplicationContext.cs  # 托盘主逻辑
├── TrayIconRenderer.cs     # 托盘图标渲染
├── ConfigManager.cs        # 配置管理
└── app.ico                 # 应用图标
```

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
