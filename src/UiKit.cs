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
        // ================= 配色（浅色底 + 橙粉渐变强调；粉调偏淡紫，呼应佐伊的暮光紫） =================
        public static readonly Color Canvas = AppInfo.CanvasColor;   // 单一来源：AppInfo
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(0xE9, 0xDD, 0xF6);
        public static readonly Color BorderStrong = Color.FromArgb(0xD6, 0xC1, 0xED);
        public static readonly Color TextPrimary = Color.FromArgb(0x2B, 0x1D, 0x35);
        public static readonly Color TextSecondary = Color.FromArgb(0x67, 0x54, 0x6F);
        public static readonly Color TextHint = Color.FromArgb(0x99, 0x86, 0xA7);
        public static readonly Color Accent = Color.FromArgb(0xB0, 0x4F, 0xC4);
        public static readonly Color AccentHover = Color.FromArgb(0xC4, 0x64, 0xD6);
        public static readonly Color AccentPressed = Color.FromArgb(0x8E, 0x3A, 0xA0);
        public static readonly Color AccentTint = Color.FromArgb(0xF9, 0xF1, 0xFE);
        public static readonly Color AccentTintBorder = Color.FromArgb(0xE4, 0xCB, 0xF7);
        public static readonly Color Hover = Color.FromArgb(0xFB, 0xF6, 0xFF);
        public static readonly Color Separator = Color.FromArgb(0xF2, 0xEA, 0xF9);
        public static readonly Color Danger = Color.FromArgb(0xD8, 0x29, 0x6B);
        public static readonly Color DisabledFill = Color.FromArgb(0xF3, 0xED, 0xF8);

        // 品牌渐变：橙 → 粉 → 淡紫（粉的那一端往淡紫偏，不再走品红）
        public static readonly Color GradOrange = Color.FromArgb(0xFF, 0x9E, 0x4F);
        public static readonly Color GradCoral = Color.FromArgb(0xFF, 0x7F, 0xAE);
        public static readonly Color GradPink = Color.FromArgb(0xC0, 0x6C, 0xDE);
        // 按钮用的两段渐变（更亮、对比更强）
        public static readonly Color GradButtonA = Color.FromArgb(0xFF, 0x9A, 0x4D);
        public static readonly Color GradButtonB = Color.FromArgb(0xD9, 0x5F, 0xC9);
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

        // ================= 渐变笔刷 =================

        public static LinearGradientBrush GradientBrush(Rectangle r, Color a, Color b, float angle)
        {
            Rectangle safe = r.Width <= 0 || r.Height <= 0 ? new Rectangle(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height)) : r;
            return new LinearGradientBrush(safe, a, b, angle);
        }

        public static LinearGradientBrush TriGradientBrush(Rectangle r, Color a, Color b, Color c, float angle)
        {
            LinearGradientBrush br = GradientBrush(r, a, c, angle);
            var blend = new ColorBlend(3);
            blend.Colors = new Color[] { a, b, c };
            blend.Positions = new float[] { 0f, 0.52f, 1f };
            br.InterpolationColors = blend;
            return br;
        }

        /// <summary>品牌三段渐变（橙 → 珊瑚粉 → 品红粉）</summary>
        public static LinearGradientBrush BrandBrush(Rectangle r, float angle)
        {
            return TriGradientBrush(r, GradOrange, GradCoral, GradPink, angle);
        }

        /// <summary>品牌两段渐变（按钮用）</summary>
        public static LinearGradientBrush BrandButtonBrush(Rectangle r, float angle)
        {
            return GradientBrush(r, GradButtonA, GradButtonB, angle);
        }

        public static void FillBrand(Graphics g, GraphicsPath path, Rectangle bounds, float angle)
        {
            using (LinearGradientBrush br = BrandBrush(bounds, angle)) g.FillPath(br, path);
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

                Color ringA = white ? Color.FromArgb(245, 255, 255, 255) : GradOrange;
                Color ringB = white ? Color.FromArgb(140, 255, 255, 255) : GradPink;
                float penW = Math.Max(1.4f, w * 0.075f);
                float d = w * 0.92f - penW;

                using (var br = new LinearGradientBrush(
                    new RectangleF(cx - w / 2f, cy - w / 2f, w, w), ringA, ringB, 25f))
                using (var pen = new Pen(br, penW))
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
                    using (var br2 = new LinearGradientBrush(
                        new RectangleF(cx - outer, cy - outer, outer * 2, outer * 2), GradOrange, GradPink, 35f))
                    using (var b = new SolidBrush(Color.White))
                    {
                        g.FillPolygon(br2, star);
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

        public static void StyleTextBox(TextBox t)
        {
            t.Font = FontBody;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.BackColor = Card;
            t.ForeColor = TextPrimary;
        }

        public static void StyleCombo(ComboBox c)
        {
            c.Font = FontBody;
            // Flat 风格的下拉框在白底上边框几乎看不见，看起来像浮空的文字，这里用 Standard 保证有 1px 边框
            c.FlatStyle = FlatStyle.Standard;
            c.BackColor = Card;
            c.ForeColor = TextPrimary;
        }

        public static void StyleNumeric(NumericUpDown n)
        {
            n.Font = FontBody;
            n.BorderStyle = BorderStyle.FixedSingle;
            n.BackColor = Card;
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
                    using (LinearGradientBrush br = UiKit.BrandButtonBrush(gr, 15f))
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
        int _row;

        /// <summary>卡片白底的透明度（0-255）。255 = 不透明白底，越小底纹越明显。</summary>
        public int FillAlpha = 196;

        public CardPanel(string title)
        {
            // 透明：让父容器（带底纹的阴影容器 / 分割面板）先把背景画出来
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;

            Body = new GlassTable();
            Body.Dock = DockStyle.Fill;
            Body.ColumnCount = 2;
            Body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiKit.S(112)));
            Body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            Body.Padding = UiKit.Pad(20, 2, 20, 14);

            var titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.Font = UiKit.FontCardTitle;
            titleLabel.ForeColor = UiKit.TextPrimary;
            titleLabel.AutoSize = false;
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = UiKit.S(44);
            // 左边距与内容区一致（20），标题和下面的输入框左边缘对齐
            titleLabel.Padding = UiKit.Pad(20, 0, 0, 0);
            // 透明背景：让父面板 OnPaint 画的卡片底色/圆角描边透出来
            titleLabel.BackColor = Color.Transparent;

            Controls.Add(Body);
            Controls.Add(titleLabel);
        }

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
            UiKit.DrawSoftShadow(e.Graphics, DisplayRectangle, UiKit.S(14), UiKit.S(_pad),
                UiKit.ShadowTint, 10);
        }
    }

    /// <summary>带一条 1px 分隔线的面板（用作顶栏/底栏）；LineGradient=true 时把线画成品牌渐变。
    /// 半透明白底：让主界面的底纹能透到顶栏/底栏上。</summary>
    public class BarPanel : Panel
    {
        public bool LineOnTop;
        public bool LineOnBottom;
        public bool LineGradient;

        /// <summary>白底透明度（0-255）</summary>
        public int FillAlpha = 210;

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
                int y = LineOnTop ? 0 : Height - 1;
                var line = new Rectangle(0, y, Math.Max(1, Width), 1);
                using (LinearGradientBrush br = UiKit.GradientBrush(line,
                    UiKit.Alpha(UiKit.GradOrange, 120), UiKit.Alpha(UiKit.GradPink, 120), 0f))
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

    /// <summary>顶部品牌渐变横幅：橙粉渐变 + 右侧传送门光环装饰（文字用透明 Label 叠在上面）</summary>
    public class HeroPanel : Panel
    {
        public HeroPanel()
        {
            BackColor = UiKit.GradOrange;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle b = ClientRectangle;
            if (b.Width <= 0 || b.Height <= 0) return;

            using (LinearGradientBrush br = UiKit.BrandBrush(b, 18f))
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
                    using (LinearGradientBrush br = UiKit.BrandBrush(badge, 20f)) g.FillPath(br, path);
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
