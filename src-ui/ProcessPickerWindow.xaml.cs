using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PortalJump;   // 核心：AppInfo / Switcher

namespace PortalJump.UI
{
    /// <summary>
    /// 程序检索对话框（WPF 版，第 3 轮迁移），等价于原来的 WinForms 版 ProcessPickerForm。
    /// 勾选正在运行的程序，或直接浏览任意 exe；确定后返回一组进程名（不含 .exe）。
    /// 和 WinForms 版对齐的点：枚举 Process.GetProcesses()、同名进程合并成一行、
    /// 按进程名 OrdinalIgnoreCase 排序、空标题/空路径的填充规则、已绑定但没运行的也列出来、
    /// 双击切换勾选、状态栏文案。
    /// 差别只在观感：卡片/表头/输入框都是真半透明，底纹从控件里透出来。
    /// </summary>
    public partial class ProcessPickerWindow : Window
    {
        // ---------- 列表的一行 ----------
        public class ProcRow : INotifyPropertyChanged
        {
            public ProcRow(string name, string title, string file)
            {
                Name = name; _title = title; _file = file;
            }

            string _title, _file;
            bool _checked;

            /// <summary>进程名（不含 .exe），也就是最终交回去的值</summary>
            public string Name { get; private set; }

            public string Title
            {
                get { return _title; }
                set { if (_title != value) { _title = value; Raise("Title"); } }
            }

            public string File
            {
                get { return _file; }
                set { if (_file != value) { _file = value; Raise("File"); } }
            }

            public bool IsChecked
            {
                get { return _checked; }
                set { if (_checked != value) { _checked = value; Raise("IsChecked"); } }
            }

            void Raise(string prop)
            {
                PropertyChangedEventHandler h = PropertyChanged;
                if (h != null) h(this, new PropertyChangedEventArgs(prop));
            }
            public event PropertyChangedEventHandler PropertyChanged;
        }

        readonly ObservableCollection<ProcRow> _rows = new ObservableCollection<ProcRow>();
        readonly string[] _preselected;
        string[] _result = new string[0];
        bool _firstShown;

        /// <summary>用户确定后选中的进程名</summary>
        public string[] SelectedProcessNames
        {
            get { return _result; }
        }

        public ProcessPickerWindow(string[] preselected)
        {
            InitializeComponent();
            _preselected = preselected == null ? new string[0] : preselected;

            // 文案对齐 WinForms 版：Text = "选择要关联的程序 — " + AppInfo.Name
            Title = "选择要关联的程序 — " + AppInfo.Name;

            ProcList.ItemsSource = _rows;

            // 对齐 WinForms 的 OnShown：首次枚举进程时按“打开对话框时的初始勾选”预选。
            // Loaded 理论上会随可视化树重挂而再触发，所以加一道闸，免得把用户改过的勾选冲掉。
            Loaded += delegate
            {
                if (_firstShown) return;
                _firstShown = true;
                Reload(true);
            };

            // WinForms 版 MaximizeBox=false；WPF 不重写窗体边框就没法去掉最大化按钮，
            // 用“一最大化就还原”来达到同样的效果（最小化不受影响）。
            StateChanged += delegate
            {
                if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
            };
        }

        // ==================================================================
        // 列表事件
        // ==================================================================

        void OnRowChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsChecked") UpdateStatus();
        }

        void OnFilterChanged(object sender, TextChangedEventArgs e)
        {
            // Reload 不会回写筛选框/复选框，所以不存在重入（原来是 TextChanged -> Reload(false)）
            Reload(false);
        }

        void OnRefresh(object sender, RoutedEventArgs e) { Reload(false); }

        /// <summary>
        /// 勾选/取消"只看有窗口的程序"立刻重筛列表。
        /// 说明：WinForms 原版这个复选框没挂事件，必须点"刷新列表"才生效 ——
        /// 这里刻意改成即时生效，因为"勾了没反应"用起来像 bug（这是本对话框唯一有意偏离原版的地方）。
        /// </summary>
        void OnOnlyWindowedChanged(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;   // 构造/初始化阶段不重筛
            Reload(false);
        }

        void OnClearAll(object sender, RoutedEventArgs e)
        {
            foreach (ProcRow r in _rows) r.IsChecked = false;
            UpdateStatus();
        }

