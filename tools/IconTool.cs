using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;

namespace PortalJump.Tools
{
    /// <summary>
    /// 素材后处理小工具（开发用，不进分发包）。
    ///
    ///   IconTool pixelize &lt;源图&gt; &lt;输出.png&gt; &lt;格数&gt; &lt;色数&gt; &lt;输出尺寸&gt; [keyWhite] [阈值]
    ///       像素风格化：块内平均降采样 → 限制调色板 → 最近邻放大。
    ///       例：IconTool pixelize icon.png pixel.png 64 24 512 keyWhite 240
    /// </summary>
    static class IconTool
    {
        static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0) { Usage(); return 2; }
                switch (args[0].ToLowerInvariant())
                {
                    case "pixelize": return Pixelize(args);
                    case "lineart": return LineArt(args);
                    default: Usage(); return 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("失败: " + ex.Message);
                return 1;
            }
        }

        static void Usage()
        {
            Console.WriteLine("用法:");
            Console.WriteLine("  IconTool pixelize <源图> <输出.png> <格数> <色数> <输出尺寸> [keyWhite] [阈值]");
            Console.WriteLine("  IconTool lineart  <源图> <输出.png> <色数> <描边粗细> <输出尺寸> [keyWhite] [阈值]");
            Console.WriteLine("      lineart：量化配色（平涂）+ Sobel 描边 -> 线稿 / 简笔画风格，结果确定、不受模型随机性影响");
        }

        /// <summary>
        /// 线稿化：把任意插画压成"平涂色块 + 粗描边"。
        /// 做法：高质量缩放到目标尺寸 → k-means 压成 N 色（平涂）→ 对亮度求 Sobel 梯度，
        /// 梯度大的地方涂成近黑色并向外扩张（描边）。这样出来的图在 16~32px 下靠形状和高对比轮廓辨识，
        /// 适合做图标；而且完全确定，不会像模型那样偶尔画出重复小人头。
        /// </summary>
        static int LineArt(string[] a)
        {
            if (a.Length < 6) { Usage(); return 2; }
            string src = a[1], outPath = a[2];
            int colors = int.Parse(a[3], CultureInfo.InvariantCulture);
            int outline = int.Parse(a[4], CultureInfo.InvariantCulture);     // 0 = 不描边
            int size = int.Parse(a[5], CultureInfo.InvariantCulture);
            bool keyWhite = a.Length > 6 && a[6].ToLowerInvariant().StartsWith("key");
            int keyThreshold = a.Length > 7 ? int.Parse(a[7], CultureInfo.InvariantCulture) : 242;

            using (Bitmap img = Load(src))
            {
                // 关键：在"工作分辨率"上做简化，再放大到输出尺寸。
                // 直接在高分辨率上描边会把眼睛高光之类的碎细节也描成黑团；先降采样就自然丢掉了。
                int n = Math.Max(96, Math.Min(size, 384));   // 直接在工作分辨率出图，不放大（放大会把平涂糊掉）
                Console.WriteLine("工作分辨率: " + n + " -> 输出 " + size);
                var buf = new int[n * n];       // 0xAARRGGBB
                using (var small = new Bitmap(n, n, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(small))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.Clear(Color.Transparent);
                        g.DrawImage(img, 0, 0, n, n);
                    }
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                            buf[y * n + x] = small.GetPixel(x, y).ToArgb();
                }

                // ---- 0) 先轻度降噪：两遍 3x3 均值模糊 ----
                // 不模糊的话，原图的细碎明暗会变成一堆孤立"边缘点"，描边出来满脸麻子。
                for (int pass = 0; pass < 1; pass++)
                {
                    var tmp = new int[n * n];
                    Array.Copy(buf, tmp, buf.Length);
                    for (int y = 1; y < n - 1; y++)
                    {
                        for (int x = 1; x < n - 1; x++)
                        {
                            int i = y * n + x;
                            if (((tmp[i] >> 24) & 255) < 24) { continue; }
                            int sr = 0, sg = 0, sb = 0, sa = 0, cnt = 0;
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    int j = (y + dy) * n + (x + dx);
                                    int al = (tmp[j] >> 24) & 255;
                                    if (al < 24) continue;
                                    sr += (tmp[j] >> 16) & 255; sg += (tmp[j] >> 8) & 255; sb += tmp[j] & 255;
                                    sa += al; cnt++;
                                }
                            }
                            if (cnt == 0) continue;
                            buf[i] = ((sa / cnt) << 24) | ((sr / cnt) << 16) | ((sg / cnt) << 8) | (sb / cnt);
                        }
                    }
                }

                // ---- 1) 平涂：k-means 压色 ----
                var samples = new List<int[]>();
                for (int i = 0; i < buf.Length; i += 2)
                {
                    int argb = buf[i];
                    if (((argb >> 24) & 255) < 24) continue;
                    samples.Add(new int[] { (argb >> 16) & 255, (argb >> 8) & 255, argb & 255 });
                }
                List<int[]> pal = MakePalette(samples, colors);
                Console.WriteLine("平涂调色板: " + pal.Count + " 色");
                for (int i = 0; i < buf.Length; i++)
                {
                    int argb = buf[i];
                    int al = (argb >> 24) & 255;
                    if (al < 24) continue;
                    int[] c = pal[NearestOf(pal, new int[] { (argb >> 16) & 255, (argb >> 8) & 255, argb & 255 })];
                    buf[i] = (al << 24) | (c[0] << 16) | (c[1] << 8) | c[2];
                }

                // ---- 2) 描边：亮度梯度 + 膨胀 ----
                if (outline > 0)
                {
                    var lum = new double[n * n];
                    for (int i = 0; i < buf.Length; i++)
                    {
                        int r = (buf[i] >> 16) & 255, gg = (buf[i] >> 8) & 255, b = buf[i] & 255;
                        lum[i] = 0.299 * r + 0.587 * gg + 0.114 * b;
                    }
                    var edge = new bool[n * n];
                    for (int y = 1; y < n - 1; y++)
                    {
                        for (int x = 1; x < n - 1; x++)
                        {
                            int i = y * n + x;
                            if (((buf[i] >> 24) & 255) < 24) continue;
                            double gx = -lum[i - n - 1] - 2 * lum[i - 1] - lum[i + n - 1]
                                        + lum[i - n + 1] + 2 * lum[i + 1] + lum[i + n + 1];
                            double gy = -lum[i - n - 1] - 2 * lum[i - n] - lum[i - n + 1]
                                        + lum[i + n - 1] + 2 * lum[i + n] + lum[i + n + 1];
                            if (Math.Abs(gx) + Math.Abs(gy) > 170) edge[i] = true;
                        }
                    }
                    // 清理孤立边缘点：只有邻居里也有边缘的才留下（否则满脸麻子）
                    var clean = new bool[n * n];
                    for (int y = 1; y < n - 1; y++)
                    {
                        for (int x = 1; x < n - 1; x++)
                        {
                            int i = y * n + x;
                            if (!edge[i]) continue;
                            int cnt = 0;
                            for (int dy = -1; dy <= 1; dy++)
                                for (int dx = -1; dx <= 1; dx++)
                                    if (dx != 0 || dy != 0) { if (edge[i + dy * n + dx]) cnt++; }
                            if (cnt >= 3) clean[i] = true;
                        }
                    }
                    edge = clean;

                    // 膨胀 outline 像素，让描边够粗
                    var thick = new bool[n * n];
                    for (int y = 0; y < n; y++)
                    {
                        for (int x = 0; x < n; x++)
                        {
                            if (!edge[y * n + x]) continue;
                            for (int dy = -outline; dy <= outline; dy++)
                            {
                                for (int dx = -outline; dx <= outline; dx++)
                                {
                                    int nx = x + dx, ny = y + dy;
                                    if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                                    thick[ny * n + nx] = true;
                                }
                            }
                        }
                    }
                    for (int i = 0; i < buf.Length; i++)
                    {
                        if (!thick[i]) continue;
                        int al = (buf[i] >> 24) & 255;
                        if (al < 24) continue;
                        buf[i] = (al << 24) | (26 << 16) | (22 << 8) | 32;   // 近黑描边（偏冷紫，和主题协调）
                    }
                    Console.WriteLine("描边: 粗细 " + outline + "px，阈值 170（已清理孤立点）");
                }

                // ---- 3) 去白底（可选）----
                if (keyWhite)
                {
                    for (int i = 0; i < buf.Length; i++)
                    {
                        int r = (buf[i] >> 16) & 255, gg = (buf[i] >> 8) & 255, b = buf[i] & 255;
                        if (r >= keyThreshold && gg >= keyThreshold && b >= keyThreshold) buf[i] = 0;
                    }
                    Console.WriteLine("去白底阈值: " + keyThreshold);
                }

                using (var work = new Bitmap(n, n, PixelFormat.Format32bppArgb))
                {
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                            work.SetPixel(x, y, Color.FromArgb(buf[y * n + x]));

                    if (size == n) work.Save(outPath, ImageFormat.Png);
                    else
                    {
                        using (var outp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                        using (var g = Graphics.FromImage(outp))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.Clear(Color.Transparent);
                            g.DrawImage(work, 0, 0, size, size);
                            outp.Save(outPath, ImageFormat.Png);
                        }
                    }
                }
            }
            Console.WriteLine("完成 -> " + outPath + "（线稿化，平涂 " + colors + " 色）");
            return 0;
        }

        /// <summary>调色板：先取最暗的一格当第一个种子，之后每次取"离已有种子最远"的一格，再跑 k-means</summary>
        static List<int[]> MakePalette(List<int[]> samples, int k)
        {
            var pal = new List<int[]>();
            if (samples.Count == 0) return pal;

            // 种子 1：最暗的一格（保证描线/睫毛的黑色在调色板里）
            int first = 0; double minLum = double.MaxValue;
            for (int i = 0; i < samples.Count; i++)
            {
                double lum = 0.299 * samples[i][0] + 0.587 * samples[i][1] + 0.114 * samples[i][2];
                if (lum < minLum) { minLum = lum; first = i; }
            }
            pal.Add(new int[] { samples[first][0], samples[first][1], samples[first][2] });

            // 种子 2..k：最远点采样
            while (pal.Count < k && pal.Count < samples.Count)
            {
                int pick = 0; double bestD = -1;
                for (int i = 0; i < samples.Count; i++)
                {
                    double d = double.MaxValue;
                    for (int j = 0; j < pal.Count; j++)
                    {
                        double dr = samples[i][0] - pal[j][0], dg = samples[i][1] - pal[j][1], db = samples[i][2] - pal[j][2];
                        double e = dr * dr + dg * dg + db * db;
                        if (e < d) d = e;
                    }
                    if (d > bestD) { bestD = d; pick = i; }
                }
                if (bestD <= 0) break;
                pal.Add(new int[] { samples[pick][0], samples[pick][1], samples[pick][2] });
            }

            // k-means 迭代
            var sum = new double[pal.Count][];
            var cnt = new int[pal.Count];
            for (int it = 0; it < 12; it++)
            {
                for (int i = 0; i < pal.Count; i++) { sum[i] = new double[3]; cnt[i] = 0; }
                foreach (int[] s in samples)
                {
                    int b = NearestOf(pal, s);
                    sum[b][0] += s[0]; sum[b][1] += s[1]; sum[b][2] += s[2]; cnt[b]++;
                }
                for (int i = 0; i < pal.Count; i++)
                {
                    if (cnt[i] == 0) continue;
                    pal[i][0] = (int)Math.Round(sum[i][0] / cnt[i]);
                    pal[i][1] = (int)Math.Round(sum[i][1] / cnt[i]);
                    pal[i][2] = (int)Math.Round(sum[i][2] / cnt[i]);
                }
            }
            return pal;
        }

        static int NearestOf(List<int[]> pal, int[] v)
        {
            int best = 0, bestD = int.MaxValue;
            for (int i = 0; i < pal.Count; i++)
            {
                int dr = v[0] - pal[i][0], dg = v[1] - pal[i][1], db = v[2] - pal[i][2];
                int d = dr * dr + dg * dg + db * db;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        static Bitmap Load(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(bytes))
            using (var img = Image.FromStream(ms))
            {
                var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp)) g.DrawImage(img, 0, 0, img.Width, img.Height);
                return bmp;
            }
        }

        static int Pixelize(string[] a)
        {
            if (a.Length < 6) { Usage(); return 2; }
            string src = a[1], outPath = a[2];
            int grid = int.Parse(a[3], CultureInfo.InvariantCulture);
            int colors = int.Parse(a[4], CultureInfo.InvariantCulture);
            int up = int.Parse(a[5], CultureInfo.InvariantCulture);
            bool keyWhite = a.Length > 6 && a[6].ToLowerInvariant().StartsWith("key");
            int keyThreshold = a.Length > 7 ? int.Parse(a[7], CultureInfo.InvariantCulture) : 242;

            using (Bitmap img = Load(src))
            {
                // ---------- 1) 块内平均降采样 ----------
                var cell = new int[grid * grid][];
                double cw = (double)img.Width / grid, chh = (double)img.Height / grid;
                for (int gy = 0; gy < grid; gy++)
                {
                    for (int gx = 0; gx < grid; gx++)
                    {
                        int x0 = (int)Math.Floor(gx * cw), x1 = (int)Math.Ceiling((gx + 1) * cw);
                        int y0 = (int)Math.Floor(gy * chh), y1 = (int)Math.Ceiling((gy + 1) * chh);
                        if (x1 > img.Width) x1 = img.Width;
                        if (y1 > img.Height) y1 = img.Height;
                        double sr = 0, sg = 0, sb = 0, sa = 0; int n = 0;
                        for (int y = y0; y < y1; y++)
                        {
                            for (int x = x0; x < x1; x++)
                            {
                                Color c = img.GetPixel(x, y);
                                double al = c.A / 255.0;
                                sr += c.R * al; sg += c.G * al; sb += c.B * al; sa += al; n++;
                            }
                        }
                        int[] v;
                        if (n == 0 || sa <= 0.0001) v = new int[] { 0, 0, 0, 0 };
                        else v = new int[]
                        {
                            (int)Math.Round(sr / sa), (int)Math.Round(sg / sa), (int)Math.Round(sb / sa),
                            (int)Math.Round((sa / n) * 255)
                        };
                        cell[gy * grid + gx] = v;
                    }
                }

                // ---------- 2) 调色板：最远点撒种 + k-means ----------
                // 不用"按出现频率取色"：那样选出来全是一堆相近的肤色，
                // 眼睛、睫毛、描线这些深色会被丢掉，画面就变成一团噪点。
                // 最远点撒种能保证深色/彩色都有人代表，再跑 k-means 收敛。
                if (colors > 0)
                {
                    var samples = new List<int[]>();
                    foreach (int[] v in cell) if (v[3] >= 8) samples.Add(v);

                    List<int[]> pal = MakePalette(samples, colors);
                    Console.WriteLine("调色板: " + pal.Count + " 色（最远点撒种 + k-means）");

                    foreach (int[] v in cell)
                    {
                        if (v[3] < 8) continue;
                        int[] c = pal[NearestOf(pal, v)];
                        v[0] = c[0]; v[1] = c[1]; v[2] = c[2];
                    }
                }

                // ---------- 3) 白底透明 ----------
                if (keyWhite)
                {
                    foreach (int[] v in cell)
                    {
                        if (v[3] >= 8 && v[0] >= keyThreshold && v[1] >= keyThreshold && v[2] >= keyThreshold) v[3] = 0;
                    }
                    Console.WriteLine("去白底阈值: " + keyThreshold);
                }

                // ---------- 4) 最近邻放大输出 ----------
                using (var outp = new Bitmap(up, up, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(outp))
                {
                    g.Clear(Color.Transparent);
                    double s = (double)up / grid;
                    using (var br = new SolidBrush(Color.White))
                    {
                        for (int gy = 0; gy < grid; gy++)
                        {
                            for (int gx = 0; gx < grid; gx++)
                            {
                                int[] v = cell[gy * grid + gx];
                                if (v[3] < 8) continue;
                                br.Color = Color.FromArgb(v[3], v[0], v[1], v[2]);
                                int px = (int)Math.Round(gx * s), py = (int)Math.Round(gy * s);
                                int pw = (int)Math.Round((gx + 1) * s) - px, ph = (int)Math.Round((gy + 1) * s) - py;
                                g.FillRectangle(br, px, py, pw, ph);
                            }
                        }
                    }
                    outp.Save(outPath, ImageFormat.Png);
                }
            }
            Console.WriteLine("完成 -> " + outPath + "  (" + grid + "x" + grid + " 像素格, " + up + "px 输出)");
            return 0;
        }
    }
}
