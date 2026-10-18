# tools —— 回归测试

改完代码跑一遍确认没坏。全部用 Windows 自带的 `csc.exe` 编译，没有额外依赖。

| 文件 | 作用 |
|------|------|
| `ConfigTest.cs` | 配置层回归：解析、保存往返、GBK/UTF-8 兼容、坏数据容错、分辨率枚举、路径解析 |
| `SwitchTest.cs` | **真实切换**：切到非原生分辨率并核对分辨率 + 缩放是否都到位、幂等性、切回。**会真的改屏幕**，结束自动还原 |
| `WatchdogTest.cs` | **看门狗**：模拟"游戏启动后把分辨率改回原生"，验证托盘能否自动纠正。**会真的改屏幕** |

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

## 安全性说明

- `ConfigTest` 在临时沙盒里造配置来跑，**不会改你正在用的 `presets.ini`**。
- `SwitchTest` / `WatchdogTest` 会**临时结束正在运行的托盘**并在结束后重新拉起，同时会真的切换显示模式（测试完会尽量还原）。
- 所有工具都不写注册表。
