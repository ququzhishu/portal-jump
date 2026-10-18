using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using PortalJump;

namespace PortalJump.UI
{
    /// <summary>
    /// 关于页（WPF 版）：头图 + 应用信息 + 作者社交主页。
    /// 文案全部来自 AppInfo，运行环境来自 Switcher / DisplayInfo（与托盘进程共用同一批核心源码）。
    /// </summary>
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            // 信息卡的显卡行多了一行"缩放通路"，默认高度放不下，加高一点（内容是上对齐的，不会露底）
            Height = 620;
            LoadTexts();
            LoadArt();
        }

        // ---------------- 文案 ----------------

        void LoadTexts()
        {
            VersionText.Text = "v" + AppInfo.Version;
            HeroDesc.Text = AppInfo.Description;

            VerValue.Text = "v" + AppInfo.Version + "   （" + AppInfo.EnglishName + "）";
            DescValue.Text = AppInfo.Description;

            try
            {
                // 显卡行下面追加一行"缩放通路"：v0.11 起缩放不再绑定 NVIDIA，
                // 用户需要能看到自己实际走的是 NVAPI 还是 Windows 原生接口（DEVMODE.dmDisplayFixedOutput）
                GpuValue.Text = Switcher.EnvironmentText + "\n缩放通路：" + Switcher.ScalingBackendText;
                GpuValue.ToolTip = Switcher.ScalingBackendText;
            }
            catch { GpuValue.Text = "(读取失败)"; }

            try
            {
                ModeValue.Text = DisplayInfo.CurrentModeText() + "   ·   缩放 " + Switcher.GetCurrentScalingText();
            }
            catch (Exception e) { ModeValue.Text = "读取失败：" + e.Message; }

            IniValue.Text = AppPaths.IniPath;
            LogValue.Text = AppPaths.LogPath;
            IniValue.ToolTip = AppPaths.IniPath;
            LogValue.ToolTip = AppPaths.LogPath;

            AuthorLink.Content = AppInfo.Author + " ↗";
            AuthorUrl.Text = AppInfo.SocialDisplay;

            FooterText.Text = "© 2026 " + AppInfo.Author + "   ·   " + AppInfo.TitleWithVersion;
        }

        // ---------------- 素材 ----------------

        void LoadArt()
        {
            // 圆头像（有素材才显示，没有就是渐变圆底）
            ImageBrush avatar = Art.AvatarBrush();
            if (avatar != null) AvatarBrush.ImageSource = avatar.ImageSource;
            else AvatarFill.Visibility = Visibility.Collapsed;

            // 头图插画：素材多为"人物在右、左侧留白"的构图，这里用 Viewbox 只取右侧人物，
            // 再由 XAML 里的 OpacityMask 往左渐隐融入品牌渐变；没有素材就整块不显示。
            ImageSource bg = Art.Background;
            if (bg != null)
            {
                HeroBrush.ImageSource = bg;
                HeroBrush.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
                HeroBrush.Viewbox = new Rect(0.46, 0, 0.54, 1);
            }
            else
            {
                HeroArt.Visibility = Visibility.Collapsed;
            }
        }

        // ---------------- 交互 ----------------

        void OpenIni_Click(object sender, RoutedEventArgs e)
        {
            OpenPath(AppPaths.IniPath);
        }

        void OpenLog_Click(object sender, RoutedEventArgs e)
        {
            OpenPath(AppPaths.LogPath);
        }

        void OpenPath(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    Process.Start("notepad.exe", "\"" + path + "\"");
                }
                else
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Process.Start("explorer.exe", "\"" + dir + "\"");
                    else MessageBox.Show(this, "找不到文件：" + path, AppInfo.Title);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开失败：" + ex.Message, AppInfo.Title);
            }
        }

        void AuthorLink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = AppInfo.SocialUrls.Length > 0 ? AppInfo.SocialUrls[0] : "";
                if (url.Length > 0) Process.Start(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开主页失败：" + ex.Message, AppInfo.Title);
            }
        }

        void SelfTest_Click(object sender, RoutedEventArgs e)
        {
            string report;
            try { report = Switcher.SelfTest(); }
            catch (Exception ex) { report = "自检异常：" + ex.Message; }

            try
            {
                File.WriteAllText(AppPaths.SelfTestPath, report, new System.Text.UTF8Encoding(false));
            }
            catch { }

            MessageBox.Show(this, report + Environment.NewLine + "（同时已保存到 " + AppPaths.SelfTestPath + "）",
                AppInfo.Title + " 环境自检", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
