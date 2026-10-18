// 缩放语义 / 旧配置迁移 / 后端选择的单元测试（不改变屏幕）
//
// 编译： csc /nologo /codepage:65001 /out:ScalingTest.exe /r:NvAPIWrapper.dll ScalingTest.cs ..\src\*.cs
// 说明：只测纯逻辑（解析、映射、读写往返），不调用任何会切换画面的 API。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

static class ScalingTest
{
    static int _fail;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (detail.Length > 0 ? "  -> " + detail : ""));
        if (!ok) _fail++;
    }

    static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== 1. 缩放语义解析（新写法）===");
        var cases = new Dictionary<string, PortalJump.ScalingSpec>
        {
            { "unchanged",            PortalJump.ScalingSpec.Unchanged },
            { "fullscreen-gpu",       new PortalJump.ScalingSpec(PortalJump.ScalingMode.FullScreen, PortalJump.ScaleActor.Gpu) },
            { "fullscreen-display",   new PortalJump.ScalingSpec(PortalJump.ScalingMode.FullScreen, PortalJump.ScaleActor.Display) },
            { "aspect-gpu",           new PortalJump.ScalingSpec(PortalJump.ScalingMode.Aspect, PortalJump.ScaleActor.Gpu) },
            { "centered-display",     new PortalJump.ScalingSpec(PortalJump.ScalingMode.Centered, PortalJump.ScaleActor.Display) },
            { "integer-gpu",          new PortalJump.ScalingSpec(PortalJump.ScalingMode.Integer, PortalJump.ScaleActor.Gpu) },
            { "FullScreen-GPU",       new PortalJump.ScalingSpec(PortalJump.ScalingMode.FullScreen, PortalJump.ScaleActor.Gpu) },
            { "stretch",              new PortalJump.ScalingSpec(PortalJump.ScalingMode.FullScreen, PortalJump.ScaleActor.Auto) },
        };
        foreach (var kv in cases)
        {
            PortalJump.ScalingSpec got;
            bool ok = PortalJump.ScalingNames.TryParse(kv.Key, out got);
            Check("解析 \"" + kv.Key + "\"", ok && got.Equals(kv.Value), ok ? got.ToString() : "解析失败");
        }

        Console.WriteLine();
        Console.WriteLine("=== 2. 旧版数字配置迁移 ===");
        // 旧 NV_SCALING → 语义
        var legacy = new Dictionary<int, PortalJump.ScalingSpec>
        {
            { 0, PortalJump.ScalingSpec.Unchanged },
            { 1, new PortalJump.ScalingSpec(PortalJump.ScalingMode.FullScreen, PortalJump.ScaleActor.Display) },
            { 2, new PortalJump.ScalingSpec(PortalJump.ScalingMode.FullScreen, PortalJump.ScaleActor.Gpu) },
            { 3, new PortalJump.ScalingSpec(PortalJump.ScalingMode.Centered, PortalJump.ScaleActor.Gpu) },
            { 5, new PortalJump.ScalingSpec(PortalJump.ScalingMode.Aspect, PortalJump.ScaleActor.Gpu) },
            { 6, new PortalJump.ScalingSpec(PortalJump.ScalingMode.Aspect, PortalJump.ScaleActor.Display) },
            { 7, new PortalJump.ScalingSpec(PortalJump.ScalingMode.Centered, PortalJump.ScaleActor.Display) },
            { 8, new PortalJump.ScalingSpec(PortalJump.ScalingMode.Integer, PortalJump.ScaleActor.Gpu) },
        };
        foreach (var kv in legacy)
        {
            PortalJump.ScalingSpec got;
            bool ok = PortalJump.ScalingNames.TryParse(kv.Key.ToString(), out got);
            Check("旧值 " + kv.Key, ok && got.Equals(kv.Value), ok ? got.ToString() : "解析失败");
        }

        Console.WriteLine();
        Console.WriteLine("=== 3. ini 往返（写出 → 读回 必须一致）===");
        foreach (PortalJump.ScalingSpec s in PortalJump.ScalingNames.Choices())
        {
            string ini = PortalJump.ScalingNames.ToIniValue(s);
            PortalJump.ScalingSpec back;
            bool ok = PortalJump.ScalingNames.TryParse(ini, out back);
            Check("往返 " + s.ToString() + "  [" + ini + "]", ok && back.Equals(s), ok ? "OK" : "不一致");
        }

        Console.WriteLine();
        Console.WriteLine("=== 4. 界面下拉选项 ===");
        var choices = PortalJump.ScalingNames.Choices();
        Check("选项数量 = 8", choices.Length == 8, "实际 " + choices.Length);
        Check("首项是不改动", choices[0].IsUnchanged, choices[0].ToString());
        Check("选项无重复", NoDuplicate(choices), "");
        foreach (var c in choices) Console.WriteLine("      " + PortalJump.ScalingNames.Label(c));

        Console.WriteLine();
        Console.WriteLine("=== 5. 配置读写往返（含旧配置迁移）===");
        string dir = Path.Combine(Path.GetTempPath(), "pj-scale-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            string ini = Path.Combine(dir, "presets.ini");
            // 故意用旧版数字写
            File.WriteAllText(ini,
                "; 旧版配置\r\n[general]\r\nautoswitch=1\r\nrestore=office\r\n\r\n"
                + "[office]\r\nname=办公\r\nwidth=3840\r\nheight=2160\r\nscaling=2\r\n\r\n"
                + "[game]\r\nname=三角洲\r\nwidth=2560\r\nheight=1440\r\nscaling=3\r\nprocess=DeltaForceClient-Win64-Shipping\r\n",
                new UTF8Encoding(false));

            PortalJump.Config cfg = PortalJump.Config.Load(ini);
            Check("读到 2 个预设", cfg.Presets.Count == 2, "实际 " + cfg.Presets.Count);
            PortalJump.Preset office = cfg.Find("office");
            PortalJump.Preset game = cfg.Find("game");
            Check("office 旧值 2 → 全屏铺满(GPU)",
                office != null && office.ScalingSpec.Equals(
                    new PortalJump.ScalingSpec(PortalJump.ScalingMode.FullScreen, PortalJump.ScaleActor.Gpu)),
                office == null ? "未找到" : office.ScalingText);
            Check("game 旧值 3 → 居中不缩放(GPU)",
                game != null && game.ScalingSpec.Equals(
                    new PortalJump.ScalingSpec(PortalJump.ScalingMode.Centered, PortalJump.ScaleActor.Gpu)),
                game == null ? "未找到" : game.ScalingText);

            // 保存后应变成新写法
            cfg.Save(ini);
            string saved = File.ReadAllText(ini, Encoding.UTF8);
            Check("保存后使用新语义写法", saved.Contains("scaling=fullscreen-gpu") && saved.Contains("scaling=centered-gpu"),
                saved.Contains("scaling=") ? "含 scaling= 行" : "没有 scaling 行");
            Check("保存后不再写旧数字", !saved.Contains("scaling=2") && !saved.Contains("scaling=3"), "");

            // 再读一次，语义必须保持
            PortalJump.Config cfg2 = PortalJump.Config.Load(ini);
            PortalJump.Preset game2 = cfg2.Find("game");
            Check("二次读取语义不变",
                game2 != null && game2.ScalingSpec.Equals(game != null ? game.ScalingSpec : PortalJump.ScalingSpec.Unchanged),
                game2 == null ? "未找到" : game2.ScalingText);
            Check("关联程序保留", game2 != null && game2.ProcessNames.Length == 1
                && game2.ProcessNames[0] == "DeltaForceClient-Win64-Shipping", "");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }

        Console.WriteLine();
        Console.WriteLine("=== 6. 非法值容错 ===");
        PortalJump.ScalingSpec bad;
        Check("空串解析失败", !PortalJump.ScalingNames.TryParse("", out bad), "");
        Check("乱写解析失败", !PortalJump.ScalingNames.TryParse("hello", out bad), "");
        Check("越界数字解析失败", !PortalJump.ScalingNames.TryParse("99", out bad), "");

        Console.WriteLine();
        Console.WriteLine("=== 7. 结构体布局自检 ===");
        Check("DEVMODEA = 156 字节（旧版错写 160）",
            PortalJump.DisplayConfigBackend.DevModeSize == 156,
            "实际 " + PortalJump.DisplayConfigBackend.DevModeSize);

        Console.WriteLine();
        Console.WriteLine("=== 8. 运行环境（只读）===");
        PortalJump.Switcher.TryInitNvapi();
        Console.WriteLine("      厂商: " + PortalJump.Switcher.Vendor);
        Console.WriteLine("      适配器: " + PortalJump.Switcher.EnvironmentText);
        Console.WriteLine("      缩放通路: " + PortalJump.Switcher.ScalingBackendText);
        Console.WriteLine("      当前缩放: " + PortalJump.Switcher.GetCurrentScalingText());
        Check("能识别出显卡厂商", PortalJump.Switcher.Vendor != PortalJump.GpuVendor.Unknown,
            PortalJump.Switcher.Vendor.ToString());

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? "全部通过 ✔" : (_fail + " 项失败 ✘"));
        Environment.ExitCode = _fail == 0 ? 0 : 1;
    }

    static bool NoDuplicate(PortalJump.ScalingSpec[] list)
    {
        for (int i = 0; i < list.Length; i++)
            for (int j = i + 1; j < list.Length; j++)
                if (list[i].Equals(list[j])) return false;
        return true;
    }
}
