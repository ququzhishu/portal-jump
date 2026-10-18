using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PortalJump;

/// <summary>配置层回归测试：解析、往返保存、编码兼容、异常容错。</summary>
static class ConfigTest
{
    static int _fail;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (detail.Length > 0 ? "  -> " + detail : ""));
        if (!ok) _fail++;
    }

    static void Main(string[] args)
    {
        var sw = new StreamWriter(Console.OpenStandardOutput());
        sw.AutoFlush = true;
        Console.SetOut(sw);

        string realIni = args.Length > 0 ? args[0] : Path.Combine("PortalJump", "presets.ini");

        Console.WriteLine("== 1. 读取真实配置 ==");
        Config cfg = Config.Load(realIni);
        Console.WriteLine("  文件: " + cfg.FilePath);
        Console.WriteLine("  自动切换=" + cfg.AutoSwitch + "  恢复目标=" + cfg.RestoreKey);
        Console.WriteLine("  预设数=" + cfg.Presets.Count);
        foreach (Preset p in cfg.Presets)
        {
            Console.WriteLine("    [" + p.Key + "] " + p.Name + "  " + p.ResolutionText
                + "  scaling=" + ScalingNames.ToIniValue(p.ScalingSpec) + "(" + p.ScalingText + ")  程序=" + (p.ProcessList.Length == 0 ? "(无)" : p.ProcessList));
        }
        Console.WriteLine("  RestoreTarget=" + (cfg.RestoreTarget == null ? "(null)" : cfg.RestoreTarget.Key));
        Console.WriteLine("  警告数=" + cfg.Warnings.Count);
        foreach (string w in cfg.Warnings) Console.WriteLine("    ! " + w);

        Check("读到预设", cfg.Presets.Count > 0, cfg.Presets.Count + " 个");
        Check("恢复目标有值且存在", cfg.RestoreTarget != null && cfg.Find(cfg.RestoreKey) != null,
            cfg.RestoreTarget == null ? "null" : cfg.RestoreTarget.Key);

        Console.WriteLine();
        Console.WriteLine("== 2. 保存/重新读取往返 ==");
        string tmp = Path.Combine(Path.GetTempPath(), "rs-roundtrip.ini");
        cfg.Save(tmp);
        Config cfg2 = Config.Load(tmp);
        bool ok = cfg2.Presets.Count == cfg.Presets.Count
            && cfg2.RestoreKey == cfg.RestoreKey
            && cfg2.AutoSwitch == cfg.AutoSwitch;
        if (ok)
        {
            for (int i = 0; i < cfg.Presets.Count; i++)
            {
                Preset a = cfg.Presets[i], b = cfg2.Presets[i];
                if (a.Key != b.Key || a.Name != b.Name || a.Width != b.Width || a.Height != b.Height
                    || !a.ScalingSpec.Equals(b.ScalingSpec) || a.ProcessList != b.ProcessList)
                {
                    ok = false;
                    Console.WriteLine("    差异 @" + i + ": " + a.Summary + "  vs  " + b.Summary);
                }
            }
        }
        Check("往返一致", ok, "");
        Console.WriteLine("  --- 保存后的文件内容 ---");
        foreach (string line in File.ReadAllLines(tmp)) Console.WriteLine("  | " + line);

        Console.WriteLine();
        Console.WriteLine("== 3. ANSI(GBK) 编码兼容 ==");
        string gbk = Path.Combine(Path.GetTempPath(), "rs-gbk.ini");
        string body = "[general]\r\nautoswitch=0\r\nrestore=桌面\r\n\r\n[桌面]\r\nname=桌面预设\r\nwidth=2560\r\nheight=1440\r\nscaling=3\r\n";
        File.WriteAllText(gbk, body, Encoding.GetEncoding(936));
        Config c3 = Config.Load(gbk);
        Check("GBK 文件能解析", c3.Presets.Count == 1, c3.Presets.Count + " 个预设");
        Check("GBK 中文不乱码", c3.Presets.Count == 1 && c3.Presets[0].Name == "桌面预设",
            c3.Presets.Count == 1 ? c3.Presets[0].Name : "");
        Check("autoswitch=0 生效", !c3.AutoSwitch, c3.AutoSwitch.ToString());

        Console.WriteLine();
        Console.WriteLine("== 4. 异常/坏数据容错 ==");
        string bad = Path.Combine(Path.GetTempPath(), "rs-bad.ini");
        File.WriteAllText(bad,
            "; 注释\r\n[general]\r\nautoswitch=maybe\r\n\r\n[noSize]\r\nname=缺尺寸\r\n\r\n"
            + "[ok]\r\nname=正常\r\nwidth=1920\r\nheight=1080\r\nscaling=99\r\nprocess= C:\\a\\b\\Game.exe ;game2;GAME2 \r\n",
            new UTF8Encoding(false));
        Config c4 = Config.Load(bad);
        Check("忽略缺尺寸的段", c4.Presets.Count == 1, c4.Presets.Count + " 个");
        // 旧版这里校验"非法值归一到 2"；v0.11 改成按语义校验：
        // 非法值应回退为"全屏铺满（GPU）"，并给出警告（不再静默吞掉）。
        Check("非法 scaling 回退为全屏铺满(GPU)",
            c4.Presets.Count == 1 && c4.Presets[0].ScalingSpec.Equals(
                new ScalingSpec(ScalingMode.FullScreen, ScaleActor.Gpu)),
            c4.Presets.Count == 1 ? c4.Presets[0].ScalingText : "");
        Check("进程名去路径/.exe/去重", c4.Presets.Count == 1 && c4.Presets[0].ProcessList == "Game;game2",
            c4.Presets.Count == 1 ? c4.Presets[0].ProcessList : "");
        Check("非法布尔值回退默认", c4.AutoSwitch, c4.AutoSwitch.ToString());
        Check("有问题会给出警告", c4.Warnings.Count >= 2, c4.Warnings.Count + " 条");
        foreach (string w in c4.Warnings) Console.WriteLine("    ! " + w);

        Console.WriteLine();
        Console.WriteLine("== 5. 无配置文件时按硬件生成默认 ==");
        string missing = Path.Combine(Path.GetTempPath(), "rs-does-not-exist-" + Guid.NewGuid().ToString("N") + ".ini");
        Config c5 = Config.Load(missing);
        Check("生成默认预设", c5.Presets.Count == 1, c5.Presets.Count + " 个");
        if (c5.Presets.Count == 1)
        {
            DisplayInfo.DEVMODE cur = DisplayInfo.GetCurrentMode();
            Check("默认预设用当前分辨率", c5.Presets[0].Width == cur.dmPelsWidth && c5.Presets[0].Height == cur.dmPelsHeight,
                c5.Presets[0].ResolutionText + " vs 实际 " + cur.dmPelsWidth + "x" + cur.dmPelsHeight);
            Check("默认预设设为恢复目标", c5.RestoreKey == c5.Presets[0].Key, c5.RestoreKey);
        }

        Console.WriteLine();
        Console.WriteLine("== 6. 分辨率枚举 ==");
        List<DisplayMode> modes = DisplayInfo.GetResolutions();
        Check("枚举到分辨率", modes.Count > 0, modes.Count + " 个");
        if (modes.Count > 0)
        {
            Console.WriteLine("  最大: " + modes[0].Text);
            Console.WriteLine("  最小: " + modes[modes.Count - 1].Text);
            bool desc = true;
            for (int i = 1; i < modes.Count; i++)
            {
                if ((long)modes[i].Width * modes[i].Height > (long)modes[i - 1].Width * modes[i - 1].Height) desc = false;
            }
            Check("按像素数降序排列", desc, "");
        }

        Console.WriteLine();
        Console.WriteLine("== 7. 路径解析 ==");
        Console.WriteLine("  ExeDir   = " + AppPaths.ExeDir);
        Console.WriteLine("  DataDir  = " + AppPaths.DataDir);
        Console.WriteLine("  IniPath  = " + AppPaths.IniPath);
        Console.WriteLine("  回退到 APPDATA: " + AppPaths.UsingRoamingData);
        Check("数据目录可写", AppPaths.CanWrite(AppPaths.DataDir), AppPaths.DataDir);

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? "全部通过 ✔" : (_fail + " 项失败 ✘"));
        Environment.ExitCode = _fail == 0 ? 0 : 1;
    }
}
