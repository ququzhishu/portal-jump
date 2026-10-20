#if WITH_NVAPI || UNIFIED
using System;
using System.Collections.Generic;
using System.Linq;
using NvAPIWrapper;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.Display;
using NvAPIWrapper.Native.Display.Structures;
using NvAPIWrapper.Native.Exceptions;
using NvAPIWrapper.Native.General;
using NvAPIWrapper.Native.Interfaces.Display;

namespace PortalJump
{
    /// <summary>
    /// NVAPI 桥接层 —— **整个程序里唯一接触 NvAPIWrapper 的入口**。
    ///
    /// 为什么要有这一层（重要，别拆散）：
    /// 折返跃迁要同时服务 NVIDIA 和 AMD / Intel 用户，而 NVAPI 只有 NVIDIA 有。
    /// 如果主代码直接 `using NvAPIWrapper;` 并调用它的类型，那么在没装
    /// NvAPIWrapper.dll 的机器上，**JIT 编译这些方法时就会抛 FileNotFoundException**，
    /// 程序可能直接崩在启动阶段。
    ///
    /// 所以这里做了两件事：
    ///  1. 所有 NVAPI 类型只出现在本文件里；
    ///  2. 每个对外方法都只接收/返回基础类型（string / bool / int），
    ///     并且真正碰 NVAPI 的那一步放在**独立的内部方法**里，
    ///     由外层 try/catch 包住 —— 这样即使整个方法体 JIT 失败，
    ///     异常也能被外层捕获，降级成"NVAPI 不可用"，而不是让程序崩掉。
    ///
    /// 结果：**同一个 exe，带不带 NvAPIWrapper.dll 都能跑**。
    ///   · 带 → NVAPI 可用，支持整数缩放 / 自定义分辨率 / 精确缩放
    ///   · 不带 → 自动降级到 Windows 原生接口，AMD / Intel 正常使用
    /// </summary>
    internal static class NvapiBridge
    {
        /// <summary>NVAPI 是否已成功初始化（未初始化时所有方法都是安全的空操作）</summary>
        internal static bool Ready { get; private set; }

        internal static string GpuInfo { get; private set; }
        internal static string DriverInfo { get; private set; }
        internal static string Error { get; private set; }

        static NvapiBridge()
        {
            GpuInfo = "";
            DriverInfo = "";
            Error = "";
        }

        /// <summary>
        /// 初始化。**永不抛异常**。
        /// 注意：真正碰 NVAPI 的代码在 InitCore() 里，
        /// 万一 NvAPIWrapper.dll 缺失导致 InitCore 无法 JIT，
        /// 异常会在这里被捕获，降级为"不可用"。
        /// </summary>
        internal static bool TryInit()
        {
            try
            {
                return InitCore();
            }
            catch (Exception e)
            {
                Ready = false;
                Error = DescribeLoadFailure(e);
                GpuInfo = "";
                DriverInfo = "";
                return false;
            }
        }

        /// <summary>
        /// 把"NvAPIWrapper.dll 缺失/损坏"翻译成人话，别让用户看到一堆英文类型名。
        /// </summary>
        static string DescribeLoadFailure(Exception e)
        {
            string msg = e == null ? "" : (e.Message == null ? "" : e.Message);
            string full = e == null ? "" : e.GetType().FullName;

            bool missing = (full != null && (full.IndexOf("FileNotFound", StringComparison.OrdinalIgnoreCase) >= 0
                                         || full.IndexOf("TypeLoad", StringComparison.OrdinalIgnoreCase) >= 0
                                         || full.IndexOf("BadImageFormat", StringComparison.OrdinalIgnoreCase) >= 0))
                           || msg.IndexOf("NvAPIWrapper", StringComparison.OrdinalIgnoreCase) >= 0
                           || msg.IndexOf("NvAPI", StringComparison.OrdinalIgnoreCase) >= 0;

            if (missing)
            {
                return "未找到 NvAPIWrapper.dll（NVIDIA 专用组件）。"
                    + "当前按通用模式运行：分辨率与缩放仍可用，"
                    + "但整数缩放、创建自定义分辨率这两项不可用。";
            }
            return msg.Length > 0 ? msg : "NVAPI 初始化失败";
        }

