using System;
using System.IO;
using Microsoft.Win32;

namespace PortalJump
{
    /// <summary>
    /// 开机自启：写入当前用户的 Run 项（不需要管理员权限），命令固定为 "程序路径" tray。
    ///
    /// 改名（ResSwitch → PortalJump）后，启动时会把旧名字的启动项迁移到新名字上，
    /// 否则用户会发现"自启突然失效"（旧项指向已经不存在的旧 exe）。
    /// </summary>
    public static class AutoStart
    {
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = AppInfo.InternalId;
        const string LegacyValueName = AppInfo.LegacyInternalId;

        /// <summary>自启时要执行的命令行（exe 路径取自 AppPaths，托盘与 UI 进程通用）</summary>
        public static string Command
        {
            get { return "\"" + AppPaths.ExePath + "\" tray"; }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    return k != null && k.GetValue(ValueName) != null;
                }
            }
            catch { return false; }
        }

        /// <summary>已登记的命令行；未登记返回空串</summary>
        public static string CurrentCommand()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    if (k == null) return "";
                    object v = k.GetValue(ValueName);
                    return v == null ? "" : v.ToString();
                }
            }
            catch { return ""; }
        }

        /// <summary>设置自启；失败时抛出异常，由调用方提示用户</summary>
        public static void Set(bool enable)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (k == null) throw new InvalidOperationException("无法打开注册表启动项（HKCU\\...\\Run）");
                if (enable)
                {
                    k.SetValue(ValueName, Command);
                }
                else
                {
                    k.DeleteValue(ValueName, false);
                    k.DeleteValue(LegacyValueName, false);
                }
            }
        }

        /// <summary>
        /// 把旧版名字的启动项迁移到当前名字（只在旧项存在、且新项还没写时迁移）。
        /// 返回一句人话说明，没有需要迁移的事情时返回空串。
        ///
        /// ⚠️ 从临时目录启动时（回归测试的沙盒副本、解压后先试跑）**绝不迁移**：
        /// 那会把用户真实的自启项改写成指向临时 exe 的路径，沙盒一删自启就废了。
        /// </summary>
        public static string MigrateLegacy()
        {
            if (IsRunningFromTemp()) return "";
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (k == null) return "";
                    object legacy = k.GetValue(LegacyValueName);
                    if (legacy == null) return "";

                    if (k.GetValue(ValueName) != null)
                    {
                        // 新项已存在：只清掉旧项，避免两处同时自启
                        k.DeleteValue(LegacyValueName, false);
                        return "已清理旧版开机自启项（" + LegacyValueName + "）";
                    }

                    k.SetValue(ValueName, Command);
                    k.DeleteValue(LegacyValueName, false);
                    return "开机自启项已从 " + LegacyValueName + " 迁移到 " + ValueName;
                }
            }
            catch (Exception e)
            {
                return "迁移旧版开机自启项失败：" + e.Message;
            }
        }

        /// <summary>当前 exe 是不是跑在临时目录里（沙盒副本）—— 是的话不要碰用户的注册表配置</summary>
        static bool IsRunningFromTemp()
        {
            try
            {
                string exe = AppPaths.ExePath;
                if (string.IsNullOrEmpty(exe)) return true;
                string temp = Path.GetTempPath();
                if (string.IsNullOrEmpty(temp)) return true;
                return exe.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }
    }
}
