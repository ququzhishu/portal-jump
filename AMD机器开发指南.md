# 在 AMD 机器上开发 / 验证 —— 折返跃迁 Portal Jump

这份文档给**在 AMD 显卡机器上继续开发**的人（包括未来的我）。
读完你应该知道：拿哪些文件、怎么编译、怎么验证、以及哪些地方必须改。

---

## ⚠️ 先说验证状态（重要，别被"能跑"骗了）

开发这版时手上只有一台 **NVIDIA RTX 4060 Ti 16GB** 的机器，
**没有任何 AMD 机器可用**。所以下面这些说清楚：

| 项目 | 验证状态 |
|---|---|
| 通用版能启动、托盘常驻 | ✅ 已验证（在 N 卡上跑通用版，不崩） |
| 通用版能切分辨率 | ✅ 已验证 |
| 通用版界面能打开 | ✅ 已验证 |
| 通用版**缩放能在 AMD 上生效** | ❌ **未验证，无任何证据** |
| `dmDisplayFixedOutput` 在 AMD 驱动上的行为 | ❌ **未验证** |

在 N 卡上"强制走通用路径"跑出来的结果**不能**当作 AMD 可用的证据 ——
NVIDIA 驱动本来就不采纳 `dmDisplayFixedOutput`，那条路上注定失败，
这只能证明"N 卡上不行"，不能证明"AMD 上能行"。

**所以拿到 AMD 机器后，第一件事就是跑第 3 节的自检和真实切换测试，
确认缩放真的生效，再把"可用"这个结论说出去。**

---

## 0. 一句话背景

v0.11 起程序拆成**两个版本，共用同一套源码**：

| 版本 | 文件 | 缩放走哪 | 整数缩放 | 自定义分辨率 | 需要 NvAPIWrapper.dll |
|------|------|----------|----------|--------------|----------------------|
| NVIDIA 版 | `PortalJump.exe`（386KB 左右） | NVIDIA NVAPI | ✅ | ✅ | **需要** |
| 通用版 | `PortalJump.exe`（372KB 左右） | Windows 原生 `dmDisplayFixedOutput` | ❌ | ❌ | **不需要** |

两个版本的差别**只有两处**：

1. 编译时是否定义 `WITH_NVAPI` 符号；
2. 是否把 `src\CustomResolutionForm.cs` 编进去（它只用 NVAPI）。

源码层面，所有 NVAPI 调用都被关在 `src\NvapiBridge.cs` 里，用 `#if WITH_NVAPI ... #else ... #endif`
提供了空实现。所以**主代码不需要写任何 `#if`**。

> 为什么 NVIDIA 不能用通用路径：实测 30 次，NVIDIA 驱动把 `dmDisplayFixedOutput` 变成了**只读**——
> `ChangeDisplaySettingsEx` 返回 0、`CDS_TEST` 也返回 0，但字段恒为 0、画面不变。
> 反过来用 NVAPI 设缩放时该字段会跟着同步，说明它只是 NVAPI 的镜像。
> 而 AMD / Intel 上这个字段是正常可写的（这也是为什么它们本来就该走这条路）。

---

## 1. 你需要的文件

把整个 `res-switch` 目录拷过去即可。**关键文件清单**：

### 必须有的源码（通用版就靠这些）

```
src\AppInfo.cs              程序身份（版本号在这里改）
src\AppPaths.cs             路径解析
src\Config.cs               配置读写
src\Preset.cs               预设模型
src\Scaling.cs              ★ 缩放语义层（ScalingMode / ScaleActor / ScalingSpec / ScalingNames）
src\DisplayConfigBackend.cs ★ 跨厂商后端（Win32 DEVMODE + dmDisplayFixedOutput）
src\DisplayInfo.cs          分辨率枚举（对上面的兼容包装）
src\Switcher.cs             切换逻辑、后端选择、自检
src\GpuExtensions.cs        ★ 版本能力开关（WITH_NVAPI 条件编译）
src\NvapiBridge.cs          ★ NVAPI 隔离层（通用版走 #else 空实现）
src\AutoStart.cs            开机自启
src\Program.cs              入口、托盘、进程监控、看门狗
src\UiKit.cs                WinForms 主题（兜底界面用）
src\SettingsForm.cs         设置界面（WinForms 兜底）
src\AboutForm.cs            关于页（WinForms 兜底）
src\ProcessPickerForm.cs    选程序对话框（WinForms 兜底）
src\AppIcon.cs              图标
src\Backdrop.cs             底纹
```

### 只有 NVIDIA 版才用

```
src\CustomResolutionForm.cs   创建自定义分辨率（纯 NVAPI，通用版不编译它）
NvAPIWrapper.dll              NVAPI 封装（通用版不需要）
```

### 界面进程（WPF，两个版本共用）

