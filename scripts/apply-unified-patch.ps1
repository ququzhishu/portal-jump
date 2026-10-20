# =====================================================================
#  给「折返跃迁」源码打上 UNIFIED（合并版）补丁
#
#  用法：powershell -File apply-unified-patch.ps1 -Root <源码根目录>
#
#  背景：合并版 = 一个 exe 同时编入 NVAPI 与 ADLX 两套桥接，运行时按厂商分流。
#        补丁共 5 处，全部幂等（重复执行不会重复插入）。
#
#  必须在带 BOM 的 UTF-8 下读写本文件，否则中文乱码。
# =====================================================================
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Root
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Root)) { throw "源码根目录不存在: $Root" }

function Read-Utf8($p) { [IO.File]::ReadAllText($p, [Text.Encoding]::UTF8) }
function Read-Utf8NoBom($p) { [IO.File]::ReadAllText($p, [Text.Encoding]::UTF8) }
function Write-Utf8Bom($p, $t) { [IO.File]::WriteAllText($p, $t, (New-Object Text.UTF8Encoding($true))) }
function Write-Utf8NoBom($p, $t) { [IO.File]::WriteAllText($p, $t, (New-Object Text.UTF8Encoding($false))) }

$report = @()

# ---------------------------------------------------------------
# 1) GpuExtensions.cs：新增 UNIFIED 分支
# ---------------------------------------------------------------
$p = Join-Path $Root 'src\GpuExtensions.cs'
$t = Read-Utf8 $p
if ($t -notmatch '#elif UNIFIED') {
    $anchor = "#elif WITH_NVAPI"
    $insert = @'
#elif UNIFIED
        /// <summary>本版本是否编入了 NVAPI 支持（双桥接：N 卡走 NVAPI，A 卡走 ADLX）</summary>
        public const bool Compiled = true;

        /// <summary>本版本是否只做分辨率、不碰缩放</summary>
        public const bool ResolutionOnly = false;

        /// <summary>本版本名称（界面/自检显示）</summary>
        public const string EditionName = "合并版";

        /// <summary>
        /// 本版本支持的能力说明。
        /// 【合并版】同时编入 NVAPI 与 ADLX 两套桥接，**具体走哪条路由运行时厂商识别决定**，
        /// 所以这里只描述"可能的能力"，实际可用性见 Switcher 的运行时判断。
        /// </summary>
        public const string EditionFeatures =
            "自动识别显卡：NVIDIA 走 NVAPI，AMD 走官方 ADLX；两家的整数缩放与创建自定义分辨率均支持。"
            + "识别不出专有接口时回退 Windows 原生接口做基础缩放。";
#elif WITH_NVAPI
'@
    $t = $t.Replace($anchor, $insert)
    Write-Utf8NoBom $p $t
    $report += "GpuExtensions.cs: 已加 UNIFIED 分支"
} else { $report += "GpuExtensions.cs: 已有 UNIFIED 分支（跳过）" }

# ---------------------------------------------------------------
# 2) NvapiBridge.cs：让 NVAPI 实现在 UNIFIED 下也编译
# ---------------------------------------------------------------
$p = Join-Path $Root 'src\NvapiBridge.cs'
$t = Read-Utf8NoBom $p
if ($t -match '(?m)^#if WITH_NVAPI\s*$') {
    $t = $t -replace '(?m)^#if WITH_NVAPI\s*$', '#if WITH_NVAPI || UNIFIED'
    Write-Utf8NoBom $p $t
    $report += "NvapiBridge.cs: 已放行 UNIFIED"
} else { $report += "NvapiBridge.cs: 已处理（跳过）" }

