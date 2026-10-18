using System;
using System.Collections.Generic;

namespace PortalJump
{
    /// <summary>
    /// 显示预设：分辨率 + 缩放方式 + 关联程序。
    /// </summary>
    public class Preset
    {
        public const int MinDimension = 320;
        public const int MaxDimension = 16384;

        /// <summary>ini 段名 / 命令行参数（唯一）</summary>
        public string Key;

        /// <summary>显示名</summary>
        public string Name;

        public int Width;
        public int Height;

        /// <summary>
        /// 缩放方式（与厂商无关的语义）。ini 里写成 "fullscreen-gpu" 这类字符串；
        /// 旧版配置里的数字（0/1/2/3/5/6/7/8）读取时自动迁移。
        /// </summary>
        public ScalingSpec ScalingSpec;

        /// <summary>关联程序进程名（不含 .exe），用于自动切换</summary>
        public string[] ProcessNames;

        public Preset(string key, string name, int w, int h, ScalingSpec scaling, string[] processNames)
        {
            Key = key == null ? "" : key.Trim();
            Name = name == null ? "" : name.Trim();
            Width = w;
            Height = h;
            ScalingSpec = scaling;
            ProcessNames = processNames == null ? new string[0] : processNames;
        }

        /// <summary>兼容旧调用：用 NV_SCALING 数值构造（内部转成语义）</summary>
        public Preset(string key, string name, int w, int h, int legacyScaling, string[] processNames)
            : this(key, name, w, h, FromLegacyScaling(legacyScaling), processNames)
        {
        }

        public Preset Clone()
        {
            string[] copy = new string[ProcessNames == null ? 0 : ProcessNames.Length];
            if (ProcessNames != null) Array.Copy(ProcessNames, copy, ProcessNames.Length);
            return new Preset(Key, Name, Width, Height, ScalingSpec, copy);
        }

        /// <summary>关联程序列表（分号分隔），图形界面与 ini 都用这个格式</summary>
        public string ProcessList
        {
            get
            {
                if (ProcessNames == null || ProcessNames.Length == 0) return "";
                return string.Join(";", ProcessNames);
            }
            set { ProcessNames = ParseProcessList(value); }
        }

        /// <summary>把 "a;b;c" 解析为进程名数组，去空、去重（忽略大小写）</summary>
        public static string[] ParseProcessList(string text)
        {
            var list = new List<string>();
            if (text == null) return list.ToArray();
            foreach (string part in text.Split(';', ',', '\r', '\n'))
            {
                string n = part.Trim();
                if (n.Length == 0) continue;
                // 允许用户直接粘贴带路径或带 .exe 的写法
                int slash = n.LastIndexOf('\\');
                if (slash >= 0) n = n.Substring(slash + 1);
                if (n.ToLowerInvariant().EndsWith(".exe")) n = n.Substring(0, n.Length - 4);
                n = n.Trim();
                if (n.Length == 0) continue;
                bool dup = false;
                foreach (string e in list)
                {
                    if (string.Equals(e, n, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                }
                if (!dup) list.Add(n);
            }
            return list.ToArray();
        }

        public bool HasProcessBinding
        {
            get { return ProcessNames != null && ProcessNames.Length > 0; }
        }

        public bool IsUsable
        {
            get
            {
                return Width >= MinDimension && Width <= MaxDimension
                    && Height >= MinDimension && Height <= MaxDimension;
            }
        }

        public string ResolutionText
        {
            get { return Width + "x" + Height; }
        }

        public string ScalingText
        {
            get { return ScalingNames.Describe(ScalingSpec); }
        }

        /// <summary>托盘菜单与列表里显示的一行摘要</summary>
        public string Summary
        {
            get
            {
                string s = ResolutionText + " · " + ScalingText;
                if (HasProcessBinding) s += " · 关联 " + ProcessList;
                return s;
            }
        }

        /// <summary>进程名是否命中本预设</summary>
        public bool MatchesProcess(string processName)
        {
            if (processName == null || ProcessNames == null) return false;
            foreach (string n in ProcessNames)
            {
                if (n.Length > 0 && string.Equals(n, processName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // ---- 缩放方式 ----

        /// <summary>
        /// 旧版 NV_SCALING 数值 → 语义。仅用于兼容老配置。
        /// 注意 NV_SCALING 的编号与 Windows DISPLAYCONFIG_SCALING 不同，别混用。
        /// </summary>
        public static ScalingSpec FromLegacyScaling(int s)
        {
            switch (s)
            {
                case 0: return ScalingSpec.Unchanged;
                case 1: return new ScalingSpec(ScalingMode.FullScreen, ScaleActor.Display);
                case 2: return new ScalingSpec(ScalingMode.FullScreen, ScaleActor.Gpu);
                case 3: return new ScalingSpec(ScalingMode.Centered, ScaleActor.Gpu);
                case 5: return new ScalingSpec(ScalingMode.Aspect, ScaleActor.Gpu);
                case 6: return new ScalingSpec(ScalingMode.Aspect, ScaleActor.Display);
                case 7: return new ScalingSpec(ScalingMode.Centered, ScaleActor.Display);
                case 8: return new ScalingSpec(ScalingMode.Integer, ScaleActor.Gpu);
                default: return new ScalingSpec(ScalingMode.FullScreen, ScaleActor.Gpu);
            }
        }

        /// <summary>界面下拉框可选项（顺序固定，索引稳定）</summary>
        public static ScalingSpec[] ScalingChoices
        {
            get { return ScalingNames.Choices(); }
        }

        public override string ToString()
        {
            return (Name == null || Name.Length == 0 ? Key : Name) + " (" + Summary + ")";
        }
    }
}
