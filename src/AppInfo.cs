using System;

namespace PortalJump
{
    /// <summary>
    /// 程序身份信息（单一来源）。
    ///
    /// 界面标题、托盘、关于页、帮助文本全部从这里取，改名 / 改说明只需要动这一个文件；
    /// build.ps1 也从这里读版本号（正则锚定行首），所以 Version 必须写成
    /// 下面 Version 字段那样的常量声明。
    /// </summary>
    public static class AppInfo
    {
        /// <summary>
        /// 界面画布底色（淡中性灰白）。WinForms 的 UiKit 与 WPF 的 Backdrop 都用它，
        /// 放在这里是为了让底纹生成逻辑不依赖任何一个 UI 框架（WPF 进程里不再链接 UiKit）。
        /// 原值 #FBF8FE 带明显紫调；改淡色系后取中性灰白，与淡紫强调色搭配更清爽。
        /// </summary>
        public static readonly System.Drawing.Color CanvasColor =
            System.Drawing.Color.FromArgb(0xF7, 0xF6, 0xF9);

        /// <summary>版本号（build.ps1 用正则从这里提取，用来命名分发包）</summary>
        public const string Version = "0.21";

        /// <summary>
        /// 中文名。**不再出现在任何界面/标题里**（用户要求只保留英文名），
        /// 仅保留常量给日志与旧配置兼容使用。
        /// </summary>
        public const string Name = "折返跃迁";

        /// <summary>英文名（现在是对外的唯一名称，界面标题、托盘、窗口标题都用它）</summary>
        public const string EnglishName = "Portal Jump";

        /// <summary>程序说明（关于页 / 帮助）—— 只讲功能，不讲使用场景</summary>
        public const string Description = "按程序自动切换显示分辨率与缩放模式（NVIDIA / AMD 通用）";

        /// <summary>一句话功能概括（托盘提示用）</summary>
        public const string Tagline = "托盘常驻的显示模式自动切换工具";

        // ---- 个人标签 ----
        /// <summary>作者 / 社交媒体名字</summary>
        public const string Author = "区区之数";

        /// <summary>作者的角色文案</summary>
        public const string AuthorRole = "作者";

        // ---- 社交主页（固定内容：关于页只做展示，界面上不可编辑） ----
        /// <summary>社交平台名（与 SocialUrls 一一对应）</summary>
        public static readonly string[] SocialNames = new string[]
        {
            "Bilibili"
        };

        /// <summary>社交主页地址（固定值，不要在界面里改）</summary>
        public static readonly string[] SocialUrls = new string[]
        {
            "https://space.bilibili.com/21100323"
        };

        /// <summary>主页链接的展示文案（去掉协议头，界面上只读显示）</summary>
        public static string SocialDisplay
        {
            get
            {
                if (SocialUrls.Length == 0) return "";
                string u = SocialUrls[0];
                if (u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) u = u.Substring(8);
                else if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) u = u.Substring(7);
                return u;
            }
        }

        // ---- 内部标识 ----
        /// <summary>程序文件基名（不含扩展名）</summary>
        public const string ExeName = "PortalJump.exe";

        /// <summary>单实例互斥体 / 注册表启动项使用的标识</summary>
        public const string InternalId = "PortalJump";

        /// <summary>旧版本的名字，用于迁移自启项与配置文件</summary>
        public const string LegacyInternalId = "ResSwitch";

        /// <summary>
        /// 对外标题：**只有英文名 Portal Jump**，不含中文。
        /// 窗口标题、托盘提示、对话框标题、关于页全部走这里，
        /// 所以改这一处就能统一去掉中文名。
        /// </summary>
        public static string Title
        {
            get { return EnglishName; }
        }

        /// <summary>「Portal Jump  v0.11」</summary>
        public static string TitleWithVersion
        {
            get { return EnglishName + "  v" + Version; }
        }
    }
}
