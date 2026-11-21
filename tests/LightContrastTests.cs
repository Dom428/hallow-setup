// Hallow Setup 1.12 - light mode contrast: every text / icon colour on the surface it really sits on in light mode, for
// several accents (default orange, violet, yellow, light cyan, dark blue and more). Part of tests\SetupTests.exe.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HallowSetup.Tests
{
    public static partial class Run
    {
        // accent -> light fill, text on the fill, fill hover, accent for text on #EEEAF5: worked out with Hallow Toolkit 5.16's own
        // functions (HallowFamily.ps1 Get-LightAccentFill / Get-AccentFillText / Get-LightTextAccent, HallowToolkit.ps1 fill * 0.9)
        static readonly string[,] Toolkit = {
            { "#F07820", "#F07820", "#1E1A26", "#D86C1D", "#A94E0B" },
            { "#8B5CF6", "#8452F5", "#FFFFFF", "#774ADC", "#763FF4" },
            { "#FACC15", "#FACC15", "#1E1A26", "#E1B813", "#7E6503" },
            { "#67E8F9", "#67E8F9", "#1E1A26", "#5DD1E0", "#057685" },
            { "#1E3A8A", "#1E3A8A", "#FFFFFF", "#1B347C", "#1E3A8A" },
            { "#22C55E", "#22C55E", "#1E1A26", "#1FB155", "#157B3B" },
            { "#EC4899", "#EC4899", "#1E1A26", "#D4418A", "#CD1570" },
            { "#3B82F6", "#3B82F6", "#1E1A26", "#3575DD", "#0B5FE9" },
            { "#FFFFFF", "#FFFFFF", "#1E1A26", "#E6E6E6", "#696969" },
            { "#000000", "#000000", "#FFFFFF", "#000000", "#000000" },
        };
        static string Mul(string hex, double f)
        {
            Func<int, string> x = i => ((int)Math.Round(Convert.ToInt32(hex.Substring(1 + 2 * i, 2), 16) * f)).ToString("X2");
            return "#" + x(0) + x(1) + x(2);
        }

        // XamlReader on an STA thread; the theme's converters live in this test exe (it compiles src\*.cs too)
        static bool Parses(string xaml)
        {
            Exception err = null;
            var th = new System.Threading.Thread(() => { try { System.Windows.Markup.XamlReader.Parse(xaml.Replace("assembly=HallowSetup", "assembly=" + typeof(Look).Assembly.GetName().Name)); } catch (Exception e) { err = e; } });
            th.SetApartmentState(System.Threading.ApartmentState.STA); th.Start(); th.Join();
            if (err != null) Console.WriteLine("     " + err.Message);
            return err == null;
        }

        static void LightContrast()
        {
            const string bg = "#F6F4FA", panel = "#FFFFFF", panel2 = "#EEEAF5", border = "#DAD3E6", text = "#1E1A26", muted = "#6B6478";
            // the same rule as Hallow Toolkit 5.16, value for value
            for (int i = 0; i < Toolkit.GetLength(0); i++)
            {
                string a = Toolkit[i, 0];
                Ok(Look.LightFill(a) == Toolkit[i, 1] && Look.FillText(Look.LightFill(a)) == Toolkit[i, 2] && (Look.FillHover(Look.LightFill(a)) == Toolkit[i, 3] || Look.Contrast(Toolkit[i, 2], Toolkit[i, 3]) < 4.5)
                    && Look.Darken(a, 4.5, panel2) == Toolkit[i, 4], "accent " + a + ": fill / text / hover / text accent match Hallow Toolkit 5.16");
            }
            var x = ThemeSrc();
            Look.Light = true;
            try
            {
                foreach (var a in new[] { "#F07820", "#8B5CF6", "#FACC15", "#67E8F9", "#1E3A8A", "#22C55E", "#EC4899", "#3B82F6", "#FFFFFF", "#000000" })
                {
                    // filled accent buttons (Install / Update / Install ticked / Update Hallow Setup / Uninstall): small SemiBold text
                    string fill = Look.LightFill(a), ft = Look.FillText(fill), fh = Look.FillHover(fill);
                    Ok(Look.Contrast(ft, fill) >= 4.5, "accent " + a + ": button text " + ft + " on fill " + fill + " >= 4.5:1 (" + Look.Contrast(ft, fill).ToString("0.00") + ")");
                    Ok(Look.Contrast(ft, fh) >= 4.5, "accent " + a + ": button text on hover " + fh + " >= 4.5:1 (" + Look.Contrast(ft, fh).ToString("0.00") + ")");
                    var l = Look.Theme(x, a, true);
                    Ok(Parses(l), "accent " + a + ": the light theme loads in WPF");
                    Ok(l.Contains("<Setter Property=\"Background\" Value=\"" + fill + "\"/><Setter Property=\"BorderBrush\" Value=\"" + fill + "\"/><Setter Property=\"Foreground\" Value=\"" + ft + "\"/>")
                        && l.Contains("<Setter Property=\"Background\" Value=\"" + fh + "\"/>") && !System.Text.RegularExpressions.Regex.Match(l, "<Style x:Key=\"Accent\".*?</Style>", System.Text.RegularExpressions.RegexOptions.Singleline).Value.Contains("#1A0E04"),
                        "accent " + a + ": light theme's Accent style uses the fill, its text and its hover");
                    // the accent as text / thin lines: HALLOW title (22 px bold = large), app icons, uninstall dialog dots, caret and focus line
                    var ta = Look.TextAccent(a);
                    foreach (var b in new[] { bg, panel }) Ok(Look.Contrast(ta, b) >= 3, "accent " + a + ": title / icons " + ta + " >= 3:1 on " + b);
                    Ok(Look.Contrast(ta, panel) >= 3, "accent " + a + ": caret / focus line >= 3:1 in the text fields");
                }
                // text on every surface it sits on: window (Bg), cards / menus / tooltips / fields (Panel), buttons / combo (Panel2),
                // menu + combo item highlight and combo hover (Border)
                foreach (var b in new[] { bg, panel, panel2, border }) Ok(Look.Contrast(text, b) >= 4.5, "text on " + b);
                foreach (var b in new[] { bg, panel }) Ok(Look.Contrast(muted, b) >= 4.5, "muted text (versions, descriptions, data path) on " + b);
                Ok(Look.Contrast(muted, border) >= 3, "combo arrow (icon) on the combo hover " + border);
                // buttons: hover / press darken the button by a black overlay at 7 % / 14 %
                foreach (var f in new[] { 0.93, 0.86 }) Ok(Look.Contrast(text, Mul(panel2, f)) >= 4.5, "button text on the hover / press " + Mul(panel2, f));
                // status text (versions, "is out", the status line, uninstall notes) sits on the window and on cards
                foreach (var b in new[] { bg, panel })
                {
                    Ok(Look.Contrast(Look.Green, b) >= 4.5, "green status text " + Look.Green + " on " + b);
                    Ok(Look.Contrast(Look.Amber, b) >= 4.5, "amber status text " + Look.Amber + " on " + b);
                    Ok(Look.Contrast(Look.Red, b) >= 4.5, "red " + Look.Red + " on " + b);
                }
                Ok(Look.Contrast("#1E8E4F", panel) >= 3, "switch on track >= 3:1 on the card");
            }
            finally { Look.Light = false; }
            Ok(Look.Theme(x, "#8B5CF6", false) == x.Replace("#F07820", "#8B5CF6"), "dark mode Accent style unchanged");
            Ok(Parses(Look.Theme(x, "#8B5CF6", false)), "the dark theme loads in WPF");
        }
    }
}
