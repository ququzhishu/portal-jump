using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PortalJump
{
    /// <summary>
    /// 图形化设置界面（折返跃迁主题 + 高 DPI 适配）。
    ///
    /// 结构：顶部品牌渐变横幅 → 状态条（显卡 / 当前模式 / 关于 / 重新检测）→ 两张浮动卡片
    ///       （预设列表 + 预设详情）→ 底栏（自动切换开关 + 保存/应用按钮）。
    ///
    /// 布局单位全部是 96 DPI 下的逻辑像素，经 UiKit.S() 按实际 DPI 放大，
    /// 所以在 200% 缩放的 4K 屏上不会出现字体压住控件、文字被裁掉的问题。
    /// </summary>
    public class SettingsForm : Form
    {
        readonly Config _config;
        bool _loading;
        bool _dirty;

        // 预设列表
        PresetListBox _presetList;
        FlatButton _btnAdd, _btnCopy, _btnDelete, _btnUp, _btnDown;
        SplitContainer _split;
        BarPanel _status, _footer;

        // 预设详情
        TextBox _txtName;
        ComboBox _cmbRes;
        NumericUpDown _numW, _numH;
        ComboBox _cmbScaling;
        TextBox _txtProcess;
        CheckBox _chkRestore;

        // 全局
        CheckBox _chkAutoSwitch, _chkAutoStart;
        Label _lblEnv, _lblCurrent, _lblStatus;

        public SettingsForm(Config config)
        {
            _config = config;

            Text = AppInfo.Title + " 设置 — 分辨率 / 缩放 / 程序关联";
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;      // 由 UiKit.S() 手动缩放，避免二次缩放
            Font = UiKit.FontBody;
            BackColor = UiKit.Canvas;
            ShowInTaskbar = true;
            try { Icon = AppIcon.Get(32); }
            catch { }

            // 高缩放机器上（本机 3840x2160 用 300%，逻辑桌面只有 1280x720）窗口不能超出工作区，
            // 否则底栏按钮会被屏幕切掉；放不下的内容由卡片的滚动条兜底（见 Scrollable）。
            // 顶部不再有品牌横幅，所以窗口高度比上一版矮了 100 逻辑像素。
            Size desired = UiKit.Sz(1120, 580);
            int maxW, maxH;
            try
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                maxW = Math.Max(UiKit.S(760), wa.Width - UiKit.S(12));
                maxH = Math.Max(UiKit.S(520), wa.Height - UiKit.S(12));
            }
            catch
            {
                maxW = desired.Width;
                maxH = desired.Height;
            }
            Size fit = new Size(Math.Min(desired.Width, maxW), Math.Min(desired.Height, maxH));
            Size = fit;
            MinimumSize = new Size(Math.Min(UiKit.S(1000), fit.Width), Math.Min(UiKit.S(570), fit.Height));

            BuildUi();
            RefreshResolutionCombo();
            RefreshEnvironment();
            LoadPresetsIntoList(0);
        }

        // ==================================================================
        // 界面搭建
        // ==================================================================

        void BuildUi()
        {
            // 主窗口不放品牌横幅（程序名已经在标题栏里，界面留白给内容）；
            // 顶部只保留一条状态条，品牌色靠状态条下沿的渐变细线、卡片标题短条和渐变按钮带出来。
            // ---------- 状态条：环境信息（左） + 关于 / 重新检测（右） ----------
            _status = new BarPanel(false);
            _status.LineOnBottom = true;
            _status.Dock = DockStyle.Top;
            _status.Height = UiKit.S(76);

            var statusGrid = new GlassTable();
            statusGrid.Dock = DockStyle.Fill;
            statusGrid.ColumnCount = 3;
            statusGrid.RowCount = 1;
            statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiKit.S(64)));
            statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiKit.S(120)));
            
            statusGrid.Padding = UiKit.Pad(24, 12, 24, 12);

            var infoBlock = new GlassPanel();
            infoBlock.Dock = DockStyle.Fill;
            
            infoBlock.Padding = UiKit.Pad(0, 2, UiKit.S(20), 0);

            // 左对齐的两行环境信息（两行等高，文字垂直居中，上下基线看起来是齐的）
            _lblEnv = new Label();
            _lblEnv.AutoSize = false;
            _lblEnv.Dock = DockStyle.Top;
            _lblEnv.Height = UiKit.S(25);
            _lblEnv.TextAlign = ContentAlignment.MiddleLeft;
            _lblEnv.Font = UiKit.FontBody;
            _lblEnv.ForeColor = UiKit.TextPrimary;
            _lblEnv.AutoEllipsis = true;

            _lblCurrent = new Label();
            _lblCurrent.AutoSize = false;
            _lblCurrent.Dock = DockStyle.Top;
            _lblCurrent.Height = UiKit.S(25);
            _lblCurrent.TextAlign = ContentAlignment.MiddleLeft;
            _lblCurrent.Font = UiKit.FontHint;
            _lblCurrent.ForeColor = UiKit.TextSecondary;
            _lblCurrent.AutoEllipsis = true;

            // Dock=Top 的堆叠顺序与添加顺序相反
            infoBlock.Controls.Add(_lblCurrent);
            infoBlock.Controls.Add(_lblEnv);

            var btnAbout = new FlatButton("关于", FlatButton.Kind.Link, 7);
            btnAbout.Size = UiKit.Sz(48, 34);
            btnAbout.Anchor = AnchorStyles.Right;
            btnAbout.Margin = new Padding(0, 0, UiKit.S(12), 0);
            btnAbout.Click += delegate { Program.RunAboutOwned(this); };

            var btnRescan = new FlatButton("重新检测", FlatButton.Kind.Secondary);
            btnRescan.Size = UiKit.Sz(112, 34);
            btnRescan.Anchor = AnchorStyles.Right;
            btnRescan.Margin = new Padding(0);
            btnRescan.Click += delegate
            {
                RefreshResolutionCombo();
                RefreshEnvironment();
                SetStatus("已重新检测显示器和显卡状态。");
            };

            statusGrid.Controls.Add(infoBlock, 0, 0);
            statusGrid.Controls.Add(btnAbout, 1, 0);
            statusGrid.Controls.Add(btnRescan, 2, 0);
            _status.Controls.Add(statusGrid);

            // ---------- 底栏 ----------
            _footer = new BarPanel(true);
            _footer.Dock = DockStyle.Bottom;
            _footer.Height = UiKit.S(100);

            var footerGrid = new GlassTable();
            footerGrid.Dock = DockStyle.Fill;
            footerGrid.ColumnCount = 2;
            footerGrid.RowCount = 1;
            footerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            footerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            
            footerGrid.Padding = UiKit.Pad(24, 14, 24, 14);

            var leftBlock = new GlassPanel();
            leftBlock.Dock = DockStyle.Fill;
            

            _chkAutoSwitch = UiKit.CheckBoxFlat("托盘常驻时自动跟随程序切换");
            _chkAutoSwitch.Dock = DockStyle.Top;
            _chkAutoSwitch.Height = UiKit.S(30);
            _chkAutoSwitch.CheckedChanged += delegate
            {
                if (_loading) return;
                _config.AutoSwitch = _chkAutoSwitch.Checked;
                MarkDirty();
            };

            _chkAutoStart = UiKit.CheckBoxFlat("开机自动启动（登录后常驻托盘）");
            _chkAutoStart.Dock = DockStyle.Top;
            _chkAutoStart.Height = UiKit.S(30);
            _chkAutoStart.CheckedChanged += delegate
            {
                if (_loading) return;
                try
                {
                    AutoStart.Set(_chkAutoStart.Checked);
                    SetStatus(_chkAutoStart.Checked
                        ? "已设置开机自启：" + AutoStart.Command
                        : "已取消开机自启。");
                }
                catch (Exception e)
                {
                    MessageBox.Show(this, "设置开机自启失败：" + e.Message, AppInfo.Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _loading = true;
                    try { _chkAutoStart.Checked = AutoStart.IsEnabled(); }
                    catch { _chkAutoStart.Checked = false; }
                    _loading = false;
                }
            };

            _lblStatus = new Label();
            _lblStatus.Dock = DockStyle.Top;
            _lblStatus.Height = UiKit.S(24);
            _lblStatus.Font = UiKit.FontHint;
            _lblStatus.ForeColor = UiKit.TextSecondary;
            _lblStatus.AutoEllipsis = true;

            // Dock=Top 的堆叠顺序与添加顺序相反，所以倒着加
            leftBlock.Controls.Add(_chkAutoStart);
            leftBlock.Controls.Add(_chkAutoSwitch);
            leftBlock.Controls.Add(_lblStatus);

            var actions = new FlowLayoutPanel();
            actions.FlowDirection = FlowDirection.RightToLeft;
            actions.WrapContents = false;
            actions.AutoSize = true;
            actions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            actions.Anchor = AnchorStyles.Right;
            

            AddFooterButton(actions, "关闭", 88, FlatButton.Kind.Secondary, delegate { Close(); });
            AddFooterButton(actions, "保存并应用", 116, FlatButton.Kind.Primary,
                delegate { if (SaveConfig(true)) ApplyCurrent(true); });
            AddFooterButton(actions, "保存", 84, FlatButton.Kind.Secondary, delegate { SaveConfig(true); });
            AddFooterButton(actions, "仅应用（测试）", 128, FlatButton.Kind.Secondary, delegate { ApplyCurrent(false); });
            AddFooterButton(actions, "放弃改动并重载", 140, FlatButton.Kind.Secondary, delegate { ReloadFromDisk(); });

            footerGrid.Controls.Add(leftBlock, 0, 0);
            footerGrid.Controls.Add(actions, 1, 0);
            _footer.Controls.Add(footerGrid);

            // ---------- 中间：两张卡片（浮在浅粉画布上，带柔和投影） ----------
            _split = new SplitContainer();
            _split.Size = UiKit.Sz(1040, 560);
            _split.Dock = DockStyle.Fill;
            _split.FixedPanel = FixedPanel.Panel1;
            _split.BackColor = UiKit.Canvas;
            _split.SplitterWidth = UiKit.S(14);
            _split.Panel1MinSize = UiKit.S(300);
            _split.Panel2MinSize = UiKit.S(500);
            _split.SplitterDistance = UiKit.S(416);
            _split.Panel1.BackColor = UiKit.Canvas;
            _split.Panel2.BackColor = UiKit.Canvas;
            _split.Panel1.Padding = UiKit.Pad(18, 8, 6, 10);
            _split.Panel2.Padding = UiKit.Pad(6, 8, 18, 10);

            var host1 = new CardShadowHost();
            host1.Dock = DockStyle.Fill;
            host1.Controls.Add(BuildPresetCard());

            var host2 = new CardShadowHost();
            host2.Dock = DockStyle.Fill;
            host2.Controls.Add(BuildDetailCard());

            _split.Panel1.Controls.Add(host1);
            _split.Panel2.Controls.Add(host2);

            // Dock 顺序：后加的贴外沿，所以状态条最后加 → 在最上面
            Controls.Add(_split);
            Controls.Add(_footer);
            Controls.Add(_status);

            // 主界面半透明底纹：把 zoe-bg.png 压成柔和背景铺在两张卡片后面
            //（卡片本体是半透明白，底纹会从卡片里透出来；没有素材时自动跳过）
            _split.Resize += delegate { UpdateBackdrop(); };
            Load += delegate { UpdateBackdrop(); };

            FormClosing += OnFormClosing;
        }

        // ==================================================================
        // 主界面底纹
        // ==================================================================

        Bitmap _veil;
        readonly System.Collections.Generic.List<Bitmap> _veilCrops = new System.Collections.Generic.List<Bitmap>();

        /// <summary>
        /// 生成整块底纹（整窗尺寸），再按各容器在窗口里的位置裁剪下发 ——
        /// 顶栏、左右两块面板、底栏拿到的都是同一张图的不同区域，图案跨控件是连续的。
        /// </summary>
        void UpdateBackdrop()
        {
            try
            {
                if (!Backdrop.HasImage) return;
                int w = ClientSize.Width, h = ClientSize.Height;
                if (w < 64 || h < 64) return;
                if (_veil != null && _veil.Width == w && _veil.Height == h) return;

                Bitmap veil = Backdrop.Veil(w, h);
                if (veil == null) return;

                var fresh = new System.Collections.Generic.List<Bitmap>();
                Apply(_status, veil, fresh);
                Apply(_footer, veil, fresh);
                Apply(_split.Panel1, veil, fresh, _split.Left, _split.Top);
                Apply(_split.Panel2, veil, fresh, _split.Left, _split.Top);

                Bitmap oldVeil = _veil;
                System.Collections.Generic.List<Bitmap> oldCrops = new System.Collections.Generic.List<Bitmap>(_veilCrops);
                _veil = veil;
                _veilCrops.Clear();
                _veilCrops.AddRange(fresh);

                TintSurfaces(this, veil);

                foreach (Bitmap b in oldCrops) { try { b.Dispose(); } catch { } }
                if (oldVeil != null) { try { oldVeil.Dispose(); } catch { } }
            }
            catch { }
        }

        /// <summary>把一个容器在窗口里的那块底纹裁下来挂上去</summary>
        void Apply(Control c, Bitmap veil, System.Collections.Generic.List<Bitmap> keep,
            int parentOffsetX = -1, int parentOffsetY = -1)
        {
            if (c == null || c.Width <= 0 || c.Height <= 0) return;
            int ox = parentOffsetX < 0 ? c.Left : parentOffsetX + c.Left;
            int oy = parentOffsetY < 0 ? c.Top : parentOffsetY + c.Top;

            var src = new Rectangle(ox, oy, c.Width, c.Height);
            src.Intersect(new Rectangle(0, 0, veil.Width, veil.Height));

            var crop = new Bitmap(c.Width, c.Height);
            using (Graphics g = Graphics.FromImage(crop))
            {
                g.Clear(UiKit.Canvas);
                if (src.Width > 0 && src.Height > 0)
                {
                    g.DrawImage(veil, new Rectangle(src.X - ox, src.Y - oy, src.Width, src.Height),
                        src, GraphicsUnit.Pixel);
                }
            }
            var panel = c as Panel;
            if (panel != null)
            {
                panel.BackgroundImage = crop;
                panel.BackgroundImageLayout = ImageLayout.None;
            }
            else
            {
                c.BackgroundImage = crop;
                c.BackgroundImageLayout = ImageLayout.None;
            }
            keep.Add(crop);
        }

        /// <summary>
        /// 原生控件（TextBox / ComboBox / NumericUpDown / ListBox / CheckBox）和自绘按钮都不支持透明背景，
        /// 直接留白就会出现"整行死白、看不到底纹"。这里给每个控件取它所在位置底纹的偏亮平均色当浮层色，
        /// 视觉上就和背景连成一片了。窗口尺寸变化时随底纹一起重算。
        /// </summary>
        void TintSurfaces(Control root, Bitmap veil)
        {
            foreach (Control c in root.Controls)
            {
                var btn = c as FlatButton;
                if (btn != null)
                {
                    Color bt = Backdrop.AverageLight(veil, ClientRectOf(c));
                    btn.Surface = bt;
                    btn.Fill = bt;
                }
                else if (c is CheckBox || c is RadioButton)
                {
                    // 这两类支持透明背景，直接透出底纹，不用浮层色
                    c.BackColor = Color.Transparent;
                }
                else if (c is TextBox || c is ComboBox || c is NumericUpDown || c is ListBox)
                {
                    // 系统原生控件不支持透明背景，只能取该处的平均色当浮层色（见方法注释）
                    c.BackColor = Backdrop.AverageLight(veil, ClientRectOf(c));
                }
                if (c.HasChildren) TintSurfaces(c, veil);
            }
        }

        /// <summary>控件在窗口客户区里的矩形（底纹就是按客户区坐标生成的）</summary>
        Rectangle ClientRectOf(Control c)
        {
            Point p;
            try { p = PointToClient(c.PointToScreen(Point.Empty)); }
            catch { p = c.Location; }
            return new Rectangle(p, c.Size);
        }
        void AddFooterButton(FlowLayoutPanel host, string text, int width, FlatButton.Kind kind, EventHandler onClick)
        {
            var b = new FlatButton(text, kind);
            b.Size = UiKit.Sz(width, 38);
            b.Margin = new Padding(UiKit.S(10), 0, 0, 0);
            b.Click += onClick;
            host.Controls.Add(b);
        }

        Control BuildPresetCard()
        {
            var card = new CardPanel("预设");
            card.Dock = DockStyle.Fill;

            _presetList = new PresetListBox();
            _presetList.Dock = DockStyle.Fill;
            _presetList.SelectedIndexChanged += delegate
            {
                if (_loading) return;
                LoadEditorFromPreset(CurrentPreset);
            };
            card.AddFill(_presetList);

            // 五个按钮等宽排一行，用 TableLayoutPanel 而不是 FlowLayoutPanel：
            // FlowLayoutPanel 在内容总宽刚好等于容器宽时会把最后一个子控件的右边框裁掉
            // （实测就是「下移」缺了一整条边框）。表格布局留了余量，且五个按钮严格等宽、基线齐平。
            var strip = new GlassTable();
            strip.ColumnCount = 5;
            strip.RowCount = 1;
            strip.Height = UiKit.S(36);
            strip.Margin = new Padding(0);
            strip.Padding = new Padding(0);

            for (int i = 0; i < 5; i++) strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));

            _btnAdd = MakeStripButton("新增", FlatButton.Kind.Primary, false, delegate { AddPreset(); });
            _btnCopy = MakeStripButton("复制", FlatButton.Kind.Secondary, false, delegate { CopyPreset(); });
            _btnDelete = MakeStripButton("删除", FlatButton.Kind.Secondary, false, delegate { DeletePreset(); });
            _btnUp = MakeStripButton("上移", FlatButton.Kind.Secondary, false, delegate { MovePreset(-1); });
            _btnDown = MakeStripButton("下移", FlatButton.Kind.Secondary, true, delegate { MovePreset(1); });

            strip.Controls.Add(_btnAdd, 0, 0);
            strip.Controls.Add(_btnCopy, 1, 0);
            strip.Controls.Add(_btnDelete, 2, 0);
            strip.Controls.Add(_btnUp, 3, 0);
            strip.Controls.Add(_btnDown, 4, 0);

            card.AddWide(strip, 46, true);

            // 恢复目标：紧跟预设列表，和列表项上的「恢复目标」徽标对应
            _chkRestore = UiKit.CheckBoxFlat("所有关联程序退出后，自动恢复到本预设");
            _chkRestore.Anchor = AnchorStyles.Left;
            _chkRestore.CheckedChanged += delegate { OnRestoreToggled(); };
            card.AddWide(_chkRestore, 44, false);

            return card;
        }

        Control BuildDetailCard()
        {
            var card = new CardPanel("预设详情");
            card.Dock = DockStyle.Fill;
            card.Scrollable = true;   // 高缩放 / 小屏时字段放不下可以滚动，不会丢内容

            // 名称
            _txtName = new TextBox();
            UiKit.StyleTextBox(_txtName);
            _txtName.TextChanged += delegate
            {
                if (_loading) return;
                Preset p = CurrentPreset;
                if (p == null) return;
                p.Name = _txtName.Text.Trim();
                MarkDirty();
                RefreshListText(p);
            };
            card.AddRow("名称", _txtName, 44);

            // 分辨率：本机实时枚举的下拉框 + 驱动级自定义分辨率入口
            // 用嵌套行保证"下拉框 + 右侧按钮"的右边缘与上下各行严格对齐
            _cmbRes = new ComboBox();
            UiKit.StyleCombo(_cmbRes);
            _cmbRes.DropDownStyle = ComboBoxStyle.DropDown;
            _cmbRes.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _cmbRes.Margin = new Padding(0, 0, UiKit.S(10), 0);
            _cmbRes.SelectedIndexChanged += delegate
            {
                if (_loading) return;
                if (_cmbRes.SelectedItem is DisplayMode)
                {
                    DisplayMode m = (DisplayMode)_cmbRes.SelectedItem;
                    _loading = true;
                    _numW.Value = Clamp(m.Width);
                    _numH.Value = Clamp(m.Height);
                    _loading = false;
                    CommitResolution();
                }
            };

            var btnCustom = new FlatButton("创建自定义分辨率…", FlatButton.Kind.Secondary, 5);
            btnCustom.Size = UiKit.Sz(152, 32);
            btnCustom.Anchor = AnchorStyles.Right;
            btnCustom.Margin = new Padding(0);
            btnCustom.Click += delegate { CreateCustomResolution(); };

            var resRow = UiKit.NestedRow(36);
            resRow.ColumnCount = 2;
            resRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            resRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiKit.S(152)));
            resRow.Controls.Add(_cmbRes, 0, 0);
            resRow.Controls.Add(btnCustom, 1, 0);
            card.AddRow("分辨率", resRow, 44);

            // 自定义宽高（可以直接输入任意尺寸）
            _numW = new NumericUpDown();
            UiKit.StyleNumeric(_numW);
            _numW.Minimum = Preset.MinDimension;
            _numW.Maximum = Preset.MaxDimension;
            _numW.Increment = 2;
            _numW.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _numW.Margin = new Padding(0);
            _numW.ValueChanged += delegate { if (!_loading) CommitResolution(); };

            var times = new Label();
            times.Text = "×";
            times.AutoSize = false;
            times.TextAlign = ContentAlignment.MiddleCenter;
            times.Font = UiKit.FontBody;
            times.ForeColor = UiKit.TextSecondary;
            times.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            times.Margin = new Padding(0);
            times.Height = UiKit.S(30);

            _numH = new NumericUpDown();
            UiKit.StyleNumeric(_numH);
            _numH.Minimum = Preset.MinDimension;
            _numH.Maximum = Preset.MaxDimension;
            _numH.Increment = 2;
            _numH.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _numH.Margin = new Padding(0);
            _numH.ValueChanged += delegate { if (!_loading) CommitResolution(); };

            var btnCurrent = new FlatButton("取当前分辨率", FlatButton.Kind.Secondary, 5);
            btnCurrent.Size = UiKit.Sz(116, 32);
            btnCurrent.Anchor = AnchorStyles.Right;
            btnCurrent.Margin = new Padding(0);
            btnCurrent.Click += delegate
            {
                DisplayInfo.DEVMODE cur = DisplayInfo.GetCurrentMode();
                _loading = true;
                _numW.Value = Clamp(cur.dmPelsWidth);
                _numH.Value = Clamp(cur.dmPelsHeight);
                _loading = false;
                CommitResolution();
            };

            var sizeRow = UiKit.NestedRow(36);
            sizeRow.ColumnCount = 5;
            sizeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiKit.S(100)));
            sizeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiKit.S(30)));
            sizeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiKit.S(100)));
            sizeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            sizeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiKit.S(116)));
            sizeRow.Controls.Add(_numW, 0, 0);
            sizeRow.Controls.Add(times, 1, 0);
            sizeRow.Controls.Add(_numH, 2, 0);
            sizeRow.Controls.Add(btnCurrent, 4, 0);
            card.AddRow("自定义尺寸", sizeRow, 44);

            // 缩放方式
            _cmbScaling = new ComboBox();
            UiKit.StyleCombo(_cmbScaling);
            _cmbScaling.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (ScalingSpec s in Preset.ScalingChoices) _cmbScaling.Items.Add(ScalingNames.Label(s));
            _cmbScaling.SelectedIndexChanged += delegate
            {
                if (_loading) return;
                Preset p = CurrentPreset;
                if (p == null) return;
                int idx = _cmbScaling.SelectedIndex;
                if (idx >= 0 && idx < Preset.ScalingChoices.Length)
                {
                    p.ScalingSpec = Preset.ScalingChoices[idx];
                    MarkDirty();
                    RefreshListText(p);
                }
            };
            card.AddRow("缩放方式", _cmbScaling, 44);

            // 关联程序
            _txtProcess = new TextBox();
            UiKit.StyleTextBox(_txtProcess);
            _txtProcess.TextChanged += delegate
            {
                if (_loading) return;
                Preset p = CurrentPreset;
                if (p == null) return;
                p.ProcessNames = Preset.ParseProcessList(_txtProcess.Text);
                MarkDirty();
                RefreshListText(p);
            };
            card.AddRow("关联程序", _txtProcess, 44);

            card.AddWide(UiKit.HintLabel("填进程名（不含 .exe），多个用分号隔开；命中后托盘会自动切到本预设。"), 22, true);

            var procStrip = MakeStrip();

            var btnPick = new FlatButton("选择运行中的程序…", FlatButton.Kind.Secondary, 5);
            btnPick.Size = UiKit.Sz(176, 34);
            btnPick.Margin = new Padding(0, 0, UiKit.S(10), 0);
            btnPick.Click += delegate { PickProcesses(); };

            var btnClearProc = new FlatButton("清空关联", FlatButton.Kind.Secondary, 5);
            btnClearProc.Size = UiKit.Sz(104, 34);
            btnClearProc.Margin = new Padding(0);
            btnClearProc.Click += delegate
            {
                _loading = true;
                _txtProcess.Text = "";
                _loading = false;
                Preset p = CurrentPreset;
                if (p != null)
                {
                    p.ProcessNames = new string[0];
                    MarkDirty();
                    RefreshListText(p);
                }
            };

            procStrip.Controls.Add(btnPick);
            procStrip.Controls.Add(btnClearProc);
            card.AddWide(procStrip, 50, false);

            card.AddFiller();
            return card;
        }

        /// <summary>
        /// 勾选 / 取消"恢复目标"。
        /// 这个开关放在预设列表下面（和列表里的「恢复目标」徽标对应），把右侧详情卡片的高度省出来。
        /// </summary>
        void OnRestoreToggled()
        {
            if (_loading) return;
            Preset p = CurrentPreset;
            if (p == null) return;
            _config.RestoreKey = _chkRestore.Checked ? p.Key : "";
            MarkDirty();
            int keep = _presetList.SelectedIndex;
            LoadPresetsIntoList(keep);
            SetStatus(_chkRestore.Checked
                ? "恢复目标已设为：" + ((p.Name == null || p.Name.Length == 0) ? p.Key : p.Name)
                : "已取消恢复目标（程序退出后将不再自动切换）。");
        }

        /// <summary>预设列表下面那排按钮：等宽铺满单元格，边框不会被父容器裁掉</summary>
        FlatButton MakeStripButton(string text, FlatButton.Kind kind, bool lastInRow, EventHandler onClick)
        {
            var b = new FlatButton(text, kind);
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(UiKit.S(3), 0, UiKit.S(3), 0);
            b.Click += onClick;
            return b;
        }

        /// <summary>一行里横向排列的小控件条（自适应宽度、左对齐、行内垂直居中）</summary>
        static FlowLayoutPanel MakeStrip()
        {
            var f = new FlowLayoutPanel();
            f.FlowDirection = FlowDirection.LeftToRight;
            f.WrapContents = false;
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.Anchor = AnchorStyles.Left;
            f.BackColor = UiKit.Card;
            return f;
        }

        static int Clamp(int v)
        {
            if (v < Preset.MinDimension) return Preset.MinDimension;
            if (v > Preset.MaxDimension) return Preset.MaxDimension;
            return v;
        }

        // ==================================================================
        // 数据 <-> 界面
        // ==================================================================

        Preset CurrentPreset
        {
            get
            {
                int i = _presetList.SelectedIndex;
                if (i < 0 || i >= _config.Presets.Count) return null;
                return _config.Presets[i];
            }
        }

        void LoadPresetsIntoList(int select)
        {
            _loading = true;
            _presetList.BeginUpdate();
            _presetList.Items.Clear();
            foreach (Preset p in _config.Presets) _presetList.Items.Add(p);
            _presetList.EndUpdate();
            _presetList.RestoreKey = _config.RestoreKey;
            if (_presetList.Items.Count > 0)
            {
                if (select < 0) select = 0;
                if (select >= _presetList.Items.Count) select = _presetList.Items.Count - 1;
                _presetList.SelectedIndex = select;
            }
            _loading = false;
            LoadEditorFromPreset(CurrentPreset);
            RefreshGlobalOptions();
        }

        /// <summary>预设内容变了：刷新列表里的该行（列表是自绘的，只要重画即可）</summary>
        void RefreshListText(Preset p)
        {
            int i = _config.Presets.IndexOf(p);
            if (i < 0 || i >= _presetList.Items.Count) return;
            _loading = true;
            _presetList.Items[i] = p;
            _presetList.RestoreKey = _config.RestoreKey;
            _presetList.SelectedIndex = i;
            _loading = false;
            _presetList.Invalidate();
        }

        void LoadEditorFromPreset(Preset p)
        {
            _loading = true;
            try
            {
                if (p == null)
                {
                    _txtName.Text = "";
                    _txtProcess.Text = "";
                    _chkRestore.Checked = false;
                    return;
                }
                _txtName.Text = p.Name;
                _numW.Value = Clamp(p.Width);
                _numH.Value = Clamp(p.Height);
                _txtProcess.Text = p.ProcessList;
                _chkRestore.Checked = string.Equals(p.Key, _config.RestoreKey, StringComparison.OrdinalIgnoreCase);
                SelectScalingInCombo(p.ScalingSpec);
                SelectResolutionInCombo(p.Width, p.Height);
            }
            finally
            {
                _loading = false;
            }
        }

        void SelectScalingInCombo(ScalingSpec scaling)
        {
            int idx = Array.IndexOf(Preset.ScalingChoices, scaling);
            if (idx < 0)
            {
                string label = ScalingNames.Label(scaling);
                if (!_cmbScaling.Items.Contains(label)) _cmbScaling.Items.Add(label);
                idx = _cmbScaling.Items.IndexOf(label);
            }
            _cmbScaling.SelectedIndex = idx;
        }

        void SelectResolutionInCombo(int w, int h)
        {
            for (int i = 0; i < _cmbRes.Items.Count; i++)
            {
                if (_cmbRes.Items[i] is DisplayMode)
                {
                    DisplayMode m = (DisplayMode)_cmbRes.Items[i];
                    if (m.Width == w && m.Height == h)
                    {
                        _cmbRes.SelectedIndex = i;
                        return;
                    }
                }
            }
            _cmbRes.SelectedIndex = -1;
            _cmbRes.Text = "自定义 " + w + "x" + h + "（当前显示器未列出）";
        }

        void RefreshGlobalOptions()
        {
            _loading = true;
            _chkAutoSwitch.Checked = _config.AutoSwitch;
            try { _chkAutoStart.Checked = AutoStart.IsEnabled(); }
            catch { _chkAutoStart.Checked = false; }
            _loading = false;
        }

        void RefreshEnvironment()
        {
            _lblEnv.Text = "显卡：" + Switcher.EnvironmentText;
            string cur;
            try { cur = DisplayInfo.CurrentModeText(); }
            catch (Exception e) { cur = "读取失败：" + e.Message; }
            // 缩放通路要跟上：v0.11 跨厂商后用户需要知道当前走 NVAPI 还是 Windows 原生接口
            _lblCurrent.Text = "当前 " + cur + "    缩放 " + Switcher.GetCurrentScalingText()
                + "    通路 " + Switcher.ScalingBackendText;
        }

        void RefreshResolutionCombo()
        {
            int keepW = (int)_numW.Value;
            int keepH = (int)_numH.Value;
            _loading = true;
            try
            {
                _cmbRes.Items.Clear();
                foreach (DisplayMode m in DisplayInfo.GetResolutions()) _cmbRes.Items.Add(m);
                if (keepW > 0 && keepH > 0) SelectResolutionInCombo(keepW, keepH);
            }
            finally
            {
                _loading = false;
            }
        }

        void CommitResolution()
        {
            Preset p = CurrentPreset;
            if (p == null) return;
            p.Width = (int)_numW.Value;
            p.Height = (int)_numH.Value;
            MarkDirty();
            _loading = true;
            try
            {
                SelectResolutionInCombo(p.Width, p.Height);
            }
            finally
            {
                _loading = false;
            }
            RefreshListText(p);
        }

        void MarkDirty()
        {
            _dirty = true;
        }

        void SetStatus(string text)
        {
            _lblStatus.Text = text;
        }

        // ==================================================================
        // 预设增删改
        // ==================================================================

        void AddPreset()
        {
            DisplayInfo.DEVMODE cur = DisplayInfo.GetCurrentMode();
            string key = _config.MakeUniqueKey("game");
            var p = new Preset(key, "新预设", cur.dmPelsWidth > 0 ? cur.dmPelsWidth : 1920,
                cur.dmPelsHeight > 0 ? cur.dmPelsHeight : 1080, 2, null);
            _config.Presets.Add(p);
            MarkDirty();
            LoadPresetsIntoList(_config.Presets.Count - 1);
            _txtName.Focus();
            _txtName.SelectAll();
            SetStatus("已新增预设，改好名称、分辨率、关联程序后点“保存”。");
        }

        void CopyPreset()
        {
            Preset cur = CurrentPreset;
            if (cur == null) return;
            Preset c = cur.Clone();
            c.Key = _config.MakeUniqueKey(cur.Key);
            c.Name = cur.Name + " 副本";
            _config.Presets.Insert(_config.Presets.IndexOf(cur) + 1, c);
            MarkDirty();
            LoadPresetsIntoList(_config.Presets.IndexOf(c));
        }

        void DeletePreset()
        {
            Preset cur = CurrentPreset;
            if (cur == null) return;
            if (_config.Presets.Count <= 1)
            {
                MessageBox.Show(this, "至少要保留一个预设。", AppInfo.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "确定删除预设「" + cur.Name + "」？", AppInfo.Title,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            int i = _config.Presets.IndexOf(cur);
            _config.Presets.Remove(cur);
            if (string.Equals(_config.RestoreKey, cur.Key, StringComparison.OrdinalIgnoreCase)) _config.RestoreKey = "";
            MarkDirty();
            LoadPresetsIntoList(Math.Max(0, i - 1));
        }

        void MovePreset(int delta)
        {
            Preset cur = CurrentPreset;
            if (cur == null) return;
            int i = _config.Presets.IndexOf(cur);
            int j = i + delta;
            if (j < 0 || j >= _config.Presets.Count) return;
            _config.Presets.RemoveAt(i);
            _config.Presets.Insert(j, cur);
            MarkDirty();
            LoadPresetsIntoList(j);
        }

        void PickProcesses()
        {
            Preset p = CurrentPreset;
            if (p == null) return;
            using (var dlg = new ProcessPickerForm(p.ProcessNames))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                p.ProcessNames = dlg.SelectedProcessNames;
                _loading = true;
                _txtProcess.Text = p.ProcessList;
                _loading = false;
                MarkDirty();
                RefreshListText(p);
                SetStatus(p.ProcessNames.Length > 0
                    ? "已关联 " + p.ProcessNames.Length + " 个程序：" + p.ProcessList
                    : "已清空关联程序（本预设将不再被自动切换触发）。");
            }
        }

        void CreateCustomResolution()
        {
            // 通用版（不带 NVAPI）里没有这个对话框，直接说明原因。
            // 用编译符号判断，而不是运行时判断 —— 通用版压根不编译 CustomResolutionForm。
            if (!GpuExtensions.SupportsCustomResolution)
            {
                MessageBox.Show(this,
                    GpuExtensions.UnavailableReason("创建自定义分辨率"),
                    AppInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!Switcher.NvapiReady)
            {
                MessageBox.Show(this,
                    "创建自定义分辨率是 NVIDIA 驱动专有的功能（通过 NVAPI），当前不可用：\n"
                    + Switcher.NvapiError
                    + "\n\n注意：这只是“自定义分辨率”这一项不可用，"
                    + "切换分辨率和缩放方式在 AMD / Intel 显卡上照常可用。",
                    AppInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

#if WITH_NVAPI
            using (var dlg = new CustomResolutionForm((int)_numW.Value, (int)_numH.Value))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                RefreshResolutionCombo();
                _loading = true;
                _numW.Value = Clamp(dlg.ResultWidth);
                _numH.Value = Clamp(dlg.ResultHeight);
                _loading = false;
                CommitResolution();
                RefreshEnvironment();
                SetStatus("自定义分辨率 " + dlg.ResultWidth + "x" + dlg.ResultHeight + " 已写入驱动，现在可以直接应用或保存。");
            }
#endif
        }

        // ==================================================================
        // 保存 / 应用
        // ==================================================================

        void ValidateAndFix()
        {
            foreach (Preset p in _config.Presets)
            {
                if (p.Name == null || p.Name.Trim().Length == 0) p.Name = p.Key;
                if (!p.IsUsable)
                {
                    p.Width = Clamp(p.Width);
                    p.Height = Clamp(p.Height);
                }
                p.ProcessNames = Preset.ParseProcessList(p.ProcessList);
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Preset p in _config.Presets)
            {
                if (p.Key == null || p.Key.Trim().Length == 0 || used.Contains(p.Key)
                    || string.Equals(p.Key, Config.GeneralSection, StringComparison.OrdinalIgnoreCase))
                {
                    string old = p.Key;
                    p.Key = _config.MakeUniqueKey(string.IsNullOrEmpty(old) ? p.Name : old);
                }
                used.Add(p.Key);
            }

            if (_config.Find(_config.RestoreKey) == null)
            {
                Preset r = null;
                foreach (Preset p in _config.Presets) { if (!p.HasProcessBinding) { r = p; break; } }
                _config.RestoreKey = r != null ? r.Key : _config.Presets[0].Key;
            }
        }

        bool SaveConfig(bool showStatus)
        {
            try
            {
                ValidateAndFix();
                _config.Save(_config.FilePath);
                _dirty = false;
                Switcher.Log("设置界面：已保存配置到 " + _config.FilePath + "（" + _config.Presets.Count + " 个预设）");
                LoadPresetsIntoList(_presetList.SelectedIndex);
                if (showStatus) SetStatus("已保存到 " + _config.FilePath + "（托盘程序会自动读取新配置）");
                return true;
            }
            catch (Exception e)
            {
                Switcher.Log("设置界面：保存失败 " + e.Message);
                MessageBox.Show(this, "保存失败：" + e.Message, AppInfo.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        void ApplyCurrent(bool afterSave)
        {
            Preset p = CurrentPreset;
            if (p == null) return;
            if (!p.IsUsable)
            {
                MessageBox.Show(this, "分辨率不合理：" + p.ResolutionText, AppInfo.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            System.Windows.Forms.Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            string msg;
            bool ok;
            try
            {
                ok = Switcher.Apply(p, out msg);
            }
            finally
            {
                this.Cursor = previous;
            }

            RefreshEnvironment();
            if (ok) SetStatus((afterSave ? "[已保存并应用] " : "[已应用] ") + msg);
            else
            {
                SetStatus("[失败] " + msg);
                MessageBox.Show(this, msg, AppInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void ReloadFromDisk()
        {
            if (_dirty &&
                MessageBox.Show(this, "将丢弃尚未保存的改动，重新从配置文件读取。继续？", AppInfo.Title,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            Config fresh = Config.Load(_config.FilePath);
            _config.AutoSwitch = fresh.AutoSwitch;
            _config.RestoreKey = fresh.RestoreKey;
            _config.Presets.Clear();
            foreach (Preset p in fresh.Presets) _config.Presets.Add(p);
            _dirty = false;

            RefreshGlobalOptions();
            LoadPresetsIntoList(0);
            RefreshEnvironment();
            SetStatus(fresh.Warnings.Count > 0 ? string.Join("；", fresh.Warnings.ToArray()) : "已重新载入配置文件。");
        }

        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_dirty)
            {
                Switcher.Log("设置界面：关闭（无未保存改动）");
                return;
            }
            Switcher.Log("设置界面：关闭时有未保存改动，弹出确认框");
            DialogResult r = MessageBox.Show(this, "还有改动没保存，要先保存吗？", AppInfo.Title,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            Switcher.Log("设置界面：用户选择 " + r);
            if (r == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (r == DialogResult.Yes && !SaveConfig(true)) e.Cancel = true;
        }
    }
}
