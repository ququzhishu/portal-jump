using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PortalJump
{
    /// <summary>
    /// 程序检索对话框：勾选正在运行的程序，或直接浏览任意 exe。
    /// 返回一组进程名（不含 .exe），用于预设的自动切换绑定。
    /// </summary>
    public class ProcessPickerForm : Form
    {
        class Row
        {
            public string Name;
            public string Title;
            public string File;

            public Row(string name, string title, string file)
            {
                Name = name; Title = title; File = file;
            }
        }

        readonly TextBox _filter = new TextBox();
        readonly CheckBox _onlyWindowed = UiKit.CheckBoxFlat("只看有窗口的程序");
        readonly ListView _list = new ListView();
        readonly Label _status = new Label();
        readonly ImageList _rowHeight = new ImageList();

        readonly string[] _preselected;
        string[] _result = new string[0];

        /// <summary>用户确定后选中的进程名</summary>
        public string[] SelectedProcessNames
        {
            get { return _result; }
        }

        public ProcessPickerForm(string[] preselected)
        {
            _preselected = preselected == null ? new string[0] : preselected;

            Text = "选择要关联的程序 — " + AppInfo.Name;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            Font = UiKit.FontBody;
            BackColor = UiKit.Canvas;
            ClientSize = UiKit.Sz(880, 620);
            MinimumSize = UiKit.Sz(700, 480);
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;

            // ---------- 顶栏（品牌渐变） ----------
            var header = new HeroPanel();
            header.Dock = DockStyle.Top;
            header.Height = UiKit.S(88);

            var title = new Label();
            title.Text = "选择要关联的程序";
            title.Font = UiKit.FontOf(13f, FontStyle.Bold);
            title.ForeColor = Color.White;
            title.AutoSize = true;
            title.BackColor = Color.Transparent;
            title.Location = UiKit.Pt(26, 24);

            var subtitle = new Label();
            subtitle.Text = "勾选后点“确定”。没在运行的程序，可以用下面的“浏览 exe 文件…”。";
            subtitle.Font = UiKit.FontHint;
            subtitle.ForeColor = Color.FromArgb(226, 255, 255, 255);
            subtitle.AutoSize = true;
            subtitle.BackColor = Color.Transparent;
            subtitle.Location = UiKit.Pt(26, 51);

            header.Controls.Add(title);
            header.Controls.Add(subtitle);

            // ---------- 工具条 ----------
            // 用 TableLayoutPanel：筛选标签 / 输入框 / 复选框 / 刷新按钮 全部在单元格里垂直居中，
            // 水平方向按列对齐，不再靠手写坐标（那样每行的基线都会差几个像素）。
            var toolbar = new Panel();
            toolbar.Dock = DockStyle.Top;
            toolbar.Height = UiKit.S(60);
            toolbar.BackColor = UiKit.Canvas;

            var toolbarGrid = new TableLayoutPanel();
            toolbarGrid.Dock = DockStyle.Fill;
            toolbarGrid.ColumnCount = 4;
            toolbarGrid.RowCount = 1;
            toolbarGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbarGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiKit.S(280)));
            toolbarGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            toolbarGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiKit.S(100)));
            toolbarGrid.Padding = UiKit.Pad(26, 0, 24, 0);
            toolbarGrid.BackColor = UiKit.Canvas;

            var lblFilter = new Label();
            lblFilter.Text = "筛选";
            lblFilter.Font = UiKit.FontBody;
            lblFilter.ForeColor = UiKit.TextPrimary;
            lblFilter.AutoSize = true;
            lblFilter.BackColor = Color.Transparent;
            lblFilter.Anchor = AnchorStyles.Left;
            lblFilter.Margin = new Padding(0, 0, UiKit.S(10), 0);

            UiKit.StyleTextBox(_filter);
            _filter.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _filter.Margin = new Padding(0, 0, UiKit.S(20), 0);
            _filter.TextChanged += delegate { Reload(false); };

            _onlyWindowed.Anchor = AnchorStyles.Left;
            _onlyWindowed.Margin = new Padding(0);

            var btnRefresh = new FlatButton("刷新列表", FlatButton.Kind.Secondary);
            btnRefresh.Size = UiKit.Sz(100, 34);
            btnRefresh.Anchor = AnchorStyles.Right;
            btnRefresh.Margin = new Padding(0);
            btnRefresh.Surface = UiKit.Canvas;
            btnRefresh.Click += delegate { Reload(false); };

            toolbarGrid.Controls.Add(lblFilter, 0, 0);
            toolbarGrid.Controls.Add(_filter, 1, 0);
            toolbarGrid.Controls.Add(_onlyWindowed, 2, 0);
            toolbarGrid.Controls.Add(btnRefresh, 3, 0);
            toolbar.Controls.Add(toolbarGrid);

            // ---------- 底栏 ----------
            var footer = new BarPanel(true);
            footer.Dock = DockStyle.Bottom;
            footer.Height = UiKit.S(76);

            _status.AutoSize = true;
            _status.Font = UiKit.FontHint;
            _status.ForeColor = UiKit.TextSecondary;
            _status.Location = UiKit.Pt(24, 32);

            var btnBrowse = new FlatButton("浏览 exe 文件…", FlatButton.Kind.Secondary);
            btnBrowse.Size = UiKit.Sz(140, 38);
            btnBrowse.Margin = new Padding(UiKit.S(8), 0, 0, 0);
            btnBrowse.Click += delegate { BrowseExe(); };

            var btnNone = new FlatButton("全不选", FlatButton.Kind.Secondary);
            btnNone.Size = UiKit.Sz(88, 38);
            btnNone.Margin = new Padding(UiKit.S(8), 0, 0, 0);
            btnNone.Click += delegate
            {
                foreach (ListViewItem it in _list.Items) it.Checked = false;
                UpdateStatus();
            };

            var btnCancel = new FlatButton("取消", FlatButton.Kind.Secondary);
            btnCancel.Size = UiKit.Sz(88, 38);
            btnCancel.Margin = new Padding(UiKit.S(8), 0, 0, 0);
            btnCancel.DialogResult = DialogResult.Cancel;

            var btnOk = new FlatButton("确定", FlatButton.Kind.Primary);
            btnOk.Size = UiKit.Sz(88, 38);
            btnOk.Margin = new Padding(UiKit.S(8), 0, 0, 0);
            btnOk.DialogResult = DialogResult.OK;
            btnOk.Click += delegate { Collect(); };

            // 从右往左排：确定 / 取消 / 全不选 / 浏览 exe 文件…
            var footerButtons = new FlowLayoutPanel();
            footerButtons.FlowDirection = FlowDirection.RightToLeft;
            footerButtons.WrapContents = false;
            footerButtons.AutoSize = true;
            footerButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            footerButtons.Dock = DockStyle.Right;
            footerButtons.Padding = UiKit.Pad(0, 19, 24, 0);
            footerButtons.BackColor = UiKit.Card;
            footerButtons.Controls.Add(btnOk);
            footerButtons.Controls.Add(btnCancel);
            footerButtons.Controls.Add(btnNone);
            footerButtons.Controls.Add(btnBrowse);

            footer.Controls.Add(_status);
            footer.Controls.Add(footerButtons);

            // ---------- 列表 ----------
            var listHost = new Panel();
            listHost.Dock = DockStyle.Fill;
            listHost.Padding = UiKit.Pad(20, 0, 20, 12);
            listHost.BackColor = UiKit.Canvas;

            _rowHeight.ImageSize = new Size(1, UiKit.S(28));   // 用空 ImageList 撑高行距
            _rowHeight.ColorDepth = ColorDepth.Depth32Bit;

            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.CheckBoxes = true;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.MultiSelect = true;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.BackColor = UiKit.Card;
            _list.ForeColor = UiKit.TextPrimary;
            _list.Font = UiKit.FontBody;
            _list.SmallImageList = _rowHeight;
            _list.Columns.Add("进程名", UiKit.S(210));
            _list.Columns.Add("窗口标题", UiKit.S(300));
            _list.Columns.Add("文件路径", UiKit.S(300));
            _list.ItemChecked += delegate { UpdateStatus(); };
            _list.DoubleClick += delegate
            {
                if (_list.SelectedItems.Count > 0)
                {
                    ListViewItem it = _list.SelectedItems[0];
                    it.Checked = !it.Checked;
                }
            };

            listHost.Controls.Add(_list);

            Controls.Add(listHost);
            Controls.Add(toolbar);
            Controls.Add(header);
            Controls.Add(footer);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Reload(true);
        }

        void BrowseExe()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "选择要关联的程序";
                ofd.Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                ofd.CheckFileExists = true;
                if (ofd.ShowDialog(this) != DialogResult.OK) return;

                string name = Path.GetFileNameWithoutExtension(ofd.FileName);
                if (name.Length == 0) return;

                ListViewItem found = null;
                foreach (ListViewItem it in _list.Items)
                {
                    if (string.Equals((string)it.Tag, name, StringComparison.OrdinalIgnoreCase))
                    {
                        found = it;
                        break;
                    }
                }
                if (found == null)
                {
                    found = new ListViewItem(name);
                    found.SubItems.Add("（未在运行）");
                    found.SubItems.Add(ofd.FileName);
                    found.Tag = name;
                    _list.Items.Add(found);
                }
                found.Checked = true;
                found.Selected = true;
                found.EnsureVisible();
                UpdateStatus();
            }
        }

        void Collect()
        {
            var names = new List<string>();
            foreach (ListViewItem it in _list.CheckedItems)
            {
                string n = it.Tag as string;
                if (!string.IsNullOrEmpty(n)) names.Add(n);
            }
            _result = names.ToArray();
        }

        void UpdateStatus()
        {
            int total = _list.Items.Count;
            int chose = 0;
            foreach (ListViewItem it in _list.Items) if (it.Checked) chose++;
            _status.Text = "共 " + total + " 个程序，已勾选 " + chose + " 个";
        }

        /// <summary>重新枚举进程；usePreselect=true 时用打开对话框时的初始勾选</summary>
        void Reload(bool usePreselect)
        {
            var checkedNames = new List<string>();
            if (usePreselect)
            {
                checkedNames.AddRange(_preselected);
            }
            else
            {
                foreach (ListViewItem it in _list.CheckedItems)
                {
                    string n = it.Tag as string;
                    if (!string.IsNullOrEmpty(n)) checkedNames.Add(n);
                }
            }

            string filter = _filter.Text.Trim();
            bool onlyWindowed = _onlyWindowed.Checked;

            Cursor previous = Cursor;
            Cursor = Cursors.WaitCursor;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();

                var rows = new List<Row>();
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
                                foreach (Row r in rows)
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
                            rows.Add(new Row(name, title, file));
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
                        rows.Add(new Row(n, "（当前未运行）", ""));
                    }
                }

                rows.Sort(delegate(Row a, Row b)
                {
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });

                var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string n in checkedNames) wanted.Add(n);

                foreach (Row r in rows)
                {
                    var it = new ListViewItem(r.Name);
                    it.SubItems.Add(r.Title);
                    it.SubItems.Add(r.File);
                    it.Tag = r.Name;
                    if (wanted.Contains(r.Name)) it.Checked = true;
                    _list.Items.Add(it);
                }
            }
            finally
            {
                _list.EndUpdate();
                Cursor = previous;
            }
            UpdateStatus();
        }
    }
}
