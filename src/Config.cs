using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PortalJump
{
    /// <summary>
    /// 配置：沿用 presets.ini（便于手工编辑与迁移），
    /// 但所有读写都容错，保证换一台机器/换一个系统语言都能正常解析。
    /// </summary>
    public class Config
    {
        public const string GeneralSection = "general";

        /// <summary>托盘是否自动跟随程序切换</summary>
        public bool AutoSwitch = true;

        /// <summary>
        /// 启动时是否自动纠正"桌面停在游戏分辨率上"的残留。
        /// 默认开：上次没恢复成功、或关机时正处在游戏分辨率，开机能自动回到办公预设。
        /// 但它会主动改分辨率，如果你习惯把桌面长期设成某个游戏预设的分辨率，
        /// 这个行为会跟你打架 —— 那种情况在 ini 里写 fixresidual=0 关掉即可。
        /// </summary>
        public bool FixResidual = true;

        /// <summary>所有程序退出后要恢复到的预设 Key（空 = 不恢复）</summary>
        public string RestoreKey = "";

        public List<Preset> Presets = new List<Preset>();

        /// <summary>加载时发现的问题，供界面提示</summary>
        public List<string> Warnings = new List<string>();

        public string FilePath = "";

        public Config()
        {
        }

        // ---------------- 查找 ----------------

        public Preset Find(string key)
        {
            if (key == null) return null;
            foreach (Preset p in Presets)
            {
                if (string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }

        /// <summary>退出游戏后要恢复的预设</summary>
        public Preset RestoreTarget
        {
            get
            {
                Preset p = Find(RestoreKey);
                if (p != null) return p;
                // 未配置时：优先选未绑定程序的预设，否则第一个
                foreach (Preset q in Presets)
                {
                    if (!q.HasProcessBinding) return q;
                }
                return Presets.Count > 0 ? Presets[0] : null;
            }
        }

        public bool KeyExists(string key)
        {
            return Find(key) != null;
        }

        /// <summary>生成不与现有预设冲突的 Key</summary>
        public string MakeUniqueKey(string baseKey)
        {
            string b = (baseKey == null ? "" : baseKey.Trim());
            if (b.Length == 0) b = "preset";
            // 只保留字母数字与下划线，避免 ini 段名/命令行参数出现怪字符
            var sb = new StringBuilder();
            foreach (char c in b)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == '_' || c == '-') sb.Append(c);
            }
            b = sb.ToString();
            if (b.Length == 0) b = "preset";
            if (string.Equals(b, GeneralSection, StringComparison.OrdinalIgnoreCase)) b = "preset";

            string candidate = b;
            int n = 2;
            while (KeyExists(candidate))
            {
                candidate = b + n;
                n++;
            }
            return candidate;
        }

        // ---------------- 加载 ----------------

        public static Config Load(string path)
        {
            var cfg = new Config();
            cfg.FilePath = path;

            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    cfg.ParseIni(ReadAllTextSmart(path));
                }
                catch (Exception e)
                {
                    cfg.Warnings.Add("配置文件解析失败，已改用默认预设：" + e.Message);
                }
            }

            if (cfg.Presets.Count == 0)
            {
                cfg.ApplyHardwareDefaults();
            }
            if (cfg.RestoreTarget == null || cfg.Find(cfg.RestoreKey) == null)
            {
                Preset r = null;
                foreach (Preset q in cfg.Presets) { if (!q.HasProcessBinding) { r = q; break; } }
                cfg.RestoreKey = r != null ? r.Key : (cfg.Presets.Count > 0 ? cfg.Presets[0].Key : "");
            }
            return cfg;
        }

        /// <summary>
        /// 配置文件不存在时，把内存里的默认配置落盘一次。
        /// 目的：绿色程序"解压即用"，用户一运行就能在程序目录看到 presets.ini，而不是等到第一次保存才出现。
        /// 目录不可写等异常一律吞掉 —— 落盘失败不影响使用，配置照样在内存里生效。
        /// </summary>
        public bool EnsureFile()
        {
            try
            {
                if (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath)) return false;
                Save(FilePath);
                return true;
            }
            catch { return false; }
        }
        /// <summary>首次运行（或配置文件全坏）时，按当前显示器实际情况生成一个"办公"预设</summary>
        public void ApplyHardwareDefaults()
        {
            DisplayInfo.DEVMODE cur = DisplayInfo.GetCurrentMode();
            int w = cur.dmPelsWidth > 0 ? cur.dmPelsWidth : 1920;
            int h = cur.dmPelsHeight > 0 ? cur.dmPelsHeight : 1080;
            var p = new Preset("office", "办公", w, h,
                new ScalingSpec(ScalingMode.FullScreen, ScaleActor.Gpu), null);
            Presets.Add(p);
            RestoreKey = p.Key;
            Warnings.Add("未找到配置文件，已按当前分辨率 " + w + "x" + h + " 生成默认预设「办公」。");
        }

        void ParseIni(string text)
        {
            string section = null;
            var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string raw in SplitLines(text))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    FlushSection(section, kv);
                    section = line.Substring(1, line.Length - 2).Trim();
                    kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq > 0 && section != null)
                {
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    if (k.Length > 0) kv[k] = v;
                }
            }
            FlushSection(section, kv);
        }

        void FlushSection(string section, Dictionary<string, string> kv)
        {
            if (section == null || section.Length == 0) return;

            if (string.Equals(section, GeneralSection, StringComparison.OrdinalIgnoreCase))
            {
                string v;
                if (kv.TryGetValue("autoswitch", out v))
                {
                    AutoSwitch = ParseBool(v, true);
                    if (!IsBoolLiteral(v))
                    {
                        Warnings.Add("autoswitch 的值「" + v + "」无法识别，已按“开启”处理（请用 1/0）。");
                    }
                }
                if (kv.TryGetValue("restore", out v)) RestoreKey = v.Trim();
                if (kv.TryGetValue("fixresidual", out v)) FixResidual = ParseBool(v, true);
                return;
            }

            string sv;
            int w = 0, h = 0;
            // 默认全屏铺满（GPU），与旧版"未写 scaling 时按 2 处理"的行为一致
            ScalingSpec sc = new ScalingSpec(ScalingMode.FullScreen, ScaleActor.Gpu);
            if (kv.TryGetValue("width", out sv)) int.TryParse(sv.Trim(), out w);
            if (kv.TryGetValue("height", out sv)) int.TryParse(sv.Trim(), out h);
            if (kv.TryGetValue("scaling", out sv))
            {
                ScalingSpec parsed;
                if (ScalingNames.TryParse(sv, out parsed))
                {
                    sc = parsed;
                }
                else
                {
                    Warnings.Add("预设 [" + section + "] 的 scaling 值「" + sv
                        + "」无法识别，已按“全屏铺满（GPU 缩放）”处理。");
                }
            }

            if (w <= 0 || h <= 0)
            {
                Warnings.Add("忽略预设 [" + section + "]：缺少有效的 width / height。");
                return;
            }

            string name = section;
            if (kv.TryGetValue("name", out sv) && sv.Trim().Length > 0) name = sv.Trim();

            string[] procs = null;
            if (kv.TryGetValue("process", out sv)) procs = Preset.ParseProcessList(sv);

            var p = new Preset(section, name, w, h, sc, procs);
            if (!p.IsUsable)
            {
                Warnings.Add("忽略预设 [" + section + "]：分辨率 " + p.ResolutionText + " 超出合理范围。");
                return;
            }
            Presets.Add(p);
        }

        static bool IsBoolLiteral(string s)
        {
            if (s == null) return false;
            s = s.Trim().ToLowerInvariant();
            return s == "1" || s == "0" || s == "true" || s == "false"
                || s == "yes" || s == "no" || s == "on" || s == "off"
                || s == "是" || s == "否" || s == "开" || s == "关";
        }

        static bool ParseBool(string s, bool fallback)
        {
            if (s == null) return fallback;
            s = s.Trim().ToLowerInvariant();
            if (s == "1" || s == "true" || s == "yes" || s == "on" || s == "是" || s == "开") return true;
            if (s == "0" || s == "false" || s == "no" || s == "off" || s == "否" || s == "关") return false;
            return fallback;
        }

        // ---------------- 保存 ----------------

        public void Save()
        {
            Save(FilePath);
        }

        public void Save(string path)
        {
            FilePath = path;
            var sb = new StringBuilder();
            sb.AppendLine("; 折返跃迁 Portal Jump 配置文件（可以直接用记事本修改，也可以用图形界面修改）");
            sb.AppendLine("; scaling 取值（与显卡厂商无关，AMD / Intel / NVIDIA 都能用）：");
            sb.AppendLine(";   unchanged        不改动（保持当前设置）");
            sb.AppendLine(";   fullscreen-gpu   全屏铺满，GPU 缩放");
            sb.AppendLine(";   fullscreen-display 全屏铺满，显示器缩放");
            sb.AppendLine(";   aspect-gpu       保持比例，GPU 缩放");
            sb.AppendLine(";   aspect-display   保持比例，显示器缩放");
            sb.AppendLine(";   centered-gpu     居中不缩放，GPU 缩放");
            sb.AppendLine(";   centered-display 居中不缩放，显示器缩放");
            sb.AppendLine(";   integer-gpu      整数缩放（仅 NVIDIA）");
            sb.AppendLine("; 旧版配置里的数字（0/1/2/3/5/6/7/8）会被自动识别并迁移，不用手改。");
            sb.AppendLine("; process: 关联程序的进程名（不含 .exe，多个用分号隔开），命中后自动切换到本预设");
            sb.AppendLine("; restore: 所有关联程序都退出后，自动恢复到哪个预设（段名）");
            sb.AppendLine();
            sb.AppendLine("[" + GeneralSection + "]");
            sb.AppendLine("autoswitch=" + (AutoSwitch ? "1" : "0"));
            sb.AppendLine("fixresidual=" + (FixResidual ? "1" : "0"));
            sb.AppendLine("restore=" + (RestoreKey == null ? "" : RestoreKey));
            sb.AppendLine();

            foreach (Preset p in Presets)
            {
                sb.AppendLine("[" + p.Key + "]");
                sb.AppendLine("name=" + p.Name);
                sb.AppendLine("width=" + p.Width);
                sb.AppendLine("height=" + p.Height);
                sb.AppendLine("scaling=" + ScalingNames.ToIniValue(p.ScalingSpec));
                if (p.HasProcessBinding) sb.AppendLine("process=" + p.ProcessList);
                sb.AppendLine();
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // 先写临时文件再替换，避免写到一半崩溃把配置弄坏
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch
            {
                File.Copy(tmp, path, true);
                try { File.Delete(tmp); } catch { }
            }
        }

        // ---------------- 文本编码兼容 ----------------

        /// <summary>
        /// 用户可能用记事本另存为 ANSI(GBK)，也可能存成 UTF-8。
        /// 这里先按 BOM，再按严格 UTF-8 试，失败才退回系统 ANSI 代码页。
        /// </summary>
        public static string ReadAllTextSmart(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch
            {
                try { return Encoding.GetEncoding(936).GetString(bytes); }
                catch { return Encoding.Default.GetString(bytes); }
            }
        }

        static IEnumerable<string> SplitLines(string text)
        {
            return text.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }
    }
}
