using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using PortalJump;   // 核心：AppInfo / AppPaths / Config / Preset / DisplayInfo / Switcher / AutoStart / Backdrop

namespace PortalJump.UI
{
    /// <summary>
    /// 设置界面（WPF 版，第 2 轮迁移）。
    /// 结构、文案、行为都对齐原来的 WinForms 版（SettingsForm），差别在于：
    ///   · 卡片/输入框/按钮都是真半透明，底纹从控件里透出来（WinForms 做不到）
    ///   · 底纹直接复用核心里的 Backdrop.Veil（乘法混色），和已确认的效果完全一致
    ///   · 选程序 / 自定义分辨率两个对话框也已迁到 WPF，整个界面进程 100% WPF、全部真半透明
    /// </summary>
    public partial class SettingsWindow : Window
    {
        // ---------- 预设列表的一行 ----------
        public class PresetRow : INotifyPropertyChanged
        {
            public Preset Preset;

            public PresetRow(Preset p) { Preset = p; }

            public string Name { get { return string.IsNullOrEmpty(Preset.Name) ? Preset.Key : Preset.Name; } }
            public string Summary { get { return Preset.Summary; } }

            bool _restore;
            public bool IsRestoreTarget
            {
                get { return _restore; }
                set
                {
                    if (_restore == value) return;
                    _restore = value;
                    Raise("IsRestoreTarget");
                    Raise("RestoreVisibility");
                }
            }
            public Visibility RestoreVisibility { get { return _restore ? Visibility.Visible : Visibility.Collapsed; } }

            /// <summary>列表项的 UIA 名字用这个（和 WinForms 版 Preset.ToString() 一致，探针按它取值）</summary>
            public override string ToString() { return Preset.ToString(); }

            public void Raise(string prop)
            {
                PropertyChangedEventHandler h = PropertyChanged;
                if (h != null) h(this, new PropertyChangedEventArgs(prop));
            }
            public event PropertyChangedEventHandler PropertyChanged;
        }

        // ---------- 分辨率下拉项 ----------
        class ResItem
        {
            public int W, H;
            public string Text;
            public override string ToString() { return Text; }
        }

        readonly Config _config;
        readonly ObservableCollection<PresetRow> _rows = new ObservableCollection<PresetRow>();
        bool _dirty, _loading;
        int _lastW = -1, _lastH = -1;

        public SettingsWindow(Config config)
        {
            InitializeComponent();
            _config = config;

            PresetList.ItemsSource = _rows;

            // 缩放方式下拉（与厂商无关的语义选项，AMD / Intel 也能选）
            foreach (ScalingSpec s in Preset.ScalingChoices) CmbScaling.Items.Add(ScalingNames.Label(s));

            Loaded += delegate
            {
                RefreshEnvironment();
                RefreshResolutionCombo();
                RefreshGlobalOptions();
                LoadPresets(_rows.Count > 0 ? 0 : -1);
                UpdateBackdrop();
            };
            SizeChanged += delegate { UpdateBackdrop(); };
            Closing += OnClosing;
        }

        // ==================================================================
        // 底纹
        // ==================================================================

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr hObject);

