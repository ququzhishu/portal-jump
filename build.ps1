# =====================================================================
#  折返跃迁 Portal Jump 构建脚本
#
#  用法：
#    .\build.ps1            只编译到 .\PortalJump\（就地更新本机使用的程序）
#    .\build.ps1 -Dist      额外产出 .\dist\PortalJump-<版本>\ 可整包拷走的目录
#    .\build.ps1 -Zip       在 -Dist 基础上再打一个 zip
#    .\build.ps1 -Run       编译完直接启动（便于本机验证）
#
#  依赖：Windows 自带的 .NET Framework 编译器（csc.exe）+ NvAPIWrapper.dll
#        不需要 Visual Studio，也不需要联网。
#
#  ⚠️ 本文件必须保持 UTF-8 带 BOM：Windows PowerShell 5.1 对无 BOM 文件按 ANSI 读，
#     中文会变乱码并直接语法报错。改完用下面两行补回 BOM：
#       $p='build.ps1'; $t=[IO.File]::ReadAllText($p,[Text.Encoding]::UTF8)
#       [IO.File]::WriteAllText($p,$t,(New-Object Text.UTF8Encoding($true)))
# =====================================================================
[CmdletBinding()]
param(
    [switch]$Dist,
    [switch]$Zip,
    [switch]$Run,
    [switch]$Ui          # 同时构建 WPF 界面进程 PortalJump.UI.exe（迁移期默认关，第 4 轮转正）
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$srcDir = Join-Path $root 'src'
$runDir = Join-Path $root 'PortalJump'
$exeName = 'PortalJump.exe'
$legacyRunDir = Join-Path $root 'ResSwitch'          # 旧版运行目录（改名前的名字）
$version = '0.10'

# 版本号以源码里的常量为准（AppInfo.cs，兼容旧位置 Program.cs），避免两处写不一样
foreach ($candidate in @('AppInfo.cs', 'Program.cs')) {
    $p = Join-Path $srcDir $candidate
    if (Test-Path $p) {
        # 注意锚定行首：源码注释里也写了同样的示例文本，不锚定会匹配到注释
        $m = Select-String -Path $p -Pattern '^\s*public const string Version = "([^"]+)"'
        if ($m) { $version = $m.Matches[0].Groups[1].Value; break }
    }
}

Write-Host "== 折返跃迁 Portal Jump $version 构建 ==" -ForegroundColor Magenta

# ---- 找编译器（优先 64 位 .NET Framework 自带 csc） ----
$candidates = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$csc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw "找不到 csc.exe，请先安装 .NET Framework 4.x 运行时/开发包。" }
Write-Host "编译器: $csc"

$nvapi = Join-Path $root 'NvAPIWrapper.dll'
# 注意：NvAPIWrapper.dll 只在编 NVIDIA 版时需要。
# AMD 机器上通常没有这个文件，不能因此让整个构建失败 ——
# 那种情况下只编通用版，并明确告诉用户"NVIDIA 版被跳过"。
$hasNvapi = Test-Path $nvapi

$sources = Get-ChildItem (Join-Path $srcDir '*.cs') | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw "src 目录里没有找到任何 .cs 源码。" }

New-Item -ItemType Directory -Force -Path $runDir | Out-Null

# ---- 程序正在运行时 exe 会被占用，先停掉，编译完再自动启动回来 ----
# 新旧名字都查一遍：改名后可能还残留旧进程（它占着全局单实例互斥体）
$running = @(Get-Process -Name 'PortalJump', 'ResSwitch' -ErrorAction SilentlyContinue)
$wasRunning = $false
if ($running.Count -gt 0) {
    $wasRunning = $true
    Write-Host ("检测到程序正在运行（{0}），先结束它以便覆盖程序文件…" -f (($running | ForEach-Object { $_.ProcessName + ':' + $_.Id }) -join ', ')) -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 900
}

