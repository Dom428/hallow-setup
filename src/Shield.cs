// Hallow Setup - v1.6: Hallow Shield, the browser extension (no window code).
// Shield isn't a Windows app: Chrome, Edge and Brave load it "unpacked" from a folder. Setup keeps that folder at
// %LOCALAPPDATA%\Hallow Shield\chrome (and \firefox for the Firefox copy):
//   - the version in the folder: version.json (Shield 1.3 and newer), or manifest.json for 1.2 and older zips
//   - Install / Update: downloads hallow-shield-chrome-<version>.zip from the newest GitHub release only when you click,
//     checks it against the SHA-256 digest GitHub gives for it (no digest, or a mismatch: nothing is installed), and
//     replaces the folder's files. Nothing of yours is in that folder - Shield keeps its settings, My filters and stats in
//     the browser's own profile - so replacing it is safe; the browser picks up the new files when you click the reload
//     arrow on Shield's card.
//   - Uninstall: the folder goes to the Recycle Bin (remove Shield's card in the browser yourself).
// Setup never opens or changes a browser by itself: "Open Chrome extensions" etc. only run when you click them.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace HallowSetup
{
    public static class Shield
    {
        public const string Repo = "Dom428/hallow-shield";
        public const string Title = "Hallow Shield";
        public static Version Latest;              // from GitHub (null until checked)
        public static bool JustInstalled;          // show the "load it in your browser" steps

        public static string Root { get { return Path.Combine(Setup.Local, "Hallow Shield"); } }
        public static string Dir(string browser) { return Path.Combine(Root, browser == "firefox" ? "firefox" : "chrome"); }
        public static bool Installed(string browser) { return File.Exists(Path.Combine(Dir(browser), "manifest.json")); }

        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        // the version unpacked in a folder: version.json's "version", or (1.2 and older) manifest.json's
        public static Version ReadVersion(string dir)
        {
            foreach (var f in new[] { "version.json", "manifest.json" })
            {
                try
                {
                    var p = Path.Combine(dir, f); if (!File.Exists(p)) continue;
                    var d = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(p)); object v;
                    if (d != null && d.TryGetValue("version", out v) && v is string) { var ver = Setup.Ver((string)v); if (ver != null) return ver; }
                }
                catch { }
            }
            return null;
        }
        public static Version InstalledVersion(string browser) { return Installed(browser) ? ReadVersion(Dir(browser)) : null; }
        public static bool Newer(Version latest, Version installed) { return latest != null && installed != null && latest > installed; }
        public static bool UpdateAvailable(string browser) { return Newer(Latest, InstalledVersion(browser)); }
        // "sha256:<64 hex>" -> the hex, or null (no digest, another algorithm, malformed)
        public static string Sha256Of(string digest)
        {
            var d = (digest ?? "").Trim();
            if (!d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;
            var h = d.Substring(7);
            return Regex.IsMatch(h, "^[0-9a-fA-F]{64}$") ? h.ToLowerInvariant() : null;
        }
        public static string FileSha256(string path)
        {
            using (var s = File.OpenRead(path)) using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }
        public static bool ShaMatches(string path, string digest) { var want = Sha256Of(digest); return want != null && string.Equals(FileSha256(path), want, StringComparison.OrdinalIgnoreCase); }
        public static string AssetName(string browser, Version v) { return "hallow-shield-" + (browser == "firefox" ? "firefox" : "chrome") + "-" + Setup.Show(v) + ".zip"; }

        public static void CheckLatest() { try { var r = Setup.LatestReleaseOf(Repo); object t; if (r.TryGetValue("tag_name", out t)) Latest = Setup.Ver(t as string); } catch { } }

        // download (only now), check, unpack and replace the folder. Returns a sentence for the window.
        public static string Install(string browser, Action<string> progress)
        {
            var who = browser == "firefox" ? "Firefox" : "Chrome / Edge / Brave";
            Dictionary<string, object> rel;
            try { rel = Setup.LatestReleaseOf(Repo); } catch (Exception e) { return "Couldn't reach GitHub: " + e.Message; }
            var ver = rel.ContainsKey("tag_name") ? Setup.Ver(rel["tag_name"] as string) : null;
            if (ver == null) return "The latest Hallow Shield release has no version.";
            var want = AssetName(browser, ver);
            object o; var assets = rel.TryGetValue("assets", out o) ? o as System.Collections.ArrayList : null;
            var asset = assets == null ? null : assets.OfType<Dictionary<string, object>>().FirstOrDefault(x => string.Equals(x.ContainsKey("name") ? x["name"] as string : null, want, StringComparison.OrdinalIgnoreCase));
            if (asset == null) return "The latest Hallow Shield release has no " + want + ".";
            var digest = asset.ContainsKey("digest") ? asset["digest"] as string : null;
            if (Sha256Of(digest) == null) return "GitHub didn't give a checksum for " + want + " - not installed.";
            var url = asset.ContainsKey("browser_download_url") ? asset["browser_download_url"] as string : null;
            if (url == null) return "The latest Hallow Shield release has no download link.";
            var tmp = Path.Combine(Path.GetTempPath(), "hallow-setup-shield-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp);
            try
            {
                var zip = Path.Combine(tmp, "download.zip");
                progress("Downloading " + want + "...");
                try { using (var wc = new WebClient()) { wc.Headers["User-Agent"] = "HallowSetup"; wc.DownloadFile(url, zip); } }
                catch (Exception e) { return "Download failed: " + e.Message; }
                progress("Checking the download...");
                if (!ShaMatches(zip, digest)) return "The download doesn't match GitHub's checksum - Hallow Shield not installed.";
                progress("Unpacking...");
                var x = Path.Combine(tmp, "x");
                try { ZipFile.ExtractToDirectory(zip, x); } catch (Exception e) { return "Couldn't unpack the download: " + e.Message; }
                // the zip's files are the extension itself (manifest.json at the top)
                if (!File.Exists(Path.Combine(x, "manifest.json")) || !File.ReadAllText(Path.Combine(x, "manifest.json")).Contains("\"Hallow Shield\"")) return "The download isn't a Hallow Shield extension - not installed.";
                var got = ReadVersion(x);
                if (Setup.DryRun) return "Checked and unpacked Hallow Shield " + Setup.Show(got) + " for " + who + " - test run, would replace " + Dir(browser) + ".";
                progress("Replacing the files in " + Dir(browser) + "...");
                var err = Replace(x, Dir(browser));
                if (err != null) return err;
                JustInstalled = true;
                return "Hallow Shield " + Setup.Show(got) + " for " + who + " is in " + Dir(browser) + ".";
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }
        // the new files in place of the old ones: the old folder is moved aside first and put back if anything fails
        static string Replace(string from, string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            var stage = to + ".new-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var old = to + ".old-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            try { CopyTree(from, stage); }
            catch (Exception e) { try { Directory.Delete(stage, true); } catch { } return "Couldn't write to " + Path.GetDirectoryName(to) + ": " + e.Message; }
            bool moved = false;
            try
            {
                if (Directory.Exists(to)) { Directory.Move(to, old); moved = true; }
                Directory.Move(stage, to);
            }
            catch (Exception e)
            {
                if (moved && !Directory.Exists(to)) try { Directory.Move(old, to); } catch { }
                try { Directory.Delete(stage, true); } catch { }
                return "Couldn't replace " + to + " (is a file in it open?): " + e.Message;
            }
            // the previous version's files (Shield's own, nothing of yours - its data lives in the browser profile)
            if (moved) try { Directory.Delete(old, true); } catch { }
            return null;
        }
        static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(to, d.Substring(from.Length + 1)));
            foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(to, f.Substring(from.Length + 1)), true);
        }

        // Uninstall: the folder to the Recycle Bin (the empty "Hallow Shield" folder goes too)
        public static string Uninstall(string browser)
        {
            if (Setup.TestMode && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HALLOW_SETUP_LOCAL"))) return "Test mode without HALLOW_SETUP_LOCAL - nothing uninstalled.";
            var d = Dir(browser);
            if (!Directory.Exists(d)) return "Hallow Shield isn't in " + d + ".";
            if (!Uninstaller.Recycle(d)) return "Couldn't move " + d + " to the Recycle Bin (is a file in it open?).";
            try { if (Directory.Exists(Root) && !Directory.EnumerateFileSystemEntries(Root).Any()) Directory.Delete(Root); } catch { }
            JustInstalled = false;
            return "Hallow Shield's folder is in the Recycle Bin. Remove its card on your browser's extensions page too.";
        }

        // ---- the browsers' extension pages: started only when you click the button ----
        public class Browser { public string Name, Exe, Url; }
        public static readonly Browser[] Browsers = {
            new Browser { Name = "Chrome", Exe = "chrome.exe", Url = "chrome://extensions" },
            new Browser { Name = "Edge", Exe = "msedge.exe", Url = "edge://extensions" },
            new Browser { Name = "Brave", Exe = "brave.exe", Url = "brave://extensions" },
        };
        // where Windows says the browser is (App Paths), or null
        public static string FindExe(string exe)
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    try
                    {
                        using (var b = RegistryKey.OpenBaseKey(hive == Registry.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, view))
                        using (var k = b.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                        { var p = k == null ? null : k.GetValue(null) as string; if (p != null) { p = p.Trim('"'); if (File.Exists(p)) return p; } }
                    }
                    catch { }
                }
            return null;
        }
        // Opens the browser on its extensions page and copies the address too: Chromium browsers usually open their own
        // pages from the command line, but if one doesn't, you can paste it. Returns what happened, in words.
        // copy: puts text on the clipboard (the window passes its own; tests pass a stand-in). start: false in tests.
        public static string OpenExtensions(Browser b, Action<string> copy, bool start = true)
        {
            try { copy(b.Url); } catch { }
            var exe = FindExe(b.Exe);
            if (exe == null) return b.Name + " isn't installed (or Windows doesn't know where it is). The address " + b.Url + " is copied - paste it into the browser's address bar.";
            if (!start) return "Would open " + exe + " " + b.Url;
            try { Process.Start(new ProcessStartInfo(exe, b.Url) { UseShellExecute = false }); }
            catch (Exception e) { return "Couldn't start " + b.Name + " (" + e.Message + "). The address " + b.Url + " is copied - paste it into the address bar."; }
            return "Opened " + b.Name + ". If it didn't show its extensions page, paste the address (copied: " + b.Url + ") into the address bar.";
        }
        public const string Steps = "Developer mode on → Load unpacked → pick this folder. After an update, click the reload arrow on Shield's card.";
        public const string FirefoxSteps = "Firefox: about:debugging → This Firefox → Load Temporary Add-on → pick manifest.json in the folder (temporary until Shield is signed by Mozilla).";
    }
}
