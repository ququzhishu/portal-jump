using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

/// <summary>
/// 给界面截图（截的是屏幕上真实显示的样子，含 DPI 缩放后的效果），用来肉眼检查布局与清晰度。
/// 用法：Shot.exe &lt;exe&gt; &lt;out.png&gt; [mode] [先点这个按钮再截新窗口]
/// 会在临时沙盒里跑，不动用户真实配置。
/// </summary>
static class Shot
{
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out RECT r);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    static void Main(string[] args)
    {
        var sw = new StreamWriter(Console.OpenStandardOutput());
        sw.AutoFlush = true;
        Console.SetOut(sw);

        try { SetProcessDPIAware(); } catch { }

        using (var screen = Graphics.FromHwnd(IntPtr.Zero))
        {
            Console.WriteLine("屏幕 DPI: " + screen.DpiX + " x " + screen.DpiY
                + "   缩放 " + Math.Round(screen.DpiX / 96.0 * 100) + "%");
        }
        Console.WriteLine("桌面尺寸: " + SystemInformation.VirtualScreen.Width + "x" + SystemInformation.VirtualScreen.Height);

        if (args.Length < 2) { Console.WriteLine("用法: Shot.exe <exe> <out.png> [mode] [clickButton]"); Environment.ExitCode = 2; return; }

        string exe = args[0];
        string outPng = args[1];
        string mode = args.Length > 2 ? args[2] : "settings";
        string clickButton = args.Length > 3 ? args[3] : null;

        string dir = Path.GetDirectoryName(exe);
        string sandbox = Path.Combine(Path.GetTempPath(), "rs-shot-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(sandbox);
        File.Copy(exe, Path.Combine(sandbox, "PortalJump.exe"));
        File.Copy(Path.Combine(dir, "NvAPIWrapper.dll"), Path.Combine(sandbox, "NvAPIWrapper.dll"), true);
        // 素材（头图/头像）也要带进沙盒，否则界面里看不到插画
        foreach (string asset in Directory.GetFiles(dir, "zoe-*.png")) File.Copy(asset, Path.Combine(sandbox, Path.GetFileName(asset)), true);
        File.WriteAllText(Path.Combine(sandbox, "presets.ini"),
            "; 截图沙盒配置\r\n[general]\r\nautoswitch=1\r\nrestore=desktop\r\n\r\n"
            + "[desktop]\r\nname=桌面\r\nwidth=3840\r\nheight=2160\r\nscaling=2\r\n\r\n"
            + "[lol]\r\nname=英雄联盟\r\nwidth=3840\r\nheight=1080\r\nscaling=3\r\nprocess=League of Legends\r\n\r\n"
            + "[fps]\r\nname=射击游戏（示例）\r\nwidth=2560\r\nheight=1600\r\nscaling=5\r\nprocess=game1;game1-launcher\r\n",
            new System.Text.UTF8Encoding(false));

        Process proc = Process.Start(Path.Combine(sandbox, "PortalJump.exe"), mode);
        IntPtr hwnd = IntPtr.Zero;
        for (int i = 0; i < 60; i++)
        {
            Thread.Sleep(300);
            proc.Refresh();
            if (proc.MainWindowHandle != IntPtr.Zero) { hwnd = proc.MainWindowHandle; break; }
        }
        if (hwnd == IntPtr.Zero) { Console.WriteLine("没拿到主窗口"); try { proc.Kill(); } catch { } Environment.ExitCode = 1; return; }
        Thread.Sleep(1800);

        if (clickButton != null)
        {
            AutomationElement win = FindWindow(proc.Id, null);
            AutomationElement btn = win == null ? null : win.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, clickButton));
            if (btn != null)
            {
                try { ((InvokePattern)btn.GetCurrentPattern(InvokePattern.Pattern)).Invoke(); Console.WriteLine("已点击: " + clickButton); }
                catch (Exception e) { Console.WriteLine("点击失败: " + e.Message); }
            }
            else Console.WriteLine("没找到按钮: " + clickButton);
            Thread.Sleep(2600);

            AutomationElement dlg = FindWindow(proc.Id, win == null ? null : win.Current.Name);
            if (dlg != null && dlg.Current.NativeWindowHandle != 0)
            {
                hwnd = new IntPtr(dlg.Current.NativeWindowHandle);
                Console.WriteLine("改为截取对话框: " + dlg.Current.Name);
            }
        }

        try { SetForegroundWindow(hwnd); } catch { }
        Thread.Sleep(900);

        RECT r;
        GetWindowRect(hwnd, out r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        Console.WriteLine("窗口矩形: " + w + "x" + h + " @ (" + r.Left + "," + r.Top + ")");

        Bitmap bmp = new Bitmap(Math.Max(w, 1), Math.Max(h, 1), PixelFormat.Format32bppArgb);
        bool captured = false;
        try
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
            }
            captured = !IsMostlyBlank(bmp);
            Console.WriteLine("屏幕抓取: " + (captured ? "成功" : "疑似空白，改用 PrintWindow"));
        }
        catch (Exception e) { Console.WriteLine("屏幕抓取失败: " + e.Message); }

        if (!captured)
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(hwnd, hdc, 2);
                g.ReleaseHdc(hdc);
            }
        }

        bmp.Save(outPng, ImageFormat.Png);
        bmp.Dispose();

        // 再存一张"只渲染窗口自身内容"的版本（PrintWindow），
        // 用来区分画面上的痕迹到底是本程序的，还是屏幕上别的东西
        using (var bmp2 = new Bitmap(Math.Max(w, 1), Math.Max(h, 1), PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp2))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(hwnd, hdc, 2);
                g.ReleaseHdc(hdc);
            }
            string printPath = Path.ChangeExtension(outPng, null) + ".print.png";
            bmp2.Save(printPath, ImageFormat.Png);
            Console.WriteLine("已保存(仅窗口内容): " + printPath);
        }

        Console.WriteLine("已保存: " + outPng);

        try { proc.Kill(); } catch { }
        Thread.Sleep(400);
        try { Directory.Delete(sandbox, true); } catch { }
        Environment.ExitCode = 0;
    }

    static bool IsMostlyBlank(Bitmap b)
    {
        int same = 0, total = 0;
        Color first = b.GetPixel(0, 0);
        for (int y = 0; y < b.Height; y += 7)
        {
            for (int x = 0; x < b.Width; x += 7)
            {
                total++;
                Color c = b.GetPixel(x, y);
                if (Math.Abs(c.R - first.R) + Math.Abs(c.G - first.G) + Math.Abs(c.B - first.B) < 6) same++;
            }
        }
        return total == 0 || same > total * 0.97;
    }

    static AutomationElement FindWindow(int pid, string excludeName)
    {
        var cond = new PropertyCondition(AutomationElement.ProcessIdProperty, pid);
        foreach (AutomationElement e in AutomationElement.RootElement.FindAll(TreeScope.Children, cond))
        {
            try
            {
                if (e.Current.ControlType != ControlType.Window) continue;
                if (excludeName != null && e.Current.Name == excludeName) continue;
                return e;
            }
            catch { }
        }
        foreach (AutomationElement e in AutomationElement.RootElement.FindAll(TreeScope.Descendants, cond))
        {
            try
            {
                if (e.Current.ControlType != ControlType.Window) continue;
                if (excludeName != null && e.Current.Name == excludeName) continue;
                return e;
            }
            catch { }
        }
        return null;
    }
}