        /// <summary>真正碰 NVAPI 的初始化。单独成方法，便于外层捕获 JIT/加载失败。</summary>
        static bool InitCore()
        {
            try
            {
                NVIDIA.Initialize();
            }
            catch (Exception e)
            {
                Ready = false;
                Error = e.Message;
                return false;
            }

            Ready = true;
            Error = "";

            try
            {
                var names = new List<string>();
                foreach (PhysicalGPU g in PhysicalGPU.GetPhysicalGPUs())
                {
                    names.Add(g.FullName);
                }
                GpuInfo = names.Count > 0 ? string.Join(" + ", names.ToArray()) : "";
            }
            catch (Exception e)
            {
                GpuInfo = "";
                Switcher.Log("枚举 NVIDIA GPU 失败: " + e.Message);
            }

            try
            {
                DriverInfo = NVIDIA.DriverBranchVersion + " (" + NVIDIA.DriverVersion + ")";
            }
            catch
            {
                DriverInfo = "";
            }

            return true;
        }

        // ------------------------------------------------------------------
        // 缩放
        // ------------------------------------------------------------------

        /// <summary>
        /// 用 NVAPI 设置缩放。nvScaling 用 NV_SCALING 数值。
        /// 成功返回 true；actual 非空表示驱动实际采用了别的值。
        /// 所有 NVAPI 类型都封在 ApplyCore 里。
        /// </summary>
        internal static bool ApplyScaling(int nvScaling, out string detail, out string actual)
        {
            detail = "";
            actual = "";
            if (!Ready)
            {
                detail = "NVAPI 不可用";
                return false;
            }
            try
            {
                return ApplyScalingCore(nvScaling, out detail, out actual);
            }
            catch (Exception e)
            {
                detail = DescribeLoadFailure(e);
                return false;
            }
        }

        static bool ApplyScalingCore(int nvScaling, out string detail, out string actual)
        {
            detail = "";
            actual = "";

            IPathInfo[] paths = GetDisplayConfigRetry();
            int idx = FindPrimaryPathIndex(paths);
            if (idx < 0)
            {
                detail = "未找到主显示器路径（显卡未输出到任何显示器？）";
                return false;
            }

            try
            {
                IPathInfo[] rebuilt = BuildPathsWithScaling(paths, idx, nvScaling);
                DisplayApi.SetDisplayConfig(rebuilt, DisplayConfigFlags.None);
            }
            catch (NVIDIAApiException e)
            {
                detail = "驱动拒绝（" + e.Status + "）：" + e.Message;
                return false;
            }
            catch (NVIDIANotSupportedException e)
            {
                detail = e.Message;
                return false;
            }

            // 回读校验。NVAPI 的回读有滞后：刚设完读到的是上一次的值，
            // 所以要"连续两次读到期望值"才算稳定，不一致就重新提交（最多 3 次）。
            int got = -1;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                System.Threading.Thread.Sleep(250);
                int stable = 0;
                int last = -2;
                int waited = 0;
                while (waited < 1500)
                {
                    int cur = -1;
                    try
                    {
                        IPathInfo[] after = GetDisplayConfigRetry();
                        int a = FindPrimaryPathIndex(after);
                        if (a >= 0)
                        {
                            foreach (IPathTargetInfo t in after[a].TargetsInfo)
                            {
                                if (t.Details.HasValue) { cur = (int)t.Details.Value.Scaling; break; }
                            }
                        }
                    }
                    catch { }

                    if (cur == nvScaling)
                    {
                        if (last == nvScaling) stable++;
                        else stable = 1;
                        if (stable >= 2) { got = nvScaling; break; }
                    }
                    else
                    {
                        stable = 0;
                    }
                    last = cur;
                    got = cur;
                    System.Threading.Thread.Sleep(120);
                    waited += 120;
                }

                if (got == nvScaling) return true;

                if (attempt < 3)
                {
                    Switcher.Log("NVAPI 缩放回读 " + got + "（期望 " + nvScaling + "），第 " + attempt + " 次重试提交");
                    try
                    {
                        IPathInfo[] again = GetDisplayConfigRetry();
                        int a2 = FindPrimaryPathIndex(again);
                        if (a2 >= 0)
                        {
                            IPathInfo[] rebuilt2 = BuildPathsWithScaling(again, a2, nvScaling);
                            DisplayApi.SetDisplayConfig(rebuilt2, DisplayConfigFlags.None);
                        }
                    }
                    catch (Exception e)
                    {
                        Switcher.Log("  重试提交失败：" + e.Message);
                    }
                }
            }

            if (got >= 0 && got != nvScaling)
            {
                actual = got + " - " + ScalingNames.LegacyName(got);
                Switcher.Log("NVAPI 缩放最终回读不一致：请求 " + nvScaling + "，实际 " + got);
            }
            return true;
        }

