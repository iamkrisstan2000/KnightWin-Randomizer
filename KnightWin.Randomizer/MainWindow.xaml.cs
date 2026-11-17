using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KnightWin.Core;
using KnightWin.Interop;
using KnightWin.Theme;

namespace KnightWin
{
    public partial class MainWindow : Window
    {
        // ---------------------------------------------------------------- state
        private readonly ObservableCollection<RenameEngine.Item> _items = new();
        private readonly List<string> _tokens = new();
        private RenameMode _mode = RenameMode.TotalRandom;
        private bool _suppress;
        private bool _tokensDirty = true;
        private bool _navCollapsed;
        private string _selectedAccent = "System";
        private readonly DispatcherTimer _ramTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly Process _currentProcess = Process.GetCurrentProcess();
        private readonly ulong _totalPhysicalRam = ReadTotalPhysicalRam();
        private bool _memoryAsPercent;

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
            public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

        private static ulong ReadTotalPhysicalRam()
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            try { return GlobalMemoryStatusEx(ref status) ? status.TotalPhysical : 0; }
            catch { return 0; }
        }

        private const double NavExpandedWidth = 220;
        private const double NavCollapsedWidth = 52;

        public MainWindow()
        {
            InitializeComponent();
            _ramTimer.Tick += (_, _) => UpdateMemoryLine();
            FileList.ItemsSource = _items;
            ReviewList.ItemsSource = _items;
            _items.CollectionChanged += (_, _) =>
            {
                UpdateEmptyState();
                RefreshPreview();
            };

            // The backdrop decides how the window is created, so it is chosen before the handle
            // exists: a layered window cannot have a DWM acrylic material behind it, so on Win11
            // 22H2+ the window stays non-layered and lets the compositor supply the frost. The
            // layered path (per-pixel alpha + blur-behind) is kept for Win10 and older Win11.
            if (WindowFx.SystemAcrylicAvailable) AllowsTransparency = false;

            WindowFx.HookMaximizeFix(this);
            SourceInitialized += (_, _) =>
            {
                // WPF normally clears its NON-layered HWND redirection surface to opaque
                // black. A 95%-white brush over that clear becomes flat RGB(232,232,233)
                // (the user's Windows screenshot) rather than exposing any acrylic. This
                // must be set on the HwndTarget BEFORE applying the DWM material. Window's
                // Background="Transparent" alone does NOT change the target clear colour.
                var hwnd = new WindowInteropHelper(this).Handle;
                var source = HwndSource.FromHwnd(hwnd);
                if (source?.CompositionTarget != null)
                    source.CompositionTarget.BackgroundColor = Colors.Transparent;
                ApplyBackdrop();
            };
            StateChanged += (_, _) => { UpdateChromeForState(); ReassertFrame(); };
            Activated += (_, _) => ReassertFrame();

            // stamp the real assembly version into the title bar - no hard-coded badge
            var ver = typeof(MainWindow).Assembly.GetName().Version;
            if (VersionBadge != null && ver != null)
                VersionBadge.Text = $"v{ver.Major}.{ver.Minor}.{ver.Build}";

            LoadSettingsIntoUi();
            Width = Math.Max(900, App.Settings.WindowWidth);
            Height = Math.Max(620, App.Settings.WindowHeight);

            Loaded += (_, _) =>
            {
                var args = Environment.GetCommandLineArgs();
                if (args.Length > 1)
                {
                    var paths = args.Skip(1).Where(a => !a.StartsWith("-")).ToArray();
                    if (paths.Length > 0) AddPaths(paths);
                }
                RefreshPreview();
                RefreshBackdrop();
                if (PageSettings.Visibility == Visibility.Visible) _ramTimer.Start();
                else StartPreviewGlow();
                // DWM can paint the frame line as soon as the window is on screen: settle it.
                ReassertFrame();
                Dispatcher.BeginInvoke(new Action(ReassertFrame),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (App.Settings.NavCollapsed) SetNavCollapsed(true, animate: false);
            };

            PreviewKeyDown += OnKeyDown;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _ramTimer.Stop();
            _queueDebounce?.Stop();
            StopPreviewGlow();
            _currentProcess.Dispose();
            if (WindowState == WindowState.Normal)
            {
                App.Settings.WindowWidth = Width;
                App.Settings.WindowHeight = Height;
            }
            App.Settings.Save();
            base.OnClosing(e);
        }

        // ---------------------------------------------------------------- chrome
        public void RefreshBackdrop()
        {
            ApplyBackdrop();
            var acc = ThemeManager.ResolveAccent(App.Settings.AccentKey);
            Wheel.Accent = acc.Base;
            Wheel.HubBrush = (Brush)FindResource("PaneBgSolid");
        }

        // The OS can flip while System mode is selected; update the conditional
        // Black/White swatch label as well as its underlying accent resource.
        public void RefreshAccentChoices() => BuildAccentList(App.Settings.AccentKey);

        private void ApplyBackdrop()
        {
            bool dark = ThemeManager.CurrentIsDark;
            var tint = dark ? ThemeManager.Hex("#171719") : ThemeManager.Hex("#F4F4F6");

            bool acrylicEngaged = false;
            if (WindowFx.SystemAcrylicAvailable)
            {
                // Win11 22H2+: DWM paints the real acrylic material behind a *non-layered* window,
                // and per-pixel compositing lets the desktop show through the 12% / 5% surface.
                WindowFx.ApplySystemAcrylic(this, dark);
                acrylicEngaged = WindowFx.LastBackdrop == "system-acrylic";
            }
            else
            {
                // Fallback (Win10 or older Win11): layered window + blur-behind, which is the most
                // transparency those builds can give a WPF window.
                WindowFx.ApplyAcrylic(this, tint, dark ? (byte)0x9C : (byte)0xA8, dark);
            }

            // Safety floor: the translucent surface needs a live material behind it.
            // If the acrylic material did not actually engage (read back from DWM, not assumed),
            // draw the root opaque so the content stays readable. Nothing else paints an
            // opaque page rectangle over the confirmed acrylic path.
            if (RootBorder != null)
            {
                RootBorder.SetResourceReference(Border.BackgroundProperty,
                    acrylicEngaged ? "WindowBg" : "WindowBgSolid");
            }

            WindowFx.EnsureNativeFrameRemoved(this);
            WindowFx.ApplyRoundedRegion(this, 14);
        }

        /// <summary>
        /// Re-asserts the frame after the window has been shown or restored. DWM can repaint the
        /// frame line when a window is deactivated, maximised or restored, so the attributes are
        /// written again at those moments rather than only once at startup.
        /// </summary>
        private void ReassertFrame()
        {
            ApplyBackdrop();
            try
            {
                var diagnostic = WindowFx.DescribeFrame(this);
                // The live DWM state is visible without digging into AppData: hover the badge.
                if (VersionBadge != null) VersionBadge.ToolTip = diagnostic;
                var line = $"[{DateTime.Now:HH:mm:ss}] {diagnostic}{Environment.NewLine}";
                File.AppendAllText(Path.Combine(AppSettings.Folder, "frame.txt"), line);
            }
            catch { }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            // A region is measured in pixels, so it must be rebuilt whenever the window resizes.
            if (IsLoaded) WindowFx.ApplyRoundedRegion(this, 14);
        }

        private void UpdateChromeForState()
        {
            MaxIcon.Data = (Geometry)FindResource(
                WindowState == WindowState.Maximized ? "GeoRestore" : "GeoMax");
            // Chrome is borderless in both states: the frame line is removed in WindowFx,
            // so nothing here should reintroduce a 1px outline when restored.
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }
            WindowFx.BeginDragMove(this);
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
        private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaxBtn_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        // ---------------------------------------------------------------- navigation
        private void NavToggle_Click(object sender, RoutedEventArgs e) => SetNavCollapsed(!_navCollapsed);

        private void SetNavCollapsed(bool collapsed, bool animate = true)
        {
            _navCollapsed = collapsed;
            App.Settings.NavCollapsed = collapsed;
            App.Settings.Save();

            double target = collapsed ? NavCollapsedWidth : NavExpandedWidth;
            if (animate)
            {
                var anim = new DoubleAnimation(NavPane.Width, target, new Duration(TimeSpan.FromMilliseconds(180)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                NavPane.BeginAnimation(WidthProperty, anim);
            }
            else
            {
                NavPane.BeginAnimation(WidthProperty, null);
                NavPane.Width = target;
            }

            // PageSurface fills Grid.Column=1; Grid.Column=0 tracks NavPane.Width. No manual
            // margin or chrome width is needed when the rail animates.

            var vis = collapsed ? Visibility.Collapsed : Visibility.Visible;
            NavHeader.Visibility = vis;
            LblRandomize.Visibility = vis;
            LblSettings.Visibility = vis;

            // Rail geometry.
            //   expanded : 8px outer margin, 42px item, 12px label inset (Win11 NavigationView metrics)
            //   collapsed: item shrinks to a 40x36 chip centred in the 52px rail, so the glyph sits
            //              on the rail's centre line instead of hugging the left edge
            double chipX = Math.Max(0, (NavCollapsedWidth - 40) / 2);
            foreach (var rb in new[] { NavRandomize, NavSettings })
            {
                rb.Margin = collapsed ? new Thickness(chipX, 2, chipX, 2) : new Thickness(8, 1, 8, 1);
                if (rb.Content is not StackPanel sp) continue;
                sp.Margin = collapsed ? new Thickness(0) : new Thickness(12, 0, 0, 0);
                sp.HorizontalAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                sp.VerticalAlignment = VerticalAlignment.Center;
                var pip = FindPip(rb);
                if (pip != null) pip.Opacity = collapsed ? 0 : (rb.IsChecked == true ? 1 : 0);
            }
            if (NavBottomRule != null)
                NavBottomRule.Margin = collapsed ? new Thickness(8, 0, 8, 6) : new Thickness(12, 0, 12, 6);
            // Always show the screenshot's three-line menu glyph, even when collapsed.
            if (NavToggleIcon != null)
                NavToggleIcon.Data = (Geometry)FindResource("GeoMenu");

        }

        /// <summary>Finds the selection indicator inside a nav item template.</summary>
        private static Border? FindPip(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Border b && b.Name == "Pip") return b;
                var found = FindPip(child);
                if (found != null) return found;
            }
            return null;
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb || PageRandomize == null) return;
            string page = rb.Tag?.ToString() ?? "randomize";

            PageRandomize.Visibility = page == "randomize" ? Visibility.Visible : Visibility.Collapsed;
            PageSettings.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;

            if (page == "settings")
            {
                RefreshAboutDetails();
                if (IsLoaded)
                {
                    StopPreviewGlow();
                    _ramTimer.Start();
                }
            }
            else
            {
                _ramTimer.Stop();
                if (IsLoaded) StartPreviewGlow();
                UpdateEmptyState();
            }

            // collapsed rail: a flyout-style tooltip keeps the destination discoverable
            if (_navCollapsed) SetNavCollapsed(true, animate: false);
        }

