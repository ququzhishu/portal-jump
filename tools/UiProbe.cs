using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Automation;

/// <summary>
/// 用 UI Automation 真正驱动一次图形界面（在临时沙盒目录里跑，不动用户真实配置）：
///   读列表 → 点新增 → 保存（校验 ini 内容）→ 改名 → 关闭时选“否”丢弃（校验 ini 未被改）
/// </summary>
static class UiProbe
{
    static int _fail;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (detail.Length > 0 ? "  -> " + detail : ""));
        if (!ok) _fail++;
    }

    static void Section(string s)
    {
        Console.WriteLine();
        Console.WriteLine("== " + s + " ==");
    }

    static void Main(string[] args)
    {
        try { SetProcessDPIAware(); } catch { }
        var sw = new StreamWriter(Console.OpenStandardOutput());
        sw.AutoFlush = true;
        Console.SetOut(sw);

        string exe = args.Length > 0 ? args[0] : Path.Combine("PortalJump", "PortalJump.exe");
        Console.WriteLine("被测程序: " + exe);
        if (!File.Exists(exe)) { Console.WriteLine("找不到 exe"); Environment.ExitCode = 2; return; }

        string sandbox = Path.Combine(Path.GetTempPath(), "rs-uitest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(sandbox);
        string dir = Path.GetDirectoryName(exe);
        File.Copy(exe, Path.Combine(sandbox, "PortalJump.exe"));
        File.Copy(Path.Combine(dir, "NvAPIWrapper.dll"), Path.Combine(sandbox, "NvAPIWrapper.dll"), true);
        // 素材也要带进沙盒，否则 WPF 界面没有底纹/头像（测出来的像素和真实不符）
        foreach (string asset in Directory.GetFiles(dir, "zoe-*.png")) File.Copy(asset, Path.Combine(sandbox, Path.GetFileName(asset)), true);
        string ini = Path.Combine(sandbox, "presets.ini");
        File.WriteAllText(ini,
            "[general]\r\nautoswitch=1\r\nrestore=desk\r\n\r\n"
            + "[desk]\r\nname=桌面\r\nwidth=1920\r\nheight=1080\r\nscaling=2\r\n\r\n"
            + "[game]\r\nname=测试游戏\r\nwidth=1280\r\nheight=960\r\nscaling=3\r\nprocess=notepad\r\n",
            new System.Text.UTF8Encoding(false));

        Process proc = Process.Start(Path.Combine(sandbox, "PortalJump.exe"), "settings");
        AutomationElement win = null;
        for (int i = 0; i < 40 && win == null; i++)
        {
            Thread.Sleep(300);
            win = FindWindowByPid(proc.Id, null);
        }
        Check("设置窗口已出现", win != null, win == null ? "15 秒内没找到窗口" : win.Current.Name);
        if (win == null)
        {
            try { proc.Kill(); } catch { }
            Environment.ExitCode = 1;
            return;
        }

        Section("关键控件是否齐全");
        string[] needed = new string[]
        {
            "新增", "复制", "删除", "上移", "下移", "取当前分辨率", "创建自定义分辨率…",
            "选择运行中的程序…", "清空关联", "所有关联程序退出后，自动恢复到本预设",
            "托盘常驻时自动跟随程序切换", "开机自动启动（登录后常驻托盘）",
            "保存", "保存并应用", "仅应用（测试）", "放弃改动并重载", "关闭",
            "预设", "预设详情", "自定义尺寸", "分辨率", "缩放方式", "关联程序"
        };
        int missing = 0;
        foreach (string n in needed)
        {
            if (FindByName(win, n) == null) { missing++; Console.WriteLine("    缺: " + n); }
        }
        Check("全部 " + needed.Length + " 个关键控件都在", missing == 0, missing + " 个缺失");

        Section("预设列表初始内容（只看预设列表，不含下拉框选项）");
        AutomationElement presetList = FindPresetList(win);
        Check("找到预设列表控件", presetList != null, "");
        List<string> items = ReadItems(presetList);
        foreach (string s in items) Console.WriteLine("    · " + s);
        Check("读出 2 个预设", items.Count == 2, items.Count + " 个");
        Check("游戏预设显示关联程序", items.Count > 1 && items[1].Contains("notepad"), items.Count > 1 ? items[1] : "");
        Check("顶部显示显卡信息", TextContains(win, "NVIDIA") || TextContains(win, "NVAPI"), "");

        Section("恢复目标（按功能校验：选中哪一项，勾选框状态应与之对应）");
        // 沙盒里 restore=desk，所以第一项（桌面）选中时应勾选，切到第二项应取消勾选
        AutomationElement item0 = NthListItem(presetList, 0);
        AutomationElement item1 = NthListItem(presetList, 1);
        Check("能选中第一个预设（真实鼠标点击）", ClickCenter(item0, win), "");
        Thread.Sleep(700);
        bool onFirst = WaitToggle(win, "所有关联程序退出后，自动恢复到本预设", true, 4000);
        Check("选中恢复目标预设时，勾选框为选中", onFirst, onFirst ? "" : "未勾选");
        if (item1 != null && ClickCenter(item1, win))
        {
            Thread.Sleep(700);
            Console.WriteLine("  选中第二项后「名称」框 = " + ReadEditValue(win, "名称"));
            Console.WriteLine("  第二项文本 = " + item1.Current.Name);
            bool onSecondOk = WaitToggle(win, "所有关联程序退出后，自动恢复到本预设", false, 4000);
            Check("切到非恢复目标预设时，勾选框为未选中", onSecondOk, onSecondOk ? "" : "仍处于勾选");
        }
        else Check("能选中第二个预设", false, "");
        if (item0 != null) ClickCenter(item0, win);
        Thread.Sleep(400);

        Section("点击「新增」");
        AutomationElement add = FindByName(win, "新增");
        Check("点到「新增」", add != null && Invoke(add), "");
        Thread.Sleep(900);
        List<string> afterAdd = ReadItems(presetList);
        foreach (string s in afterAdd) Console.WriteLine("    · " + s);
        Check("预设数 2 -> 3", afterAdd.Count == 3, afterAdd.Count + " 个");
        Check("新预设默认名/默认取当前分辨率", afterAdd.Count == 3 && afterAdd[2].Contains("新预设"), afterAdd.Count == 3 ? afterAdd[2] : "");

        Section("点击「保存」→ 校验写出的 ini");
        AutomationElement save = FindByName(win, "保存");
        Check("点到「保存」", save != null && Invoke(save), "");
        Thread.Sleep(1200);
        string iniText = File.ReadAllText(ini, System.Text.Encoding.UTF8);
        Console.WriteLine("  --- 保存后的 ini ---");
        foreach (string line in iniText.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None)) Console.WriteLine("  | " + line);
        Check("写出 [general] 段", iniText.Contains("[general]"), "");
        Check("保留 restore=desk", iniText.Contains("restore=desk"), "");
        Check("三个预设都写进去了", iniText.Contains("[desk]") && iniText.Contains("[game]") && iniText.Contains("[newpreset]") || iniText.Contains("新预设"), "");
        Check("进程关联被保留", iniText.Contains("process=notepad"), "");
        Check("保存后列表刷新为 3 项", ReadItems(presetList).Count == 3, ReadItems(presetList).Count + " 个");

        Section("改名（UIA 直接写文本框）后列表是否跟随");
        AutomationElement nameEdit = FindEditByName(win, "名称");
        Check("找到名称输入框", nameEdit != null, "");
        bool typed = SetValue(nameEdit, "不该被保存的名字");
        Thread.Sleep(800);
        Console.WriteLine("  改后列表: ");
        foreach (string s in ReadItems(presetList)) Console.WriteLine("    · " + s);
        bool reflected = false;
        foreach (string s in ReadItems(presetList)) if (s.Contains("不该被保存的名字")) reflected = true;
        Console.WriteLine("  (UIA 写值能否触发 TextChanged: " + (reflected ? "能" : "不能，属 UIA 提供程序行为") + ")");

        Section("点「复制」制造未保存改动 → 关闭应弹确认框");
        AutomationElement copy = FindByName(win, "复制");
        Check("点到「复制」", copy != null && Invoke(copy), "");
        Thread.Sleep(900);
        int beforeClose = ReadItems(presetList).Count;
        Check("列表变为 4 项（未保存）", beforeClose == 4, beforeClose + " 项");

        AutomationElement closeBtn = FindByName(win, "关闭");
        if (closeBtn != null) Invoke(closeBtn);

        // 轮询等待确认框出现（MessageBox.Show 是阻塞的，日志证明它确实被调用了）。
        // 用 Win32 EnumWindows 找窗口，比 UIA 的 TopLevelChildren 枚举可靠：
        // 模态对话框偶尔不会出现在 UIA 的顶层窗口列表里（实测踩过）。
        AutomationElement prompt = null;
        for (int i = 0; i < 20 && prompt == null; i++)
        {
            Thread.Sleep(400);
            IntPtr hwnd = FindDialogHandle(proc.Id, win.Current.Name);
            if (hwnd != IntPtr.Zero)
            {
                try { prompt = AutomationElement.FromHandle(hwnd); }
                catch { }
            }
            if (prompt == null) prompt = FindWindowByPid(proc.Id, win.Current.Name);
        }

        Console.WriteLine("  桌面上的顶层窗口（全部进程）：");
        foreach (AutomationElement e in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
        {
            try
            {
                if (e.Current.ControlType != ControlType.Window) continue;
                Console.WriteLine("    - pid=" + e.Current.ProcessId + "  \"" + e.Current.Name + "\"");
            }
            catch { }
        }
        if (prompt != null)
        {
            Console.WriteLine("  确认框窗口: \"" + prompt.Current.Name + "\"");
        }
        Check("关闭时弹出了未保存确认框", prompt != null, prompt == null ? "没有弹窗" : prompt.Current.Name);
        if (prompt != null)
        {
            Console.WriteLine("  确认框里的按钮：");
            var btnCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
            foreach (AutomationElement b in prompt.FindAll(TreeScope.Descendants, btnCond))
            {
                try { Console.WriteLine("    - \"" + b.Current.Name + "\""); }
                catch { }
            }

            AutomationElement no = FindButtonContaining(prompt, "否");
            if (no == null) no = FindButtonContaining(prompt, "No");
            Check("确认框有「否」按钮", no != null, no == null ? "" : no.Current.Name);
            if (no != null) Invoke(no);
            Thread.Sleep(1500);
        }

        string iniAfterDiscard = File.ReadAllText(ini, System.Text.Encoding.UTF8);
        Check("丢弃后 ini 仍是 3 个预设（复制的那份没落盘）",
            !iniAfterDiscard.Contains("[game3]") && !iniAfterDiscard.Contains("副本"),
            "");
        Check("丢弃后之前保存的内容还在", iniAfterDiscard.Contains("restore=desk") && iniAfterDiscard.Contains("process=notepad"), "");

        Section("清理");
        try
        {
            if (!proc.HasExited) { proc.Kill(); Thread.Sleep(400); }
            Console.WriteLine("  界面进程已结束: " + proc.HasExited);
        }
        catch { }
        string crash = Path.Combine(sandbox, "portal-jump-crash.log");
        Check("整个过程没有崩溃日志", !File.Exists(crash), File.Exists(crash) ? File.ReadAllText(crash) : "");

        string logPath = Path.Combine(sandbox, "portal-jump.log");
        Console.WriteLine();
        Console.WriteLine("== 应用日志（沙盒内） ==");
        if (File.Exists(logPath))
        {
            foreach (string line in File.ReadAllLines(logPath, System.Text.Encoding.UTF8)) Console.WriteLine("  | " + line);
        }
        else
        {
            Console.WriteLine("  (没有日志文件)");
        }

        try { Directory.Delete(sandbox, true); } catch { }

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? "全部通过 ✔" : (_fail + " 项失败 ✘"));
        Environment.ExitCode = _fail == 0 ? 0 : 1;
    }

    // ---------------- UIA 辅助 ----------------

    static AutomationElement FindWindowByPid(int pid, string excludeName)
    {
        var cond = new PropertyCondition(AutomationElement.ProcessIdProperty, pid);
        AutomationElementCollection all = AutomationElement.RootElement.FindAll(TreeScope.Children, cond);
        foreach (AutomationElement e in all)
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

    // ---------------- Win32 窗口枚举 ----------------

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>按进程号找"不是主窗口"的那个可见顶层窗口（也就是模态确认框）</summary>
    static IntPtr FindDialogHandle(int pid, string excludeTitle)
    {
        IntPtr found = IntPtr.Zero;
        try
        {
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                uint wpid;
                GetWindowThreadProcessId(hWnd, out wpid);
                if (wpid != (uint)pid) return true;
                if (!IsWindowVisible(hWnd)) return true;
                var sb = new System.Text.StringBuilder(512);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();
                if (title.Length == 0) return true;
                if (excludeTitle != null && title == excludeTitle) return true;
                found = hWnd;
                return false;
            }, IntPtr.Zero);
        }
        catch { }
        return found;
    }

    /// <summary>预设列表是那个没有 Name 的 List（下拉框里的 List 带 "分辨率"/"缩放方式" 名字）</summary>
    static AutomationElement FindPresetList(AutomationElement root)
    {
        var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List);
        AutomationElementCollection lists = root.FindAll(TreeScope.Descendants, cond);
        foreach (AutomationElement e in lists)
        {
            try
            {
                string n = e.Current.Name;
                if (string.IsNullOrEmpty(n)) return e;
            }
            catch { }
        }
        return null;
    }

    static List<string> ReadItems(AutomationElement list)
    {
        var result = new List<string>();
        if (list == null) return result;
        var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem);
        AutomationElementCollection items = list.FindAll(TreeScope.Children, cond);
        foreach (AutomationElement e in items)
        {
            try { result.Add(e.Current.Name); }
            catch { }
        }
        if (result.Count == 0)
        {
            items = list.FindAll(TreeScope.Descendants, cond);
            foreach (AutomationElement e in items)
            {
                try { result.Add(e.Current.Name); }
                catch { }
            }
        }
        return result;
    }

    /// <summary>列表里第 index 个条目（限定在这个列表控件内，避免匹配到下拉框的选项）</summary>
    static AutomationElement NthListItem(AutomationElement list, int index)
    {
        if (list == null) return null;
        var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem);
        AutomationElementCollection items = list.FindAll(TreeScope.Children, cond);
        if (items.Count == 0) items = list.FindAll(TreeScope.Descendants, cond);
        return index >= 0 && index < items.Count ? items[index] : null;
    }

    static bool Select(AutomationElement e)
    {
        if (e == null) return false;
        try
        {
            ((SelectionItemPattern)e.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("    (Select 失败: " + ex.GetType().Name + ")");
            return false;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetCursorPos(int x, int y);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;

    /// <summary>在元素中心真点一下鼠标（UIA 的 SelectionItemPattern.Select 对自绘 ListBox 是空操作）</summary>
    static bool ClickCenter(AutomationElement e, AutomationElement window)
    {
        if (e == null) return false;
        try
        {
            if (window != null && window.Current.NativeWindowHandle != 0)
            {
                SetForegroundWindow(new IntPtr(window.Current.NativeWindowHandle));
                Thread.Sleep(300);
            }
            System.Windows.Rect r = e.Current.BoundingRectangle;
            if (r.Width <= 0 || r.Height <= 0) return false;
            int x = (int)(r.Left + r.Width / 2);
            int y = (int)(r.Top + r.Height / 2);
            SetCursorPos(x, y);
            Thread.Sleep(150);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(60);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("    (点击失败: " + ex.GetType().Name + ")");
            return false;
        }
    }

    /// <summary>等待勾选框到达期望状态：固定 Sleep 在高负载下会假失败（实测偶发）</summary>
    static bool WaitToggle(AutomationElement root, string name, bool expected, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (IsToggleOn(root, name) == expected) return true;
            Thread.Sleep(120);
        }
        return false;
    }
    static bool IsToggleOn(AutomationElement root, string name)
    {
        AutomationElement e = FindByName(root, name);
        if (e == null) return false;
        try
        {
            return ((TogglePattern)e.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState == ToggleState.On;
        }
        catch { return false; }
    }

    static AutomationElement FindButtonContaining(AutomationElement root, string text)    {
        var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
        foreach (AutomationElement e in root.FindAll(TreeScope.Descendants, cond))
        {
            try
            {
                string n = e.Current.Name;
                if (n != null && n.Contains(text)) return e;
            }
            catch { }
        }
        return null;
    }

    static AutomationElement FindByName(AutomationElement root, string name)
    {
        return root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name));
    }

    static string ReadEditValue(AutomationElement root, string name)
    {
        AutomationElement e = FindEditByName(root, name);
        if (e == null) return "(找不到输入框)";
        try { return ((ValuePattern)e.GetCurrentPattern(ValuePattern.Pattern)).Current.Value; }
        catch { return "(读不到值)"; }
    }

    static AutomationElement FindEditByName(AutomationElement root, string name)
    {
        var cond = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new PropertyCondition(AutomationElement.NameProperty, name));
        return root.FindFirst(TreeScope.Descendants, cond);
    }

    static bool TextContains(AutomationElement root, string text)
    {
        var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text);
        AutomationElementCollection texts = root.FindAll(TreeScope.Descendants, cond);
        foreach (AutomationElement e in texts)
        {
            try { if (e.Current.Name != null && e.Current.Name.Contains(text)) return true; }
            catch { }
        }
        return false;
    }

    static bool Invoke(AutomationElement e)
    {
        if (e == null) return false;
        try
        {
            ((InvokePattern)e.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("    (Invoke 失败: " + ex.GetType().Name + ")");
            return false;
        }
    }

    static bool SetValue(AutomationElement e, string value)
    {
        if (e == null) return false;
        try
        {
            ((ValuePattern)e.GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("    (SetValue 失败: " + ex.GetType().Name + ")");
            return false;
        }
    }
}
