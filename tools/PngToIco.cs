using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

/// <summary>
/// 把一张 PNG 打包成 Windows .ico（多尺寸）。
/// 用法: PngToIco.exe &lt;源.png&gt; &lt;输出.ico&gt; [尺寸列表，逗号分隔，默认 16,20,24,32,40,48,64,128,256]
///
/// Windows 图标需要多尺寸：任务栏/标题栏用 16-32，资源管理器大图标用 256。
/// 只塞一个尺寸会导致某些位置显示模糊或空白。
/// </summary>
static class PngToIco
{
    static int Main(string[] args)
    {
        var ow = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        Console.SetOut(ow);

        if (args.Length < 2)
        {
            Console.WriteLine("用法: PngToIco.exe <源.png> <输出.ico> [尺寸列表]");
            return 2;
        }
        string src = args[0];
        string dst = args[1];
        string sizeSpec = args.Length > 2 ? args[2] : "16,20,24,32,40,48,64,128,256";
        if (!File.Exists(src)) { Console.WriteLine("源文件不存在: " + src); return 1; }

        var sizes = new List<int>();
        foreach (string t in sizeSpec.Split(','))
        {
            int n;
            if (int.TryParse(t.Trim(), out n) && n >= 1 && n <= 256) sizes.Add(n);
        }
        if (sizes.Count == 0) { Console.WriteLine("尺寸列表无效"); return 2; }

        using (var original = new Bitmap(src))
        {
            var frames = new List<byte[]>();
            foreach (int n in sizes)
            {
                using (var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        g.Clear(Color.Transparent);
                        int side = Math.Min(original.Width, original.Height);
                        int ox = (original.Width - side) / 2;
                        int oy = (original.Height - side) / 2;
                        g.DrawImage(original,
                            new Rectangle(0, 0, n, n),
                            new Rectangle(ox, oy, side, side),
                            GraphicsUnit.Pixel);
                    }
                    using (var ms = new MemoryStream())
                    {
                        // PNG 压缩的帧（256 尺寸必须 PNG，其余 PNG 也能被 Vista+ 接受）
                        bmp.Save(ms, ImageFormat.Png);
                        frames.Add(ms.ToArray());
                    }
                }
            }

            using (var fs = new FileStream(dst, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((ushort)0);            // reserved
                w.Write((ushort)1);            // type = icon
                w.Write((ushort)sizes.Count);  // 帧数

                int offset = 6 + 16 * sizes.Count;
                for (int i = 0; i < sizes.Count; i++)
                {
                    int n = sizes[i];
                    w.Write((byte)(n >= 256 ? 0 : n));   // width（256 记作 0）
                    w.Write((byte)(n >= 256 ? 0 : n));   // height
                    w.Write((byte)0);                    // 调色板色数
                    w.Write((byte)0);                    // reserved
                    w.Write((ushort)1);                  // color planes
                    w.Write((ushort)32);                 // 位深
                    w.Write((uint)frames[i].Length);     // 数据长度
                    w.Write((uint)offset);               // 数据偏移
                    offset += frames[i].Length;
                }
                foreach (byte[] f in frames) w.Write(f);
            }
        }

        Console.WriteLine("已生成: " + dst + "  (" + sizes.Count + " 个尺寸: " + string.Join(",", sizes) + ")  "
            + new FileInfo(dst).Length + " B");
        return 0;
    }
}
