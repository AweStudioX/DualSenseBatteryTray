# Controller Battery Tray

[简体中文](README.md) · [English](README.en.md)

在 Windows 托盘直接看手柄电量。连接时显示，断开后隐藏；低电量时提醒。

**Windows 10/11 · x64 · 当前稳定版 v1.0.2**

[下载安装包](https://github.com/AweStudioX/DualSenseBatteryTray/releases/latest) · [查看支持范围](#支持的手柄) · [反馈问题](https://github.com/AweStudioX/DualSenseBatteryTray/issues)

![本机 USB DualSense 的状态窗口：55%，正在放电](docs/images/controller-status.png)

*本机 USB DualSense 实拍。电量按手柄上报档位换算；55% 不表示精确到 1%。*

## 三步开始

1. 从 [Releases](https://github.com/AweStudioX/DualSenseBatteryTray/releases/latest) 下载 Windows x64 的 **Setup.exe**，运行安装。
2. 连接手柄。托盘出现手柄图标和电量；点击图标查看详情。
3. 右键图标切换 **Tray layout → Two icons / Compact**。低电量会在 20% 和 10% 提醒。

安装包已包含 .NET 运行时，按当前用户安装。卸载请到 **Windows 设置 → 应用 → 已安装的应用**。安装包尚未签名，下载时可对照 Release 页面附带的 SHA-256。

## 看起来是什么样

托盘提供两种布局，右键即可切换，选择会在下次连接时保留：

- **Two icons（默认）**：手柄图标和带百分号的电量分开，读数更清楚。
- **Compact**：手柄与数字合为一个图标，节省空间。

<details>
<summary>展开查看两种布局的实机截图</summary>

| Two icons | Compact |
| :---: | :---: |
| <img src="docs/images/controller-tray.png" alt="双图标托盘截图" width="72"> | <img src="docs/images/controller-tray-compact.png" alt="Compact 单图标托盘截图" width="72"> |

截图来自本机竖向任务栏，按原生像素显示。Compact 图标内省略百分号，悬停提示和详情窗口仍显示完整电量。

</details>

## 支持的手柄

| 设备 / 连接方式 | 状态 | 电量说明 |
| --- | --- | --- |
| DualSense · USB | 已在本机验证 | 电量按约 10% 档位换算，显示充电状态 |
| XInput 兼容手柄 | 支持检测 | 无线设备通常只有约 0 / 25 / 50 / 100% 四档；部分接收器不提供电量 |
| 飞智八爪鱼 4 · 2.4 GHz | 通过飞智空间站后台服务读取 | 依赖本地服务；0–5 档映射为近似百分比 |
| DualSense · 原生蓝牙 | 源码预览，**尚未实机验证** | 当前 v1.0.2 安装包不包含此功能 |
| DualSense Edge、DualShock 4、其他飞智型号 | 未承诺支持 | 需要单独适配和实测 |

读不到时显示 **?**，不会用 100% 代替未知电量。手柄型号、固件和接收器可能影响结果；欢迎在 [Issues](https://github.com/AweStudioX/DualSenseBatteryTray/issues) 留下设备和连接方式。

## 蓝牙预览版说明

当前源码加入了 DualSense 原生蓝牙报告读取、CRC 校验和断连判断。蓝牙连接时会读取校准 Feature Report，以请求包含电量的完整报告。**这部分还未用实物验证**；首次配对、重连、休眠恢复和 Steam Input 共存仍待测试。想尝试请[从源码构建](#从源码构建)，不要下载 v1.0.2 后期待蓝牙功能。

## Rust 原生版（独立预览）

仓库另有 [Rust 原生版](rust/Cargo.toml)，使用 Win32 窗口与托盘，不要求用户安装 .NET。它已实现双图标/Compact 切换、低电量通知、登录后监听和多手柄选择；下一次版本标签的 Release 流程会额外生成独立的 `ControllerBatteryNative-...-Setup.exe` 与 ZIP，不会覆盖 C# 稳定版。首次安装后的监听在后台运行，手柄出现才打开托盘；从开始菜单可以打开详情窗口。

当前本机安装/升级/卸载烟测和短时资源测量见 [原生版验证记录](docs/verification/2026-11-07-rust-native.md)。重连、休眠恢复及 Steam/游戏共存仍需实机验收，暂不把它标为默认稳定下载。

## 从源码构建

需要 Windows x64 和 .NET 8 SDK。

```powershell
dotnet restore DualSenseBatteryTray.sln
dotnet test DualSenseBatteryTray.sln -c Release
dotnet publish src/DualSenseBatteryTray.App/DualSenseBatteryTray.App.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o artifacts/app-publish
dotnet publish src/DualSenseBatteryTray.Watcher/DualSenseBatteryTray.Watcher.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o artifacts/watcher-publish
.\scripts\merge-publish.ps1 -AppDirectory (Resolve-Path artifacts/app-publish).Path -WatcherDirectory (Resolve-Path artifacts/watcher-publish).Path -OutputDirectory (Join-Path $PWD artifacts/publish)
.\scripts\install.ps1 -PublishDirectory (Resolve-Path artifacts/publish).Path
```

Release 中的 ZIP 也需要运行附带的安装脚本，并非便携版；ZIP 安装请用附带的 uninstall.ps1 卸载。源码的构建与发布流程见 [release.yml](.github/workflows/release.yml)。

<details>
<summary>读取方式、隐私与致谢</summary>

DualSense 使用共享只读 HID 访问；XInput 使用电池状态 API；飞智读取本地空间站服务。应用不创建虚拟手柄，不映射输入，也不发送灯光、震动或按键控制命令。蓝牙预览路径会额外读取一次 Feature Report。日志和布局偏好保存在本地；设备隐藏或独占访问可能导致电量不可读。

电量解析参考 [dualshock-tools](https://github.com/dualshock-tools/dualshock-tools.github.io)，飞智本地服务协议参考 [BatteryMonitor](https://github.com/JKWTCN/BatteryMonitor)，图标素材来自 [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons)。授权声明见 [LICENSES](LICENSES)。蓝牙报告布局参考 [Linux hid-playstation](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c)。

</details>
