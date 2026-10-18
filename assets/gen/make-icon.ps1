# =====================================================================
#  折返跃迁 —— 图标 / 头像 / 背景 后期处理
#
#  用法示例：
#    powershell -File make-icon.ps1 -Source ..\candidates\ic6-anime-frame-a.png `
#        -KeyWhite -RoundAvatar
#
#  产出：
#    ..\PortalJump.ico     多尺寸图标（16/24/32/48/64 用 DIB，128/256 用 PNG 压缩）
#    ..\zoe-icon.png       512×512 头像（-KeyWhite 时背景透明，-RoundAvatar 时圆形）
#    ..\zoe-bg.png         （可选 -Background 时）整备后的背景图
#
#  说明：小尺寸用 DIB（BMP）条目，兼容性最好；大尺寸用 PNG 压缩，体积小。
# =====================================================================
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Source,
    [string]$AssetDir = 'C:\工作区\res-switch\assets',
    [string]$IcoName = 'PortalJump.ico',
    [string]$AvatarName = 'zoe-icon.png',
    [switch]$KeyWhite,                 # 去掉接近白/浅色的背景 -> 透明
    [int]$KeyThreshold = 236,          # 亮度阈值（>= 该值的像素视为背景）
    [int]$KeyFeather = 16,             # 边缘羽化带宽
    [switch]$RoundAvatar,              # 头像裁成圆形
  [switch]$Nearest,                  # 缩放用最近邻（像素风素材必须用这个，否则块状边缘会被插值糊掉）
    [string]$Background,               # 可选：把这张图整备成背景图
    [int]$BgWidth = 1600
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-Argb32([int]$w, [int]$h) {
    New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
}

# ---------- 居中裁成正方形 ----------
function Get-Square([System.Drawing.Image]$img) {
    $side = [Math]::Min($img.Width, $img.Height)
    $x = [int](($img.Width - $side) / 2)
    $y = [int](($img.Height - $side) / 2)
    $bmp = New-Argb32 $side $side
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.DrawImage($img, (New-Object System.Drawing.Rectangle 0, 0, $side, $side),
                 (New-Object System.Drawing.Rectangle $x, $y, $side, $side), [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    return $bmp
}

# ---------- 去底：接近白/浅色的背景变透明，边缘羽化 ----------
function Remove-LightBackground([System.Drawing.Bitmap]$bmp, [int]$thr, [int]$feather) {
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        for ($x = 0; $x -lt $bmp.Width; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $lum = [Math]::Min($c.R, [Math]::Min($c.G, $c.B))   # 用最暗通道判"白"
            if ($lum -ge $thr) {
                $a = 0
            } elseif ($lum -ge ($thr - $feather)) {
                $a = [int](255 * ($thr - $lum) / [Math]::Max(1, $feather))
            } else {
                continue
            }
            $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb([Math]::Max(0, [Math]::Min(255, $a)), $c.R, $c.G, $c.B))
        }
    }
    return $bmp
}

# ---------- 圆形 alpha 蒙版 ----------
function Apply-RoundMask([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            $dx = ($x - $w / 2.0) / ($w / 2.0)
            $dy = ($y - $h / 2.0) / ($h / 2.0)
            $d = [Math]::Sqrt($dx * $dx + $dy * $dy)
            if ($d -le 0.985) { continue }
            $c = $bmp.GetPixel($x, $y)
            $a = if ($d -ge 1.0) { 0 } else { [int]($c.A * (1.0 - ($d - 0.985) / 0.015)) }
            $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb([Math]::Max(0, [Math]::Min(255, $a)), $c.R, $c.G, $c.B))
        }
    }
    return $bmp
}

# ---------- 32 位 DIB 条目（小尺寸图标用，兼容性最好） ----------
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]($w * $h * 4)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    $rowBytes = [int][Math]::Ceiling($w / 8.0)
    $rowBytes += ((4 - ($rowBytes % 4)) % 4)
    $zero = New-Object byte[] $rowBytes
    for ($y = 0; $y -lt $h; $y++) { $bw.Write($zero, 0, $rowBytes) }
    $bw.Flush()
    return $ms.ToArray()
}