# ---- 编译（/codepage:65001 保证中文在任何语言的系统上都不会乱码） ----
# 两个版本共用同一套源码，差别只有两处：
#   · NVIDIA 版：/define:WITH_NVAPI + 引用 NvAPIWrapper.dll + 编译 CustomResolutionForm.cs
#   · 通用版  ：不定义该符号、不引用 NVAPI、排除 CustomResolutionForm.cs
# 通用版是给 AMD 用户用的 —— 他们机器上没有 NvAPIWrapper.dll，
# 通用版**不需要**那个文件也能跑。
$commonRefs = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')
$icoArg = @()
if (Test-Path (Join-Path $root 'assets\PortalJump.ico')) { $icoArg = @("/win32icon:$(Join-Path $root 'assets\PortalJump.ico')") }

$genericSources = $sources | Where-Object { $_ -notlike '*CustomResolutionForm.cs' }

# ---- NVIDIA 版：需要 NvAPIWrapper.dll，没有就跳过（AMD 机器上属正常情况）----
$exe = $null
if ($hasNvapi) {
    $compilerArgs = @(
        '/nologo', '/utf8output', '/codepage:65001',
        '/target:winexe', '/optimize+',
        '/define:WITH_NVAPI',
        "/out:$runDir\$exeName",
        "/r:$nvapi"
    ) + $commonRefs + $icoArg + $sources

    $output = & $csc @compilerArgs 2>&1 | Out-String -Width 400
    if ($LASTEXITCODE -ne 0) {
        Write-Host $output -ForegroundColor Red
        throw "编译失败（exit=$LASTEXITCODE）"
    }
    if ($output.Trim().Length -gt 0) { Write-Host $output }

    Copy-Item $nvapi $runDir -Force
    $exe = Get-Item (Join-Path $runDir $exeName)
    Write-Host ("编译成功: {0}  ({1:N0} 字节, {2})" -f $exe.FullName, $exe.Length, $exe.LastWriteTime) -ForegroundColor Green
}
else {
    Write-Host "跳过 NVIDIA 版：当前目录没有 NvAPIWrapper.dll（在 AMD 机器上这是正常的）。" -ForegroundColor Yellow
    Write-Host "  只编译通用版。要编 NVIDIA 版，请把 NvAPIWrapper.dll 放到 $root 下。" -ForegroundColor Yellow
}

# ---- 通用版（不含 NVAPI）：给 AMD 用户 ----
$genericExe = Join-Path $runDir 'PortalJump-generic.exe'
$genericArgs = @(
    '/nologo', '/utf8output', '/codepage:65001',
    '/target:winexe', '/optimize+',
    "/out:$genericExe"
) + $commonRefs + $icoArg + $genericSources

$gOut = & $csc @genericArgs 2>&1 | Out-String -Width 400
if ($LASTEXITCODE -ne 0) {
    Write-Host $gOut -ForegroundColor Red
    throw "通用版编译失败（exit=$LASTEXITCODE）"
}
$gexe = Get-Item $genericExe
Write-Host ("编译成功: {0}  ({1:N0} 字节)  ← 通用版，AMD 用，不需要 NvAPIWrapper.dll" -f $gexe.FullName, $gexe.Length) -ForegroundColor Green

# ---- 仅分辨率版：完全不碰缩放，只切分辨率 ----
# 这是"零厂商依赖"的版本：任何显卡都能用，缩放方式由用户在驱动面板里设好，
# 程序切换分辨率时驱动会沿用。最不容易出问题，适合只想要"自动切分辨率"的用户。
$resOnlyExe = Join-Path $runDir 'PortalJump-resonly.exe'
$resOnlyArgs = @(
    '/nologo', '/utf8output', '/codepage:65001',
    '/target:winexe', '/optimize+',
    '/define:RESOLUTION_ONLY',
    "/out:$resOnlyExe"
) + $commonRefs + $icoArg + $genericSources

$rOut = & $csc @resOnlyArgs 2>&1 | Out-String -Width 400
if ($LASTEXITCODE -ne 0) {
    Write-Host $rOut -ForegroundColor Red
    throw "仅分辨率版编译失败（exit=$LASTEXITCODE）"
}
$resOnlyItem = Get-Item $resOnlyExe
Write-Host ("编译成功: {0}  ({1:N0} 字节)  ← 仅分辨率版，任何显卡通用，不碰缩放" -f $resOnlyItem.FullName, $resOnlyItem.Length) -ForegroundColor Green

