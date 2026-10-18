using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PortalJump
{
    static class Program
    {
        static string ExeDir
        {
            get { return AppPaths.ExeDir; }
        }

        static string IniPath
        {
            get { return AppPaths.IniPath; }
        }

        static string LogPath
        {
            get { return AppPaths.LogPath; }
        }

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        static int Main(string[] args)
        {
            // 崩溃时把类型/消息/堆栈写到文件，方便在别人的机器上排查（图形程序没有控制台）
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                WriteCrash(e.ExceptionObject as Exception, "AppDomain.UnhandledException");
            };
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                WriteCrash(e.Exception, "Application.ThreadException");
            };

            try
            {
                return RunMain(args);
            }
            catch (Exception e)
            {
                WriteCrash(e, "Main");
                return 1;
            }
        }

        static void WriteCrash(Exception e, string source)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  [" + source + "] ====");
                if (e == null)
                {
                    sb.AppendLine("(异常对象为空)");
                }
                else
                {
                    sb.AppendLine("类型: " + e.GetType().FullName);
                    sb.AppendLine("消息: " + e.Message);
                    sb.AppendLine("堆栈: " + e.StackTrace);
                    Exception inner = e.InnerException;
                    int depth = 0;
                    while (inner != null && depth < 5)
                    {
                        sb.AppendLine("内层[" + depth + "]: " + inner.GetType().FullName + " - " + inner.Message);
                        inner = inner.InnerException;
                        depth++;
                    }
                }
                sb.AppendLine();
                File.AppendAllText(AppPaths.CrashLogPath, sb.ToString());
            }
            catch { }
        }

        static int RunMain(string[] args)
        {
            try { SetProcessDPIAware(); }
            catch { }

            Config config = Config.Load(IniPath);
            // 绿色程序"解压即用"：首次运行就把默认配置写出来，用户能立刻看到 presets.ini
            if (config.EnsureFile()) Switcher.Log("首次运行：已生成默认配置 " + config.FilePath);
            Switcher.TryInitNvapi();
            Switcher.Log("---- 启动 " + AppInfo.TitleWithVersion + "，参数："
                + (args.Length > 0 ? string.Join(" ", args) : "(无)") + " ----");

            // 改名后旧版的开机自启项指向已经不存在的 exe，这里顺手迁移
            string migrated = AutoStart.MigrateLegacy();
            if (migrated.Length > 0) Switcher.Log(migrated);

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
            }
            catch { }

            // ---- 命令行 ----
            if (args.Length > 0)
            {
                string key = args[0].ToLowerInvariant();

                if (key == "tray") return RunTray(config, false);
                if (key == "settings" || key == "gui" || key == "config") return RunSettings();
                if (key == "about") return RunAbout();
                if (key == "selftest" || key == "diag")
                {
                    bool quiet = args.Length > 1 && string.Equals(args[1], "quiet", StringComparison.OrdinalIgnoreCase);
                    return RunSelfTest(quiet);
                }
                if (key == "-h" || key == "--help" || key == "/?" || key == "help")
                {
                    ShowHelp(config);
                    return 0;
                }

                Preset p = config.Find(args[0]);
                if (p != null)
                {
                    string msg;
                    bool ok = Switcher.Apply(p, out msg);
                    Switcher.Log("[命令行] " + args[0] + " -> " + msg);
                    return ok ? 0 : 1;
                }

                MessageBox.Show(
                    "没有找到名为「" + args[0] + "」的预设。\n\n可用的预设：" + ListKeys(config)
                    + "\n\n用 " + AppInfo.ExeName + " settings 打开图形设置界面。",
                    AppInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 2;
            }

            // ---- 无参数：托盘常驻；若已在运行则直接打开设置界面 ----
            return RunTray(config, true);
        }

        /// <summary>环境自检：结果写文件（quiet=true 时不弹窗），便于部署到新机器时排错</summary>
        public static int RunSelfTest(bool quiet)
        {
            string report;
            try
            {
                report = Switcher.SelfTest();
            }
            catch (Exception e)
            {
                report = "自检异常：" + e.GetType().FullName + Environment.NewLine + e.Message
                    + Environment.NewLine + e.StackTrace;
            }

            string path = AppPaths.SelfTestPath;
            try { File.WriteAllText(path, report, new UTF8Encoding(false)); }
            catch { }

            if (!quiet)
            {
                try
                {
                    MessageBox.Show(report + Environment.NewLine + "（同时已保存到 " + path + "）",
                        AppInfo.Title + " 环境自检", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch { }
            }
            return 0;
        }

        /// <summary>关于页（作者标签在里面）</summary>
        public static int RunAbout()
        {
            return RunAboutOwned(null);
        }

        /// <summary>关于页，可指定 owner（从设置界面打开时居中在设置窗口上）</summary>
        public static int RunAboutOwned(IWin32Window owner)
        {
            using (var dlg = new AboutForm())
            {
                dlg.ShowDialog(owner);
            }
            return 0;
        }

        static string ListKeys(Config config)
        {
            var names = new List<string>();
            foreach (Preset p in config.Presets) names.Add(p.Key);
            return names.Count > 0 ? string.Join("、", names.ToArray()) : "(无)";
        }

        static void ShowHelp(Config config)
        {
            var sb = new StringBuilder();
            sb.AppendLine(AppInfo.TitleWithVersion + " —— " + AppInfo.Description);
            sb.AppendLine();
            sb.AppendLine("用法：");
            sb.AppendLine("  " + AppInfo.ExeName + "              启动托盘常驻（已运行则打开设置界面）");
            sb.AppendLine("  " + AppInfo.ExeName + " settings     打开图形化设置界面");
            sb.AppendLine("  " + AppInfo.ExeName + " about        打开关于页");
            sb.AppendLine("  " + AppInfo.ExeName + " <预设名>     直接切换到某个预设");
            sb.AppendLine("  " + AppInfo.ExeName + " tray         强制以托盘方式启动");
            sb.AppendLine();
            sb.AppendLine("当前预设：" + ListKeys(config));
            sb.AppendLine();
            sb.AppendLine("配置文件：" + IniPath);
            MessageBox.Show(sb.ToString(), AppInfo.Title + " 帮助", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>托盘常驻。settingsIfRunning：已有实例在跑时改为打开设置界面。</summary>
        static int RunTray(Config config, bool settingsIfRunning)
        {
            bool createdNew;
            using (var mutex = new System.Threading.Mutex(true, @"Global\" + AppInfo.InternalId + "_Tray_SingleInstance", out createdNew))
            {
                Switcher.Log("RunTray: createdNew=" + createdNew + " settingsIfRunning=" + settingsIfRunning);
                if (!createdNew)
                {
                    if (settingsIfRunning) return RunSettings();
                    return 0;
                }

                using (var ctx = new TrayContext(config))
                {
                    Application.Run(ctx);
                }
                Switcher.Log("RunTray: 消息循环已结束");
            }
            return 0;
        }

        /// <summary>WPF 界面进程路径（第 2 轮起设置界面由它提供；不存在时回退到内置 WinForms 界面）</summary>
        internal static string UiExePath
        {
            get { return System.IO.Path.Combine(AppPaths.ExeDir, "PortalJump.UI.exe"); }
        }

        internal static bool HasUiProcess
        {
            get { try { return System.IO.File.Exists(UiExePath); } catch { return false; } }
        }

        /// <summary>
        /// 启动界面进程（用完即走，不常驻）。成功返回 true。
        /// onExit 在界面关闭后回调 —— 托盘借此重新读取配置（界面里可能改过 presets.ini）。
        /// </summary>
        internal static bool LaunchUi(string mode, Action onExit)
        {
            try
            {
                if (!HasUiProcess) return false;
                var psi = new ProcessStartInfo(UiExePath, mode);
                psi.UseShellExecute = false;
                psi.WorkingDirectory = AppPaths.ExeDir;
                Process proc = Process.Start(psi);
                if (proc == null) return false;
                if (onExit != null)
                {
                    proc.EnableRaisingEvents = true;
                    proc.Exited += delegate { try { onExit(); } catch { } };
                }
                Switcher.Log("已启动界面进程：" + mode);
                return true;
            }
            catch (Exception e)
            {
                Switcher.Log("启动界面进程失败（改用内置界面）：" + e.Message);
                return false;
            }
        }
        /// <summary>独立打开设置界面（不要求托盘在运行）</summary>
        static int RunSettings()
        {
            // 优先用 WPF 界面进程（真半透明效果，第 2 轮）；没有那个 exe 就回退到内置 WinForms 界面
            if (LaunchUi("settings", null)) return 0;

            // 单独加载一份配置，和托盘各自持有，避免托盘热重载时把界面正在编辑的对象改掉
            Config config = Config.Load(IniPath);
            if (config.EnsureFile()) Switcher.Log("首次运行：已生成默认配置 " + config.FilePath);

            using (var form = new SettingsForm(config))
            {
                Application.Run(form);
            }
            return 0;
        }
    }

    /// <summary>托盘常驻上下文：菜单 + 程序进程监控自动切换</summary>
    class TrayContext : ApplicationContext
    {
        Config _config;
        NotifyIcon _icon;
        System.Windows.Forms.Timer _monitorTimer;
        SettingsForm _settings;
        string _lastAutoKey;
        DateTime _configStamp = DateTime.MinValue;
        int _ticks;                 // 本次会话的定时器计数
        bool _startupReconciled;    // 启动后的"残留游戏分辨率"纠正是否已执行过
        string _stuckLoggedFor;     // 已记录过的残留状态，避免每 2 秒刷日志

        // ---- 切换后看门狗：防止游戏把分辨率改回去 ----
        Preset _watchdogPreset;     // 正在盯的预设（null = 不盯）
        int _watchdogPid;           // 关联进程（仅用于日志）
        int _watchdogTicksLeft;     // 还要盯几个 tick
        int _watchdogRepairs;       // 已纠正次数（防止和游戏无限拉锯）

        public TrayContext(Config config)
        {
            _config = config;
            _configStamp = ConfigStamp();

            _icon = new NotifyIcon();
            _icon.Icon = AppIcon.Get(32);
            _icon.Text = AppInfo.Title + " — " + AppInfo.Tagline;
            _icon.Visible = true;
            _icon.BalloonTipTitle = AppInfo.Name;
            _icon.DoubleClick += delegate { OpenSettings(); };
            RebuildMenu();

            _monitorTimer = new System.Windows.Forms.Timer();
            _monitorTimer.Interval = 2000;
            _monitorTimer.Tick += MonitorTick;
            _monitorTimer.Start();
        }

        static DateTime ConfigStamp()
        {
            try
            {
                string p = AppPaths.IniPath;
                return File.Exists(p) ? File.GetLastWriteTimeUtc(p) : DateTime.MinValue;
            }
            catch { return DateTime.MinValue; }
        }

        static string IniPath
        {
            get { return AppPaths.IniPath; }
        }

        // ---------------- 菜单 ----------------

        static ToolStripMenuItem MenuItem(string text)
        {
            var item = new ToolStripMenuItem(text);
            item.Font = UiKit.FontBody;
            item.Padding = UiKit.Pad(0, 4, 0, 4);
            return item;
        }

        void RebuildMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Font = UiKit.FontBody;
            menu.Renderer = UiKit.MenuRenderer();
            // 必须开左边这列：关掉的话 ToolStripMenuItem 的勾选标志会被裁掉，
            // 用户就看不到"自动跟随程序切换 / 开机自动启动"到底是开还是关（实测反馈）
            menu.ShowImageMargin = true;
            menu.Padding = UiKit.Pad(4);

            var open = MenuItem("打开设置界面…");
            open.Font = UiKit.FontBodyBold;
            open.Click += delegate { OpenSettings(); };
            menu.Items.Add(open);
            menu.Items.Add(new ToolStripSeparator());

            var auto = MenuItem("自动跟随程序切换");
            auto.Checked = _config.AutoSwitch;
            auto.CheckOnClick = true;
            auto.CheckedChanged += delegate
            {
                _config.AutoSwitch = auto.Checked;
                if (!_config.AutoSwitch) _lastAutoKey = null;
                SaveConfigQuiet();
                Balloon(auto.Checked ? "已开启自动切换" : "已关闭自动切换");
            };
            menu.Items.Add(auto);
            menu.Items.Add(new ToolStripSeparator());

            if (_config.Presets.Count == 0)
            {
                var none = MenuItem("(还没有预设，请打开设置界面新增)");
                none.Enabled = false;
                menu.Items.Add(none);
            }
            foreach (Preset p in _config.Presets)
            {
                var item = MenuItem("");
                string label = (p.Name == null || p.Name.Length == 0) ? p.Key : p.Name;
                item.Text = label + "   (" + p.ResolutionText + " · " + p.ScalingText + ")"
                    + (string.Equals(p.Key, _config.RestoreKey, StringComparison.OrdinalIgnoreCase) ? "  ★" : "");
                Preset preset = p;
                item.Click += delegate { ApplyPreset(preset, false); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());

            var autostart = MenuItem("开机自动启动");
            try { autostart.Checked = AutoStart.IsEnabled(); }
            catch { autostart.Checked = false; }
            autostart.Click += delegate
            {
                try
                {
                    AutoStart.Set(!autostart.Checked);
                    autostart.Checked = AutoStart.IsEnabled();
                    Balloon(autostart.Checked ? "已设置开机自启" : "已取消开机自启");
                }
                catch (Exception e)
                {
                    MessageBox.Show("设置开机自启失败：" + e.Message, AppInfo.Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            menu.Items.Add(autostart);

            var openIni = MenuItem("编辑配置文件 (presets.ini)");
            openIni.Click += delegate
            {
                try { Process.Start("notepad.exe", "\"" + IniPath + "\""); }
                catch (Exception e) { MessageBox.Show("打开失败：" + e.Message, AppInfo.Title); }
            };
            menu.Items.Add(openIni);

            var diag = MenuItem("环境自检 / 诊断");
            diag.Click += delegate { Program.RunSelfTest(false); };
            menu.Items.Add(diag);

            var openLog = MenuItem("查看运行日志");
            openLog.Click += delegate
            {
                try
                {
                    string log = AppPaths.LogPath;
                    if (File.Exists(log)) Process.Start("notepad.exe", "\"" + log + "\"");
                    else MessageBox.Show("还没有日志文件。", AppInfo.Title);
                }
                catch (Exception e) { MessageBox.Show("打开失败：" + e.Message, AppInfo.Title); }
            };
            menu.Items.Add(openLog);
            menu.Items.Add(new ToolStripSeparator());

            var about = MenuItem("关于 " + AppInfo.TitleWithVersion);
            about.Click += delegate { ShowAbout(); };
            menu.Items.Add(about);

            var exit = MenuItem("退出");
            exit.Click += delegate
            {
                _monitorTimer.Stop();
                _icon.Visible = false;
                Application.Exit();
            };
            menu.Items.Add(exit);

            ContextMenuStrip old = _icon.ContextMenuStrip;
            _icon.ContextMenuStrip = menu;
            if (old != null) old.Dispose();
        }

        void ShowAbout()
        {
            if (_settings != null && !_settings.IsDisposed)
            {
                Program.RunAboutOwned(_settings);
                return;
            }
            Program.RunAbout();
        }

        void SaveConfigQuiet()
        {
            try
            {
                _config.Save(_config.FilePath);
                _configStamp = ConfigStamp();
            }
            catch (Exception e)
            {
                Switcher.Log("保存配置失败: " + e.Message);
            }
        }

        void OpenSettings()
        {
            if (_settings != null && !_settings.IsDisposed)
            {
                if (_settings.WindowState == FormWindowState.Minimized) _settings.WindowState = FormWindowState.Normal;
                _settings.Activate();
                return;
            }
            // 优先启动 WPF 界面进程；关掉它之后重新读配置
            if (Program.LaunchUi("settings", ReloadConfig)) return;

            Config cfg = Config.Load(IniPath);
            cfg.EnsureFile();
            _settings = new SettingsForm(cfg);
            _settings.FormClosed += delegate { _settings = null; ReloadConfig(); };
            _settings.Show();
        }

        // ---------------- 配置热加载 ----------------

        void ReloadConfig()
        {
            try
            {
                Config fresh = Config.Load(IniPath);
                _config.AutoSwitch = fresh.AutoSwitch;
                _config.RestoreKey = fresh.RestoreKey;
                _config.Presets.Clear();
                foreach (Preset p in fresh.Presets) _config.Presets.Add(p);
                _configStamp = ConfigStamp();
                RebuildMenu();
                Switcher.Log("已重新载入配置：" + _config.Presets.Count + " 个预设");
            }
            catch (Exception e)
            {
                Switcher.Log("重载配置失败: " + e.Message);
            }
        }

        void ReloadConfigIfChanged()
        {
            DateTime now = ConfigStamp();
            if (now != _configStamp && now != DateTime.MinValue) ReloadConfig();
        }

        // ---------------- 自动切换 ----------------

        /// <summary>找出正在运行的程序对应的预设（按预设顺序，第一个匹配），并回传命中的进程名与 PID（便于排查）。</summary>
        Preset FindRunningGame(out string matchedName, out int matchedPid)
        {
            matchedName = null;
            matchedPid = 0;

            var map = new Dictionary<string, Preset>(StringComparer.OrdinalIgnoreCase);
            foreach (Preset p in _config.Presets)
            {
                if (!p.HasProcessBinding) continue;
                foreach (string n in p.ProcessNames)
                {
                    if (n.Length > 0 && !map.ContainsKey(n)) map[n] = p;
                }
            }
            if (map.Count == 0) return null;

            Process[] procs = null;
            try
            {
                procs = Process.GetProcesses();
                foreach (Process pr in procs)
                {
                    string name;
                    try { name = pr.ProcessName; }
                    catch { continue; }
                    Preset hit;
                    if (name != null && map.TryGetValue(name, out hit))
                    {
                        matchedName = name;
                        try { matchedPid = pr.Id; }
                        catch { }
                        return hit;
                    }
                }
                return null;
            }
            catch { return null; }
            finally
            {
                if (procs != null)
                {
                    foreach (Process pr in procs)
                    {
                        try { pr.Dispose(); }
                        catch { }
                    }
                }
            }
        }

        /// <summary>
        /// 切换成功后的一小段时间内盯紧分辨率。
        ///
        /// 为什么需要：三角洲这类游戏在启动流程里会自己再调一次显示模式，
        /// 时间点可能比我们晚几百毫秒到几秒。旧版只在检测到进程的那一刻切一次，
        /// 被游戏覆盖后就不再管了 —— 表现就是"有时候切不过去"。
        /// 这里在切换后 12 秒内每 2 秒核对一次，被改回去就再切一次（最多补 3 次）。
        /// </summary>
        void StartWatchdog(Preset target, int pid)
        {
            _watchdogPreset = target;
            _watchdogPid = pid;
            _watchdogTicksLeft = 6;      // 6 × 2 秒 = 12 秒
            _watchdogRepairs = 0;
        }

        void CheckDrift(Preset target)
        {
            if (_watchdogPreset == null || _watchdogTicksLeft <= 0) return;
            // 预设换了就不再盯旧的
            if (!string.Equals(_watchdogPreset.Key, target.Key, StringComparison.OrdinalIgnoreCase))
            {
                _watchdogPreset = null;
                return;
            }

            _watchdogTicksLeft--;

            if (DisplayInfo.IsCurrent(target.Width, target.Height)) return;

            if (_watchdogRepairs >= 3)
            {
                Switcher.Log("分辨率被程序反复改回，已放弃纠正（预设 [" + target.Key + "]，当前 "
                    + DisplayInfo.CurrentModeText() + "）");
                _watchdogPreset = null;
                return;
            }

            _watchdogRepairs++;
            Switcher.Log("检测到分辨率被程序改回（当前 " + DisplayInfo.CurrentModeText()
                + "，目标 " + target.ResolutionText + "），第 " + _watchdogRepairs + " 次纠正");
            ApplyPreset(target, true);
        }

        /// <summary>关联程序退出时清掉看门狗</summary>
        void ClearWatchdog()
        {
            _watchdogPreset = null;
            _watchdogTicksLeft = 0;
        }

        void MonitorTick(object sender, EventArgs e)
        {
            ReloadConfigIfChanged();
            _ticks++;

            if (!_config.AutoSwitch)
            {
                _lastAutoKey = null;
                return;
            }

            string matched;
            int matchedPid;
            Preset running = FindRunningGame(out matched, out matchedPid);

            if (running != null)
            {
                _stuckLoggedFor = null;
                if (!string.Equals(_lastAutoKey, running.Key, StringComparison.OrdinalIgnoreCase))
                {
                    _lastAutoKey = running.Key;
                    Switcher.Log("检测到关联程序 " + matched + " (PID " + matchedPid + ") → 自动切到预设 [" + running.Key + "]");
                    ApplyPreset(running, true);
                    StartWatchdog(running, matchedPid);
                }
                else
                {
                    // 已经在目标预设上：检查游戏有没有把我们切的分辨率又改回去。
                    // 这是"三角洲有时切不过去"的常见成因 ——
                    // 游戏启动后自己再设一次显示模式，把我们刚切的覆盖掉，
                    // 而旧版只在"检测到程序"那一刻切一次，之后就不管了。
                    CheckDrift(running);
                }
                return;
            }

            if (_lastAutoKey != null)
            {
                _lastAutoKey = null;
                ClearWatchdog();
                Preset restore = _config.RestoreTarget;
                if (restore != null) ApplyPreset(restore, true);
                return;
            }

            // 没有关联程序在运行，本次会话也没切换过。
            // 这里要处理"桌面卡在游戏预设分辨率上"的残留情况，成因有三种：
            //   1) 游戏自己改了显示模式，本工具没参与，所以没有切换记录；
            //   2) 上次退出时恢复失败；
            //   3) 关机时正处于游戏分辨率，重启后 Windows 原样保留了这个模式。
            // 旧版本因为没有切换记录就永远不管，只能手动点回来。
            //
            // 但这个行为会主动改分辨率，可能和"我就要桌面用这个分辨率"的用户打架，
            // 所以给了一个开关（ini 里 fixresidual=0 关掉）。
            if (!_config.FixResidual) return;

            Preset stuck = FindPresetMatchingDesktop();
            if (stuck == null) return;

            string sig = stuck.Key + "|" + DisplayInfo.CurrentModeText();
            if (sig != _stuckLoggedFor)
            {
                _stuckLoggedFor = sig;
                Switcher.Log("桌面当前停在游戏预设 [" + stuck.Key + "] 的分辨率（" + DisplayInfo.CurrentModeText()
                    + "），而它关联的程序都没在运行");
            }

            if (_startupReconciled || _ticks < 3) return;   // 启动几秒后再纠正，避开开机瞬间的竞争
            _startupReconciled = true;

            Preset target = _config.RestoreTarget;
            if (target == null) return;
            if (target.Width == stuck.Width && target.Height == stuck.Height) return;  // 目标分辨率一样就不用动
            Switcher.Log("启动检查：自动从游戏预设的分辨率恢复到 [" + target.Key + "]");
            ApplyPreset(target, true);
        }

        /// <summary>
        /// 当前桌面分辨率是否正好等于某个"关联了程序"的预设（用来判断分辨率是不是游戏残留）。
        /// 只比较分辨率、不比较缩放：驱动可能不采用请求的缩放值（日志里会看到"回读不一致"），
        /// 拿缩放做判据会误判。
        /// </summary>
        Preset FindPresetMatchingDesktop()
        {
            DisplayInfo.DEVMODE cur = DisplayInfo.GetCurrentMode();
            if (cur.dmPelsWidth <= 0) return null;
            foreach (Preset p in _config.Presets)
            {
                if (!p.HasProcessBinding) continue;
                if (p.Width == cur.dmPelsWidth && p.Height == cur.dmPelsHeight) return p;
            }
            return null;
        }

        void ApplyPreset(Preset p, bool fromMonitor)
        {
            string msg;
            bool ok = Switcher.Apply(p, out msg);
            Balloon((fromMonitor ? "[自动] " : "") + msg);
            if (!ok) Switcher.Log("[失败] " + msg);
        }

        void Balloon(string text)
        {
            try
            {
                _icon.BalloonTipText = text.Length > 250 ? text.Substring(0, 250) : text;
                _icon.ShowBalloonTip(3000);
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_monitorTimer != null)
                {
                    _monitorTimer.Stop();
                    _monitorTimer.Dispose();
                }
                if (_settings != null && !_settings.IsDisposed) _settings.Dispose();
                if (_icon != null)
                {
                    _icon.Visible = false;
                    _icon.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
