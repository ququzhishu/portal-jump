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

        // ---- ADLX 状态（经 AdlxBridge，AMD 专用缩放通路）----
        // AMD 显卡的专用缩放通路，加载失败或枚举不到显示器时如实报告，不自动降级。
        static bool _adlxReady;
        static string _adlxError = "";

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
        public static bool AdlxReady { get { return _adlxReady; } }
        public static string AdlxError { get { return _adlxError; } }
        public static string GpuInfo { get { return _gpuInfo; } }
        public static string DriverInfo { get { return _driverInfo; } }
        public static GpuVendor Vendor { get { return _vendor; } }

        /// <summary>
        /// 当前是否可创建自定义分辨率：NVIDIA 走 NVAPI，AMD 走 ADLX（方案 B）。
        /// 这是运行时判断（取决于实际厂商与桥接是否就绪），供界面决定是否放行。
        /// </summary>
        public static bool CustomResolutionAvailable
        {
            get
            {
                if (_vendor == GpuVendor.Nvidia && _nvapiReady) return true;
                if (_vendor == GpuVendor.Amd && _adlxReady)
                {
                    try
                    {
                        bool supported;
                        string detail;
                        return AdlxBridge.CustomResolutionSupported(out supported, out detail) && supported;
                    }
                    catch { return false; }
                }
                return false;
            }
        }

        /// <summary>
        /// 自定义分辨率不可用时给用户看的原因（可用时返回空串）。
        /// 区分 NVIDIA / AMD / 其他厂商，避免把 AMD 误报成"需要 NVIDIA"。
        /// </summary>
        public static string CustomResolutionUnavailableReason()
        {
            if (_vendor == GpuVendor.Amd)
            {
                if (!_adlxReady)
                {
                    return "AMD 显卡创建自定义分辨率需要官方 ADLX 接口，但当前桥接不可用：\n"
                        + _adlxError
                        + "\n\n请确认：① 已完整安装 AMD Software 驱动；② 显示器直连显卡输出口"
                        + "（远程桌面 / 向日葵会话下 ADLX 枚举不到物理显示器）；③ 以管理员权限运行。";
                }
                return "该显示器当前不支持创建自定义分辨率（AMD 驱动报告）。\n\n"
                    + "复制 / Eyefinity 多屏模式，或某些连接类型下 AMD 不允许自定义分辨率，"
                    + "请改为单屏独立显示模式后再试。";
            }
            if (_vendor == GpuVendor.Intel)
            {
                return "本程序未在 Intel 显卡上验证过，因此不在程序内提供创建自定义分辨率。\n\n"
                    + "如需自定义分辨率，请在 Intel 显卡控制中心（或 Windows 显示设置）中操作。";
            }
            return "创建自定义分辨率需要显卡驱动专有接口（NVIDIA NVAPI / AMD ADLX）。\n\n"
                + "当前显卡未识别为 NVIDIA 或 AMD，或对应的驱动桥接没有就绪，因此该功能不可用。"
                + "切换分辨率和缩放方式不受影响。";
        }

        // ---- AMD（ADLX）自定义分辨率转发封装 ----
        public static bool AmdCustomResolutionSupported(out bool supported, out string detail)
        {
            return AdlxBridge.CustomResolutionSupported(out supported, out detail);
        }
        internal static System.Collections.Generic.List<AdlxBridge.AdlxCustomResolutionInfo> AmdListCustomResolutions()
        {
            return AdlxBridge.EnumerateCustomResolutions();
        }
        public static bool AmdCreateCustomResolution(int width, int height, out int refreshRate, out string detail)
        {
            bool ok = AdlxBridge.CreateCustomResolution(width, height, out refreshRate, out detail);
            if (ok) Log("ADLX 自定义分辨率已创建：" + width + "x" + height + "@" + refreshRate);
            else Log("ADLX 自定义分辨率创建失败：" + detail);
            return ok;
        }
        public static bool AmdDeleteCustomResolution(int width, int height, out string detail)
        {
            bool ok = AdlxBridge.DeleteCustomResolution(width, height, out detail);
            Log("ADLX 自定义分辨率删除 " + width + "x" + height + "：" + (ok ? "成功" : "失败 " + detail));
            return ok;
        }

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
                // 仅分辨率版：完全不碰缩放，如实说明
                if (GpuExtensions.ResolutionOnly)
                    return "不设置缩放（仅分辨率版）—— 缩放方式由显卡驱动面板决定";

                if (_backendOverride == ScalingBackend.Nvapi)
                    return "NVIDIA NVAPI（已手动强制指定）";
                if (_backendOverride == ScalingBackend.Win32)
                    return "Windows 原生接口（已手动强制指定）";

                if (UsesNvapiScaling)
                    return "NVIDIA NVAPI（该驱动不采纳 Windows 原生缩放字段）";
                if (_vendor == GpuVendor.Amd && _adlxReady)
                    return "AMD ADLX（官方驱动配置接口，精确控制缩放模式）";
                if (_vendor == GpuVendor.Amd && !_adlxReady)
                    return "AMD ADLX 不可用（" + _adlxError + "）—— 按不降级原则，缩放不会设置；如确需可强制 Win32 后端";
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
                // 环境变量强制指定后端时，必须优先遵守。
                // 否则排错模式形同虚设："强制 win32" 仍会被下面的 N 卡分支改回 NVAPI，
                // 这样就没法复现"走错后端导致缩放写不进去"的现场。
                if (_backendOverride == ScalingBackend.Win32)
                {
                    _nativeScalingWritable = true;
                    Log("原生缩放可写性判定：环境变量强制 Windows 原生接口 → 强制走原生");
                    return;
                }
                if (_backendOverride == ScalingBackend.Nvapi)
                {
                    _nativeScalingWritable = !_nvapiReady;
                    Log("原生缩放可写性判定：环境变量强制 NVAPI → "
                        + (_nvapiReady ? "走 NVAPI" : "但 NVAPI 不可用，退回原生"));
                    return;
                }

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

            // 2.5) AMD 显卡：尝试 ADLX 桥接层（方案 B）。
            //      ADLX 是 AMD 官方的驱动配置接口，随 amdadlx64.dll 安装。
            //      成功则缩放走 ADLX（精确设置 GPU scaling mode），失败则记录原因。
            //      不做自动降级到 Win32 —— ADLX 不可用时在报告里如实说明。
            if (_vendor == GpuVendor.Amd)
            {
                try
                {
                    _adlxReady = AdlxBridge.TryInit();
                    _adlxError = AdlxBridge.Error;
                    if (_adlxReady)
                        Log("ADLX 初始化成功：" + (AdlxBridge.DisplayName.Length > 0 ? AdlxBridge.DisplayName : "(未识别显示器)"));
                    else
                        Log("ADLX 初始化失败：" + _adlxError);
                }
                catch (Exception e)
                {
                    _adlxReady = false;
                    _adlxError = e.Message;
                    Log("ADLX 初始化异常：" + e.Message);
                }
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
                    // 【仅分辨率版】完全不碰缩放：缩放方式由用户在显卡驱动面板里设好，
                    // 程序只负责切分辨率，驱动会沿用那个设置。
                    // 这样零厂商依赖、任何显卡通用，也不会因为缩放设不上而报错。
                    if (GpuExtensions.ResolutionOnly)
                    {
                        message = string.Format("已切换：{0}x{1}@{2}Hz（缩放方式由显卡驱动设置决定，本版本不改动）",
                            p.Width, p.Height, appliedHz);
                        Log("成功(仅分辨率版，不碰缩放): " + message);
                        return true;
                    }

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

            // 缩放并进"切模式"那一次调用，只对 Windows 原生后端有意义。
            // 【仅分辨率版】永远不并：它根本不碰缩放。
            // 【NVIDIA】走 NVAPI 独立设缩放，不并。
            // 【AMD】方案 B 走 ADLX 官方接口独立设缩放，也不并；除非用户显式强制 Win32
            //   后端（排错用）。ADLX 不可用时同样不并 —— 按"不降级"原则，后面会如实报错，
            //   而不是悄悄用 dmDisplayFixedOutput 顶替。
            bool amdUsesAdlx = _vendor == GpuVendor.Amd && _backendOverride != ScalingBackend.Win32;
            bool foldScaling = !GpuExtensions.ResolutionOnly
                && !UsesNvapiScaling && !amdUsesAdlx
                && !spec.IsUnchanged
                && spec.Mode != ScalingMode.Integer;

            // 已经是目标分辨率：无法靠"切模式"带出缩放，交给后面的单独设置
            if (DisplayInfo.IsCurrent(p.Width, p.Height))
            {
                appliedHz = DisplayInfo.GetCurrentMode().dmDisplayFrequency;
                Log("已是目标分辨率 @" + appliedHz + "Hz，跳过模式切换");
                return true;
            }

            string lastReason = "";
            // 记录进这个循环前的原始模式，任何失败路径都要回到它 —— 见下面的恢复逻辑。
            DisplayInfo.DEVMODE origin = DisplayInfo.GetCurrentMode();
            bool scalingFailed = false;
            // 连续被驱动以 -2（BADMODE）拒绝的次数。达到 3 次就判定该分辨率整体不可用，
            // 不再遍历剩余刷新率 —— 见循环内的说明。
            int badModeCount = 0;
            bool allModesBad = false;

            foreach (DisplayConfigBackend.DEVMODE m in modes)
            {
                // 若已经因为"缩放写不进去"中止，就不再尝试更低刷新率的同分辨率模式。
                // 缩放失败和刷新率无关，换刷新率解决不了问题，只会把屏幕越拖越低。
                if (scalingFailed) break;
                // 该分辨率整体被驱动拒绝：不再试其它刷新率（纯空转）
                if (allModesBad) break;

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
                        // r == -2（DISP_CHANGE_BADMODE）：这个"分辨率+刷新率"组合被驱动拒绝。
                        // 若同一分辨率下连续几个组合都被拒，说明整个分辨率不可用
                        // （例如当前显示器/驱动不支持该分辨率），继续换刷新率是纯空转：
                        // 实测会把 11 个模式全试一遍（144→23Hz）后才报错，白等几秒。
                        if (r == -2)
                        {
                            badModeCount++;
                            if (badModeCount >= 3)
                            {
                                Log("该分辨率下连续 " + badModeCount + " 个模式均被驱动拒绝，判定分辨率整体不可用");
                                allModesBad = true;
                                break;
                            }
                            break;
                        }
                        // 驱动忙 / 一般性错误：等一会儿重试；明确不支持就别浪费次数
                        if (r == -1 || r == -3)
                        {
                            Thread.Sleep(300 * attempt);
                            continue;
                        }
                        break;
                    }
                    badModeCount = 0;   // 一旦有模式被接受，清零

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
                                // 【关键】缩放没落上就**到此为止**，不再换更低刷新率重试。
                                //
                                // 旧逻辑在这里 continue，内层 3 次用完后外层 foreach 会推进到
                                // 下一个更低刷新率的模式，于是一路 144→120→60→50→30→29→25→24→23Hz，
                                // 最后报失败时屏幕已经停在最低刷新率上（实测 2026-10-23 17:34 发生）。
                                //
                                // 缩放写不进去通常意味着走错了后端（例如 N 卡误判成"未知厂商"
                                // 而退回 Windows 原生接口），降刷新率既解决不了也伤害体验。
                                // 正确做法：立刻停手，把屏幕恢复原状，如实报错。
                                Log("缩放未随模式切换生效（回读 " + got + "），中止重试并恢复原模式");
                                lastReason = "缩放未生效";
                                scalingFailed = true;
                                break;
                            }
                        }
                        else
                        {
                            // 不并缩放时，只要模式切对了就算成功。
                            return true;
                        }
                        return true;
                    }

                    Log("返回成功但回读不符，重试（第" + attempt + "次）");
                    lastReason = "驱动未真正生效";
                    Thread.Sleep(400 * attempt);
                }
            }

            // 无论是缩放失败还是所有模式都试完仍失败，都把屏幕恢复到进入循环前的模式。
            // 这是"绝不把用户屏幕留在糟糕状态"的兜底：宁可切回原状，也不留着最低刷新率。
            if (origin.dmPelsWidth > 0)
            {
                try
                {
                    bool curOk = DisplayInfo.IsCurrent(origin.dmPelsWidth, origin.dmPelsHeight)
                                 && DisplayInfo.GetCurrentMode().dmDisplayFrequency == origin.dmDisplayFrequency;
                    if (!curOk)
                    {
                        Log("恢复到切换前的模式：" + origin.dmPelsWidth + "x" + origin.dmPelsHeight
                            + "@" + origin.dmDisplayFrequency + "Hz");
                        // 用 DisplayInfo.ApplyMode：它接受 DisplayInfo.DEVMODE（origin 的类型）。
                        // DisplayConfigBackend.ApplyMode 要的是另一个类型，不能直接混用。
                        DisplayInfo.ApplyMode(origin);
                        if (WaitForMode(origin.dmPelsWidth, origin.dmPelsHeight, origin.dmDisplayFrequency, 3000))
                        {
                            Log("已恢复原模式");
                        }
                        else
                        {
                            Log("恢复原模式未能确认（驱动可能拒绝），当前停留在 "
                                + DisplayInfo.GetCurrentMode().dmPelsWidth + "x"
                                + DisplayInfo.GetCurrentMode().dmPelsHeight + "@"
                                + DisplayInfo.GetCurrentMode().dmDisplayFrequency + "Hz");
                        }
                    }
                }
                catch (Exception e)
                {
                    Log("恢复原模式异常：" + e.Message);
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

            // AMD 显卡 + ADLX 就绪：走 ADLX 精确设置缩放（方案 B）
            if (_vendor == GpuVendor.Amd && _adlxReady && _backendOverride != ScalingBackend.Win32)
            {
                bool adlxOk = AdlxBridge.ApplyScaling(spec, out detail, out actual);
                if (adlxOk) return true;
                // ADLX 失败：如实报错，不自动降级到 Win32
                detail = "ADLX 设置失败：" + detail;
                return false;
            }

            // AMD 显卡但 ADLX 不可用：不降级到 Win32，如实报错
            // （用户明确要求 AMD 版必须走 ADLX，不走 Windows 原生字段）
            if (_vendor == GpuVendor.Amd && !_adlxReady && _backendOverride != ScalingBackend.Win32)
            {
                detail = "AMD 显卡 ADLX 桥接不可用，无法设置缩放：" + _adlxError
                       + "。请确认：① AMD Software 完整安装；② 显示器直连显卡输出口"
                       + "（远程桌面/向日葵会话下 ADLX 枚举不到物理显示器）；"
                       + "③ 以管理员权限运行。如确需临时走原生接口，可在设置里强制指定 Win32 后端。";
                return false;
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
                detail = "整数缩放需要显卡专有接口（NVIDIA NVAPI / AMD ADLX），原生 Win32 接口不支持（本机为 " + _vendorText + "）";
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
                if (GpuExtensions.ResolutionOnly)
                {
                    // 仅分辨率版本来就不需要 NVAPI，别让用户以为"缺了组件"
                    sb.AppendLine("  说明：本版本只切换分辨率，本来就不需要 NVAPI，这是正常的。");
                }
                else
                {
                    if (_vendor == GpuVendor.Amd && _adlxReady)
                    {
                        // AMD 走 ADLX：缩放、整数缩放、自定义分辨率都由官方 ADLX 提供
                        sb.AppendLine("  说明：AMD 显卡不使用 NVAPI，缩放 / 整数缩放 / 自定义分辨率");
                        sb.AppendLine("        均改走 AMD 官方 ADLX 接口（见下文），功能不受影响。");
                    }
                    else
                    {
                        sb.AppendLine("  说明：这不是错误。非 NVIDIA 显卡本来就没有 NVAPI，");
                        sb.AppendLine("        缩放会自动改走 Windows 原生接口，功能不受影响。");
                    }
                }
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

            // 仅分辨率版不做缩放自检 —— 它根本不碰缩放，测了也没意义，反而误导用户。
            if (GpuExtensions.ResolutionOnly)
            {
                sb.AppendLine("[缩放]");
                sb.AppendLine("  本版本只切换分辨率，不设置缩放方式。");
                sb.AppendLine("  缩放方式请在显卡驱动控制面板里设置一次：");
                sb.AppendLine("    · NVIDIA：NVIDIA 控制面板 → 显示 → 调整桌面尺寸和位置");
                sb.AppendLine("    · AMD   ：AMD Software → 设置 → 显示 → 缩放模式");
                sb.AppendLine("  设置好之后，切换分辨率时会自动沿用该缩放方式。");
                sb.AppendLine();
                return sb.ToString();
            }

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
                    : (_vendor == GpuVendor.Amd && _adlxReady
                        ? "AMD 显卡，走 ADLX（开 GPU scaling 后可用）"
                        : "需要 NVIDIA 或 AMD 显卡的专有接口，本机不支持（不影响其它缩放方式）")));
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

            if (_vendor == GpuVendor.Amd)
            {
                try
                {
                    sb.Append(AdlxBridge.SelfTestSection());
                }
                catch (Exception e)
                {
                    sb.AppendLine();
                    sb.AppendLine("[ADLX 缩放自检] 失败：" + e.Message);
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
