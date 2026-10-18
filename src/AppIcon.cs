using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PortalJump
{
    /// <summary>
    /// 程序图标（托盘 / 标题栏 / 任务栏）。按优先级取：
    ///   1) 程序目录里的 zoe-icon.png —— Q 版佐伊头像，**运行期可替换，不必重编译**；
    ///   2) exe 自带图标（build.ps1 用 /win32icon 写入的多尺寸 .ico）；
    ///   3) 内置绘制的传送门标志（兜底，保证任何情况下都有图标）。
    /// </summary>
    public static class AppIcon
    {
        static readonly string[] Names = new string[]
        {
            "zoe-icon.png", "佐伊头像.png", "avatar.png", "头像.png"
        };

        static Icon _cached;
        static int _cachedSize;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>取指定尺寸的程序图标（同尺寸会复用实例）</summary>
        public static Icon Get(int size)
        {
            if (_cached != null && _cachedSize == size) return _cached;

            Icon ic = null;
            try { ic = FromPng(size); } catch { }
            if (ic == null) { try { ic = FromExe(); } catch { } }
            if (ic == null) { try { ic = UiKit.MakeLogoIcon(size); } catch { } }

            _cached = ic;
            _cachedSize = size;
            return ic;
        }

        /// <summary>素材目录里找头像图，按需要的尺寸重绘（保证小尺寸清晰）</summary>
        static Icon FromPng(int size)
        {
            string dir = AppPaths.ExeDir;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;

            foreach (string n in Names)
            {
                string p = Path.Combine(dir, n);
                if (!File.Exists(p)) continue;
                using (var raw = new Bitmap(p))
                using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.Clear(Color.Transparent);
                        g.DrawImage(raw, new Rectangle(0, 0, size, size));
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
            return null;
        }

        /// <summary>exe 自身携带的图标（/win32icon 写入）</summary>
        static Icon FromExe()
        {
            return Icon.ExtractAssociatedIcon(AppPaths.ExePath);
        }
    }
}
