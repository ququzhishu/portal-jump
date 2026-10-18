# =====================================================================
#  折返跃迁 —— 素材生成脚本（调用本机 ComfyUI 0.37.0 API）
#
#  用法：
#    powershell -File comfy-batch.ps1 -JobsFile jobs.json -OutDir ..\candidates
#
#  说明：
#    - ComfyUI 需已启动（C:\ComfyUI\启动.bat，默认 http://127.0.0.1:8188）
#    - jobs.json 是数组，每项一个生成任务：
#        { name, prompt, negative, ckpt, lora, loraStrength, width, height, steps, cfg, seed }
#    - 结果从 ComfyUI 的 output 目录拷到 OutDir，并按任务名重命名
#    - 会打印每个任务的耗时，便于评估显存/速度
# =====================================================================
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$JobsFile,
    [string]$OutDir = 'C:\工作区\res-switch\assets\candidates',
    [string]$Base = 'http://127.0.0.1:8188',
    [string]$ComfyOutput = 'C:\ComfyUI\ComfyUI\output',
    [int]$TimeoutSec = 900
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

function Post-Json($uri, $obj) {
    $json = $obj | ConvertTo-Json -Depth 12 -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    return Invoke-RestMethod -Uri $uri -Method Post -Body $bytes -ContentType 'application/json; charset=utf-8' -TimeoutSec 60
}

# ---------- 组装 API 格式工作流 ----------
function Build-Workflow($j) {
    $ckpt  = if ($j.ckpt) { $j.ckpt } else { 'WAI-Branch-Rouwei_Epred1.safetensors' }
    $steps = if ($j.steps) { [int]$j.steps } else { 30 }
    $cfg   = if ($j.cfg) { [double]$j.cfg } else { 6.0 }
    $w     = if ($j.width) { [int]$j.width } else { 1024 }
    $h     = if ($j.height) { [int]$j.height } else { 1024 }
    $seed  = if ($j.seed) { [int]$j.seed } else { 12345 }
    $loraS = if ($j.loraStrength) { [double]$j.loraStrength } else { 0.7 }

    $wf = [ordered]@{}
    $wf['1'] = @{ class_type='CheckpointLoaderSimple'; inputs=@{ ckpt_name=$ckpt } }

    $clipSrc = @('1',1); $modelSrc = @('1',0)

    # 支持多个 LoRA 串联（例如：佐伊角色 LoRA + 像素风 LoRA）
    # 节点号从 20 起，避免和下面的 3~8 冲突
    $loraList = @()
    if ($j.loras) { foreach ($l in $j.loras) { $loraList += @{ name=$l.name; strength=$l.strength } } }
    elseif ($j.lora) { $loraList += @{ name=$j.lora; strength=$loraS } }
    $loraNode = 20
    foreach ($l in $loraList) {
        $id = "$loraNode"
        $wf[$id] = @{ class_type='LoraLoader'; inputs=@{
            model=$modelSrc; clip=$clipSrc; lora_name=$l.name
            strength_model=[double]$l.strength; strength_clip=[double]$l.strength } }
        $modelSrc = @($id,0); $clipSrc = @($id,1)
        $loraNode++
    }

    $wf['3'] = @{ class_type='CLIPTextEncode'; inputs=@{ text=$j.prompt;   clip=$clipSrc } }
    $wf['4'] = @{ class_type='CLIPTextEncode'; inputs=@{ text=$j.negative; clip=$clipSrc } }
    $wf['5'] = @{ class_type='EmptyLatentImage'; inputs=@{ width=$w; height=$h; batch_size=1 } }
    $wf['6'] = @{ class_type='KSampler'; inputs=@{
        model=$modelSrc; seed=$seed; steps=$steps; cfg=$cfg
        sampler_name='dpmpp_2m'; scheduler='karras'
        positive=@('3',0); negative=@('4',0); latent_image=@('5',0); denoise=1.0 } }
    $wf['7'] = @{ class_type='VAEDecode'; inputs=@{ samples=@('6',0); vae=@('1',2) } }
    $wf['8'] = @{ class_type='SaveImage'; inputs=@{ images=@('7',0); filename_prefix=$j.name } }
    return $wf
}

# ---------- 提交 ----------
$jobs = Get-Content -LiteralPath $JobsFile -Raw -Encoding UTF8 | ConvertFrom-Json
$submitted = @()
foreach ($j in $jobs) {
    $wf = Build-Workflow $j
    try {
        $r = Post-Json "$Base/prompt" @{ prompt = $wf; client_id = 'portaljump-assetgen' }
        $submitted += [pscustomobject]@{ name=$j.name; id=$r.prompt_id; t0=(Get-Date) }
        Write-Host ("已提交 {0,-18} prompt_id={1}" -f $j.name, $r.prompt_id) -ForegroundColor Cyan
    } catch {
        Write-Host ("提交失败 {0}: {1}" -f $j.name, $_.Exception.Message) -ForegroundColor Red
    }
}

# ---------- 轮询并取图 ----------
# 注意：不要用 "$submitted = $submitted | Where-Object {...}" 这种边遍历边收敛的写法 ——
# PowerShell 在只剩一项时会把数组降级成标量，循环条件可能提前为假（已踩过：6 张只取回 5 张）。
$done = 0
$finished = @{}
$deadline = (Get-Date).AddSeconds($TimeoutSec)
while ((Get-Date) -lt $deadline) {
    $pending = @($submitted | Where-Object { -not $finished.ContainsKey($_.id) })
    if ($pending.Count -eq 0) { break }
    Start-Sleep -Milliseconds 1500
    foreach ($s in $pending) {
        try { $hist = Invoke-RestMethod -Uri "$Base/history/$($s.id)" -TimeoutSec 30 } catch { continue }
        $entry = $hist.$($s.id)
        if (-not $entry) { continue }
        if ($entry.status.status_str -eq 'error') {
            Write-Host ("{0} 生成失败: {1}" -f $s.name, ($entry.status.messages | Out-String)) -ForegroundColor Red
            $finished[$s.id] = $true
            continue
        }
        $img = $null
        if ($entry.outputs) {
            foreach ($n in $entry.outputs.PSObject.Properties) {
                if ($n.Value.images -and $n.Value.images.Count -gt 0) { $img = $n.Value.images[0]; break }
            }
        }
        if (-not $img) { continue }
        $src = Join-Path $ComfyOutput $img.filename
        if (-not (Test-Path $src)) { continue }
        $safe = ($s.name -replace '[^\w\-\.]','_')
        $dst = Join-Path $OutDir ("{0}.png" -f $safe)
        Copy-Item -LiteralPath $src -Destination $dst -Force
        $secs = [math]::Round(((Get-Date) - $s.t0).TotalSeconds, 1)
        Write-Host ("完成 {0,-18} {1,6}s -> {2}" -f $s.name, $secs, (Split-Path $dst -Leaf)) -ForegroundColor Green
        $done++
        $finished[$s.id] = $true
    }
}
Write-Host ("`n共完成 {0} / {1} 张，输出目录: {2}" -f $done, $jobs.Count, $OutDir)