        /// <summary>读当前主显示器缩放（NVAPI 视角）。失败返回空串。</summary>
        internal static string CurrentScalingText()
        {
            if (!Ready) return "";
            try
            {
                return CurrentScalingTextCore();
            }
            catch
            {
                return "";
            }
        }

        static string CurrentScalingTextCore()
        {
            IPathInfo[] paths = GetDisplayConfigRetry();
            int idx = FindPrimaryPathIndex(paths);
            if (idx < 0) return "";
            foreach (IPathTargetInfo t in paths[idx].TargetsInfo)
            {
                if (t.Details.HasValue)
                {
                    Scaling s = t.Details.Value.Scaling;
                    return s + " - " + ScalingNames.LegacyName((int)s);
                }
            }
            return "";
        }

        /// <summary>自检报告里的 NVAPI 段。NVAPI 不可用时返回空串。</summary>
        internal static string SelfTestSection()
        {
            if (!Ready) return "";
            try
            {
                return SelfTestSectionCore();
            }
            catch (Exception e)
            {
                return "  自检失败：" + e.GetType().Name + " " + e.Message + Environment.NewLine;
            }
        }

        static string SelfTestSectionCore()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine();
            sb.AppendLine("[NVAPI 缩放自检]（ValidateOnly 校验，不会切换画面）");
            IPathInfo[] paths = GetDisplayConfigRetry();
            int idx = FindPrimaryPathIndex(paths);
            if (idx < 0)
            {
                sb.AppendLine("  未找到主显示器路径。");
                return sb.ToString();
            }
            foreach (int s in new int[] { 1, 2, 3, 5, 6, 7, 8 })
            {
                string verdict;
                try
                {
                    IPathInfo[] rebuilt = BuildPathsWithScaling(paths, idx, s);
                    DisplayApi.SetDisplayConfig(rebuilt, DisplayConfigFlags.ValidateOnly);
                    verdict = "驱动接受";
                }
                catch (NVIDIAApiException e) { verdict = "驱动拒绝（" + e.Status + "）"; }
                catch (ScalingNotSupportedException e) { verdict = "不支持：" + e.Message; }
                catch (Exception e) { verdict = "失败：" + e.GetType().Name + " " + e.Message; }
                sb.AppendLine("  " + s + " (" + ScalingNames.LegacyName(s) + ")：" + verdict);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 以下全部是 NVAPI 专属实现细节
        // ------------------------------------------------------------------

        static IPathInfo[] GetDisplayConfigRetry()
        {
            Exception last = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    return DisplayApi.GetDisplayConfig();
                }
                catch (NVIDIAApiException e)
                {
                    last = e;
                    if (e.Status != Status.DeviceBusy) throw;
                    Switcher.Log("GetDisplayConfig DeviceBusy，第 " + (attempt + 1) + " 次重试");
                    System.Threading.Thread.Sleep(400);
                }
                catch (Exception e)
                {
                    last = e;
                    System.Threading.Thread.Sleep(300);
                }
            }
            throw new InvalidOperationException("读取显示配置失败（驱动持续忙）："
                + (last == null ? "" : last.Message));
        }

        static int FindPrimaryPathIndex(IPathInfo[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i].SourceModeInfo.IsGDIPrimary) return i;
            }
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i].TargetsInfo.Any()) return i;
            }
            return -1;
        }

        /// <summary>
        /// 按驱动返回的结构版本（V2/V1）整体重建路径数组，只改主显示器的缩放，
        /// 其余路径与字段原样保留。绝不允许 V1/V2 混用，否则会破坏其它显示器。
        /// </summary>
        static IPathInfo[] BuildPathsWithScaling(IPathInfo[] paths, int idx, int scaling)
        {
            bool useV2 = false;
            foreach (IPathInfo p in paths)
            {
                if (p is PathInfoV2) { useV2 = true; break; }
            }

            var rebuilt = new IPathInfo[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                IPathInfo path = paths[i];
                var targets = new List<IPathTargetInfo>(path.TargetsInfo);

                if (useV2)
                {
                    var list = new List<PathTargetInfoV2>();
                    for (int j = 0; j < targets.Count; j++)
                    {
                        IPathTargetInfo t = targets[j];
                        bool isMainTarget = (i == idx && j == 0);
                        PathAdvancedTargetInfo adv = isMainTarget
                            ? BuildAdvancedWithScaling(t, scaling)
                            : GetAdvancedOrThrow(t);
                        uint ccd = 0;
                        if (t is PathTargetInfoV2) ccd = ((PathTargetInfoV2)t).WindowsCCDTargetId;
                        list.Add(new PathTargetInfoV2(t.DisplayId, ccd, adv));
                    }
                    rebuilt[i] = new PathInfoV2(list.ToArray(), path.SourceModeInfo, path.SourceId);
                }
                else
                {
                    var list = new List<PathTargetInfoV1>();
                    for (int j = 0; j < targets.Count; j++)
                    {
                        IPathTargetInfo t = targets[j];
                        bool isMainTarget = (i == idx && j == 0);
                        PathAdvancedTargetInfo adv = isMainTarget
                            ? BuildAdvancedWithScaling(t, scaling)
                            : GetAdvancedOrThrow(t);
                        list.Add(new PathTargetInfoV1(t.DisplayId, adv));
                    }
                    rebuilt[i] = new PathInfoV1(list.ToArray(), path.SourceModeInfo, path.SourceId);
                }
            }
            return rebuilt;
        }

        static PathAdvancedTargetInfo GetAdvancedOrThrow(IPathTargetInfo t)
        {
            if (!t.Details.HasValue) return default(PathAdvancedTargetInfo);
            return t.Details.Value;
        }

        /// <summary>只替换缩放字段，其余（旋转/刷新率/时序/TV 格式等）全部原样保留</summary>
        static PathAdvancedTargetInfo BuildAdvancedWithScaling(IPathTargetInfo t, int scaling)
        {
            if (!t.Details.HasValue)
            {
                throw new ScalingNotSupportedException("主显示器未提供缩放信息，无法设置缩放模式");
            }

            PathAdvancedTargetInfo d = t.Details.Value;
            if (d.TimingOverride == TimingOverride.Custom)
            {
                throw new ScalingNotSupportedException(
                    "当前显示器正在使用自定义时序（Custom Timing），NVAPI 不支持在自定义时序下修改缩放模式");
            }

            var s = (Scaling)scaling;
            if (d.TVFormat == TVFormat.None)
            {
                return new PathAdvancedTargetInfo(d.Rotation, s, d.RefreshRateInMillihertz, d.TimingOverride,
                    d.IsInterlaced, d.IsClonePrimary, d.IsClonePanAndScanTarget, d.DisableVirtualModeSupport,
                    d.IsPreferredUnscaledTarget);
            }

            // 电视/HDMI 场景必须带上 TV 格式与接口类型，否则会被驱动重置
            return new PathAdvancedTargetInfo(d.Rotation, s, d.TVFormat, d.ConnectorType,
                d.RefreshRateInMillihertz, d.TimingOverride, d.IsInterlaced, d.IsClonePrimary,
                d.IsClonePanAndScanTarget, d.DisableVirtualModeSupport, d.IsPreferredUnscaledTarget);
        }
    }
}
#else
using System;

