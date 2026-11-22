// Hallow Setup - unit tests (build.ps1 -Tests -> tests\SetupTests.exe). Pure checks with stand-ins: never the real
// theme.json, settings or Windows setting.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HallowSetup.Tests
{
    public static partial class Run
    {
        static int pass, fail;
        static void Ok(bool c, string name) { if (c) { pass++; Console.WriteLine("ok   " + name); } else { fail++; Console.WriteLine("FAIL " + name); } }

        public static int Main()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "hallow-setup-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmp);
            // v1.18: 5-minute kill timeout - a stand-in or a child process that hangs can't hang the build
            new System.Threading.Thread(() => { System.Threading.Thread.Sleep(TimeSpan.FromMinutes(5)); Console.WriteLine("FAIL the tests took longer than 5 minutes - killed"); Environment.Exit(3); }) { IsBackground = true }.Start();
            try { Mode(tmp); Files(tmp); Colours(); ThemeXaml(); NoHardCoded(); LightContrast(); CliTests(tmp); }
            catch (Exception e) { fail++; Console.WriteLine("FAIL exception " + e); }
            finally { try { Directory.Delete(tmp, true); } catch { } }
            Console.WriteLine(pass + " passed, " + fail + " failed");
            return fail == 0 ? 0 : 1;
        }

        static void Mode(string tmp)
        {
            Func<bool?> sysLight = () => true, sysDark = () => false, sysNone = () => null, sysThrows = () => { throw new Exception("x"); };
            Ok(!Look.ResolveLight("follow", null, sysLight), "missing mode -> dark");
            Ok(!Look.ResolveLight(null, null, sysLight), "no settings, no mode -> dark");
            Ok(!Look.ResolveLight("follow", "dark", sysLight), "mode dark -> dark");
            Ok(Look.ResolveLight("follow", "light", sysDark), "mode light -> light");
            Ok(Look.ResolveLight("follow", "LIGHT", sysDark), "mode is case-insensitive");
            Ok(!Look.ResolveLight("follow", "purple", sysLight), "unknown mode -> dark");
            Ok(Look.ResolveLight("follow", "system", sysLight), "system + Windows light (fake) -> light");
            Ok(!Look.ResolveLight("follow", "system", sysDark), "system + Windows dark (fake) -> dark");
            Ok(!Look.ResolveLight("follow", "system", sysNone), "system + no registry value -> dark");
            Ok(!Look.ResolveLight("follow", "system", sysThrows), "system + registry error -> dark");
            Ok(!Look.ResolveLight("dark", "light", sysLight), "Setup's Look Dark wins over Hallow light");
            Ok(Look.ResolveLight("light", "dark", sysDark), "Setup's Look Light wins over Hallow dark");
            Ok(Look.ResolveLight("light", null, sysDark), "Setup's Look Light wins when theme.json is missing");
            Ok(!Look.ResolveLight("dark", "system", sysLight), "Setup's Look Dark wins over system");
        }

        static void Files(string tmp)
        {
            var theme = Path.Combine(tmp, "theme.json");
            Ok(Look.ThemeMode(theme) == null, "theme.json missing -> no mode");
            File.WriteAllText(theme, "{\"accent\":\"#8B5CF6\",\"updated\":\"2026-11-21T00:00:00Z\"}");
            Ok(Look.ThemeMode(theme) == null, "theme.json without mode -> no mode (dark)");
            File.WriteAllText(theme, "{\"accent\":\"#8B5CF6\",\"mode\":\"system\",\"future\":{\"x\":1}}");
            Ok(Look.ThemeMode(theme) == "system", "mode read, unknown fields tolerated");
            File.WriteAllText(theme, "{not json");
            Ok(Look.ThemeMode(theme) == null, "broken theme.json -> no mode, no crash");
            File.WriteAllText(theme, "{\"mode\":5}");
            Ok(Look.ThemeMode(theme) == null, "mode of the wrong type -> no mode");

            var set = Path.Combine(tmp, "sub", "settings.json");
            Ok(Look.AppLook(set) == "follow", "no settings -> Follow Hallow");
            Look.SaveAppLook(set, "light");
            Ok(Look.AppLook(set) == "light", "Look Light saved and read back");
            File.WriteAllText(set, "{\"look\":\"dark\",\"other\":\"keep me\"}");
            Look.SaveAppLook(set, "follow");
            var txt = File.ReadAllText(set);
            Ok(Look.AppLook(set) == "follow" && txt.Contains("keep me"), "saving the Look keeps other fields");
            Ok(!File.Exists(set + ".tmp"), "atomic write leaves no temp file");
            File.WriteAllText(set, "garbage");
            Ok(Look.AppLook(set) == "follow", "broken settings -> Follow Hallow");
            Look.SaveAppLook(set, "weird");
            Ok(Look.AppLook(set) == "follow", "unknown Look saved as follow");
        }

        static void Near(string a, string b, int tol, string name)
        {
            Func<string, int, int> ch = (h, i) => Convert.ToInt32(h.Substring(1 + 2 * i, 2), 16);
            Ok(Enumerable.Range(0, 3).All(i => Math.Abs(ch(a, i) - ch(b, i)) <= tol), name + " (" + a + " vs " + b + ")");
        }

        static void Colours()
        {
            var orange = Look.ForText("#F07820", "#FFFFFF");
            Near(orange, "#B85410", 6, "default orange for text is about #B85410");
            Ok(Look.Contrast(orange, "#FFFFFF") >= 4.5, "darkened orange >= 4.5:1 on white");
            foreach (var acc in new[] { "#8B5CF6", "#22C55E", "#EAB308", "#06B6D4", "#EC4899", "#FFFFFF", "#000000", "#3B82F6" })
            {
                var t = Look.ForText(acc, "#FFFFFF");
                Ok(Look.Contrast(t, "#FFFFFF") >= 4.5, "accent " + acc + " -> " + t + " >= 4.5:1 on white");
                Ok(Look.Contrast(t, "#F6F4FA") >= 3.0, "accent " + acc + " title (large) >= 3:1 on the background");
            }
            Ok(Look.ForText("#1E1A26", "#FFFFFF") == "#1E1A26", "a colour that already reads is kept");

            Look.Light = true;
            try
            {
                string bg = "#F6F4FA", panel = "#FFFFFF", panel2 = "#EEEAF5", text = "#1E1A26", muted = "#6B6478";
                foreach (var b in new[] { bg, panel, panel2 })
                {
                    Ok(Look.Contrast(text, b) >= 4.5, "text on " + b);
                    Ok(Look.Contrast(muted, b) >= 4.5, "muted text on " + b);
                    if (b == panel2) continue;   // only text and muted text sit on buttons / hover
                    Ok(Look.Contrast(Look.Green, b) >= 4.5, "green status " + Look.Green + " on " + b);
                    Ok(Look.Contrast(Look.Amber, b) >= 4.5, "amber status " + Look.Amber + " on " + b);
                    Ok(Look.Contrast(Look.Red, b) >= 4.5, "red " + Look.Red + " on " + b);
                    Ok(Look.Contrast("#7A5CC0", b) >= 4.5, "lilac on " + b);
                    Ok(Look.Contrast(Look.TextAccent("#F07820"), b) >= 4.5 || b == bg && Look.Contrast(Look.TextAccent("#F07820"), b) >= 3, "accent text on " + b);
                }
                Ok(Look.Contrast("#1A0E04", "#F07820") >= 4.5, "dark text on the filled accent button");
                Ok(Look.Contrast("#1E8E4F", panel) >= 3, "switch on (icon-size) >= 3:1 on the panel");
                Ok(Look.Contrast("#6B6478", panel2) >= 3, "combo arrow >= 3:1");
            }
            finally { Look.Light = false; }
            Ok(Look.Green == "#6ED28C" && Look.Amber == "#F0BE5A" && Look.TextAccent("#F07820") == "#F07820", "dark mode keeps today's colours");
        }

        static string ThemeSrc()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            return File.ReadAllText(Path.Combine(dir, @"..\src\Theme.xaml"));
        }
        static void ThemeXaml()
        {
            var x = ThemeSrc();
            Ok(Look.Theme(x, null, false) == x, "dark + default accent = Theme.xaml unchanged");
            Ok(Look.Theme(x, "#8B5CF6", false) == x.Replace("#F07820", "#8B5CF6"), "dark + Hallow accent = only the accent swapped");
            var l = Look.Theme(x, "#8B5CF6", true);
            for (int i = 0; i < Look.Map.GetLength(0); i++) Ok(l.IndexOf(Look.Map[i, 0], StringComparison.OrdinalIgnoreCase) < 0, "light theme has no dark " + Look.Map[i, 0]);
            Ok(l.Contains("#8B5CF6"), "light theme keeps the real accent for filled buttons");
            Ok(l.Contains("CaretBrush\" Value=\"" + Look.ForText("#8B5CF6", "#FFFFFF")), "light theme caret uses the darkened accent");
            Ok(l.Contains("x:Name=\"hl\" Background=\"{DynamicResource HoverTint}\"") && l.Contains("<SolidColorBrush x:Key=\"HoverTint\" Color=\"#000000\"/>"), "light theme hover overlay darkens");
            // every colour left is from the light palette, the accent, or a known safe one
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "#FFFFFF", "#000000", "#1A0E04", "#F59A50", "#8B5CF6", Look.ForText("#8B5CF6", "#FFFFFF"), Look.LightFill("#8B5CF6"), Look.FillHover(Look.LightFill("#8B5CF6")) };
            for (int i = 0; i < Look.Map.GetLength(0); i++) allowed.Add(Look.Map[i, 1]);
            var stray = Regex.Matches(l, "#[0-9A-Fa-f]{6}\\b").Cast<Match>().Select(m => m.Value).Where(v => !allowed.Contains(v)).Distinct().ToList();
            Ok(stray.Count == 0, "no hard-coded colours left in the light theme" + (stray.Count > 0 ? ": " + string.Join(" ", stray) : ""));
        }
        static void NoHardCoded()
        {
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\src");
            foreach (var f in Directory.GetFiles(dir, "*.cs"))
            {
                if (Path.GetFileName(f) == "Look.cs") continue;
                var hits = Regex.Matches(File.ReadAllText(f), "B\\(\"#[0-9A-Fa-f]{6}\"\\)").Cast<Match>().Select(m => m.Value).ToList();
                Ok(hits.Count == 0, Path.GetFileName(f) + ": no hard-coded colours (use the theme or Look)" + (hits.Count > 0 ? ": " + string.Join(" ", hits) : ""));
            }
        }
    }
}
