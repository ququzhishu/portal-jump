using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

/// <summary>
/// 验证托盘的"程序启动自动切换 / 退出自动恢复"。
///
/// 关键点：沙盒里的预设分辨率 = 当前分辨率、scaling=0（不改动），
/// 所以整个测试**不会真的改变屏幕**，只是把"检测到程序 → 走完切换流程"这条路跑通。
/// 绑定的程序是一个专门的假游戏进程（rsprobe-game.exe），不会和用户其它程序冲突。
/// </summary>
static class TrayProbe
{
    static int _fail;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (detail.Length > 0 ? "  -> " + detail : ""));
        if (!ok) _fail++;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY;
        public int dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
        public int dmReserved1, dmReserved2, dmReserved3;
        public int dmPanningWidth, dmPanningHeight;
    }

    static void Main(string[] args)
    {
        var sw = new StreamWriter(Console.OpenStandardOutput());
        sw.AutoFlush = true;
        Console.SetOut(sw);

        string exe = args.Length > 0 ? args[0] : Path.Combine("PortalJump", "PortalJump.exe");
        string fakeGame = args.Length > 1 ? args[1] : Path.Combine("tools", "rsprobe-game.exe");
        Console.WriteLine("被测程序: " + exe);
        if (!File.Exists(exe) || !File.Exists(fakeGame)) { Console.WriteLine("缺少文件"); Environment.ExitCode = 2; return; }

        DEVMODE cur = new DEVMODE();
        cur.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        EnumDisplaySettings(null, -1, ref cur);
        Console.WriteLine("当前分辨率: " + cur.dmPelsWidth + "x" + cur.dmPelsHeight + "@" + cur.dmDisplayFrequency + "Hz");
        Console.WriteLine("（沙盒预设就用这个分辨率 + scaling=0，所以测试期间画面不会变）");

        string sandbox = Path.Combine(Path.GetTempPath(), "rs-traytest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(sandbox);
        string dir = Path.GetDirectoryName(exe);
        File.Copy(exe, Path.Combine(sandbox, "PortalJump.exe"));
        File.Copy(Path.Combine(dir, "NvAPIWrapper.dll"), Path.Combine(sandbox, "NvAPIWrapper.dll"), true);
        File.Copy(fakeGame, Path.Combine(sandbox, "rsprobe-game.exe"), true);

        string ini = Path.Combine(sandbox, "presets.ini");
        string body =
            "[general]\r\nautoswitch=1\r\nrestore=desk\r\n\r\n"
            + "[desk]\r\nname=桌面\r\nwidth=" + cur.dmPelsWidth + "\r\nheight=" + cur.dmPelsHeight + "\r\nscaling=0\r\n\r\n"
            + "[game]\r\nname=假游戏\r\nwidth=" + cur.dmPelsWidth + "\r\nheight=" + cur.dmPelsHeight
            + "\r\nscaling=0\r\nprocess=rsprobe-game\r\n";
        File.WriteAllText(ini, body, new UTF8Encoding(false));
        Console.WriteLine("沙盒: " + sandbox);

        string log = Path.Combine(sandbox, "portal-jump.log");

        Console.WriteLine();
        Console.WriteLine("== 先停掉已经在运行的 折返跃迁 Portal Jump（它是全局单实例，会占住互斥体） ==");
        int stopped = 0;
        foreach (Process p in Process.GetProcessesByName("PortalJump"))
        {
            try { p.Kill(); stopped++; Console.WriteLine("  已结束 PID " + p.Id); }
            catch (Exception e) { Console.WriteLine("  结束 PID " + p.Id + " 失败: " + e.Message); }
        }
        Thread.Sleep(1200);
        Console.WriteLine("  共结束 " + stopped + " 个实例（测试结束后会重新启动）");

        Console.WriteLine();
        Console.WriteLine("== 启动托盘常驻 ==");
        Process tray = Process.Start(Path.Combine(sandbox, "PortalJump.exe"), "tray");
        Thread.Sleep(4000);
        Check("托盘进程在运行", !tray.HasExited, tray.HasExited ? "已退出 " + tray.ExitCode : "PID " + tray.Id);
        Check("托盘写入了启动日志", LogContains(log, "启动 折返跃迁"), "");

        Console.WriteLine();
        Console.WriteLine("== 启动假游戏，应自动切到 [game] ==");
        Process game = Process.Start(Path.Combine(sandbox, "rsprobe-game.exe"));
        bool autoSwitched = WaitFor(log, "Apply 开始: game", 20000);
        Check("检测到程序并触发自动切换", autoSwitched, autoSwitched ? "" : "20 秒内日志里没有 Apply 开始: game");
        if (autoSwitched)
        {
            Thread.Sleep(1500);
            // v0.11 文案从"缩放保持驱动当前设置"改成"缩放保持当前设置（未改动）"，
            // 这两个都算对：关键行为是 scaling=0/unchanged 时不去动缩放。
            Check("切换流程成功完成（scaling=0 时不动缩放）",
                LogContains(log, "缩放保持当前设置") || LogContains(log, "缩放保持驱动当前设置"), Tail(log, 6));
        }

        Console.WriteLine();
        Console.WriteLine("== 关闭假游戏，应自动恢复到 [desk] ==");
        try { game.Kill(); } catch { }
        bool restored = WaitFor(log, "Apply 开始: desk", 20000);
        Check("程序退出后自动恢复桌面预设", restored, restored ? "" : "20 秒内没有恢复");

        Console.WriteLine();
        Console.WriteLine("== 关闭自动切换开关后不应再触发 ==");
        string logLenBefore = "";
        if (File.Exists(log)) logLenBefore = File.ReadAllText(log, Encoding.UTF8);
        // 把 autoswitch 改成 0，托盘应在 2 秒内热加载
        File.WriteAllText(ini, body.Replace("autoswitch=1", "autoswitch=0"), new UTF8Encoding(false));
        Thread.Sleep(5000);
        Check("托盘热加载了新配置", LogContains(log, "已重新载入配置"), "");
        Process game2 = Process.Start(Path.Combine(sandbox, "rsprobe-game.exe"));
        Thread.Sleep(7000);
        string after = File.ReadAllText(log, Encoding.UTF8);
        int before = CountOccurrences(logLenBefore, "Apply 开始: game");
        int now = CountOccurrences(after, "Apply 开始: game");
        Check("自动切换关闭后不再切换", now == before, "关闭前 " + before + " 次，关闭后 " + now + " 次");
        try { game2.Kill(); } catch { }

        Console.WriteLine();
        Console.WriteLine("== 关闭托盘 ==");
        try { tray.Kill(); Thread.Sleep(500); } catch { }
        Check("托盘已退出", tray.HasExited, "");

        Console.WriteLine();
        Console.WriteLine("== 完整日志 ==");
        if (File.Exists(log))
        {
            foreach (string line in File.ReadAllLines(log, Encoding.UTF8)) Console.WriteLine("  | " + line);
        }

        string crash = Path.Combine(sandbox, "portal-jump-crash.log");
        Check("没有崩溃日志", !File.Exists(crash), File.Exists(crash) ? File.ReadAllText(crash) : "");

        try { Directory.Delete(sandbox, true); } catch { }

        Console.WriteLine();
        Console.WriteLine("== 恢复：重新启动原来那个托盘 ==");
        try
        {
            Process restarted = Process.Start(exe, "tray");
            Thread.Sleep(2500);
            Console.WriteLine("  已重新启动 " + exe + " (PID " + restarted.Id + ")");
        }
        catch (Exception e)
        {
            Console.WriteLine("  重新启动失败：" + e.Message + "  —— 请手动双击一次 PortalJump.exe");
        }

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? "全部通过 ✔" : (_fail + " 项失败 ✘"));
        Environment.ExitCode = _fail == 0 ? 0 : 1;
    }

    static bool LogContains(string path, string text)
    {
        try { return File.Exists(path) && File.ReadAllText(path, Encoding.UTF8).Contains(text); }
        catch { return false; }
    }

    static string Tail(string path, int lines)
    {
        try
        {
            string[] all = File.ReadAllLines(path, Encoding.UTF8);
            int start = Math.Max(0, all.Length - lines);
            var sb = new StringBuilder();
            for (int i = start; i < all.Length; i++) sb.Append(all[i]).Append(" / ");
            return sb.ToString();
        }
        catch { return ""; }
    }

    static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return 0;
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0) { count++; idx += needle.Length; }
        return count;
    }

    static bool WaitFor(string path, string text, int timeoutMs)
    {
        int waited = 0;
        while (waited < timeoutMs)
        {
            if (LogContains(path, text)) return true;
            Thread.Sleep(500);
            waited += 500;
        }
        return false;
    }
}
