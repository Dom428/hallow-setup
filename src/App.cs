// Hallow Setup - one window to install, update and start-with-Windows the Hallow family:
// Mixer, Hub, Vault, Overlay, Remote, Clips, Keys, Assist, Guard, Tasks, Browser and Toolkit. Nothing is downloaded until you click Install or Update.
// v1.3: Uninstall per app (Uninstall.cs, UninstallDialog.cs).
// v1.4: Hallow Assist in the list.
// v1.5: Hallow Guard in the list.
// v1.6: Hallow Shield (browser extension) in the list: install / update its unpacked folder, open the browsers'
//       extension pages when you click, uninstall to the Recycle Bin (Shield.cs).
// v1.8: the window fits the screen: never taller than the monitor's working area and kept on it, the app list scrolls,
//       and the Shield steps scroll into view when they open. Shield 1.6+: its Hallow family bridge is registered
//       (per user) after Shield is unpacked (Shield.cs).
// v1.10: light and dark mode - follows the mode picked in Hallow Toolkit (theme.json), or Setup's own Look (Follow Hallow /
//       Light / Dark, at the bottom right); changes live (Look.cs).
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

[assembly: AssemblyTitle("Hallow Setup")]
[assembly: AssemblyProduct("Hallow Setup")]
[assembly: AssemblyDescription("Install and update the Hallow apps")]
[assembly: AssemblyVersion("1.12.0.0")]
[assembly: AssemblyFileVersion("1.12.0.0")]

