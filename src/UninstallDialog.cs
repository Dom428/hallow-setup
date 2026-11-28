// Hallow Setup - v1.3: the "Uninstall Hallow <App>?" dialog. Says what goes, what never goes, and offers
// "Also remove my settings and data" (unticked; it goes to the Recycle Bin).
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HallowSetup
{
    public static class UninstallDialog
    {
        static SolidColorBrush B(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }

        public static Window Build(Window owner, HallowApp a, out CheckBox data)
        {
            var w = new Window { Title = "Uninstall " + a.Title, Width = 540, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen, FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5 };
            if (owner != null && owner.IsLoaded) w.Owner = owner;
            w.SourceInitialized += (s, e) => Look.TitleBar(w);   // v1.10: light or dark title bar
            w.Background = (Brush)w.FindResource("Bg"); w.Foreground = (Brush)w.FindResource("Text");
            var muted = (Brush)w.FindResource("Muted");
            var sp = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var ic = Icons.Make(a.Icon, 22, B(Look.TextAccent(Program.AccentHex))); ic.Margin = new Thickness(0, 0, 12, 0); ic.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(ic);
            head.Children.Add(new TextBlock { Text = "Uninstall " + a.Title + "?", FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(head);
            sp.Children.Add(new TextBlock { Text = "Hallow Setup closes it first if it's running, then removes:", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            var items = new System.Collections.Generic.List<string>();
            if (a.Name == "Mixer") items.Add("Its volume changes: every app goes back to full volume and its normal output");
            items.Add("The program folder (" + Uninstaller.ProgramDir(a) + ")");
            items.Add("Its Start menu shortcut (and Desktop shortcut)");
            if (!a.NoTray) items.Add("Its Start with Windows entry");   // v1.21: Browser and Files never have one (v1.22: Paint, Notes, Uninstall)
            if (a.Name == "Net") items.Add("Its 24-hour history and timeline (" + Uninstaller.NetHistoryDir() + ") - deleted for good");   // v1.22
            if (a.Name == "Keys") items.Add("Its browser autofill link (you can remove the extension from your browser)");
            foreach (var t in items)
            {
                var row = new DockPanel { Margin = new Thickness(6, 0, 0, 3) };
                var dot = new TextBlock { Text = "•", Foreground = B(Look.TextAccent(Program.AccentHex)), Margin = new Thickness(0, 0, 8, 0) }; DockPanel.SetDock(dot, Dock.Left); row.Children.Add(dot);
                row.Children.Add(new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap }); sp.Children.Add(row);
            }
            var kept = Uninstaller.KeptNote(a);
            if (kept != null) sp.Children.Add(new TextBlock { Text = kept, TextWrapping = TextWrapping.Wrap, Foreground = B(Look.Green), Margin = new Thickness(0, 10, 0, 0) });
            var blocked = Uninstaller.DataBlocked(a);
            data = null;
            if (a.Name != "Keys" && a.Name != "Financials")
            {
                var label = new StackPanel();
                label.Children.Add(new TextBlock { Text = "Also remove my settings and data", Foreground = (Brush)w.FindResource("Text") });
                label.Children.Add(new TextBlock { Text = blocked ?? DataText(a), Foreground = blocked != null ? B(Look.Amber) : muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
                data = new CheckBox { Content = label, IsChecked = false, IsEnabled = blocked == null, Margin = new Thickness(0, 12, 0, 0), VerticalContentAlignment = VerticalAlignment.Top };
                sp.Children.Add(data);
            }
            // v1.20: Capture's clipboard history and Find's file index are deleted for good (said in their notes above; v1.22: Net's history)
            sp.Children.Add(new TextBlock { Text = FooterText(a), Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "Cancel", Style = (Style)w.FindResource("Btn"), IsCancel = true, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
            var ok = new Button { Content = "Uninstall", Style = (Style)w.FindResource("Accent"), MinWidth = 108 };
            ok.Click += (s, e) => { w.DialogResult = true; };
            btns.Children.Add(cancel); btns.Children.Add(ok); sp.Children.Add(btns);
            w.Content = sp; return w;
        }

        // v1.22: what "Also remove my settings and data" takes - with Hallow Notes' backups of unsaved text and Hallow Uninstall's
        // registry backups named, since those are the ones you'd miss
        public static string DataText(HallowApp a)
        {
            var d = Uninstaller.DataDir(a); var ld = Uninstaller.LocalDataDir(a);
            // v1.23: what's in them for the new apps too - Clock's alarms, Fonts' collections, Updates' history, Doctor's dumps and reports
            var what = a.Name == "Notes" ? " - including the backups of your unsaved text" : a.Name == "Uninstall" ? " - including its history and registry backups"
                : a.Name == "Doctor" ? " - including the copied crash dumps, their analysis reports and symbols" : a.Name == "Updates" ? " - including its history of updates"
                : a.Name == "Fonts" ? " - including your favourites and collections" : a.Name == "Clock" ? " - including your alarms, timers and reminders" : "";
            return (ld == null ? d : d + " and " + ld) + what;
        }

        // the line above the buttons: what goes to the Recycle Bin (v1.20: Capture's clipboard history and Find's file index are deleted for
        // good - said in their notes above; v1.22: Net's history). v1.23: for Doctor, Updates, Clock and Fonts it says what the ticked data
        // includes, so it's clear what you'd get back from the Recycle Bin
        public static string FooterText(HallowApp a)
        {
            var t = a.Name == "Capture" || a.Name == "Find" || a.Name == "Net" ? "Everything else removed goes to the Recycle Bin, so you can get it back." : "Everything removed goes to the Recycle Bin, so you can get it back.";
            if (a.Name == "Doctor") t += " With the box ticked, that includes the copied crash dumps and their analysis reports.";
            else if (a.Name == "Updates") t += " With the box ticked, that includes its history of updates - the apps it updated stay as they are.";
            else if (a.Name == "Clock") t += " With the box ticked, that includes your alarms, timers and reminders.";
            else if (a.Name == "Fonts") t += " With the box ticked, that includes your favourites and collections - the fonts you installed stay installed.";
            return t;
        }

        // true = go ahead; withData = the box was ticked
        public static bool Ask(Window owner, HallowApp a, out bool withData)
        {
            CheckBox data; var w = Build(owner, a, out data);
            var r = w.ShowDialog() == true;
            withData = r && data != null && data.IsEnabled && data.IsChecked == true;
            return r;
        }
    }
}
