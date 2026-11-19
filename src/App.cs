// Hallow Setup - one window to install, update and start-with-Windows the Hallow family:
// Mixer, Hub, Vault, Overlay, Remote, Clips, Keys and Toolkit. Nothing is downloaded until you click Install or Update.
// v1.3: Uninstall per app (Uninstall.cs, UninstallDialog.cs).
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
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

namespace HallowSetup
{
    public static class Program
    {
        public const string Version = "1.3";
        public static string AccentHex = "#F07820";
        [STAThread]
        public static void Main(string[] args)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            string xaml;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("HallowSetup.Theme.xaml")) using (var r = new System.IO.StreamReader(s)) xaml = r.ReadToEnd();
            var acc = Setup.Accent();   // the Hallow accent picked in Hallow Toolkit, if any
            if (acc != null) { AccentHex = acc; xaml = xaml.Replace("#F07820", acc).Replace("#f07820", acc); }
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(xaml));
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
            int si = Array.IndexOf(args, "--snap");
            if (si >= 0 && si + 1 < args.Length) { Snap.On = true; new MainWindow().SnapAll(args[si + 1]); return; }
            var w = new MainWindow(); app.Run(w);
        }
    }

    public class MainWindow : Window
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        static SolidColorBrush B(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        StackPanel rows; TextBlock status; Button checkBtn, tickedBtn, selfBtn; bool busy;

        public MainWindow()
        {
            Title = "Hallow Setup"; Width = 760; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.CanMinimize;
            Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text"); FontFamily = new FontFamily("Segoe UI"); FontSize = 12.5;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = AppIcon(); } catch { }
            SourceInitialized += (s, e) => { int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, 4); };
            Content = Build(); Refresh();
            if (!Snap.On) Loaded += (s, e) => Check();
        }
        static ImageSource AppIcon()
        {
            var ico = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            return Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        UIElement Build()
        {
            var sp = new StackPanel { Margin = new Thickness(24, 16, 24, 18) };
            var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            try { title.Children.Add(new Image { Source = AppIcon(), Width = 30, Height = 30, Margin = new Thickness(0, 0, 10, 0) }); } catch { }
            title.Children.Add(new TextBlock { Text = "HALLOW", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = B(Program.AccentHex), VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(new TextBlock { Text = " SETUP", FontSize = 22, FontWeight = FontWeights.Light, VerticalAlignment = VerticalAlignment.Center });
            // v1.1: "Update Hallow Setup" appears here when a newer Setup is out
            selfBtn = new Button { Content = "Update Hallow Setup", Style = (Style)FindResource("Accent"), Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            selfBtn.Click += (s, e) => UpdateSelf();
            title.Children.Add(selfBtn);
            sp.Children.Add(title);
            sp.Children.Add(new TextBlock { Text = "Install, update and start the Hallow apps. Each download is checked against GitHub's checksum before its own installer runs as you.",
                Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            rows = new StackPanel(); sp.Children.Add(rows);
            var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            checkBtn = new Button { Content = "Check for updates", Style = (Style)FindResource("Btn") }; checkBtn.Click += (s, e) => Check();
            DockPanel.SetDock(checkBtn, Dock.Right); bottom.Children.Add(checkBtn);
            tickedBtn = new Button { Content = "Install ticked", Style = (Style)FindResource("Accent"), Margin = new Thickness(0, 0, 8, 0) }; tickedBtn.Click += (s, e) => InstallTicked();
            DockPanel.SetDock(tickedBtn, Dock.Right); bottom.Children.Add(tickedBtn);
            status = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            bottom.Children.Add(status); sp.Children.Add(bottom);
            sp.Children.Add(new TextBlock { Text = "Hallow Setup " + Program.Version + "  -  icons: Font Awesome Free (CC BY 4.0)", Foreground = (Brush)FindResource("Muted"), FontSize = 11, Margin = new Thickness(0, 12, 0, 0) });
            return sp;
        }

        void Refresh()
        {
            rows.Children.Clear();
            foreach (var a in Setup.Apps) rows.Children.Add(Row(a));
            UpdateTickedButton();
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
            var icon = Icons.Make(a.Icon, 24, B(Program.AccentHex)); icon.Margin = new Thickness(0, 0, 14, 0); icon.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(icon, Dock.Left); dp.Children.Add(icon);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var installed = a.Installed; var iv = a.InstalledVersion;
            if (!a.IsToolkit)
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
            name.Inlines.Add(new System.Windows.Documents.Run(ver) { Foreground = installed ? (Brush)FindResource("Muted") : B("#F0BE5A"), FontSize = 12 });
            if (a.UpdateAvailable) name.Inlines.Add(new System.Windows.Documents.Run("  -  " + Setup.Show(a.Latest) + " is out") { Foreground = B("#6ED28C"), FontSize = 12 });
            else if (a.Latest != null && !installed) name.Inlines.Add(new System.Windows.Documents.Run("  -  latest " + Setup.Show(a.Latest)) { Foreground = (Brush)FindResource("Muted"), FontSize = 12 });
            mid.Children.Add(name);
            mid.Children.Add(new TextBlock { Text = a.Desc, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap });
            dp.Children.Add(mid);
            card.Child = dp; return card;
        }
        void Say(string text, bool bad) { status.Text = text; status.Foreground = bad ? B("#F0BE5A") : B("#6ED28C"); }

        void Check()
        {
            if (busy) return; busy = true; checkBtn.IsEnabled = false; Say("Checking GitHub for the newest versions...", false); status.Foreground = (Brush)FindResource("Muted");
            new Thread(() =>
            {
                Setup.CheckLatest(); Setup.CheckSelf();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var a in Setup.Apps) if (a.UpdateAvailable) a.Ticked = true;   // updates are ticked for Install ticked
                    busy = false; checkBtn.IsEnabled = true; Refresh(); Summary();
                }));
            }) { IsBackground = true }.Start();
        }
        void Summary()
        {
            var known = Setup.Apps.Count(x => x.Latest != null); var upd = Setup.Apps.Count(x => x.UpdateAvailable);
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
            if (Setup.TestMode) { Setup.CheckLatest(); Setup.CheckSelf(); foreach (var a in Setup.Apps) a.Ticked = a.UpdateAvailable || !a.Installed && !a.IsToolkit; }   // only against the test stand-in, never GitHub
            Refresh(); if (Setup.TestMode) Summary(); else Say("Snapshot - versions on this PC only.", false);
            Snap.Save(this, System.IO.Path.Combine(dir, "main.png"), 760, 0);
            foreach (var a in Setup.Apps.Where(x => x.Installed && !x.IsToolkit))   // v1.3: the uninstall dialog (nothing is removed)
            { CheckBox data; Snap.Save(UninstallDialog.Build(null, a, out data), System.IO.Path.Combine(dir, "uninstall-" + a.Name + ".png"), 540, 0); }
        }
    }
}
