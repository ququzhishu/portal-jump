using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace PortalJump
{
    /// <summary>
    /// 跨厂商显示配置后端（Windows CCD API + Win32 DEVMODE）。
    ///
    /// 这是解决"只能 NVIDIA 用"的核心：
    ///  - 缩放走 DEVMODE.dmDisplayFixedOutput（DMDFO_STRETCH / DMDFO_CENTER）。
    ///    这是 Win32 从 XP 时代就有的标准字段，由**操作系统**转达给显示驱动，
    ///    不依赖 NVAPI，AMD / Intel / 任何 WDDM 驱动都能用。
    ///    实测（本机 RTX 4060 Ti）：STRETCH / CENTER 都能真实生效并回读一致。
    ///  - 分辨率走 EnumDisplaySettings / ChangeDisplaySettingsEx，本来就是通用的。
    ///  - 附带 CCD API 读取更精确的当前缩放状态，用于自检与诊断。
    ///
    /// 注意：NVIDIA 驱动会把"非原生分辨率的呈现方式"接管到自己的 NVAPI 通道，
    /// 所以 NVIDIA 机器上 DMDFO 的效果可能被驱动设置覆盖；此时由 NvapiBackend 优先处理，
    /// 本后端作为兜底（并在自检里如实报告两者是否一致）。
    /// </summary>
    public static class DisplayConfigBackend
    {
        // ---------------- DEVMODE（DEVMODEA，156 字节） ----------------

        // 字段必须与 wingdi.h 的 DEVMODEA 严格一致：
        // 32+2*4+4+4*2+4*2+2*5+32+2+4*5+4*4+4*2+4*2 = 156。
        // 旧版在 dmReserved2 后面多写了一个 dmReserved3，把结构撑到 160 字节；
        // 虽然 dmSize 里写的是 160、系统只读前 156，多数情况不出错，
        // 但这是明确的越界布局，已修正。
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsA", CharSet = CharSet.Ansi)]
        static extern bool EnumDisplaySettingsA(string deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExA", CharSet = CharSet.Ansi)]
        static extern int ChangeDisplaySettingsExA(string deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesA", CharSet = CharSet.Ansi)]
        static extern bool EnumDisplayDevicesA(string device, uint devNum, ref DISPLAY_DEVICE displayDevice, uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        const int ENUM_CURRENT_SETTINGS = -1;
        public const uint CDS_UPDATEREGISTRY = 0x00000001;
        public const uint CDS_TEST = 0x00000002;
        public const uint CDS_FULLSCREEN = 0x00000004;
        public const uint CDS_GLOBAL = 0x00000008;
        public const uint CDS_NORESET = 0x10000000;

        // dmFields 位
        const int DM_POSITION = 0x00000020;
        const int DM_BITSPERPEL = 0x00040000;
        const int DM_PELSWIDTH = 0x00080000;
        const int DM_PELSHEIGHT = 0x00100000;
        const int DM_DISPLAYFREQUENCY = 0x00400000;
        const int DM_DISPLAYFIXEDOUTPUT = 0x20000000;

        // dmDisplayFixedOutput 取值（DMDFO_*）
        public const int DMDFO_DEFAULT = 0;
        public const int DMDFO_STRETCH = 1;
        public const int DMDFO_CENTER = 2;

        // DISPLAY_DEVICE.StateFlags
        const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
        const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;

        // ---------------- CCD（读取更精确的状态） ----------------

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RATIONAL { public uint Numerator; public uint Denominator; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTL { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct REGION2D { public uint cx; public uint cy; }

        [StructLayout(LayoutKind.Sequential)]
        public struct PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            // Win10 是 modeInfoIdx；Win11 22H2 起是 cloneGroupId/sourceModeInfoIdx 的位域并集。
            // 两者都是 4 字节，按原值透传即可，不要自行解释。
            public uint modeInfoIdx;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint outputTechnology;
            public uint rotation;
            public uint scaling;
            public RATIONAL refreshRate;
            public uint scanLineOrdering;
            [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PATH_INFO
        {
            public PATH_SOURCE_INFO sourceInfo;
            public PATH_TARGET_INFO targetInfo;
            // 末尾这个 flags 不能漏：少了它结构是 68 字节，而系统按 72 字节写，
            // 会造成 STATUS_HEAP_CORRUPTION 直接崩进程（实测踩过）。
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VIDEO_SIGNAL_INFO
        {
            public ulong pixelRate;
            public RATIONAL hSyncFreq;
            public RATIONAL vSyncFreq;
            public REGION2D activeSize;
            public REGION2D totalSize;
            public uint videoStandard;
            public uint scanLineOrdering;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TARGET_MODE { public VIDEO_SIGNAL_INFO targetVideoSignalInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SOURCE_MODE
        {
            public uint width;
            public uint height;
            public uint pixelFormat;
            public POINTL position;
        }

        // DISPLAYCONFIG_MODE_INFO 是 64 字节：type(4)+id(4)+adapterId(8)+union(48)
        [StructLayout(LayoutKind.Explicit, Size = 64)]
        public struct MODE_INFO
        {
            [FieldOffset(0)] public uint infoType;
            [FieldOffset(4)] public uint id;
            [FieldOffset(8)] public LUID adapterId;
            [FieldOffset(16)] public TARGET_MODE targetMode;
            [FieldOffset(16)] public SOURCE_MODE sourceMode;
        }

        [DllImport("user32.dll")]
        static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements,
            [Out] PATH_INFO[] pathInfoArray, ref uint numModeInfoArrayElements,
            [Out] MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        static extern int SetDisplayConfig(uint numPathArrayElements, [In] PATH_INFO[] pathArray,
            uint numModeInfoArrayElements, [In] MODE_INFO[] modeInfoArray, uint flags);

        const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002; // = QDC_VIRTUAL_MODE_AWARE
        const uint PATH_ACTIVE = 0x00000001;

        // SetDisplayConfig 的 flags（见 winuser.h / MSDN）
        const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
        const uint SDC_APPLY = 0x00000080;
        const uint SDC_ALLOW_CHANGES = 0x00000400;

        // ---------------- 基础读取 ----------------

        public static int DevModeSize { get { return Marshal.SizeOf(typeof(DEVMODE)); } }

        public static DEVMODE NewDevMode()
        {
            var dm = new DEVMODE();
            dm.dmSize = (short)DevModeSize;
            return dm;
        }

        /// <summary>当前主显示器模式</summary>
        public static DEVMODE GetCurrentMode()
        {
            DEVMODE dm = NewDevMode();
            EnumDisplaySettingsA(null, ENUM_CURRENT_SETTINGS, ref dm);
            return dm;
        }

        /// <summary>枚举主显示器上的全部模式</summary>
        public static List<DEVMODE> EnumAllModes()
        {
            var list = new List<DEVMODE>();
            for (int i = 0; ; i++)
            {
                DEVMODE dm = NewDevMode();
                if (!EnumDisplaySettingsA(null, i, ref dm)) break;
                if (dm.dmPelsWidth <= 0 || dm.dmPelsHeight <= 0) continue;
                list.Add(dm);
                if (i > 100000) break; // 防御：驱动返回异常枚举时不死循环
            }
            return list;
        }

        /// <summary>该分辨率下的全部模式，优先 32 位色，按刷新率降序</summary>
        public static List<DEVMODE> FindModes(int w, int h)
        {
            var deep = new List<DEVMODE>();
            var any = new List<DEVMODE>();
            foreach (DEVMODE dm in EnumAllModes())
            {
                if (dm.dmPelsWidth != w || dm.dmPelsHeight != h) continue;
                if (dm.dmBitsPerPel == 32) deep.Add(dm);
                else any.Add(dm);
            }
            List<DEVMODE> list = deep.Count > 0 ? deep : any;
            list.Sort(delegate(DEVMODE a, DEVMODE b)
            {
                return b.dmDisplayFrequency.CompareTo(a.dmDisplayFrequency);
            });
            return list;
        }

        /// <summary>去重后的分辨率列表（同宽高保留最高刷新率），按像素数降序</summary>
        public static List<DisplayMode> GetResolutions()
        {
            var bySize = new Dictionary<string, DisplayMode>();
            foreach (DEVMODE dm in EnumAllModes())
            {
                string k = dm.dmPelsWidth + "x" + dm.dmPelsHeight;
                DisplayMode cur;
                if (bySize.TryGetValue(k, out cur))
                {
                    if (dm.dmDisplayFrequency > cur.MaxHz) { cur.MaxHz = dm.dmDisplayFrequency; bySize[k] = cur; }
                }
                else
                {
                    var m = new DisplayMode();
                    m.Width = dm.dmPelsWidth;
                    m.Height = dm.dmPelsHeight;
                    m.MaxHz = dm.dmDisplayFrequency;
                    m.ColorDepth = dm.dmBitsPerPel;
                    bySize[k] = m;
                }
            }
            var list = new List<DisplayMode>(bySize.Values);
            list.Sort(delegate(DisplayMode a, DisplayMode b)
            {
                long pa = (long)a.Width * a.Height;
                long pb = (long)b.Width * b.Height;
                if (pa != pb) return pb.CompareTo(pa);
                if (a.Width != b.Width) return b.Width.CompareTo(a.Width);
                return b.Height.CompareTo(a.Height);
            });
            return list;
        }

        // ---------------- 应用模式（含缩放） ----------------

        /// <summary>
        /// 把 DEVMODE 的 dmDisplayFixedOutput 设成指定缩放，并标好 dmFields。
        /// 这是跨厂商缩放的唯一入口。
        /// </summary>
        public static void SetFixedOutput(ref DEVMODE dm, ScalingMode mode)
        {
            int value;
            switch (mode)
            {
                case ScalingMode.Centered:
                case ScalingMode.Aspect:      // 保持比例也由"不拉伸"实现，黑边交给显示器/GPU 决定
                    value = DMDFO_CENTER;
                    break;
                case ScalingMode.FullScreen:
                case ScalingMode.Integer:
                    value = DMDFO_STRETCH;
                    break;
                default:
                    return; // Unchanged：不动
            }
            dm.dmFields |= DM_DISPLAYFIXEDOUTPUT;
            dm.dmDisplayFixedOutput = value;
        }

        /// <summary>
        /// 应用一个模式，并可选地同时设置缩放。
        /// 返回 ChangeDisplaySettingsEx 的原始结果码（0 = 成功）。
        /// </summary>
        public static int ApplyMode(DEVMODE dm, ScalingMode scaling)
        {
            dm.dmSize = (short)DevModeSize;
            SetFixedOutput(ref dm, scaling);
            return ChangeDisplaySettingsExA(null, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        }

        /// <summary>只做校验、不真正切换（CDS_TEST）</summary>
        public static int TestMode(DEVMODE dm, ScalingMode scaling)
        {
            dm.dmSize = (short)DevModeSize;
            SetFixedOutput(ref dm, scaling);
            return ChangeDisplaySettingsExA(null, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
        }

        /// <summary>
        /// 只改缩放、不动分辨率：以当前模式为底，改 dmDisplayFixedOutput 后提交。
        /// 在"分辨率已经是对的、只需调缩放"的场景下比整套重设更稳。
        /// </summary>
        public static int ApplyScalingOnly(ScalingMode mode)
        {
            DEVMODE dm = GetCurrentMode();
            if (dm.dmPelsWidth <= 0) return -1;
            dm.dmSize = (short)DevModeSize;
            SetFixedOutput(ref dm, mode);
            return ChangeDisplaySettingsExA(null, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        }

        /// <summary>回读当前 dmDisplayFixedOutput</summary>
        public static int GetCurrentFixedOutput()
        {
            DEVMODE dm = GetCurrentMode();
            return dm.dmDisplayFixedOutput;
        }

        // ---------------- 诊断 ----------------

        public static string DescribeResult(int code)
        {
            switch (code)
            {
                case 0: return "成功";
                case 1: return "需要重启才能生效";
                case -1: return "切换失败（驱动返回一般性错误）";
                case -2: return "该模式不被显示器/驱动支持";
                case -3: return "驱动未能更新注册表（可能被其他程序占用）";
                case -4: return "模式标志无效";
                case -5: return "参数无效";
                case -6: return "该模式是双视模式，必须与另一个模式一起使用";
                default: return "未知结果码 " + code;
            }
        }

        public static string FixedOutputName(int v)
        {
            switch (v)
            {
                case DMDFO_DEFAULT: return "默认（由驱动/显示器决定）";
                case DMDFO_STRETCH: return "拉伸铺满";
                case DMDFO_CENTER: return "居中不缩放";
                default: return "未知值 " + v;
            }
        }

        /// <summary>
        /// 列出已连接到桌面的显示适配器名字（用于界面显示）。
        ///
        /// 注意：这里取的是 DISPLAY_DEVICE.DeviceString（驱动报告的设备串），
        /// **不是** WMI 的 Win32_VideoController.Name。两者可能不一致 ——
        /// 实测本机用户为了游戏改过系统显示名，WMI 报 "GTX 1050 Ti"，
        /// 但 DeviceString 仍是真实的 "RTX 4060 Ti"。判厂商请用 DetectVendor()。
        /// </summary>
        public static List<string> GetAdapterNames(bool includeDetached = false)
        {
            var names = new List<string>();
            var dd = new DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            for (uint i = 0; EnumDisplayDevicesA(null, i, ref dd, 0); i++)
            {
                // 只要真正挂在桌面上的适配器；虚拟显示驱动（如向日葵 OrayIddDriver）
                // 通常没有 ATTACHED_TO_DESKTOP 标志，会被自动排除。
                //
                // includeDetached=true 时放宽这个条件：启动早期可能一个都没有 attached
                // （见 GetAdapterVendorIds 的说明）。此时靠名字兜底也比"未知厂商"强，
                // 调用方需自行过滤掉已知的虚拟显示驱动名。
                bool attached = (dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0;
                if ((attached || includeDetached)
                    && !string.IsNullOrEmpty(dd.DeviceString)
                    && !names.Contains(dd.DeviceString))
                {
                    names.Add(dd.DeviceString);
                }
                dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            }
            return names;
        }

        /// <summary>
        /// 列出适配器的 PCI 厂商 ID（形如 "VEN_10DE"）。这是硬件层信息，改显示名改不掉它。
        ///
        /// includeDetached 参数很关键：
        /// 正常情况只统计 ATTACHED_TO_DESKTOP 的适配器；但实测存在**启动早期枚举不到**
        /// 的情况（本机 2026-10-23 16:42 那次启动，14 个适配器一个都没置 attached 标志），
        /// 此时会返回空列表，导致厂商识别成「未知」。带 includeDetached 再取一次，
        /// 就能从硬件 ID 拿到真实厂商 —— 虚拟显示驱动（Root\OrayIddDriver）没有 VEN_ 段，
        /// 不会被误判，所以放宽这个条件不会引入错误识别。
        /// </summary>
        public static List<string> GetAdapterVendorIds(bool includeDetached = false)
        {
            var ids = new List<string>();
            var dd = new DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            for (uint i = 0; EnumDisplayDevicesA(null, i, ref dd, 0); i++)
            {
                bool attached = (dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0;
                if ((attached || includeDetached)
                    && !string.IsNullOrEmpty(dd.DeviceID))
                {
                    // DeviceID 形如：
                    // PCI\VEN_10DE&DEV_2803&SUBSYS_28051ED3&REV_A1\4&142C1193&0&0019
                    // 取出 VEN_xxxx 这一段
                    string id = dd.DeviceID.ToUpperInvariant();
                    int p = id.IndexOf("VEN_", StringComparison.Ordinal);
                    if (p >= 0 && p + 8 <= id.Length)
                    {
                        string ven = id.Substring(p, 8);
                        if (!ids.Contains(ven)) ids.Add(ven);
                    }
                }
                dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            }
            return ids;
        }

        /// <summary>
        /// 判断显卡厂商。
        ///
        /// 为什么优先用 PCI 厂商 ID 而不是名字：
        /// 名字可以被改（实测本机为提升游戏画质改过显示名，WMI 报 GTX 1050 Ti，
        /// 真实是 RTX 4060 Ti）。而 VEN_10DE / VEN_1002 / VEN_8086 是硬件 ID，
        /// 改显示名改不掉，所以更可靠。名字只作为 ID 缺失时的兜底。
        ///
        /// 【重要】必须容忍枚举失败：
        /// 实测本机 2026-10-23 两次启动同一份程序，04:12 识别为 NVIDIA，
        /// 16:42 识别为「未知厂商（）」—— 后者导致缩放从 NVAPI 错退到 Windows 原生接口，
        /// 而原生接口在 N 卡上写不进缩放，进而触发"降刷新率重试"把屏幕拖到 23Hz。
        /// 所以这里：先重试几轮（枚举有时序抖动），仍失败就**放宽 attached 条件**按硬件 ID 判，
        /// 最后才用名字猜。宁可多试几次，也不能把 N 卡误判成未知。
        /// </summary>
        public static GpuVendor DetectVendor()
        {
            // 1) 首选：PCI 厂商 ID（硬件层，改不掉）。重试 3 轮，每轮 120ms。
            //    启动早期 EnumDisplayDevices 可能返回空，等一会儿通常就有了。
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    GpuVendor v = MatchVendorIds(GetAdapterVendorIds());
                    if (v != GpuVendor.Unknown) return v;
                }
                catch { }
                if (attempt < 2) System.Threading.Thread.Sleep(120);
            }

            // 2) 仍没识别出来：放宽 attached 条件再取一次硬件 ID。
            //    虚拟显示驱动（Root\OrayIddDriver）的 DeviceID 里没有 VEN_ 段，
            //    所以放宽不会把它误认成显卡厂商。
            try
            {
                GpuVendor v = MatchVendorIds(GetAdapterVendorIds(true));
                if (v != GpuVendor.Unknown) return v;
            }
            catch { }

            // 3) 最后兜底：按适配器名字猜（同时放宽 attached）
            List<string> names = GetAdapterNames(true);
            bool nv = false, amd = false, intel = false;
            foreach (string n in names)
            {
                string s = n.ToLowerInvariant();
                // 排除虚拟显示驱动，避免把 "OrayIddDriver Device" 当成真实显卡
                if (s.Contains("oray") || s.Contains("virtual") || s.Contains("idd")) continue;
                if (s.Contains("nvidia") || s.Contains("geforce") || s.Contains("quadro") || s.Contains("rtx")) nv = true;
                else if (s.Contains("amd") || s.Contains("radeon") || s.Contains("ati ")) amd = true;
                else if (s.Contains("intel") || s.Contains("arc") || s.Contains("iris") || s.Contains("uhd")) intel = true;
            }
            // 混合显卡（笔记本独显+核显）时优先报独显厂商
            if (nv) return GpuVendor.Nvidia;
            if (amd) return GpuVendor.Amd;
            if (intel) return GpuVendor.Intel;
            return GpuVendor.Unknown;
        }

        static GpuVendor MatchVendorIds(List<string> vids)
        {
            foreach (string v in vids)
            {
                if (v == "VEN_10DE") return GpuVendor.Nvidia;   // NVIDIA
                if (v == "VEN_1002") return GpuVendor.Amd;      // AMD / ATI
                if (v == "VEN_8086") return GpuVendor.Intel;    // Intel
            }
            return GpuVendor.Unknown;
        }

        /// <summary>读当前 CCD 缩放值（仅用于诊断；失败返回 -1）</summary>
        public static int QueryCcdScaling()
        {
            try
            {
                uint np, nm;
                int e = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out np, out nm);
                if (e != 0 || np == 0) return -1;
                var paths = new PATH_INFO[np];
                var modes = new MODE_INFO[nm];
                e = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero);
                if (e != 0) return -1;
                for (int i = 0; i < np; i++)
                {
                    if ((paths[i].flags & PATH_ACTIVE) == 0) continue;
                    return (int)paths[i].targetInfo.scaling;
                }
            }
            catch { }
            return -1;
        }

        /// <summary>
        /// 用 Windows 原生 CCD API 把缩放设成指定值（不做拓扑变更，只改 scaling）。
        ///
        /// 为什么需要它：
        /// 实测发现 dmDisplayFixedOutput **只在 CCD 侧缩放为 IDENTITY(1) 或 PREFERRED(128) 时才被驱动采纳**。
        /// 如果 CCD 停在 ASPECTRATIO(4)、STRETCHED(3) 之类的值上，
        /// ChangeDisplaySettingsEx 会返回 0 但 dmDisplayFixedOutput 恒为 0（不生效）。
        /// 所以通用后端在设缩放前，必须先把 CCD 侧"让位"成 IDENTITY。
        ///
        /// 注意：NVIDIA 驱动会拦截 CCD 的缩放写入（返回 0 但值不变），
        /// 所以本方法在 NVIDIA 上可能无效 —— 此时调用方应回退到 NVAPI。
        /// </summary>
        public static bool TrySetCcdScaling(uint scaling)
        {
            try
            {
                uint np, nm;
                int e = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out np, out nm);
                if (e != 0 || np == 0) return false;
                var paths = new PATH_INFO[np];
                var modes = new MODE_INFO[nm];
                e = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero);
                if (e != 0) return false;

                bool changed = false;
                for (int i = 0; i < np; i++)
                {
                    if ((paths[i].flags & PATH_ACTIVE) == 0) continue;
                    if (paths[i].targetInfo.scaling == scaling) continue;
                    paths[i].targetInfo.scaling = scaling;
                    changed = true;
                }
                if (!changed) return true;

                e = SetDisplayConfig(np, paths, nm, modes,
                    SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES);
                return e == 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// CCD 缩放值是否"会让位给 dmDisplayFixedOutput"。
        /// IDENTITY(1) 与 PREFERRED(128) 会让位；其它值（2/3/4/5）会压制 DMDFO。
        /// </summary>
        public static bool CcdScalingAllowsFixedOutput(int ccd)
        {
            return ccd < 0 || ccd == 1 || ccd == 128;
        }
    }

    /// <summary>显卡厂商</summary>
    public enum GpuVendor
    {
        Unknown = 0,
        Nvidia = 1,
        Amd = 2,
        Intel = 3
    }
}