# ---- WPF 界面进程（-Ui）：用系统自带的 MSBuild 4.8 编译 XAML 工程，不需要 .NET SDK ----
# csproj 会按 NvAPIWrapper.dll 是否存在自动选版本，并输出到不同子目录：
#   PortalJump\nvidia\PortalJump.UI.exe   ← NVIDIA 版界面（含自定义分辨率窗口）
#   PortalJump\generic\PortalJump.UI.exe  ← 通用版界面（AMD）
# 所以这里要编两次（有 NVAPI 时），分别拿到两个界面 exe。
$uiExeNv = Join-Path $runDir 'nvidia\PortalJump.UI.exe'
$uiExeGen = Join-Path $runDir 'generic\PortalJump.UI.exe'
$uiExe = $uiExeNv       # 兼容后面旧逻辑的默认值
if ($Ui) {
    $uiProj = Join-Path $root 'src-ui\PortalJump.UI.csproj'
    if (-not (Test-Path $uiProj)) { throw "找不到 WPF 工程: $uiProj" }
    $msbuild = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
    if (-not (Test-Path $msbuild)) { throw "找不到 MSBuild.exe（.NET Framework 4.x 自带）" }

    Write-Host "编译 WPF 界面进程…" -ForegroundColor Cyan

    # 先编当前环境对应的版本（有 NVAPI → NVIDIA 版界面；没有 → 通用版界面）
    $uiOut = & $msbuild $uiProj /p:Configuration=Release /v:m /nologo /clp:NoSummary 2>&1 | Out-String -Width 300
    if ($LASTEXITCODE -ne 0) {
        Write-Host $uiOut -ForegroundColor Red
        throw "WPF 界面进程编译失败（exit=$LASTEXITCODE）"
    }
    $uiOut -split "`r?`n" | Where-Object { $_ -match 'error|warning CS' } | ForEach-Object { Write-Host $_ }
    $uiOut -split "`r?`n" | Where-Object { $_ -match '界面进程：' } | ForEach-Object { Write-Host $_.Trim() }

    # 如果本机有 NVAPI，还要额外编一次通用版界面（临时移走 DLL 再编）
    if ($hasNvapi) {
        $nvPath = Join-Path $root 'NvAPIWrapper.dll'
        $nvTmp = Join-Path $root 'NvAPIWrapper.dll.__tmp_build'
        try {
            Move-Item -LiteralPath $nvPath -Destination $nvTmp -Force
            $gOut = & $msbuild $uiProj /p:Configuration=Release /v:m /nologo /clp:NoSummary 2>&1 | Out-String -Width 300
            if ($LASTEXITCODE -ne 0) {
                Write-Host $gOut -ForegroundColor Red
                throw "通用版 WPF 界面编译失败（exit=$LASTEXITCODE）"
            }
            $gOut -split "`r?`n" | Where-Object { $_ -match '界面进程：' } | ForEach-Object { Write-Host $_.Trim() }
        }
        finally {
            if (Test-Path $nvTmp) { Move-Item -LiteralPath $nvTmp -Destination $nvPath -Force }
        }
    }

    foreach ($pair in @(@{ Path = $uiExeNv; Label = 'NVIDIA 版界面' },
                        @{ Path = $uiExeGen; Label = '通用版界面' })) {
        if (Test-Path $pair.Path) {
            $u = Get-Item $pair.Path
            Write-Host ("编译成功: {0}  ({1:N0} 字节)  ← {2}" -f $u.FullName, $u.Length, $pair.Label) -ForegroundColor Green
        }
    }
}

# ---- 素材：程序目录里放 zoe-bg.png / zoe-icon.png 即自动生效（运行期可随时换，不必重编译） ----
foreach ($a in 'zoe-bg.png', 'zoe-icon.png') {
    $src = Join-Path $root "assets\$a"
    if (Test-Path $src) { Copy-Item -LiteralPath $src -Destination (Join-Path $runDir $a) -Force }
}

# ---- 从旧目录迁移用户配置：改名不该让用户重配一遍预设 ----
$legacyIni = Join-Path $legacyRunDir 'presets.ini'
$newIni = Join-Path $runDir 'presets.ini'
if (-not (Test-Path $newIni) -and (Test-Path $legacyIni)) {
    Copy-Item -LiteralPath $legacyIni -Destination $newIni -Force
    Write-Host "已从旧目录迁移用户配置: $newIni" -ForegroundColor Cyan
}

