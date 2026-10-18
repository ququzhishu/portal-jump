# =====================================================================
#  像素风格化（把任意素材变成像素画）
#
#  做法：格子化降采样（块内平均）-> 限制调色板色数 -> 最近邻放大。
#  输出 PNG 之后可以直接喂给 make-icon.ps1 -Nearest 出 .ico 与头像。
#
#  例：.\pixelize.ps1 -Source ..\candidates\icon-anime-wai.png `
#                     -Out ..\candidates\pixel-icon-64.png -Grid 64 -Palette 24
#
#  注意：本脚本必须保持 UTF-8 BOM，否则 PowerShell 5.1 按 ANSI 读会破坏中文字符串。
# =====================================================================
param(
  [Parameter(Mandatory=$true)][string]$Source,
  [Parameter(Mandatory=$true)][string]$Out,
  [int]$Grid = 64,            # 像素格数（越小越"像素"，64~96 适合图标）
  [int]$Palette = 24,         # 调色板色数（0 = 不限制颜色）
  [int]$UpScale = 512,        # 输出尺寸
  [int]$AlphaThreshold = 8,   # alpha 低于此值的格子直接透明
  [switch]$KeyWhite,          # 把接近纯白的格子设为透明
  [int]$KeyThreshold = 242
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Load-Bitmap([string]$path){
  $bytes = [IO.File]::ReadAllBytes($path)
  $ms = [IO.MemoryStream]::new($bytes)
  $img = [System.Drawing.Image]::FromStream($ms)
  $bmp = [System.Drawing.Bitmap]::new($img.Width, $img.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.DrawImage($img, 0, 0, $img.Width, $img.Height)
  $g.Dispose(); $img.Dispose(); $ms.Dispose()
  return $bmp
}

Write-Host ("像素化: {0}" -f (Split-Path $Source -Leaf))
$src = Load-Bitmap $Source

# ---------- 1) 块内平均降采样到 Grid×Grid ----------
$cellW = $src.Width / $Grid
$cellH = $src.Height / $Grid
$cells = @{}
for($gy=0; $gy -lt $Grid; $gy++){
  for($gx=0; $gx -lt $Grid; $gx++){
    $x0=[int][Math]::Floor($gx*$cellW); $x1=[int][Math]::Ceiling(($gx+1)*$cellW)
    $y0=[int][Math]::Floor($gy*$cellH); $y1=[int][Math]::Ceiling(($gy+1)*$cellH)
    if($x1 -gt $src.Width){$x1=$src.Width}; if($y1 -gt $src.Height){$y1=$src.Height}
    $sr=0.0;$sg=0.0;$sb=0.0;$sa=0.0;$n=0
    for($y=$y0;$y -lt $y1;$y++){
      for($x=$x0;$x -lt $x1;$x++){
        $c=$src.GetPixel($x,$y)
        $a=$c.A/255.0
        $sr+=$c.R*$a; $sg+=$c.G*$a; $sb+=$c.B*$a; $sa+=$a; $n++
      }
    }
    if($n -eq 0){ $cells[$gy*$Grid+$gx]=@{R=0;G=0;B=0;A=0}; continue }
    if($sa -le 0.0001){ $cells[$gy*$Grid+$gx]=@{R=0;G=0;B=0;A=0}; continue }
    $cells[$gy*$Grid+$gx]=@{
      R=[int][Math]::Round($sr/$sa); G=[int][Math]::Round($sg/$sa); B=[int][Math]::Round($sb/$sa)
      A=[int][Math]::Round(($sa/$n)*255)
    }
  }
}
$src.Dispose()

# ---------- 2) 调色板：先按 4bit/通道 统计，取出现最多的若干色 ----------
if($Palette -gt 0){
  $hist=@{}
  for($gy=0;$gy -lt $Grid;$gy++){
    for($gx=0;$gx -lt $Grid;$gx++){
      $p=$cells[$gy*$Grid+$gx]
      if($p.A -lt $AlphaThreshold){ continue }
      $key = ('{0},{1},{2}' -f [int]($p.R/16),[int]($p.G/16),[int]($p.B/16))
      if($hist.ContainsKey($key)){ $hist[$key]++ } else { $hist[$key]=1 }
    }
  }
  $pal = New-Object System.Collections.ArrayList
  foreach($kv in ($hist.GetEnumerator() | Sort-Object -Property Value -Descending | Select-Object -First $Palette)){
    $parts=$kv.Key -split ','
    [void]$pal.Add(@([int]$parts[0]*16+8, [int]$parts[1]*16+8, [int]$parts[2]*16+8))
  }
  Write-Host ("调色板: {0} 色（原图 4bit 统计取前 {1}）" -f $pal.Count, $Palette)

  # 每个格子吸附到最近的调色板色
  for($gy=0;$gy -lt $Grid;$gy++){
    for($gx=0;$gx -lt $Grid;$gx++){
      $p=$cells[$gy*$Grid+$gx]
      if($p.A -lt $AlphaThreshold){ continue }
      $best=0; $bestD=[double]::MaxValue
      for($i=0;$i -lt $pal.Count;$i++){
        $dr=$p.R-$pal[$i][0]; $dg=$p.G-$pal[$i][1]; $db=$p.B-$pal[$i][2]
        $d=$dr*$dr+$dg*$dg+$db*$db
        if($d -lt $bestD){ $bestD=$d; $best=$i }
      }
      $cells[$gy*$Grid+$gx]=@{ R=$pal[$best][0]; G=$pal[$best][1]; B=$pal[$best][2]; A=$p.A }
    }
  }
}

# ---------- 3) 白底透明化（可选） ----------
if($KeyWhite){
  for($gy=0;$gy -lt $Grid;$gy++){
    for($gx=0;$gx -lt $Grid;$gx++){
      $p=$cells[$gy*$Grid+$gx]
      if($p.A -ge $AlphaThreshold -and $p.R -ge $KeyThreshold -and $p.G -ge $KeyThreshold -and $p.B -ge $KeyThreshold){
        $cells[$gy*$Grid+$gx]=@{ R=$p.R; G=$p.G; B=$p.B; A=0 }
      }
    }
  }
  Write-Host ("去白底: 阈值 {0}" -f $KeyThreshold)
}

# ---------- 4) 最近邻放大输出 ----------
$outBmp = [System.Drawing.Bitmap]::new($UpScale, $UpScale, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($outBmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.Clear([System.Drawing.Color]::Transparent)
$scale = $UpScale / $Grid
$brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
for($gy=0;$gy -lt $Grid;$gy++){
  for($gx=0;$gx -lt $Grid;$gx++){
    $p=$cells[$gy*$Grid+$gx]
    if($p.A -lt $AlphaThreshold){ continue }
    $brush.Color = [System.Drawing.Color]::FromArgb($p.A, $p.R, $p.G, $p.B)
    $g.FillRectangle($brush, [single]($gx*$scale), [single]($gy*$scale), [single]([Math]::Ceiling($scale)), [single]([Math]::Ceiling($scale)))
  }
}
$brush.Dispose(); $g.Dispose()
$outBmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$outBmp.Dispose()
Write-Host ("完成 -> {0}  ({1}px 输出, {2}×{2} 像素格)" -f $Out, $UpScale, $Grid)
