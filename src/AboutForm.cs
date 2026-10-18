using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PortalJump
{
    /// <summary>
    /// 关于页：品牌渐变头图 + 程序说明 + 运行环境 + 作者个人标签（社交媒体名：区区之数）。
    ///
    /// 文案全部取自 AppInfo，改名字 / 改作者 / 换主页链接只需要动 AppInfo.cs 一个文件。
    /// </summary>
    public class AboutForm : Form
    {
        readonly ToolTip _tips = new ToolTip();

        public AboutForm()
        {
            Text = "关于 " + AppInfo.Title;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;     // 尺寸全部由 UiKit.S() 换算，避免二次缩放
            Font = UiKit.FontBody;
            BackColor = UiKit.Canvas;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            // 信息卡多了一行"缩放通路"，窗口相应加高
            ClientSize = UiKit.Sz(680, 652);
            try { Icon = AppIcon.Get(32); }
            catch { }

            // 高缩放机器（本机 3840x2160 用 300%，逻辑桌面只有 1280x720）上别让对话框超出工作区，
            // 否则底部的「关闭」按钮会被屏幕切掉
            try
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                if (Height > wa.Height - UiKit.S(12)) Height = Math.Max(UiKit.S(420), wa.Height - UiKit.S(12));
                if (Width > wa.Width - UiKit.S(12)) Width = Math.Max(UiKit.S(480), wa.Width - UiKit.S(12));
            }
            catch { }

            BuildUi();
        }

        // ==================================================================

        void BuildUi()
        {
            // ---------- 头图（品牌橙粉渐变） ----------
            var hero = new HeroPanel();
            hero.Dock = DockStyle.Top;
            hero.Height = UiKit.S(132);

            var mark = new LogoMark();
            mark.White = true;
            mark.Location = UiKit.Pt(30, 30);
            mark.Size = UiKit.Sz(88, 88);

            var name = new Label();
            name.Text = AppInfo.Name;
            name.Font = UiKit.FontHero;
            name.ForeColor = Color.White;
            name.AutoSize = true;
            name.BackColor = Color.Transparent;
            name.Location = UiKit.Pt(140, 28);

            var english = new Label();
            english.Text = AppInfo.EnglishName;
            english.Font = UiKit.FontHeroSub;
            english.ForeColor = Color.FromArgb(232, 255, 255, 255);
            english.AutoSize = true;
            english.BackColor = Color.Transparent;
            english.Location = UiKit.Pt(140, 72);

            var desc = new Label();
            desc.Text = AppInfo.Description;
            desc.Font = UiKit.FontLead;
            desc.ForeColor = Color.FromArgb(224, 255, 255, 255);
            desc.AutoSize = true;
            desc.BackColor = Color.Transparent;
            desc.Location = UiKit.Pt(140, 102);

            var ver = new Label();
            ver.Text = "v" + AppInfo.Version;
            ver.Font = UiKit.FontBodyBold;
            ver.ForeColor = Color.FromArgb(210, 255, 255, 255);
            ver.AutoSize = true;
            ver.BackColor = Color.Transparent;
            ver.Location = UiKit.Pt(600, 30);
            hero.Resize += delegate
            {
                ver.Location = new Point(hero.ClientSize.Width - ver.Width - UiKit.S(28), UiKit.S(30));
            };

            hero.Controls.Add(mark);
            hero.Controls.Add(name);
            hero.Controls.Add(english);
            hero.Controls.Add(desc);
            hero.Controls.Add(ver);

            // ---------- 底栏 ----------
            var footer = new BarPanel(true);
            footer.Dock = DockStyle.Bottom;
            footer.Height = UiKit.S(72);

            var made = new Label();
            made.Text = "© 2026 " + AppInfo.Author + "   ·   " + AppInfo.Name + " " + AppInfo.EnglishName;
            made.Font = UiKit.FontHint;
            made.ForeColor = UiKit.TextHint;
            made.AutoSize = true;
            made.Location = UiKit.Pt(24, 30);

            var btnSelfTest = new FlatButton("环境自检", FlatButton.Kind.Secondary);
            btnSelfTest.Size = UiKit.Sz(100, 38);
            btnSelfTest.Margin = new Padding(UiKit.S(10), 0, 0, 0);
            btnSelfTest.Click += delegate { Program.RunSelfTest(false); };

            var btnClose = new FlatButton("关闭", FlatButton.Kind.Primary);
            btnClose.Size = UiKit.Sz(96, 38);
            btnClose.Margin = new Padding(UiKit.S(10), 0, 0, 0);
            btnClose.DialogResult = DialogResult.OK;

            var footerButtons = new FlowLayoutPanel();
            footerButtons.FlowDirection = FlowDirection.RightToLeft;
            footerButtons.WrapContents = false;
            footerButtons.AutoSize = true;
            footerButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            footerButtons.Dock = DockStyle.Right;
            footerButtons.Padding = UiKit.Pad(0, 16, 24, 0);
            footerButtons.BackColor = UiKit.Card;
            footerButtons.Controls.Add(btnClose);
            footerButtons.Controls.Add(btnSelfTest);

            footer.Controls.Add(made);
            footer.Controls.Add(footerButtons);

            // ---------- 内容：两张卡片 ----------
            var body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.ColumnCount = 1;
            body.RowCount = 2;
            // 信息卡多了一行"缩放通路"（显卡行之后）：262 → 306
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, UiKit.S(306)));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, UiKit.S(112)));
            body.Padding = UiKit.Pad(20, 6, 20, 14);
            body.BackColor = UiKit.Canvas;

            var host1 = new CardShadowHost();
            host1.Dock = DockStyle.Fill;
            host1.Margin = new Padding(0, 0, 0, UiKit.S(10));
            host1.Controls.Add(BuildInfoCard());

            var host2 = new CardShadowHost();
            host2.Dock = DockStyle.Fill;
            host2.Margin = new Padding(0);
            host2.Controls.Add(BuildAuthorCard());

            body.Controls.Add(host1, 0, 0);
            body.Controls.Add(host2, 0, 1);

            Controls.Add(body);
            Controls.Add(hero);
            Controls.Add(footer);

            AcceptButton = btnClose;
            CancelButton = btnClose;
        }

        // ---------- 卡片一：程序与运行环境 ----------

        Control BuildInfoCard()
        {
            var card = new CardPanel("应用信息");
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0);

            AddValueRow(card, "版本", "v" + AppInfo.Version + "   （" + AppInfo.EnglishName + "）", 26);
            AddValueRow(card, "软件说明", AppInfo.Description, 26);

            string gpu;
            try { gpu = Switcher.EnvironmentText; }
            catch { gpu = "(读取失败)"; }
            AddValueRow(card, "显卡", gpu, 26);

            // v0.11 起缩放不再绑定 NVIDIA，独立一行显示"缩放通路"，
            // 让用户看到当前走的是 NVAPI 还是 Windows 原生接口（DEVMODE.dmDisplayFixedOutput）；
            // 后端文案可能折行，所以用带换行的行（高度给足，见 AddWrappedValueRow）
            string backend;
            try { backend = Switcher.ScalingBackendText; }
            catch (Exception e) { backend = "读取失败：" + e.Message; }
            AddWrappedValueRow(card, "缩放通路", backend, 44);

            string mode = "";
            try
            {
                mode = DisplayInfo.CurrentModeText() + "   ·   缩放 " + Switcher.GetCurrentScalingText();
            }
            catch (Exception e) { mode = "读取失败：" + e.Message; }
            AddValueRow(card, "当前显示模式", mode, 28);

            AddPathRow(card, "配置文件", AppPaths.IniPath, 34);
            AddPathRow(card, "运行日志", AppPaths.LogPath, 34);

            return card;
        }

        void AddValueRow(CardPanel card, string label, string value, int height)
        {
            Label v = UiKit.TextLabel(value, UiKit.FontBody, UiKit.TextPrimary, ContentAlignment.MiddleLeft);
            v.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            card.AddRow(label, v, height);
        }

        /// <summary>
        /// 可能折行的值行（如"缩放通路"的后端说明）：
        /// Label 得关掉 AutoEllipsis 才会自动折行，且高度必须按行数给足，否则第二行会被切掉；
        /// 完整文字同时挂 ToolTip 兜底。
        /// </summary>
        void AddWrappedValueRow(CardPanel card, string label, string value, int height)
        {
            Label v = UiKit.TextLabel(value, UiKit.FontBody, UiKit.TextPrimary, ContentAlignment.MiddleLeft);
            v.AutoEllipsis = false;                       // 关掉省略号才允许折行
            v.TextAlign = ContentAlignment.TopLeft;       // 折行后从顶部排，视觉上和标签行对齐
            int lineH = (int)Math.Ceiling(v.Font.GetHeight(v.CreateGraphics()));
            v.Height = Math.Max(UiKit.S(22), lineH * 3 + UiKit.S(2));   // 按 3 行给高度，短文案折行也不至于溢出
            _tips.SetToolTip(v, value);
            v.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            card.AddRow(label, v, height);
        }

        /// <summary>路径行：左侧省略显示完整路径（ToolTip 显示全文），右侧一个「打开」按钮</summary>
        void AddPathRow(CardPanel card, string label, string path, int height)
        {
            Label v = UiKit.TextLabel(path, UiKit.FontBody, UiKit.TextPrimary, ContentAlignment.MiddleLeft);
            v.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            v.Margin = new Padding(0, 0, UiKit.S(10), 0);
            _tips.SetToolTip(v, path);

            var open = new FlatButton("打开", FlatButton.Kind.Secondary, 7);
            open.Size = UiKit.Sz(76, 32);
            open.Anchor = AnchorStyles.Right;
            open.Margin = new Padding(0);
            string target = path;
            open.Click += delegate { OpenPath(target, label); };

            var row = UiKit.NestedRow(36);
            row.ColumnCount = 2;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiKit.S(76)));
            row.Controls.Add(v, 0, 0);
            row.Controls.Add(open, 1, 0);

            card.AddRow(label, row, height);
        }

        void OpenPath(string path, string what)
        {
            try
            {
                if (File.Exists(path)) Process.Start("notepad.exe", "\"" + path + "\"");
                else
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        Process.Start("explorer.exe", "\"" + dir + "\"");
                    else
                        MessageBox.Show(this, "找不到" + what + "：" + path, AppInfo.Title,
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "打开失败：" + e.Message, AppInfo.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---------- 卡片二：作者（只留一行社交主页） ----------

        Control BuildAuthorCard()
        {
            var card = new CardPanel("作者");
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0);

            // 一行搞定：社交名字（可点，打开主页） + 只读的地址文字。
            // 用 TableLayoutPanel 让两个控件在单元格里垂直居中，不会一个浮在上面一个沉在下面。
            int count = Math.Min(AppInfo.SocialNames.Length, AppInfo.SocialUrls.Length);
            string siteName = count > 0 ? AppInfo.SocialNames[0] : "";
            string siteUrl = count > 0 ? AppInfo.SocialUrls[0] : "";

            var row = new TableLayoutPanel();
            row.ColumnCount = 2;
            row.RowCount = 1;
            row.AutoSize = true;
            row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.Anchor = AnchorStyles.Left;
            row.Margin = new Padding(0);
            row.Padding = new Padding(0);
            row.BackColor = UiKit.Card;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            string linkText = AppInfo.Author + " ↗";
            var link = new FlatButton(linkText, FlatButton.Kind.Link, 7);
            link.Font = UiKit.FontOf(11f, FontStyle.Bold);
            link.Size = new Size(TextRenderer.MeasureText(linkText, link.Font).Width + UiKit.S(14), UiKit.S(34));
            link.Margin = new Padding(0, 0, UiKit.S(14), 0);
            link.Click += delegate { OpenSocial(siteName, siteUrl); };
            _tips.SetToolTip(link, AppInfo.Author + " · " + siteName + "（" + siteUrl + "）");

            var urlText = new Label();
            urlText.Text = AppInfo.SocialDisplay;
            urlText.Font = UiKit.FontHint;
            urlText.ForeColor = UiKit.TextHint;
            urlText.AutoSize = true;
            urlText.BackColor = Color.Transparent;
            urlText.Anchor = AnchorStyles.Left;
            urlText.Margin = new Padding(0);
            _tips.SetToolTip(urlText, siteUrl);

            row.Controls.Add(link, 0, 0);
            row.Controls.Add(urlText, 1, 0);
            card.AddRow("社交主页", row, 46);

            return card;
        }

        void OpenSocial(string site, string url)
        {
            try
            {
                Process.Start(url);
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "打开 " + site + " 失败：" + e.Message
                    + "\n\n可以手工访问：" + url, AppInfo.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    /// <summary>渐变底上的白色 logo（透明背景，父面板的渐变会透过来）</summary>
    public class LogoMark : Control
    {
        public bool White = true;

        public LogoMark()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            UiKit.DrawLogo(e.Graphics, new Rectangle(0, 0, Width, Height), White);
        }
    }
}
