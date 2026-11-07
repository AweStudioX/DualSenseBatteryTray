<div align="center">

# Controller Battery Tray

**手柄电量，抬眼可见。**

Windows 托盘电量工具 · DualSense · XInput · 飞智八爪鱼 4

**简体中文** · [English](README.en.md)

[下载稳定版](https://github.com/AweStudioX/DualSenseBatteryTray/releases/latest) · [支持范围](#支持范围) · [从源码构建](#从源码构建) · [反馈问题](https://github.com/AweStudioX/DualSenseBatteryTray/issues)

</div>

> **版本说明：**原生蓝牙支持已加入当前源码，尚待实机验证；已发布的 **v1.0.2 不包含此功能**。以下截图来自本机 USB DualSense，不能作为蓝牙实测证据。

## 界面预览

![本机 USB DualSense 的详情窗口](docs/images/controller-status.png)

### 两种托盘布局

| Two icons · 默认双图标 | Compact · 紧凑单图标 |
| :---: | :---: |
| ![双图标：手柄与百分比](docs/images/controller-tray.png) | ![Compact：单图标内显示手柄与数字](docs/images/controller-tray-compact.png) |
| 手柄图标 + 带百分号的电量，优先可读性 | 手柄与电量合并，节省托盘空间 |

本机使用竖向任务栏，以上均为原生像素截图。Compact 图标内省略百分号，悬停提示与详情窗口仍显示完整电量。右键图标 → **Tray layout** 可切换；选择会保存，切换不会重启手柄读取。

## 功能

- **托盘电量与充电状态**：点击图标查看设备、连接方式、电量和状态。
- **低电量提醒**：放电时到达或低于 20% / 10% 提醒，避免重复通知。
- **随手柄出现与隐藏**：后台 Watcher 在登录后检测设备；DualSense 通过报告序号和时间戳判断存活，避免把常插接收器误认为开机手柄。
- **主题适配**：图标跟随 Windows 系统明暗主题。
- **当前用户安装**：安装包包含 .NET 运行时，无需单独下载，可从 Windows“已安装的应用”卸载。

## 支持范围

| 手柄 / 连接方式 | 当前源码 | 电量来源与限制 | 实测情况 |
| --- | --- | --- | --- |
| DualSense / USB HID | 支持 | 约 10% 档位换算，例如 55% 不表示精确到 1% | 本机读取与截图已确认；游戏共存需按环境验证 |
| DualSense / 原生蓝牙 | 已实现，预览 | 读取完整蓝牙报告并校验 CRC；打开连接时请求校准 Feature Report，以启用完整报告 | **待实机验证**：首次配对、重连、休眠恢复、Steam Input 共存 |
| XInput 兼容手柄 | 支持检测，电量取决于上报 | 已知无线电池类型约为 0% / 25% / 50% / 100% 四档；按有线设备上报的接收器显示未知 | 不承诺所有型号和接收器 |
| 飞智八爪鱼 4 / 2.4 GHz | 支持本地服务读取 | 依赖空间站后台服务的 fcs.sock 通道及 k2 身份；0–5 等级映射为近似电量 | 当前适配不代表所有固件均实测 |
| DualSense Edge / DualShock 4 / 其他飞智型号 | 暂不承诺支持 | 需要单独适配与验证 | 未覆盖 |

无法读取时显示问号，不用 100% 代替未知值。飞智充电时可能只有状态，没有百分比。蓝牙简化报告、损坏报告与不支持的报告不会被当作电量数据。

## 安装与使用

1. 从 [Releases](https://github.com/AweStudioX/DualSenseBatteryTray/releases/latest) 下载 Windows x64 的 Setup.exe。
2. 安装后连接受支持手柄；点击托盘图标打开详情，右键管理布局、刷新和登录后监听。
3. 手柄断开或持续停止有效上报后，应用退出并移除图标；后台 Watcher 保留检测能力。
4. 卸载：**设置 → 应用 → 已安装的应用 → DualSense Battery Tray**。

### Rust 原生版（独立预览）

仓库另有 [Rust 原生版](rust/Cargo.toml)，使用 Win32 窗口与托盘，不要求用户安装 .NET。它已实现双图标/Compact 切换、低电量通知、登录后监听和多手柄选择；下一次版本标签的 Release 流程会额外生成独立的 `ControllerBatteryNative-...-Setup.exe` 与 ZIP，不会覆盖上面的 C# 稳定版。首次安装后的监听在后台运行，手柄出现才打开托盘；从开始菜单可以打开详情窗口。

当前本机安装/升级/卸载烟测和短时资源测量见 [原生版验证记录](docs/verification/2026-11-07-rust-native.md)。重连、休眠恢复及 Steam/游戏共存仍需实机验收，暂不把它标为默认稳定下载。

安装包目前未签名，Windows SmartScreen 可能显示提示。请核对 GitHub Release 来源与附带 SHA-256。ZIP 为手动安装包，**不是便携版**；解压后运行 install.ps1，移除使用 uninstall.ps1。Setup 安装请从 Windows 应用列表卸载。

## 原生蓝牙（源码预览）

在 Windows 中先完成 DualSense 蓝牙配对，然后运行从当前源码构建的版本。应用使用同一 Sony VID/PID 识别设备，根据 HID 报告长度区分 USB 与蓝牙，并在详情中显示 **Bluetooth**。

蓝牙连接打开时读取校准 Feature Report 0x05，请求手柄提供含电量的完整报告；不会写入校准值。若请求失败且无法获得完整报告，应用不会编造电量，Watcher 会在后续检测中重新尝试。此路径已有自动测试，硬件验证仍待完成。

## 数据与兼容性

- HID 保持共享只读访问；不创建虚拟手柄、不映射输入、不发送灯光、震动或按键控制命令。
- USB 不请求 Feature Report；蓝牙额外进行上述 Feature Report 读取。因此“完全不访问 Feature Report”不适用于新版蓝牙路径。
- XInput 使用电池状态 API；飞智通过本地空间站服务查询。
- 日志与布局偏好保存在本地，不上传按键或摇杆数据。HidHide、独占访问或设备隐藏可能导致电量不可读。
- 蓝牙模式切换可能影响其他软件对手柄报告格式的假设；Steam Input 与游戏共存需实机确认。

## 从源码构建

需要 Windows x64 和 .NET 8 SDK。

~~~powershell
dotnet restore DualSenseBatteryTray.sln
dotnet test DualSenseBatteryTray.sln -c Release
dotnet publish src/DualSenseBatteryTray.App/DualSenseBatteryTray.App.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o artifacts/app-publish
dotnet publish src/DualSenseBatteryTray.Watcher/DualSenseBatteryTray.Watcher.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o artifacts/watcher-publish
.\scripts\merge-publish.ps1 -AppDirectory (Resolve-Path artifacts/app-publish).Path -WatcherDirectory (Resolve-Path artifacts/watcher-publish).Path -OutputDirectory (Join-Path $PWD artifacts/publish)
.\scripts\install.ps1 -PublishDirectory (Resolve-Path artifacts/publish).Path
~~~

发布 version tag 会触发 Windows Release 流程：测试、构建自包含应用与 Watcher、构建并验证安装包、上传产物及校验文件。详见 [.github/workflows/release.yml](.github/workflows/release.yml)。

## 致谢

- [dualshock-tools](https://github.com/dualshock-tools/dualshock-tools.github.io)：电量解析依据，MIT 声明见 [LICENSES](LICENSES/dualshock-tools-MIT.txt)。
- [JKWTCN/BatteryMonitor](https://github.com/JKWTCN/BatteryMonitor)：飞智本地服务协议封装参考，见 [MIT 声明](LICENSES/batterymonitor-MIT.txt)。
- [Microsoft Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons)：手柄图标素材，见 [MIT 声明](LICENSES/fluentui-system-icons-MIT.txt)。
- 蓝牙报告布局和 CRC 依据 [Linux hid-playstation](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c) 的协议定义独立实现；完整报告初始化行为参考 [go-dualsense-battery](https://github.com/neolit123/go-dualsense-battery/blob/main/main.go)。
