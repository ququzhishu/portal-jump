using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace PortalJump
{
    /// <summary>
    /// 核心切换逻辑：分辨率走 Win32，缩放走"当前可用的那条通路"。
    ///
    /// v0.11 的关键变化：
    /// 1. **AMD / Intel 也能用了**：缩放不再强制走 NVAPI。
    ///    通用路径用 Windows 原生 DEVMODE.dmDisplayFixedOutput；
    ///    NVIDIA 驱动只认 NVAPI，程序会自动探测并切换（见 UsesNvapiScaling）。
    /// 2. **NVAPI 完全隔离在 NvapiBridge 里**：主代码不引用任何 NVAPI 类型，
    ///    所以 NvAPIWrapper.dll 缺失时（通用版 / AMD 用户）只是降级，不会崩溃。
    /// 3. **切换可靠性重做**（修"三角洲有时切不过去"）：
    ///    - 每次切换后**回读校验**，不再只看 ChangeDisplaySettingsEx 的返回码；
    ///    - 驱动忙时**退避重试**，而不是死等固定时长；
    ///    - 分辨率没切成功时不再继续设缩放，也不谎报成功；
    ///    - 缩放失败不把整体判失败（否则托盘会反复重试，越重试越糟）。
    /// </summary>
    public static class Switcher
    {
        static readonly object Lock = new object();

        // ---- NVAPI 状态（经 NvapiBridge，本类不接触 NVAPI 类型）----
        static bool _nvapiReady;
        static string _nvapiError = "";
        static string _gpuInfo = "";
        static string _driverInfo = "";

        // ---- 本机显卡与后端选择 ----
        static GpuVendor _vendor = GpuVendor.Unknown;
        static string _vendorText = "";
        static string _adapterNames = "";

        /// <summary>切完分辨率后等驱动稳定的基础时长（毫秒）</summary>
        const int SettleMilliseconds = 700;

        /// <summary>回读确认的最长等待（毫秒）。驱动忙的时候宁可多等，也别谎报成功</summary>
        const int ConfirmTimeoutMilliseconds = 4000;

        /// <summary>模式切换重试次数</summary>
        const int ApplyRetryCount = 3;

        static string LogPath
        {
            get { return AppPaths.LogPath; }
        }

        public static void Log(string m)
        {
            try
            {
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + m + Environment.NewLine);
            }
            catch { }
        }

        public static bool NvapiReady { get { return _nvapiReady; } }
        public static string NvapiError { get { return _nvapiError; } }
        public static string GpuInfo { get { return _gpuInfo; } }
        public static string DriverInfo { get { return _driverInfo; } }
        public static GpuVendor Vendor { get { return _vendor; } }

        /// <summary>给界面用的一行环境摘要</summary>
        public static string EnvironmentText
        {
            get
            {
                string s = _gpuInfo.Length > 0 ? _gpuInfo : (_adapterNames.Length > 0 ? _adapterNames : "未知显卡");
                if (_driverInfo.Length > 0) s += "  驱动 " + _driverInfo;
                return s;
            }
        }

        // ------------------------------------------------------------------
        // 后端选择：按"驱动真实能力"而不是"厂商名字"
        // ------------------------------------------------------------------

        static ScalingBackend _backendOverride = ScalingBackend.Auto;

        /// <summary>原生缩放字段（dmDisplayFixedOutput）在本机是否真的能写</summary>
        static bool _nativeScalingWritable = true;

        static bool _nativeScalingProbed;

        /// <summary>
        /// 是否走 NVAPI 设缩放。
        ///
        /// 实测结论（30 次实验，别凭直觉改）：
        ///  NVIDIA 驱动把 dmDisplayFixedOutput 变成**只读**——ChangeDisplaySettingsEx
        ///  返回 0、CDS_TEST 也返回 0，但字段恒为 0、画面不变。
        ///  反过来用 NVAPI 设缩放时该字段会跟着同步，说明它只是 NVAPI 的镜像。
        /// 所以 NVIDIA 上必须走 NVAPI；AMD / Intel 上没有 NVAPI，走原生字段即可。
        ///
        /// 但这里不是硬编码厂商：先看原生字段是否可写（ProbeNativeScalingWritable），
        /// 可写就用原生（统一路径），不可写才用 NVAPI。
        /// 将来某个 NVIDIA 驱动修好了原生字段，程序会自动用回原生。
        /// </summary>
        static bool UsesNvapiScaling
        {
            get
            {
                if (_backendOverride == ScalingBackend.Win32) return false;
                if (_backendOverride == ScalingBackend.Nvapi) return _nvapiReady;
                if (!_nvapiReady) return false;
                return !_nativeScalingWritable;
            }
        }

        /// <summary>当前缩放后端</summary>
        public static ScalingBackend Backend
        {
            get
            {
                if (_backendOverride != ScalingBackend.Auto) return _backendOverride;
                return UsesNvapiScaling ? ScalingBackend.Nvapi : ScalingBackend.Win32;
            }
        }

        /// <summary>当前用哪条缩放通路（给界面/自检看）</summary>
        public static string ScalingBackendText
        {
            get
            {
                if (_backendOverride == ScalingBackend.Nvapi)
                    return "NVIDIA NVAPI（已手动强制指定）";
                if (_backendOverride == ScalingBackend.Win32)
                    return "Windows 原生接口（已手动强制指定）";

                if (UsesNvapiScaling)
                    return "NVIDIA NVAPI（该驱动不采纳 Windows 原生缩放字段）";
                if (_vendor == GpuVendor.Nvidia && !_nvapiReady)
                    return "Windows 原生接口（NVAPI 不可用，缩放可能无效）";
                return "Windows 原生接口（DEVMODE.dmDisplayFixedOutput，兼容所有显卡）";
            }
        }

        /// <summary>
        /// 探测"Windows 原生缩放字段能不能写"。
        ///
        /// 为什么不能只靠 CCD 值判断：CCD 的缩放值本身是被 NVAPI 影响的，
        /// 用"CCD 是不是 IDENTITY"来推断"原生字段能不能写"会形成循环依赖 ——
        /// 实测就出现过：NVAPI 把 CCD 设成 1 之后，程序误判"原生可写"，
        /// 于是放着能用的 NVAPI 不用、去走必然失败的原生路径。
        ///
        /// 正确做法：**只按厂商判断**。
        ///   · NVIDIA → 直接用 NVAPI（实测原生字段 30/30 失败，见 NvapiBridge 注释）
        ///   · 其它   → 直接用原生（它们没有 NVAPI）
        /// 保留探测入口是为了将来 NVIDIA 驱动若修好原生字段可以改这里，
        /// 但默认不再用 CCD 做推断。
        /// </summary>
        static void ProbeNativeScalingWritable()
        {
            if (_nativeScalingProbed) return;
            _nativeScalingProbed = true;

            try
            {
                if (_vendor == GpuVendor.Nvidia)
                {
                    // NVIDIA：原生字段不可写，必须 NVAPI。
                    // 但若 NVAPI 不可用（通用版/缺 DLL），只能退回原生碰运气。
                    _nativeScalingWritable = !_nvapiReady;
                    Log("原生缩放可写性判定：NVIDIA 显卡 + NVAPI "
                        + (_nvapiReady ? "可用 → 走 NVAPI" : "不可用 → 退回原生（可能无效）"));
                }
                else
                {
                    // AMD / Intel / 未知：没有 NVAPI，原生字段是唯一选择，且它们正常支持。
                    _nativeScalingWritable = true;
                    Log("原生缩放可写性判定：" + _vendorText + " → 走 Windows 原生接口");
                }
            }
            catch (Exception e)
            {
                Log("原生缩放可写性判定异常（按可写处理）：" + e.Message);
                _nativeScalingWritable = true;
            }
        }

        /// <summary>
        /// 初始化环境。NVAPI 失败**不是错误**：只是改用通用后端。
        /// 显卡厂商由 Win32 枚举适配器判断，不依赖 NVAPI。
        /// </summary>
        public static void TryInitNvapi()
        {
            // 0) 环境变量可强制指定后端（排错用）
            try
            {
                string forced = Environment.GetEnvironmentVariable("PORTALJUMP_SCALING_BACKEND");
                if (!string.IsNullOrEmpty(forced))
                {
                    forced = forced.Trim().ToLowerInvariant();
                    if (forced == "win32" || forced == "generic")
                    {
                        _backendOverride = ScalingBackend.Win32;
                        Log("环境变量强制缩放后端 = Windows 原生接口（排错模式）");
                    }
                    else if (forced == "nvapi")
                    {
                        _backendOverride = ScalingBackend.Nvapi;
                        Log("环境变量强制缩放后端 = NVAPI（排错模式）");
                    }
                }
            }
            catch { }

            // 1) 先判断厂商（这条永远可用，不需要 NVAPI）
            try
            {
                _vendor = DisplayConfigBackend.DetectVendor();
                List<string> names = DisplayConfigBackend.GetAdapterNames();
                _adapterNames = string.Join(" + ", names.ToArray());
                switch (_vendor)
                {
                    case GpuVendor.Nvidia: _vendorText = "NVIDIA"; break;
                    case GpuVendor.Amd: _vendorText = "AMD"; break;
                    case GpuVendor.Intel: _vendorText = "Intel"; break;
                    default: _vendorText = "未知厂商"; break;
                }
                Log("显卡厂商识别：" + _vendorText + "（" + _adapterNames + "）");
            }
            catch (Exception e)
            {
                Log("枚举显示适配器失败: " + e.Message);
            }

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

            // 3) NVAPI 成功但没拿到名字时，用适配器名兜底
            if (_gpuInfo.Length == 0) _gpuInfo = _adapterNames;

            // 4) 探测原生缩放字段是否可写，据此决定走哪条路
            ProbeNativeScalingWritable();
        }

        // ------------------------------------------------------------------
        // 应用预设
        // ------------------------------------------------------------------

        /// <summary>应用预设。返回是否成功，message 为给用户看的结果说明。</summary>
        public static bool Apply(Preset p, out string message)
        {
            lock (Lock)
            {
                if (p == null)
                {
                    message = "预设为空";
                    return false;
                }

                Log("Apply 开始: " + p.Key + " " + p.Width + "x" + p.Height
                    + " scaling=" + ScalingNames.ToIniValue(p.ScalingSpec) + "（" + p.ScalingText + "）");

                try
                {
                    if (!p.IsUsable)
                    {
                        message = "预设分辨率不合理：" + p.ResolutionText;
                        return false;
                    }

                    // ---------- 1) 切分辨率（原生后端会把缩放并进同一次调用）----------
                    ScalingSpec spec = p.ScalingSpec;
                    string detail;
                    string actual;
                    int appliedHz;
                    string modeMessage;

                    bool modeOk = ApplyResolution(p, spec, out appliedHz, out modeMessage, out detail, out actual);
                    if (!modeOk)
                    {
                        message = modeMessage;
                        Log("失败: " + message);
                        return false;
                    }

                    // ---------- 2) 缩放 ----------
                    if (spec.IsUnchanged)
                    {
                        message = string.Format("已切换：{0}x{1}@{2}Hz，缩放保持当前设置（未改动）",
                            p.Width, p.Height, appliedHz);
                        Log("成功(不改缩放): " + message);
                        return true;
                    }

                    bool scalingOk = true;
                    if (!actual.StartsWith("ok:", StringComparison.Ordinal))
                    {
                        // 原生后端在"已是目标分辨率"等情况下没法把缩放并进模式切换，单独补一次
                        scalingOk = TryApplyScaling(spec, out detail, out actual);
                    }
                    else
                    {
                        actual = actual.Substring(3);
                        Log("缩放已随模式切换一并设置完成");
                    }

                    if (!scalingOk)
                    {
                        // 分辨率已经切好了，缩放失败不能算整体失败。
                        // 旧版这里 return false，托盘会认为失败并反复重试，
                        // 反而把"偶尔失败"放大成"一直失败"。
                        message = string.Format("分辨率已切换为 {0}x{1}@{2}Hz，但缩放没设成功：{3}",
                            p.Width, p.Height, appliedHz, detail);
                        Log("缩放失败（分辨率已成功，不算整体失败）: " + detail);
                        return true;
                    }

                    if (actual.Length > 0)
                    {
                        message = string.Format("已切换：{0}x{1}@{2}Hz，缩放={3}（驱动实际采用 {4}）",
                            p.Width, p.Height, appliedHz, p.ScalingText, actual);
                    }
                    else
                    {
                        message = string.Format("已切换：{0}x{1}@{2}Hz，缩放={3}",
                            p.Width, p.Height, appliedHz, p.ScalingText);
                    }
                    Log("成功: " + message);
                    return true;
                }
                catch (Exception e)
                {
                    Log("Apply 异常: " + e.GetType().FullName + " " + e.Message);
                    Log(e.StackTrace);
                    message = "切换失败：" + e.Message;
                    return false;
                }
            }
        }

        /// <summary>
        /// 切分辨率（原生后端会把缩放并进同一次调用），以**回读**为准。
        ///
        /// 为什么缩放要并进同一次调用：
        /// Windows 只在"源尺寸 ≠ 目标尺寸"时才有缩放概念。先把分辨率切到原生，
        /// 再单独设 dmDisplayFixedOutput，驱动会认为无需缩放而拒绝。
        /// 所以必须在"切到非原生分辨率"的那一次 ChangeDisplaySettingsEx 里一起设好。
        ///
        /// actual 以 "ok:" 开头表示缩放已由本次调用完成，调用方不必再设。
        /// </summary>
        static bool ApplyResolution(Preset p, ScalingSpec spec, out int appliedHz,
            out string message, out string detail, out string actual)
        {
            appliedHz = 0;
            message = "";
            detail = "";
            actual = "";

            List<DisplayConfigBackend.DEVMODE> modes = DisplayConfigBackend.FindModes(p.Width, p.Height);
            Log("FindModes 返回 " + modes.Count + " 个模式");
            if (modes.Count == 0)
            {
                message = "当前显示器不支持 " + p.ResolutionText + "。"
                    + "若这是自定义分辨率，请先在显卡控制面板里创建它。";
                return false;
            }

            // 只有原生后端需要把缩放并进模式切换（NVAPI 能独立设缩放）
            bool foldScaling = !UsesNvapiScaling && !spec.IsUnchanged
                && spec.Mode != ScalingMode.Integer;

            // 已经是目标分辨率：无法靠"切模式"带出缩放，交给后面的单独设置
            if (DisplayInfo.IsCurrent(p.Width, p.Height))
            {
                appliedHz = DisplayInfo.GetCurrentMode().dmDisplayFrequency;
                Log("已是目标分辨率 @" + appliedHz + "Hz，跳过模式切换");
                return true;
            }

            string lastReason = "";
            foreach (DisplayConfigBackend.DEVMODE m in modes)
            {
                DisplayConfigBackend.DEVMODE candidate = m;
                for (int attempt = 1; attempt <= ApplyRetryCount; attempt++)
                {
                    ScalingMode foldMode = foldScaling ? spec.Mode : ScalingMode.Unchanged;
                    int r = DisplayConfigBackend.ApplyMode(candidate, foldMode);
                    Log("尝试模式 @" + candidate.dmDisplayFrequency + "Hz 第" + attempt + "次 r=" + r
                        + " (" + DisplayConfigBackend.DescribeResult(r) + ")"
                        + (foldScaling ? " 含缩放=" + ScalingNames.ToIniValue(spec) : ""));

                    if (r != 0)
                    {
                        lastReason = DisplayConfigBackend.DescribeResult(r);
                        // 驱动忙 / 一般性错误：等一会儿重试；明确不支持就别浪费次数
                        if (r == -1 || r == -3)
                        {
                            Thread.Sleep(300 * attempt);
                            continue;
                        }
                        break;
                    }

                    // 返回 0 也要回读确认 —— 这正是旧版漏掉的一步
                    if (WaitForMode(p.Width, p.Height, candidate.dmDisplayFrequency, ConfirmTimeoutMilliseconds))
                    {
                        appliedHz = candidate.dmDisplayFrequency;

                        if (foldScaling)
                        {
                            // 分辨率回读成功 ≠ 缩放已落地：驱动常先切模式、过一会儿才写缩放。
                            int want = ExpectedFixedOutput(spec.Mode);
                            int got = WaitForFixedOutput(want, 2500);
                            if (got == want)
                            {
                                actual = "ok:";
                                Log("缩放随模式切换一并生效：" + DisplayConfigBackend.FixedOutputName(got));
                            }
                            else
                            {
                                // 没落上：重试整个"切模式+缩放"，而不是只打日志放过。
                                Log("缩放未随模式切换生效（回读 " + got + "），重试整个切换");
                                lastReason = "缩放未生效";
                                Thread.Sleep(350 * attempt);
                                continue;
                            }
                        }
                        return true;
                    }

                    Log("返回成功但回读不符，重试（第" + attempt + "次）");
                    lastReason = "驱动未真正生效";
                    Thread.Sleep(400 * attempt);
                }
            }

            message = "切换分辨率失败：" + p.ResolutionText + "（" + lastReason + "）";
            return false;
        }

        /// <summary>
        /// 轮询等待 dmDisplayFixedOutput 变成期望值，返回最终读到的值。
        /// 驱动把"切模式"和"写缩放"分两步做，中间有延迟，所以必须轮询。
        /// </summary>
        static int WaitForFixedOutput(int want, int timeoutMs)
        {
            int got = -1;
            int waited = 0;
            while (waited < timeoutMs)
            {
                got = DisplayConfigBackend.GetCurrentFixedOutput();
                if (got == want) return got;
                Thread.Sleep(100);
                waited += 100;
            }
            return got;
        }

        /// <summary>轮询回读，直到分辨率/刷新率真的变成目标值</summary>
        static bool WaitForMode(int w, int h, int hz, int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                DisplayConfigBackend.DEVMODE cur = DisplayConfigBackend.GetCurrentMode();
                if (cur.dmPelsWidth == w && cur.dmPelsHeight == h && cur.dmDisplayFrequency == hz)
                {
                    return true;
                }
                Thread.Sleep(120);
                waited += 120;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // 缩放
        // ------------------------------------------------------------------

        /// <summary>判断某分辨率是否是显示器原生（最高）分辨率</summary>
        static bool IsNativeResolution(int w, int h)
        {
            try
            {
                long area = (long)w * h;
                long maxArea = 0;
                foreach (DisplayConfigBackend.DEVMODE dm in DisplayConfigBackend.EnumAllModes())
                {
                    long a = (long)dm.dmPelsWidth * dm.dmPelsHeight;
                    if (a > maxArea) maxArea = a;
                }
                return area >= maxArea;
            }
            catch { return false; }
        }

        /// <summary>
        /// 挑一个"非原生"分辨率用于缩放自检。
        /// 当前处于原生分辨率时，缩放没有物理意义，拿它做 CDS_TEST 会得到误导性的"拒绝"。
        /// </summary>
        static DisplayConfigBackend.DEVMODE PickNonNativeMode(DisplayConfigBackend.DEVMODE cur)
        {
            var candidates = new int[][] {
                new int[] { 2560, 1440 }, new int[] { 1920, 1080 }, new int[] { 1600, 900 }
            };
            foreach (int[] wh in candidates)
            {
                if (wh[0] == cur.dmPelsWidth && wh[1] == cur.dmPelsHeight) continue;
                List<DisplayConfigBackend.DEVMODE> list = DisplayConfigBackend.FindModes(wh[0], wh[1]);
                if (list.Count > 0) return list[0];
            }

            long target = (long)cur.dmPelsWidth * cur.dmPelsHeight * 3 / 4;
            DisplayConfigBackend.DEVMODE best = DisplayConfigBackend.NewDevMode();
            long bestDiff = long.MaxValue;
            foreach (DisplayConfigBackend.DEVMODE dm in DisplayConfigBackend.EnumAllModes())
            {
                if (dm.dmPelsWidth == cur.dmPelsWidth && dm.dmPelsHeight == cur.dmPelsHeight) continue;
                if (dm.dmBitsPerPel != 32) continue;
                long diff = Math.Abs((long)dm.dmPelsWidth * dm.dmPelsHeight - target);
                if (diff < bestDiff) { bestDiff = diff; best = dm; }
            }
            return best;
        }

        /// <summary>当前主显示器的缩放描述（用于界面显示）</summary>
        public static string GetCurrentScalingText()
        {
            try
            {
                if (UsesNvapiScaling)
                {
                    string nv = NvapiBridge.CurrentScalingText();
                    if (nv.Length > 0) return nv;
                }
                int fo = DisplayConfigBackend.GetCurrentFixedOutput();
                string s = DisplayConfigBackend.FixedOutputName(fo);
                int ccd = DisplayConfigBackend.QueryCcdScaling();
                if (ccd >= 0) s += "（CCD=" + ccd + "）";
                return s;
            }
            catch (Exception e)
            {
                return "读取失败：" + e.Message;
            }
        }

        /// <summary>
        /// 设置缩放。返回是否成功；detail 失败原因；actual 非空表示驱动实际采用了别的值。
        ///
        /// 统一策略（按能力走，不按厂商硬编码）：
        ///   1) 整数缩放 → 只有 NVAPI 能表达，直接走 NVAPI。
        ///   2) 其它 → 先走 Windows 原生字段（所有显卡通用）。
        ///   3) 原生回读确认没生效 → 有 NVAPI 就自动改用 NVAPI 重试一次。
        /// </summary>
        static bool TryApplyScaling(ScalingSpec spec, out string detail, out string actual)
        {
            detail = "";
            actual = "";

            // 整数缩放：原生字段表达不了（只有 拉伸/居中 两档），必须走 NVAPI
            if (spec.Mode == ScalingMode.Integer && _nvapiReady && _backendOverride != ScalingBackend.Win32)
            {
                bool nvOk = TryApplyScalingNvapi(spec, out detail, out actual);
                if (nvOk) return true;
                detail = "整数缩放需要 NVIDIA 显卡配合（本机为 " + _vendorText + "）：" + detail;
                return false;
            }

            if (UsesNvapiScaling)
            {
                return TryApplyScalingNvapi(spec, out detail, out actual);
            }

            // 先走原生
            string gDetail, gActual;
            bool ok = TryApplyScalingGeneric(spec, out gDetail, out gActual);

            // "返回成功但回读没生效"也算失败 —— 这正是 NVIDIA 驱动的情况
            bool reallyApplied = ok && gActual.Length == 0;
            if (reallyApplied)
            {
                detail = gDetail;
                actual = gActual;
                return true;
            }

            // 原生不行：有 NVAPI 就换 NVAPI 再试一次
            if (_nvapiReady && _backendOverride == ScalingBackend.Auto)
            {
                Log("原生缩放未生效（" + (ok ? "回读=" + gActual : gDetail) + "），改用 NVAPI 重试");
                string nvDetail, nvActual;
                bool nvOk = TryApplyScalingNvapi(spec, out nvDetail, out nvActual);
                if (nvOk)
                {
                    // 记住这个结论，后续直接走 NVAPI，不再每次都先失败一次
                    _nativeScalingWritable = false;
                    detail = nvDetail;
                    actual = nvActual;
                    return true;
                }
                detail = gDetail + "；NVAPI 也失败：" + nvDetail;
                actual = gActual;
                return ok;
            }

            detail = gDetail;
            actual = gActual;
            return ok;
        }

        /// <summary>语义 → NV_SCALING 数值</summary>
        static bool TryMapToNvScaling(ScalingSpec spec, out int nv)
        {
            nv = 0;
            bool gpu = spec.Actor != ScaleActor.Display;   // Auto 在 NVIDIA 上按 GPU 处理
            switch (spec.Mode)
            {
                case ScalingMode.Unchanged: nv = 0; return true;
                case ScalingMode.FullScreen: nv = gpu ? 2 : 1; return true;   // 2=Force GPU，1=Balanced
                case ScalingMode.Aspect: nv = gpu ? 5 : 6; return true;
                case ScalingMode.Centered: nv = gpu ? 3 : 7; return true;
                case ScalingMode.Integer: nv = 8; return true;
                default: return false;
            }
        }

        static bool TryApplyScalingNvapi(ScalingSpec spec, out string detail, out string actual)
        {
            detail = "";
            actual = "";
            if (!_nvapiReady)
            {
                detail = "NVAPI 不可用";
                return false;
            }

            int scaling;
            if (!TryMapToNvScaling(spec, out scaling))
            {
                detail = "该缩放方式在 NVAPI 下没有对应取值";
                return false;
            }

            Thread.Sleep(SettleMilliseconds);
            return NvapiBridge.ApplyScaling(scaling, out detail, out actual);
        }

        // ---------- 通用后端（AMD / Intel / 任何显卡） ----------

        static bool TryApplyScalingGeneric(ScalingSpec spec, out string detail, out string actual)
        {
            detail = "";
            actual = "";

            ScalingMode mode = spec.Mode;
            if (mode == ScalingMode.Integer)
            {
                detail = "整数缩放需要 NVIDIA 显卡（本机为 " + _vendorText + "）";
                return false;
            }

            Thread.Sleep(SettleMilliseconds);

            DisplayConfigBackend.DEVMODE cur = DisplayConfigBackend.GetCurrentMode();
            if (cur.dmPelsWidth <= 0)
            {
                detail = "读不到当前显示模式";
                return false;
            }

            // 前置：把 CCD 侧缩放"让位"成 IDENTITY。
            // 实测 dmDisplayFixedOutput 只在 CCD 缩放为 IDENTITY(1)/PREFERRED(128) 时被采纳。
            int ccdBefore = DisplayConfigBackend.QueryCcdScaling();
            if (!DisplayConfigBackend.CcdScalingAllowsFixedOutput(ccdBefore))
            {
                Log("CCD 缩放=" + ccdBefore + " 会压制 DMDFO，先让位成 IDENTITY");
                DisplayConfigBackend.TrySetCcdScaling(1);
                Thread.Sleep(400);
            }

            int test = DisplayConfigBackend.TestMode(cur, mode);
            if (test != 0)
            {
                detail = "驱动预检拒绝（" + DisplayConfigBackend.DescribeResult(test) + "）";
                return false;
            }

            // 设置 + 回读，失败重试（驱动刚切完模式时常处于忙态）
            int want = ExpectedFixedOutput(mode);
            int got = -1;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                int r2 = DisplayConfigBackend.ApplyScalingOnly(mode);
                if (r2 != 0)
                {
                    detail = "驱动拒绝（" + DisplayConfigBackend.DescribeResult(r2) + "）";
                    Log("单独设缩放第 " + attempt + " 次失败：" + detail);
                    Thread.Sleep(250 * attempt);
                    continue;
                }
                got = WaitForFixedOutput(want, 1200);
                if (got == want) return true;
                Log("单独设缩放第 " + attempt + " 次回读 " + got + "（期望 " + want + "），重试");
                Thread.Sleep(250 * attempt);
            }

            // 仍然没落上：不判整体失败，但如实报告实际值
            actual = DisplayConfigBackend.FixedOutputName(got);
            Log("通用缩放回读不一致：请求 " + want + "，实际 " + got);
            return true;
        }

        static int ExpectedFixedOutput(ScalingMode mode)
        {
            switch (mode)
            {
                case ScalingMode.FullScreen:
                case ScalingMode.Integer:
                    return DisplayConfigBackend.DMDFO_STRETCH;
                case ScalingMode.Aspect:
                case ScalingMode.Centered:
                    return DisplayConfigBackend.DMDFO_CENTER;
                default:
                    return DisplayConfigBackend.DMDFO_DEFAULT;
            }
        }

        // ------------------------------------------------------------------
        // 环境自检
        // ------------------------------------------------------------------

        /// <summary>
        /// 生成环境自检报告。缩放部分只用 CDS_TEST / ValidateOnly，不会真的切换画面。
        /// </summary>
        public static string SelfTest()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(AppInfo.Title + " 环境自检");
            sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("版本：" + AppInfo.Version + "（" + GpuExtensions.EditionName + "）");
            sb.AppendLine();

            sb.AppendLine("[显卡]");
            sb.AppendLine("  厂商识别：" + _vendorText);
            sb.AppendLine("  显示适配器：" + (_adapterNames.Length > 0 ? _adapterNames : "(未识别)"));
            if (_nvapiReady)
            {
                sb.AppendLine("  NVAPI：可用（" + _gpuInfo + "）");
                if (_driverInfo.Length > 0) sb.AppendLine("  驱动：" + _driverInfo);
            }
            else
            {
                sb.AppendLine("  NVAPI：不可用 —— " + _nvapiError);
                sb.AppendLine("  说明：这不是错误。非 NVIDIA 显卡本来就没有 NVAPI，");
                sb.AppendLine("        缩放会自动改走 Windows 原生接口，功能不受影响。");
            }
            sb.AppendLine("  缩放通路：" + ScalingBackendText);
            sb.AppendLine();

            sb.AppendLine("[显示器]");
            try
            {
                DisplayConfigBackend.DEVMODE cur = DisplayConfigBackend.GetCurrentMode();
                sb.AppendLine("  当前模式：" + cur.dmPelsWidth + "x" + cur.dmPelsHeight + "@"
                    + cur.dmDisplayFrequency + "Hz " + cur.dmBitsPerPel + "位");
                sb.AppendLine("  当前缩放（dmDisplayFixedOutput）："
                    + DisplayConfigBackend.FixedOutputName(cur.dmDisplayFixedOutput));
                int ccd = DisplayConfigBackend.QueryCcdScaling();
                sb.AppendLine("  当前缩放（CCD scaling 值）：" + (ccd >= 0 ? ccd.ToString() : "(读取失败)"));
                List<DisplayMode> modes = DisplayInfo.GetResolutions();
                sb.AppendLine("  支持的分辨率数量：" + modes.Count);
                var sample = new List<string>();
                for (int i = 0; i < modes.Count && i < 8; i++) sample.Add(modes[i].Width + "x" + modes[i].Height);
                sb.AppendLine("  分辨率示例：" + string.Join("、", sample.ToArray()));
            }
            catch (Exception e)
            {
                sb.AppendLine("  读取失败：" + e.Message);
            }
            sb.AppendLine();

            sb.AppendLine("[缩放自检]（CDS_TEST 校验，不会切换画面）");
            try
            {
                DisplayConfigBackend.DEVMODE cur = DisplayConfigBackend.GetCurrentMode();
                if (cur.dmPelsWidth <= 0)
                {
                    sb.AppendLine("  读不到当前模式，无法自检。");
                    return sb.ToString();
                }

                // 必须拿非原生分辨率来试：原生分辨率下"缩放"没有物理意义，
                // 驱动会拒绝，那会让人误以为缩放不可用。
                DisplayConfigBackend.DEVMODE probe = PickNonNativeMode(cur);
                if (probe.dmPelsWidth <= 0)
                {
                    sb.AppendLine("  当前只有原生分辨率可用，无法演示缩放（这是正常的：");
                    sb.AppendLine("  源与目标尺寸相同时系统会自动把缩放强制为 1:1）。");
                }
                else
                {
                    sb.AppendLine("  用非原生分辨率 " + probe.dmPelsWidth + "x" + probe.dmPelsHeight
                        + "@" + probe.dmDisplayFrequency + "Hz 做校验（只测试，不切画面）：");
                    foreach (ScalingMode m in new ScalingMode[] {
                        ScalingMode.FullScreen, ScalingMode.Aspect, ScalingMode.Centered })
                    {
                        int r = DisplayConfigBackend.TestMode(probe, m);
                        string verdict = r == 0
                            ? "驱动接受"
                            : "驱动拒绝（" + DisplayConfigBackend.DescribeResult(r) + "）";
                        sb.AppendLine("    " + ScalingNames.Describe(new ScalingSpec(m, ScaleActor.Auto))
                            + "：" + verdict);
                    }
                }
                sb.AppendLine("  整数缩放：" + (_nvapiReady
                    ? "NVIDIA 显卡，走 NVAPI"
                    : "需要 NVIDIA 显卡，本机不支持（不影响其它缩放方式）"));
            }
            catch (Exception e)
            {
                sb.AppendLine("  自检失败：" + e.GetType().Name + " " + e.Message);
            }

            if (_nvapiReady)
            {
                try
                {
                    sb.Append(NvapiBridge.SelfTestSection());
                }
                catch (Exception e)
                {
                    sb.AppendLine();
                    sb.AppendLine("[NVAPI 缩放自检] 失败：" + e.Message);
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>当前显示器/时序无法通过 NVAPI 设置缩放时抛出，消息可直接展示给用户</summary>
    public class ScalingNotSupportedException : Exception
    {
        public ScalingNotSupportedException(string message) : base(message)
        {
        }
    }
}
