using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Automation;

namespace PortalJump.Tools
{
    /// <summary>
    /// 开发用探针：驱动 WPF 的「创建自定义分辨率」对话框，验证宽视距按钮算出来的尺寸对不对。
    ///
    ///   UltraProbe &lt;PortalJump.UI.exe&gt;
    ///
    /// 检查项（当前显示器横向 3840 时）：
    ///   21:9  -> 3840 x 1646（3840 × 9 ÷ 21 = 1645.7，取偶数 1646）
    ///   32:9  -> 3840 x 1080（3840 × 9 ÷ 32 = 1080）
    /// </summary>
    static class UltraProbe
    {
        static int _fail;

        static void Check(string what, bool ok, string detail)
        {
            Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + what + (detail.Length > 0 ? "  -> " + detail : ""));
            if (!ok) _fail++;
        }

        [STAThread]
        static int Main(string[] args)
        {
            string exe = args.Length > 0 ? args[0] : Path.Combine("PortalJump", "PortalJump.UI.exe");
            if (!File.Exists(exe)) { Console.WriteLine("找不到 exe: " + exe); return 2; }
            string dir = Path.GetDirectoryName(Path.GetFullPath(exe));

            string sandbox = Path.Combine(Path.GetTempPath(), "rs-ultra-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(sandbox);
            string uiExe = Path.Combine(sandbox, "PortalJump.UI.exe");
            File.Copy(exe, uiExe);
            File.Copy(Path.Combine(dir, "NvAPIWrapper.dll"), Path.Combine(sandbox, "NvAPIWrapper.dll"), true);
            foreach (string a in Directory.GetFiles(dir, "zoe-*.png")) File.Copy(a, Path.Combine(sandbox, Path.GetFileName(a)), true);
            if (File.Exists(Path.Combine(dir, "presets.ini"))) File.Copy(Path.Combine(dir, "presets.ini"), Path.Combine(sandbox, "presets.ini"), true);

            Process proc = Process.Start(uiExe, "settings");
            try
            {
                AutomationElement win = WaitWindow(proc.Id, 20000);
                if (win == null) { Console.WriteLine("设置界面没起来"); return 2; }

                AutomationElement open = FindByName(win, "创建自定义分辨率…");
                Check("找到「创建自定义分辨率…」按钮", open != null, "");
                if (open == null) return 1;
                BringToFront(win);
                bool invoked = ClickCenter(open);
                Check("点到了「创建自定义分辨率…」", invoked, "");
                Thread.Sleep(1500);
                Console.WriteLine("  进程是否还活着: " + (!proc.HasExited));
                Console.WriteLine("  --- 全桌面里名字相关的窗口/元素 ---");
                foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
                {
                    try
                    {
                        string nm = w.Current.Name;
                        if (nm != null && (nm.Contains("Portal") || nm.Contains("折返") || nm.Contains("创建") || nm.Contains("NVAPI") || nm.Contains("NVIDIA")))
                            Console.WriteLine("      窗口[" + w.Current.ProcessId + "] \"" + nm + "\"");
                    }
                    catch { }
                }
                Console.WriteLine("  --- 设置窗口里所有含「创建」「自定义」「分辨率」的文字/按钮 ---");
                foreach (AutomationElement e in win.FindAll(TreeScope.Descendants, Condition.TrueCondition))
                {
                    try
                    {
                        string nm = e.Current.Name;
                        if (nm != null && (nm.Contains("创建") || nm.Contains("自定义") || nm.Contains("NVAPI")))
                            Console.WriteLine("      [" + e.Current.ControlType.ProgrammaticName + "] \"" + nm + "\"");
                    }
                    catch { }
                }
                // 注意：WPF 的 owned 对话框在 UIA 树里是**挂在属主窗口下面**的，
                // 不是桌面根节点的直接子节点 —— 只搜根节点会误判成"没打开"（踩过）。
                AutomationElement dlg = null;
                for (int i = 0; i < 40 && dlg == null; i++)
                {
                    var condDlg = new AndCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                        new PropertyCondition(AutomationElement.NameProperty, "创建自定义分辨率"));
                    dlg = win.FindFirst(TreeScope.Descendants, condDlg);
                    if (dlg == null) Thread.Sleep(250);
                }
                Check("对话框已打开", dlg != null, "");
                if (dlg == null) return 1;

                AutomationElement baseHint = FindByName(dlg, "基准横向 3840px");
                Console.WriteLine("  基准提示: " + (baseHint != null ? baseHint.Current.Name : "(没读到)"));

                // 21:9
                Invoke(FindByName(dlg, "21:9 宽视距"));
                Thread.Sleep(600);
                int w1 = ReadEdit(dlg, "宽度输入"), h1 = ReadEdit(dlg, "高度输入");
                Check("21:9 填入 3840x1646", w1 == 3840 && h1 == 1646, w1 + "x" + h1);

                // 32:9
                Invoke(FindByName(dlg, "32:9 超宽视距"));
                Thread.Sleep(600);
                int w2 = ReadEdit(dlg, "宽度输入"), h2 = ReadEdit(dlg, "高度输入");
                Check("32:9 填入 3840x1080", w2 == 3840 && h2 == 1080, w2 + "x" + h2);

                AutomationElement hint = TextContaining(dlg, "纵向");
                Console.WriteLine("  说明文字: " + (hint != null ? hint.Current.Name : "(没读到)"));

                Invoke(FindByName(dlg, "取消"));
                Thread.Sleep(400);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  异常: " + ex.Message);
                _fail++;
            }
            finally
            {
                try { if (!proc.HasExited) proc.Kill(); } catch { }
                try { Directory.Delete(sandbox, true); } catch { }
            }

            Console.WriteLine(_fail == 0 ? "全部通过 ✔" : (_fail + " 项失败"));
            return _fail == 0 ? 0 : 1;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetCursorPos(int x, int y);

        /// <summary>把窗口置前并最大化到前台，否则鼠标点击会落到盖在上面的终端窗口上</summary>
        static void BringToFront(AutomationElement win)
        {
            try
            {
                IntPtr h = new IntPtr(win.Current.NativeWindowHandle);
                ShowWindow(h, 9);        // SW_RESTORE
                SetForegroundWindow(h);
                Thread.Sleep(400);
            }
            catch { }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);

        /// <summary>真实鼠标点击元素中心（比 UIA Invoke 更接近用户操作，实测这个按钮只认鼠标输入）</summary>
        static bool ClickCenter(AutomationElement e)
        {
            if (e == null) return false;
            try
            {
                System.Windows.Rect r = e.Current.BoundingRectangle;
                if (r.Width <= 0 || r.Height <= 0) return false;
                int x = (int)(r.Left + r.Width / 2), y = (int)(r.Top + r.Height / 2);
                SetCursorPos(x, y);
                Thread.Sleep(120);
                mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);   // left down
                mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);   // left up
                Thread.Sleep(120);
                return true;
            }
            catch { return false; }
        }
        static AutomationElement WaitWindow(int pid, int timeoutMs, string titlePart = null)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                AutomationElement root = AutomationElement.RootElement;
                var cond = new PropertyCondition(AutomationElement.ProcessIdProperty, pid);
                AutomationElementCollection wins = root.FindAll(TreeScope.Children, cond);
                foreach (AutomationElement w in wins)
                {
                    try
                    {
                        if (w.Current.ControlType != ControlType.Window) continue;
                        if (titlePart == null || (w.Current.Name != null && w.Current.Name.Contains(titlePart)))
                        {
                            if (w.Current.Name != null && w.Current.Name.Length > 0) return w;
                        }
                    }
                    catch { }
                }
                Thread.Sleep(250);
            }
            return null;
        }

        static AutomationElement FindByName(AutomationElement root, string name)
        {
            return root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name));
        }

        static AutomationElement TextContaining(AutomationElement root, string part)
        {
            var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text);
            foreach (AutomationElement e in root.FindAll(TreeScope.Descendants, cond))
            {
                try { if (e.Current.Name != null && e.Current.Name.Contains(part)) return e; }
                catch { }
            }
            return null;
        }

        static int ReadEdit(AutomationElement root, string name)
        {
            var cond = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.NameProperty, name));
            AutomationElement e = root.FindFirst(TreeScope.Descendants, cond);
            if (e == null) return -1;
            try { return int.Parse(((ValuePattern)e.GetCurrentPattern(ValuePattern.Pattern)).Current.Value.Trim()); }
            catch { return -1; }
        }

        static bool Invoke(AutomationElement e)
        {
            if (e == null) return false;
            try { ((InvokePattern)e.GetCurrentPattern(InvokePattern.Pattern)).Invoke(); return true; }
            catch { return false; }
        }
    }
}
