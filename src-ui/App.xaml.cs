using System;
using System.Windows;
using PortalJump;

namespace PortalJump.UI
{
    /// <summary>
    /// UI 进程入口：只负责按参数打开对应窗口，然后退出（用完即走，不常驻）。
    /// 参数：
    ///   about     关于页（第 1 轮已完成）
    ///   settings  设置界面（第 2 轮）
    /// 不带参数时按 settings 处理。
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);


            // NVAPI 必须先初始化：显卡信息、当前缩放、环境自检都依赖它
            // （托盘进程在 Program.RunMain 里做了同样的事，这里是独立进程，不能省）
            try { Switcher.TryInitNvapi(); }
            catch { }

            string mode = e.Args.Length > 0 ? e.Args[0].ToLowerInvariant() : "settings";
            Window w;
            if (mode == "about") w = new AboutWindow();
            else
            {
                // 直接跑界面进程（而不是从托盘拉起）时，同样保证配置文件存在
                Config cfg = Config.Load(AppPaths.IniPath);
                cfg.EnsureFile();
                w = new SettingsWindow(cfg);
            }

            MainWindow = w;
            w.Show();
        }
    }
}