# ---- 清理旧版运行目录（避免残留的 ResSwitch.exe 被误启动；删前核对绝对路径） ----
if (Test-Path -LiteralPath $legacyRunDir) {
    $resolved = (Resolve-Path -LiteralPath $legacyRunDir).Path
    $expected = [System.IO.Path]::Combine($root, 'ResSwitch')
    if ($resolved -eq $expected -and (Test-Path -LiteralPath (Join-Path $resolved 'ResSwitch.exe'))) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
        Write-Host "已移除旧版运行目录: $resolved（配置已迁移到 $runDir）" -ForegroundColor Yellow
    } elseif ($resolved -eq $expected) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
        Write-Host "已清理旧版运行目录: $resolved" -ForegroundColor Yellow
    } else {
        Write-Host "跳过旧目录清理（路径核对不符）: $resolved" -ForegroundColor Yellow
    }
}

if ($wasRunning) {
    Write-Host "重新启动托盘程序（新版本）…" -ForegroundColor Cyan
    Start-Process -FilePath (Join-Path $runDir $exeName) -ArgumentList 'tray' | Out-Null
    Start-Sleep -Milliseconds 600
}

# ---- 产出可分发包 ----
if ($Dist) {
    $distRoot = Join-Path $root 'dist'

    # 一键自检脚本（两个包都要，内容相同，用 ANSI 写配合中文控制台）
    $diagCmd = @"
@echo off
echo 正在生成环境自检报告...
"%~dp0$exeName" selftest
echo.
echo 报告已保存到 portal-jump-selftest.txt
pause
"@

    # 通用的公共文件（素材、文档、示例配置、自检脚本）
    function Copy-CommonFiles($target, $uiSource) {
        if ($Ui -and $uiSource -and (Test-Path $uiSource)) { Copy-Item $uiSource (Join-Path $target 'PortalJump.UI.exe') -Force }
        foreach ($a in 'zoe-bg.png', 'zoe-icon.png') {
            $src = Join-Path $runDir $a
            if (Test-Path $src) { Copy-Item -LiteralPath $src -Destination $target -Force }
        }
        # 只放面向最终用户的使用说明。
        # 部署说明.md 里有「从源码自己构建」整节（csc/MSBuild/src 目录等），属开发过程内容，
        # 普通用户不需要，也不该出现在发布包里。
        $userDoc = Join-Path $runDir '使用说明.md'
        if (Test-Path $userDoc) { Copy-Item $userDoc $target -Force }
        $example = Join-Path $runDir 'presets.example.ini'
        if (Test-Path $example) { Copy-Item $example $target -Force }
        Set-Content -Path (Join-Path $target '环境自检.cmd') -Value $diagCmd -Encoding Default
    }

    # ============ 包 1：NVIDIA 版（带 NvAPIWrapper.dll）============
    if ($hasNvapi) {
        $pkg = Join-Path $distRoot "折返跃迁v$version-NVIDIA显卡版"
        if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $pkg | Out-Null

        Copy-Item (Join-Path $runDir $exeName) $pkg -Force
        Copy-Item $nvapi $pkg -Force
    Copy-CommonFiles $pkg $uiExeNv

        Write-Host "已产出分发包（NVIDIA 版）: $pkg" -ForegroundColor Green
    }
    else {
        Write-Host "跳过 NVIDIA 分发包（没有 NvAPIWrapper.dll）" -ForegroundColor Yellow
    }

    # ============ 包 2：通用版（AMD，不带 NvAPIWrapper.dll）============
    $pkgG = Join-Path $distRoot "折返跃迁v$version-AMD显卡版"
    if (Test-Path $pkgG) { Remove-Item $pkgG -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $pkgG | Out-Null

    # 通用版改名成 PortalJump.exe，让用户双击的就是它（不需要知道"generic"这个词）
    Copy-Item $genericExe (Join-Path $pkgG $exeName) -Force
    # 故意不复制 NvAPIWrapper.dll —— 这就是"不需要 NVIDIA"的证明
    Copy-CommonFiles $pkgG $uiExeGen

    Write-Host "已产出分发包（通用版，AMD）: $pkgG" -ForegroundColor Green
    Get-ChildItem $pkgG | Select-Object Name, Length | Format-Table -AutoSize | Out-String | Write-Host

    # ============ 包 3：仅分辨率版（任何显卡，完全不碰缩放）============
    $pkgR = Join-Path $distRoot "折返跃迁v$version-仅分辨率版"
    if (Test-Path $pkgR) { Remove-Item $pkgR -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $pkgR | Out-Null

    Copy-Item $resOnlyExe (Join-Path $pkgR $exeName) -Force
    # 同样不带 NvAPIWrapper.dll
    Copy-CommonFiles $pkgR $uiExeGen

    Write-Host "已产出分发包（仅分辨率版，任何显卡）: $pkgR" -ForegroundColor Green

    # 外发用的"先看这里"：给非技术用户看的，3 步上手 + 免责声明
    # UTF-8 带 BOM 写，记事本打开中文才不乱码
    # 两个版本各写一份，差别只在前面的"你拿到的是哪个版本"那一段。
    $readmeCommon = @"

怎么开始用（3 步）
  1) 双击 PortalJump.exe —— 屏幕右下角托盘会出现图标。程序没有主窗口，这是正常的。
  2) 右键那个托盘图标 →「打开设置界面」。
  3) 「新增」一个预设：填名称、选分辨率与缩放方式 →「关联程序」里勾上你的游戏 → 点「保存并应用」。

