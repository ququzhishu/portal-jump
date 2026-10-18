using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.Display;
using NvAPIWrapper.Native.Display.Structures;
using NvAPIWrapper.Native.Interfaces.Display;
using PortalJump;   // AppInfo / Switcher

namespace PortalJump.UI
{
    /// <summary>
    /// 用 NVIDIA 驱动接口为显示器新建一个自定义分辨率（相当于在 NVIDIA 控制面板里"创建自定义分辨率"）。
    /// 第 3 轮从 WinForms 版 CustomResolutionForm 迁到 WPF，逻辑逐条对齐：
    ///   先"试用"（屏幕临时切过去）→ 倒计时内点保留才真正保存，否则自动还原，避免黑屏无法操作。
    /// </summary>
    public partial class CustomResolutionWindow : Window
    {
        public int ResultWidth = 0;
        public int ResultHeight = 0;

        static readonly int[] TimingValues = new int[]
        {
            (int)TimingOverride.Auto,
            (int)TimingOverride.CVTReducedBlanking,
            (int)TimingOverride.CVT,
            (int)TimingOverride.DMT,
            (int)TimingOverride.DMTReducedBlanking,
            (int)TimingOverride.GTF,
            (int)TimingOverride.EDID
        };

        static readonly string[] TimingTexts = new string[]
        {
            "自动（交给驱动决定）",
            "CVT 减少消隐（液晶推荐）",
            "CVT",
            "DMT（VESA 标准）",
            "DMT 减少消隐",
            "GTF",
            "EDID"
        };

        public CustomResolutionWindow(int presetWidth, int presetHeight)
        {
            InitializeComponent();

            // 默认值 = 当前系统正在用的分辨率与刷新率（读不到才退回调用方给的预设尺寸）
            int defW = presetWidth > 0 ? presetWidth : 1920;
            int defH = presetHeight > 0 ? presetHeight : 1080;
            int defHz = 60;
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
            TxtHz.Text = defHz.ToString(CultureInfo.InvariantCulture);
            UpdateHzHint();

            // 宽视距的基准宽度直接写在界面上，用户一眼能看到"按什么算的"
            try { BaseWidthHint.Text = "基准横向 " + BaseWidth() + "px"; } catch { }

            foreach (string t in TimingTexts) CmbTiming.Items.Add(t);
            // 默认用「自动」：交给驱动按显示器 EDID 和当前模式挑最合适的时序。
            // 以前硬编码成「CVT 减少消隐」，那是替用户做了个不必要、且可能不兼容的决定 ——
            // 多数情况驱动自己选得比我们准，只有驱动算不出来或结果不对时才需要手动指定。
            CmbTiming.SelectedIndex = 0;
        }

        // ==================================================================
        // 数字输入的读写
        // ==================================================================

        /// <summary>当前显示器枚举到的模式（只查一次，之后复用）</summary>
        System.Collections.Generic.List<DisplayMode> _modes;

        /// <summary>
        /// 刷新率提示：说明"应该填显示器支持的最高刷新率"，并把该分辨率下实际查到的最高值写出来。
        /// 用户在宽高里改了尺寸，提示会跟着更新。
        /// </summary>
        void UpdateHzHint()
        {
            try
            {
                if (_modes == null) _modes = DisplayInfo.GetResolutions();
                int w = ParseInt(TxtW.Text), h = ParseInt(TxtH.Text);
                int max = 0;
                foreach (DisplayMode m in _modes)
                {
                    if (m.Width == w && m.Height == h && m.MaxHz > max) max = m.MaxHz;
                }
                HzHint.Text = max > 0
                    ? "推荐填显示器支持的最高刷新率（" + w + "x" + h + " 下最高 " + max + "Hz）"
                    : "推荐填显示器支持的最高刷新率（该分辨率未在显示器模式里列出）";
            }
            catch { HzHint.Text = "推荐填显示器支持的最高刷新率"; }
        }

        void OnSizeChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateHzHint();
        }

        // ==================================================================
        // 宽视距（21:9 / 32:9）
        // ==================================================================

        /// <summary>
        /// 基准宽度：取当前显示器正在用的横向像素数（"适合当前显示器"）。
        /// 读不到当前模式就退回模式表里的最大宽度，再不行用 3840。
        /// </summary>
        int BaseWidth()
        {
            try
            {
                DisplayInfo.DEVMODE cur = DisplayInfo.GetCurrentMode();
                if (cur.dmPelsWidth > 0) return cur.dmPelsWidth;
            }
            catch { }
            try
            {
                if (_modes == null) _modes = DisplayInfo.GetResolutions();
                int max = 0;
                foreach (DisplayMode m in _modes) { if (m.Width > max) max = m.Width; }
                if (max > 0) return max;
            }
            catch { }
            return 3840;
        }

        void OnUltrawide21(object sender, RoutedEventArgs e) { ApplyAspect(21, 9); }
        void OnUltrawide32(object sender, RoutedEventArgs e) { ApplyAspect(32, 9); }

