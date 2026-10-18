using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PortalJump
{
    /// <summary>
    /// 缩放模式（与厂商无关的抽象）。
    ///
    /// 为什么要有这一层：
    /// 旧版直接把 NVAPI 的 NV_SCALING 数值写进 ini（1/2/3/5/6/7/8），
    /// 而 NV_SCALING 的编号与 Windows 自己的 DISPLAYCONFIG_SCALING 完全不同，
    /// 导致"缩放方式"这件事被死死绑在 NVIDIA 上。AMD / Intel 用户连选都选不了。
    ///
    /// 这里改成按**语义**定义，各后端自己翻译成厂商数值：
    ///   FullScreen 全屏铺满 / Aspect 保持比例 / Centered 居中不缩放 / Integer 整数缩放
    ///   + 是否强制由 GPU 做缩放（而不是交给显示器做）
    /// ini 里同时接受新语义值（如 "fullscreen-gpu"）与旧数值（1..8），旧配置自动迁移。
    /// </summary>
    public enum ScalingMode
    {
        /// <summary>不改动，保持驱动/显示器当前设置</summary>
        Unchanged = 0,

        /// <summary>全屏铺满（拉伸到整个面板，不保持比例）</summary>
        FullScreen = 1,

        /// <summary>保持比例铺满（留黑边但不变形）</summary>
        Aspect = 2,

        /// <summary>居中不缩放（1:1 像素，四周黑边）</summary>
        Centered = 3,

        /// <summary>整数倍缩放（需要显卡支持）</summary>
        Integer = 4
    }

    /// <summary>缩放由谁执行</summary>
    public enum ScaleActor
    {
        /// <summary>跟随该平台最常用的做法（NVIDIA 默认 GPU，其它默认显示器）</summary>
        Auto = 0,

        /// <summary>强制由 GPU 做缩放（GPU 缩放）</summary>
        Gpu = 1,

        /// <summary>强制由显示器做缩放（显示器缩放）</summary>
        Display = 2
    }

    /// <summary>
    /// 缩放后端（一般不用管，程序自动选）。
    /// 暴露出来是为了两件事：排错时能强制走某条路；换显卡/驱动异常时可以手动兜底。
    /// </summary>
    public enum ScalingBackend
    {
        /// <summary>自动：NVIDIA 优先 NVAPI，其它走 Windows 原生</summary>
        Auto = 0,

        /// <summary>强制走 NVIDIA NVAPI（非 NVIDIA 机器上会失败）</summary>
        Nvapi = 1,

        /// <summary>强制走 Windows 原生 DEVMODE.dmDisplayFixedOutput（任何显卡都能试）</summary>
        Win32 = 2
    }

    /// <summary>缩放方式：语义 + 执行者，与具体显卡厂商解耦</summary>
    public struct ScalingSpec
    {
        public ScalingMode Mode;
        public ScaleActor Actor;

        public static readonly ScalingSpec Unchanged = new ScalingSpec(ScalingMode.Unchanged, ScaleActor.Auto);

        public ScalingSpec(ScalingMode mode, ScaleActor actor)
        {
            Mode = mode;
            Actor = actor;
        }

        public bool IsUnchanged { get { return Mode == ScalingMode.Unchanged; } }

        public override string ToString()
        {
            return ScalingNames.Describe(this);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is ScalingSpec)) return false;
            ScalingSpec o = (ScalingSpec)obj;
            return o.Mode == Mode && o.Actor == Actor;
        }

        public override int GetHashCode()
        {
            return ((int)Mode * 397) ^ (int)Actor;
        }
    }

    /// <summary>缩放方式的文本化（ini 读写、界面显示、日志共用同一套说法）</summary>
    public static class ScalingNames
    {
        /// <summary>ini 里的合法写法 → 语义（读旧配置时也走这里）</summary>
        public static bool TryParse(string raw, out ScalingSpec spec)
        {
            spec = ScalingSpec.Unchanged;
            if (raw == null) return false;
            string s = raw.Trim().ToLowerInvariant();
            if (s.Length == 0) return false;

            // 旧版数值（NV_SCALING）→ 语义。见 Preset.KnownScalings 的注释。
            int legacy;
            if (int.TryParse(s, out legacy))
            {
                switch (legacy)
                {
                    case 0: spec = ScalingSpec.Unchanged; return true;
                    case 1: spec = new ScalingSpec(ScalingMode.FullScreen, ScaleActor.Display); return true; // GPU_SCALING_TO_CLOSEST（显示器）
                    case 2: spec = new ScalingSpec(ScalingMode.FullScreen, ScaleActor.Gpu); return true;
                    case 3: spec = new ScalingSpec(ScalingMode.Centered, ScaleActor.Gpu); return true;
                    case 5: spec = new ScalingSpec(ScalingMode.Aspect, ScaleActor.Gpu); return true;
                    case 6: spec = new ScalingSpec(ScalingMode.Aspect, ScaleActor.Display); return true;
                    case 7: spec = new ScalingSpec(ScalingMode.Centered, ScaleActor.Display); return true;
                    case 8: spec = new ScalingSpec(ScalingMode.Integer, ScaleActor.Gpu); return true;
                    default: return false;
                }
            }

            // 新写法："fullscreen-gpu" / "aspect-display" / "centered" ...
            ScaleActor actor = ScaleActor.Auto;
            int dash = s.LastIndexOf('-');
            if (dash > 0)
            {
                string tail = s.Substring(dash + 1);
                if (tail == "gpu") { actor = ScaleActor.Gpu; s = s.Substring(0, dash); }
                else if (tail == "display" || tail == "monitor" || tail == "scaler") { actor = ScaleActor.Display; s = s.Substring(0, dash); }
            }

            ScalingMode mode;
            if (!TryParseMode(s, out mode)) return false;
            spec = new ScalingSpec(mode, actor);
            return true;
        }

        static bool TryParseMode(string s, out ScalingMode mode)
        {
            switch (s)
            {
                case "unchanged": case "none": case "off": case "keep": mode = ScalingMode.Unchanged; return true;
                case "fullscreen": case "full": case "stretch": case "stretched": mode = ScalingMode.FullScreen; return true;
                case "aspect": case "aspectratio": case "keepaspect": mode = ScalingMode.Aspect; return true;
                case "centered": case "center": case "noscale": mode = ScalingMode.Centered; return true;
                case "integer": case "integerscaling": mode = ScalingMode.Integer; return true;
                default: mode = ScalingMode.Unchanged; return false;
            }
        }

        /// <summary>写回 ini 用的规范写法</summary>
        public static string ToIniValue(ScalingSpec spec)
        {
            string mode;
            switch (spec.Mode)
            {
                case ScalingMode.Unchanged: return "unchanged";
                case ScalingMode.FullScreen: mode = "fullscreen"; break;
                case ScalingMode.Aspect: mode = "aspect"; break;
                case ScalingMode.Centered: mode = "centered"; break;
                case ScalingMode.Integer: mode = "integer"; break;
                default: mode = "fullscreen"; break;
            }
            if (spec.Actor == ScaleActor.Gpu) return mode + "-gpu";
            if (spec.Actor == ScaleActor.Display) return mode + "-display";
            return mode;
        }

        /// <summary>给用户看的中文描述</summary>
        public static string Describe(ScalingSpec spec)
        {
            string mode;
            switch (spec.Mode)
            {
                case ScalingMode.Unchanged: return "不改动（保持当前设置）";
                case ScalingMode.FullScreen: mode = "全屏铺满"; break;
                case ScalingMode.Aspect: mode = "保持比例"; break;
                case ScalingMode.Centered: mode = "居中不缩放"; break;
                case ScalingMode.Integer: mode = "整数缩放"; break;
                default: mode = "全屏铺满"; break;
            }
            switch (spec.Actor)
            {
                case ScaleActor.Gpu: return mode + "（GPU 缩放）";
                case ScaleActor.Display: return mode + "（显示器缩放）";
                default: return mode + "（自动选择执行者）";
            }
        }

        /// <summary>界面下拉框里的一行</summary>
        public static string Label(ScalingSpec spec)
        {
            return Describe(spec);
        }

        /// <summary>
        /// 旧版 NV_SCALING 数值的名字（只用于日志/自检里解释驱动回读到的原始值）。
        /// 新代码不要用这个来存配置 —— 配置一律用语义名（ToIniValue）。
        /// </summary>
        public static string LegacyName(int s)
        {
            switch (s)
            {
                case 0: return "不改动";
                case 1: return "全屏铺满（显示器缩放）";
                case 2: return "全屏铺满（GPU 缩放）";
                case 3: return "居中不缩放（GPU 缩放）";
                case 5: return "保持比例（GPU 缩放）";
                case 6: return "保持比例（显示器缩放）";
                case 7: return "居中不缩放（显示器缩放）";
                case 8: return "整数缩放（GPU 缩放）";
                default: return "缩放模式 " + s;
            }
        }

        /// <summary>界面下拉框的全部可选项（按固定顺序，索引稳定）</summary>
        public static ScalingSpec[] Choices()
        {
            return new ScalingSpec[]
            {
                ScalingSpec.Unchanged,
                new ScalingSpec(ScalingMode.FullScreen, ScaleActor.Gpu),
                new ScalingSpec(ScalingMode.FullScreen, ScaleActor.Display),
                new ScalingSpec(ScalingMode.Aspect, ScaleActor.Gpu),
                new ScalingSpec(ScalingMode.Aspect, ScaleActor.Display),
                new ScalingSpec(ScalingMode.Centered, ScaleActor.Gpu),
                new ScalingSpec(ScalingMode.Centered, ScaleActor.Display),
                new ScalingSpec(ScalingMode.Integer, ScaleActor.Gpu)
            };
        }
    }
}