        void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 对齐 WinForms 版：双击整行 = 切换勾选
            ProcRow r = ProcList.SelectedItem as ProcRow;
            if (r != null) r.IsChecked = !r.IsChecked;
        }

        void OnOk(object sender, RoutedEventArgs e)
        {
            Collect();
            DialogResult = true;   // 设了 DialogResult 就会关窗，ShowDialog() 返回 true
        }

        // ==================================================================
        // 浏览 exe
        // ==================================================================

        void OnBrowse(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog();
            ofd.Title = "选择要关联的程序";
            ofd.Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";
            ofd.CheckFileExists = true;
            if (ofd.ShowDialog(this) != true) return;

            // 取文件名去掉扩展名：和 WinForms 版一致（进程名不带 .exe）
            string name = Path.GetFileNameWithoutExtension(ofd.FileName);
            if (name == null || name.Length == 0) return;

            ProcRow found = null;
            foreach (ProcRow r in _rows)
            {
                if (string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) { found = r; break; }
            }
            if (found == null)
            {
                // 新加的行走“未在运行”文案（注意和 Reload 里补的行“当前未运行”不是同一个词，两边都沿用原文案）
                found = new ProcRow(name, "（未在运行）", ofd.FileName);
                found.PropertyChanged += OnRowChanged;
                _rows.Add(found);
            }
            found.IsChecked = true;
            ProcList.SelectedItem = found;
            ProcList.ScrollIntoView(found);
            UpdateStatus();
        }

        // ==================================================================
        // 结果 / 状态
        // ==================================================================

        void Collect()
        {
            var names = new List<string>();
            foreach (ProcRow r in _rows)
            {
                if (!r.IsChecked) continue;
                if (!string.IsNullOrEmpty(r.Name)) names.Add(r.Name);
            }
            _result = names.ToArray();
        }

        void UpdateStatus()
        {
            int total = _rows.Count;
            int chose = 0;
            foreach (ProcRow r in _rows) if (r.IsChecked) chose++;
            StatusText.Text = "共 " + total + " 个程序，已勾选 " + chose + " 个";
        }

        // ==================================================================
        // 重新枚举进程
        // ==================================================================

        /// <summary>重新枚举进程；usePreselect=true 时用打开对话框时的初始勾选</summary>
        void Reload(bool usePreselect)
        {
            // 先把"当前勾了谁"记成名字：行对象整体重建，勾选只能按名字恢复
            var checkedNames = new List<string>();
            if (usePreselect)
            {
                checkedNames.AddRange(_preselected);
            }
            else
            {
                foreach (ProcRow r in _rows)
                {
                    if (r.IsChecked && !string.IsNullOrEmpty(r.Name)) checkedNames.Add(r.Name);
                }
            }

            string filter = (FilterBox.Text == null ? "" : FilterBox.Text).Trim();
            bool onlyWindowed = OnlyWindowedBox.IsChecked == true;

            Cursor previous = Cursor;
            Cursor = Cursors.Wait;
            try
            {
                var rows = new List<ProcRow>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                Process[] procs = null;
                try { procs = Process.GetProcesses(); }
                catch { procs = null; }

                if (procs != null)
                {
                    foreach (Process p in procs)
                    {
                        try
                        {
                            string name = "";
                            try { name = p.ProcessName; }
                            catch { continue; }
                            if (name.Length == 0) continue;

                            string title = "";
                            try { title = p.MainWindowTitle; }
                            catch { }

                            if (onlyWindowed && title.Length == 0) continue;

                            if (filter.Length > 0 &&
                                name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                                title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                            string file = "";
                            try { file = p.MainModule.FileName; }
                            catch { }

                            if (seen.Contains(name))
                            {
                                // 同名多实例只留一行，空着的标题/路径被后面的实例补上（原版就是这个规则）
                                foreach (ProcRow r in rows)
                                {
                                    if (string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (r.Title.Length == 0 && title.Length > 0) r.Title = title;
                                        if (r.File.Length == 0 && file.Length > 0) r.File = file;
                                        break;
                                    }
                                }
                                continue;
                            }
                            seen.Add(name);
                            rows.Add(new ProcRow(name, title, file));
                        }
                        catch { }
                        finally
                        {
                            try { p.Dispose(); } catch { }
                        }
                    }
                }

                // 已绑定但当前没运行的也列出来，避免重选时把绑定丢掉
                foreach (string n in checkedNames)
                {
                    if (!seen.Contains(n))
                    {
                        seen.Add(n);
                        rows.Add(new ProcRow(n, "（当前未运行）", ""));
                    }
                }

                rows.Sort(delegate(ProcRow a, ProcRow b)
                {
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });

                var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string n in checkedNames) wanted.Add(n);

                _rows.Clear();
                foreach (ProcRow r in rows)
                {
                    if (wanted.Contains(r.Name)) r.IsChecked = true;   // 先设勾选再订阅，避免重建过程中反复刷状态
                    r.PropertyChanged += OnRowChanged;
                    _rows.Add(r);
                }
            }
            finally
            {
                Cursor = previous;
            }
            UpdateStatus();
        }
    }
}
