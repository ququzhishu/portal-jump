using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PortalJump
{
    /// <summary>
    /// ADLX 桥接层 —— AMD 显卡的缩放控制入口（与 NvapiBridge 同构）。
    ///
    /// 为什么要有这一层：
    /// AMD 没有 NVAPI，但有 ADLX（AMD Device Library eXtra），随 AMD 驱动安装。
    /// 真正的 dll 名是 <c>amdadlx64.dll</c>（在 C:\Windows\System32\ 下），不是 ADLX.dll。
    /// 本类在运行时 LoadLibrary 它，通过 C++ COM 风格 vtable 调用：
    ///   ADLXInitializeWithIncompatibleDriver → IADLXSystem → IADLXDisplayServices
    ///     → IADLXDisplayList → IADLXDisplay（主显示器）
    ///     → IADLXDisplayGPUScaling / IADLXDisplayScalingMode（SetEnabled / SetMode）
    ///
    /// vtable 偏移依据 ADLX 官方头文件（GPUOpen-LibrariesAndSDKs/ADLX）逐方法核对，
    /// 并在本机（RX 9070 XT，ADLX 1.4）用探针逐个验证过：
    ///   · 普通接口 IADLXInterface 基类恒为 [0]Acquire [1]Release [2]QueryInterface，派生类从 [3] 开始；
    ///   · 但 IADLXSystem 是单例（不应被 delete），vtable 直接从业务方法开始，
    ///     没有 Acquire/Release，故 GetDisplaysServices 在 [3]（不是 [5]）；
    ///   · 列表取元素要用类型安全的 At_DisplayList（vtable[11]），泛型 At([7]) 会
    ///     返回 ADLX_UNKNOWN_INTERFACE(6)；
    ///   · 列表 Size() 是无参返回（adlx_uint Size()），不是 out 参数。
    /// 顺序写错会直接 AccessViolation 崩进程，所以这里每一步都回读验证。
    ///
    /// 分辨率仍然走 Win32 ChangeDisplaySettingsEx（ADLX 没有 SetResolution），
    /// 本类只负责"缩放方式"这一件事。
    /// </summary>
    internal static class AdlxBridge
    {
        /// <summary>ADLX 是否成功初始化（未初始化时所有方法都是安全空操作）</summary>
        internal static bool Ready { get; private set; }

        internal static string Error { get; private set; }
        internal static string GpuInfo { get; private set; }
        internal static string DisplayName { get; private set; }

        // ---- 缓存的接口指针 ----
        static IntPtr _hDll;            // amdadlx64.dll 模块句柄
        static IntPtr _system;          // IADLXSystem
        static IntPtr _displayServices; // IADLXDisplayServices
        static IntPtr _display;         // IADLXDisplay（主显示器）

        static AdlxBridge()
        {
            Error = "";
            GpuInfo = "";
            DisplayName = "";
        }

        // =====================================================================
        //  vtable 调用辅助
        // =====================================================================

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int AdlxQueryVersionDelegate(out ulong version);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int AdlxInitDelegate(ulong version, out IntPtr ppSystem);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int OutPtrDelegate(IntPtr self, out IntPtr ppOut);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int OutUIntDelegate(IntPtr self, out uint value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int IntArgDelegate(IntPtr self, int arg);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int StrArgOutDelegate(IntPtr self, out IntPtr ppStr);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate UIntPtr SizeMethodDelegate(IntPtr self);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr h, string name);
        [DllImport("kernel32.dll")]
        static extern bool FreeLibrary(IntPtr h);

        /// <summary>读 vtable 第 index 项并转成指定委托。绝不抛异常。</summary>
        static T Vtbl<T>(IntPtr @object, int index) where T : class
        {
            if (@object == IntPtr.Zero) return null;
            IntPtr vtbl = Marshal.ReadIntPtr(@object);
            IntPtr fn = Marshal.ReadIntPtr(vtbl, index * IntPtr.Size);
            if (fn == IntPtr.Zero) return null;
            return (T)(object)Marshal.GetDelegateForFunctionPointer(fn, typeof(T));
        }

        // =====================================================================
        //  初始化
        // =====================================================================

        internal static bool TryInit()
        {
            try
            {
                return InitCore();
            }
            catch (Exception e)
            {
                Ready = false;
                Error = e.GetType().Name + ": " + e.Message;
                _system = IntPtr.Zero;
                _displayServices = IntPtr.Zero;
                _display = IntPtr.Zero;
                return false;
            }
        }

        static bool InitCore()
        {
            // 1) 找并加载 amdadlx64.dll（AMD 驱动自带，System32 下）
            _hDll = LoadLibraryW("amdadlx64.dll");
            if (_hDll == IntPtr.Zero)
            {
                // 兜底：常见绝对路径
                string[] fallbacks =
                {
                    @"C:\Windows\System32\amdadlx64.dll",
                };
                foreach (string p in fallbacks)
                {
                    _hDll = LoadLibraryW(p);
                    if (_hDll != IntPtr.Zero) break;
                }
            }
            if (_hDll == IntPtr.Zero)
            {
                Error = "未找到 amdadlx64.dll（AMD 驱动组件）。请确认已安装完整 AMD Software 驱动。";
                return false;
            }

            // 2) 先取本机 ADLX 真实版本号（ADLXQueryFullVersion 输出 uint64）
            ulong version = 0x10000;
            IntPtr pQueryVer = GetProcAddress(_hDll, "ADLXQueryFullVersion");
            if (pQueryVer != IntPtr.Zero)
            {
                var qv = (AdlxQueryVersionDelegate)Marshal.GetDelegateForFunctionPointer(
                    pQueryVer, typeof(AdlxQueryVersionDelegate));
                ulong got;
                if (qv(out got) == 0 && got != 0) version = got;
            }

            // 3) 初始化系统。
            //
            // 关键：本机用标准 ADLXInitialize 初始化后，GetNumberOfDisplays 会在
            // ADLX 内部空引用（NullReferenceException，且是 SEH 崩溃，catch 不住）。
            // 改用官方提供的 ADLXInitializeWithIncompatibleDriver 入口即可正常枚举——
            // 它不是降级：仍是完整 ADLX 接口（能枚举 GPU/显示器、SetMode 全部可用），
            // 只是官方为"内部 ADL 初始化异常"的机器准备的另一条初始化路径。
            // 实测（probe）：此入口下 GPUScaling.SetEnabled 与 ScalingMode.SetMode 均 r=0。
            IntPtr pInit = GetProcAddress(_hDll, "ADLXInitializeWithIncompatibleDriver");
            int r;
            if (pInit != IntPtr.Zero)
            {
                var initCompat = (AdlxInitDelegate)Marshal.GetDelegateForFunctionPointer(
                    pInit, typeof(AdlxInitDelegate));
                r = initCompat(version, out _system);
            }
            else
            {
                // 老版本没有兼容入口，退回标准 ADLXInitialize
                pInit = GetProcAddress(_hDll, "ADLXInitialize");
                if (pInit == IntPtr.Zero)
                {
                    Error = "amdadlx64.dll 没有 ADLXInitialize 导出。";
                    return false;
                }
                var initStd = (AdlxInitDelegate)Marshal.GetDelegateForFunctionPointer(
                    pInit, typeof(AdlxInitDelegate));
                r = initStd(version, out _system);
            }
            if (r != 0 || _system == IntPtr.Zero)
            {
                Error = "ADLX 初始化失败，返回码=" + r;
                _system = IntPtr.Zero;
                return false;
            }

            // 4) IADLXSystem 是单例（无 Acquire/Release/QueryInterface 前缀），
            //    其 vtable 直接从业务方法开始：
            //    [0]GetHybridGraphicsType [1]GetGPUs [2]QueryInterface
            //    [3]GetDisplaysServices [4]GetDesktopsServices ...
            var getDispSvc = Vtbl<OutPtrDelegate>(_system, 3);
            if (getDispSvc == null) { Error = "取不到 IADLXSystem.GetDisplaysServices"; return false; }
            r = getDispSvc(_system, out _displayServices);
            if (r != 0 || _displayServices == IntPtr.Zero)
            {
                Error = "GetDisplaysServices 失败，返回码=" + r;
                return false;
            }

            // 5) IADLXDisplayServices vtable[4] = GetDisplays（拿显示器列表）
            var getDisplays = Vtbl<OutPtrDelegate>(_displayServices, 4);
            if (getDisplays == null) { Error = "取不到 GetDisplays"; return false; }
            IntPtr displayList;
            r = getDisplays(_displayServices, out displayList);
            if (r != 0 || displayList == IntPtr.Zero)
            {
                Error = "GetDisplays 失败，返回码=" + r;
                return false;
            }

            // IADLXDisplayList vtable[3] = Size() —— 无参，返回值即数量
            var sizeFn = Vtbl<SizeMethodDelegate>(displayList, 3);
            uint count = sizeFn == null ? 0 : (uint)sizeFn(displayList).ToUInt32();

            // 6) 取第一个显示器（Begin 位置，通常是主显示器）
            if (count == 0)
            {
                Error = "ADLX 报告没有任何显示器。";
                return false;
            }

            // 不能用泛型 At(vtable[7])：它内部 QueryInterface 会返回
            // ADLX_UNKNOWN_INTERFACE(6)。要用类型安全的 At_DisplayList(vtable[11])。
            IntPtr pAt = Marshal.ReadIntPtr(Marshal.ReadIntPtr(displayList), 11 * IntPtr.Size);
            var atTyped = (AtDelegate)Marshal.GetDelegateForFunctionPointer(pAt, typeof(AtDelegate));

            // location 取 Begin()（vtable[5]），多显示器时是第一个
            var beginFn = (SizeMethodDelegate)Marshal.GetDelegateForFunctionPointer(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(displayList), 5 * IntPtr.Size),
                typeof(SizeMethodDelegate));
            uint location = (uint)beginFn(displayList);
            r = atTyped(displayList, location, out _display);
            if (r != 0 || _display == IntPtr.Zero)
            {
                Error = "DisplayList.At_DisplayList(" + location + ") 失败，返回码=" + r;
                return false;
            }

            // 读显示器名（vtable[6] = Name(const char**)，ANSI）
            try
            {
                var nameFn = Vtbl<StrArgOutDelegate>(_display, 6);
                IntPtr pName;
                if (nameFn != null && nameFn(_display, out pName) == 0 && pName != IntPtr.Zero)
                {
                    DisplayName = Marshal.PtrToStringAnsi(pName) ?? "";
                }
            }
            catch { }

            Ready = true;
            Error = "";
            GpuInfo = DisplayName.Length > 0 ? DisplayName : "AMD 显示器";
            return true;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int AtDelegate(IntPtr self, uint location, out IntPtr ppItem);

        // =====================================================================
        //  缩放映射：程序语义 → ADLX_SCALE_MODE
        // =====================================================================

        // ADLX_SCALE_MODE 枚举（ADLXDefines.h）
        const int ADLX_PRESERVE_ASPECT_RATIO = 0;
        const int ADLX_FULL_PANEL = 1;
        const int ADLX_CENTERED = 2;

        /// <summary>把本程序的 ScalingMode 翻成 ADLX_SCALE_MODE（Integer 走单独开关）。</summary>
        internal static int MapToAdlxMode(ScalingMode mode)
        {
            switch (mode)
            {
                case ScalingMode.FullScreen: return ADLX_FULL_PANEL;
                case ScalingMode.Aspect: return ADLX_PRESERVE_ASPECT_RATIO;
                case ScalingMode.Centered: return ADLX_CENTERED;
                default: return ADLX_PRESERVE_ASPECT_RATIO;
            }
        }

        // =====================================================================
        //  应用缩放
        // =====================================================================

        /// <summary>
        /// 设置缩放方式。返回是否成功。actual 用于"回读不一致"时报告实际值。
        /// 语义：mode 为 ADLX_SCALE_MODE 值；enableInteger 为 true 时同时开整数缩放。
        /// </summary>
        internal static bool ApplyScaling(ScalingSpec spec, out string detail, out string actual)
        {
            detail = "";
            actual = "";
            if (!Ready) { detail = "ADLX 未初始化"; return false; }

            try
            {
                // IADLXDisplayServices vtable[11]=GetGPUScaling [12]=GetScalingMode [13]=GetIntegerScaling
                IntPtr gpuScaling, scalingMode, intScaling;
                if (!GetSubInterfaces(out gpuScaling, out scalingMode, out intScaling))
                {
                    detail = "取不到缩放子接口：" + Error;
                    return false;
                }

                // 整数缩放：必须先开 GPU scaling
                if (spec.Mode == ScalingMode.Integer)
                {
                    return ApplyIntegerScaling(gpuScaling, intScaling, out detail, out actual);
                }

                // 普通缩放：根据 Actor 决定 GPU scaling 开关
                //   Actor.Gpu / Auto → GPU scaling on（由 GPU 把低分辨率拉伸）
                //   Actor.Display    → GPU scaling off（交给显示器自己的 scaler）
                bool wantGpu = spec.Actor != ScaleActor.Display;

                // 先确认 GPU scaling 接口可用
                bool gpuSupported;
                if (!ReadBool(gpuScaling, 3, out gpuSupported) || !gpuSupported)
                {
                    detail = "该显示器不支持 AMD GPU 缩放。";
                    return false;
                }

                // 1) 按需打开/关闭 GPU scaling（只在状态不同时才调，避免无谓写操作）
                bool gpuEnabled;
                ReadBool(gpuScaling, 4, out gpuEnabled);
                if (gpuEnabled != wantGpu)
                {
                    int rSet = SetGpuScalingEnabled(gpuScaling, wantGpu);
                    if (rSet != 0)
                    {
                        detail = "GPU scaling " + (wantGpu ? "启用" : "关闭")
                            + "失败，返回码=" + rSet + "（" + ResultName(rSet) + "）";
                        return false;
                    }
                    // 驱动应用开关是异步的，轮询等它落地，再设模式才会被采纳（实测需 0.3~0.8s）
                    if (!WaitForGpuScaling(gpuScaling, wantGpu, 3000))
                    {
                        detail = "GPU scaling 开关已下发但驱动未在 3 秒内生效。";
                        return false;
                    }
                }

                // 显示器缩放：GPU scaling 关闭即可，缩放由显示器处理，模式对 GPU 无意义
                if (!wantGpu)
                {
                    detail = "ADLX 已关闭 GPU scaling（缩放交由显示器执行）";
                    return true;
                }

                // 2) GPU scaling 开着时，设缩放模式
                int adlxMode = MapToAdlxMode(spec.Mode);
                IntPtr pSetMode = Marshal.ReadIntPtr(Marshal.ReadIntPtr(scalingMode), 5 * IntPtr.Size);
                var setMode = (IntArgDelegate)Marshal.GetDelegateForFunctionPointer(pSetMode, typeof(IntArgDelegate));
                int r = setMode(scalingMode, adlxMode);
                if (r != 0)
                {
                    detail = "SetMode 失败，返回码=" + r + "（" + ResultName(r) + "）";
                    return false;
                }

                // 回读 GetMode（vtable[4]），轮询确认驱动真正采纳
                int got = -1;
                if (!WaitForMode(scalingMode, adlxMode, 3000, out got))
                {
                    actual = "请求 " + adlxMode + "（" + ModeName(adlxMode) + "），回读 "
                        + got + "（" + ModeName(got) + "）";
                    detail = "SetMode 后驱动回读不一致：" + actual;
                    return false;
                }

                detail = "ADLX 设置成功：GPU scaling=开，" + ModeName(adlxMode);
                actual = ModeName(adlxMode);
                return true;
            }
            catch (Exception e)
            {
                detail = "ADLX 异常：" + e.GetType().Name + " " + e.Message;
                return false;
            }
        }

        /// <summary>读某接口的 out-bool 方法（如 IsSupported/IsEnabled）。adlx_bool 是 32 位 int。</summary>
        static bool ReadBool(IntPtr iface, int vindex, out bool value)
        {
            value = false;
            try
            {
                var fn = Vtbl<OutIntDelegate>(iface, vindex);
                if (fn == null) return false;
                int v;
                if (fn(iface, out v) != 0) return false;
                value = v != 0;
                return true;
            }
            catch { return false; }
        }

        /// <summary>轮询等待 GPU scaling 变成目标状态。</summary>
        static bool WaitForGpuScaling(IntPtr gpuScaling, bool want, int timeoutMs)
        {
            int waited = 0;
            while (waited <= timeoutMs)
            {
                bool enabled;
                if (ReadBool(gpuScaling, 4, out enabled) && enabled == want) return true;
                Thread.Sleep(100);
                waited += 100;
            }
            return false;
        }

        /// <summary>轮询等待 ScalingMode 变成目标值。</summary>
        static bool WaitForMode(IntPtr scalingMode, int want, int timeoutMs, out int got)
        {
            got = -1;
            IntPtr pGetMode = Marshal.ReadIntPtr(Marshal.ReadIntPtr(scalingMode), 4 * IntPtr.Size);
            var getMode = (OutIntDelegate)Marshal.GetDelegateForFunctionPointer(pGetMode, typeof(OutIntDelegate));
            int waited = 0;
            while (waited <= timeoutMs)
            {
                int cur;
                if (getMode(scalingMode, out cur) == 0)
                {
                    got = cur;
                    if (cur == want) return true;
                }
                Thread.Sleep(100);
                waited += 100;
            }
            return false;
        }

        /// <summary>ADLX_RESULT 常见值中文名（自检/报错用）。</summary>
        static string ResultName(int r)
        {
            switch (r)
            {
                case 0: return "成功";
                case 1: return "已处于启用状态";
                case 2: return "已初始化";
                case 3: return "未指定失败";
                case 4: return "参数无效";
                case 5: return "版本不兼容";
                case 6: return "未知接口";
                case 7: return "已终止";
                case 8: return "ADL 初始化错误";
                case 9: return "未找到";
                case 10: return "对象无效";
                case 11: return "不支持";
                default: return "错误码 " + r;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int OutIntDelegate(IntPtr self, out int value);

        /// <summary>取出三个缩放子接口。失败返回 false 并写 Error。</summary>
        static bool GetSubInterfaces(out IntPtr gpuScaling, out IntPtr scalingMode, out IntPtr intScaling)
        {
            gpuScaling = IntPtr.Zero;
            scalingMode = IntPtr.Zero;
            intScaling = IntPtr.Zero;

            IntPtr pGetGpu = Marshal.ReadIntPtr(Marshal.ReadIntPtr(_displayServices), 11 * IntPtr.Size);
            IntPtr pGetMode = Marshal.ReadIntPtr(Marshal.ReadIntPtr(_displayServices), 12 * IntPtr.Size);
            IntPtr pGetInt = Marshal.ReadIntPtr(Marshal.ReadIntPtr(_displayServices), 13 * IntPtr.Size);

            var getGpu = (DispOutDelegate)Marshal.GetDelegateForFunctionPointer(pGetGpu, typeof(DispOutDelegate));
            var getMode = (DispOutDelegate)Marshal.GetDelegateForFunctionPointer(pGetMode, typeof(DispOutDelegate));
            var getInt = (DispOutDelegate)Marshal.GetDelegateForFunctionPointer(pGetInt, typeof(DispOutDelegate));

            if (getGpu(_displayServices, _display, out gpuScaling) != 0) { Error = "GetGPUScaling 失败"; return false; }
            if (getMode(_displayServices, _display, out scalingMode) != 0) { Error = "GetScalingMode 失败"; return false; }
            getInt(_displayServices, _display, out intScaling); // 整数缩放接口可能不存在，不致命
            return true;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int DispOutDelegate(IntPtr self, IntPtr display, out IntPtr ppOut);

        /// <summary>开/关 GPU scaling，返回 ADLX_RESULT（0 成功）。</summary>
        static int SetGpuScalingEnabled(IntPtr gpuScaling, bool enable)
        {
            if (gpuScaling == IntPtr.Zero) return 10; // ADLX_INVALID_OBJECT
            try
            {
                // vtable[5] = SetEnabled(bool)
                IntPtr pSet = Marshal.ReadIntPtr(Marshal.ReadIntPtr(gpuScaling), 5 * IntPtr.Size);
                var setFn = (IntArgDelegate)Marshal.GetDelegateForFunctionPointer(pSet, typeof(IntArgDelegate));
                return setFn(gpuScaling, enable ? 1 : 0);
            }
            catch (Exception e)
            {
                Error = "SetEnabled 异常：" + e.Message;
                return 3; // ADLX_FAIL
            }
        }

        /// <summary>整数缩放：先开 GPU scaling，再开 integer scaling。</summary>
        static bool ApplyIntegerScaling(IntPtr gpuScaling, IntPtr intScaling, out string detail, out string actual)
        {
            detail = "";
            actual = "";
            if (intScaling == IntPtr.Zero)
            {
                detail = "该显示器不支持 ADLX 整数缩放接口。";
                return false;
            }

            // 整数缩放依赖 GPU scaling：必须先把 GPU scaling 打开（且等它生效），
            // 否则 IsSupported 恒为 false（文档明确：GPU scaling 关闭时整数缩放不支持）。
            bool gpuEnabled;
            ReadBool(gpuScaling, 4, out gpuEnabled);
            if (!gpuEnabled)
            {
                int rG = SetGpuScalingEnabled(gpuScaling, true);
                if (rG != 0)
                {
                    detail = "为整数缩放启用 GPU scaling 失败，返回码=" + rG + "（" + ResultName(rG) + "）";
                    return false;
                }
                if (!WaitForGpuScaling(gpuScaling, true, 3000))
                {
                    detail = "GPU scaling 已下发但未在 3 秒内生效，无法开整数缩放。";
                    return false;
                }
            }

            // 此时再查 IsSupported 才有意义
            bool supported;
            if (!ReadBool(intScaling, 3, out supported) || !supported)
            {
                detail = "该显示器/当前分辨率不支持整数缩放。";
                return false;
            }

            IntPtr pSet = Marshal.ReadIntPtr(Marshal.ReadIntPtr(intScaling), 5 * IntPtr.Size);
            var setFn = (IntArgDelegate)Marshal.GetDelegateForFunctionPointer(pSet, typeof(IntArgDelegate));
            int r = setFn(intScaling, 1);
            if (r != 0) { detail = "整数缩放 SetEnabled 失败，返回码=" + r + "（" + ResultName(r) + "）"; return false; }
            detail = "ADLX 整数缩放已开启（GPU scaling=开）";
            actual = "整数缩放";
            return true;
        }

        // =====================================================================
        //  自检 / 信息
        // =====================================================================

        internal static string CurrentScalingText()
        {
            if (!Ready) return "ADLX 不可用";
            try
            {
                IntPtr gpuScaling, scalingMode, intScaling;
                if (!GetSubInterfaces(out gpuScaling, out scalingMode, out intScaling)) return Error;

                var getMode = Vtbl<OutIntDelegate>(scalingMode, 4);
                int mode = -1;
                if (getMode != null) getMode(scalingMode, out mode);

                var gpuOn = Vtbl<OutIntDelegate>(gpuScaling, 4);
                int gpu = 0;
                if (gpuOn != null) gpuOn(gpuScaling, out gpu);

                return "ADLX mode=" + mode + "（" + ModeName(mode) + "），GPU scaling=" + (gpu != 0 ? "开" : "关");
            }
            catch (Exception e) { return "ADLX 读取失败：" + e.Message; }
        }

        static string ModeName(int m)
        {
            switch (m)
            {
                case ADLX_PRESERVE_ASPECT_RATIO: return "保持比例";
                case ADLX_FULL_PANEL: return "全屏铺满";
                case ADLX_CENTERED: return "居中";
                default: return "未知 " + m;
            }
        }

        internal static string SelfTestSection()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("  ADLX 桥接：" + (Ready ? "已就绪" : "不可用 —— " + Error));
            if (Ready)
            {
                sb.AppendLine("  显示器：" + (DisplayName.Length > 0 ? DisplayName : "(未知)"));
                sb.AppendLine("  当前缩放：" + CurrentScalingText());
                TryAppendCustomResolution(sb);
            }
            return sb.ToString();
        }

        // =====================================================================
        //  自定义分辨率（IADLXDisplayCustomResolution）
        // =====================================================================
        //
        // 结构体布局（ADLXStructures.h，已在本机用探针 dump 字节核对，68 字节）：
        //   ADLX_TimingInfo = 11 个连续 adlx_int，共 44 字节；
        //   ADLX_CustomResolution：
        //     int resWidth(0), resHeight(4), refreshRate(8),
        //     int presentation(12), int timingStandard(16),
        //     int GPixelClock(20)  —— adlx_long 在 Windows 是 4 字节 long，
        //     ADLX_TimingInfo detailedTiming(24)，共 68 字节。
        //
        // vtable（每个接口 [0][1][2] 为 IADLXInterface 基类 Acquire/Release/QI）：
        //   IADLXDisplayServices[18] = GetCustomResolution(display, IADLXDisplayCustomResolution**)
        //   IADLXDisplayCustomResolution：
        //     [3]IsSupported(adlx_bool*)  [4]GetResolutionList(…List**)
        //     [5]GetCurrentAppliedResolution(…Resolution**)
        //     [6]CreateNewResolution(…Resolution*)  [7]DeleteResolution(…Resolution*)
        //   IADLXDisplayResolution：
        //     [3]GetValue(ADLX_CustomResolution*)  [4]SetValue(ADLX_CustomResolution【值】)
        //     —— 68 字节结构按值传递，Windows x64 下实际走隐藏指针，C# 用 ref 即正确。
        //   IADLXDisplayResolutionList：
        //     [3]Size()  [5]Begin()  [11]At_DisplayResolutionList(loc, …Resolution**)
        //
        // 创建时序严格按官方 C++ 示例：取出当前应用分辨率 → GetValue → 只改
        // resWidth/resHeight 与 detailedTiming.hDisplay/vDisplay 四项 → SetValue →
        // CreateNewResolution；刷新率与时序沿用当前（timingStandard=MANUAL）。
        // 注意：复制 / Eyefinity 模式不支持自定义分辨率。

        [StructLayout(LayoutKind.Sequential)]
        struct AdlxTimingInfo
        {
            public int timingFlags;
            public int hTotal;
            public int vTotal;
            public int hDisplay;
            public int vDisplay;
            public int hFrontPorch;
            public int vFrontPorch;
            public int hSyncWidth;
            public int vSyncWidth;
            public int hPolarity;
            public int vPolarity;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct AdlxCustomResolutionStruct
        {
            public int resWidth;
            public int resHeight;
            public int refreshRate;
            public int presentation;
            public int timingStandard;
            public int GPixelClock;       // adlx_long = long = 4 字节（Windows LLP64）
            public AdlxTimingInfo detailedTiming;
        }

        /// <summary>给上层用的自定义分辨率信息（只读三要素）。</summary>
        internal struct AdlxCustomResolutionInfo
        {
            public int Width;
            public int Height;
            public int RefreshRate;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int ReleaseDelegate(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int ResolutionObjectDelegate(IntPtr self, IntPtr resolution);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int ResolutionGetValueDelegate(IntPtr self, out AdlxCustomResolutionStruct cr);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int ResolutionSetValueDelegate(IntPtr self, ref AdlxCustomResolutionStruct cr);

        const int SVC_GET_CUSTOM_RESOLUTION = 18;
        const int CR_IS_SUPPORTED = 3;
        const int CR_GET_RESOLUTION_LIST = 4;
        const int CR_GET_CURRENT_APPLIED = 5;
        const int CR_CREATE_NEW = 6;
        const int CR_DELETE = 7;
        const int RES_GET_VALUE = 3;
        const int RES_SET_VALUE = 4;
        const int RLIST_SIZE = 3;
        const int RLIST_AT_TYPED = 11;

        /// <summary>取出 IADLXDisplayCustomResolution 接口（已 AddRef），调用方负责 Release。</summary>
        static bool GetCustomResolutionInterface(out IntPtr customResolution)
        {
            customResolution = IntPtr.Zero;
            if (!Ready) return false;
            IntPtr pGet = Marshal.ReadIntPtr(
                Marshal.ReadIntPtr(_displayServices),
                SVC_GET_CUSTOM_RESOLUTION * IntPtr.Size);
            if (pGet == IntPtr.Zero) return false;
            var get = (DispOutDelegate)Marshal.GetDelegateForFunctionPointer(pGet, typeof(DispOutDelegate));
            try
            {
                int r = get(_displayServices, _display, out customResolution);
                return r == 0 && customResolution != IntPtr.Zero;
            }
            catch { return false; }
        }

        /// <summary>释放一个 ADLX 接口（忽略错误）。每个 Get/At 返回的接口对应一次。</summary>
        static void ReleaseInterface(IntPtr iface)
        {
            try
            {
                if (iface == IntPtr.Zero) return;
                var rel = Vtbl<ReleaseDelegate>(iface, 1);
                if (rel != null) rel(iface);
            }
            catch { }
        }

        /// <summary>查询该显示器是否支持自定义分辨率。</summary>
        internal static bool CustomResolutionSupported(out bool supported, out string detail)
        {
            supported = false;
            detail = "";
            if (!Ready) { detail = "ADLX 未初始化"; return false; }
            IntPtr cust;
            try
            {
                if (!GetCustomResolutionInterface(out cust))
                { detail = "取不到 IADLXDisplayCustomResolution 接口"; return false; }
                bool ok = ReadBool(cust, CR_IS_SUPPORTED, out supported);
                ReleaseInterface(cust);
                if (!ok) { detail = "IsSupported 查询失败"; return false; }
                if (!supported) detail = "该显示器当前不支持自定义分辨率（复制 / Eyefinity 模式或连接类型不支持）。";
                return true;
            }
            catch (Exception e) { detail = "查询异常：" + e.Message; return false; }
        }

        /// <summary>枚举已写入驱动的自定义分辨率。</summary>
        internal static List<AdlxCustomResolutionInfo> EnumerateCustomResolutions()
        {
            var result = new List<AdlxCustomResolutionInfo>();
            if (!Ready) return result;
            IntPtr cust;
            try
            {
                if (!GetCustomResolutionInterface(out cust)) return result;
                IntPtr list;
                var getList = Vtbl<OutPtrDelegate>(cust, CR_GET_RESOLUTION_LIST);
                if (getList == null || getList(cust, out list) != 0 || list == IntPtr.Zero)
                { ReleaseInterface(cust); return result; }

                var sizeFn = Vtbl<SizeMethodDelegate>(list, RLIST_SIZE);
                uint n = sizeFn == null ? 0 : (uint)sizeFn(list).ToUInt32();
                var at = Vtbl<AtDelegate>(list, RLIST_AT_TYPED);
                for (uint i = 0; i < n; i++)
                {
                    IntPtr res;
                    if (at != null && at(list, i, out res) == 0 && res != IntPtr.Zero)
                    {
                        var gv = Vtbl<ResolutionGetValueDelegate>(res, RES_GET_VALUE);
                        AdlxCustomResolutionStruct cr;
                        if (gv != null && gv(res, out cr) == 0)
                        {
                            result.Add(new AdlxCustomResolutionInfo
                            { Width = cr.resWidth, Height = cr.resHeight, RefreshRate = cr.refreshRate });
                        }
                        ReleaseInterface(res);
                    }
                }
                ReleaseInterface(list);
                ReleaseInterface(cust);
            }
            catch { }
            return result;
        }

        /// <summary>
        /// 创建自定义分辨率并写入驱动（注册到系统可选列表，不立即切换当前显示）。
        /// 刷新率沿用当前应用模式（官方示例路径），通过 refreshRate 回传实际刷新率。
        /// </summary>
        internal static bool CreateCustomResolution(int width, int height, out int refreshRate, out string detail)
        {
            refreshRate = 0;
            detail = "";
            if (!Ready) { detail = "ADLX 未初始化"; return false; }
            if (width < 1 || height < 1) { detail = "宽高无效"; return false; }

            IntPtr cust;
            IntPtr res = IntPtr.Zero;
            try
            {
                if (!GetCustomResolutionInterface(out cust))
                { detail = "取不到自定义分辨率接口"; return false; }

                bool supported;
                if (!ReadBool(cust, CR_IS_SUPPORTED, out supported) || !supported)
                {
                    detail = "该显示器当前不支持自定义分辨率（复制 / Eyefinity 模式或连接类型不支持）。";
                    ReleaseInterface(cust);
                    return false;
                }

                // 已存在相同分辨率则不重复创建
                var existing = EnumerateCustomResolutions();
                foreach (var e in existing)
                {
                    if (e.Width == width && e.Height == height)
                    {
                        detail = "该自定义分辨率已存在：" + width + "x" + height + "@" + e.RefreshRate + "Hz";
                        refreshRate = e.RefreshRate;
                        ReleaseInterface(cust);
                        return false;
                    }
                }

                // 取当前应用分辨率作为时序基准（官方示例路径）
                var getCur = Vtbl<OutPtrDelegate>(cust, CR_GET_CURRENT_APPLIED);
                if (getCur == null || getCur(cust, out res) != 0 || res == IntPtr.Zero)
                { detail = "取不到当前应用分辨率"; ReleaseInterface(cust); return false; }

                var gv = Vtbl<ResolutionGetValueDelegate>(res, RES_GET_VALUE);
                AdlxCustomResolutionStruct cr;
                if (gv == null || gv(res, out cr) != 0)
                { detail = "读取当前分辨率时序失败"; ReleaseInterface(res); ReleaseInterface(cust); return false; }

                cr.resWidth = width;
                cr.resHeight = height;
                cr.detailedTiming.hDisplay = width;
                cr.detailedTiming.vDisplay = height;
                // 其余字段（刷新率、时序标准、pixel clock、porch、极性）严格沿用当前值
                refreshRate = cr.refreshRate;

                var sv = Vtbl<ResolutionSetValueDelegate>(res, RES_SET_VALUE);
                if (sv == null)
                { detail = "取不到 SetValue"; ReleaseInterface(res); ReleaseInterface(cust); return false; }
                int rSet = sv(res, ref cr);
                if (rSet != 0)
                {
                    detail = "SetValue 失败，返回码=" + rSet + "（" + ResultName(rSet) + "）";
                    ReleaseInterface(res); ReleaseInterface(cust); return false;
                }

                var create = Vtbl<ResolutionObjectDelegate>(cust, CR_CREATE_NEW);
                int rCreate = create == null ? 3 : create(cust, res);
                ReleaseInterface(res);
                ReleaseInterface(cust);
                if (rCreate != 0)
                {
                    detail = "CreateNewResolution 失败，返回码=" + rCreate + "（" + ResultName(rCreate)
                        + "）。显示器可能不接受该分辨率时序，可换一个更接近标准的尺寸。";
                    return false;
                }

                Thread.Sleep(600); // 等驱动把新模式注册到系统
                detail = "已创建自定义分辨率 " + width + "x" + height + "@" + refreshRate + "Hz";
                return true;
            }
            catch (Exception e)
            {
                if (res != IntPtr.Zero) ReleaseInterface(res);
                detail = "创建自定义分辨率异常：" + e.GetType().Name + " " + e.Message;
                return false;
            }
        }

        /// <summary>删除指定宽高的自定义分辨率（删除不存在的返回 false 并说明）。</summary>
        internal static bool DeleteCustomResolution(int width, int height, out string detail)
        {
            detail = "";
            if (!Ready) { detail = "ADLX 未初始化"; return false; }
            IntPtr cust;
            try
            {
                if (!GetCustomResolutionInterface(out cust))
                { detail = "取不到自定义分辨率接口"; return false; }
                IntPtr list;
                var getList = Vtbl<OutPtrDelegate>(cust, CR_GET_RESOLUTION_LIST);
                if (getList == null || getList(cust, out list) != 0 || list == IntPtr.Zero)
                { detail = "取不到自定义分辨率列表"; ReleaseInterface(cust); return false; }

                var sizeFn = Vtbl<SizeMethodDelegate>(list, RLIST_SIZE);
                uint n = sizeFn == null ? 0 : (uint)sizeFn(list).ToUInt32();
                var at = Vtbl<AtDelegate>(list, RLIST_AT_TYPED);
                bool deleted = false;
                for (uint i = 0; i < n; i++)
                {
                    IntPtr res;
                    if (at != null && at(list, i, out res) == 0 && res != IntPtr.Zero)
                    {
                        var gv = Vtbl<ResolutionGetValueDelegate>(res, RES_GET_VALUE);
                        AdlxCustomResolutionStruct cr;
                        bool match = gv != null && gv(res, out cr) == 0
                            && cr.resWidth == width && cr.resHeight == height;
                        if (match)
                        {
                            var del = Vtbl<ResolutionObjectDelegate>(cust, CR_DELETE);
                            int rDel = del == null ? 3 : del(cust, res);
                            if (rDel == 0) deleted = true;
                            else detail = "删除失败，返回码=" + rDel + "（" + ResultName(rDel) + "）";
                            ReleaseInterface(res);
                            break;
                        }
                        ReleaseInterface(res);
                    }
                }
                ReleaseInterface(list);
                ReleaseInterface(cust);
                if (!deleted && detail.Length == 0) detail = "未找到 " + width + "x" + height + " 的自定义分辨率";
                return deleted;
            }
            catch (Exception e)
            { detail = "删除异常：" + e.Message; return false; }
        }

        /// <summary>自检段附加一行自定义分辨率支持情况。</summary>
        static void TryAppendCustomResolution(StringBuilder sb)
        {
            try
            {
                bool supported;
                string detail;
                if (!CustomResolutionSupported(out supported, out detail))
                {
                    sb.AppendLine("  自定义分辨率：未知（" + detail + "）");
                    return;
                }
                if (supported)
                {
                    var list = EnumerateCustomResolutions();
                    sb.AppendLine("  自定义分辨率：支持（当前已创建 " + list.Count + " 个）");
                }
                else
                {
                    sb.AppendLine("  自定义分辨率：不支持（" + detail + "）");
                }
            }
            catch { }
        }
    }
}
