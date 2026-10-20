using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using PortalJump;   // AppInfo / Switcher / DisplayInfo

namespace PortalJump.UI
{
    /// <summary>
    /// AMD 显卡的"创建自定义分辨率"窗口（走官方 ADLX，不依赖 NVAPI）。
    ///
    /// 与 NVIDIA 版的差别：ADLX 的 CreateNewResolution 是把分辨率"注册"进驱动列表，
    /// 不立即切换当前画面（没有 NVAPI 的试用/倒计时机制）。创建成功后回到主界面，
    /// 在分辨率下拉里选中它并应用即可。刷新率与时序沿用当前应用模式（官方示例做法），
    /// 所以这里刷新率只读，只让用户填宽高。
    /// </summary>
    public partial class CustomResolutionWindowAmd : Window
    {
        public int ResultWidth = 0;
        public int ResultHeight = 0;

        public CustomResolutionWindowAmd(int presetWidth, int presetHeight)
        {
            InitializeComponent();

            int defW = presetWidth > 0 ? presetWidth : 1920;
            int defH = presetHeight > 0 ? presetHeight : 1080;
            int defHz = 0;
            try
            {
                DisplayInfo.DEVMODE cur = DisplayInfo.GetCurrentMode();
                if (cur.dmPelsWidth > 0) defW = cur.dmPelsWidth;
                if (cur.dmPelsHeight > 0) defH = cur.dmPelsHeight;
                if (cur.dmDisplayFrequency > 0) defHz = cur.dmDisplayFrequency;
            }
            catch { }

            TxtW.Text = defW.ToString(CultureInfo.InvariantCulture);
            TxtH.Text = defH.ToString(CultureInfo.InvariantCulture);
            TxtHz.Text = defHz > 0 ? defHz.ToString(CultureInfo.InvariantCulture) + " Hz（沿用当前）" : "沿用当前";

            try { BaseWidthHint.Text = "基准横向 " + BaseWidth() + "px"; } catch { }
        }

        // ==================================================================
        // 宽视距（21:9 / 32:9）
        // ==================================================================

        int BaseWidth()
        {
            try
            {
                DisplayInfo.DEVMODE cur = DisplayInfo.GetCurrentMode();
                if (cur.dmPelsWidth > 0) return cur.dmPelsWidth;
            }
            catch { }
            return 3840;
        }

        void OnUltrawide21(object sender, RoutedEventArgs e) { ApplyAspect(21, 9); }
        void OnUltrawide32(object sender, RoutedEventArgs e) { ApplyAspect(32, 9); }

        void ApplyAspect(int num, int den)
        {
            int baseW = BaseWidth();
            long h = (long)Math.Round((double)baseW * den / num);
            if (h % 2 != 0) h++;
            if (h < 320) h = 320;
            if (h > 9999) h = 9999;

            TxtW.Text = baseW.ToString(CultureInfo.InvariantCulture);
            TxtH.Text = h.ToString(CultureInfo.InvariantCulture);

            AspectHint.Text = "已按 " + num + ":" + den + " 填入 " + baseW + "×" + h
                + "（横向 " + baseW + "，纵向 " + baseW + " × 9 ÷ " + num + " = " + h
                + "）；确认后点「创建自定义分辨率」写入驱动。";
        }

        // ==================================================================
        // 数字输入与步进
        // ==================================================================

        static int ParseInt(string s)
        {
            int v;
            return int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        static void SetValue(System.Windows.Controls.TextBox box, int v)
        {
            box.Text = v.ToString(CultureInfo.InvariantCulture);
        }

        static int Step(int value, int delta, int min, int max)
        {
            int v = value + delta;
            if (v < min) v = min;
            if (v > max) v = max;
            return v;
        }

        void OnWidthUp(object sender, RoutedEventArgs e) { SetValue(TxtW, Step(Math.Max(320, ParseInt(TxtW.Text)), 1, 320, 9999)); }
        void OnWidthDown(object sender, RoutedEventArgs e) { SetValue(TxtW, Step(Math.Max(320, ParseInt(TxtW.Text)), -1, 320, 9999)); }
        void OnHeightUp(object sender, RoutedEventArgs e) { SetValue(TxtH, Step(Math.Max(320, ParseInt(TxtH.Text)), 1, 320, 9999)); }
        void OnHeightDown(object sender, RoutedEventArgs e) { SetValue(TxtH, Step(Math.Max(320, ParseInt(TxtH.Text)), -1, 320, 9999)); }

        void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ==================================================================
        // 创建（写入驱动，不切换当前画面）
        // ==================================================================

        void OnCreate(object sender, RoutedEventArgs e)
        {
            int w = ParseInt(TxtW.Text);
            int h = ParseInt(TxtH.Text);
            if (w < 320 || w > 9999 || h < 320 || h > 9999)
            {
                MessageBox.Show(this, "宽度/高度需要在 320 ~ 9999 之间（ADLX 限制）。", AppInfo.Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Cursor previous = Cursor;
            Cursor = Cursors.Wait;
            try
            {
                int hz;
                string detail;
                bool ok = Switcher.AmdCreateCustomResolution(w, h, out hz, out detail);
                if (!ok)
                {
                    MessageBox.Show(this, "创建自定义分辨率失败：\n" + detail,
                        AppInfo.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ResultWidth = w;
                ResultHeight = h;
                MessageBox.Show(this,
                    "已创建自定义分辨率 " + w + "x" + h + "@" + hz + "Hz。\n\n"
                    + "它已写入 AMD 驱动，当前画面不会改变。回到主界面后，在分辨率下拉里选中它并"
                    + "「应用」即可切换；若显示器不接受，AMD 驱动会自动回退到原模式。",
                    AppInfo.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "操作失败：" + ex.Message, AppInfo.Title,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { Cursor = previous; }
        }
    }
}
