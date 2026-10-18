using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.Display;
using NvAPIWrapper.Native.Display.Structures;
using NvAPIWrapper.Native.Interfaces.Display;

namespace PortalJump
{
    /// <summary>
    /// 用 NVIDIA 驱动接口为显示器新建一个自定义分辨率（相当于在 NVIDIA 控制面板里“创建自定义分辨率”）。
    ///
    /// 安全设计：先做“试用模式”，屏幕会临时切过去，
    /// 用户必须在倒计时内点“保留”，否则自动还原，避免黑屏无法操作。
    /// </summary>
    public class CustomResolutionForm : Form
    {
        NumericUpDown _numW, _numH, _numHz;
        ComboBox _cmbTiming;

        public int ResultWidth;
        public int ResultHeight;

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

        public CustomResolutionForm(int presetWidth, int presetHeight)
        {
            Text = "创建自定义分辨率";
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            Font = UiKit.FontBody;
            BackColor = UiKit.Canvas;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = UiKit.Sz(600, 442);

            // ---------- 顶栏（品牌渐变） ----------
            var header = new HeroPanel();
            header.Dock = DockStyle.Top;
            header.Height = UiKit.S(88);

            var title = new Label();
            title.Text = "创建自定义分辨率";
            title.Font = UiKit.FontOf(13f, FontStyle.Bold);
            title.ForeColor = Color.White;
            title.AutoSize = true;
            title.BackColor = Color.Transparent;
            title.Location = UiKit.Pt(26, 24);

            var subtitle = new Label();
            subtitle.Text = "交给 NVIDIA 驱动新建一个分辨率，先试用、确认画面正常再保留。";
            subtitle.Font = UiKit.FontHint;
            subtitle.ForeColor = Color.FromArgb(226, 255, 255, 255);
            subtitle.AutoSize = true;
            subtitle.BackColor = Color.Transparent;
            subtitle.Location = UiKit.Pt(26, 51);

            header.Controls.Add(title);
            header.Controls.Add(subtitle);

            // ---------- 表单 ----------
            var body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.ColumnCount = 2;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiKit.S(112)));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.Padding = UiKit.Pad(24, 12, 24, 8);
            body.BackColor = UiKit.Canvas;

            // 默认值 = 当前系统正在用的分辨率与刷新率（读不到才退回预设尺寸）
            int defW = presetWidth > 0 ? presetWidth : 1920;
            int defH = presetHeight > 0 ? presetHeight : 1080;
            int defHz = 60;
            try
            {
                DisplayInfo.DEVMODE curMode = DisplayInfo.GetCurrentMode();
                if (curMode.dmPelsWidth > 0) defW = curMode.dmPelsWidth;
                if (curMode.dmPelsHeight > 0) defH = curMode.dmPelsHeight;
                if (curMode.dmDisplayFrequency > 0) defHz = curMode.dmDisplayFrequency;
            }
            catch { }

            _numW = MakeNumeric(defW, 160, 320, 16384);
            _numH = MakeNumeric(defH, 160, 320, 16384);
            _numHz = MakeNumeric(defHz, 120, 24, 480);

            var hzHint = new Label();
            hzHint.Text = "推荐填显示器支持的最高刷新率";
            hzHint.Font = UiKit.FontHint;
            hzHint.ForeColor = UiKit.TextHint;
            hzHint.AutoSize = false;
            hzHint.TextAlign = ContentAlignment.MiddleLeft;
            hzHint.Size = UiKit.Sz(300, 30);
            hzHint.Anchor = AnchorStyles.Left;
            hzHint.Margin = new Padding(UiKit.S(10), 0, 0, 0);

            _cmbTiming = new ComboBox();
            UiKit.StyleCombo(_cmbTiming);
            _cmbTiming.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbTiming.Width = UiKit.S(320);
            foreach (string t in TimingTexts) _cmbTiming.Items.Add(t);
            // 默认「自动」：交给驱动按 EDID 挑时序，比硬编码某个标准更稳。
            _cmbTiming.SelectedIndex = 0;

            AddFormRow(body, 0, "宽度", _numW);
            AddFormRow(body, 1, "高度", _numH);

            // 数字框 + 提示文字用表格排：两者在单元格里垂直居中，提示文字不会浮在数字框上方
            var hzStrip = new TableLayoutPanel();
            hzStrip.ColumnCount = 2;
            hzStrip.RowCount = 1;
            hzStrip.AutoSize = true;
            hzStrip.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            hzStrip.Anchor = AnchorStyles.Left;
            hzStrip.BackColor = UiKit.Canvas;
            hzStrip.Margin = new Padding(0);
            hzStrip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            hzStrip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _numHz.Anchor = AnchorStyles.Left;
            _numHz.Margin = new Padding(0);
            hzStrip.Controls.Add(_numHz, 0, 0);
            hzStrip.Controls.Add(hzHint, 1, 0);
            AddFormRow(body, 2, "刷新率 (Hz)", hzStrip);

