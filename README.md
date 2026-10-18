# 折返跃迁 Portal Jump

> 按程序自动切换显示分辨率与缩放模式的 Windows 小工具。
> 打开游戏自动切到游戏分辨率，退出后自动切回桌面。

<p align="center">
  <img src="docs/screenshot-settings.jpg" alt="设置界面" width="720">
</p>

---

## 这是什么

一个常驻托盘的 Windows 工具。你事先把「某个程序」和「一套显示配置」绑定起来，
之后打开那个程序，它自动把显示器切到对应分辨率和缩放方式；程序退出后自动切回去。

典型用法：

- 玩《三角洲行动》想用 **2560×1440 + 居中不缩放**，平时办公用 **3840×2160 + 全屏铺满**
- 玩《英雄联盟》想用 **3840×1080 带鱼屏视野**，退出后回到 4K
- 笔记本接显示器，不同场景用不同分辨率

手动在系统设置里来回切分辨率又慢又麻烦，这个工具让它变成"打开程序就自动完成"。

---

## 特性

| 特性 | 说明 |
|---|---|
| **按程序自动切换** | 轮询检测关联进程，命中即切换，全部退出后自动恢复 |
| **分辨率 + 缩放一起切** | 不只是分辨率，缩放方式（全屏铺满 / 保持比例 / 居中不缩放 / 整数缩放）也一起切 |
| **切换后看门狗** | 游戏启动后常会自己再改一次分辨率，工具会在 12 秒内发现并纠正 |
| **回读校验** | 每次切换后回读确认，不轻信 API 返回值（驱动忙时会"假成功"） |
| **绿色免安装** | 解压即用，配置在同目录，整个文件夹拷走就能带走配置 |
| **托盘常驻，占用小** | 无主窗口，约 32 MB 内存 |
| **配置热加载** | 改完 `presets.ini` 保存，托盘自动生效，不用重启 |

---

## 运行要求

- **NVIDIA 显卡** + 官方驱动（缩放走 NVIDIA NVAPI 接口）
- Windows 10 / 11（64 位）
- .NET Framework 4.8（Win10 1903+ 与 Win11 已内置）

> 本版本仅支持 NVIDIA 显卡。AMD / Intel 用户暂不适用。

---

## 下载

到 [Releases](../../releases) 页面下载 `折返跃迁vX.Y-NVIDIA显卡版.zip`，解压后双击 `PortalJump.exe` 即可，无需安装。

包里包含：

```
PortalJump.exe        托盘主程序
PortalJump.UI.exe     设置界面
NvAPIWrapper.dll      NVAPI 封装（必须有，请不要删）
先看这里.txt / 使用说明.md / 部署说明.md / 环境自检.cmd
presets.example.ini + 图标素材
```

---

## 快速上手

1. 解压，双击 `PortalJump.exe` —— 右下角托盘出现图标（没有主窗口，这是正常的）
2. 右键托盘图标 →「打开设置界面」
3. 「新增」一个预设：填名称、选分辨率与缩放方式 →「关联程序」里勾上你的游戏 →「保存并应用」
4. 之后打开那个游戏，就会自动切换了

**建议先做这一步**：NVIDIA 控制面板 →「显示」→「调整桌面尺寸和位置」，
取消勾选「覆盖由游戏和程序设置的缩放模式」，点应用。
不做这步可能分辨率能切但缩放不变——这是 NVIDIA 上最常见的坑。

---

## 配置

配置文件是 `presets.ini`，可以直接用记事本改，也可以用界面改。

```ini
[general]
autoswitch=1
fixresidual=1
restore=office

[office]
name=办公
width=3840
height=2160
scaling=fullscreen-gpu

[game]
name=三角洲行动
width=2560
height=1440
scaling=centered-gpu
process=DeltaForceClient-Win64-Shipping
```

`scaling` 可选值：

