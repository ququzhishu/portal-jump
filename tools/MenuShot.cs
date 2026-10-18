using System;
using System.Drawing;
using System.Windows.Forms;

namespace PortalJump.Tools
{
    /// <summary>
    /// 开发用小工具：把托盘右键菜单在 ShowImageMargin 开/关两种情况各渲染一次并拼成对比图。
    /// 用途：证实"关掉左边那一列会把可勾选项的勾选标志裁掉"（用户实际反馈过看不到勾）。
    ///
    ///   MenuShot &lt;输出.png&gt;
    /// </summary>
    static class MenuShot
    {
        [STAThread]
        static int Main(string[] args)
        {
            string outPath = args.Length > 0 ? args[0] : "menu-shot.png";
            Application.EnableVisualStyles();

            Bitmap a = Grab(false);
            Bitmap b = Grab(true);

            int pad = 16, titleH = 34;
            int w = pad + a.Width + pad + b.Width + pad;
            int h = titleH + Math.Max(a.Height, b.Height) + pad;
            using (var sheet = new Bitmap(w, h))
            using (var g = Graphics.FromImage(sheet))
            using (var font = new Font("Microsoft YaHei UI", 10f))
            {
                g.Clear(Color.FromArgb(251, 248, 254));
                g.DrawString("左：ShowImageMargin = false（勾选标志被裁掉）    右：true（能看到勾）", font, Brushes.Black, pad, 8);
                g.DrawImage(a, pad, titleH);
                g.DrawImage(b, pad + a.Width + pad, titleH);
                sheet.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            }
            a.Dispose(); b.Dispose();
            Console.WriteLine("已保存: " + outPath);
            return 0;
        }

        static Bitmap Grab(bool showImageMargin)
        {
            var menu = new ContextMenuStrip();
            menu.Font = new Font("Microsoft YaHei UI", 9f);
            menu.ShowImageMargin = showImageMargin;

            var header = new ToolStripMenuItem("打开设置界面…");
            header.Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add(header);

            var auto = new ToolStripMenuItem("自动跟随程序切换");
            auto.Checked = true;            // 开启状态：应该看到勾
            auto.CheckOnClick = true;
            menu.Items.Add(auto);

            var startup = new ToolStripMenuItem("开机自动启动");
            startup.Checked = false;        // 关闭状态：不该有勾
            menu.Items.Add(startup);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("办公   (3840x2160 · 全屏拉伸（GPU 缩放）)  ★"));
            menu.Items.Add(new ToolStripMenuItem("关于 折返跃迁 Portal Jump v0.10"));
            menu.Items.Add(new ToolStripMenuItem("退出"));

            // 先量出合适尺寸，再显示到屏幕外，最后从屏幕抓真实渲染结果
            Size sz = menu.GetPreferredSize(Size.Empty);
            menu.Size = sz;
            menu.Show(new Point(-4000, -4000));      // 屏幕外，用户看不到
            Application.DoEvents();
            System.Threading.Thread.Sleep(350);
            Application.DoEvents();

            var rect = new Rectangle(menu.Left, menu.Top, menu.Width, menu.Height);
            var bmp = new Bitmap(Math.Max(1, rect.Width), Math.Max(1, rect.Height));
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(rect.Width, rect.Height));
            }
            menu.Close();
            menu.Dispose();
            return bmp;
        }
    }
}