# ---------------------------------------------------------------
# 3) Switcher.cs：AMD 机器直接跳过 NVAPI（与已验证的 AMD 版行为一致）
# ---------------------------------------------------------------
$p = Join-Path $Root 'src\Switcher.cs'
$t = Read-Utf8NoBom $p
if ($t -notmatch 'skipNvapi') {
    $old = @'
            // 2) 尝试 NVAPI。全部经由 NvapiBridge，主代码不碰任何 NVAPI 类型，
            //    所以 NvAPIWrapper.dll 缺失时只是降级，不会崩。
            try
            {
                _nvapiReady = NvapiBridge.TryInit();
                _nvapiError = NvapiBridge.Error;
                _gpuInfo = NvapiBridge.GpuInfo;
                _driverInfo = NvapiBridge.DriverInfo;

                if (_nvapiReady)
                {
                    Log("NVAPI 初始化成功：" + (_gpuInfo.Length > 0 ? _gpuInfo : "(未识别)"));
                }
                else if (_vendor == GpuVendor.Nvidia)
                {
                    Log("NVAPI 初始化失败（将改用 Windows 原生缩放接口）: " + _nvapiError);
                }
                else
                {
                    Log("非 NVIDIA 显卡，跳过 NVAPI，使用 Windows 原生缩放接口");
                }
            }
            catch (Exception e)
            {
                _nvapiReady = false;
                _nvapiError = e.Message;
                Log("NVAPI 初始化异常（已降级）：" + e.Message);
            }
'@
    $new = @'
            // 2) 尝试 NVAPI。全部经由 NvapiBridge，主代码不碰任何 NVAPI 类型，
            //    所以 NvAPIWrapper.dll 缺失时只是降级，不会崩。
            //
            //    【合并版 UNIFIED】已确认是 AMD 的机器**直接跳过 NVAPI**，不加载
            //    NvAPIWrapper.dll、不调用 NVIDIA.Initialize() —— 这样合并版在 AMD 上的
            //    行为与已验证的 AMD 版完全一致（后者走 #else 空实现，同样一次都不碰 NVAPI）。
            //    未识别厂商时仍会尝试，避免把"N 卡但识别失败"误判成不支持。
            //
            //    仅合并版做这个短路；NVIDIA 版 / 通用版保持原有代码路径不变，
            //    以免动到已经验证过的行为。
#if UNIFIED
            bool skipNvapi = (_vendor == GpuVendor.Amd);
#else
            bool skipNvapi = false;
#endif
            if (skipNvapi)
            {
                _nvapiReady = false;
                _nvapiError = "本机是 AMD 显卡，不使用 NVAPI；缩放走 AMD 官方 ADLX。";
                _gpuInfo = "";
                _driverInfo = "";
                Log("显卡厂商为 AMD，跳过 NVAPI（改用 ADLX / Windows 原生接口）");
            }
            else
            {
                try
                {
                    _nvapiReady = NvapiBridge.TryInit();
                    _nvapiError = NvapiBridge.Error;
                    _gpuInfo = NvapiBridge.GpuInfo;
                    _driverInfo = NvapiBridge.DriverInfo;

                    if (_nvapiReady)
                    {
                        Log("NVAPI 初始化成功：" + (_gpuInfo.Length > 0 ? _gpuInfo : "(未识别)"));
                    }
                    else if (_vendor == GpuVendor.Nvidia)
                    {
                        Log("NVAPI 初始化失败（改用 Windows 原生缩放接口）: " + _nvapiError);
                    }
                    else
                    {
                        Log("非 NVIDIA 显卡，跳过 NVAPI，使用 Windows 原生缩放接口");
                    }
                }
                catch (Exception e)
                {
                    _nvapiReady = false;
                    _nvapiError = e.Message;
                    Log("NVAPI 初始化异常（已降级）：" + e.Message);
                }
            }
'@
    if ($t.Contains($old)) {
        $t = $t.Replace($old, $new)
        Write-Utf8NoBom $p $t
        $report += "Switcher.cs: 已加 AMD 短路"
    } else {
        $report += "Switcher.cs: !! 未匹配到锚点，需手工处理"
    }
} else { $report += "Switcher.cs: 已有 AMD 短路（跳过）" }