不确定能不能用？先跑自检
  双击「环境自检.cmd」，会生成 portal-jump-selftest.txt，
  里面会写明显卡厂商、当前走哪条缩放通路、各缩放方式是否被驱动接受。

缩放方式怎么选
  下拉框里是中文名，不是数字：不改动 / 全屏铺满（GPU 缩放）/ 全屏铺满（显示器缩放）/
  保持比例（GPU 缩放）/ 保持比例（显示器缩放）/ 居中不缩放（GPU 缩放）/
  居中不缩放（显示器缩放）/ 整数缩放（GPU 缩放）。
  拿不准就先用「全屏铺满（GPU 缩放）」。
  旧配置文件里的数字（0/1/2/3/5/6/7/8）会自动迁移成新名字，不用手改。

运行要求
  Windows 10 / 11（64 位）；.NET Framework 4.8（Win10 1903+ 与 Win11 已内置）。
  绿色程序，不用安装，整个文件夹拷走就能用。

更多说明
  使用说明.md —— 功能、界面导览、缩放方式怎么选、常见问题
  部署说明.md —— 部署到别的机器 / 卸载 / 从源码自己编译

免责声明
  本工具会直接通过显示驱动修改分辨率、刷新率与缩放模式，可能出现黑屏、花屏、
  显示异常等情况。使用前请自行确认显示器与驱动支持你要设置的模式；
  因使用本工具造成的任何直接或间接损失（含数据丢失、工作中断、硬件损坏），
  作者不承担任何责任。
  本工具为个人免费作品，按"现状"提供，不提供任何担保，与 NVIDIA / AMD / Intel 等公司无关联；
  继续使用即表示接受以上内容，不同意请不要运行。

作者：区区之数    B 站主页：https://space.bilibili.com/21100323
"@

    $readmeNv = @"
折返跃迁 Portal Jump $version（NVIDIA 版）—— 先看这里
================================================

你拿到的是【NVIDIA 版】。
  适合 NVIDIA 显卡。缩放走 NVIDIA 官方 NVAPI 接口，支持全部缩放方式
  （含「整数缩放」）和「创建自定义分辨率」。
  这个包里带了 NvAPIWrapper.dll，请不要删它。

用之前请先做这一步
  打开 NVIDIA 控制面板 →「显示」→「调整桌面尺寸和位置」，
  取消勾选"覆盖由游戏和程序设置的缩放模式"，点应用。
  不做这一步：分辨率能切，但缩放可能不会变（NVIDIA 上最常见的坑）。
"@ + $readmeCommon

    $readmeGen = @"