            AddFormRow(body, 3, "时序标准", _cmbTiming);

            var warn = new Label();
            warn.AutoSize = false;
            warn.Dock = DockStyle.Fill;
            warn.Font = UiKit.FontHint;
            warn.ForeColor = UiKit.TextHint;
            warn.Padding = UiKit.Pad(0, 10, 0, 0);
            warn.Text = "说明：不是所有显示器/接口都接受自定义时序（笔记本内屏、部分 DP 转接、电视常会失败）。\n"
                + "失败不会有副作用；成功后新分辨率会出现在设置界面的“分辨率”下拉里。";
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            body.Controls.Add(warn, 0, 4);
            body.SetColumnSpan(warn, 2);

            // ---------- 底栏 ----------
            var footer = new BarPanel(true);
            footer.Dock = DockStyle.Bottom;
            footer.Height = UiKit.S(76);

            var btnCancel = new FlatButton("取消", FlatButton.Kind.Secondary);
            btnCancel.Size = UiKit.Sz(96, 38);
            btnCancel.Margin = new Padding(UiKit.S(10), 0, 0, 0);
            btnCancel.DialogResult = DialogResult.Cancel;

            var btnOk = new FlatButton("试用并创建", FlatButton.Kind.Primary);
            btnOk.Size = UiKit.Sz(150, 38);
            btnOk.Margin = new Padding(0);
            btnOk.Click += delegate { DoCreate(); };

            var footerButtons = new FlowLayoutPanel();
            footerButtons.FlowDirection = FlowDirection.RightToLeft;
            footerButtons.WrapContents = false;
            footerButtons.AutoSize = true;
            footerButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            footerButtons.Dock = DockStyle.Right;
            footerButtons.Padding = UiKit.Pad(0, 19, 24, 0);
            footerButtons.BackColor = UiKit.Card;
            footerButtons.Controls.Add(btnOk);
            footerButtons.Controls.Add(btnCancel);

            footer.Controls.Add(footerButtons);

            Controls.Add(body);
            Controls.Add(header);
            Controls.Add(footer);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        static void AddFormRow(TableLayoutPanel host, int row, string label, Control content)
        {
            host.RowStyles.Add(new RowStyle(SizeType.Absolute, UiKit.S(54)));
            host.Controls.Add(UiKit.FieldLabel(label), 0, row);
            content.Anchor = AnchorStyles.Left;
            host.Controls.Add(content, 1, row);
            host.RowCount = row + 1;
        }

        static NumericUpDown MakeNumeric(int value, int width, int min, int max)
        {
            var n = new NumericUpDown();
            UiKit.StyleNumeric(n);
            n.Minimum = min;
            n.Maximum = max;
            // 必须先设好范围再赋值，否则会被旧的上下限夹住
            n.Value = value < min ? min : (value > max ? max : value);
            n.Width = UiKit.S(width);
            return n;
        }

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