| 值 | 含义 |
|---|---|
| `unchanged` | 不改动，保持当前设置 |
| `fullscreen-gpu` | 全屏铺满，GPU 缩放 |
| `fullscreen-display` | 全屏铺满，显示器缩放 |
| `aspect-gpu` | 保持比例，GPU 缩放 |
| `aspect-display` | 保持比例，显示器缩放 |
| `centered-gpu` | 居中不缩放，GPU 缩放 |
| `centered-display` | 居中不缩放，显示器缩放 |
| `integer-gpu` | 整数缩放 |

> 旧版配置里的数字（`0`/`1`/`2`/`3`/`5`/`6`/`7`/`8`）会被自动识别并迁移，不用手改。

`process` 填进程名（不含 `.exe`，多个用分号隔开）。不知道进程名？
用设置界面里的「选择运行中的程序…」最省事。

---

## 从源码构建

需要系统自带的 .NET Framework 4.8 工具链（`csc.exe` / `MSBuild.exe`），
**不需要 Visual Studio、不需要 .NET SDK、不需要联网**。

```powershell
cd res-switch
.\build.ps1 -Ui -Dist -Zip
```

产出：

```
PortalJump\PortalJump.exe            托盘主程序
PortalJump\nvidia\PortalJump.UI.exe  设置界面
dist\                                分发包
```

也可以双击 `build.cmd` 一键构建。

### 代码结构

```
src\                     托盘进程 + 界面共用的核心
  Scaling.cs             缩放语义层
  DisplayConfigBackend.cs 显示配置后端（Win32 DEVMODE + CCD）
  NvapiBridge.cs         NVAPI 隔离层
  Switcher.cs            切换逻辑、回读校验
  Program.cs             入口、托盘、进程监控、看门狗
src-ui\                  界面进程（WPF）
tools\                   回归测试与诊断工具
```

关键设计约束写在 [`项目交接.md`](项目交接.md) 里，改代码前建议先读。

---

## 测试

```powershell
.\tools\ConfigTest.exe  .\PortalJump\presets.ini   # 配置层回归（17 项）
.\tools\ScalingTest.exe                            # 缩放语义 / 旧配置迁移（39 项）
.\tools\SwitchTest.exe  .\PortalJump\PortalJump.exe # 真实切换（15 项，会改屏幕，自动还原）
.\tools\WatchdogTest.exe .\PortalJump\PortalJump.exe .\tools\rsprobe-game.exe  # 看门狗
.\tools\UiProbe.exe     .\PortalJump\PortalJump.UI.exe  # 界面自动化
.\tools\TrayProbe.exe   .\PortalJump\PortalJump.exe .\tools\rsprobe-game.exe   # 托盘端到端
```

---

## 已知限制

- 仅支持 NVIDIA 显卡（缩放依赖 NVAPI）。
- 进程检测是轮询（约 2 秒一次），不是事件驱动；极短命的进程可能漏掉。
- 切换后有 12 秒看门狗（最多纠正 3 次），超过就不再纠缠。
- 没有安装包，升级就是覆盖文件夹；配置在程序目录或 `%APPDATA%\PortalJump\`，不会被覆盖。
- 自定义分辨率的保护机制是「15 秒内不确认就还原」，但如果驱动在试验阶段直接卡死，仍需手动重启。

---

## 免责声明

本工具会**直接通过显示驱动修改分辨率、刷新率与缩放模式**，并可创建自定义分辨率，
可能出现黑屏、花屏、显示异常等情况。

使用前请自行确认显示器与驱动支持你要设置的模式；因使用本工具造成的任何直接或间接损失
（含数据丢失、工作中断、硬件损坏），作者不承担任何责任。

本工具为个人免费作品，按「现状」提供，不提供任何担保，与 NVIDIA 公司无关联。
继续使用即表示接受以上内容，不同意请不要运行。

---

## 作者

**区区之数** · B 站主页：https://space.bilibili.com/21100323

如果这个工具帮到了你，欢迎点个 Star ⭐