折返跃迁 Portal Jump $version（通用版）—— 先看这里
================================================

你拿到的是【AMD 版】，适用于 AMD 显卡。
  这个版本不需要 NVIDIA 驱动，也不需要 NvAPIWrapper.dll（包里本来就没有）。

【AMD 显卡：已通过官方 ADLX 在 RX 9070 XT 实机验证】
  · 缩放方式（全屏铺满 / 保持比例 / 居中）走 AMD 官方 ADLX 接口，精确生效、可回读；
  · 「整数缩放」走 ADLX（程序会先自动打开 GPU scaling 再启用整数缩放）；
  · 「创建自定义分辨率」走 ADLX：在设置界面点「创建自定义分辨率」，
    填好宽高（刷新率与时序沿用当前模式）即可写入 AMD 驱动，过程不黑屏；
    创建后回到主界面，在分辨率下拉里选中它并「应用」即可切换。
  · 缩放通路不降级：若 ADLX 不可用会明确报错，不会偷偷改走不可靠的通用接口。

先跑一次自检确认环境（建议用管理员身份运行）：
  双击「环境自检.cmd」，打开生成的 portal-jump-selftest.txt，
  看到「缩放通路：AMD ADLX」「ADLX 桥接：已就绪」「自定义分辨率：支持」即正常。
  若用远程桌面 / 向日葵会话，ADLX 枚举不到物理显示器，请在本机直连显示器时使用。

小提示
  · 自定义分辨率在复制 / Eyefinity 多屏模式下不可用，请先切到单屏独立显示。
  · 其余功能——按程序自动切分辨率、切缩放方式、托盘常驻——均可用。

如果你其实用的是 NVIDIA 显卡
  请改用 NVIDIA 版（文件名里带 NVIDIA 的那个包），功能更全。
"@ + $readmeCommon

    $readmeResOnly = @"
折返跃迁 Portal Jump $version（仅分辨率版）—— 先看这里
================================================

你拿到的是【仅分辨率版】。
  这个版本只做一件事：按程序自动切换分辨率。
  它**完全不碰缩放设置**，所以任何显卡（NVIDIA / AMD）都能用，也不会因为
  缩放设不上而出问题。这是最稳的一个版本。

怎么用
  1) 先在显卡驱动控制面板里把缩放方式设置一次（只需一次）：
       · NVIDIA：NVIDIA 控制面板 → 显示 → 调整桌面尺寸和位置
       · AMD   ：AMD Software → 设置 → 显示 → 缩放模式
     选好你要的缩放方式（全屏铺满 / 保持比例 / 居中不缩放），点应用。
  2) 双击 PortalJump.exe，右下角托盘出现图标。
  3) 右键托盘 →「打开设置界面」→ 新增预设：填名称、选分辨率、关联程序 → 保存。
  4) 之后打开那个程序，就会自动切到对应分辨率，缩放方式沿用你在第 1 步设的。

  之所以要你先手动设缩放：这个版本不调用任何显卡厂商接口，只能靠驱动自己的设置。
  好处是零依赖、跨厂商、极稳；代价是缩放方式不能按预设自动变。

适用场景
  · 你只需要"打开游戏自动切分辨率"
  · 你的显卡不是 NVIDIA，也不想折腾缩放接口
  · 你希望程序尽可能不出问题

不适合的场景
  · 你想让不同预设用不同的缩放方式（那需要完整版）
  · 你需要整数缩放或创建自定义分辨率

