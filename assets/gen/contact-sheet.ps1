# =====================================================================
#  把候选图拼成一张对比表（每个候选按固定格子缩放，带文件名标注）
#  用法: powershell -File contact-sheet.ps1 -Dir ..\candidates -Out ..\candidates\_sheet.png
# =====================================================================
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Dir,
    [string]$Out = '',
    [int]$CellW = 420,
    [int]$CellH = 420,
    [int]$Cols = 3
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$files = Get-ChildItem -LiteralPath $Dir -Filter *.png | Where-Object { $_.Name -notlike '_*' } | Sort-Object Name
if ($files.Count -eq 0) { Write-Host '没有找到候选图'; exit 1 }
if (-not $Out) { $Out = Join-Path $Dir '_sheet.png' }

$rows = [math]::Ceiling($files.Count / $Cols)
$labelH = 26
$sheet = New-Object System.Drawing.Bitmap ([int]($Cols*$CellW)), ([int]($rows*($CellH+$labelH)))
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.Clear([System.Drawing.Color]::FromArgb(255, 249, 246, 252))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font = New-Object System.Drawing.Font('Microsoft YaHei UI', 11)
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 40, 26, 48))
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 220, 210, 230))

for ($i = 0; $i -lt $files.Count; $i++) {
    $col = $i % $Cols
    $row = [math]::Floor($i / $Cols)
    $x = $col * $CellW
    $y = $row * ($CellH + $labelH)
    $img = [System.Drawing.Image]::FromFile($files[$i].FullName)
    $scale = [math]::Min(($CellW-16)/$img.Width, ($CellH-16)/$img.Height)
    $w = [int]($img.Width*$scale); $h = [int]($img.Height*$scale)
    $g.DrawImage($img, ($x + ($CellW-$w)/2), ($y + ($CellH-$h)/2 + 8), $w, $h)
    $g.DrawRectangle($pen, $x+4, $y+4, $CellW-8, $CellH-8)
    $g.DrawString($files[$i].BaseName, $font, $brush, ($x+8), ($y+$CellH+2))
    $img.Dispose()
}
$g.Dispose()
$sheet.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
Write-Host "对比表已保存: $Out  ($($files.Count) 张)"