namespace HallowSetup
{
    public static class Program
    {
        public const string Version = "1.12";
        public static string AccentHex = "#F07820";
        static string ThemeXaml;
        // v1.10: Theme.xaml in light or dark with the Hallow accent picked in Hallow Toolkit (if any)
        static ResourceDictionary LoadTheme(bool light)
        {
            Look.Light = light; AccentHex = Setup.Accent() ?? "#F07820";
            return (ResourceDictionary)XamlReader.Parse(Look.Theme(ThemeXaml, AccentHex, light));
        }
        public static void ApplyTheme(bool light)
        {
            var md = Application.Current.Resources.MergedDictionaries; var d = LoadTheme(light);
            if (md.Count > 0) md[0] = d; else md.Add(d);
        }
        [STAThread]
        public static void Main(string[] args)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            string xaml;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("HallowSetup.Theme.xaml")) using (var r = new System.IO.StreamReader(s)) xaml = r.ReadToEnd();
            ThemeXaml = xaml;
            app.Resources.MergedDictionaries.Add(LoadTheme(Look.CurrentLight()));   // v1.10: light or dark, and the Hallow accent
            int ti = Array.IndexOf(args, "--test-install");   // developer tests only (needs the HALLOW_SETUP_API stand-in): install one app, write the result to a file
            if (ti >= 0 && ti + 2 < args.Length && Setup.TestMode)
            {
                var a = Setup.Apps.FirstOrDefault(x => string.Equals(x.Name, args[ti + 1], StringComparison.OrdinalIgnoreCase)); string res;
                try { res = a == null ? "no such app" : Setup.Install(a, p => { }); } catch (Exception e) { res = "Install failed: " + e.Message; }
                System.IO.File.WriteAllText(args[ti + 2], res); return;
            }
            int mi = Array.IndexOf(args, "--test-install-all");   // developer tests only: install several, one after another
            if (mi >= 0 && mi + 2 < args.Length && Setup.TestMode)
            {
                var names = args[mi + 1].Split(',');
                var list = Setup.Apps.Where(x => names.Any(n => string.Equals(n, x.Name, StringComparison.OrdinalIgnoreCase))).ToList();
                System.IO.File.WriteAllLines(args[mi + 2], Setup.InstallMany(list, p => { })); return;
            }
            int ui = Array.IndexOf(args, "--test-selfupdate");   // developer tests only
            if (ui >= 0 && ui + 1 < args.Length && Setup.TestMode)
            {
                Setup.CheckSelf(); string r;
                try { r = "newer=" + Setup.SelfNewer() + " " + (Setup.SelfNewer() ? Setup.UpdateSelf(p => { }) ?? "started" : "-"); } catch (Exception e) { r = "failed: " + e.Message; }
                System.IO.File.WriteAllText(args[ui + 1], r); return;
            }
            int xi = Array.IndexOf(args, "--test-uninstall");   // developer tests only (all the test folders must be set): uninstall <app> <data 0/1> <force 0/1> <out>
            if (xi >= 0 && xi + 4 < args.Length && Setup.TestMode)
            {
                var a = Setup.Apps.FirstOrDefault(x => string.Equals(x.Name, args[xi + 1], StringComparison.OrdinalIgnoreCase)); string res;
                try { var r = a == null ? null : Uninstaller.Uninstall(a, args[xi + 2] == "1", args[xi + 3] == "1", p => { }); res = r == null ? "no such app" : (r.StillRunning ? "STILLRUNNING " : r.Ok ? "OK " : "FAIL ") + r.Message; }
                catch (Exception e) { res = "EXC " + e.Message; }
                System.IO.File.WriteAllText(args[xi + 4], res); return;
            }
            // v1.6 developer tests only (need the HALLOW_SETUP_API stand-in and HALLOW_SETUP_LOCAL): Hallow Shield
            int hi = Array.IndexOf(args, "--test-shield");   // --test-shield <chrome|firefox|versions|uninstall|open> <out>
            if (hi >= 0 && hi + 2 < args.Length && Setup.TestMode)
            {
                string res; var what = args[hi + 1];
                try
                {
                    if (what == "versions") { Shield.CheckLatest(); res = "latest=" + Setup.Show(Shield.Latest) + " installed=" + Setup.Show(Shield.InstalledVersion("chrome")) + " update=" + Shield.UpdateAvailable("chrome"); }
                    else if (what == "uninstall") res = Shield.Uninstall("chrome");
                    else if (what == "open") { string copied = null; res = string.Join("\n", Shield.Browsers.Select(b => Shield.OpenExtensions(b, t => copied = t, false) + " | copied=" + copied)); }
                    else res = Shield.Install(what, p => { });
                }
                catch (Exception e) { res = "EXC " + e.Message; }
                System.IO.File.WriteAllText(args[hi + 2], res); return;
            }
            int si = Array.IndexOf(args, "--snap");
            if (si >= 0 && si + 1 < args.Length)   // v1.10: every window in dark (the folder) and light (its "light" subfolder)
            {
                Snap.On = true; ApplyTheme(false); var sw = new MainWindow(); sw.SnapAll(args[si + 1]);
                sw.Redraw(true); sw.SnapAll(System.IO.Path.Combine(args[si + 1], "light")); return;   // the same window switched live
            }
            var w = new MainWindow(); app.Run(w);
        }
    }

    public class MainWindow : Window
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        static SolidColorBrush B(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        StackPanel rows; TextBlock status; Button checkBtn, tickedBtn, selfBtn; bool busy;
        ScrollViewer scroll; FrameworkElement stepsPanel; bool scrollToSteps;

        public MainWindow()
        {
            Title = "Hallow Setup"; Width = 760; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.CanMinimize;
            Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text"); FontFamily = new FontFamily("Segoe UI"); FontSize = 12.5;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = AppIcon(); } catch { }
            SourceInitialized += (s, e) => { Look.TitleBar(this); FitToScreen(); };
            SizeChanged += (s, e) => KeepOnScreen();   // v1.8: taller content (the Shield steps) never pushes the window off the screen
            Content = Build(); Refresh();
            if (!Snap.On) Loaded += (s, e) => Check();
            if (!Snap.On)   // v1.10: light / dark (and the accent) change live: theme.json, Setup's Look and Windows' setting, checked every 2 s
            {
                var tm = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                tm.Tick += (s, e) => Retheme(false); tm.Start();
            }
        }
        static ImageSource AppIcon()
        {
            var ico = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            return Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        UIElement Build()
        {
            var sp = new StackPanel();
            var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            try { title.Children.Add(new Image { Source = AppIcon(), Width = 30, Height = 30, Margin = new Thickness(0, 0, 10, 0) }); } catch { }
            title.Children.Add(new TextBlock { Text = "HALLOW", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = B(Look.TextAccent(Program.AccentHex)), VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(new TextBlock { Text = " SETUP", FontSize = 22, FontWeight = FontWeights.Light, VerticalAlignment = VerticalAlignment.Center });
            // v1.1: "Update Hallow Setup" appears here when a newer Setup is out
            selfBtn = new Button { Content = "Update Hallow Setup", Style = (Style)FindResource("Accent"), Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            selfBtn.Click += (s, e) => UpdateSelf();
            title.Children.Add(selfBtn);
            sp.Children.Add(title);
            sp.Children.Add(new TextBlock { Text = "Install, update and start the Hallow apps. Each download is checked against GitHub's checksum before its own installer runs as you.",
                Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            // v1.8: header, the app list (scrolls when the screen is too short for it), the buttons and the footer
            var root = new Grid { Margin = new Thickness(24, 16, 24, 18) };
            foreach (var h in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) root.RowDefinitions.Add(new RowDefinition { Height = h });
            root.Children.Add(sp);
            rows = new StackPanel();
            scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            Grid.SetRow(scroll, 1); root.Children.Add(scroll);
            var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            checkBtn = new Button { Content = "Check for updates", Style = (Style)FindResource("Btn") }; checkBtn.Click += (s, e) => Check();
            DockPanel.SetDock(checkBtn, Dock.Right); bottom.Children.Add(checkBtn);
            tickedBtn = new Button { Content = "Install ticked", Style = (Style)FindResource("Accent"), Margin = new Thickness(0, 0, 8, 0) }; tickedBtn.Click += (s, e) => InstallTicked();
            DockPanel.SetDock(tickedBtn, Dock.Right); bottom.Children.Add(tickedBtn);
            status = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            bottom.Children.Add(status); Grid.SetRow(bottom, 2); root.Children.Add(bottom);
            var foot = new TextBlock { Text = "Hallow Setup " + Program.Version + "  -  icons: Font Awesome Free (CC BY 4.0)", Foreground = (Brush)FindResource("Muted"), FontSize = 11, Margin = new Thickness(0, 12, 0, 0) };
            foot.Margin = new Thickness(0); foot.VerticalAlignment = VerticalAlignment.Center;
            var footRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) }; var look = LookPicker(); DockPanel.SetDock(look, Dock.Right);   // v1.10
            footRow.Children.Add(look); footRow.Children.Add(foot);
            Grid.SetRow(footRow, 3); root.Children.Add(footRow);
            return root;
        }

        // v1.10: redraws the window when the mode or the accent changed (force = Setup's own Look was just picked)
        bool? lastBad;
        void Retheme(bool force)
        {
            bool light; string acc;
            try { light = Look.CurrentLight(); acc = Setup.Accent() ?? "#F07820"; } catch { return; }
            if (!force && light == Look.Light && acc == Program.AccentHex) return;
            Redraw(light);
        }
        public void Redraw(bool light)
        {
            try
            {
                var off = scroll != null ? scroll.VerticalOffset : 0; var st = status != null ? status.Text : ""; var bad = lastBad;
                Program.ApplyTheme(light);
                Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text");
                Content = Build(); Refresh(); checkBtn.IsEnabled = !busy;
                if (bad.HasValue) Say(st, bad.Value); else { status.Text = st; status.Foreground = (Brush)FindResource("Muted"); }
                if (scroll != null) scroll.ScrollToVerticalOffset(off);
                Look.TitleBar(this);
            }
            catch { }
        }
        // v1.10: Look - Follow Hallow / Light / Dark (saved in %APPDATA%\HallowSetup\settings.json)
        FrameworkElement LookPicker()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = "Look", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var keys = new[] { "follow", "light", "dark" };
            var cb = new ComboBox { Style = (Style)FindResource("Combo"), Width = 132, Height = 26, ToolTip = "Light or dark - Follow Hallow uses the mode picked in Hallow Toolkit" };
            foreach (var n in new[] { "Follow Hallow", "Light", "Dark" }) cb.Items.Add(new ComboBoxItem { Content = n });
            cb.SelectedIndex = Math.Max(0, Array.IndexOf(keys, Look.AppLook(Look.SettingsFile)));
            cb.SelectionChanged += (s, e) =>
            {
                var k = keys[Math.Max(0, cb.SelectedIndex)];
                try { Look.SaveAppLook(Look.SettingsFile, k); } catch (Exception ex) { Say("Couldn't save the look: " + ex.Message, true); return; }
                Dispatcher.BeginInvoke(new Action(() => Retheme(true)));
            };
            sp.Children.Add(cb);
            return sp;
        }

        void Refresh()
        {
            rows.Children.Clear();
            foreach (var a in Setup.Apps) rows.Children.Add(Row(a));
            rows.Children.Add(ShieldRow());
            UpdateTickedButton();
            if (scrollToSteps && stepsPanel != null) { scrollToSteps = false; var el = stepsPanel; Dispatcher.BeginInvoke(new Action(() => ScrollIntoView(el)), System.Windows.Threading.DispatcherPriority.Loaded); }
            if (selfBtn != null) { selfBtn.Visibility = Setup.SelfNewer() ? Visibility.Visible : Visibility.Collapsed; selfBtn.Content = "Update Hallow Setup to " + Setup.Show(Setup.Self.Latest); selfBtn.IsEnabled = !busy; }
        }
        void UpdateTickedButton()
        {
            if (tickedBtn == null) return; var n = Setup.Apps.Count(x => x.Ticked);
            tickedBtn.Content = n == 0 ? "Install ticked" : "Install ticked (" + n + ")"; tickedBtn.IsEnabled = n > 0 && !busy;
        }
        // v1.1: install every ticked app, one after another
        void InstallTicked()
        {
            var list = Setup.Apps.Where(x => x.Ticked).ToList(); if (busy || list.Count == 0) return;
            var tk = list.Any(x => x.IsToolkit) && !Setup.DryRun;
            if (MessageBox.Show(this, "Install or update " + string.Join(", ", list.Select(x => x.Title)) + "?" + (tk ? "\n\nHallow Toolkit installs as admin, so Windows will ask you to allow it." : ""), "Hallow Setup", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            busy = true; Refresh(); checkBtn.IsEnabled = false;
            new Thread(() =>
            {
                var res = Setup.InstallMany(list, p => Dispatcher.BeginInvoke(new Action(() => { status.Text = p; status.Foreground = (Brush)FindResource("Muted"); })));
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    busy = false; checkBtn.IsEnabled = true; foreach (var a in list) a.Ticked = false; Refresh();
                    var bad = res.Count(r => !(r.Contains("installed") || r.Contains("running") || r.Contains("test run")));
                    Say(bad == 0 ? "Done: " + string.Join("  ", res) : bad + " of " + res.Count + " didn't install: " + string.Join("  ", res), bad > 0);
                }));
            }) { IsBackground = true }.Start();
        }
        void UpdateSelf()
        {
            if (busy || MessageBox.Show(this, "Update Hallow Setup to " + Setup.Show(Setup.Self.Latest) + "?\n\nIt downloads from GitHub, checks the file, and reopens by itself.", "Hallow Setup", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            busy = true; Refresh();
            new Thread(() =>
            {
                string err; try { err = Setup.UpdateSelf(p => Dispatcher.BeginInvoke(new Action(() => status.Text = p))); } catch (Exception e) { err = "Update failed: " + e.Message; }
                Dispatcher.BeginInvoke(new Action(() => { busy = false; if (err == null) Application.Current.Shutdown(); else { Refresh(); Say(err, !err.StartsWith("TEST")); } }));
            }) { IsBackground = true }.Start();
        }
        UIElement Row(HallowApp a)
        {
            var card = new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8) };
            var dp = new DockPanel();
            // v1.1: tick it to include it in "Install ticked"
            var tick = new CheckBox { IsChecked = a.Ticked, IsEnabled = !busy, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), ToolTip = "Include in Install ticked" };
            tick.Click += (s, e) => { a.Ticked = tick.IsChecked == true; UpdateTickedButton(); };
            DockPanel.SetDock(tick, Dock.Left); dp.Children.Add(tick);
            var icon = AppIconFor(a.Name, a.Icon); icon.Margin = new Thickness(0, 0, 12, 0); icon.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(icon, Dock.Left); dp.Children.Add(icon);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var installed = a.Installed; var iv = a.InstalledVersion;
            if (a.NoTray) right.Children.Add(new TextBlock { Text = "Opens from its shortcut", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center });
            else if (!a.IsToolkit)
            {
                var sw = new CheckBox { Content = new TextBlock { Text = "Start with Windows" }, IsChecked = installed && a.StartsWithWindows, IsEnabled = installed && !busy, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = installed ? "Starts " + a.Title + " in the tray when you sign in (its own Start with Windows setting)" : "Install it first" };
                sw.Click += (s, e) =>
                {
                    try { a.SetStartWithWindows(sw.IsChecked == true); Say(a.Title + (sw.IsChecked == true ? " starts with Windows (in the tray)." : " no longer starts with Windows."), false); }
                    catch (Exception ex) { Say("Couldn't change that: " + ex.Message, true); }
                };
                right.Children.Add(sw);
            }
            else right.Children.Add(new TextBlock { Text = "Starts from its own shortcut", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center });
            string label; bool enabled = !busy;
            if (!installed) label = "Install";
            else if (a.UpdateAvailable) label = "Update";
            else if (a.Latest != null) { label = "Up to date"; enabled = false; }
            else label = "Reinstall";
            var btn = new Button { Content = label, Style = (Style)FindResource(label == "Install" || label == "Update" ? "Accent" : "Btn"), MinWidth = 108, IsEnabled = enabled };
            btn.Click += (s, e) => Install(a);
            right.Children.Add(btn);
            if (installed)   // v1.3
            {
                var un = new Button { Content = "Uninstall", Tag = "icon", Style = (Style)FindResource("Btn"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 7, 10, 7), IsEnabled = !busy, ToolTip = "Uninstall " + a.Title };
                un.Click += (s, e) => Uninstall(a);
                right.Children.Add(un);
            }
            DockPanel.SetDock(right, Dock.Right); dp.Children.Add(right);
            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var name = new TextBlock { FontSize = 14 }; name.Inlines.Add(new System.Windows.Documents.Run(a.Title) { FontWeight = FontWeights.SemiBold });
            var ver = !installed ? "  Not installed" : "  " + Setup.Show(iv) + (a.Running ? " - running" : "");
            name.Inlines.Add(new System.Windows.Documents.Run(ver) { Foreground = installed ? (Brush)FindResource("Muted") : B(Look.Amber), FontSize = 12 });
            if (a.UpdateAvailable) name.Inlines.Add(new System.Windows.Documents.Run("  -  " + Setup.Show(a.Latest) + " is out") { Foreground = B(Look.Green), FontSize = 12 });
            else if (a.Latest != null && !installed) name.Inlines.Add(new System.Windows.Documents.Run("  -  latest " + Setup.Show(a.Latest)) { Foreground = (Brush)FindResource("Muted"), FontSize = 12 });
            mid.Children.Add(name);
            mid.Children.Add(new TextBlock { Text = a.Desc, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap });
            dp.Children.Add(mid);
            card.Child = dp; return card;
        }
        // v1.7: each app's own icon (64 px PNGs built in), or its Font Awesome glyph if one is missing
        static readonly System.Collections.Generic.Dictionary<string, ImageSource> appIcons = new System.Collections.Generic.Dictionary<string, ImageSource>();
        static FrameworkElement AppIconFor(string name, string fa)
        {
            ImageSource src;
            if (!appIcons.TryGetValue(name, out src))
            {
                try
                {
                    using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("HallowSetup.icon." + name.ToLowerInvariant() + ".png"))
                        if (st != null) { var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = st; bi.EndInit(); bi.Freeze(); src = bi; }
                }
                catch { src = null; }
                appIcons[name] = src;
            }
            if (src == null) return Icons.Make(fa, 24, B(Look.TextAccent(Program.AccentHex)));
            var img = new Image { Source = src, Width = 32, Height = 32, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }
        void Say(string text, bool bad) { lastBad = bad; status.Text = text; status.Foreground = bad ? B(Look.Amber) : B(Look.Green); }

        // ---- v1.6: Hallow Shield, the browser extension: its unpacked folder, and the steps to load it in a browser
        bool shieldSteps;
        UIElement ShieldRow()
        {
            var card = new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8) };
            var outer = new StackPanel();
            var dp = new DockPanel();
            var spacer = new Border { Width = 16, Margin = new Thickness(0, 0, 12, 0) };   // lines up with the tick boxes above
            DockPanel.SetDock(spacer, Dock.Left); dp.Children.Add(spacer);
            var icon = AppIconFor("Shield", "shield-halved"); icon.Margin = new Thickness(0, 0, 12, 0); icon.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(icon, Dock.Left); dp.Children.Add(icon);
            var installed = Shield.Installed("chrome"); var iv = Shield.InstalledVersion("chrome"); var upd = Shield.UpdateAvailable("chrome");
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (installed)
            {
                var how = new Button { Content = shieldSteps || Shield.JustInstalled ? "Hide steps" : "Load in browser", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), IsEnabled = !busy, ToolTip = "How to load the folder in Chrome, Edge or Brave" };
                how.Click += (s, e) => { var open = shieldSteps || Shield.JustInstalled; shieldSteps = !open; Shield.JustInstalled = false; scrollToSteps = !open; Refresh(); };
                right.Children.Add(how);
            }
            string label; bool enabled = !busy;
            if (!installed) label = "Install";
            else if (upd) label = "Update";
            else if (Shield.Latest != null && iv != null && !(Shield.Latest > iv)) { label = "Up to date"; enabled = false; }
            else label = "Reinstall";
            var btn = new Button { Content = label, Style = (Style)FindResource(label == "Install" || label == "Update" ? "Accent" : "Btn"), MinWidth = 108, IsEnabled = enabled };
            btn.Click += (s, e) => InstallShield("chrome");
            right.Children.Add(btn);
            if (installed)
            {
                var un = new Button { Content = "Uninstall", Tag = "icon", Style = (Style)FindResource("Btn"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 7, 10, 7), IsEnabled = !busy, ToolTip = "Move Hallow Shield's folder to the Recycle Bin" };
                un.Click += (s, e) => UninstallShield();
                right.Children.Add(un);
            }
            DockPanel.SetDock(right, Dock.Right); dp.Children.Add(right);
            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var name = new TextBlock { FontSize = 14 }; name.Inlines.Add(new System.Windows.Documents.Run("Hallow Shield (browser extension)") { FontWeight = FontWeights.SemiBold });
            name.Inlines.Add(new System.Windows.Documents.Run(!installed ? "  Not installed" : "  " + Setup.Show(iv)) { Foreground = installed ? (Brush)FindResource("Muted") : B(Look.Amber), FontSize = 12 });
            if (upd) name.Inlines.Add(new System.Windows.Documents.Run("  -  " + Setup.Show(Shield.Latest) + " is out") { Foreground = B(Look.Green), FontSize = 12 });
            else if (Shield.Latest != null && !installed) name.Inlines.Add(new System.Windows.Documents.Run("  -  latest " + Setup.Show(Shield.Latest)) { Foreground = (Brush)FindResource("Muted"), FontSize = 12 });
            mid.Children.Add(name);
            mid.Children.Add(new TextBlock { Text = "Ad, tracker and cookie-banner blocker for Chrome, Edge and Brave - unpacked into " + Shield.Dir("chrome"), Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap });
            dp.Children.Add(mid);
            outer.Children.Add(dp);
            stepsPanel = null;
            if (installed && (shieldSteps || Shield.JustInstalled)) { outer.Children.Add(ShieldSteps()); stepsPanel = card; }
            card.Child = outer; return card;
        }
        UIElement ShieldSteps()
        {
            var sp = new StackPanel { Margin = new Thickness(56, 10, 0, 2) };
            sp.Children.Add(new TextBlock { Text = "Load it in your browser (once):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            sp.Children.Add(new TextBlock { Text = "1.  Open the browser's extensions page:", Foreground = (Brush)FindResource("Muted") });
            var wrap = new WrapPanel { Margin = new Thickness(18, 4, 0, 6) };
            foreach (var b in Shield.Browsers)
            {
                var br = b; var btn = new Button { Content = "Open " + b.Name + " extensions", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 4), ToolTip = "Starts " + b.Name + " with " + b.Url + " (and copies the address)" };
                btn.Click += (s, e) => { var m = Shield.OpenExtensions(br, t => Clipboard.SetText(t)); Say(m, m.Contains("isn't installed") || m.StartsWith("Couldn't")); };
                wrap.Children.Add(btn);
            }
            sp.Children.Add(wrap);
            var line2 = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var copy = new Button { Content = "Copy folder path", Style = (Style)FindResource("Btn"), ToolTip = Shield.Dir("chrome") };
            copy.Click += (s, e) => { try { Clipboard.SetText(Shield.Dir("chrome")); Say("Copied " + Shield.Dir("chrome") + " - paste it into the Load unpacked folder picker.", false); } catch (Exception ex) { Say("Couldn't copy: " + ex.Message, true); } };
            DockPanel.SetDock(copy, Dock.Right); line2.Children.Add(copy);
            line2.Children.Add(new TextBlock { Text = "2.  " + Shield.Steps, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            sp.Children.Add(line2);
            sp.Children.Add(new TextBlock { Text = "Shield keeps its settings, My filters and stats in the browser, not in this folder, so updating replaces the folder safely.", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) });
            // v1.8: Shield 1.6 has a fixed extension ID (for the family bridge): the browser sees it as a new extension
            var iv = Shield.InstalledVersion("chrome");
            if (iv != null && iv >= new Version(1, 6)) sp.Children.Add(new TextBlock { Text = "Coming from Shield 1.5 or older? 1.6 has a fixed extension ID, so the browser treats it as a new extension: remove the old Shield card, then Load unpacked again (or click reload, then remove the old card if two show). Its settings and stats start fresh - export My filters first (Shield options, My filters, Export).", Foreground = B(Look.Amber), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            // v1.8: Shield 1.6+ brings the Hallow family bridge (registered for this user when Shield is unpacked)
            if (Shield.BridgeRegistered()) sp.Children.Add(new TextBlock { Text = "The Hallow family bridge is registered: turn on \"Share counts with Hallow apps on this PC\" in Shield's options (Stats) to show what Shield blocked in Hallow Hub and Remote. It stays off until you turn it on.", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            // Firefox: secondary (it only keeps an unsigned add-on until it restarts)
            var ff = new DockPanel();
            var ffBtn = new Button { Content = Shield.Installed("firefox") ? "Update the Firefox copy" : "Get the Firefox copy", Style = (Style)FindResource("Btn"), IsEnabled = !busy, ToolTip = Shield.Dir("firefox") };
            ffBtn.Click += (s, e) => InstallShield("firefox");
            DockPanel.SetDock(ffBtn, Dock.Right); ff.Children.Add(ffBtn);
            ff.Children.Add(new TextBlock { Text = Shield.FirefoxSteps, Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            sp.Children.Add(ff);
            return sp;
        }
        void InstallShield(string browser)
        {
            if (busy) return;
            busy = true; Refresh(); checkBtn.IsEnabled = false;
            new Thread(() =>
            {
                string msg; bool ok;
                try { msg = Shield.Install(browser, p => Dispatcher.BeginInvoke(new Action(() => { status.Text = p; status.Foreground = (Brush)FindResource("Muted"); }))); ok = msg.Contains(" is in ") || msg.Contains("test run"); }
                catch (Exception e) { msg = "Install failed: " + e.Message; ok = false; }
                Dispatcher.BeginInvoke(new Action(() => { busy = false; checkBtn.IsEnabled = true; if (ok && browser == "chrome") { shieldSteps = true; scrollToSteps = true; } Refresh(); Say(msg + (ok && browser == "chrome" ? " Now load it in your browser - the steps are below." : ""), !ok); }));
            }) { IsBackground = true }.Start();
        }
        void UninstallShield()
        {
            if (busy || MessageBox.Show(this, "Move Hallow Shield's folder (" + Shield.Dir("chrome") + ") to the Recycle Bin?\n\nThe browser can't load Shield without it: remove Shield's card on the extensions page too. Your Shield settings live in the browser and stay there until you remove the card.", "Uninstall Hallow Shield", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            var m = Shield.Uninstall("chrome"); shieldSteps = false; Refresh(); Say(m, !m.Contains("Recycle Bin."));
        }

        // ---- v1.8: never taller than the screen, and kept on it ----
        // the working area (taskbar left out) of the monitor the window is on, in WPF units
        Rect WorkArea()
        {
            try
            {
                var h = new WindowInteropHelper(this).Handle;
                var src = h == IntPtr.Zero ? null : PresentationSource.FromVisual(this);
                if (src != null && src.CompositionTarget != null)
                {
                    var wa = System.Windows.Forms.Screen.FromHandle(h).WorkingArea; var m = src.CompositionTarget.TransformFromDevice;
                    return new Rect(m.Transform(new Point(wa.Left, wa.Top)), m.Transform(new Point(wa.Right, wa.Bottom)));
                }
            }
            catch { }
            return SystemParameters.WorkArea;
        }
        void FitToScreen()
        {
            var wa = WorkArea(); MaxHeight = wa.Height;
            var h = Math.Min(ActualHeight > 0 ? ActualHeight : wa.Height, wa.Height);
            Top = wa.Top + (wa.Height - h) / 2; Left = Math.Max(wa.Left, wa.Left + (wa.Width - Width) / 2);
        }
        void KeepOnScreen()
        {
            if (Snap.On || WindowState != WindowState.Normal || double.IsNaN(Top) || double.IsNaN(Left)) return;
            var wa = WorkArea(); if (MaxHeight != wa.Height) MaxHeight = wa.Height;
            var h = ActualHeight > 0 ? ActualHeight : Height; var w = ActualWidth > 0 ? ActualWidth : Width;
            if (Top + h > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - h);
            if (Top < wa.Top) Top = wa.Top;
            if (Left + w > wa.Right) Left = Math.Max(wa.Left, wa.Right - w);
            if (Left < wa.Left) Left = wa.Left;
        }
        // scrolls the list so the element (the Shield card with its steps) is in view: its bottom, and its top if it fits
        void ScrollIntoView(FrameworkElement el)
        {
            try
            {
                if (scroll == null || el == null || !el.IsDescendantOf(rows)) return;
                scroll.UpdateLayout();
                var top = el.TransformToAncestor(rows).Transform(new Point(0, 0)).Y; var bottom = top + el.ActualHeight; var view = scroll.ViewportHeight;
                var off = scroll.VerticalOffset;
                if (bottom > off + view) off = bottom - view;
                if (top < off) off = top;
                scroll.ScrollToVerticalOffset(Math.Max(0, off)); scroll.UpdateLayout();
            }
            catch { }
        }

        void Check()
        {
            if (busy) return; busy = true; checkBtn.IsEnabled = false; Say("Checking GitHub for the newest versions...", false); status.Foreground = (Brush)FindResource("Muted");
            new Thread(() =>
            {
                Setup.CheckLatest(); Setup.CheckSelf(); Shield.CheckLatest();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var a in Setup.Apps) if (a.UpdateAvailable) a.Ticked = true;   // updates are ticked for Install ticked
                    busy = false; checkBtn.IsEnabled = true; Refresh(); Summary();
                }));
            }) { IsBackground = true }.Start();
        }
        void Summary()
        {
            var known = Setup.Apps.Count(x => x.Latest != null); var upd = Setup.Apps.Count(x => x.UpdateAvailable) + (Shield.UpdateAvailable("chrome") ? 1 : 0);
            if (known == 0) Say("Couldn't reach GitHub - versions shown are the ones on this PC.", true);
            else Say(upd == 0 ? "Everything installed is up to date." : upd + " update" + (upd == 1 ? "" : "s") + " available.", false);
        }
        void Install(HallowApp a)
        {
            if (busy) return;
            if (a.IsToolkit && !Setup.DryRun && MessageBox.Show(this, "Hallow Toolkit installs as admin, so Windows will ask you to allow it. Continue?", "Hallow Setup", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            busy = true; Refresh(); checkBtn.IsEnabled = false;
            new Thread(() =>
            {
                string msg; bool ok;
                try { msg = Setup.Install(a, p => Dispatcher.BeginInvoke(new Action(() => { status.Text = p; status.Foreground = (Brush)FindResource("Muted"); }))); ok = msg.Contains("installed") || msg.Contains("running") || msg.Contains("test run"); }
                catch (Exception e) { msg = "Install failed: " + e.Message; ok = false; }
                Dispatcher.BeginInvoke(new Action(() => { busy = false; checkBtn.IsEnabled = true; Refresh(); Say(msg, !ok); }));
            }) { IsBackground = true }.Start();
        }

        // ---- v1.3: uninstall one app (asks first; settings and data only when ticked, to the Recycle Bin)
        void Uninstall(HallowApp a)
        {
            if (busy) return;
            if (a.IsToolkit) { UninstallToolkit(); return; }
            bool withData; if (!UninstallDialog.Ask(this, a, out withData)) return;
            RunUninstall(a, withData, false);
        }
        void RunUninstall(HallowApp a, bool withData, bool force)
        {
            busy = true; Refresh(); checkBtn.IsEnabled = false;
            new Thread(() =>
            {
                UninstallResult r;
                try { r = Uninstaller.Uninstall(a, withData, force, p => Dispatcher.BeginInvoke(new Action(() => { status.Text = p; status.Foreground = (Brush)FindResource("Muted"); }))); }
                catch (Exception e) { r = new UninstallResult { Message = "Uninstall failed: " + e.Message }; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    busy = false; checkBtn.IsEnabled = true; a.Ticked = false; Refresh();
                    if (r.StillRunning)
                    {
                        if (MessageBox.Show(this, a.Title + " didn't close when asked.\n\nClose it anyway and uninstall? Anything it hasn't saved yet is lost.", "Hallow Setup", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) RunUninstall(a, withData, true);
                        else Say(a.Title + " is still installed - close it from its tray icon, then click Uninstall again.", true);
                        return;
                    }
                    Say(r.Message, !r.Ok);
                }));
            }) { IsBackground = true }.Start();
        }
        // Hallow Toolkit installs as admin: start its own uninstaller (Windows asks you), or say how to remove it
        void UninstallToolkit()
        {
            if (Uninstaller.ToolkitUninstaller() != null)
            {
                if (MessageBox.Show(this, "Hallow Toolkit uninstalls as admin, so Hallow Setup starts its own uninstaller and Windows asks you to allow it.\n\nContinue?", "Uninstall Hallow Toolkit", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                try { var m = Uninstaller.StartToolkitUninstaller(); Say(m, m.StartsWith("Cancelled")); } catch (Exception e) { Say("Couldn't start Hallow Toolkit's uninstaller: " + e.Message, true); }
                return;
            }
            if (MessageBox.Show(this, "Hallow Toolkit doesn't have its own uninstaller yet, and it changes Windows settings as admin, so Hallow Setup doesn't remove it for you.\n\n" +
                "To remove it: open Hallow Toolkit, undo the tweaks you want back and turn off its weekly clean-up, then delete " + Setup.ToolkitDir + " and its Desktop and Start menu shortcuts.\n\nOpen its folder now?",
                "Uninstall Hallow Toolkit", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                try { System.Diagnostics.Process.Start("explorer.exe", "\"" + Setup.ToolkitDir + "\""); } catch { }
        }

        public void SnapAll(string dir)
        {
            if (Setup.TestMode) { Setup.CheckLatest(); Setup.CheckSelf(); Shield.CheckLatest(); foreach (var a in Setup.Apps) a.Ticked = a.UpdateAvailable || !a.Installed && !a.IsToolkit; }   // only against the test stand-in, never GitHub
            Refresh(); if (Setup.TestMode) Summary(); else Say("Snapshot - versions on this PC only.", false);
            Snap.Save(this, System.IO.Path.Combine(dir, "main.png"), 760, 0);
            // v1.6: the Shield row with its "load it in your browser" steps
            if (Shield.Installed("chrome")) { Shield.JustInstalled = true; Refresh(); Say("Hallow Shield " + Setup.Show(Shield.InstalledVersion("chrome")) + " for Chrome / Edge / Brave is in " + Shield.Dir("chrome") + ".", false); Snap.Save(this, System.IO.Path.Combine(dir, "shield.png"), 760, 0);
                // v1.8: as on a 1920x1080 screen (1032 px working area) and a 1366x768 one (728 px), less the title bar: the list scrolls, the steps in view
                foreach (var wa in new[] { 1032, 728 }) { var el = stepsPanel; Snap.Save(this, System.IO.Path.Combine(dir, "shield-" + wa + ".png"), 760, wa - 31, () => ScrollIntoView(el)); }
                Shield.JustInstalled = false; }
            foreach (var a in Setup.Apps.Where(x => x.Installed && !x.IsToolkit))   // v1.3: the uninstall dialog (nothing is removed)
            { CheckBox data; Snap.Save(UninstallDialog.Build(null, a, out data), System.IO.Path.Combine(dir, "uninstall-" + a.Name + ".png"), 540, 0); }
        }
    }
}
