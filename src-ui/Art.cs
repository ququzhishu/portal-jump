using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PortalJump.UI
{
    /// <summary>
    /// 素材层（UI 进程侧）：读取程序目录里的图片素材。
    ///
    ///   zoe-bg.png    立绘/背景图（头图插画）
    ///   zoe-icon.png  Q 版头像（关于页圆头像 + exe 图标源图）
    ///
    /// 素材是可替换的：换图不用重新编译，把文件丢进程序目录即可。
    /// 读不到就返回 null，界面自动降级成纯渐变，不会崩、也不会出现占位方框。
    /// </summary>
    public static class Art
    {
        public static readonly string[] BackgroundNames = new string[]
        {
            "zoe-bg.png", "zoe.png", "background.png", "背景.png"
        };

        public static readonly string[] AvatarNames = new string[]
        {
            "zoe-icon.png", "佐伊头像.png", "avatar.png", "头像.png"
        };

        static bool _loaded;
        static ImageSource _bg, _avatar;
        static string _bgPath, _avatarPath;

        /// <summary>背景/立绘素材（可作为头图插画）</summary>
        public static ImageSource Background { get { EnsureLoaded(); return _bg; } }

        /// <summary>头像素材</summary>
        public static ImageSource Avatar { get { EnsureLoaded(); return _avatar; } }

        public static string BackgroundPath { get { EnsureLoaded(); return _bgPath; } }
        public static string AvatarPath { get { EnsureLoaded(); return _avatarPath; } }

        /// <summary>是否用上了用户素材</summary>
        public static bool UsingUserArt { get { return Background != null || Avatar != null; } }

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string dir = PortalJump.AppPaths.ExeDir;
                _avatar = LoadFirst(dir, AvatarNames, out _avatarPath);
                _bg = LoadFirst(dir, BackgroundNames, out _bgPath);

                if (_bg == null && _avatar == null)
                {
                    string data = PortalJump.AppPaths.DataDir;
                    if (!string.Equals(data, dir, StringComparison.OrdinalIgnoreCase))
                    {
                        _avatar = LoadFirst(data, AvatarNames, out _avatarPath);
                        _bg = LoadFirst(data, BackgroundNames, out _bgPath);
                    }
                }

                // 只放了背景图时，头像也可以用它（裁成圆头像用）
                if (_avatar == null && _bg != null) { _avatar = _bg; _avatarPath = _bgPath; }
            }
            catch { }
        }

        /// <summary>按文件名顺序找第一个存在的图片；用 OnLoad 一次读完，避免锁住文件（方便随时换图）</summary>
        static ImageSource LoadFirst(string dir, string[] names, out string path)
        {
            path = null;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            foreach (string n in names)
            {
                try
                {
                    string p = Path.Combine(dir, n);
                    if (!File.Exists(p)) continue;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bmp.UriSource = new Uri(p, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                    path = p;
                    return bmp;
                }
                catch { }
            }
            return null;
        }

        /// <summary>生成圆形头像画刷（ImageBrush + 居中裁切）；没有素材返回 null</summary>
        public static ImageBrush AvatarBrush()
        {
            ImageSource src = Avatar;
            if (src == null) return null;
            var brush = new ImageBrush(src);
            brush.Stretch = Stretch.UniformToFill;
            brush.Freeze();
            return brush;
        }
    }
}