        void UpdateBackdrop()
        {
            try
            {
                int w = (int)Math.Round(ActualWidth), h = (int)Math.Round(ActualHeight);
                if (w < 64 || h < 64) return;
                if (w == _lastW && h == _lastH) return;
                if (!Backdrop.HasImage) return;

                using (var bmp = Backdrop.Veil(w, h))
                {
                    if (bmp == null) return;
                    IntPtr hb = bmp.GetHbitmap();
                    try
                    {
                        BitmapSource src = Imaging.CreateBitmapSourceFromHBitmap(hb, IntPtr.Zero,
                            Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        src.Freeze();
                        BackdropImage.Source = src;
                    }
                    finally { DeleteObject(hb); }
                }
                _lastW = w; _lastH = h;
            }
            catch { }
        }

        // ==================================================================
        // 列表 / 编辑器
        // ==================================================================

        Preset CurrentPreset
        {
            get
            {
                int i = PresetList.SelectedIndex;
                if (i < 0 || i >= _rows.Count) return null;
                return _rows[i].Preset;
            }
        }

        void LoadPresets(int select)
        {
            _loading = true;
            try
            {
                _rows.Clear();
                foreach (Preset p in _config.Presets)
                {
                    var row = new PresetRow(p);
                    row.IsRestoreTarget = IsRestore(p);
                    _rows.Add(row);
                }
                if (_rows.Count > 0)
                {
                    if (select < 0) select = 0;
                    if (select >= _rows.Count) select = _rows.Count - 1;
                    PresetList.SelectedIndex = select;
                }
            }
            finally { _loading = false; }

            LoadEditor(CurrentPreset);
            RefreshGlobalOptions();
        }

        bool IsRestore(Preset p)
        {
            return p != null && string.Equals(p.Key, _config.RestoreKey, StringComparison.OrdinalIgnoreCase);
        }

        void RefreshRow(Preset p)
        {
            int i = _config.Presets.IndexOf(p);
            if (i < 0 || i >= _rows.Count) return;
            _rows[i].IsRestoreTarget = IsRestore(p);
            _rows[i].Raise("Name");
            _rows[i].Raise("Summary");
        }

        void LoadEditor(Preset p)
        {
            _loading = true;
            try
            {
                if (p == null)
                {
                    TxtName.Text = "";
                    TxtProcess.Text = "";
                    TxtW.Text = "";
                    TxtH.Text = "";
                    ChkRestore.IsChecked = false;
                    return;
                }
                TxtName.Text = p.Name;
                TxtW.Text = p.Width.ToString(CultureInfo.InvariantCulture);
                TxtH.Text = p.Height.ToString(CultureInfo.InvariantCulture);
                TxtProcess.Text = p.ProcessList;
                ChkRestore.IsChecked = IsRestore(p);
                SelectScaling(p.ScalingSpec);
                SelectResolution(p.Width, p.Height);
            }
            finally { _loading = false; }
        }

        void SelectScaling(ScalingSpec scaling)
        {
            int idx = Array.IndexOf(Preset.ScalingChoices, scaling);
            if (idx < 0)
            {
                string label = ScalingNames.Label(scaling);
                if (!CmbScaling.Items.Contains(label)) CmbScaling.Items.Add(label);
                idx = CmbScaling.Items.IndexOf(label);
            }
            CmbScaling.SelectedIndex = idx;
        }

        void SelectResolution(int w, int h)
        {
            for (int i = 0; i < CmbRes.Items.Count; i++)
            {
                ResItem r = CmbRes.Items[i] as ResItem;
                if (r != null && r.W == w && r.H == h) { CmbRes.SelectedIndex = i; return; }
            }
            // 当前显示器没列出这个分辨率：补一条，保持和 WinForms 版一样能看到实际值
            var item = new ResItem { W = w, H = h, Text = "自定义 " + w + "x" + h + "（当前显示器未列出）" };
            CmbRes.Items.Add(item);
            CmbRes.SelectedIndex = CmbRes.Items.Count - 1;
        }

        void RefreshGlobalOptions()
        {
            _loading = true;
            try
            {
                ChkAutoSwitch.IsChecked = _config.AutoSwitch;
                try { ChkAutoStart.IsChecked = AutoStart.IsEnabled(); }
                catch { ChkAutoStart.IsChecked = false; }
            }
            finally { _loading = false; }
        }

        void RefreshEnvironment()
        {
            LblEnv.Text = "显卡：" + Switcher.EnvironmentText;
            string cur;
            try { cur = DisplayInfo.CurrentModeText(); }
            catch (Exception e) { cur = "读取失败：" + e.Message; }
            // 缩放通路要跟上：v0.11 跨厂商后用户需要知道当前走 NVAPI 还是 Windows 原生接口
            LblCurrent.Text = "当前 " + cur + "    缩放 " + Switcher.GetCurrentScalingText()
                + "    通路 " + Switcher.ScalingBackendText;
        }

        void RefreshResolutionCombo()
        {
            int keepW = ParseInt(TxtW.Text), keepH = ParseInt(TxtH.Text);
            _loading = true;
            try
            {
                CmbRes.Items.Clear();
                foreach (DisplayMode m in DisplayInfo.GetResolutions())
                    CmbRes.Items.Add(new ResItem { W = m.Width, H = m.Height, Text = m.Text });
                if (keepW > 0 && keepH > 0) SelectResolution(keepW, keepH);
            }
            finally { _loading = false; }
        }

        void SetStatus(string text) { StatusText.Text = text; }

        void MarkDirty() { _dirty = true; }

        static int ParseInt(string s)
        {
            int v;
            return int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        static int Clamp(int v)
        {
            if (v < Preset.MinDimension) return Preset.MinDimension;
            if (v > Preset.MaxDimension) return Preset.MaxDimension;
            return v;
        }

        // ==================================================================
        // 编辑器事件
        // ==================================================================

        void OnPresetSelected(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            LoadEditor(CurrentPreset);
        }

        void OnNameChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            Preset p = CurrentPreset;
            if (p == null) return;
            p.Name = TxtName.Text;
            MarkDirty();
            RefreshRow(p);
        }

        void OnProcessTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            Preset p = CurrentPreset;
            if (p == null) return;
            p.ProcessNames = Preset.ParseProcessList(TxtProcess.Text);
            MarkDirty();
            RefreshRow(p);
        }