# ---------------------------------------------------------------
# 4) csproj：支持 /p:Unified=true 与 unified 输出目录
# ---------------------------------------------------------------
$p = Join-Path $Root 'src-ui\PortalJump.UI.csproj'
$t = Read-Utf8NoBom $p
if ($t -notmatch 'IsUnified') {
    $t = $t.Replace(
        "    <HasNvapi Condition=`"!Exists('..\NvAPIWrapper.dll')`">false</HasNvapi>",
        "    <HasNvapi Condition=`"!Exists('..\NvAPIWrapper.dll')`">false</HasNvapi>`r`n    <IsUnified Condition=`" '`$(IsUnified)' == '' `">false</IsUnified>`r`n    <IsUnified Condition=`" '`$(Unified)' == 'true' AND '`$(HasNvapi)' == 'true' `">true</IsUnified>")
    $t = $t.Replace(
        "    <OutputPath Condition=`" '`$(HasNvapi)' == 'true' `">..\PortalJump\nvidia\</OutputPath>",
        "    <OutputPath Condition=`" '`$(IsUnified)' == 'true' `">..\PortalJump\unified\</OutputPath>`r`n    <OutputPath Condition=`" '`$(IsUnified)' != 'true' AND '`$(HasNvapi)' == 'true' `">..\PortalJump\nvidia\</OutputPath>")
    $t = $t.Replace(
        "    <OutputPath Condition=`" '`$(HasNvapi)' != 'true' `">..\PortalJump\generic\</OutputPath>",
        "    <OutputPath Condition=`" '`$(IsUnified)' != 'true' AND '`$(HasNvapi)' != 'true' `">..\PortalJump\generic\</OutputPath>")
    $t = $t.Replace(
        "    <DefineConstants Condition=`" '`$(HasNvapi)' == 'true' `">TRACE;WITH_NVAPI</DefineConstants>",
        "    <DefineConstants Condition=`" '`$(IsUnified)' == 'true' `">TRACE;UNIFIED;WITH_NVAPI</DefineConstants>`r`n    <DefineConstants Condition=`" '`$(IsUnified)' != 'true' AND '`$(HasNvapi)' == 'true' `">TRACE;WITH_NVAPI</DefineConstants>")
    $t = $t.Replace(
        "    <DefineConstants Condition=`" '`$(HasNvapi)' != 'true' `">TRACE</DefineConstants>",
        "    <DefineConstants Condition=`" '`$(IsUnified)' != 'true' AND '`$(HasNvapi)' != 'true' `">TRACE</DefineConstants>")
    $t = $t.Replace(
        "Condition=`" '`$(HasNvapi)' == 'true' `">`r`n      <Generator>MSBuild:Compile</Generator>`r`n      <SubType>Designer</SubType>`r`n    </Page>",
        "Condition=`" '`$(IsUnified)' == 'true' OR '`$(HasNvapi)' == 'true' `">`r`n      <Generator>MSBuild:Compile</Generator>`r`n      <SubType>Designer</SubType>`r`n    </Page>")
    $t = $t.Replace(
        "<Compile Include=`"CustomResolutionWindow.xaml.cs`" Condition=`" '`$(HasNvapi)' == 'true' `">",
        "<Compile Include=`"CustomResolutionWindow.xaml.cs`" Condition=`" '`$(IsUnified)' == 'true' OR '`$(HasNvapi)' == 'true' `">")
    Write-Utf8NoBom $p $t
    # 提示消息也要加"合并版"分支，否则构建日志会误报成 NVIDIA 版
    $t = Read-Utf8NoBom $p
    if ($t -notmatch '合并版（NVAPI \+ ADLX') {
        $t = $t.Replace(
            "    <Message Importance=`"high`" Condition=`" '`$(HasNvapi)' == 'true' `"`r`n             Text=`"界面进程：NVIDIA 版（含自定义分辨率窗口）`" />",
            "    <Message Importance=`"high`" Condition=`" '`$(IsUnified)' == 'true' `"`r`n             Text=`"界面进程：合并版（NVAPI + ADLX 双桥接，按显卡自动选择）`" />`r`n    <Message Importance=`"high`" Condition=`" '`$(IsUnified)' != 'true' AND '`$(HasNvapi)' == 'true' `"`r`n             Text=`"界面进程：NVIDIA 版（含自定义分辨率窗口）`" />")
        $t = $t.Replace(
            "    <Message Importance=`"high`" Condition=`" '`$(HasNvapi)' != 'true' `"`r`n             Text=`"界面进程：通用版（AMD/Intel，无 NvAPIWrapper.dll；AMD 经 ADLX 支持缩放与自定义分辨率窗口）`" />",
            "    <Message Importance=`"high`" Condition=`" '`$(IsUnified)' != 'true' AND '`$(HasNvapi)' != 'true' `"`r`n             Text=`"界面进程：通用版（AMD，无 NvAPIWrapper.dll；AMD 经 ADLX 支持缩放与自定义分辨率窗口）`" />")
        Write-Utf8NoBom $p $t
    }
    $report += "csproj: 已加 Unified 支持"
} else { $report += "csproj: 已有 Unified 支持（跳过）" }

# ---------------------------------------------------------------
# 5) 两处 UI / WinForms 的 #if WITH_NVAPI 放行 UNIFIED
# ---------------------------------------------------------------
foreach ($f in @('src-ui\SettingsWindow.xaml.cs','src\SettingsForm.cs')) {
    $p = Join-Path $Root $f
    $t = Read-Utf8NoBom $p
    if ($t -match '(?m)^#if WITH_NVAPI\s*$') {
        $t = $t -replace '(?m)^#if WITH_NVAPI\s*$', '#if WITH_NVAPI || UNIFIED'
        Write-Utf8NoBom $p $t
        $report += "$f : 已放行 UNIFIED"
    } else { $report += "$f : 已处理（跳过）" }
}

$report | ForEach-Object { Write-Host "  $_" }
Write-Host ""
Write-Host "补丁完成。构建命令：" -ForegroundColor Cyan
Write-Host "  托盘：csc /define:UNIFIED /define:WITH_NVAPI /r:NvAPIWrapper.dll ..."
Write-Host "  界面：msbuild src-ui\PortalJump.UI.csproj /p:Configuration=Release /p:Unified=true"