namespace PortalJump
{
    /// <summary>
    /// 通用版（未定义 WITH_NVAPI）的 NVAPI 空实现。
    ///
    /// 作用：让 Switcher 等调用方**不需要写任何 #if**，直接调用这些方法即可。
    /// 所有方法都安全地返回"不可用"，绝不抛异常。
    /// 这样两个版本共享同一套核心代码，只有 NvapiBridge.cs 一个文件不同。
    /// </summary>
    internal static class NvapiBridge
    {
        internal static bool Ready { get { return false; } }
        internal static string GpuInfo { get { return ""; } }
        internal static string DriverInfo { get { return ""; } }
        internal static string Error
        {
            get
            {
                // 按版本说人话：仅分辨率版本来就不需要 NVAPI，别让用户以为缺了组件
                if (GpuExtensions.ResolutionOnly)
                {
                    return "本版本只切换分辨率，不需要 NVAPI 组件（这是正常的）。";
                }
                return "当前为通用版（未包含 NVAPI 组件）。"
                    + "分辨率与缩放照常可用；AMD 显卡的整数缩放与创建自定义分辨率经官方 ADLX 支持，"
                    + "NVIDIA 专有功能才需要 NVIDIA 版。";
            }
        }

        internal static bool TryInit() { return false; }

        internal static bool ApplyScaling(int nvScaling, out string detail, out string actual)
        {
            detail = Error;
            actual = "";
            return false;
        }

        internal static string CurrentScalingText() { return ""; }

        internal static string SelfTestSection() { return ""; }
    }
}
#endif
