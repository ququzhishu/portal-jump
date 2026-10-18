# =====================================================================
#  用 ComfyUI 的放大模型（ESRGAN 等）放大一张图 —— LoadImage -> UpscaleModelLoader
#  -> ImageUpscaleWithModel ->（可选二次缩放）-> SaveImage
#
#  例：.\comfy-upscale.ps1 -Source C:\path\a.png -OutDir C:\path\out `
#                          -Model RealESRGAN_x4plus_anime_6B.pth -Label portal
#
#  注意：必须保持 UTF-8 BOM，否则 PowerShell 5.1 按 ANSI 读会破坏中文字符串。
# =====================================================================
param(
  [Parameter(Mandatory=$true)][string]$Source,
  [Parameter(Mandatory=$true)][string]$OutDir,
  [string]$Model = 'RealESRGAN_x4plus_anime_6B.pth',
  [string]$Label = 'upscaled',
  [int]$SecondScale = 0,        # >0 时再用 lanczos 缩放到这个边长（例如 512）
  [string]$Server = 'http://127.0.0.1:8188'
)
$ErrorActionPreference = 'Stop'

$comfyInput  = 'C:\ComfyUI\ComfyUI\input'
$comfyOutput = 'C:\ComfyUI\ComfyUI\output'
if(-not (Test-Path $comfyInput)){ throw "找不到 ComfyUI input 目录: $comfyInput" }

# 1) 源图放进 ComfyUI 的 input 目录
$inName = "pj-up-$([Guid]::NewGuid().ToString('N').Substring(0,8)).png"
Copy-Item -LiteralPath $Source -Destination (Join-Path $comfyInput $inName) -Force
Write-Host ("源图 -> {0}" -f $inName)

# 2) 组装工作流
$prompt = [ordered]@{
  '1' = @{ class_type='LoadImage'; inputs=@{ image=$inName } }
  '2' = @{ class_type='UpscaleModelLoader'; inputs=@{ model_name=$Model } }
  '3' = @{ class_type='ImageUpscaleWithModel'; inputs=@{ upscale_model=@('2',0); image=@('1',0) } }
}
$last = '3'
if($SecondScale -gt 0){
  $prompt['4'] = @{ class_type='ImageScale'; inputs=@{ image=@('3',0); upscale_method='lanczos'; width=$SecondScale; height=$SecondScale; crop='disabled' } }
  $last = '4'
}
$prompt[([string]([int]$last + 1))] = @{ class_type='SaveImage'; inputs=@{ images=@($last,0); filename_prefix=("pj-" + $Label) } }

$body = @{ prompt = $prompt } | ConvertTo-Json -Depth 12
$resp = Invoke-RestMethod -Uri "$Server/prompt" -Method Post -Body $body -ContentType 'application/json' -TimeoutSec 30
$pid2 = $resp.prompt_id
Write-Host ("已提交: prompt_id={0}" -f $pid2)

# 3) 轮询直到完成
$sw = [Diagnostics.Stopwatch]::StartNew()
$images = @()
while($sw.Elapsed.TotalSeconds -lt 300){
  Start-Sleep -Milliseconds 800
  try {
    $h = Invoke-RestMethod -Uri "$Server/history/$pid2" -TimeoutSec 15
    if($h.PSObject.Properties.Name -contains $pid2){
      $outs = $h.$pid2.outputs
      foreach($k in $outs.PSObject.Properties.Name){
        foreach($im in $outs.$k.images){ $images += $im }
      }
      break
    }
  } catch { }
}
if($images.Count -eq 0){ throw "放大失败或超时" }

# 4) 取回结果
if(-not (Test-Path $OutDir)){ New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }
$n = 0
$saved = @()
foreach($im in $images){
  if ([string]::IsNullOrEmpty($im.subfolder)) { $src = Join-Path $comfyOutput $im.filename } else { $src = Join-Path (Join-Path $comfyOutput $im.subfolder) $im.filename }
  $n++
  $dst = Join-Path $OutDir ("{0}-{1}.png" -f $Label, $n)
  Copy-Item -LiteralPath $src -Destination $dst -Force
  $saved += $dst
  Write-Host ("结果 -> {0}" -f $dst)
}
Write-Host ("完成，用时 {0:N1}s" -f $sw.Elapsed.TotalSeconds)
return $saved
