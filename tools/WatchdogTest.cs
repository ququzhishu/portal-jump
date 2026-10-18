// 看门狗测试：模拟"游戏启动后把分辨率改回去"，验证托盘能否自动纠正。
//
// 这正是三角洲行动"有时候切不过去"的核心场景：
// 托盘切到 2560x1440 → 游戏自己又设回 3840x2160 → 旧版不管了 → 用户看到"没切过去"。
//
// 用法： WatchdogTest.exe <PortalJump.exe> <假游戏.exe>
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

static class WatchdogTest
{
    static int _fail;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (detail.Length > 0 ? "  -> " + detail : ""));
        if (!ok) _fail++;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DEVMODE
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
        public int dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsA", CharSet = CharSet.Ansi)]
    static extern bool EnumDisplaySettings(string d, int m, ref DEVMODE dm);
    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExA", CharSet = CharSet.Ansi)]
    static extern int ChangeDisplaySettingsEx(string d, ref DEVMODE dm, IntPtr h, uint f, IntPtr p);

    const uint CDS_UPDATEREGISTRY = 0x01;

    static DEVMODE Cur()
    {
        var dm = new DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        EnumDisplaySettings(null, -1, ref dm);
        return dm;
    }

    static string CurText()
    {
        var d = Cur();
        return d.dmPelsWidth + "x" + d.dmPelsHeight + "@" + d.dmDisplayFrequency;
    }

    static bool FindHz(int w, int h, int hz, out DEVMODE found)
    {
        found = new DEVMODE();
        for (int i = 0; ; i++)
        {
            var dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (!EnumDisplaySettings(null, i, ref dm)) break;
            if (dm.dmPelsWidth == w && dm.dmPelsHeight == h && dm.dmDisplayFrequency == hz && dm.dmBitsPerPel == 32)
            { found = dm; return true; }
        }
        return false;
    }

    static void ForceMode(int w, int h, int hz)
    {
        DEVMODE dm;
        if (FindHz(w, h, hz, out dm))
        {
            ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            Thread.Sleep(1400);
        }
    }

    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string exe = args.Length > 0 ? args[0] : @"C:\工作区\res-switch\PortalJump\PortalJump.exe";
        string fakeGame = args.Length > 1 ? args[1] : @"C:\工作区\res-switch\tools\rsprobe-game.exe";
        if (!File.Exists(exe) || !File.Exists(fakeGame))
        {
            Console.WriteLine("缺少文件: exe=" + File.Exists(exe) + " game=" + File.Exists(fakeGame));
            Environment.ExitCode = 2;
            return;
        }

        var orig = Cur();
        Console.WriteLine("初始: " + CurText());
        Console.WriteLine();

        string sandbox = Path.Combine(Path.GetTempPath(), "pj-wd-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(sandbox);
        string dir = Path.GetDirectoryName(exe);
        File.Copy(exe, Path.Combine(sandbox, "PortalJump.exe"), true);
        string nv = Path.Combine(dir, "NvAPIWrapper.dll");
        if (File.Exists(nv)) File.Copy(nv, Path.Combine(sandbox, "NvAPIWrapper.dll"), true);
        File.Copy(fakeGame, Path.Combine(sandbox, "rsprobe-game.exe"), true);

        string ini = Path.Combine(sandbox, "presets.ini");
        File.WriteAllText(ini,
            "[general]\r\nautoswitch=1\r\nfixresidual=0\r\nrestore=office\r\n\r\n"
            + "[office]\r\nname=办公\r\nwidth=" + orig.dmPelsWidth + "\r\nheight=" + orig.dmPelsHeight + "\r\nscaling=unchanged\r\n\r\n"
            + "[game]\r\nname=假游戏\r\nwidth=2560\r\nheight=1440\r\nscaling=fullscreen-gpu\r\nprocess=rsprobe-game\r\n",
            new UTF8Encoding(false));

        string log = Path.Combine(sandbox, "portal-jump.log");
        Process tray = null;
        Process game = null;
        try
        {
            // 停掉真实托盘（全局单实例互斥体）
            foreach (Process p in Process.GetProcessesByName("PortalJump"))
            {
                try { p.Kill(); } catch { }
            }
            Thread.Sleep(1200);

            Console.WriteLine("== 启动托盘 ==");
            tray = Process.Start(Path.Combine(sandbox, "PortalJump.exe"), "tray");
            Thread.Sleep(4500);
            Check("托盘在运行", tray != null && !tray.HasExited, "");

            Console.WriteLine();
            Console.WriteLine("== 启动假游戏，应自动切到 2560x1440 ==");
            game = Process.Start(Path.Combine(sandbox, "rsprobe-game.exe"));
            bool switched = WaitFor(log, "Apply 开始: game", 20000);
            Check("检测到程序并触发切换", switched, "");
            Thread.Sleep(3000);
            var after = Cur();
            Check("分辨率已切到 2560x1440", after.dmPelsWidth == 2560 && after.dmPelsHeight == 1440, CurText());

            Console.WriteLine();
            Console.WriteLine("== 关键场景：模拟游戏把分辨率改回原生 ==");
            int origHz = orig.dmDisplayFrequency;
            ForceMode(orig.dmPelsWidth, orig.dmPelsHeight, origHz);
            Console.WriteLine("  已强制改回 " + CurText() + "（模拟游戏自己设分辨率）");

            // 看门狗应在若干个 tick 内纠正
            bool repaired = WaitForResolution(2560, 1440, 15000);
            Check("看门狗自动把分辨率纠正回 2560x1440", repaired, CurText());
            Check("日志记录了纠正动作", LogContains(log, "检测到分辨率被程序改回"), "");

            Console.WriteLine();
            Console.WriteLine("== 关闭游戏，应恢复到办公预设 ==");
            try { game.Kill(); } catch { }
            bool restored = WaitFor(log, "Apply 开始: office", 20000);
            Check("程序退出后自动恢复", restored, "");
            Thread.Sleep(2500);
            var fin = Cur();
            Check("分辨率回到 " + orig.dmPelsWidth + "x" + orig.dmPelsHeight,
                fin.dmPelsWidth == orig.dmPelsWidth && fin.dmPelsHeight == orig.dmPelsHeight, CurText());

            Check("没有崩溃日志", !File.Exists(Path.Combine(sandbox, "portal-jump-crash.log")), "");

            Console.WriteLine();
            Console.WriteLine("== 日志 ==");
            if (File.Exists(log))
            {
                foreach (string line in File.ReadAllLines(log, Encoding.UTF8)) Console.WriteLine("  | " + line);
            }
        }
        finally
        {
            try { if (game != null && !game.HasExited) game.Kill(); } catch { }
            try { if (tray != null && !tray.HasExited) tray.Kill(); } catch { }
            Thread.Sleep(600);

            Console.WriteLine();
            Console.WriteLine("== 还原 ==");
            ForceMode(orig.dmPelsWidth, orig.dmPelsHeight, orig.dmDisplayFrequency);
            Console.WriteLine("  现在: " + CurText());
            try { Directory.Delete(sandbox, true); } catch { }

            // 恢复真实托盘
            try
            {
                Process.Start(exe, "tray");
                Thread.Sleep(1500);
                Console.WriteLine("  已重新启动真实托盘");
            }
            catch (Exception e) { Console.WriteLine("  重启托盘失败: " + e.Message); }
        }

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? "全部通过 ✔" : (_fail + " 项失败 ✘"));
        Environment.ExitCode = _fail == 0 ? 0 : 1;
    }

    static bool WaitForResolution(int w, int h, int timeoutMs)
    {
        int waited = 0;
        while (waited < timeoutMs)
        {
            var d = Cur();
            if (d.dmPelsWidth == w && d.dmPelsHeight == h) return true;
            Thread.Sleep(400);
            waited += 400;
        }
        return false;
    }

    static bool LogContains(string path, string text)
    {
        try { return File.Exists(path) && File.ReadAllText(path, Encoding.UTF8).Contains(text); }
        catch { return false; }
    }

    static bool WaitFor(string path, string text, int timeoutMs)
    {
        int waited = 0;
        while (waited < timeoutMs)
        {
            if (LogContains(path, text)) return true;
            Thread.Sleep(400);
            waited += 400;
        }
        return false;
    }
}
