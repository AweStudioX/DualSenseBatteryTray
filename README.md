# Controller Battery Tray

[简体中文](README.md) · [English](README.en.md)

在 Windows 托盘直接看手柄电量。连接时显示，断开后隐藏；低电量时提醒。

**Windows 10/11 · x64 · 当前版本 v1.0.3（稳定版 + Rust 预览版）**

[下载安装包](https://github.com/AweStudioX/DualSenseBatteryTray/releases/latest) · [查看支持范围](#支持的手柄) · [反馈问题](https://github.com/AweStudioX/DualSenseBatteryTray/issues)

![本机 DualSense 手柄电量截图：USB 连接，85%](docs/images/controller-status.png)

*本机 DualSense 手柄 USB 连接实拍，显示 85% 电量。截图来自轻量预览版；稳定版界面略有不同。电量是参考值。*

## 三步开始

1. 从 [Releases](https://github.com/AweStudioX/DualSenseBatteryTray/releases/latest) 下载 Windows x64 的 `DualSenseBatteryTray-...-Setup.exe`（稳定版）或 `ControllerBatteryNative-...-Setup.exe`（轻量 Rust 预览版），运行安装。两者请只启用一个自动启动监听器。
2. 连接手柄。托盘出现手柄图标和电量；点击图标查看详情。
3. 右键图标切换 **Tray layout → Two icons / Compact**。低电量会在 20% 和 10% 提醒；Rust 版同档提醒跨重连有 12 小时冷却，降至 10% 仍会升级提醒。

稳定版安装包已包含 .NET 运行时；Rust 版不需要 .NET。两者均按当前用户安装，卸载请到 **Windows 设置 → 应用 → 已安装的应用**。安装包尚未签名，下载时可对照 Release 页面附带的 SHA-256。

## 看起来是什么样

托盘提供两种布局，右键即可切换，选择会在下次连接时保留：

- **Two icons（默认）**：手柄图标和带百分号的电量分开，读数更清楚。
- **Compact**：手柄与数字合为一个图标，节省空间。

<details>
<summary>展开查看两种托盘显示方式的实机截图</summary>

| Two icons | Compact |
| :---: | :---: |
| <img src="docs/images/controller-tray.png" alt="双图标托盘，DualSense USB 85%" width="72"> | <img src="docs/images/controller-tray-compact.png" alt="合并显示托盘，DualSense USB 85%" width="72"> |

截图来自本机竖向任务栏，按原生像素显示。Compact 图标内省略百分号，悬停提示和详情窗口仍显示完整电量；真实任务栏图标尺寸很小。

</details>

## 支持的手柄

| 设备 / 连接方式 | 状态 | 电量说明 |
| --- | --- | --- |
| DualSense · USB | 稳定版与 Rust 版均已本机验证 | 电量按约 10% 档位换算，显示充电状态 |
| XInput 兼容手柄 | 支持检测 | 无线设备通常只有约 0 / 25 / 50 / 100% 四档；部分接收器不提供电量 |
| 飞智八爪鱼 4 · 2.4 GHz | 通过飞智空间站后台服务读取 | 依赖本地服务；0–5 档映射为近似百分比 |
| DualSense · 原生蓝牙 | Rust 版已实现，**尚未实机验证** | 稳定版不包含原生蓝牙读取 |
| DualSense Edge、原装 DualShock 4 | Rust 版已实现，**尚未实机验证** | USB／蓝牙完整 HID 报告；无数据时不伪造百分比 |
| Switch Pro／Joy-Con | 尚未适配专有电量协议 | 不主动切换报告模式，以免影响游戏输入 |
| 其他飞智型号 | 未承诺支持 | 需要逐型号实测 |

读不到时显示 **?**，不会用 100% 代替未知电量。手柄型号、固件和接收器可能影响结果；欢迎在 [Issues](https://github.com/AweStudioX/DualSenseBatteryTray/issues) 留下设备和连接方式。

## 蓝牙预览版说明

Rust 版加入了 DualSense 原生蓝牙报告读取、CRC 校验和断连判断。蓝牙连接时会读取校准 Feature Report，以请求包含电量的完整报告。**这部分还未用实物验证**；首次配对、重连、休眠恢复和 Steam Input 共存仍待测试。请下载 Rust 预览版，不要下载 C# 稳定版后期待此功能。

## Rust 原生版（独立预览）

仓库另有 [Rust 原生版](rust/Cargo.toml)，使用 Win32 窗口与托盘，不要求用户安装 .NET。它已实现双图标/Compact 切换、低电量通知、登录后监听和多手柄选择；Release 提供独立的 `ControllerBatteryNative-...-Setup.exe` 与 ZIP，不会覆盖 C# 稳定版。首次安装后的监听在后台运行，手柄出现才打开托盘；从开始菜单可以打开详情窗口。由于蓝牙、DS4／Edge 和休眠恢复尚未完成实机验收，**暂不能直接取代稳定版**。

当前本机安装/升级/卸载烟测、游戏输入与震动共存测试和短时资源测量见 [原生版验证记录](docs/verification/2026-11-07-rust-native.md)。实测原生安装包约 1.58 MB，托盘 + Watcher 短时工作集约 28 MB；这不是长期性能保证。

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
