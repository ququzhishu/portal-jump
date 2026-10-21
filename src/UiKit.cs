using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PortalJump
{
    /// <summary>
    /// 界面基础件：折返跃迁的主题层。
    ///
    /// 配色取自英雄联盟佐伊（暮光星灵）的橙 → 粉渐变：底色保持浅色（设置界面长时间看着不累），
    /// 品牌色集中在「英雄区渐变横幅 / 渐变主按钮 / 选中态 / 徽标」这些视觉焦点上。
    ///
    /// 关键点：所有尺寸都写成"96 DPI 下的逻辑像素"，再经 S() 按当前 DPI 放大。
    /// 否则在 200% 缩放的 4K 屏上（本机就是 192 DPI），字体是 2 倍高、布局只有 1 倍，必然互相压字。
    /// </summary>
    public static class UiKit
    {
        // ================= 配色（淡色系：低饱和紫 + 柔和中性灰） =================
        // 整体改用淡雅的低饱和配色：强调色从原来的中饱和紫(#A644BA)降到淡紫，
        // 底、边、字都往灰白方向靠，长时间看着不刺眼，也不与游戏画面抢视觉。
        public static readonly Color Canvas = AppInfo.CanvasColor;   // 单一来源：AppInfo
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(0xEB, 0xE7, 0xF2);
        public static readonly Color BorderStrong = Color.FromArgb(0xD8, 0xCE, 0xE8);
        public static readonly Color TextPrimary = Color.FromArgb(0x33, 0x2D, 0x3D);
        public static readonly Color TextSecondary = Color.FromArgb(0x6B, 0x62, 0x78);
        // 提示文字：淡色系下仍要保住正文级可读性。压到 #6E6480 后对白卡 5.05:1、对画布 4.72:1，
        // 明显比视觉上"够淡"的上限更深一点，但这是能读清的边界。
        public static readonly Color TextHint = Color.FromArgb(0x6E, 0x64, 0x80);
        // 强调色：淡紫。这里存在硬冲突 —— 颜色越淡越"淡色系"，但白字对比度越低。
        // 实测 #8B72B8 白字只有 4.04:1（不达标）；取 #7A61A8 后 4.86:1 达标，
        // 同时仍是明显偏淡的紫（不是原来那种饱和紫）。以可读性优先。
        public static readonly Color Accent = Color.FromArgb(0x7A, 0x61, 0xA8);
        public static readonly Color AccentHover = Color.FromArgb(0x8A, 0x71, 0xB8);
        public static readonly Color AccentPressed = Color.FromArgb(0x69, 0x52, 0x96);
        public static readonly Color AccentTint = Color.FromArgb(0xF6, 0xF3, 0xFB);
        public static readonly Color AccentTintBorder = Color.FromArgb(0xE3, 0xDC, 0xF2);
        public static readonly Color Hover = Color.FromArgb(0xF8, 0xF6, 0xFC);
        public static readonly Color Separator = Color.FromArgb(0xF0, 0xED, 0xF6);
        // 危险色：淡色系下也要能读清白字。#B84A62 白字 5.05:1。
        public static readonly Color Danger = Color.FromArgb(0xB8, 0x4A, 0x62);
        public static readonly Color DisabledFill = Color.FromArgb(0xF2, 0xEF, 0xF7);

        // 品牌色：统一为一个紫色实色，不再用渐变。
        // 原来这里是橙→粉→淡紫的三角渐变（GradOrange / GradCoral / GradPink），
        // 主按钮、顶栏分隔线、页头横幅、徽标、传送门光环各用其中一段，
        // 整个界面因此显得花。改成单一 Accent 紫后，强调色只有一个，信息层级更清楚。
        // 保留这三个名字只为兼容旧调用点，值已统一到 Accent 系。
        public static readonly Color GradOrange = Accent;
        public static readonly Color GradCoral = Accent;
        public static readonly Color GradPink = Accent;
        // 按钮用的两段色（已统一，保留名字仅为兼容）
        public static readonly Color GradButtonA = Accent;
        public static readonly Color GradButtonB = Accent;
        // 卡片投影的色调（淡紫，跟主题一致）
        public static readonly Color ShadowTint = Color.FromArgb(0x8B, 0x4C, 0xA8);

        // ---- DPI ----
        public static readonly float Scale;
        public static readonly int Dpi;

        // ---- 字体 ----
        static readonly string Family = PickFamily();

        public static readonly Font FontBody;
        public static readonly Font FontBodyBold;
        public static readonly Font FontHint;
        public static readonly Font FontCardTitle;
        public static readonly Font FontTitle;
        public static readonly Font FontHero;
        public static readonly Font FontHeroSub;
        public static readonly Font FontLead;

        static UiKit()
        {
            float s = 1f;
            int dpi = 96;
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    dpi = (int)Math.Round(g.DpiX);
                    s = g.DpiX / 96f;
                }
            }
            catch { }
            if (s <= 0.1f || s > 8f) s = 1f;
            Scale = s;
            Dpi = dpi;

            FontBody = new Font(Family, 9f, FontStyle.Regular, GraphicsUnit.Point);
            FontBodyBold = new Font(Family, 9f, FontStyle.Bold, GraphicsUnit.Point);
            FontHint = new Font(Family, 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            FontCardTitle = new Font(Family, 10.5f, FontStyle.Bold, GraphicsUnit.Point);
            FontTitle = new Font(Family, 15f, FontStyle.Bold, GraphicsUnit.Point);
            FontHero = new Font(Family, 21f, FontStyle.Bold, GraphicsUnit.Point);
            FontHeroSub = new Font(Family, 11f, FontStyle.Bold, GraphicsUnit.Point);
            FontLead = new Font(Family, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        }

        /// <summary>按同一字体族要一个新字号</summary>
        public static Font FontOf(float size, FontStyle style)
        {
            return new Font(Family, size, style, GraphicsUnit.Point);
        }

        /// <summary>字体族名（AboutForm 里给"区区之数"这类签名用）</summary>
        public static string FamilyName
        {
            get { return Family; }
        }

        /// <summary>
        /// 品牌标题字体族：主界面左上角「Portal Jump」用的展示字体。
        ///
        /// **只用开源许可字体，避免商业风险**：
        ///   - Cascadia Code（OFL-1.1，微软开源，等宽技术感最强）← 首选
        ///   - Noto Sans SC（OFL-1.1，Google 开源，稳重）
        ///   - DejaVu Sans（免费许可，Linux 常见兜底）
        /// 都取不到就退回正文族，不会出现方块字。
        /// </summary>
        static readonly string BrandFamily = PickBrandFamily();

        static string PickBrandFamily()
        {
            string[] prefer = {
                "Cascadia Code",        // OFL-1.1
                "Cascadia Mono",        // OFL-1.1
                "Noto Sans SC",         // OFL-1.1
                "DejaVu Sans",          // 免费许可
            };
            foreach (string name in prefer)
            {
                try
                {
                    using (var f = new Font(name, 9f, FontStyle.Bold))
                    {
                        if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return name;
                    }
                }
                catch { }
            }
            return PickFamily();
        }

        /// <summary>品牌标题字体（每次新建，调用方负责释放）</summary>
        public static Font BrandTitleFont(float size)
        {
            return new Font(BrandFamily, size, FontStyle.Bold, GraphicsUnit.Point);
        }

        /// <summary>品牌标题用的字体族名（关于页可显示，便于确认许可证来源）</summary>
        public static string BrandFamilyName
        {
            get { return BrandFamily; }
        }

        static string PickFamily()
        {
            string[] prefer = { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" };
            foreach (string name in prefer)
            {
                try
                {
                    using (var f = new Font(name, 9f))
                    {
                        if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return name;
                    }
                }
                catch { }
            }
            return FontFamily.GenericSansSerif.Name;
        }

        /// <summary>逻辑像素 -> 实际像素</summary>
        public static int S(int logical)
        {
            return (int)Math.Round(logical * Scale);
        }

        public static Size Sz(int w, int h)
        {
            return new Size(S(w), S(h));
        }

        public static Point Pt(int x, int y)
        {
            return new Point(S(x), S(y));
        }

        public static Padding Pad(int all)
        {
            return new Padding(S(all));
        }

        public static Padding Pad(int left, int top, int right, int bottom)
        {
            return new Padding(S(left), S(top), S(right), S(bottom));
        }

        // ================= 颜色工具 =================

        /// <summary>两色插值（t=0 取 a，t=1 取 b）</summary>
        public static Color Blend(Color a, Color b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            return Color.FromArgb(
                a.A + (int)((b.A - a.A) * t),
                a.R + (int)((b.R - a.R) * t),
                a.G + (int)((b.G - a.G) * t),
                a.B + (int)((b.B - a.B) * t));
        }

        public static Color Lighten(Color c, float t) { return Blend(c, Color.White, t); }

        /// <summary>带透明度的同色（用于半透明覆盖层）</summary>
        public static Color Alpha(Color c, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c);
        }

        // ================= 笔刷（已去渐变，统一纯色） =================
        //
        // 原来这里返回 LinearGradientBrush，各调用点画的是橙→粉→紫的渐变。
        // 按"去掉渐变色彩"的要求，全部改为纯色实心；返回类型放宽到 Brush，
        // 这样所有调用点的 using (…brush…) 代码一行都不用改。

        /// <summary>两色渐变 → 实为纯色（取 a）。保留签名兼容旧调用。</summary>
        public static Brush GradientBrush(Rectangle r, Color a, Color b, float angle)
        {
            return new SolidBrush(a);
        }

        /// <summary>三色渐变 → 实为纯色（取中间色 b，视觉上最接近原本的平均观感）。</summary>
        public static Brush TriGradientBrush(Rectangle r, Color a, Color b, Color c, float angle)
        {
            return new SolidBrush(b);
        }

        /// <summary>品牌主色刷（原三段渐变，现为 Accent 纯色）</summary>
        public static Brush BrandBrush(Rectangle r, float angle)
        {
            return new SolidBrush(Accent);
        }

        /// <summary>主按钮刷（原两段渐变，现为 Accent 纯色）</summary>
        public static Brush BrandButtonBrush(Rectangle r, float angle)
        {
            return new SolidBrush(Accent);
        }

        public static void FillBrand(Graphics g, GraphicsPath path, Rectangle bounds, float angle)
        {
            using (Brush br = BrandBrush(bounds, angle)) g.FillPath(br, path);
        }

        // ================= 圆角 =================

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(0, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            if (d <= 1)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void ApplyRoundedRegion(Control c, int radiusLogical)
        {
            if (radiusLogical <= 0 || c.Width <= 0 || c.Height <= 0) return;
            try
            {
                using (GraphicsPath p = RoundRect(new Rectangle(0, 0, c.Width, c.Height), S(radiusLogical)))
                {
                    Region old = c.Region;
                    c.Region = new Region(p);
                    if (old != null) old.Dispose();
                }
            }
            catch { }
        }

        /// <summary>在矩形外面画一圈柔和投影（父容器 OnPaint 里调用，子控件再覆盖上去）</summary>
        public static void DrawSoftShadow(Graphics g, Rectangle r, int radius, int depth, Color tint, int alpha)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                for (int i = depth; i >= 1; i--)
                {
                    var rr = new Rectangle(r.X - i, r.Y - i + S(1), r.Width + i * 2, r.Height + i * 2);
                    using (GraphicsPath p = RoundRect(rr, radius + i))
                    using (var b = new SolidBrush(Alpha(tint, alpha)))
                    {
                        g.FillPath(b, p);
                    }
                }
            }
            finally { g.SmoothingMode = old; }
        }

        /// <summary>四角星（佐伊的星）顶点</summary>
        public static PointF[] SparklePoints(float cx, float cy, float outer, float inner, int points)
        {
            var list = new PointF[points * 2];
            double step = Math.PI / points;
            double a0 = -Math.PI / 2;
            for (int i = 0; i < points * 2; i++)
            {
                double a = a0 + i * step;
                float rr = (i % 2 == 0) ? outer : inner;
                list[i] = new PointF(cx + (float)(Math.Cos(a) * rr), cy + (float)(Math.Sin(a) * rr));
            }
            return list;
        }

        // ================= 程序图标：暮光之门 =================

        /// <summary>画 logo：一圈传送门环 + 中心四角星。white=true 时画成白色（用于渐变底上）</summary>
        public static void DrawLogo(Graphics g, Rectangle r, bool white)
        {
            if (r.Width <= 2 || r.Height <= 2) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                float w = Math.Min(r.Width, r.Height);
                float cx = r.X + r.Width / 2f;
                float cy = r.Y + r.Height / 2f;

                Color ringA = white ? Color.FromArgb(245, 255, 255, 255) : Accent;
                Color ringB = white ? Color.FromArgb(140, 255, 255, 255) : Accent;
                float penW = Math.Max(1.4f, w * 0.075f);
                float d = w * 0.92f - penW;

                // 圆环原本是橙→紫渐变描边，现改为 Accent 纯色（去渐变）
                using (var pen = new Pen(ringA, penW))
                {
                    g.DrawEllipse(pen, cx - d / 2f, cy - d / 2f, d, d);
                }

                float outer = w * 0.30f;
                PointF[] star = SparklePoints(cx, cy, outer, outer * 0.24f, 4);
                if (white)
                {
                    using (var b = new SolidBrush(Color.White)) g.FillPolygon(b, star);
                }
                else
                {
                    // 中心四角星原本也是渐变填充，现改为纯色
                    using (var b = new SolidBrush(Accent))
                    {
                        g.FillPolygon(b, star);
                    }
                }
            }
            finally { g.SmoothingMode = old; }
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>生成程序图标：渐变圆角方块 + 白色传送门标志（托盘 / 窗口 / 任务栏共用）</summary>
        public static Icon MakeLogoIcon(int size)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    var full = new Rectangle(0, 0, size, size);
                    int radius = Math.Max(2, (int)Math.Round(size * 0.24));
                    using (GraphicsPath p = RoundRect(new Rectangle(0, 0, size - 1, size - 1), radius))
                    {
                        FillBrand(g, p, full, 30f);
                    }

                    int inset = (int)Math.Round(size * 0.14);
                    DrawLogo(g, new Rectangle(inset, inset, size - inset * 2, size - inset * 2), true);
                }

                IntPtr h = bmp.GetHicon();
                try
                {
                    using (Icon tmp = Icon.FromHandle(h))
                    {
                        return (Icon)tmp.Clone();
                    }
                }
                finally { DestroyIcon(h); }
            }
        }

        // ================= 控件工厂 =================

        /// <summary>
        /// 字段名：右对齐贴住输入框，形成整齐的一列（表单看起来"工整"的关键）。
        /// </summary>
        public static Label FieldLabel(string text)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Dock = DockStyle.Fill;
            l.TextAlign = ContentAlignment.MiddleRight;
            l.Font = FontBody;
            l.ForeColor = TextSecondary;
            l.Padding = new Padding(0, 0, S(12), 0);
            return l;
        }

        /// <summary>
        /// 灰色小字说明。高度必须按字体实际高度给足：
        /// AutoSize=false 的 Label 默认只有 23 物理像素高，而 200% 缩放下 8.25pt 字本身就要 28px 左右，
        /// 不给高度就会把字的上下切掉（看起来"显示不完全"）。
        /// </summary>
        public static Label HintLabel(string text)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Font = FontHint;
            l.ForeColor = TextHint;
            l.Height = FontHint.Height + S(4);
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        /// <summary>普通文字标签（高 DPI 下必须显式给高度，别用 AutoSize 混排）</summary>
        public static Label TextLabel(string text, Font font, Color color, ContentAlignment align)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Font = font;
            l.ForeColor = color;
            l.TextAlign = align;
            l.AutoEllipsis = true;
            l.Height = font.Height + S(4);
            return l;
        }

        /// <summary>
        /// 嵌套在表格单元里的一行：宽度铺满单元格、内部再分列，
        /// 用来让"输入框 + 右侧按钮"这种组合的右边缘与其它行严格对齐。
        /// </summary>
        public static TableLayoutPanel NestedRow(int heightLogical)
        {
            var t = new TableLayoutPanel();
            t.RowCount = 1;
            t.Margin = new Padding(0);
            t.Padding = new Padding(0);
            t.Height = S(heightLogical);
            t.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            t.BackColor = Card;
            return t;
        }

        /// <summary>
        /// 输入控件的包装容器：**只做布局**，不画任何装饰。
        ///
        /// 早先这里画过一条下划线来提示"可输入"，但原生控件现在保留了 FixedSingle 边框
        /// （去掉边框会丢 NumericUpDown 的上下按钮和 ComboBox 的下拉箭头），
        /// 再加下划线就成了双重线条。所以退回纯布局容器：
        /// 作用只是让各输入控件高度统一、Dock=Fill 撑满表格单元。
        /// </summary>
        public class UnderlineHost : Panel
        {
            public UnderlineHost()
            {
                SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Color.Transparent;
                Padding = new Padding(0);
            }
        }

        // ---- 输入类控件的去底色处理 ----
        //
        // 用户要求"可输入框去掉底色"。WinForms 原生 TextBox/ComboBox/NumericUpDown
        // **不支持真透明**（BackColor=Transparent 会被忽略，退回系统灰）。
        // 做法：卡片改成完全不透明白（CardPanel.FillAlpha=255），输入框也用纯白，
        // 两者同色 → 视觉上没有"底色块"。
        //
        // 【重要】不能用 BorderStyle.None / FlatStyle.Flat 把边框去掉：
        // 实测这会连带删掉 NumericUpDown 的「前进/后退」按钮和 ComboBox 的「下拉按钮」
        // （UI Automation 枚举从 25 个 Button 掉到 20 个）。这些子按钮是原生控件内部
        // 不可自绘的部分，去掉边框等于去掉交互。所以保留 BorderStyle.FixedSingle。
        static readonly Color InputSurface = Color.White;

        public static void StyleTextBox(TextBox t)
        {
            t.Font = FontBody;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.BackColor = InputSurface;
            t.ForeColor = TextPrimary;
        }

        public static void StyleCombo(ComboBox c)
        {
            c.Font = FontBody;
            // Standard 保留原生下拉箭头（Flat 会丢），白底 + 1px 边框融入卡片
            c.FlatStyle = FlatStyle.Standard;
            c.BackColor = InputSurface;
            c.ForeColor = TextPrimary;
        }

        public static void StyleNumeric(NumericUpDown n)
        {
            n.Font = FontBody;
            // FixedSingle 保留原生上下调节按钮（None 会丢）
            n.BorderStyle = BorderStyle.FixedSingle;
            n.BackColor = InputSurface;
            n.ForeColor = TextPrimary;
        }

        public static CheckBox CheckBoxFlat(string text)
        {
            var c = new CheckBox();
            c.Text = text;
            c.Font = FontBody;
            c.ForeColor = TextPrimary;
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = Color.Transparent;
            c.AutoSize = true;
            c.Cursor = Cursors.Hand;
            return c;
        }

        /// <summary>托盘 / 右键菜单的浅色主题渲染器</summary>
        public static ToolStripRenderer MenuRenderer()
        {
            return new ToolStripProfessionalRenderer(new ZoeMenuColors());
        }
    }

    /// <summary>菜单配色表：浅底 + 粉色悬停，配合佐伊主题</summary>
    public sealed class ZoeMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return UiKit.Card; } }
        public override Color MenuBorder { get { return UiKit.BorderStrong; } }
        public override Color MenuItemBorder { get { return UiKit.AccentTintBorder; } }
        public override Color MenuItemSelected { get { return UiKit.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return UiKit.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return UiKit.AccentTint; } }
        public override Color MenuItemPressedGradientBegin { get { return UiKit.AccentTint; } }
        public override Color MenuItemPressedGradientMiddle { get { return UiKit.AccentTint; } }
        public override Color MenuItemPressedGradientEnd { get { return UiKit.AccentTint; } }
        public override Color ImageMarginGradientBegin { get { return UiKit.Card; } }
        public override Color ImageMarginGradientMiddle { get { return UiKit.Card; } }
        public override Color ImageMarginGradientEnd { get { return UiKit.Card; } }
        public override Color SeparatorDark { get { return UiKit.Separator; } }
        public override Color SeparatorLight { get { return UiKit.Card; } }
        public override Color CheckBackground { get { return UiKit.AccentTint; } }
        public override Color CheckSelectedBackground { get { return UiKit.AccentTintBorder; } }
        public override Color CheckPressedBackground { get { return UiKit.AccentTintBorder; } }
        public override Color ButtonSelectedHighlight { get { return UiKit.Hover; } }
        public override Color ButtonSelectedBorder { get { return UiKit.AccentTintBorder; } }
    }

    /// <summary>
    /// 自绘按钮：
    ///   Primary   —— 品牌橙粉渐变 + 白字（主操作）
    ///   Secondary —— 白底描边（次操作）
    ///   Ghost     —— 无底无框（放弃类操作）
    ///   Link      —— 粉色文字 + 悬停下划线（"关于"这类跳转）
    /// 圆角用"先刷父容器底色再画圆角"的方式实现，边缘是抗锯齿的（Region 裁剪会有锯齿）。
    /// </summary>
    public class FlatButton : Button
    {
        public enum Kind { Primary, Secondary, Ghost, Link }

        readonly Kind _kind;
        readonly int _radius;
        bool _hover, _down;
        Color _surface = UiKit.Card;

        /// <summary>按钮所在容器的底色：用来擦掉圆角外的四个角。容器不是白色时必须显式设置。</summary>
        public Color Surface
        {
            get { return _surface; }
            set { _surface = value; Invalidate(); }
        }

        /// <summary>次级/幽灵按钮的填充色：主界面有底纹时用它调成和背景协调的浮层色</summary>
        public Color Fill = UiKit.Card;

        public FlatButton(string text, Kind kind) : this(text, kind, 8) { }

        public FlatButton(string text, Kind kind, int radiusLogical)
        {
            _kind = kind;
            _radius = radiusLogical;
            Text = text;
            Font = kind == Kind.Primary ? UiKit.FontBodyBold : UiKit.FontBody;
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            TextAlign = ContentAlignment.MiddleCenter;
            AutoSize = false;
            // 透明背景：按钮是自绘的，可以让父容器的底纹透过来（原生 Button 做不到这件事）
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            base.OnPaintBackground(e);   // 透明：先让父容器把底纹画出来（原来这里是 g.Clear 盖掉）

            var b = new Rectangle(0, 0, Width - 1, Height - 1);
            int r = UiKit.S(_radius);
            bool enabled = Enabled;
            Color fore;

            if (!enabled)
            {
                using (GraphicsPath p = UiKit.RoundRect(b, r))
                using (var fill = new SolidBrush(UiKit.DisabledFill))
                {
                    g.FillPath(fill, p);
                }
                fore = UiKit.TextHint;
            }
            else if (_kind == Kind.Primary)
            {
                using (GraphicsPath p = UiKit.RoundRect(b, r))
                {
                    Rectangle gr = new Rectangle(b.X, b.Y, b.Width, b.Height);
                    using (Brush br = UiKit.BrandButtonBrush(gr, 15f))
                    {
                        g.FillPath(br, p);
                    }
                    if (_down)
                    {
                        using (var ov = new SolidBrush(Color.FromArgb(46, 60, 0, 30))) g.FillPath(ov, p);
                    }
                    else if (_hover)
                    {
                        using (var ov = new SolidBrush(Color.FromArgb(34, 255, 255, 255))) g.FillPath(ov, p);
                    }
                }
                fore = Color.White;
            }
            else if (_kind == Kind.Link)
            {
                fore = _hover ? UiKit.AccentHover : UiKit.Accent;
            }
            else
            {
                Color bg = _down ? UiKit.Separator : (_hover ? UiKit.Hover : Fill);
                if (_kind == Kind.Secondary || _hover)
                {
                    Color fc = _kind == Kind.Secondary ? bg : UiKit.Hover;
                    using (GraphicsPath p = UiKit.RoundRect(b, r))
                    using (var fill = new SolidBrush(Color.FromArgb(168, fc.R, fc.G, fc.B)))   // 半透明填充
                    {
                        g.FillPath(fill, p);
                    }
                }
                if (_kind == Kind.Secondary)
                {
                    using (GraphicsPath p = UiKit.RoundRect(b, r))
                    using (var pen = new Pen(_hover ? UiKit.BorderStrong : UiKit.Border))
                    {
                        g.DrawPath(pen, p);
                    }
                }
                fore = _kind == Kind.Ghost ? UiKit.TextSecondary : UiKit.TextPrimary;
            }

            var textRect = new Rectangle(0, _down ? UiKit.S(1) : 0, Width, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            if (_kind == Kind.Link && _hover)
            {
                Size ts = TextRenderer.MeasureText(Text, Font);
                int tw = Math.Min(ts.Width, Width - UiKit.S(8));
                int x = (Width - tw) / 2;
                int y = (Height + ts.Height) / 2 - UiKit.S(2);
                using (var pen = new Pen(fore)) g.DrawLine(pen, x, y, x + tw, y);
            }

            if (Focused && ShowFocusCues && enabled)
            {
                Rectangle fr = new Rectangle(UiKit.S(2), UiKit.S(2), Width - UiKit.S(5), Height - UiKit.S(5));
                using (GraphicsPath p = UiKit.RoundRect(fr, Math.Max(1, r - UiKit.S(2))))
                using (var pen = new Pen(_kind == Kind.Primary
                    ? Color.FromArgb(150, 255, 255, 255) : UiKit.AccentTintBorder))
                {
                    pen.DashStyle = DashStyle.Dot;
                    g.DrawPath(pen, p);
                }
            }
        }
    }

    /// <summary>支持透明背景的普通面板（Panel 默认不支持 BackColor=Transparent）</summary>
    public class GlassPanel : Panel
    {
        public GlassPanel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Transparent;
        }
    }

    /// <summary>支持透明背景的表格容器（WinForms 的 TableLayoutPanel 默认不支持 BackColor=Transparent，
    /// 而卡片要透出主界面的半透明底纹，中间这一层就必须是透明的）</summary>
    public class GlassTable : TableLayoutPanel
    {
        public GlassTable()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Transparent;
        }
    }

    /// <summary>半透明白底圆角卡片（标题 + 两列表格内容）：卡片透出后面的底纹，看起来是"浮"在背景上</summary>
    public class CardPanel : Panel
    {
        public readonly TableLayoutPanel Body;
        readonly int _radius = 14;
        readonly string _title;
        int _row;

        /// <summary>
        /// 卡片白底的透明度（0-255）。255 = 不透明白底。
        ///
        /// 现在是 **255（完全不通明白）**：
        /// 输入框去底色后需要与卡片同色才能"看不见"，
        /// 但只要卡片带一点透明度，背景纹理就会把它染成 #EEEAED 这类色调，
        /// 而 WinForms 原生输入框只能用纯色背景，两者必然有色差、露出一个方块。
        /// 改成不透明后卡片是纯白，输入框也用纯白，方块感彻底消失。
        /// 底纹仍在卡片之间的留白、顶栏与底栏透出，氛围没有丢。
        /// </summary>
        public int FillAlpha = 255;

        public CardPanel(string title)
        {
            _title = title;
            // 透明：让父容器（带底纹的阴影容器 / 分割面板）先把背景画出来
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;

            Body = new GlassTable();
            Body.Dock = DockStyle.Fill;
            Body.ColumnCount = 2;
            Body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiKit.S(112)));
            Body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            // 上边距 0：标题行自己带着内边距，Body 不要再叠
            Body.Padding = UiKit.Pad(20, 0, 20, 14);

            // 标题不再用 Label 控件，改由 CardPanel.OnPaint 直接画文字。
            // 原因：Label 的 Dock=Top + TextAlign 组合在 WinForms 里行为反复（居中/顶对齐
            // 都会额外撑出留白，实测标题距卡片顶边在 214~341 物理像素之间浮动），
            // 无法稳定控制位置。改成自绘后坐标完全确定：标题基线固定在 TITLE_BASE。
            Body.Dock = DockStyle.Fill;
            // 上边距只让出标题区高度（TITLE_H），不要再多加。
            // 之前写成 TITLE_H+10 把内容区压矮，导致左侧卡片溢出、出现滚动条、按钮压住内容。
            Body.Padding = UiKit.Pad(20, TITLE_H + 2, 20, 14);

            Controls.Add(Body);
        }

        // ---- 标题区几何（逻辑像素，经 S() 按 DPI 放大）----
        // TITLE_H：标题区总高，分隔线画在这里；正文从这里往下开始。
        internal const int TITLE_H = 52;
        // 标题文字的绘制 y（TextRenderer 以文字左上角为锚点，所以这里留出上内边距）。
        internal const int TITLE_TOP = 16;

        /// <summary>
        /// 内容比卡片高时是否出现滚动条。
        /// 小屏 / 高缩放（本机 3840x2160 用 300%，逻辑桌面只有 1280x720）下保证每个字段都够得着。
        /// </summary>
        public bool Scrollable
        {
            get { return Body.AutoScroll; }
            set { Body.AutoScroll = value; }
        }

        public void AddRow(string label, Control content, int rowHeightLogical)
        {
            Body.RowStyles.Add(new RowStyle(SizeType.Absolute, UiKit.S(rowHeightLogical)));
            Body.Controls.Add(UiKit.FieldLabel(label), 0, _row);
            content.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            Body.Controls.Add(content, 1, _row);
            _row++;
            Body.RowCount = _row;
        }

        public void AddRowLeft(string label, Control content, int rowHeightLogical)
        {
            Body.RowStyles.Add(new RowStyle(SizeType.Absolute, UiKit.S(rowHeightLogical)));
            Body.Controls.Add(UiKit.FieldLabel(label), 0, _row);
            content.Anchor = AnchorStyles.Left;
            Body.Controls.Add(content, 1, _row);
            _row++;
            Body.RowCount = _row;
        }

        public void AddWide(Control content, int rowHeightLogical, bool stretch)
        {
            Body.RowStyles.Add(new RowStyle(SizeType.Absolute, UiKit.S(rowHeightLogical)));
            content.Anchor = stretch ? (AnchorStyles.Left | AnchorStyles.Right) : AnchorStyles.Left;
            Body.Controls.Add(content, 0, _row);
            Body.SetColumnSpan(content, 2);
            _row++;
            Body.RowCount = _row;
        }

        /// <summary>
        /// 放在"输入框那一列"（第 2 列）整行，用于说明文字、按钮条这类
        /// 需要与上方输入框左右边缘对齐的元素。
        ///
        /// 与 AddWide 的区别：AddWide 跨 2 列，左边缘会从标签列最左开始，
        /// 比输入框的起点更靠左 —— 用户反馈的"小字位置没对齐"就是这个原因。
        /// 这里只占第 2 列，左边缘与输入框严格对齐。
        /// </summary>
        public void AddInValueColumn(Control content, int rowHeightLogical, bool stretch)
        {
            Body.RowStyles.Add(new RowStyle(SizeType.Absolute, UiKit.S(rowHeightLogical)));
            content.Anchor = stretch ? (AnchorStyles.Left | AnchorStyles.Right) : AnchorStyles.Left;
            Body.Controls.Add(content, 1, _row);
            _row++;
            Body.RowCount = _row;
        }

        public void AddFill(Control content)
        {
            Body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            content.Dock = DockStyle.Fill;
            Body.Controls.Add(content, 0, _row);
            Body.SetColumnSpan(content, 2);
            _row++;
            Body.RowCount = _row;
        }

        public void AddFiller()
        {
            Body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _row++;
            Body.RowCount = _row;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaintBackground(e);   // 透明：让父容器（带底纹）先画
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var b = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = UiKit.RoundRect(b, UiKit.S(_radius)))
            {
                using (var fill = new SolidBrush(Color.FromArgb(Math.Max(0, Math.Min(255, FillAlpha)), 255, 255, 255)))
                {
                    g.FillPath(fill, p);
                }
                using (var pen = new Pen(UiKit.Border)) g.DrawPath(pen, p);
            }

            // ---- 标题 ----
            // 自绘而不是 Label：坐标完全确定，不受 WinForms Dock/TextAlign 的隐含留白影响。
            if (!string.IsNullOrEmpty(_title))
            {
                TextRenderer.DrawText(g, _title, UiKit.FontCardTitle,
                    new Point(UiKit.S(20), UiKit.S(TITLE_TOP)),
                    UiKit.TextPrimary,
                    TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            }

            // ---- 标题区与表单区的分隔线 ----
            // 原来用 Separator(#F0EDF6)，实测与卡片白底只差 1.13:1，屏幕上看不见，
            // 所以用户觉得"没有分割线"。改用边界色 Border，并在左右内缩到 20，不顶圆角。
            int sepY = UiKit.S(TITLE_H);
            if (Height > sepY + UiKit.S(8))
            {
                using (var pen = new Pen(UiKit.Border))
                {
                    g.DrawLine(pen, UiKit.S(20), sepY, Width - UiKit.S(21), sepY);
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UiKit.ApplyRoundedRegion(this, _radius);
        }
    }

    /// <summary>
    /// 卡片投影容器：透明背景（让主界面底纹透过来），只画一圈柔和阴影，卡片填满内部。
    /// 阴影必须画在父容器上（子控件随后覆盖中间），这样卡片本体还是原来的圆角卡。
    /// </summary>
    public class CardShadowHost : Panel
    {
        readonly int _pad;

        public CardShadowHost() : this(7) { }

        public CardShadowHost(int shadowPadLogical)
        {
            _pad = Math.Max(1, shadowPadLogical);
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Transparent;
            Padding = UiKit.Pad(_pad);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaintBackground(e);   // 透明：分割面板的底纹先画出来，阴影再压在上面
            base.OnPaint(e);
            // 阴影从强度 10 提到 22、扩散 14→18：卡片底色提亮后（FillAlpha 248），
            // 若阴影还那么淡，白卡片会和近白的顶栏/底栏糊成一片、失去「浮起」感。
            // 加倍阴影把卡片边界重新拉出来，同时不至于显脏。
            UiKit.DrawSoftShadow(e.Graphics, DisplayRectangle, UiKit.S(18), UiKit.S(_pad),
                UiKit.ShadowTint, 22);
        }
    }

    /// <summary>带一条 1px 分隔线的面板（用作顶栏/底栏）；LineGradient=true 时把线画成品牌渐变。
    /// 半透明白底：让主界面的底纹能透到顶栏/底栏上。</summary>
    public class BarPanel : Panel
    {
        public bool LineOnTop;
        public bool LineOnBottom;
        public bool LineGradient;

        /// <summary>
        /// 白底透明度（0-255）。
        /// 与 CardPanel 同理：210（82%）时顶部状态栏和底部按钮区被底纹染灰，
        /// 同一窗口里出现好几种底色。提到 244 让这两条横条读数稳定，
        /// 同时仍与卡片（248）形成极轻微的层次差。
        /// </summary>
        public int FillAlpha = 244;

        public BarPanel(bool lineOnTop)
        {
            LineOnTop = lineOnTop;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaintBackground(e);   // 透明：底纹先画出来
            base.OnPaint(e);
            using (var fill = new SolidBrush(Color.FromArgb(Math.Max(0, Math.Min(255, FillAlpha)), 255, 255, 255)))
            {
                e.Graphics.FillRectangle(fill, ClientRectangle);
            }
            if (LineGradient)
            {
                // 原来这里是一条橙→紫渐变的分隔线，去掉渐变后若直接用 Accent 纯色，
                // 整条横线会变成醒目的紫色实线，反而比渐变更抢眼。
                // 改为淡淡的主题色边线（同 UiKit.Border 一档），只起分隔作用。
                int y = LineOnTop ? 0 : Height - 1;
                var line = new Rectangle(0, y, Math.Max(1, Width), 1);
                using (var br = new SolidBrush(UiKit.Border))
                {
                    e.Graphics.FillRectangle(br, line);
                }
                return;
            }

            using (var pen = new Pen(UiKit.Border))
            {
                if (LineOnTop) e.Graphics.DrawLine(pen, 0, 0, Width, 0);
                if (LineOnBottom) e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            }
        }
    }

    /// <summary>
    /// 左上角品牌区：程序图标 + 软件名（参考 Clash Verge 的侧栏头部）。
    /// 图标从程序自身的 .ico 读取（与任务栏/资源管理器一致，改图标只需换 ico）；
    /// 读不到时退化成 UiKit.DrawLogo 画的矢量徽标，不会出现空白。
    /// </summary>
    /// <summary>
    /// 左上角品牌区：只有「Portal Jump」文字，**不放图标**（按用户要求）。
    /// 字体用独立的展示字族 Bahnschrift（降级 Segoe UI），比正文更粗更有工业感。
    /// 字距手动拉开一点，让纯文字标题有设计感而不显得单薄。
    /// </summary>
    public class BrandHeader : Panel
    {
        public BrandHeader()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            string text = AppInfo.EnglishName;

            int pad = UiKit.S(32);

            // 字号放大：13pt → 17pt（用户要求更醒目）。
            // Font 用 GraphicsUnit.Point 已是物理尺寸，不能再乘 Scale。
            using (Font f = UiKit.BrandTitleFont(17f))
            {
                // 字距拉开：逐字符绘制，每字后加固定间距，得到字母间距（tracking）效果。
                // TextRenderer 不支持字距参数，手工排是唯一稳的做法。
                float spacing = UiKit.S(1);
                float totalW = 0;
                foreach (char ch in text)
                {
                    totalW += TextRenderer.MeasureText(g, ch.ToString(), f,
                        Size.Empty, TextFormatFlags.NoPadding).Width + spacing;
                }

                float y = (Height - TextRenderer.MeasureText(g, "Ag", f,
                    Size.Empty, TextFormatFlags.NoPadding).Height) / 2f;
                // 整串在品牌区里水平居中会显得孤立，靠左对齐更自然；
                // 但留出 pad 之后若还有余量，轻微左偏更符合"侧栏标题"的视觉。
                float x = pad;
                foreach (char ch in text)
                {
                    string s = ch.ToString();
                    TextRenderer.DrawText(g, s, f, new Point((int)x, (int)y),
                        UiKit.TextPrimary, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    x += TextRenderer.MeasureText(g, s, f, Size.Empty, TextFormatFlags.NoPadding).Width + spacing;
                }
            }
        }
    }

    /// <summary>顶部品牌横幅：Accent 纯色底 + 右侧传送门光环装饰（文字用透明 Label 叠在上面）。
    /// 原为橙粉渐变，按"去掉渐变"要求已改为单色。</summary>
    public class HeroPanel : Panel
    {
        public HeroPanel()
        {
            BackColor = UiKit.Accent;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle b = ClientRectangle;
            if (b.Width <= 0 || b.Height <= 0) return;

            // BrandBrush 现在返回 Accent 纯色实心刷，这里保持调用形式不变
            using (Brush br = UiKit.BrandBrush(b, 18f))
            {
                g.FillRectangle(br, b);
            }

            // 右侧的传送门光环 + 星点（都压得很淡，不能抢文字）
            int cx = b.Right - UiKit.S(118);
            int cy = b.Height / 2;
            using (var pen = new Pen(UiKit.Alpha(Color.White, 40), UiKit.S(2)))
            {
                g.DrawEllipse(pen, cx - UiKit.S(76), cy - UiKit.S(76), UiKit.S(152), UiKit.S(152));
            }
            using (var pen = new Pen(UiKit.Alpha(Color.White, 26), UiKit.S(1)))
            {
                g.DrawEllipse(pen, cx - UiKit.S(48), cy - UiKit.S(48), UiKit.S(96), UiKit.S(96));
            }

            DrawSpark(g, cx + UiKit.S(58), cy - UiKit.S(52), UiKit.S(11), 150);
            DrawSpark(g, cx - UiKit.S(64), cy + UiKit.S(44), UiKit.S(7), 110);
            DrawSpark(g, cx + UiKit.S(26), cy + UiKit.S(62), UiKit.S(5), 90);

            using (var pen = new Pen(UiKit.Alpha(Color.White, 28)))
            {
                g.DrawLine(pen, 0, b.Height - 1, b.Width, b.Height - 1);
            }
        }

        static void DrawSpark(Graphics g, int x, int y, int radius, int alpha)
        {
            PointF[] pts = UiKit.SparklePoints(x, y, radius, radius * 0.22f, 4);
            using (var brush = new SolidBrush(UiKit.Alpha(Color.White, alpha)))
            {
                g.FillPolygon(brush, pts);
            }
        }
    }

    /// <summary>预设列表：两行式自绘条目（名称 + 摘要），选中项带渐变左条，恢复目标带渐变徽标</summary>
    public class PresetListBox : ListBox
    {
        int _hot = -1;

        /// <summary>当前恢复目标 Key（只用于画徽标）</summary>
        public string RestoreKey = "";

        public PresetListBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            BorderStyle = BorderStyle.None;
            BackColor = UiKit.Card;
            ForeColor = UiKit.TextPrimary;
            Font = UiKit.FontBody;
            IntegralHeight = false;
            ItemHeight = UiKit.S(58);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = IndexFromPoint(e.Location);
            if (idx != _hot) { _hot = idx; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot != -1) { _hot = -1; Invalidate(); }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            var p = Items[e.Index] as Preset;
            if (p == null) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle b = e.Bounds;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            using (var bg = new SolidBrush(UiKit.Card)) g.FillRectangle(bg, b);

            var pill = new Rectangle(b.X + UiKit.S(6), b.Y + UiKit.S(4),
                Math.Max(UiKit.S(10), b.Width - UiKit.S(12)), Math.Max(UiKit.S(10), b.Height - UiKit.S(8)));
            if (selected || e.Index == _hot)
            {
                using (GraphicsPath path = UiKit.RoundRect(pill, UiKit.S(10)))
                using (var fill = new SolidBrush(selected ? UiKit.AccentTint : UiKit.Hover))
                {
                    g.FillPath(fill, path);
                }
            }
            if (selected)
            {
                // 选中指示：一条实心强调色短条（不用渐变，避免满屏"小渐变条"的观感）
                int barW = UiKit.S(3);
                var bar = new Rectangle(pill.X + UiKit.S(2), pill.Y + UiKit.S(8), barW,
                    Math.Max(UiKit.S(6), pill.Height - UiKit.S(16)));
                using (GraphicsPath path = UiKit.RoundRect(bar, barW / 2))
                using (var brush = new SolidBrush(UiKit.Accent))
                {
                    g.FillPath(brush, path);
                }
            }

            int padL = UiKit.S(20);
            bool isRestore = string.Equals(p.Key, RestoreKey, StringComparison.OrdinalIgnoreCase);
            int reserved = isRestore ? UiKit.S(88) : UiKit.S(12);

            var nameRect = new Rectangle(b.X + padL, b.Y + UiKit.S(10),
                Math.Max(UiKit.S(20), b.Width - padL - reserved), UiKit.S(20));
            TextRenderer.DrawText(g, (p.Name == null || p.Name.Length == 0) ? p.Key : p.Name,
                UiKit.FontBodyBold, nameRect, UiKit.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            string summary = p.ResolutionText + "   ·   " + p.ScalingText;
            if (p.HasProcessBinding)
            {
                // 关联程序多的时候只报第一个 + 总数，避免被省略号切成半截单词
                string procs = p.ProcessNames.Length > 1
                    ? p.ProcessNames[0] + " 等 " + p.ProcessNames.Length + " 个"
                    : p.ProcessNames[0];
                summary += "   ·   " + procs;
            }
            var sumRect = new Rectangle(b.X + padL, b.Y + UiKit.S(33),
                Math.Max(UiKit.S(20), b.Width - padL - UiKit.S(12)), UiKit.S(18));
            TextRenderer.DrawText(g, summary, UiKit.FontHint, sumRect, UiKit.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (isRestore)
            {
                int bw = UiKit.S(70), bh = UiKit.S(22);
                var badge = new Rectangle(b.Right - UiKit.S(18) - bw, b.Y + (b.Height - bh) / 2, bw, bh);
                using (GraphicsPath path = UiKit.RoundRect(badge, UiKit.S(11)))
                {
                    using (Brush br = UiKit.BrandBrush(badge, 20f)) g.FillPath(br, path);
                }
                TextRenderer.DrawText(g, "恢复目标", UiKit.FontHint, badge, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            if (e.Index < Items.Count - 1)
            {
                using (var pen = new Pen(UiKit.Separator))
                {
                    g.DrawLine(pen, b.X + padL, b.Bottom - 1, b.Right - UiKit.S(18), b.Bottom - 1);
                }
            }
        }
    }
}