# ---------- 生成多尺寸 .ico ----------
function Write-Ico([System.Drawing.Bitmap]$src, [string]$path, [int[]]$sizes) {
    $blobs = @()
    foreach ($s in $sizes) {
        $b = New-Argb32 $s $s
        $g = [System.Drawing.Graphics]::FromImage($b)
        $g.InterpolationMode = if ($Nearest) { [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor } else { [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic }
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.DrawImage($src, 0, 0, $s, $s)
        $g.Dispose()
        if ($s -ge 128) {
            $ms = New-Object System.IO.MemoryStream
            $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $bytes = $ms.ToArray()
            $ms.Dispose()
        } else {
            $bytes = Get-DibBytes $b
        }
        $blobs += , @{ size = $s; bytes = $bytes }
        $b.Dispose()
    }

    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$blobs.Count)
    $offset = 6 + 16 * $blobs.Count
    foreach ($bl in $blobs) {
        $dim = if ($bl.size -ge 256) { 0 } else { $bl.size }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([int16]1); $bw.Write([int16]32)
        $bw.Write([int]$bl.bytes.Length); $bw.Write([int]$offset)
        $offset += $bl.bytes.Length
    }
    foreach ($bl in $blobs) { $bw.Write($bl.bytes, 0, $bl.bytes.Length) }
    $bw.Flush(); $bw.Close()
    return $path
}

# =====================================================================
$img = [System.Drawing.Image]::FromFile($Source)
Write-Host ("源图: {0}  {1}x{2}" -f (Split-Path $Source -Leaf), $img.Width, $img.Height)

$sq = Get-Square $img
if ($KeyWhite) {
    Write-Host ("去底：亮度阈值 {0}，羽化 {1}" -f $KeyThreshold, $KeyFeather) -ForegroundColor Cyan
    $sq = Remove-LightBackground $sq $KeyThreshold $KeyFeather
}
if ($RoundAvatar) { $sq = Apply-RoundMask $sq }

# 头像（512）
$av = New-Argb32 512 512
$g = [System.Drawing.Graphics]::FromImage($av)
$g.InterpolationMode = if ($Nearest) { [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor } else { [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic }
$g.Clear([System.Drawing.Color]::Transparent)
$g.DrawImage($sq, 0, 0, 512, 512)
$g.Dispose()
$avPath = Join-Path $AssetDir $AvatarName
$av.Save($avPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host ("头像: {0}  ({1:N0} 字节)" -f $avPath, (Get-Item $avPath).Length) -ForegroundColor Green

# 多尺寸图标
$icoPath = Join-Path $AssetDir $IcoName
Write-Ico $sq $icoPath @(256, 128, 64, 48, 32, 24, 16) | Out-Null
Write-Host ("图标: {0}  ({1:N0} 字节, 7 个尺寸)" -f $icoPath, (Get-Item $icoPath).Length) -ForegroundColor Green

# 背景整备
if ($Background) {
    $bi = [System.Drawing.Image]::FromFile($Background)
    $w = $BgWidth
    $h = [int]($bi.Height * $w / $bi.Width)
    $bg = New-Argb32 $w $h
    $g = [System.Drawing.Graphics]::FromImage($bg)
    $g.InterpolationMode = if ($Nearest) { [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor } else { [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic }
    $g.DrawImage($bi, 0, 0, $w, $h)
    $g.Dispose()
    $bgPath = Join-Path $AssetDir 'zoe-bg.png'
    $bg.Save($bgPath, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host ("背景: {0}  {1}x{2}  ({3:N0} 字节)" -f $bgPath, $w, $h, (Get-Item $bgPath).Length) -ForegroundColor Green
    $bg.Dispose(); $bi.Dispose()
}

$av.Dispose(); $sq.Dispose(); $img.Dispose()
Write-Host "完成。把 zoe-icon.png / zoe-bg.png 放进程序目录即生效（不必重编译）。"
