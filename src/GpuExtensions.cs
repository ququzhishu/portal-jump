using System;

namespace PortalJump
{
    /// <summary>
    /// 显卡专有能力的统一入口 —— **这是 NVAPI 与"无 NVAPI 版本"之间唯一的接缝**。
    ///
    /// 折返跃迁出三个版本，共用同一套源码，差别只在编译符号：
    ///
    ///   · **NVIDIA 版**（`/define:WITH_NVAPI`）
    ///     带 NvAPIWrapper.dll。缩放走 NVAPI，支持全部 8 种缩放方式、整数缩放、自定义分辨率。
    ///
    ///   · **通用版**（不定义符号）
    ///     不引用 NvAPIWrapper.dll。缩放走 Windows 原生 `dmDisplayFixedOutput`，
    ///     在 AMD / Intel 上可用；NVIDIA 上该字段只读，会失败。
    ///
    ///   · **仅分辨率版**（`/define:RESOLUTION_ONLY`）
    ///     **完全不碰缩放**，只切分辨率。缩放方式由用户自己在显卡驱动面板里设好，
    ///     程序切换分辨率时驱动会自动沿用那个设置。
    ///     这个版本零厂商依赖、任何显卡都能用、也最不容易出问题。
    ///
    /// 三者的能力由本类统一暴露，Switcher / 界面层只跟这里打交道。
    /// </summary>
    public static class GpuExtensions
    {
#if RESOLUTION_ONLY
        /// <summary>本版本是否编入了 NVAPI 支持</summary>
        public const bool Compiled = false;

        /// <summary>本版本是否只做分辨率、不碰缩放</summary>
        public const bool ResolutionOnly = true;

        /// <summary>本版本名称（界面/自检显示）</summary>
        public const string EditionName = "仅分辨率版";

        /// <summary>本版本支持的能力说明</summary>
        public const string EditionFeatures =
            "只切换分辨率，不改缩放方式。任何显卡通用，缩放请在显卡驱动面板里手动设置一次。";
#elif UNIFIED
        /// <summary>本版本是否编入了 NVAPI 支持（双桥接：N 卡走 NVAPI，A 卡走 ADLX）</summary>
        public const bool Compiled = true;

        /// <summary>本版本是否只做分辨率、不碰缩放</summary>
        public const bool ResolutionOnly = false;

        /// <summary>本版本名称（界面/自检显示）</summary>
        public const string EditionName = "合并版";

        /// <summary>
        /// 本版本支持的能力说明。
        /// 【合并版】同时编入 NVAPI 与 ADLX 两套桥接，**具体走哪条路由运行时厂商识别决定**，
        /// 所以这里只描述"可能的能力"，实际可用性见 Switcher 的运行时判断。
        /// </summary>
        public const string EditionFeatures =
            "自动识别显卡：NVIDIA 走 NVAPI，AMD 走官方 ADLX；两家的整数缩放与创建自定义分辨率均支持。"
            + "识别不出专有接口时回退 Windows 原生接口做基础缩放。";
#elif WITH_NVAPI
        /// <summary>本版本是否编入了 NVAPI 支持</summary>
        public const bool Compiled = true;

        /// <summary>本版本是否只做分辨率、不碰缩放</summary>
        public const bool ResolutionOnly = false;

        /// <summary>本版本名称（界面/自检显示）</summary>
        public const string EditionName = "NVIDIA 版";

        /// <summary>本版本支持的能力说明</summary>
        public const string EditionFeatures = "缩放走 NVAPI，支持整数缩放与创建自定义分辨率";
#else
        /// <summary>本版本是否编入了 NVAPI 支持</summary>
        public const bool Compiled = false;

        /// <summary>本版本是否只做分辨率、不碰缩放</summary>
        public const bool ResolutionOnly = false;

        /// <summary>本版本名称（界面/自检显示）</summary>
        public const string EditionName = "通用版";

        /// <summary>本版本支持的能力说明</summary>
        public const string EditionFeatures =
            "AMD 显卡：缩放走 AMD 官方 ADLX，支持整数缩放与创建自定义分辨率；其余显卡走 Windows 原生接口，仅基础缩放";
#endif

        /// <summary>当前版本是否支持"设置缩放方式"</summary>
        public static bool SupportsScaling
        {
            get
            {
                return !ResolutionOnly;
            }
        }

        /// <summary>当前版本是否支持"整数缩放"</summary>
        public static bool SupportsIntegerScaling
        {
            get
            {
                return Compiled;
            }
        }

        /// <summary>当前版本是否支持"创建自定义分辨率"</summary>
        public static bool SupportsCustomResolution
        {
            get
            {
                return Compiled;
            }
        }

        /// <summary>
        /// 该功能不可用时给用户看的原因（可用时返回空串）。
        /// 统一文案，避免界面上各处说法不一致。
        /// </summary>
        public static string UnavailableReason(string feature)
        {
            if (ResolutionOnly)
            {
                return "「" + feature + "」在【仅分辨率版】里不提供 —— 这个版本只负责切换分辨率，"
                    + "不碰缩放设置。\n\n"
                    + "请在显卡驱动控制面板里把缩放方式设置一次（例如 NVIDIA 控制面板 → 调整桌面尺寸和位置，"
                    + "或 AMD Software → 显示 → 缩放模式），之后切换分辨率时会自动沿用。\n\n"
                    + "需要程序帮你切缩放，请改用完整版。";
            }
            if (Compiled) return "";
            return "「" + feature + "」需要 NVIDIA 驱动专有接口（NVAPI）。"
                + "当前运行的是通用版（面向 AMD 等非 NVIDIA 显卡），"
                + "这个版本没有打包 NVAPI。\n\n"
                + "如果你用的是 NVIDIA 显卡，请改用 NVIDIA 版。";
        }
    }
}
