// Hallow Setup - v1.18: the command line, so Hallow Hub's Install button and Hallow Remote can hand one app over to Setup.
//   HallowSetup.exe --list [--check] [--json] [--out <file>]   every family app / part: installed version, (--check) the newest on GitHub
//   HallowSetup.exe --install <App> [--quiet] [--force] [--out <file>]
//        install or update ONE app (Hub, Mixer, ..., Shield). The window opens and installs it at once (you see the progress);
//        --quiet does it without a window and exits: 0 installed / up to date, 1 failed, 2 unknown app or not a Windows app,
//        3 Hallow Toolkit needs the window (it installs as admin), 4 the installer is still running.
//        Already up to date: nothing is downloaded (exit 0) unless --force.
//   HallowSetup.exe --update <App> ...   the same, but only when it's installed (exit 2 if it isn't)
// Downloads are exactly the window's: the family's own GitHub releases only, checked against GitHub's SHA-256.
// Output goes to stdout (when a parent reads it, or the console it was started from) and, with --out, to that file (UTF-8).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace HallowSetup
{
    public static class Cli
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
        [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int n);

        public static bool Wants(string[] args) { return Has(args, "--list") || Has(args, "--install") || Has(args, "--update"); }
        public static bool Has(string[] args, string flag) { return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)); }
        public static string After(string[] args, string flag)
        {
            for (int i = 0; i + 1 < args.Length; i++) if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
        // the app the window should install as soon as it opens (--install / --update without --quiet), or null
        public static string WindowInstall(string[] args) { return Has(args, "--quiet") || Has(args, "--list") ? null : After(args, "--install") ?? After(args, "--update"); }

        static void Write(string[] args, string text)
        {
            try
            {
                var h = GetStdHandle(-11);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) { if (AttachConsole(-1)) { var w = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }; Console.SetOut(w); Console.WriteLine(); } }
                Console.Out.WriteLine(text); Console.Out.Flush();
            }
            catch { }
            var o = After(args, "--out"); if (o != null) try { File.WriteAllText(o, text + Environment.NewLine, new UTF8Encoding(false)); } catch { }
        }

        // headless commands: returns the exit code, or null when the window should open
        public static int? Run(string[] args)
        {
            if (Has(args, "--list")) { Write(args, List(Has(args, "--check"), Has(args, "--json"))); return 0; }
            var name = After(args, "--install") ?? After(args, "--update");
            if (name == null && (Has(args, "--install") || Has(args, "--update"))) { Write(args, "error use: --install <App> (one of " + string.Join(", ", Names()) + ")"); return 2; }
            if (name == null) return null;
            var check = Check(name, Has(args, "--update"));
            if (check != null) { Write(args, "error " + check.Item2); return check.Item1; }
            if (!Has(args, "--quiet")) return null;   // the window does it
            var r = InstallOne(name, Has(args, "--force"), p => { });
            Write(args, (r.Item1 == 0 ? "ok " : "error ") + r.Item2); return r.Item1;
        }
        public static IEnumerable<string> Names() { return Setup.Apps.Select(a => a.Name).Concat(new[] { "Shield" }); }

        // can <name> be installed from here? null = yes, else (exit code, why)
        public static Tuple<int, string> Check(string name, bool updateOnly)
        {
            var other = Setup.FindOther(name);
            if (other != null) return Tuple.Create(2, other.Title + " isn't a Windows app, so Hallow Setup doesn't install it. " + other.Desc + " (" + other.Url + ")");
            if (Setup.Norm(name) == "setup") return Tuple.Create(2, "Hallow Setup updates itself from its window (Update Hallow Setup).");
            if (Setup.Norm(name) == "shield") return updateOnly && !Shield.Installed("chrome") ? Tuple.Create(2, "Hallow Shield isn't installed - use --install.") : null;
            var a = Setup.Find(name);
            if (a == null) return Tuple.Create(2, "There's no Hallow app called \"" + name + "\". Apps: " + string.Join(", ", Names()) + ".");
            if (updateOnly && !a.Installed) return Tuple.Create(2, a.Title + " isn't installed - use --install.");
            return null;
        }
        public static bool Quietable(string name) { var a = Setup.Find(name); return a == null || !a.IsToolkit; }

        // installs or updates one app (or Shield's Chrome / Edge / Brave copy): (exit code, sentence)
        public static Tuple<int, string> InstallOne(string name, bool force, Action<string> progress)
        {
            var c = Check(name, false); if (c != null) return c;
            if (Setup.Norm(name) == "shield")
            {
                if (!force) { Shield.CheckLatest(); var iv = Shield.InstalledVersion("chrome"); if (iv != null && Shield.Latest != null && !(Shield.Latest > iv)) return Tuple.Create(0, "Hallow Shield " + Setup.Show(iv) + " is up to date."); }
                string m; try { m = Shield.Install("chrome", progress); } catch (Exception e) { m = "Install failed: " + e.Message; }
                return Tuple.Create(m.Contains(" is in ") || m.Contains("test run") ? 0 : 1, m);
            }
            var a = Setup.Find(name);
            if (a.IsToolkit && !Setup.DryRun) return Tuple.Create(3, "Hallow Toolkit installs as admin - open Hallow Setup (HallowSetup.exe --install Toolkit) so Windows can ask you.");
            if (!force && a.Installed)
            {
                try { var r = Setup.LatestReleaseOf(a.Repo); object t; if (r.TryGetValue("tag_name", out t)) a.Latest = Setup.Ver(t as string); } catch { }
                var iv = a.InstalledVersion;
                if (iv != null && a.Latest != null && !(a.Latest > iv)) return Tuple.Create(0, a.Title + " " + Setup.Show(iv) + " is up to date.");
            }
            string msg; try { msg = Setup.Install(a, progress); } catch (Exception e) { msg = "Install failed: " + e.Message; }
            return Tuple.Create(Setup.Succeeded(msg) ? 0 : msg.Contains("is still running") ? 4 : 1, msg);
        }

        // --list: one line per app / part, or JSON
        public static string List(bool check, bool json)
        {
            if (check) { Setup.CheckLatest(); Shield.CheckLatest(); foreach (var o in Setup.Others) try { var r = Setup.LatestReleaseOf(o.Repo); object t; if (r.TryGetValue("tag_name", out t)) o.Latest = Setup.Ver(t as string); } catch { } }
            var items = new List<Dictionary<string, object>>();
            foreach (var a in Setup.Apps)
            {
                var iv = a.InstalledVersion;
                items.Add(new Dictionary<string, object> { { "name", a.Name }, { "title", a.Title }, { "kind", a.IsToolkit ? "toolkit" : "app" }, { "installable", true }, { "repo", a.Repo },
                    { "installed", a.Installed }, { "version", iv == null ? null : Setup.Show(iv) }, { "latest", a.Latest == null ? null : Setup.Show(a.Latest) },
                    { "updateAvailable", a.UpdateAvailable }, { "running", a.Installed && a.Running }, { "needsAdmin", a.IsToolkit } });
            }
            var sv = Shield.InstalledVersion("chrome");
            items.Add(new Dictionary<string, object> { { "name", "Shield" }, { "title", "Hallow Shield" }, { "kind", "extension" }, { "installable", true }, { "repo", Shield.Repo },
                { "installed", Shield.Installed("chrome") }, { "version", sv == null ? null : Setup.Show(sv) }, { "latest", Shield.Latest == null ? null : Setup.Show(Shield.Latest) },
                { "updateAvailable", Shield.UpdateAvailable("chrome") }, { "running", false }, { "needsAdmin", false } });
            foreach (var o in Setup.Others)
                items.Add(new Dictionary<string, object> { { "name", o.Name }, { "title", o.Title }, { "kind", o.Kind }, { "installable", false }, { "repo", o.Repo },
                    { "installed", false }, { "version", null }, { "latest", o.Latest == null ? null : Setup.Show(o.Latest) }, { "updateAvailable", false }, { "running", false }, { "needsAdmin", false }, { "note", o.Desc } });
            if (json) return new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "app", "Setup" }, { "version", Program.Version }, { "items", items } });
            var sb = new StringBuilder();
            foreach (var i in items)
                sb.AppendLine(string.Format("{0,-14}{1,-10}{2,-12}{3}", i["name"], i["kind"], (bool)i["installable"] ? (i["version"] as string ?? "-") : "-",
                    !(bool)i["installable"] ? "not a Windows app: " + i["note"] : (bool)i["updateAvailable"] ? i["latest"] + " is out" : i["latest"] != null ? "latest " + i["latest"] : ""));
            return sb.ToString().TrimEnd();
        }
    }
}