```
src-ui\*                      设置界面 / 关于页 / 对话框（WPF）
src-ui\PortalJump.UI.csproj   注意：里面已加 <DefineConstants>TRACE;WITH_NVAPI</DefineConstants>
```

### 构建与测试

```
build.ps1                    ★ 构建脚本（一次产出两个版本）
tools\*.cs                   回归测试源码
tools\README.md              测试工具说明
项目交接.md                   开发交接说明（架构、踩过的坑）
```

### 素材（可选，没有也能跑，只是没有背景图）

```
assets\zoe-bg.png
assets\zoe-icon.png
```

---

## 2. 编译

### 前提

- Windows 10 / 11 x64
- .NET Framework 4.8（Win10 1903+ / Win11 已内置）
- **不需要** Visual Studio、不需要 .NET SDK、不需要联网
- 编译器用系统自带的 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`

### 一条命令产出两个版本

```powershell
cd res-switch
.\build.ps1 -Ui -Dist -Zip
```

产出：

```
PortalJump\PortalJump.exe              ← NVIDIA 版（本机运行用）
PortalJump\PortalJump-generic.exe      ← 通用版（本机运行用）
PortalJump\PortalJump.UI.exe           ← 界面进程（两版共用）
dist\PortalJump-0.11-NVIDIA\  + .zip   ← 发给 NVIDIA 用户的包
dist\PortalJump-0.11-Generic\ + .zip   ← 发给 AMD / Intel 用户的包
```

### 只想编通用版（在 AMD 机器上最常用）

```powershell
$root = 'C:\工作区\res-switch'
$csc  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$srcs = Get-ChildItem "$root\src\*.cs" |
        Where-Object { $_.Name -ne 'CustomResolutionForm.cs' } |
        ForEach-Object { $_.FullName }

& $csc /nologo /utf8output /codepage:65001 /target:winexe /optimize+ `
    "/out:$root\PortalJump\PortalJump-generic.exe" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    @srcs
```

注意：**不要**加 `/define:WITH_NVAPI`，**不要**引用 `NvAPIWrapper.dll`，
**不要**把 `CustomResolutionForm.cs` 加进去。

### 界面进程

```powershell
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe' `
  "$root\src-ui\PortalJump.UI.csproj" /p:Configuration=Release /v:m /nologo /clp:NoSummary
```

会有一条 `MSB3644 未找到 .NETFramework v4.8 引用程序集` 的警告 —— **正常**，
它只是说没装 targeting pack，会退回用 GAC 里的程序集，编译结果是对的。

---

## 3. 在 AMD 机器上要验证什么

### 3.1 先跑自检（最重要）

```powershell
.\PortalJump\PortalJump.exe selftest
```

然后看 `portal-jump-selftest.txt`，重点三行：

```
厂商识别：AMD                       ← 应该识别成 AMD
NVAPI：不可用 —— ...                ← AMD 上这是正常的，不是错误
缩放通路：Windows 原生接口（...）    ← 应该走原生
```

以及 `[缩放自检]` 段：

```
用非原生分辨率 2560x1440@... 做校验（只测试，不切画面）：
  全屏铺满（自动选择执行者）：驱动接受
  保持比例（自动选择执行者）：驱动接受
  居中不缩放（自动选择执行者）：驱动接受
```

**如果这三行出现"驱动拒绝"，请把自检报告发给我** —— 说明 AMD 驱动对 `dmDisplayFixedOutput`
的处理和预期不同，需要针对性处理。

### 3.2 真实切换测试

```powershell
.\tools\SwitchTest.exe .\PortalJump\PortalJump-generic.exe
```

这个会**真的改屏幕**（结束会自动还原）。判定标准：15 项全 PASS。

### 3.3 看门狗测试（模拟游戏抢分辨率）

```powershell
.\tools\WatchdogTest.exe .\PortalJump\PortalJump-generic.exe .\tools\rsprobe-game.exe
```

验证"游戏启动后自己又设了一次分辨率，程序能否纠正回来"。

### 3.4 界面测试

```powershell
.\tools\UiProbe.exe    .\PortalJump\PortalJump.UI.exe
.\tools\UltraProbe.exe .\PortalJump\PortalJump.UI.exe
.\tools\TrayProbe.exe  .\PortalJump\PortalJump.exe .\tools\rsprobe-game.exe
```

---

## 4. AMD 上可能需要改的地方（已知风险点）

这几处是按 NVIDIA 的经验写的，AMD 驱动行为不同，**大概率需要调整**：

### 4.1 `DisplayConfigBackend.CcdScalingAllowsFixedOutput`

```csharp
public static bool CcdScalingAllowsFixedOutput(int ccd)
{
    return ccd < 0 || ccd == 1 || ccd == 128;
}
```

判定"CCD 侧缩放值会不会压制 dmDisplayFixedOutput"。
实测 NVIDIA 上 CCD=2/3/4/5 时会压制。**AMD 是否同样如此，需要在 AMD 机器上确认。**

如果 AMD 上发现即使 CCD 是某个值也能正常设缩放，可以放宽这个判断。

### 4.2 `DisplayConfigBackend.TrySetCcdScaling`

用 Windows CCD API 把缩放设成指定值。NVIDIA 驱动会拦截（返回 0 但值不变），
**AMD 驱动可能真的能设** —— 如果 AMD 上能用，这会是比 `dmDisplayFixedOutput` 更强的方案，
值得优先尝试。

### 4.3 `Switcher.ProbeNativeScalingWritable`

```csharp
if (_vendor != GpuVendor.Nvidia) return;   // 非 NVIDIA 直接用原生
```

这里对非 NVIDIA 直接乐观处理。如果 AMD 上实际写不进去，需要加一条探测。

### 4.4 `Switcher.ExpectedFixedOutput`

```csharp
case ScalingMode.Aspect:
case ScalingMode.Centered:
    return DisplayConfigBackend.DMDFO_CENTER;