        // ---------------------------------------------------------------- settings: load / apply
        private void LoadSettingsIntoUi()
        {
            _suppress = true;
            var s = App.Settings;

            LenSlider.Value = Math.Clamp(s.Length, 6, 16);
            LenValue.Text = ((int)LenSlider.Value).ToString();
            PrefixBox.Text = s.CustomPrefix;
            SelectByTag(SepBox, s.Separator);
            SelectByTag(DateFmt, s.DateFormat);
            FolderMode.IsChecked = s.ExpandFolders;
            SubfolderMode.IsChecked = s.FolderRecursive;
            UpdateSubfolderRow();
            SyncChips();

            // appearance
            ModeSystem.IsChecked = s.AppMode == "System";
            ModeLight.IsChecked = s.AppMode == "Light";
            ModeDark.IsChecked = s.AppMode == "Dark";
            BuildAccentList(s.AccentKey);

            // advanced
            AdvLength.Value = Math.Clamp(s.Length, 6, 16);
            LenBadge.Text = $"{(int)AdvLength.Value} chars";
            CkLower.IsChecked = s.UseLower;
            CkUpper.IsChecked = s.UseUpper;
            CkDigits.IsChecked = s.UseDigits;
            CkSymbols.IsChecked = s.UseSymbols;
            CkAvoid.IsChecked = s.AvoidLookAlikes;
            CkClipboard.IsChecked = s.CopyToClipboard;
            UpdateEntropy();

            _suppress = false;
        }

