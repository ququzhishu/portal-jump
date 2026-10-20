# tools —— 回归测试

改完代码跑一遍确认没坏。全部用 Windows 自带的 `csc.exe` 编译，没有额外依赖。

| 文件 | 作用 |
|------|------|
| `ConfigTest.cs` | 配置层回归：解析、保存往返、GBK/UTF-8 兼容、坏数据容错、分辨率枚举、路径解析 |
| `SwitchTest.cs` | **真实切换**：切到非原生分辨率并核对分辨率 + 缩放是否都到位、幂等性、切回。**会真的改屏幕**，结束自动还原 |
| `WatchdogTest.cs` | **看门狗**：模拟"游戏启动后把分辨率改回原生"，验证托盘能否自动纠正。**会真的改屏幕** |
| `adlx-diag.cs` | **AMD 只读诊断**：用官方 ADLX 接口枚举显示器、报告 GPU 缩放/缩放模式/整数缩放是否可用。**纯只读、不改画面**，可安全反复运行。也是 `src/AdlxBridge.cs` 的 vtable 偏移说明书 |
| `cr-product-test.cs` | **AMD 自定义分辨率产品代码回归**：链接 `src/AdlxBridge.cs`，走产品代码真实创建 `1280x960@144`、回读列表、再删除还原。会短暂写入驱动（不切当前画面），结束自动删干净 |

---

## 编译

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

# ConfigTest 需要链接部分核心源码
& $csc /nologo /define:WITH_NVAPI /target:exe /out:ConfigTest.exe /r:..\NvAPIWrapper.dll `
    ..\src\AppInfo.cs ..\src\AppPaths.cs ..\src\Config.cs ..\src\Preset.cs `
    ..\src\DisplayInfo.cs ..\src\Scaling.cs ..\src\DisplayConfigBackend.cs ..\src\Switcher.cs `
    ..\src\NvapiBridge.cs ..\src\GpuExtensions.cs ConfigTest.cs

# 另外两个只依赖 Win32，不需要链接 src
& $csc /nologo /target:exe /out:SwitchTest.exe  SwitchTest.cs
& $csc /nologo /target:exe /out:WatchdogTest.exe WatchdogTest.cs
```

## 运行

```powershell
.\ConfigTest.exe  ..\PortalJump\presets.ini
.\SwitchTest.exe  ..\PortalJump\PortalJump.exe
.\WatchdogTest.exe ..\PortalJump\PortalJump.exe .\rsprobe-game.exe
```

`WatchdogTest` 需要一个"假游戏"进程作为被检测目标。用 `FakeGame.cs` 编译一个即可：

```powershell
& $csc /nologo /target:winexe /out:rsprobe-game.exe FakeGame.cs /r:System.dll /r:System.Windows.Forms.dll
```

---

## AMD ADLX 诊断（`adlx-diag`）

AMD 机器上排查缩放问题时，先跑这个确认官方 ADLX 接口能不能枚举到显示器：

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /unsafe /target:exe /out:adlx-diag.exe adlx-diag.cs
.\adlx-diag.exe        # 只读；写缩放才需要管理员，诊断普通权限即可
```

正常应看到：`显示器数量 = 1`、`显示器[0]：<型号>`、`GPU 缩放开关 IsSupported=True`。
关键点（本机 RX 9070 XT / ADLX 1.4 实测）：

- 初始化必须用导出函数 `ADLXInitializeWithIncompatibleDriver`，标准 `ADLXInitialize` 在本机枚举显示器时会内部空引用崩溃；前者是官方的完整接口，并非降级。
- `IADLXSystem` 是单例、vtable 无 Acquire/Release 前缀；`GetDisplaysServices` 在 vtable[3]。
- 显示器列表取元素要用类型安全的 `At_DisplayList`（vtable[11]），泛型 `At` 会返回 `UNKNOWN_INTERFACE`。
- 整数缩放（IntegerScaling）在 GPU scaling 关闭时恒报 `IsSupported=False`，必须先开 GPU scaling 再查。

### 自定义分辨率回归（`cr-product-test`）

验证 `AdlxBridge` 的自定义分辨率创建/枚举/删除（走产品代码，不是底层探针）。需要管理员：

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /utf8output /unsafe /target:exe /out:cr-product-test.exe cr-product-test.cs `
    ..\src\AdlxBridge.cs ..\src\Scaling.cs
.\cr-product-test.exe   # 右键管理员运行；结果写到同目录 cr-prod-out.txt
```

正常应看到：`创建前 数量=0` → `Create=True hz=144` → `创建后 数量=1 1280x960@144` → `Delete=True` → `删除后 数量=0`。

- 关键接口：`IADLXDisplayServices[18]GetCustomResolution`，自定义分辨率接口 `[3]IsSupported [4]GetResolutionList [5]GetCurrentAppliedResolution [6]CreateNewResolution [7]DeleteResolution`，分辨率对象 `[3]GetValue [4]SetValue`。
- 结构体 `ADLX_CustomResolution` 共 68 字节（`adlx_long` 在 Windows 是 4 字节），`SetValue` 按值传 68 字节在 x64 下走隐藏指针，C# 用 `ref` 即正确。
- 创建时序严格按官方示例：取当前应用分辨率 → 只改 `resWidth/resHeight` 与 `detailedTiming.hDisplay/vDisplay` → `SetValue` → `CreateNewResolution`；刷新率与时序沿用当前。

---

## 安全性说明

- `ConfigTest` 在临时沙盒里造配置来跑，**不会改你正在用的 `presets.ini`**。
- `SwitchTest` / `WatchdogTest` 会**临时结束正在运行的托盘**并在结束后重新拉起，同时会真的切换显示模式（测试完会尽量还原）。
- 所有工具都不写注册表。