"@ + $readmeCommon

    if ($hasNvapi) {
        [IO.File]::WriteAllText((Join-Path $pkg '先看这里.txt'), $readmeNv, (New-Object Text.UTF8Encoding($true)))
    }
    [IO.File]::WriteAllText((Join-Path $pkgG '先看这里.txt'), $readmeGen, (New-Object Text.UTF8Encoding($true)))
    [IO.File]::WriteAllText((Join-Path $pkgR '先看这里.txt'), $readmeResOnly, (New-Object Text.UTF8Encoding($true)))

    # ============ 包 3：AMD 开发包（源码 + 工具 + 文档，不含任何编译产物）============
    # 给"要在 AMD 机器上继续开发/验证"的人用。
    # 刻意不含：NvAPIWrapper.dll（AMD 上没用）、exe（避免拿旧产物冒充新构建）。
    $pkgD = Join-Path $distRoot "折返跃迁v$version-AMD开发源码包"
    if (Test-Path $pkgD) { Remove-Item $pkgD -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $pkgD | Out-Null

    # 源码（排除 obj/bin 与已编译产物）
    foreach ($d in 'src', 'src-ui', 'tools') {
        $from = Join-Path $root $d
        if (Test-Path $from) {
            $to = Join-Path $pkgD $d
            New-Item -ItemType Directory -Force -Path $to | Out-Null
            Get-ChildItem $from -File -Recurse |
                Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' -and
                               $_.Extension -in '.cs','.xaml','.csproj','.md','.ps1' } |
                ForEach-Object {
                    $rel = $_.FullName.Substring($from.Length).TrimStart('\')
                    $dst = Join-Path $to $rel
                    $dstDir = Split-Path $dst -Parent
                    if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Force -Path $dstDir | Out-Null }
                    Copy-Item -LiteralPath $_.FullName -Destination $dst -Force
                }
        }
    }

    # 素材：只带运行/构建需要的。candidates 是历史候选图（200MB+），开发用不上，跳过。
    $assetsFrom = Join-Path $root 'assets'
    if (Test-Path $assetsFrom) {
        $assetsTo = Join-Path $pkgD 'assets'
        New-Item -ItemType Directory -Force -Path $assetsTo | Out-Null
        Get-ChildItem $assetsFrom -File -Recurse |
            Where-Object { $_.FullName -notmatch '\\candidates\\' -and
                           $_.Extension -in '.png','.ico','.ps1','.md' } |
            ForEach-Object {
                $rel = $_.FullName.Substring($assetsFrom.Length).TrimStart('\')
                $dst = Join-Path $assetsTo $rel
                $dstDir = Split-Path $dst -Parent
                if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Force -Path $dstDir | Out-Null }
                Copy-Item -LiteralPath $_.FullName -Destination $dst -Force
            }
    }

    # 附带根目录下的开发说明文档（若存在）。这些文档仅供开发/交接使用，
    # 不在公开仓库里，所以逐个 Test-Path 容错，缺了也不影响构建。
    foreach ($f in 'build.ps1') {
        $p = Join-Path $root $f
        if (Test-Path $p) { Copy-Item $p $pkgD -Force }
    }
    Get-ChildItem $root -File -Filter '*.md' |
        Where-Object { $_.Name -ne 'README.md' -and $_.Name -ne 'LICENSE' } |
        ForEach-Object { Copy-Item $_.FullName $pkgD -Force }

    Write-Host "已产出开发包（AMD 源码包）: $pkgD" -ForegroundColor Green

    if ($Zip) {
        # 压缩包命名：中文 + 用途，一眼能看出该给谁。
        # 解压后里面的文件夹名也跟压缩包一致，不会出现"解压出一堆看不懂的英文名"。
        $zipTargets = @()
        if ($hasNvapi) {
            $zipTargets += @{ Dir = $pkg; Name = "折返跃迁v$version-NVIDIA显卡版.zip" }
        }
        $zipTargets += @{ Dir = $pkgG; Name = "折返跃迁v$version-AMD显卡版.zip" }
        $zipTargets += @{ Dir = $pkgR; Name = "折返跃迁v$version-仅分辨率版.zip" }
        $zipTargets += @{ Dir = $pkgD; Name = "折返跃迁v$version-AMD开发源码包.zip" }
        foreach ($pair in $zipTargets) {
            $zipPath = Join-Path $distRoot $pair.Name
            if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
            # 打包目录本身：解压后是一个文件夹，文件不会散在当前目录
            Compress-Archive -Path $pair.Dir -DestinationPath $zipPath
            Write-Host ("已打包: {0}  ({1:N2} MB)" -f $pair.Name, ((Get-Item $zipPath).Length/1MB)) -ForegroundColor Green
        }
    }
}

if ($Run) {
    Write-Host "启动折返跃迁（设置界面）…" -ForegroundColor Cyan
    Start-Process -FilePath (Join-Path $runDir $exeName) -ArgumentList 'settings'
}