        private static void SelectByTag(ComboBox box, string? tag)
        {
            foreach (var o in box.Items)
            {
                if (o is ComboBoxItem ci && string.Equals(ci.Tag as string, tag, StringComparison.Ordinal))
                {
                    box.SelectedItem = ci;
                    return;
                }
            }
            if (box.Items.Count > 0) box.SelectedIndex = 0;
        }

        private static string TagOf(ComboBox box) =>
            box.SelectedItem is ComboBoxItem ci ? (ci.Tag as string ?? "") : "";

        public sealed class AccentItem
        {
            public string Key { get; init; } = "";
            public string Label { get; init; } = "";
            public Brush PreviewBrush { get; init; } = Brushes.Transparent;
            public Brush ForegroundBrush { get; init; } = Brushes.White;
            public Brush RingBrush { get; init; } = Brushes.Transparent;
            public double TickOpacity { get; init; }
        }

        private void BuildAccentList(string selected)
        {
            _selectedAccent = string.IsNullOrEmpty(selected) ? "System" : selected;
            var list = new List<AccentItem>();
            var sys = ThemeManager.ResolveAccent("System");
            var accentBrush = (Brush)FindResource("Accent");
            var none = Brushes.Transparent;

            list.Add(new AccentItem
            {
                Key = "System",
                Label = "System",
                PreviewBrush = ThemeManager.MakeBrush(sys.Base),
                ForegroundBrush = ThemeManager.MakeBrush(sys.Text),
                RingBrush = _selectedAccent == "System" ? accentBrush : none,
                TickOpacity = _selectedAccent == "System" ? 1 : 0
            });
            foreach (var item in ThemeManager.Accents)
            {
                // Monochrome switches both its swatch and its name with the theme.
                var a = ThemeManager.ResolveAccent(item.Key);
                list.Add(new AccentItem
                {
                    Key = a.Key,
                    Label = a.Label,
                    PreviewBrush = ThemeManager.MakeBrush(a.Base),
                    ForegroundBrush = ThemeManager.MakeBrush(a.Text),
                    RingBrush = _selectedAccent == a.Key ? accentBrush : none,
                    TickOpacity = _selectedAccent == a.Key ? 1 : 0
                });
            }
            AccentList.ItemsSource = list;
        }

