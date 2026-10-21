using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace PortalJump
{
    /// <summary>
    /// 主界面半透明底纹：读取程序目录里的背景素材（zoe-bg.png），压暗成一层柔和的背景，
    /// 让卡片"浮"在图案上而不是浮在纯色上。
    ///
    /// 做法：先把素材按"铺满右侧"缩放，叠到画布底色上（低透明度），再补一层左侧渐隐 + 白纱，
    /// 保证左边的文字区域依然干净、卡片上的文字对比度不受影响。
    /// 素材不存在时 HasImage=false，界面自动退回纯色（不报错）。
    /// </summary>
    public static class Backdrop
    {
        public static readonly string[] Names = new string[]
        {
            "zoe-bg.png", "zoe.png", "background.png", "背景.png"
        };

        static bool _loaded;
        static Image _img;

        /// <summary>背景素材（无素材为 null）</summary>
        public static Image Source
        {
            get { EnsureLoaded(); return _img; }
        }

        public static bool HasImage
        {
            get { EnsureLoaded(); return _img != null; }
        }

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                _img = LoadFirst(AppPaths.ExeDir);
                if (_img == null)
                {
                    string data = AppPaths.DataDir;
                    if (!string.Equals(data, AppPaths.ExeDir, StringComparison.OrdinalIgnoreCase)) _img = LoadFirst(data);
                }
            }
            catch { }
        }

        static Image LoadFirst(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            foreach (string n in Names)
            {
                try
                {
                    string p = Path.Combine(dir, n);
                    if (!File.Exists(p)) continue;
                    byte[] bytes = File.ReadAllBytes(p);
                    return Image.FromStream(new MemoryStream(bytes));
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// 生成整块底纹位图（尺寸 w×h）：画布底色 + 素材（铺满右侧、半透明）+ 左侧渐隐 + 白纱。
        /// 调用方按面板位置裁剪这张图，左右两块就能接得上、看起来是一整张背景。
        /// </summary>
        public static Bitmap Veil(int w, int h)
        {
            if (w < 8 || h < 8) return null;
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            Image src = Source;
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(AppInfo.CanvasColor);
                if (src != null)
                {
                    // 素材只铺右侧 55%（原来 80%）。左侧是文字密集区，插画铺太宽会跟
                    // 卡片标题、字段标签抢注意力；收窄后只在窗口右缘一带露出，作为氛围而非图案。
                    DrawCover(g, src, new Rectangle((int)(w * 0.45), 0, (int)(w * 0.55), h), 255);
                }
            }
            if (src == null) return bmp;

            // 关键：用**乘法混色**而不是半透明叠加。
            // 素材是接近纯白的浅色线稿，画布（#FBF8FE）同样是浅色 ——
            // 半透明叠加在数学上几乎等于没变化，这就是"背景完全看不到"的原因。
            // 乘法混色下：白色处结果 = 画布色（等于看不见），有线条/颜色的地方才把画布压暗压出色彩。
            int cR = AppInfo.CanvasColor.R, cG = AppInfo.CanvasColor.G, cB = AppInfo.CanvasColor.B;
            int fadeW = Math.Max(1, (int)(w * 0.50));      // 左侧渐隐：左边文字区保持干净
            // 插画强度。原来 0.95 几乎把线稿原样压进底纹，高 DPI 下线条很抢眼；
            // 降到 0.62 后只剩淡淡一层氛围，卡片与文字成为主角。
            const double Strength = 0.62;

            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite,
                PixelFormat.Format32bppArgb);
            try
            {
                int stride = bd.Stride;
                byte[] buf = new byte[stride * h];
                Marshal.Copy(bd.Scan0, buf, 0, buf.Length);

                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int i = row + x * 4;
                        int b = buf[i], gg = buf[i + 1], r = buf[i + 2];

                        int mr = cR * r / 255, mg = cG * gg / 255, mb = cB * b / 255;

                        double t = (x < fadeW ? (double)x / fadeW : 1.0) * Strength;   // 越靠左越接近纯画布色

                        buf[i] = (byte)(cB + (mb - cB) * t);
                        buf[i + 1] = (byte)(cG + (mg - cG) * t);
                        buf[i + 2] = (byte)(cR + (mr - cR) * t);
                        buf[i + 3] = 255;
                    }
                }
                Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
            }
            finally { bmp.UnlockBits(bd); }

            return bmp;
        }

        /// <summary>
        /// 取底纹在某个区域里的"偏亮平均色"（跳过深色线稿）。
        /// 用途：原生控件（下拉框/数字框/文本框/列表）不支持透明背景，
        /// 给它们上一个和该处背景协调的浮层色，就不会出现一整块死白。
        /// </summary>
        public static Color AverageLight(Bitmap veil, Rectangle rect)
        {
            if (veil == null) return Color.White;
            rect.Intersect(new Rectangle(0, 0, veil.Width, veil.Height));
            if (rect.Width <= 0 || rect.Height <= 0) return Color.White;

            long r = 0, g = 0, b = 0; int n = 0;
            for (int y = rect.Top; y < rect.Bottom; y += 4)
            {
                for (int x = rect.Left; x < rect.Right; x += 4)
                {
                    Color c = veil.GetPixel(x, y);
                    int lum = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                    if (lum < 200) continue;
                    r += c.R; g += c.G; b += c.B; n++;
                }
            }
            if (n == 0) return Color.White;

            // 往白色方向混 22%：浮层色只需"不是死白"，太浓会显得像涂了色
            int rr = (int)(r / n), gg = (int)(g / n), bb = (int)(b / n);
            const double ToWhite = 0.22;
            return Color.FromArgb(
                (int)(rr + (255 - rr) * ToWhite),
                (int)(gg + (255 - gg) * ToWhite),
                (int)(bb + (255 - bb) * ToWhite));
        }
        /// <summary>把图片按"填满并居中裁切"画进目标矩形（可带整体透明度）</summary>
        static void DrawCover(Graphics g, Image img, Rectangle r, int alpha)
        {
            if (img == null || r.Width <= 0 || r.Height <= 0) return;
            float s = Math.Max((float)r.Width / img.Width, (float)r.Height / img.Height);
            int dw = (int)Math.Ceiling(img.Width * s);
            int dh = (int)Math.Ceiling(img.Height * s);
            int dx = r.X + (r.Width - dw) / 2;
            int dy = r.Y + (r.Height - dh) / 2;

            var cm = new ColorMatrix();
            cm.Matrix33 = Math.Max(0f, Math.Min(1f, alpha / 255f));
            using (var ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm);
                g.DrawImage(img, new Rectangle(dx, dy, dw, dh),
                    0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
            }
        }
    }
}
