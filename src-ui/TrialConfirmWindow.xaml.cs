using System;
using System.Windows;
using System.Windows.Threading;
using PortalJump;   // Switcher.Log

namespace PortalJump.UI
{
    /// <summary>
    /// 试用自定义分辨率时的确认窗口：倒计时结束自动还原，保证不会黑屏卡死。
    /// 对应原来的 WinForms 版 TrialConfirmForm（15 秒倒计时）。
    /// </summary>
    public partial class TrialConfirmWindow : Window
    {
        readonly DispatcherTimer _timer = new DispatcherTimer();
        int _left = 15;

        public TrialConfirmWindow(int w, int h, int hz)
        {
            InitializeComponent();
            Headline.Text = "已临时切换到 " + w + "x" + h + " @" + hz + "Hz";
            Countdown.Text = _left + " 秒后自动还原…";

            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += OnTick;
            _timer.Start();
        }

        void OnTick(object sender, EventArgs e)
        {
            _left--;
            if (_left <= 0)
            {
                _timer.Stop();
                Switcher.Log("试用确认：倒计时结束，按未保留处理（自动还原）");
                DialogResult = false;
                Close();
                return;
            }
            Countdown.Text = _left + " 秒后自动还原…";
        }

        void OnKeep(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            DialogResult = true;
            Close();
        }

        void OnRevert(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            base.OnClosed(e);
        }
    }
}