        private void AppMode_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppress || sender is not RadioButton rb) return;
            App.Settings.AppMode = rb.Tag?.ToString() ?? "System";
            App.Settings.Save();
            ApplyTheme();
        }

        private void Accent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string key) return;
            App.Settings.AccentKey = key;
            App.Settings.Save();
            ApplyTheme();
            BuildAccentList(key);
        }

        private void ApplyTheme()
        {
            var mode = App.Settings.AppMode switch
            {
                "Light" => AppMode.Light,
                "Dark" => AppMode.Dark,
                _ => AppMode.System
            };
            ThemeManager.Apply(mode, App.Settings.AccentKey);
            RefreshBackdrop();
            SyncChips();
            BuildAccentList(App.Settings.AccentKey);
        }

        private void AdvLength_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LenBadge == null || LenSlider == null) return;
            int v = (int)Math.Round(e.NewValue);
            LenBadge.Text = $"{v} chars";
            if (_suppress) return;
            App.Settings.Length = v;
            App.Settings.Save();
            _suppress = true;
            LenSlider.Value = v;
            LenValue.Text = v.ToString();
            _suppress = false;
            UpdateEntropy();
            InvalidatePlan();
            RefreshPreview();
        }

        private void Set_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppress || CkLower == null) return;
            var s = App.Settings;
            s.UseLower = CkLower.IsChecked == true;
            s.UseUpper = CkUpper.IsChecked == true;
            s.UseDigits = CkDigits.IsChecked == true;
            s.UseSymbols = CkSymbols.IsChecked == true;
            s.AvoidLookAlikes = CkAvoid.IsChecked == true;
            s.CopyToClipboard = CkClipboard.IsChecked == true;

            if (!s.UseLower && !s.UseUpper && !s.UseDigits && !s.UseSymbols)
            {
                _suppress = true;
                CkLower.IsChecked = true;
                s.UseLower = true;
                _suppress = false;
            }

            s.Save();
            UpdateEntropy();
            SyncChips();
            InvalidatePlan();
            RefreshPreview();
        }

        private void UpdateEntropy()
        {
            if (EntropyText == null) return;
            int pool = RenameEngine.BuildPool(App.Settings).Length;
            int len = App.Settings.Length;
            double bits = pool > 1 ? Math.Log2(pool) * len : 0;
            EntropyText.Text = $"Alphabet of {pool} characters, {len} per name, about {bits:0} bits of entropy. " +
                               "Every batch is still checked against the folder, so no file is ever overwritten.";
        }

        private const string ProjectUrl = "https://github.com/iamkrisstan2000/KnightWin-Randomizer";

        private void AboutLogo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Process.Start(new ProcessStartInfo(ProjectUrl) { UseShellExecute = true }) is null)
                    throw new InvalidOperationException("No default web browser is configured.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not open the project page.\n{ProjectUrl}\n\n{ex.Message}",
                                "KnightWin Randomizer", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void RefreshAboutDetails()
        {
            if (VersionLine == null) return;
            var v = typeof(MainWindow).Assembly.GetName().Version;
            VersionLine.Text = $"Version {v?.ToString(3)} | native WPF | x64";
            if (AcrylicDiagnostic != null)
                AcrylicDiagnostic.Text = WindowFx.DescribeFrame(this);
            RuntimeLine.Text = RuntimeInformation.FrameworkDescription;
            UpdateMemoryLine();
        }

        private void UpdateMemoryLine()
        {
            if (MemoryLine == null) return;
            try
            {
                _currentProcess.Refresh();
                long bytes = _currentProcess.WorkingSet64;
                bool percentage = _memoryAsPercent && _totalPhysicalRam > 0;
                MemoryLine.Text = percentage
                    ? $"{100.0 * bytes / _totalPhysicalRam:0.0}% RAM"
                    : $"{bytes / (1024.0 * 1024):0} MB RAM";
                MemoryLine.ToolTip = _totalPhysicalRam > 0
                    ? "Live app working set. % is the app's share of total physical RAM. Double-click to toggle MB / %."
                    : "Live app working set. Total physical RAM unavailable, so % cannot be shown.";
            }
            catch { MemoryLine.Text = "RAM unavailable"; }
        }

        private void MemoryChip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            _memoryAsPercent = !_memoryAsPercent;
            UpdateMemoryLine();
            e.Handled = true;
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            var d = new AppSettings { NavCollapsed = _navCollapsed };
            d.Save();
            App.Settings = d;
            LoadSettingsIntoUi();
            ApplyTheme();
            _tokensDirty = true;
            RefreshPreview();
            SaveHint.Text = "Defaults restored";
        }

        // ---------------------------------------------------------------- mode
        private void Mode_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb) return;
            if (!int.TryParse(rb.Tag?.ToString(), out int modeValue)) return;
            _mode = (RenameMode)modeValue;
            // fired while XAML is still being built: record the mode and bail out
            if (DateBlock == null || CustomBlock == null) return;
            DateBlock.Visibility = _mode == RenameMode.DatePrefix ? Visibility.Visible : Visibility.Collapsed;
            CustomBlock.Visibility = _mode == RenameMode.CustomPrefix ? Visibility.Visible : Visibility.Collapsed;
            InvalidatePlan();
            RefreshPreview();
        }

        private void DateFmt_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppress) return;
            App.Settings.DateFormat = TagOf(DateFmt);
            App.Settings.Save();
            InvalidatePlan();
            RefreshPreview();
        }

        private void SepBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppress) return;
            App.Settings.Separator = TagOf(SepBox);
            App.Settings.Save();
            InvalidatePlan();
            RefreshPreview();
        }

        private void PrefixBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppress) return;
            App.Settings.CustomPrefix = PrefixBox.Text;
            RefreshPreview();
        }

        private void LenSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LenValue == null) return;
            int v = (int)Math.Round(e.NewValue);
            LenValue.Text = v.ToString();
            if (_suppress) return;
            App.Settings.Length = v;
            App.Settings.Save();
            _suppress = true;
            if (AdvLength != null) AdvLength.Value = v;
            if (LenBadge != null) LenBadge.Text = $"{v} chars";
            _suppress = false;
            UpdateEntropy();
            InvalidatePlan();
            RefreshPreview();
        }

        private void FolderMode_Changed(object sender, RoutedEventArgs e)
        {
            if (FolderMode == null) return;
            App.Settings.ExpandFolders = FolderMode.IsChecked == true;
            App.Settings.Save();
            UpdateSubfolderRow();
        }

        private void SubfolderMode_Changed(object sender, RoutedEventArgs e)
        {
            if (SubfolderMode == null) return;
            App.Settings.FolderRecursive = SubfolderMode.IsChecked == true;
            App.Settings.Save();
            UpdateSubfolderRow();
        }

        /// <summary>The subfolder option only makes sense while folder expansion is on.</summary>
        private void UpdateSubfolderRow()
        {
            if (SubfoldersRow == null) return;
            bool on = App.Settings.ExpandFolders;
            SubfoldersRow.IsEnabled = on;
            SubfoldersRow.Opacity = on ? 1.0 : 0.45;
        }

        // ---------------------------------------------------------------- chips
        private void SyncChips()
        {
            if (ChipLower == null) return;
            var s = App.Settings;
            StyleChip(ChipLower, s.UseLower);
            StyleChip(ChipUpper, s.UseUpper);
            StyleChip(ChipDigits, s.UseDigits);
            StyleChip(ChipSymbols, s.UseSymbols);
        }

        private void StyleChip(Button b, bool on)
        {
            if (on)
            {
                b.Background = (Brush)FindResource("AccentSoft");
                b.Foreground = (Brush)FindResource("TextPrimary");
                b.BorderBrush = (Brush)FindResource("Accent");
                b.Opacity = 1;
            }
            else
            {
                b.Background = (Brush)FindResource("FieldBg");
                b.Foreground = (Brush)FindResource("TextTertiary");
                b.BorderBrush = (Brush)FindResource("CardStroke");
                b.Opacity = 0.85;
            }
        }

        private void Chip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string tag) return;
            var s = App.Settings;
            switch (tag)
            {
                case "lower": s.UseLower = !s.UseLower; break;
                case "upper": s.UseUpper = !s.UseUpper; break;
                case "digits": s.UseDigits = !s.UseDigits; break;
                case "symbols": s.UseSymbols = !s.UseSymbols; break;
            }
            if (!s.UseLower && !s.UseUpper && !s.UseDigits && !s.UseSymbols)
            {
                s.UseLower = true;
                Flash("At least one character set stays on");
            }
            s.Save();
            SyncChips();
            _suppress = true;
            CkLower.IsChecked = s.UseLower;
            CkUpper.IsChecked = s.UseUpper;
            CkDigits.IsChecked = s.UseDigits;
            CkSymbols.IsChecked = s.UseSymbols;
            _suppress = false;
            UpdateEntropy();
            InvalidatePlan();
            RefreshPreview();
        }

        // ---------------------------------------------------------------- preview / roll
        private void StartPreviewGlow()
        {
            if (PreviewGlow?.Background is not RadialGradientBrush glow) return;
            var path = new PointAnimationUsingKeyFrames
            {
                Duration = new Duration(TimeSpan.FromSeconds(22)),
                RepeatBehavior = RepeatBehavior.Forever
            };
            // A gentle closed circuit across the card, rather than a distracting pulse.
            var positions = new[]
            {
                (0d, new Point(0.16, 0.35)),
                (5.5, new Point(0.73, 0.25)),
                (11d, new Point(0.86, 0.72)),
                (16.5, new Point(0.29, 0.78)),
                (22d, new Point(0.16, 0.35))
            };
            foreach (var (seconds, point) in positions)
                path.KeyFrames.Add(new EasingPointKeyFrame
                {
                    Value = point,
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds)),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
            glow.BeginAnimation(RadialGradientBrush.CenterProperty, path);
            glow.BeginAnimation(RadialGradientBrush.GradientOriginProperty, path);
        }

        private void StopPreviewGlow()
        {
            if (PreviewGlow?.Background is not RadialGradientBrush glow) return;
            glow.BeginAnimation(RadialGradientBrush.CenterProperty, null);
            glow.BeginAnimation(RadialGradientBrush.GradientOriginProperty, null);
        }

        private void RefreshPreview()
        {
            if (PreviewText == null) return;

            UpdateQueuePreview();

            string shownName;
            string sampleName;
            if (_items.Count > 0)
            {
                // After a batch this is the name that was actually written, not a new roll.
                shownName = _batchDone && _items[0].IsDone
                    ? Path.GetFileName(_items[0].TargetPath)
                    : _items[0].TargetName;
                sampleName = _items[0].SourceName;
            }
            else
            {
                sampleName = "example.txt";   // the extension is always kept
                string token = RenameEngine.RandomToken(RenameEngine.BuildPool(App.Settings),
                                                        Math.Clamp(App.Settings.Length, 6, 16));
                shownName = RenameEngine.ComposeFileName(sampleName, _mode, App.Settings,
                                                         App.Settings.CustomPrefix, token);
            }

            PreviewText.Text = DisplayNameWithoutExtension(shownName);

            // The caption used to spell out PREVIEW / NEXT UP / FINISHED above the name. The name
            // is now the only thing in the field; the queue header carries the state instead.
        }

        private static string DisplayNameWithoutExtension(string name) =>
            Path.GetFileNameWithoutExtension(name);

        /// <summary>
        /// Rolls the wheel for a new name. The Roll button is gone, so this is driven by the
        /// Space key. The wheel is display-only and still animates while renaming a batch.
        /// </summary>
        private void Roll()
        {
            if (Wheel.Spinning) return;
            Wheel.Spin(TimeSpan.FromMilliseconds(700), 5);
            Wheel.SpinCompleted += OnSpinDone;
        }

        private bool _deferPlan;

        private void OnSpinDone(object? sender, EventArgs e)
        {
            Wheel.SpinCompleted -= OnSpinDone;
            _deferPlan = false;      // the spin itself is the fresh plan
            _tokensDirty = true;
            RefreshPreview();
        }

        // ---------------------------------------------------------------- file queue
        private void AddBtn_Click(object sender, RoutedEventArgs e) => PickFiles();

        private void PickFiles()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Add files to rename",
                Multiselect = true,
                Filter = "All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) == true) AddPaths(dlg.FileNames);
        }

        private void AddPaths(IEnumerable<string> paths)
        {
            var existing = new HashSet<string>(_items.Select(i => i.SourcePath), StringComparer.OrdinalIgnoreCase);
            var direct = new List<string>();
            var roots = new List<string>();

            foreach (var p in paths)
            {
                if (Directory.Exists(p)) roots.Add(p);
                else if (File.Exists(p)) direct.Add(p);
            }

            var files = new List<string>(direct);
            int hidden = 0, skipped = 0;
            var errors = new List<string>();
            string? importFeedback = null, importContext = null;

            if (roots.Count > 0)
            {
                if (!App.Settings.ExpandFolders)
                {
                    importFeedback = "Folders are ignored - turn on Expand folders";
                }
                else
                {
                    var scan = FolderScanner.ScanFolders(roots, App.Settings.FolderRecursive, App.Settings.SkipHidden);
                    files.AddRange(scan.Files);
                    hidden = scan.SkippedHidden;
                    skipped = scan.SkippedOther;
                    errors.AddRange(scan.Errors);
                }
            }

            int added = 0;
            foreach (var f in files)
            {
                if (!existing.Add(f)) continue;
                _items.Add(MakeItem(f));
                added++;
            }

            if (added > 0 || hidden > 0 || skipped > 0 || errors.Count > 0)
            {
                var bits = new List<string>();
                if (added > 0) bits.Add($"Added {added} file{(added == 1 ? "" : "s")}");
                if (hidden > 0) bits.Add($"skipped {hidden} hidden");
                if (skipped > 0) bits.Add($"skipped {skipped} system file{(skipped == 1 ? "" : "s")}");
                if (errors.Count > 0) bits.Add($"{errors.Count} unreadable");

                string? sub = roots.Count > 0
                    ? (App.Settings.FolderRecursive
                        ? $"{roots.Count} folder{(roots.Count == 1 ? "" : "s")} scanned, subfolders included"
                        : $"{roots.Count} folder{(roots.Count == 1 ? "" : "s")} scanned, top level only")
                    : null;
                if (added == 0 && bits.Count == 0) bits.Add("Nothing new added");
                importFeedback = string.Join(", ", bits);
                importContext = sub;
            }
            if (added > 0)
            {
                _batchDone = false;   // new files: back to previewing the next batch
                _lastBatch = null;
            }
            RefreshPreview();
            UpdateEmptyState();
            if (importFeedback != null) Status(importFeedback, importContext);
        }

        /// <summary>Builds a queue row, keeping the folder so name clashes stay per-folder.</summary>
        private static RenameEngine.Item MakeItem(string path)
        {
            string dir = Path.GetDirectoryName(path) ?? "";
            return new RenameEngine.Item
            {
                SourcePath = path,
                SourceName = Path.GetFileName(path),
                Folder = dir,
                FolderLabel = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            };
        }

        private void AddFolderBtn_Click(object sender, RoutedEventArgs e) => PickFolder();

        private void PickFolder()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = App.Settings.FolderRecursive
                    ? "Rename every file in a folder and its subfolders"
                    : "Rename every file in a folder",
                Multiselect = false
            };
            if (dlg.ShowDialog(this) == true) AddPaths(new[] { dlg.FolderName });
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            _batchDone = false;
            _lastBatch = null;
            _items.Clear();
            _tokens.Clear();
            _tokensDirty = true;
            Status("Queue cleared");
            RefreshPreview();
        }

        /// <summary>
        /// The review sheet is a detail view of the single queue: the same rows, laid out as
        /// current name -> new name so a whole batch can be checked at once. There is no second
        /// queue anywhere in the app; this only changes how the one queue is presented.
        /// </summary>
        private void ReviewBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ReviewSheet == null) return;
            UpdateQueuePreview();
            RefreshQueueSummary();
            ReviewList.Items.Refresh();
            ReviewSheet.Visibility = Visibility.Visible;
        }

        private void ReviewClose_Click(object sender, RoutedEventArgs e)
        {
            if (ReviewSheet != null) ReviewSheet.Visibility = Visibility.Collapsed;
        }

        /// <summary>Fills the queue header with a plain-language summary.</summary>
        private void RefreshQueueSummary()
        {
            int n = _items.Count;
            int okCount = _items.Count(i => i.IsDone);
            int badCount = _items.Count(i => i.IsFailed);
            if (QueueSub != null && n > 0)
                QueueSub.Text = _batchDone
                    ? (badCount > 0
                        ? $"{okCount} renamed, {badCount} failed"
                        : $"All {okCount} renamed. Nothing was overwritten.")
                    : $"{n} file{(n == 1 ? "" : "s")} ready. Names are reserved, so nothing is overwritten.";
        }

        private void UpdateEmptyState()
        {
            if (EmptyState == null || FileList == null || RenameBtn == null || QueueSub == null) return;
            bool empty = _items.Count == 0;
            EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            FileList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            RenameBtn.IsEnabled = !empty && !_batchDone && !_busy;
            QueueSub.Text = empty
                ? "Drop files here or add them manually"
                : _batchDone
                    ? (_items.Any(i => i.IsFailed)
                        ? $"Finished: {_items.Count(i => i.IsDone)} renamed, {_items.Count(i => i.IsFailed)} failed"
                        : $"Finished: {_items.Count} file{(_items.Count == 1 ? "" : "s")} renamed")
                    : $"{_items.Count} file{(_items.Count == 1 ? "" : "s")} queued, preview is live";
        }

        private System.Windows.Threading.DispatcherTimer? _queueDebounce;

        /// <summary>
        /// Marks the queued names as stale.
        ///
        /// While the wheel is spinning a new name is already on its way, so re-rolling here would
        /// change the names underneath the animation and make the wheel look like it stuttered.
        /// In that case the flag is deferred and the spin's own completion re-plans instead.
        /// </summary>
        private void InvalidatePlan()
        {
            _tokensDirty = true;
            if (Wheel != null && (Wheel.Spinning || Wheel.IsBusy)) _deferPlan = true;
        }

        private void UpdateQueuePreview()
        {
            if (FileList == null) return;

            if (_items.Count > 400)
            {
                _queueDebounce ??= new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(220)
                };
                _queueDebounce.Stop();
                _queueDebounce.Tick -= DebounceTick;
                _queueDebounce.Tick += DebounceTick;
                _queueDebounce.Start();
                return;
            }
            UpdateQueuePreviewCore();
        }

        private void DebounceTick(object? sender, EventArgs e)
        {
            _queueDebounce?.Stop();
            UpdateQueuePreviewCore();
        }

        private void UpdateQueuePreviewCore()
        {
            if (FileList == null) return;
            if (_items.Count == 0) return;

            // A spin is already producing fresh names; re-rolling now would fight the animation.
            if (_deferPlan && Wheel != null && Wheel.Spinning) return;

            // Rows that came out of a finished batch keep their result: never re-roll them.
            if (_items.Any(i => !i.IsPending)) return;

            var pool = RenameEngine.BuildPool(App.Settings);
            int len = Math.Clamp(App.Settings.Length, 6, 16);
            if (_tokensDirty || _tokens.Count != _items.Count)
            {
                _tokens.Clear();
                for (int i = 0; i < _items.Count; i++) _tokens.Add(RenameEngine.RandomToken(pool, len));
                _tokensDirty = false;
            }

            // Uniqueness is per folder: a batch can span a whole folder tree, where the same
            // generated name is perfectly fine in two different directories.
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                string dir = it.Folder.Length > 0 ? it.Folder : (Path.GetDirectoryName(it.SourcePath) ?? "");
                string target = RenameEngine.ComposeFileName(it.SourceName, _mode, App.Settings,
                                                             App.Settings.CustomPrefix, _tokens[i]);
                int guard = 0;
                while (taken.Contains(dir + "\\" + target) && guard++ < 64)
                {
                    _tokens[i] = RenameEngine.RandomToken(pool, len);
                    target = RenameEngine.ComposeFileName(it.SourceName, _mode, App.Settings,
                                                          App.Settings.CustomPrefix, _tokens[i]);
                }
                taken.Add(dir + "\\" + target);
                it.TargetName = target;
                it.TargetPath = Path.Combine(Path.GetDirectoryName(it.SourcePath) ?? "", target);
            }

            FileList.Items.Refresh();
            RefreshQueueSummary();
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            var zone = (Border)sender;
            zone.Background = (Brush)FindResource("AccentSoft");
            zone.BorderBrush = (Brush)FindResource("Accent");
            e.Handled = true;
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            var zone = (Border)sender;
            zone.Background = Brushes.Transparent;
            zone.BorderBrush = Brushes.Transparent;
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            var zone = (Border)sender;
            zone.Background = Brushes.Transparent;
            zone.BorderBrush = Brushes.Transparent;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
        }

        // ---------------------------------------------------------------- rename / undo
        private void RenameBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0 || _busy || _batchDone) return;

            // Snapshot the plan from what is on screen right now.
            var plan = new List<(RenameEngine.Item row, RenameEngine.Item job)>();
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                string token = i < _tokens.Count
                    ? _tokens[i]
                    : RenameEngine.RandomToken(RenameEngine.BuildPool(App.Settings),
                                               Math.Clamp(App.Settings.Length, 6, 16));
                plan.Add((it, new RenameEngine.Item
                {
                    SourcePath = it.SourcePath,
                    SourceName = it.SourceName,
                    Token = token,
                    TargetName = it.TargetName,
                    TargetPath = it.TargetPath
                }));
            }

            _lastBatch = plan;
            _batchStart = DateTime.UtcNow;
            SetBusy(true, "Renaming...");

            // Run the batch off the UI thread so the wheel keeps its frame rate. The spin is a
            // render-thread animation, so the app stays responsive and visibly busy.
            var jobs = plan.Select(p => p.job).ToList();
            var worker = new System.Threading.Tasks.Task<RenameEngine.Result>(
                () => RenameEngine.Execute(jobs));

            worker.ContinueWith(t =>
            {
                RenameEngine.Result? result = t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion
                    ? t.Result : null;
                string? crash = t.Exception?.GetBaseException().Message;

                FinishBatch(plan, result, crash);
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());

            worker.Start();
        }

        private bool _busy;

        /// <summary>True once a batch has run and the rows below are results, not a preview.</summary>
        private bool _batchDone;

        /// <summary>The rows and jobs of the batch that just ran, so Undo can walk them back.</summary>
        private List<(RenameEngine.Item row, RenameEngine.Item job)>? _lastBatch;

        private void UndoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;

            int n = UndoStack.Undo();
            UndoBtn.IsEnabled = UndoStack.LastCount > 0;

            // The rows stay on screen and go back to being a preview of fresh names.
            if (_lastBatch != null)
            {
                foreach (var (row, job) in _lastBatch) row.MarkPending(job.SourcePath);
                _lastBatch = null;
                _batchDone = false;
            }

            _tokensDirty = true;
            Status(n > 0 ? $"Restored {n} file{(n == 1 ? "" : "s")}" : "Nothing to undo",
                   n > 0 ? "Original names are back, the preview shows the next batch" : null);

            UpdateEmptyState();
            RefreshPreview();
        }

        /// <summary>Locks the batch buttons and turns the wheel into a progress indicator.</summary>
        private void SetBusy(bool busy, string? caption = null)
        {
            _busy = busy;

            if (RenameBtn != null) RenameBtn.IsEnabled = !busy && _items.Count > 0 && !_batchDone;
            if (AddBtn != null) AddBtn.IsEnabled = !busy;
            if (AddFolderBtn != null) AddFolderBtn.IsEnabled = !busy;
            if (ClearBtn != null) ClearBtn.IsEnabled = !busy;
            if (UndoBtn != null) UndoBtn.IsEnabled = !busy && UndoStack.LastCount > 0;

            if (busy)
            {
                WheelStartBusy();
                Status(caption ?? "Working...", "The wheel keeps turning until every file is done");
            }
            else
            {
                WheelStopBusy();
            }
        }

        private void WheelStartBusy()
        {
            if (Wheel == null) return;
            Wheel.StartBusySpin(1.1);
        }

        private void WheelStopBusy()
        {
            if (Wheel == null) { return; }
            Wheel.StopBusySpin(TimeSpan.FromMilliseconds(520), () =>
            {
                if (!_busy) return;
                _busy = false;
                if (RenameBtn != null) RenameBtn.IsEnabled = _items.Count > 0 && !_batchDone;
                if (AddBtn != null) AddBtn.IsEnabled = true;
                if (AddFolderBtn != null) AddFolderBtn.IsEnabled = true;
                if (ClearBtn != null) ClearBtn.IsEnabled = true;
                if (UndoBtn != null) UndoBtn.IsEnabled = UndoStack.LastCount > 0;
            });
        }

        private DateTime _batchStart;

        /// <summary>
        /// How long the busy state is shown at minimum. A folder of 900 files is renamed in well
        /// under a third of a second, which would make the wheel's "something is happening" spin
        /// invisible. Holding it briefly means the indicator is always perceptible, and the glide
        /// that ends it always plays in full.
        /// </summary>
        private static readonly TimeSpan MinBusy = TimeSpan.FromMilliseconds(650);

        /// <summary>
        /// Applies the outcome to the rows and leaves them on screen, each marked with a tick or a
        /// cross so a finished batch is readable instead of the list vanishing.
        /// </summary>
        private void FinishBatch(List<(RenameEngine.Item row, RenameEngine.Item job)> plan,
                                 RenameEngine.Result? result, string? crash)
        {
            TimeSpan elapsed = DateTime.UtcNow - _batchStart;
            if (elapsed < MinBusy)
            {
                // Hold the spin for the remainder, then apply the results.
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = MinBusy - elapsed
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    FinishBatchCore(plan, result, crash);
                };
                timer.Start();
                return;
            }
            FinishBatchCore(plan, result, crash);
        }

        private void FinishBatchCore(List<(RenameEngine.Item row, RenameEngine.Item job)> plan,
                                     RenameEngine.Result? result, string? crash)
        {
            int ok = 0, failed = 0;

            foreach (var (row, job) in plan)
            {
                if (result != null && result.RenamedTo.TryGetValue(job.SourcePath, out string? newPath))
                {
                    row.MarkDone(newPath);
                    ok++;
                }
                else
                {
                    string why = crash
                        ?? (result != null && result.Failures.TryGetValue(job.SourcePath, out string? f) ? f : null)
                        ?? "not renamed";
                    row.MarkFailed(why);
                    failed++;
                }
            }

            UndoBtn.IsEnabled = UndoStack.LastCount > 0;

            if (App.Settings.CopyToClipboard && ok > 0)
            {
                try { Clipboard.SetText(string.Join(Environment.NewLine, plan.Select(p => p.row.TargetName))); }
                catch { }
            }

            string msg = failed == 0
                ? $"Renamed {ok} file{(ok == 1 ? "" : "s")}"
                : $"Renamed {ok}, {failed} failed";
            string sub = failed == 0
                ? "Every row is ticked. Undo is available."
                : (crash ?? string.Join("  |  ", plan.Where(p => p.row.IsFailed).Take(2)
                        .Select(p => $"{p.row.SourceName}: {p.row.Error}")));

            _batchDone = true;
            Status(msg, sub);
            WheelStopBusy();

            // The rows are results now, not a plan: keep them visible and re-enable adding.
            UpdateEmptyState();
            RefreshPreview();
        }

        // ---------------------------------------------------------------- status
        // The queue footer is actions only. Feedback belongs in the existing queue header.
        private void Status(string text, string? sub = null)
        {
            if (QueueSub == null) return;
            QueueSub.Text = text;
            QueueSub.ToolTip = sub;
        }

        private void Flash(string text) => Status(text);

        // ---------------------------------------------------------------- keyboard
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (ctrl && e.Key == Key.O)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) PickFolder(); else PickFiles();
                e.Handled = true;
            }
            else if (ctrl && e.Key == Key.R) { RenameBtn_Click(sender, e); e.Handled = true; }
            else if (ctrl && e.Key == Key.OemComma) { NavSettings.IsChecked = true; e.Handled = true; }
            else if (ctrl && e.Key == Key.Z) { if (UndoBtn.IsEnabled) UndoBtn_Click(sender, e); e.Handled = true; }
            else if (e.Key == Key.Space && Keyboard.FocusedElement is not TextBox
                     && Keyboard.FocusedElement is not ButtonBase) { Roll(); e.Handled = true; }
            else if (ctrl && e.Key == Key.B) { SetNavCollapsed(!_navCollapsed); e.Handled = true; }
        }
    }
}
