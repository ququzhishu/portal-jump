using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace PortalJump
{
    /// <summary>
    /// 统一的路径解析。
    ///
    /// 便携优先：配置文件、日志都放在程序所在目录，拷走整个文件夹就能带走配置。
    /// 但如果程序被装到 Program Files 这类没有写权限的目录，则自动改用
    /// %APPDATA%\PortalJump，保证在任何机器上都不会因为写不了文件而失效。
    ///
    /// 改名（ResSwitch → PortalJump）后还负责把旧目录里的 presets.ini 搬过来，
    /// 用户不需要重新配一遍预设。
    /// </summary>
    public static class AppPaths
    {
        static readonly object Sync = new object();
        static bool _resolved;
        static string _exeDir;
        static string _dataDir;

        public static string ExeDir
        {
            get
            {
                EnsureResolved();
                return _exeDir;
            }
        }

        public static string DataDir
        {
            get
            {
                EnsureResolved();
                return _dataDir;
            }
        }

        public static string IniPath
        {
            get { return Path.Combine(DataDir, "presets.ini"); }
        }

        public static string LogPath
        {
            get { return Path.Combine(DataDir, "portal-jump.log"); }
        }

        public static string CrashLogPath
        {
            get { return Path.Combine(DataDir, "portal-jump-crash.log"); }
        }

        public static string SelfTestPath
        {
            get { return Path.Combine(DataDir, "portal-jump-selftest.txt"); }
        }

        /// <summary>数据目录是否落在了用户配置目录（而不是程序目录）</summary>
        public static bool UsingRoamingData
        {
            get
            {
                EnsureResolved();
                return !string.Equals(_exeDir, _dataDir, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// 当前进程的 exe 路径。
        /// 刻意不依赖 WinForms（Application.ExecutablePath）：WPF 的 UI 进程也要用这里，
        /// 而 UI 进程不该因为一个路径解析就把 System.Windows.Forms 拉进内存。
        /// </summary>
        public static string ExePath
        {
            get
            {
                try
                {
                    string loc = Assembly.GetExecutingAssembly().Location;
                    if (!string.IsNullOrEmpty(loc)) return loc;
                }
                catch { }
                try
                {
                    ProcessModule m = Process.GetCurrentProcess().MainModule;
                    if (m != null && !string.IsNullOrEmpty(m.FileName)) return m.FileName;
                }
                catch { }
                return Environment.CurrentDirectory;
            }
        }

        static void EnsureResolved()
        {
            if (_resolved) return;
            lock (Sync)
            {
                if (_resolved) return;

                string dir;
                try { dir = Path.GetDirectoryName(ExePath); }
                catch { dir = null; }
                if (string.IsNullOrEmpty(dir)) dir = Environment.CurrentDirectory;

                _exeDir = dir;

                // 程序目录可写（含"已有 ini 且可覆盖"）就用它
                if (CanWrite(dir))
                {
                    _dataDir = dir;
                    MigrateFromSiblingDir(dir);
                }
                else
                {
                    string roaming = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.InternalId);
                    try { Directory.CreateDirectory(roaming); }
                    catch { }
                    _dataDir = roaming;
                    MigrateFromRoaming();
                }

                _resolved = true;
            }
        }

        /// <summary>真正试着写一个临时文件，避免"看起来能写其实不能写"（UAC 虚拟化、只读盘等）</summary>
        public static bool CanWrite(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
                string probe = Path.Combine(dir, ".portal-jump-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (FileStream fs = File.Create(probe)) { }
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// 便携模式：新目录（PortalJump）里还没有 presets.ini，但同级还留着旧目录（ResSwitch）时，
        /// 把旧配置复制过来。只复制、不删除，失败也不影响启动。
        /// </summary>
        static void MigrateFromSiblingDir(string dir)
        {
            try
            {
                if (File.Exists(Path.Combine(dir, "presets.ini"))) return;
                string parent = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(parent)) return;
                string legacy = Path.Combine(parent, AppInfo.LegacyInternalId);
                CopyIniIfAny(legacy, dir);
            }
            catch { }
        }

        /// <summary>漫游模式：%APPDATA%\ResSwitch\presets.ini → %APPDATA%\PortalJump\presets.ini</summary>
        static void MigrateFromRoaming()
        {
            try
            {
                if (File.Exists(Path.Combine(_dataDir, "presets.ini"))) return;
                string roamingRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                CopyIniIfAny(Path.Combine(roamingRoot, AppInfo.LegacyInternalId), _dataDir);
            }
            catch { }
        }

        static void CopyIniIfAny(string fromDir, string toDir)
        {
            try
            {
                if (string.IsNullOrEmpty(fromDir) || !Directory.Exists(fromDir)) return;
                string src = Path.Combine(fromDir, "presets.ini");
                string dst = Path.Combine(toDir, "presets.ini");
                if (File.Exists(src) && !File.Exists(dst)) File.Copy(src, dst, false);
            }
            catch { }
        }
    }
}