```

`dmDisplayFixedOutput` 只有两档（STRETCH / CENTER），所以"保持比例"和"居中"在通用版里
都映射到 CENTER —— 实际是"留黑边"，但具体是居中还是保持比例由驱动/显示器决定。
**AMD 上请确认这两种的实际画面表现**，必要时在文档里说明差异。

### 4.5 驱动厂商识别

`DisplayConfigBackend.DetectVendor()` 靠适配器名字里有没有 "amd"/"radeon"/"ati "。
AMD 驱动的设备名通常是 `AMD Radeon RX xxxx`，应该能认出来。
**如果自检里"厂商识别"不是 AMD，请把这行发给我。**

---

## 5. 关键设计约束（改代码前必读）

1. **C# 5 语法**：`csc.exe` 4.0 编译器。**不能用**字符串内插 `$"..."`、`nameof`、`?.`、表达式体成员 `=>`。
   lambda 请用 `delegate(...) { }`。
2. **源码 UTF-8**；`build.ps1` 和 `assets\gen\*.ps1` 必须 **UTF-8 带 BOM**（PowerShell 5.1 按 ANSI 读会坏中文）。
3. **不要**在主代码里 `using NvAPIWrapper;` —— 那会让通用版编译失败，也会让通用版运行时崩溃。
   要加 NVAPI 功能，一律走 `NvapiBridge`。
4. **`DISPLAYCONFIG_PATH_INFO` 末尾必须有 `UINT32 flags`**（结构 72 字节）。
   少了它 = 68 字节，系统按 72 写，会 `STATUS_HEAP_CORRUPTION` 直接崩进程。**踩过。**
5. **`DEVMODEA` 是 156 字节**（`dmReserved1`/`dmReserved2` 两个，没有 `dmReserved3`）。
   旧版多写一个撑到 160，是越界布局，已修正。
6. **缩放必须并进"切到非原生分辨率"的那一次调用**：Windows 只在源尺寸 ≠ 目标尺寸时才有缩放概念。
   先切分辨率再单独设缩放，驱动会拒绝。
7. **切换成败以回读为准**：`ChangeDisplaySettingsEx` 返回 0 不代表模式真的变了（驱动忙时会假成功）。
8. **分辨率成功即算整体成功**：缩放失败不要让 `Apply` 返回 false，否则托盘会反复重试、越试越糟。

---

## 6. 回传给我什么

在 AMD 机器上跑完，请把这几样发回来：

1. `portal-jump-selftest.txt` —— 环境自检全文
2. `portal-jump.log` —— 切换日志（跑几次切换后）
3. `SwitchTest.exe` 的完整输出（15 项 PASS/FAIL 明细）
4. 如果某项失败，把对应日志行贴出来

有了这些我就能判断：AMD 上是"直接可用"，还是需要改 `4.x` 里那几处。

---

## 7. 两个版本的差异一览（便于确认没编错）

| 检查点 | NVIDIA 版 | 通用版 |
|--------|-----------|--------|
| 编译符号 | `WITH_NVAPI` | 无 |
| 引用 NvAPIWrapper.dll | 是 | 否 |
| 编译 CustomResolutionForm.cs | 是 | 否 |
| exe 大小 | ~386 KB | ~372 KB |
| 自检「版本」行 | `0.11（NVIDIA 版）` | `0.11（通用版）` |
| 自检「NVAPI」行 | 可用（显卡名） | 不可用 —— 当前为通用版… |
| 设置界面「创建自定义分辨率」 | 能打开 | 弹窗说明"需要 NVIDIA 版" |
| 缩放方式下拉里的「整数缩放」 | 能选且有效 | 能选，但会提示需要 NVIDIA |
