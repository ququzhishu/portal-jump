# tools —— 回归测试、诊断与截图工具

这些工具平时用不到，**改完代码跑一遍确认没坏**。全部用 Windows 自带的 `csc.exe` 编译，没有额外依赖。

| 文件 | 作用 |
|------|------|
| `ConfigTest.cs` | 配置层回归：解析、保存往返、GBK/UTF-8 兼容、坏数据容错、分辨率枚举、路径解析 |
| `ScalingTest.cs` | **缩放语义层**：新写法解析、旧数字（0/1/2/3/5/6/7/8）迁移、ini 往返、下拉选项、DEVMODE 布局自检。纯逻辑，不动屏幕 |
| `SwitchTest.cs` | **真实切换**：切到非原生分辨率并核对分辨率 + 缩放是否都到位（STRETCH/CENTER 回读）、幂等性、切回。**会真的改屏幕**，结束自动还原 |
| `WatchdogTest.cs` | **看门狗**：模拟"游戏启动后把分辨率改回原生"，验证托盘能否自动纠正（对应三角洲"有时候切不过去"）。**会真的改屏幕** |
| `UiProbe.cs` | 界面自动化：驱动 **WPF 设置界面**（控件是否齐全、真实鼠标点击切预设、点新增/保存、校验写出的 ini、关闭确认框）。沙盒运行，不动真实配置 |
| `UltraProbe.cs` | 宽视距换算验证：打开「创建自定义分辨率」→ 点 21:9 / 32:9 → 读回宽高是否为 3840×1646、3840×1080 |
| `TrayProbe.cs` | 托盘端到端：启动假游戏 → 自动切换 → 关闭 → 自动恢复 → 配置热加载 |
| `Shot.cs` | 截图工具：在临时沙盒里跑程序，抓窗口（含 `PrintWindow` 版本），可指定要先点哪个按钮 |
| `MenuShot.cs` | 把右键菜单在 `ShowImageMargin` 开/关下各渲染一次拼成对比图（验证勾选标志是否可见） |
| `IconTool.cs` | 素材后处理：`pixelize` 像素化（降采样→限量调色板→最近邻放大）、`lineart` 线稿化（平涂 + Sobel 描边） |
| `FakeGame.cs` | 假游戏进程，给 `TrayProbe` / `WatchdogTest` 当被检测目标 |

### 验证跨厂商缩放（重要）

本机若是 NVIDIA，默认走的是 NVAPI；但 AMD / Intel 用户走的是 Windows 原生接口。
想在这台机器上验证"AMD 用户那条路"，用环境变量强制切换后端：

```powershell
$env:PORTALJUMP_SCALING_BACKEND = 'win32'   # 强制走 Windows 原生 dmDisplayFixedOutput
.\SwitchTest.exe ..\PortalJump\PortalJump.exe
$env:PORTALJUMP_SCALING_BACKEND = $null
```

`win32` 那条路径实测 14/14 全绿（含分辨率与缩放一并生效）。
另有 `nvapi` 可强制走 NVAPI。

---

## 编译

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpf = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF"

# 1) 配置层测试（依赖 AppInfo / AppPaths，要一起编进去）
& $csc /nologo /target:exe /out:ConfigTest.exe ..\src\AppInfo.cs ..\src\AppPaths.cs ..\src\Config.cs ..\src\Preset.cs ..\src\DisplayInfo.cs ..\src\Scaling.cs ..\src\DisplayConfigBackend.cs ConfigTest.cs

# 1b) 缩放语义测试
& $csc /nologo /target:exe /out:ScalingTest.exe /r:..\NvAPIWrapper.dll ..\src\AppInfo.cs ..\src\AppPaths.cs ..\src\Config.cs ..\src\Preset.cs ..\src\DisplayInfo.cs ..\src\Scaling.cs ..\src\DisplayConfigBackend.cs ..\src\Switcher.cs ScalingTest.cs

# 1c) 真实切换 / 看门狗（只依赖 Win32，不需要链接 src）
& $csc /nologo /target:exe /out:SwitchTest.exe SwitchTest.cs
& $csc /nologo /target:exe /out:WatchdogTest.exe WatchdogTest.cs

# 2) 界面类测试（需要能弹窗的桌面会话；UiProbe 会真的移动鼠标）
& $csc /nologo /target:exe /out:UiProbe.exe UiProbe.cs /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:"$wpf\UIAutomationClient.dll" /r:"$wpf\UIAutomationTypes.dll" /r:"$wpf\WindowsBase.dll"