        /// <summary>
        /// 按 N:9 换算：横向沿用当前显示器的横向长度，纵向 = 横向 × 9 ÷ N（四舍五入到偶数）。
        /// 这里只填数值、不直接写驱动 —— 用户确认后再点「创建并试用」，操作可预期。
        /// </summary>
        void ApplyAspect(int num, int den)
        {
            int baseW = BaseWidth();
            long h = (long)Math.Round((double)baseW * den / num);
            if (h % 2 != 0) h++;                      // 视频模式一般要求偶数
            if (h < 320) h = 320;
            if (h > 16384) h = 16384;

            TxtW.Text = baseW.ToString(CultureInfo.InvariantCulture);
            TxtH.Text = h.ToString(CultureInfo.InvariantCulture);
            UpdateHzHint();

            AspectHint.Text = "已按 " + num + ":" + den + " 填入 " + baseW + "×" + h
                + "（横向 " + baseW + "，纵向 " + baseW + " × 9 ÷ " + num + " = " + h
                + "）；确认后点「创建并试用」写入驱动。";
        }

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

        void OnWidthUp(object sender, RoutedEventArgs e) { SetValue(TxtW, Step(Math.Max(320, ParseInt(TxtW.Text)), 1, 320, 16384)); }
        void OnWidthDown(object sender, RoutedEventArgs e) { SetValue(TxtW, Step(Math.Max(320, ParseInt(TxtW.Text)), -1, 320, 16384)); }
        void OnHeightUp(object sender, RoutedEventArgs e) { SetValue(TxtH, Step(Math.Max(320, ParseInt(TxtH.Text)), 1, 320, 16384)); }
        void OnHeightDown(object sender, RoutedEventArgs e) { SetValue(TxtH, Step(Math.Max(320, ParseInt(TxtH.Text)), -1, 320, 16384)); }
        void OnHzUp(object sender, RoutedEventArgs e) { SetValue(TxtHz, Step(Math.Max(24, ParseInt(TxtHz.Text)), 1, 24, 480)); }
        void OnHzDown(object sender, RoutedEventArgs e) { SetValue(TxtHz, Step(Math.Max(24, ParseInt(TxtHz.Text)), -1, 24, 480)); }

        void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ==================================================================
        // 创建（先试用，再决定保留 / 还原）
        // ==================================================================

        static uint FindPrimaryDisplayId()
        {
            IPathInfo[] paths = DisplayApi.GetDisplayConfig();
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i].SourceModeInfo.IsGDIPrimary)
                {
                    foreach (IPathTargetInfo t in paths[i].TargetsInfo) return t.DisplayId;
                }
            }
            for (int i = 0; i < paths.Length; i++)
            {
                foreach (IPathTargetInfo t in paths[i].TargetsInfo) return t.DisplayId;
            }
            throw new InvalidOperationException("找不到任何已连接的显示器");
        }

        void OnCreate(object sender, RoutedEventArgs e)
        {
            int w = ParseInt(TxtW.Text);
            int h = ParseInt(TxtH.Text);
            int hz = ParseInt(TxtHz.Text);
            if (w < 320 || w > 16384 || h < 320 || h > 16384)
            {
                MessageBox.Show(this, "宽度/高度需要在 320 ~ 16384 之间。", AppInfo.Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (hz < 24 || hz > 480)
            {
                MessageBox.Show(this, "刷新率需要在 24 ~ 480 之间。", AppInfo.Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int timingIdx = CmbTiming.SelectedIndex;
            if (timingIdx < 0) timingIdx = 1;
            var timingType = (TimingOverride)TimingValues[timingIdx];

            uint displayId;
            try { displayId = FindPrimaryDisplayId(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "读取显示器信息失败：" + ex.Message, AppInfo.Title,
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Cursor previous = Cursor;
            Cursor = Cursors.Wait;

            Timing timing;
            try
            {
                timing = DisplayApi.GetTiming(displayId,
                    new TimingInput((uint)w, (uint)h, hz, timingType, false, 0));
            }
            catch (Exception ex)
            {
                Cursor = previous;
                MessageBox.Show(this, "驱动无法为 " + w + "x" + h + "@" + hz + "Hz 计算时序：\n" + ex.Message
                    + "\n\n可以换一个时序标准（例如 CVT 减少消隐 / DMT）再试。", AppInfo.Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var custom = new CustomDisplay((uint)w, (uint)h, 32, ColorFormat.Unknown, 1f, 1f, timing, false);
                var dict = new Dictionary<uint, CustomDisplay>();
                dict[displayId] = custom;
                DisplayApi.TryCustomDisplay(dict);
            }
            catch (Exception ex)
            {
                Cursor = previous;
                MessageBox.Show(this, "试用自定义分辨率失败（显示器可能不支持该时序）：\n" + ex.Message,
                    AppInfo.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            finally { Cursor = previous; }

            // 画面已临时切换：必须在倒计时内确认，否则还原
            bool keep;
            var confirm = new TrialConfirmWindow(w, h, hz);
            confirm.Owner = this;
            keep = (confirm.ShowDialog() == true);

            try
            {
                if (keep)
                {
                    DisplayApi.SaveCustomDisplay(new uint[] { displayId }, true, true);
                    Switcher.Log("自定义分辨率已保存: " + w + "x" + h + "@" + hz + " timing=" + timingType);
                    ResultWidth = w;
                    ResultHeight = h;
                    Thread.Sleep(700);   // 给驱动一点时间把新模式暴露给系统
                    DialogResult = true;
                    Close();
                }
                else
                {
                    DisplayApi.RevertCustomDisplayTrial(new uint[] { displayId });
                    Switcher.Log("自定义分辨率试用被放弃，已还原: " + w + "x" + h);
                    MessageBox.Show(this, "已还原到原来的显示模式，没有做任何修改。", AppInfo.Title,
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "操作失败：" + ex.Message, AppInfo.Title,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