        void OnSizeTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            Preset p = CurrentPreset;
            if (p == null) return;
            int w = ParseInt(TxtW.Text), h = ParseInt(TxtH.Text);
            if (w < Preset.MinDimension || h < Preset.MinDimension) return;   // 输入过程中不打断
            p.Width = w; p.Height = h;
            MarkDirty();
            RefreshRow(p);
            _loading = true;
            try { SelectResolution(w, h); }
            finally { _loading = false; }
        }

        void OnResolutionPicked(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            Preset p = CurrentPreset;
            ResItem r = CmbRes.SelectedItem as ResItem;
            if (p == null || r == null) return;
            p.Width = r.W; p.Height = r.H;
            MarkDirty();
            _loading = true;
            try { TxtW.Text = r.W.ToString(CultureInfo.InvariantCulture); TxtH.Text = r.H.ToString(CultureInfo.InvariantCulture); }
            finally { _loading = false; }
            RefreshRow(p);
        }

        void OnScalingPicked(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            Preset p = CurrentPreset;
            if (p == null || CmbScaling.SelectedIndex < 0) return;
            if (CmbScaling.SelectedIndex < Preset.ScalingChoices.Length)
                p.ScalingSpec = Preset.ScalingChoices[CmbScaling.SelectedIndex];
            MarkDirty();
            RefreshRow(p);
        }

        void OnRestoreToggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            Preset p = CurrentPreset;
            if (p == null) return;
            _config.RestoreKey = ChkRestore.IsChecked == true ? p.Key : "";
            MarkDirty();
            foreach (PresetRow r in _rows) r.IsRestoreTarget = IsRestore(r.Preset);
        }

        void OnAutoSwitchToggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _config.AutoSwitch = ChkAutoSwitch.IsChecked == true;
            MarkDirty();
            SetStatus(_config.AutoSwitch ? "已开启：托盘会按关联程序自动切换。" : "已关闭：托盘不再自动切换。");
        }

        void OnAutoStartToggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool want = ChkAutoStart.IsChecked == true;
            try
            {
                AutoStart.Set(want);
                SetStatus(want ? "已设置开机自动启动。" : "已取消开机自动启动。");
            }
            catch (Exception ex) { SetStatus("设置开机自启失败：" + ex.Message); }
        }

        void OnTakeCurrentResolution(object sender, RoutedEventArgs e)
        {
            Preset p = CurrentPreset;
            if (p == null) return;
            DisplayInfo.DEVMODE cur = DisplayInfo.GetCurrentMode();
            if (cur.dmPelsWidth <= 0 || cur.dmPelsHeight <= 0) return;
            ChkFromCurrent(cur.dmPelsWidth, cur.dmPelsHeight);
            SetStatus("已取当前分辨率 " + cur.dmPelsWidth + "x" + cur.dmPelsHeight + "。");
        }

        void ChkFromCurrent(int w, int h)
        {
            Preset p = CurrentPreset;
            if (p == null) return;
            p.Width = Clamp(w); p.Height = Clamp(h);
            MarkDirty();
            _loading = true;
            try { TxtW.Text = p.Width.ToString(CultureInfo.InvariantCulture); TxtH.Text = p.Height.ToString(CultureInfo.InvariantCulture); }
            finally { _loading = false; }
            RefreshResolutionCombo();
            RefreshRow(p);
        }


        // ==================================================================
        // 预设增删改
        // ==================================================================

        void OnAddPreset(object sender, RoutedEventArgs e)
        {
            DisplayInfo.DEVMODE cur = DisplayInfo.GetCurrentMode();
            string key = _config.MakeUniqueKey("game");
            var p = new Preset(key, "新预设", cur.dmPelsWidth > 0 ? cur.dmPelsWidth : 1920,
                cur.dmPelsHeight > 0 ? cur.dmPelsHeight : 1080, 2, null);
            _config.Presets.Add(p);
            MarkDirty();
            LoadPresets(_config.Presets.Count - 1);
            TxtName.Focus();
            TxtName.SelectAll();
            SetStatus("已新增预设，改好名称、分辨率、关联程序后点“保存”。");
        }

        void OnCopyPreset(object sender, RoutedEventArgs e)
        {
            Preset cur = CurrentPreset;
            if (cur == null) return;
            Preset c = cur.Clone();
            c.Key = _config.MakeUniqueKey(cur.Key);
            c.Name = cur.Name + " 副本";
            _config.Presets.Insert(_config.Presets.IndexOf(cur) + 1, c);
            MarkDirty();
            LoadPresets(_config.Presets.IndexOf(c));
        }

        void OnDeletePreset(object sender, RoutedEventArgs e)
        {
            Preset cur = CurrentPreset;
            if (cur == null) return;
            if (_config.Presets.Count <= 1)
            {
                MessageBox.Show(this, "至少要保留一个预设。", AppInfo.Title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show(this, "确定删除预设「" + cur.Name + "」？", AppInfo.Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            int i = _config.Presets.IndexOf(cur);
            _config.Presets.Remove(cur);
            if (string.Equals(_config.RestoreKey, cur.Key, StringComparison.OrdinalIgnoreCase)) _config.RestoreKey = "";
            MarkDirty();
            LoadPresets(Math.Max(0, i - 1));
        }

        void OnMoveUp(object sender, RoutedEventArgs e) { MovePreset(-1); }
        void OnMoveDown(object sender, RoutedEventArgs e) { MovePreset(1); }

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
            LoadPresets(j);
        }

        // ==================================================================
        // 辅助对话框（第 3 轮已全部迁到 WPF，UI 进程不再依赖 WinForms）
        // ==================================================================


        void OnPickProcesses(object sender, RoutedEventArgs e)
        {
            Preset p = CurrentPreset;
            if (p == null) return;

            var dlg = new ProcessPickerWindow(p.ProcessNames);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                p.ProcessNames = dlg.SelectedProcessNames;
                _loading = true;
                try { TxtProcess.Text = p.ProcessList; }
                finally { _loading = false; }
                MarkDirty();
                RefreshRow(p);
                SetStatus(p.ProcessNames.Length > 0
                    ? "已关联 " + p.ProcessNames.Length + " 个程序：" + p.ProcessList
                    : "已清空关联程序（本预设将不再被自动切换触发）。");
            }
        }

        void OnClearProcesses(object sender, RoutedEventArgs e)
        {
            Preset p = CurrentPreset;
            if (p == null) return;
            p.ProcessNames = new string[0];
            _loading = true;
            try { TxtProcess.Text = ""; }
            finally { _loading = false; }
            MarkDirty();
            RefreshRow(p);
            SetStatus("已清空关联程序（本预设将不再被自动切换触发）。");
        }

        void OnCreateCustomResolution(object sender, RoutedEventArgs e)
        {
            // 通用版（不带 NVAPI）里根本没有这个窗口，直接说明原因。
            // 用编译符号判断，因为通用版压根不编译 CustomResolutionWindow。
            if (!GpuExtensions.SupportsCustomResolution)
            {
                MessageBox.Show(this,
                    GpuExtensions.UnavailableReason("创建自定义分辨率"),
                    AppInfo.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!Switcher.NvapiReady)
            {
                MessageBox.Show(this,
                    "创建自定义分辨率是 NVIDIA 驱动专有的功能（通过 NVAPI），当前不可用：\n"
                    + Switcher.NvapiError
                    + "\n\n注意：这只是“自定义分辨率”这一项不可用，"
                    + "切换分辨率和缩放方式在 AMD / Intel 显卡上照常可用。",
                    AppInfo.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

#if WITH_NVAPI
            int w = ParseInt(TxtW.Text), h = ParseInt(TxtH.Text);
            if (w < Preset.MinDimension) w = 1920;
            if (h < Preset.MinDimension) h = 1080;

            var dlg = new CustomResolutionWindow(w, h);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                RefreshResolutionCombo();
                ChkFromCurrent(dlg.ResultWidth, dlg.ResultHeight);
                RefreshEnvironment();
                SetStatus("自定义分辨率 " + dlg.ResultWidth + "x" + dlg.ResultHeight + " 已写入驱动，现在可以直接应用或保存。");
            }
#endif
        }

        void OnAbout(object sender, RoutedEventArgs e)
        {
            var w = new AboutWindow();
            w.Owner = this;
            w.ShowDialog();
        }

        void OnRescan(object sender, RoutedEventArgs e)
        {
            try { Switcher.TryInitNvapi(); }
            catch { }
            RefreshEnvironment();
            RefreshResolutionCombo();
            SetStatus("已重新检测显卡与显示模式。");
        }

        // ==================================================================
        // 保存 / 应用 / 重载
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
                Switcher.Log("设置界面(WPF)：已保存配置到 " + _config.FilePath + "（" + _config.Presets.Count + " 个预设）");
                LoadPresets(PresetList.SelectedIndex);
                if (showStatus) SetStatus("已保存到 " + _config.FilePath + "（托盘程序会自动读取新配置）");
                return true;
            }
            catch (Exception ex)
            {
                Switcher.Log("设置界面(WPF)：保存失败 " + ex.Message);
                MessageBox.Show(this, "保存失败：" + ex.Message, AppInfo.Title,
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Cursor = System.Windows.Input.Cursors.Wait;
            string msg;
            bool ok;
            try { ok = Switcher.Apply(p, out msg); }
            finally { Cursor = System.Windows.Input.Cursors.Arrow; }

            RefreshEnvironment();
            if (ok) SetStatus((afterSave ? "[已保存并应用] " : "[已应用] ") + msg);
            else
            {
                SetStatus("[失败] " + msg);
                MessageBox.Show(this, msg, AppInfo.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void OnSave(object sender, RoutedEventArgs e) { SaveConfig(true); }
        void OnApplyOnly(object sender, RoutedEventArgs e) { ApplyCurrent(false); }
        void OnSaveAndApply(object sender, RoutedEventArgs e) { if (SaveConfig(false)) ApplyCurrent(true); }

        void OnReload(object sender, RoutedEventArgs e)
        {
            if (_dirty && MessageBox.Show(this, "将丢弃尚未保存的改动，重新从配置文件读取。继续？", AppInfo.Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
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
            LoadPresets(0);
            RefreshEnvironment();
            SetStatus(fresh.Warnings.Count > 0 ? string.Join("；", fresh.Warnings.ToArray()) : "已重新载入配置文件。");
        }

        void OnClose(object sender, RoutedEventArgs e) { Close(); }

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (!_dirty)
            {
                Switcher.Log("设置界面(WPF)：关闭（无未保存改动）");
                return;
            }
            Switcher.Log("设置界面(WPF)：关闭时有未保存改动，弹出确认框");
            MessageBoxResult r = MessageBox.Show(this, "还有改动没保存，要先保存吗？", AppInfo.Title,
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            Switcher.Log("设置界面(WPF)：用户选择 " + r);
            if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (r == MessageBoxResult.Yes && !SaveConfig(true)) e.Cancel = true;
        }
    }
}