        void DoCreate()
        {
            int w = (int)_numW.Value;
            int h = (int)_numH.Value;
            int hz = (int)_numHz.Value;
            int timingIdx = _cmbTiming.SelectedIndex;
            if (timingIdx < 0) timingIdx = 1;
            var timingType = (TimingOverride)TimingValues[timingIdx];

            uint displayId;
            try
            {
                displayId = FindPrimaryDisplayId();
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "读取显示器信息失败：" + e.Message, AppInfo.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Cursor previous = Cursor;
            Cursor = Cursors.WaitCursor;

            Timing timing;
            try
            {
                timing = DisplayApi.GetTiming(displayId,
                    new TimingInput((uint)w, (uint)h, hz, timingType, false, 0));
            }
            catch (Exception e)
            {
                Cursor = previous;
                MessageBox.Show(this, "驱动无法为 " + w + "x" + h + "@" + hz + "Hz 计算时序：\n" + e.Message
                    + "\n\n可以换一个时序标准（例如 CVT 减少消隐 / DMT）再试。", AppInfo.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var custom = new CustomDisplay((uint)w, (uint)h, 32, ColorFormat.Unknown, 1f, 1f, timing, false);
                var dict = new Dictionary<uint, CustomDisplay>();
                dict[displayId] = custom;
                DisplayApi.TryCustomDisplay(dict);
            }
            catch (Exception e)
            {
                Cursor = previous;
                MessageBox.Show(this, "试用自定义分辨率失败（显示器可能不支持该时序）：\n" + e.Message,
                    AppInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            finally
            {
                Cursor = previous;
            }

            // 画面已临时切换：必须在倒计时内确认，否则还原
            bool keep;
            using (var confirm = new TrialConfirmForm(w, h, hz))
            {
                keep = (confirm.ShowDialog(this) == DialogResult.OK);
            }

            try
            {
                if (keep)
                {
                    DisplayApi.SaveCustomDisplay(new uint[] { displayId }, true, true);
                    Switcher.Log("自定义分辨率已保存: " + w + "x" + h + "@" + hz + " timing=" + timingType);
                    ResultWidth = w;
                    ResultHeight = h;
                    Thread.Sleep(700);   // 给驱动一点时间把新模式暴露给系统
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    DisplayApi.RevertCustomDisplayTrial(new uint[] { displayId });
                    Switcher.Log("自定义分辨率试用被放弃，已还原: " + w + "x" + h);
                    MessageBox.Show(this, "已还原到原来的显示模式，没有做任何修改。", AppInfo.Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "操作失败：" + e.Message, AppInfo.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    /// <summary>试用自定义分辨率时的确认窗口：倒计时结束自动还原，保证不会黑屏卡死。</summary>
    public class TrialConfirmForm : Form
    {
        readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        int _left = 15;

        public TrialConfirmForm(int w, int h, int hz)
        {
            Text = "请确认画面是否正常";
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Font = UiKit.FontBody;
            BackColor = UiKit.Canvas;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            ShowInTaskbar = true;
            ClientSize = UiKit.Sz(560, 260);

            var card = new CardPanel("画面是否正常？");
            card.Dock = DockStyle.Fill;
            card.Margin = UiKit.Pad(16);

            var host = new Panel();
            host.Dock = DockStyle.Fill;
            host.Padding = UiKit.Pad(24);
            host.BackColor = UiKit.Canvas;

            var headline = new Label();
            headline.Text = "已临时切换到 " + w + "x" + h + " @" + hz + "Hz";
            headline.Font = UiKit.FontOf(11f, FontStyle.Bold);
            headline.ForeColor = UiKit.TextPrimary;
            headline.AutoSize = true;
            headline.Location = UiKit.Pt(28, 30);

            var explain = new Label();
            explain.Text = "画面显示正常 → 点「保留这个分辨率」；\n"
                + "画面异常/黑屏 → 不要操作，到时间会自动还原。";
            explain.Font = UiKit.FontBody;
            explain.ForeColor = UiKit.TextSecondary;
            explain.AutoSize = true;
            explain.Location = UiKit.Pt(28, 68);

            var countdown = new Label();
            countdown.Text = _left + " 秒后自动还原…";
            countdown.Font = UiKit.FontOf(11f, FontStyle.Bold);
            countdown.ForeColor = UiKit.Danger;
            countdown.AutoSize = true;
            countdown.Location = UiKit.Pt(28, 128);

            var btnKeep = new FlatButton("保留这个分辨率", FlatButton.Kind.Primary);
            btnKeep.Size = UiKit.Sz(170, 40);
            btnKeep.Margin = new Padding(0);
            btnKeep.DialogResult = DialogResult.OK;

            var btnRevert = new FlatButton("立即还原", FlatButton.Kind.Secondary);
            btnRevert.Size = UiKit.Sz(120, 40);
            btnRevert.Margin = new Padding(UiKit.S(10), 0, 0, 0);
            btnRevert.DialogResult = DialogResult.Cancel;

            var confirmButtons = new FlowLayoutPanel();
            confirmButtons.FlowDirection = FlowDirection.RightToLeft;
            confirmButtons.WrapContents = false;
            confirmButtons.AutoSize = true;
            confirmButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            confirmButtons.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            confirmButtons.BackColor = UiKit.Canvas;
            confirmButtons.Controls.Add(btnKeep);
            confirmButtons.Controls.Add(btnRevert);
            btnKeep.Surface = UiKit.Canvas;
            btnRevert.Surface = UiKit.Canvas;

            host.Controls.Add(headline);
            host.Controls.Add(explain);
            host.Controls.Add(countdown);
            host.Controls.Add(confirmButtons);
            card.Controls.Add(host);
            Controls.Add(card);

            // 按钮条贴到右下角（等布局完成后再定位，避免在父容器尺寸未定时算错坐标）
            host.Layout += delegate
            {
                confirmButtons.Location = new Point(
                    host.ClientSize.Width - UiKit.S(24) - confirmButtons.Width,
                    host.ClientSize.Height - UiKit.S(24) - confirmButtons.Height);
            };

            AcceptButton = btnKeep;
            CancelButton = btnRevert;

            _timer.Interval = 1000;
            _timer.Tick += delegate
            {
                _left--;
                if (_left <= 0)
                {
                    _timer.Stop();
                    DialogResult = DialogResult.Cancel;
                    Close();
                    return;
                }
                countdown.Text = _left + " 秒后自动还原…";
            };
            _timer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            base.OnFormClosed(e);
        }
    }
}
