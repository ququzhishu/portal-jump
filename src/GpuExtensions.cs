using System;

namespace PortalJump
{
    /// <summary>
    /// 显卡专有能力的统一入口 —— **这是 NVAPI 与"无 NVAPI 版本"之间唯一的接缝**。
    ///
    /// 为什么要这一层：
    /// 折返跃迁现在出两个版本：
    ///   · NVIDIA 版（PortalJump）：带 NvAPIWrapper.dll，缩放走 NVAPI，支持整数缩放与自定义分辨率。
    ///   · 通用版（PortalJump-AMD/Intel）：**不引用 NvAPIWrapper.dll**，缩放走 Windows 原生字段，
    ///     整数缩放与自定义分辨率这两项如实置灰。
    ///
    /// 两个版本的差别只体现在一个编译符号上（见下方条件编译）：
    ///   定义 WITH_NVAPI → 真实实现（调用 NvAPIWrapper）
    ///   未定义         → 空实现（所有能力报告"不可用"，绝不抛异常、绝不崩溃）
    ///
    /// 这样 Switcher / 界面层都只跟本类打交道，不需要各自写一堆 #if。
    /// </summary>
    public static class GpuExtensions
    {
#if WITH_NVAPI
        /// <summary>本版本是否编入了 NVAPI 支持</summary>
        public const bool Compiled = true;

        /// <summary>本版本名称（界面/自检显示）</summary>
        public const string EditionName = "NVIDIA 版";

        /// <summary>本版本支持的能力说明</summary>
        public const string EditionFeatures = "缩放走 NVAPI，支持整数缩放与创建自定义分辨率";
#else
        /// <summary>本版本是否编入了 NVAPI 支持</summary>
        public const bool Compiled = false;

        /// <summary>本版本名称（界面/自检显示）</summary>
        public const string EditionName = "通用版";

        /// <summary>本版本支持的能力说明</summary>
        public const string EditionFeatures = "缩放走 Windows 原生接口，兼容 AMD / Intel / NVIDIA；整数缩放与自定义分辨率不可用";
#endif

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
            if (Compiled) return "";
            return "「" + feature + "」需要 NVIDIA 驱动专有接口（NVAPI）。"
                + "当前运行的是通用版，为了在 AMD / Intel 显卡上也能正常使用，"
                + "这个版本没有打包 NVAPI。\n\n"
                + "如果你用的是 NVIDIA 显卡，请改用 NVIDIA 版。";
        }
    }
}
