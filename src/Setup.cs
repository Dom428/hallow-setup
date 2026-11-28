// Hallow Setup - what's installed, what's newest, and installing / updating the Hallow apps (no window code).
// Nothing is downloaded until you click Install or Update. Every download is checked against the SHA-256 that GitHub
// reports for it, then the app's own installer runs as you (never as admin). Hallow Toolkit's installer needs admin,
// so Setup only starts it and Windows asks you (UAC).
// Developer test overrides (never set them for real use): HALLOW_SETUP_API (a stand-in for api.github.com),
// HALLOW_SETUP_LOCAL (stand-in for %LOCALAPPDATA%), HALLOW_SETUP_TOOLKIT (Toolkit folder), HALLOW_SETUP_RUNKEY
// (an HKCU key instead of ...\Run), HALLOW_SETUP_DRYRUN=1 (check and unpack, but don't run the installer).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace HallowSetup
{
    public class HallowApp
    {
        public string Name, Icon, Desc;
        public bool IsToolkit { get { return Name == "Toolkit"; } }
        // v1.11: Hallow Browser runs only while its window is open - no tray, no Start with Windows
        public bool NoTray { get { return Name == "Browser"; } }
        public string Title { get { return "Hallow " + Name; } }
        public string Repo { get { return "Dom428/hallow-" + Name.ToLowerInvariant(); } }
        public bool Ticked;   // v1.1: picked for "Install ticked"
        public string Exe { get { return IsToolkit ? Path.Combine(Setup.ToolkitDir, "HallowToolkit.ps1") : Path.Combine(Setup.Local, "Programs", "Hallow " + Name, "Hallow" + Name + ".exe"); } }
        public bool Installed { get { return File.Exists(Exe); } }
        public Version InstalledVersion
        {
            get
            {
                try
                {
                    if (!Installed) return null;
                    if (IsToolkit) { var m = Regex.Match(File.ReadAllText(Path.Combine(Setup.ToolkitDir, "HallowCore.ps1")), "AppVersion = '([^']+)'"); return m.Success ? Setup.Ver(m.Groups[1].Value) : null; }
                    return Setup.Ver(FileVersionInfo.GetVersionInfo(Exe).FileVersion);
                }
                catch { return null; }
            }
        }
        public Version Latest;          // from GitHub (null until checked)
        public bool Running { get { return !IsToolkit && Uninstaller.Processes(this).Length > 0; } }   // v1.3: only copies running from the installed exe
        public bool UpdateAvailable { get { var v = InstalledVersion; return v != null && Latest != null && Latest > v; } }

        // Start with Windows: the app's own HKCU Run value "Hallow<App>" = "<exe>" --tray (what its own Settings switch writes)
        public bool StartsWithWindows
        {
            get { try { using (var k = Registry.CurrentUser.OpenSubKey(Setup.RunKey)) { var v = k == null ? null : k.GetValue("Hallow" + Name) as string; return v != null; } } catch { return false; } }
        }
        public void SetStartWithWindows(bool on)
        {
            if (IsToolkit) return;
            using (var k = Registry.CurrentUser.CreateSubKey(Setup.RunKey))
            {
                if (on) k.SetValue("Hallow" + Name, "\"" + Exe + "\" --tray"); else k.DeleteValue("Hallow" + Name, false);
            }
        }
    }

    // v1.18: a family part that isn't a Windows app (a NAS server, a phone app)
    public class FamilyPart
    {
        public string Name, Icon, Desc, Kind;
        public string Title { get { return "Hallow " + Name; } }
        public string Repo { get { return "Dom428/hallow-" + Name.ToLowerInvariant(); } }
        public string Url { get { return "https://github.com/" + Repo; } }
        public Version Latest;
    }

    public static class Setup
    {
        public static readonly List<HallowApp> Apps = new List<HallowApp> {
            new HallowApp { Name = "Mixer",   Icon = "sliders",       Desc = "Per-app volume mixer: game, chat, music and media faders" },
            new HallowApp { Name = "Hub",     Icon = "gamepad",       Desc = "All your games in one place, with playtime, presets and game sessions" },
            new HallowApp { Name = "Vault",   Icon = "vault",         Desc = "Backs up your game saves when a game closes and once a day" },
            new HallowApp { Name = "Overlay", Icon = "display",       Desc = "See-through stats panel over your games, anti-cheat safe" },
            new HallowApp { Name = "Remote",  Icon = "mobile-screen", Desc = "Your phone as a remote for the Hallow apps" },
            new HallowApp { Name = "Clips",   Icon = "film",          Desc = "Instant replay: save the last moments of your game" },
            new HallowApp { Name = "Keys",    Icon = "key",           Desc = "Your passwords, on your PC: an encrypted vault with browser autofill" },
            new HallowApp { Name = "Assist",  Icon = "hand-holding-heart", Desc = "Remote help for friends and family through your own relay, with their OK" },
            new HallowApp { Name = "Guard",   Icon = "shield-halved", Desc = "A friendly safety center on top of Microsoft Defender (uses Defender's engine - not an antivirus of its own)" },
            new HallowApp { Name = "Tasks",   Icon = "gauge-high",    Desc = "A better Task Manager: apps grouped like Task Manager, what's slowing your game and live graphs (no admin)" },
            new HallowApp { Name = "Browser", Icon = "globe",         Desc = "A fast browser for gamers: Hallow Shield built in, Keys autofill, game mode (never makes itself your default browser)" },
            new HallowApp { Name = "VPN",     Icon = "globe",         Desc = "Your NordVPN subscription on Windows' own VPN client: game-aware, split tunnel, optional kill switch (no drivers)" },
            new HallowApp { Name = "Torrents", Icon = "magnet",       Desc = "A game-aware torrent client that only runs over your VPN: every connection locked to it, Defender-checked downloads" },
            new HallowApp { Name = "Financials", Icon = "wallet",     Desc = "Private personal finance in an encrypted vault: import bank statements, budgets, subscriptions, bill reminders (never moves money)" },
            new HallowApp { Name = "Home",    Icon = "house",         Desc = "Your pets' feeder and fountain, cameras and lock through your own Home Assistant: smarter alerts, Feed now with a confirm" },
            new HallowApp { Name = "Email",   Icon = "envelope",      Desc = "A private email client: one inbox for all your accounts, trackers and phishing blocked, a private spam filter" },
            new HallowApp { Name = "Gear",    Icon = "battery-three-quarters", Desc = "Battery, charging and wired / wireless for your Logitech gear and Bluetooth devices - read-only" },
            new HallowApp { Name = "Capture", Icon = "camera",        Desc = "Screenshots with an editor, OCR and a private, encrypted clipboard history (never synced)" },   // v1.19
            new HallowApp { Name = "Display", Icon = "display",       Desc = "Brightness for every monitor, per-game resolution / refresh / HDR, window layouts per monitor setup, night mode" },   // v1.19
            new HallowApp { Name = "Find",    Icon = "magnifying-glass", Desc = "Instant search: every file on your drives as you type, apps, sums and units, the web, Windows settings - no admin, nothing leaves the PC" },   // v1.20
            new HallowApp { Name = "Toolkit", Icon = "bolt",          Desc = "One-click PC tune-up, monitoring, gaming and clean-up tools (installs as admin)" },
        };

        // v1.18: the family parts that aren't Windows apps - listed (window, --list) so the catalogue is complete, never installed here
        public static readonly List<FamilyPart> Others = new List<FamilyPart> {
            new FamilyPart { Name = "Places", Icon = "location-dot", Kind = "server", Desc = "Private family location sharing on your own server (OwnTracks on the phones). It runs in Docker on your NAS - see its NAS-DEPLOY.md." },
            new FamilyPart { Name = "Authenticator", Icon = "lock", Kind = "phone", Desc = "Your 2FA codes on your phone: an installable web app that Hallow Remote 1.19+ delivers to your phone (Home > Authenticator)." },
        };
        // v1.18: an app or part by name for the command line: "Hub", "hub", "Hallow Hub", "HallowHub" all work
        public static string Norm(string name)
        {
            var n = (name ?? "").Trim().Trim('"').Replace(" ", "");
            if (n.StartsWith("Hallow", StringComparison.OrdinalIgnoreCase) && n.Length > 6) n = n.Substring(6);
            return n.ToLowerInvariant();
        }
        public static HallowApp Find(string name) { var n = Norm(name); return n.Length == 0 ? null : Apps.FirstOrDefault(a => a.Name.ToLowerInvariant() == n); }
        public static FamilyPart FindOther(string name) { var n = Norm(name); return n.Length == 0 ? null : Others.FirstOrDefault(a => a.Name.ToLowerInvariant() == n); }
        // v1.18: downloads only ever come from the family's own GitHub releases (https://github.com/<repo>/releases/download/...);
        // the developer test stand-in (HALLOW_SETUP_API) may serve its own files from the same address
        public static bool AllowedDownload(string url, string repo)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(repo)) return false;
            if (url.StartsWith("https://github.com/" + repo + "/releases/download/", StringComparison.OrdinalIgnoreCase) && !url.Contains("..")) return true;
            return TestMode && url.StartsWith(Api + "/", StringComparison.OrdinalIgnoreCase);
        }

        static string Env(string n) { var v = Environment.GetEnvironmentVariable(n); return string.IsNullOrEmpty(v) ? null : v; }
        public static string Api { get { return (Env("HALLOW_SETUP_API") ?? "https://api.github.com").TrimEnd('/'); } }
        public static string Local { get { return Env("HALLOW_SETUP_LOCAL") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        public static string ToolkitDir { get { return Env("HALLOW_SETUP_TOOLKIT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "HallowToolkit"); } }
        public static string RunKey { get { return Env("HALLOW_SETUP_RUNKEY") ?? @"Software\Microsoft\Windows\CurrentVersion\Run"; } }
        public static bool DryRun { get { return Env("HALLOW_SETUP_DRYRUN") == "1"; } }
        public static bool TestMode { get { return Env("HALLOW_SETUP_API") != null; } }

        // "v3.12", "3.12.0.0" -> 3.12.0.0, so tags and file versions compare
        public static Version Ver(string text)
        {
            var m = Regex.Match(text ?? "", @"\d+(\.\d+){0,3}"); if (!m.Success) return null;
            var p = m.Value.Split('.').ToList(); while (p.Count < 4) p.Add("0");
            try { return new Version(string.Join(".", p)); } catch { return null; }
        }
        public static string Show(Version v)
        {
            if (v == null) return "-";
            var s = v.Major + "." + v.Minor; if (v.Build > 0 || v.Revision > 0) s += "." + v.Build; if (v.Revision > 0) s += "." + v.Revision; return s;
        }

        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        static Dictionary<string, object> LatestRelease(HallowApp a) { return LatestReleaseOf(a.Repo); }
        // v1.6: also used for Hallow Shield (Shield.cs)
        public static Dictionary<string, object> LatestReleaseOf(string repo)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(Api + "/repos/" + repo + "/releases/latest");
            req.UserAgent = "HallowSetup"; req.Accept = "application/vnd.github+json"; req.Timeout = 15000;
            using (var r = req.GetResponse()) using (var rd = new StreamReader(r.GetResponseStream())) return js.Deserialize<Dictionary<string, object>>(rd.ReadToEnd());
        }
        // newest release of each app (only the release info, nothing is downloaded); failures leave Latest as it was
        public static void CheckLatest()
        {
            foreach (var a in Apps)
                try { var r = LatestRelease(a); object t; if (r.TryGetValue("tag_name", out t)) a.Latest = Ver(t as string); } catch { }
        }

        // v1.18: the newest release of the parts that aren't Windows apps (shown only)
        public static void CheckOthers() { foreach (var o in Others) try { var r = LatestReleaseOf(o.Repo); object t; if (r.TryGetValue("tag_name", out t)) o.Latest = Ver(t as string); } catch { } }
        // ---- install / update: download, check the SHA-256 GitHub gives for the zip, unpack, run the app's installer as you.
        // Returns a sentence for the window. progress gets short steps.
        public static string Install(HallowApp a, Action<string> progress)
        {
            Dictionary<string, object> rel;
            try { rel = LatestRelease(a); } catch (Exception e) { return "Couldn't reach GitHub: " + e.Message; }
            object o; var assets = rel.TryGetValue("assets", out o) ? o as System.Collections.ArrayList : null;
            var asset = assets == null ? null : assets.OfType<Dictionary<string, object>>().FirstOrDefault(x => (x.ContainsKey("name") ? x["name"] as string ?? "" : "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (asset == null) return "The latest " + a.Title + " release has no download.";
            var digest = asset.ContainsKey("digest") ? asset["digest"] as string ?? "" : "";
            var sha = digest.StartsWith("sha256:") ? digest.Substring(7) : "";
            if (!Regex.IsMatch(sha, "^[0-9a-fA-F]{64}$")) return "GitHub didn't give a checksum for the " + a.Title + " download - not installed.";
            var url = asset.ContainsKey("browser_download_url") ? asset["browser_download_url"] as string : null;
            if (url == null) return "The latest " + a.Title + " release has no download link.";
            if (!AllowedDownload(url, a.Repo)) return "The " + a.Title + " download isn't from its own GitHub releases - not installed.";   // v1.18
            var tmp = Path.Combine(Path.GetTempPath(), "hallow-setup-" + a.Name.ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp); var zip = Path.Combine(tmp, "download.zip");
            bool keep = false;
            try
            {
                progress("Downloading " + a.Title + "...");
                try { using (var wc = new WebClient()) { wc.Headers["User-Agent"] = "HallowSetup"; wc.DownloadFile(url, zip); } }
                catch (Exception e) { return "Download failed: " + e.Message; }
                progress("Checking the download...");
                string got; using (var s = File.OpenRead(zip)) using (var h = SHA256.Create()) got = BitConverter.ToString(h.ComputeHash(s)).Replace("-", "");
                if (!string.Equals(got, sha, StringComparison.OrdinalIgnoreCase)) return "The download doesn't match GitHub's checksum - " + a.Title + " not installed.";
                progress("Unpacking...");
                var x = Path.Combine(tmp, "x");
                try { ZipFile.ExtractToDirectory(zip, x); } catch (Exception e) { return "Couldn't unpack the download: " + e.Message; }
                var ver = rel.ContainsKey("tag_name") ? Show(Ver(rel["tag_name"] as string)) : "";
                if (a.IsToolkit)
                {
                    // Hallow Toolkit installs as admin: start its own INSTALL.bat and Windows asks you (UAC)
                    var bat = Directory.GetFiles(x, "INSTALL.bat", SearchOption.AllDirectories).FirstOrDefault();
                    if (bat == null) return "The " + a.Title + " download has no installer.";
                    if (DryRun) return "Checked and unpacked " + a.Title + " " + ver + " - test run, would start " + Path.GetFileName(bat) + " as admin.";
                    try { Process.Start(new ProcessStartInfo(bat) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(bat) }); }
                    catch (System.ComponentModel.Win32Exception e) { if (e.NativeErrorCode == 1223) return "Cancelled - Hallow Toolkit needs your OK to install."; throw; }
                    keep = true;   // the installer still needs its files
                    return "Hallow Toolkit's installer is running - finish it in its window.";
                }
                var inst = Directory.GetFiles(x, "Install.ps1", SearchOption.AllDirectories).FirstOrDefault();
                if (inst == null) return "The " + a.Title + " download has no installer.";
                if (DryRun) return "Checked and unpacked " + a.Title + " " + ver + " - test run, would run " + inst.Substring(x.Length + 1) + ".";
                progress("Installing " + a.Title + "...");
                var args = "-NoProfile -ExecutionPolicy Bypass -File \"" + inst + "\"";
                if (Elevated())
                {
                    // never pass admin rights on: Explorer starts the installer as you
                    var cmd = Path.Combine(tmp, "install.cmd");
                    File.WriteAllText(cmd, "@echo off\r\ntitle " + a.Title + " - installing\r\npowershell " + args + "\r\n", Encoding.ASCII);
                    Process.Start("explorer.exe", "\"" + cmd + "\""); keep = true;
                    return a.Title + "'s installer is running.";
                }
                var p = Process.Start(new ProcessStartInfo("powershell.exe", args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(inst) });
                if (!p.WaitForExit(5 * 60 * 1000)) return a.Title + "'s installer is still running.";
                return p.ExitCode == 0 ? a.Title + " " + ver + " is installed." : a.Title + "'s installer stopped with an error (code " + p.ExitCode + ").";
            }
            finally { if (!keep) try { Directory.Delete(tmp, true); } catch { } }
        }
        // v1.18: did Install() work? ("... not installed." also contains "installed" - the window used to show those in green)
        public static bool Succeeded(string msg) { return msg != null && (msg.EndsWith(" is installed.") || msg.Contains("installer is running") || msg.Contains("'s installer is running") || msg.Contains("test run")); }
        // ---- v1.1: install several apps one after another (Toolkit last, since it asks for admin). One line per app.
        public static List<string> InstallMany(IEnumerable<HallowApp> apps, Action<string> progress)
        {
            var res = new List<string>();
            foreach (var a in apps.OrderBy(x => x.IsToolkit ? 1 : 0).ToList())
            {
                progress("Installing " + a.Title + " (" + (res.Count + 1) + " of " + apps.Count() + ")...");
                string r; try { r = Install(a, progress); } catch (Exception e) { r = a.Title + ": install failed - " + e.Message; }
                res.Add(r);
            }
            return res;
        }

        // ---- v1.1: Hallow Setup updates itself (same checks: GitHub's SHA-256 must match). The new exe replaces this one
        // after this copy closes, then opens.
        public static Version Current { get { return typeof(Setup).Assembly.GetName().Version; } }
        public static HallowApp Self = new HallowApp { Name = "Setup", Icon = "download", Desc = "" };
        public static bool SelfNewer() { return Self.Latest != null && Self.Latest > new Version(Current.Major, Current.Minor, Math.Max(0, Current.Build), 0); }
        public static void CheckSelf() { try { var r = LatestRelease(Self); object t; if (r.TryGetValue("tag_name", out t)) Self.Latest = Ver(t as string); } catch { } }
        // returns an error, or null once the swap is under way (the caller then closes)
        public static string UpdateSelf(Action<string> progress)
        {
            Dictionary<string, object> rel;
            try { rel = LatestRelease(Self); } catch (Exception e) { return "Couldn't reach GitHub: " + e.Message; }
            object o; var assets = rel.TryGetValue("assets", out o) ? o as System.Collections.ArrayList : null;
            var asset = assets == null ? null : assets.OfType<Dictionary<string, object>>().FirstOrDefault(x => (x.ContainsKey("name") ? x["name"] as string ?? "" : "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (asset == null) return "The latest Hallow Setup release has no download.";
            var digest = asset.ContainsKey("digest") ? asset["digest"] as string ?? "" : ""; var sha = digest.StartsWith("sha256:") ? digest.Substring(7) : "";
            if (!Regex.IsMatch(sha, "^[0-9a-fA-F]{64}$")) return "GitHub didn't give a checksum for the Hallow Setup download - not updated.";
            var tmp = Path.Combine(Path.GetTempPath(), "hallow-setup-self-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(tmp);
            var zip = Path.Combine(tmp, "download.zip");
            if (!AllowedDownload(asset.ContainsKey("browser_download_url") ? asset["browser_download_url"] as string : null, Self.Repo)) { try { Directory.Delete(tmp, true); } catch { } return "The Hallow Setup download isn't from its own GitHub releases - not updated."; }
            progress("Downloading Hallow Setup...");
            try { using (var wc = new WebClient()) { wc.Headers["User-Agent"] = "HallowSetup"; wc.DownloadFile(asset["browser_download_url"] as string, zip); } }
            catch (Exception e) { try { Directory.Delete(tmp, true); } catch { } return "Download failed: " + e.Message; }
            string got; using (var s = File.OpenRead(zip)) using (var h = SHA256.Create()) got = BitConverter.ToString(h.ComputeHash(s)).Replace("-", "");
            if (!string.Equals(got, sha, StringComparison.OrdinalIgnoreCase)) { try { Directory.Delete(tmp, true); } catch { } return "The download doesn't match GitHub's checksum - not updated."; }
            var xdir = Path.Combine(tmp, "x"); ZipFile.ExtractToDirectory(zip, xdir);
            var exe = Directory.GetFiles(xdir, "HallowSetup.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe == null) return "The download has no HallowSetup.exe.";
            var me = Process.GetCurrentProcess().MainModule.FileName;
            var cmd = Path.Combine(tmp, "swap.cmd");
            File.WriteAllText(cmd, "@echo off\r\n" +
                ":wait\r\ntasklist /fi \"PID eq " + Process.GetCurrentProcess().Id + "\" | find \"" + Process.GetCurrentProcess().Id + "\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                "copy /y \"" + exe + "\" \"" + me + "\" >nul\r\nstart \"\" \"" + me + "\"\r\n", Encoding.ASCII);
            if (DryRun) { try { Directory.Delete(tmp, true); } catch { } return "TEST: checked and unpacked Hallow Setup " + Show(Ver(rel["tag_name"] as string)) + " - would replace " + Path.GetFileName(me) + "."; }
            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + cmd + "\"") { UseShellExecute = false, CreateNoWindow = true });
            return null;
        }

        public static bool Elevated() { try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } catch { return false; } }

        // the shared Hallow accent (%APPDATA%\Hallow\theme.json), or null
        public static string Accent()
        {
            try
            {
                var f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Hallow\theme.json"); if (!File.Exists(f)) return null;
                var d = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(f)); object a;
                var s = d != null && d.TryGetValue("accent", out a) ? a as string : null;
                return s != null && Regex.IsMatch(s, "^#[0-9A-Fa-f]{6}$") ? s.ToUpperInvariant() : null;
            }
            catch { return null; }
        }
    }
}