# 3) 截图工具
& $csc /nologo /target:exe /out:Shot.exe Shot.cs /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:"$wpf\UIAutomationClient.dll" /r:"$wpf\UIAutomationTypes.dll" /r:"$wpf\WindowsBase.dll"

# 4) 假游戏
& $csc /nologo /target:winexe /out:rsprobe-game.exe FakeGame.cs /r:System.dll /r:System.Windows.Forms.dll
```

---

## 运行

```powershell
.\ConfigTest.exe ..\PortalJump\presets.ini
.\ScalingTest.exe
.\SwitchTest.exe ..\PortalJump\PortalJump.exe
.\WatchdogTest.exe ..\PortalJump\PortalJump.exe .\rsprobe-game.exe
.\UiProbe.exe    ..\PortalJump\PortalJump.UI.exe
.\UltraProbe.exe ..\PortalJump\PortalJump.UI.exe
.\TrayProbe.exe  ..\PortalJump\PortalJump.exe .\rsprobe-game.exe
.\Shot.exe       ..\PortalJump\PortalJump.UI.exe ..\tools\shot-wpf-settings.png settings
```

---

## 安全性说明

- `ConfigTest` / `ScalingTest` / `UiProbe` / `UltraProbe` / `Shot` 都在**临时沙盒**里造一份配置和 exe 副本来跑，**不会改你正在用的 `presets.ini`**。
- `SwitchTest` / `WatchdogTest` / `TrayProbe` 会**临时结束正在运行的托盘**并在结束后重新拉起，同时会真的切换显示模式（测试完会尽量还原）。
- 所有工具都不写注册表；开机自启相关的改动由程序本体负责，工具不碰。

---

## 踩过的坑（写在这里免得下次又查）

1. **WPF 的 owned 对话框在 UIA 树里挂在属主窗口下面**，不是桌面根节点的直接子节点。
   只枚举根节点会误判成"对话框没打开"，要用 `属主窗口.FindFirst(TreeScope.Descendants, ControlType.Window ...)`。
2. **UIA `InvokePattern.Invoke()` 对会打开模态对话框的按钮是阻塞的**（要等对话框关闭才返回）。
   探针里"点击后还要继续操作"的场景，用真实鼠标点击（`SetCursorPos` + `mouse_event`），并先 `SetForegroundWindow`，
   否则点击会落到盖在上面的终端窗口上。
3. 界面进程的 exe 正在运行时，MSBuild 复制输出会报"文件正由另一进程使用"；先结束 `PortalJump` / `PortalJump.UI` 再编译。
4. 探针里的固定 `Thread.Sleep` 在高负载下会假失败（状态还没更新完），改成**轮询等待**更稳。
5. `assets\gen\*.ps1` 与 `build.ps1` 必须存成 **UTF-8 带 BOM**，否则 PowerShell 5.1 按 ANSI 读会破坏中文并报解析错误。
6. **`DISPLAYCONFIG_PATH_INFO` 末尾必须保留 `UINT32 flags`**。少了它结构是 68 字节，系统按 72 字节写，
   调用 `QueryDisplayConfig` 会直接 `STATUS_HEAP_CORRUPTION` 崩进程（实测踩过）。正确大小 72。
7. **`DEVMODEA` 是 156 字节**（`dmReserved1`/`dmReserved2` 两个，没有 `dmReserved3`）。
   旧版多写一个 `dmReserved3` 撑到 160，是越界布局，已修正。
8. **Windows 只在"源尺寸 ≠ 目标尺寸"时才有缩放概念**。当前处于原生分辨率时，
   给 `dmDisplayFixedOutput` 做 `CDS_TEST` 会返回 -2（"不支持"）—— 这是**正常现象**，不代表切到非原生后也设不了。
   所以缩放必须**并进"切到非原生分辨率"的那一次 `ChangeDisplaySettingsEx`**，不能先切分辨率再单独设缩放。
9. **NVIDIA 机器上 Windows CCD 的 `SetDisplayConfig` 改不了缩放**（返回 0 但恒为 IDENTITY），
   NVIDIA 把这件事接管到了自己的 NVAPI 通道。跨厂商缩放只能走 `DEVMODE.dmDisplayFixedOutput`。
10. **NVAPI 的 `SetDisplayConfig` 回读有滞后**：连续切几个缩放值时，回读常拿到"上一次"的值
    （实测：请求 2 读到 3、请求 3 读到 2）。别拿它当"没生效"的证据。
