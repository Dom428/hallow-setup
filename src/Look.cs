// Hallow Setup - light and dark mode (family spec "Hallow light / dark mode").
// The family switch is "mode" in %APPDATA%\Hallow\theme.json (written by Hallow Toolkit; Setup only reads it):
// "dark" (also when missing or unknown), "light", or "system" (= Windows' AppsUseLightTheme). Setup's own "Look"
// (Follow Hallow / Light / Dark, in %APPDATA%\HallowSetup\settings.json) wins over it. Dark = the look Setup always had.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace HallowSetup
{
    public static class Look
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        public static string ThemeFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Hallow\theme.json");
        public static string SettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"HallowSetup\settings.json");

        // ---- which mode ----
        // appLook: "follow" (default) / "light" / "dark"; themeMode: theme.json's "mode" (null = missing); systemLight: Windows' setting (null = unknown = dark)
        public static bool ResolveLight(string appLook, string themeMode, Func<bool?> systemLight)
        {
            var a = (appLook ?? "").Trim().ToLowerInvariant();
            if (a == "light") return true;
            if (a == "dark") return false;
            var m = (themeMode ?? "").Trim().ToLowerInvariant();
            if (m == "light") return true;
            if (m == "system") { bool? s = null; try { s = systemLight == null ? null : systemLight(); } catch { } return s == true; }
            return false;   // "dark", missing or anything else
        }
        public static string ThemeMode(string file)
        {
            try
            {
                if (!File.Exists(file)) return null;
                var d = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(file)); object m;
                return d != null && d.TryGetValue("mode", out m) ? m as string : null;
            }
            catch { return null; }
        }
        // HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize AppsUseLightTheme (1 = light); only ever read
        public static bool? SystemLight()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    if (v is int) return (int)v == 1;
                    return null;
                }
            }
            catch { return null; }
        }
        // Setup's own Look: "follow" / "light" / "dark"
        public static string AppLook(string file)
        {
            try
            {
                if (!File.Exists(file)) return "follow";
                var d = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(file)); object v;
                var s = d != null && d.TryGetValue("look", out v) ? (v as string ?? "").ToLowerInvariant() : "";
                return s == "light" || s == "dark" ? s : "follow";
            }
            catch { return "follow"; }
        }
        // keeps any other fields in the file; atomic write
        public static void SaveAppLook(string file, string look)
        {
            Dictionary<string, object> d = null;
            try { if (File.Exists(file)) d = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(file)); } catch { }
            if (d == null) d = new Dictionary<string, object>();
            d["look"] = look == "light" || look == "dark" ? look : "follow";
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var tmp = file + ".tmp"; File.WriteAllText(tmp, js.Serialize(d));
            if (File.Exists(file)) File.Replace(tmp, file, null); else File.Move(tmp, file);
        }
        public static bool CurrentLight() { return ResolveLight(AppLook(SettingsFile), ThemeMode(ThemeFile), SystemLight); }

        // Windows title bar: dark or light to match (DWMWA_USE_IMMERSIVE_DARK_MODE = 20)
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        public static void TitleBar(System.Windows.Window w)
        {
            try { var h = new System.Windows.Interop.WindowInteropHelper(w).Handle; if (h == IntPtr.Zero) return; int dark = Light ? 0 : 1; DwmSetWindowAttribute(h, 20, ref dark, 4); } catch { }
        }

        // ---- colours ----
        public static bool Light;   // the mode the window is drawn in now
        public static string Green { get { return Light ? ForText("#1E8E4F", "#F6F4FA") : "#6ED28C"; } }   // ok / update available
        public static string Amber { get { return Light ? ForText("#A66A00", "#F6F4FA") : "#F0BE5A"; } }   // warnings, "Not installed"
        public static string Red { get { return Light ? "#C2333F" : "#C8414B"; } }
        // the accent for text and thin lines (darkened in light mode so it reads on white)
        public static string TextAccent(string accent) { return Light ? ForText(accent, "#FFFFFF") : accent; }

        // dark value -> light value for every colour in Theme.xaml (the keys keep their names, only values swap)
        public static readonly string[,] Map = {
            { "#16121C", "#F6F4FA" },   // Bg
            { "#211B2A", "#FFFFFF" },   // Panel, search box
            { "#2E2639", "#EEEAF5" },   // PanelHi
            { "#3A3248", "#DAD3E6" },   // Line, borders, hover, scroll thumb
            { "#ECE8F4", "#1E1A26" },   // Text
            { "#8F88A0", "#6B6478" },   // Muted
            { "#2A2333", "#EEEAF5" },   // button / combo
            { "#241E2E", "#FFFFFF" },   // menus, tooltips, drop-downs
            { "#1A1522", "#FFFFFF" },   // text field
            { "#55496A", "#B9AECD" },   // button hover border
            { "#58566A", "#B9B3C6" },   // switch off, scroll thumb hover
            { "#CDCAD6", "#FFFFFF" },   // switch knob off
            { "#6ED28C", "#1E8E4F" },   // switch on
            { "#C8414B", "#C2333F" },   // checked (red)
            { "#E05A64", "#D9545F" },
            { "#A98BE0", "#7A5CC0" },   // lilac
        };
        // Theme.xaml with the accent and (in light mode) the light palette
        public static string Theme(string xaml, string accent, bool light)
        {
            var acc = accent ?? "#F07820";
            if (light)
            {
                var txt = ForText(acc, "#FFFFFF");
                // the button hover/press overlay darkens instead of lightening
                xaml = xaml.Replace("x:Name=\"hl\" Background=\"#FFFFFF\"", "x:Name=\"hl\" Background=\"#000000\"");
                // the knob of the faders keeps a white fill on a light ring
                xaml = xaml.Replace("Fill=\"#ECE8F4\" Stroke=\"#16121C\"", "Fill=\"#FFFFFF\" Stroke=\"#B9AECD\"");
                // caret and focus line are thin: the darkened accent
                xaml = Regex.Replace(xaml, "(CaretBrush\" Value=\")#F07820", "${1}" + txt, RegexOptions.IgnoreCase);
                xaml = Regex.Replace(xaml, "(Property=\"BorderBrush\" Value=\")#F07820", "${1}" + txt, RegexOptions.IgnoreCase);
                for (int i = 0; i < Map.GetLength(0); i++) xaml = Regex.Replace(xaml, Regex.Escape(Map[i, 0]), Map[i, 1], RegexOptions.IgnoreCase);
            }
            // filled buttons keep the real accent (with dark text)
            return Regex.Replace(xaml, "#F07820", acc, RegexOptions.IgnoreCase);
        }

        // ---- contrast (WCAG) ----
        static void Rgb(string hex, out double r, out double g, out double b)
        {
            var h = hex.TrimStart('#'); if (h.Length == 8) h = h.Substring(2);
            r = int.Parse(h.Substring(0, 2), NumberStyles.HexNumber) / 255.0; g = int.Parse(h.Substring(2, 2), NumberStyles.HexNumber) / 255.0; b = int.Parse(h.Substring(4, 2), NumberStyles.HexNumber) / 255.0;
        }
        static double Lin(double c) { return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        public static double Luminance(string hex) { double r, g, b; Rgb(hex, out r, out g, out b); return 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b); }
        public static double Contrast(string a, string b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }
        // darkens the colour in HSL (same hue and saturation) until it has 4.5:1 against the background
        public static string ForText(string hex, string bg)
        {
            double r, g, b; Rgb(hex, out r, out g, out b);
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, h = 0, s = 0, d = max - min;
            if (d > 0)
            {
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                if (max == r) h = (g - b) / d + (g < b ? 6 : 0); else if (max == g) h = (b - r) / d + 2; else h = (r - g) / d + 4;
                h /= 6;
            }
            var cur = Hex(r, g, b);
            while (Contrast(cur, bg) < 4.5 && l > 0) { l = Math.Max(0, l - 0.005); cur = FromHsl(h, s, l); }
            return cur;
        }
        static string FromHsl(double h, double s, double l)
        {
            if (s == 0) return Hex(l, l, l);
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            return Hex(Hue(p, q, h + 1.0 / 3), Hue(p, q, h), Hue(p, q, h - 1.0 / 3));
        }
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
        static string Hex(double r, double g, double b)
        {
            Func<double, int> c = v => (int)Math.Round(Math.Max(0, Math.Min(1, v)) * 255);
            return "#" + c(r).ToString("X2") + c(g).ToString("X2") + c(b).ToString("X2");
        }
    }
}
