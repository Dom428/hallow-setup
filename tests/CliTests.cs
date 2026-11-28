// Hallow Setup 1.18 tests: the command line (--list / --install / --update), the catalogue, where downloads may come from, and the
// window at 1366x768. Every install runs against a local stand-in for GitHub (a tiny HTTP server on 127.0.0.1) and a temp folder
// for %LOCALAPPDATA% - never GitHub, never the real Programs folder.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowSetup.Tests
{
    public static partial class Run
    {
        // ---- a stand-in for api.github.com and its downloads
        class FakeGitHub : IDisposable
        {
            readonly TcpListener l; public readonly int Port; public readonly string Url; volatile bool stop;
            public readonly Dictionary<string, string> Json = new Dictionary<string, string>();     // path -> JSON
            public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();    // path -> bytes
            public readonly List<string> Hits = new List<string>();
            public FakeGitHub()
            {
                l = new TcpListener(IPAddress.Loopback, 0); l.Start(); Port = ((IPEndPoint)l.LocalEndpoint).Port; Url = "http://127.0.0.1:" + Port;
                new Thread(Loop) { IsBackground = true }.Start();
            }
            void Loop()
            {
                while (!stop)
                {
                    TcpClient c; try { c = l.AcceptTcpClient(); } catch { return; }
                    new Thread(() => Serve(c)) { IsBackground = true }.Start();
                }
            }
            void Serve(TcpClient c)
            {
                using (c)
                {
                    try
                    {
                        var s = c.GetStream(); var buf = new List<byte>(); var b = new byte[1];
                        while (s.Read(b, 0, 1) == 1) { buf.Add(b[0]); var n = buf.Count; if (n >= 4 && buf[n - 4] == 13 && buf[n - 3] == 10 && buf[n - 2] == 13 && buf[n - 1] == 10) break; }
                        var line = Encoding.ASCII.GetString(buf.ToArray()).Split('\r')[0]; var path = line.Split(' ').Length > 1 ? line.Split(' ')[1] : "/";
                        lock (Hits) Hits.Add(path);
                        byte[] body; string type = "application/json"; int code = 200;
                        string js; byte[] f;
                        lock (Json) { Json.TryGetValue(path, out js); Files.TryGetValue(path, out f); }
                        if (js != null) body = Encoding.UTF8.GetBytes(js);
                        else if (f != null) { body = f; type = "application/zip"; }
                        else { code = 404; body = Encoding.UTF8.GetBytes("{\"message\":\"Not Found\"}"); }
                        var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + (code == 200 ? " OK" : " Not Found") + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                        s.Write(head, 0, head.Length); s.Write(body, 0, body.Length); s.Flush();
                    }
                    catch { }
                }
            }
            public void Dispose() { stop = true; try { l.Stop(); } catch { } }
            // a release of <app> with one zip asset; digest = the real SHA-256 unless given; url = the stand-in's own address unless given
            public void Release(string app, string tag, byte[] zip, string digest = null, string url = null)
            {
                var dl = "/dl/" + app + "/Hallow." + app + ".zip";
                if (digest == null) using (var h = SHA256.Create()) digest = "sha256:" + BitConverter.ToString(h.ComputeHash(zip)).Replace("-", "").ToLowerInvariant();
                var rel = new Dictionary<string, object> { { "tag_name", tag }, { "assets", new object[] { new Dictionary<string, object> { { "name", "Hallow." + app + ".zip" }, { "digest", digest }, { "browser_download_url", url ?? Url + dl } } } } };
                lock (Json) { Json["/repos/Dom428/hallow-" + app.ToLowerInvariant() + "/releases/latest"] = new JavaScriptSerializer().Serialize(rel); Files[dl] = zip; }
            }
        }

        // a stand-in app zip: "Hallow <App>\Install.ps1" copies "Hallow<App>.exe" into the test %LOCALAPPDATA%\Programs\Hallow <App>
        static byte[] AppZip(string app, string exeFrom)
        {
            using (var ms = new MemoryStream())
            {
                using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    var e = z.CreateEntry("Hallow " + app + "/Hallow" + app + ".exe"); using (var w = e.Open()) { var b = File.ReadAllBytes(exeFrom); w.Write(b, 0, b.Length); }
                    var ps = z.CreateEntry("Hallow " + app + "/Install.ps1");
                    using (var w = new StreamWriter(ps.Open()))
                        w.Write("$d = Join-Path $env:HALLOW_SETUP_LOCAL 'Programs\\Hallow " + app + "'\r\nif (-not $env:HALLOW_SETUP_LOCAL) { exit 9 }\r\nNew-Item -ItemType Directory -Force $d | Out-Null\r\nCopy-Item (Join-Path $PSScriptRoot 'Hallow" + app + ".exe') $d -Force\r\n");
                }
                return ms.ToArray();
            }
        }

        static Tuple<int, string> Setup_(string exe, string args, Dictionary<string, string> env, int ms = 60000)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            foreach (var k in new[] { "HALLOW_SETUP_API", "HALLOW_SETUP_LOCAL", "HALLOW_SETUP_TOOLKIT", "HALLOW_SETUP_RUNKEY", "HALLOW_SETUP_DRYRUN" }) psi.EnvironmentVariables.Remove(k);
            foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
            using (var p = Process.Start(psi))
            {
                var o = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(ms)) { try { p.Kill(); } catch { } return Tuple.Create(-1, "timed out"); }
                return Tuple.Create(p.ExitCode, o.Result.Trim());
            }
        }

        // v1.19: Hallow Capture and Hallow Display - in the list with their icons, the uninstall notes, Capture's history wipe
        // (a temp folder only) and Display's restore.json guard. HALLOW_SETUP_APPDATA points at the temp folder while these run.
        static void NewApps119(string tmp)
        {
            var cap = Setup.Find("Capture"); var dis = Setup.Find("hallow display");
            Ok(cap != null && cap.Repo == "Dom428/hallow-capture" && cap.Icon == "camera" && Icons.Get("camera") != null && dis != null && dis.Repo == "Dom428/hallow-display" && dis.Icon == "display" && Icons.Get("display") != null, "1.19: Capture (camera) and Display (display) in the catalogue");
            Ok(Setup.Apps.IndexOf(cap) < Setup.Apps.IndexOf(Setup.Find("Toolkit")) && Setup.Apps.IndexOf(dis) < Setup.Apps.IndexOf(Setup.Find("Toolkit")) && !cap.NoTray && !dis.NoTray, "1.19: both before Toolkit, both start in the tray");
            foreach (var n in new[] { "capture", "display" })
            {
                var pf = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\src\icons\" + n + ".png")); var s = File.Exists(pf) ? File.OpenRead(pf) : null;   // build.ps1 builds every src\icons\*.png into HallowSetup.exe
                bool ok = false; if (s != null) using (s) { var b = new byte[24]; s.Read(b, 0, 24); ok = b[0] == 0x89 && b[1] == (byte)'P' && (b[16] << 24 | b[17] << 16 | b[18] << 8 | b[19]) == 64 && (b[20] << 24 | b[21] << 16 | b[22] << 8 | b[23]) == 64; }
                Ok(ok, "1.19: src\\icons\\" + n + ".png is there (64 x 64 PNG like the others)");
            }
            var keep = Environment.GetEnvironmentVariable("HALLOW_SETUP_APPDATA");
            var ad = Path.Combine(tmp, "appdata119"); Directory.CreateDirectory(ad);
            try
            {
                Environment.SetEnvironmentVariable("HALLOW_SETUP_APPDATA", ad);
                Ok(Uninstaller.DataDir(cap) == Path.Combine(ad, "HallowCapture") && Uninstaller.DataDir(dis) == Path.Combine(ad, "HallowDisplay"), "1.19: data folders %APPDATA%\\HallowCapture / HallowDisplay");
                var capNote = Uninstaller.KeptNote(cap);
                Ok(capNote != null && capNote.Contains("clipboard history is deleted") && capNote.Contains("Hallow Captures"), "1.19: Capture's note - history deleted, captures folder stays");
                Directory.CreateDirectory(Uninstaller.DataDir(cap));
                File.WriteAllText(Path.Combine(Uninstaller.DataDir(cap), "settings.json"), "{\"SaveRoot\":\"D:\\\\Shots\",\"Format\":\"png\"}");
                var kept = Uninstaller.KeptFolders(cap);
                Ok(kept != null && kept.Count == 1 && kept[0] == @"D:\Shots" && Uninstaller.KeptNote(cap).Contains(@"D:\Shots"), "1.19: Capture's own save folder is the kept folder");
                File.WriteAllText(Path.Combine(Uninstaller.DataDir(cap), "settings.json"), "{\"SaveRoot\":" + new JavaScriptSerializer().Serialize(Path.Combine(Uninstaller.DataDir(cap), "shots")) + "}");
                Ok(Uninstaller.DataBlocked(cap) != null && Uninstaller.DataBlocked(cap).Contains("captures are inside"), "1.19: captures saved inside the data folder keep it");
                File.Delete(Path.Combine(Uninstaller.DataDir(cap), "settings.json"));
                Ok(Uninstaller.DataBlocked(cap) == null, "1.19: otherwise Capture's settings can go");
                // the history wipe: only history\ index.bin / img-*.bin / *.tmp, for good; anything else is left and counted
                var h = Path.Combine(Uninstaller.DataDir(cap), "history"); Directory.CreateDirectory(h);
                foreach (var f in new[] { "index.bin", "index.bin.tmp", "img-a1.bin", "img-b2.bin" }) File.WriteAllBytes(Path.Combine(h, f), new byte[] { 1, 2, 3 });
                File.WriteAllText(Path.Combine(Uninstaller.DataDir(cap), "settings.json"), "{}");
                Ok(Uninstaller.WipeCaptureHistory(Uninstaller.DataDir(cap)) == 0 && !Directory.Exists(h) && File.Exists(Path.Combine(Uninstaller.DataDir(cap), "settings.json")), "1.19: Capture's history wiped, the folder gone, settings untouched");
                Directory.CreateDirectory(h); File.WriteAllBytes(Path.Combine(h, "index.bin"), new byte[] { 1 }); File.WriteAllText(Path.Combine(h, "notes.txt"), "mine");
                Ok(Uninstaller.WipeCaptureHistory(Uninstaller.DataDir(cap)) == 1 && File.Exists(Path.Combine(h, "notes.txt")) && !File.Exists(Path.Combine(h, "index.bin")), "1.19: an unknown file in history\\ is left and counted");
                var other = Path.Combine(ad, "NotCapture"); Directory.CreateDirectory(Path.Combine(other, "history")); File.WriteAllBytes(Path.Combine(other, "history", "index.bin"), new byte[] { 1 });
                Ok(Uninstaller.WipeCaptureHistory(other) == 0 && File.Exists(Path.Combine(other, "history", "index.bin")) && Uninstaller.WipeCaptureHistory(null) == 0, "1.19: the wipe only ever runs in a HallowCapture folder");
                // Display: put back first, its folder kept while restore.json says a change is still out
                var disNote = Uninstaller.KeptNote(dis);
                Ok(disNote != null && disNote.Contains("put back first") && disNote.Contains("quit"), "1.19: Display's note - display changes put back first");
                Ok(Uninstaller.QuitWaitMs(dis) >= 30000 && Uninstaller.QuitWaitMs(cap) == 15000, "1.19: Display gets 30 s to put things back after quit");
                Directory.CreateDirectory(Uninstaller.DataDir(dis));
                Ok(Uninstaller.DataBlocked(dis) == null, "1.19: Display's settings can go when nothing is waiting");
                File.WriteAllText(Path.Combine(Uninstaller.DataDir(dis), "restore.json"), "{}");
                Ok(Uninstaller.DataBlocked(dis) != null && Uninstaller.DataBlocked(dis).Contains("restore.json"), "1.19: Display's folder stays while restore.json is there");
            }
            finally { Environment.SetEnvironmentVariable("HALLOW_SETUP_APPDATA", keep); }
        }

        static void CliTests(string tmp)
        {
            // ---- the catalogue: every current family app or part
            NewApps119(tmp);
            foreach (var n in new[] { "Hub", "Toolkit", "Mixer", "Overlay", "Remote", "Vault", "Clips", "Keys", "Assist", "Guard", "Tasks", "Browser", "VPN", "Torrents", "Financials", "Email", "Home", "Gear", "Capture", "Display" })
                Ok(Setup.Find(n) != null && Setup.Find(n).Repo == "Dom428/hallow-" + n.ToLowerInvariant(), "catalogue: Hallow " + n);
            Ok(Shield.Repo == "Dom428/hallow-shield", "catalogue: Hallow Shield (browser extension)");
            Ok(Setup.FindOther("Places") != null && Setup.FindOther("Places").Kind == "server" && Setup.FindOther("Authenticator") != null && Setup.FindOther("Authenticator").Kind == "phone", "catalogue: Places (NAS) and Authenticator (phone) listed, not installable");
            Ok(Setup.Apps.Select(a => a.Name).Distinct().Count() == Setup.Apps.Count && Setup.Apps.Last().IsToolkit, "catalogue: no duplicates, Toolkit last");
            Ok(Setup.Find("hub") == Setup.Find("Hub") && Setup.Find("Hallow Hub") == Setup.Find("Hub") && Setup.Find("HallowHub") == Setup.Find("Hub") && Setup.Find("\"Hallow Hub\"") == Setup.Find("Hub") && Setup.Find("nope") == null && Setup.Find("") == null, "names: hub / Hallow Hub / HallowHub");
            // ---- downloads only from the family's own GitHub releases
            Ok(Setup.AllowedDownload("https://github.com/Dom428/hallow-hub/releases/download/v2.17/Hallow.Hub.zip", "Dom428/hallow-hub"), "download: the app's own release asset is allowed");
            Ok(!Setup.AllowedDownload("https://github.com/Dom428/hallow-mixer/releases/download/v3/Hallow.Mixer.zip", "Dom428/hallow-hub"), "download: another repo's asset is refused");
            Ok(!Setup.AllowedDownload("https://example.com/Dom428/hallow-hub/releases/download/v2/x.zip", "Dom428/hallow-hub") && !Setup.AllowedDownload("http://github.com/Dom428/hallow-hub/releases/download/v2/x.zip", "Dom428/hallow-hub")
                && !Setup.AllowedDownload("https://github.com/Dom428/hallow-hub/archive/main.zip", "Dom428/hallow-hub") && !Setup.AllowedDownload("https://github.com.evil.example/Dom428/hallow-hub/releases/download/v2/x.zip", "Dom428/hallow-hub")
                && !Setup.AllowedDownload("https://github.com/Dom428/hallow-hub/releases/download/../../evil/x.zip", "Dom428/hallow-hub") && !Setup.AllowedDownload(null, "Dom428/hallow-hub"), "download: other hosts, plain http, source archives, look-alike hosts and .. are refused");
            Ok(!Setup.Succeeded("The download doesn't match GitHub's checksum - Hallow Hub not installed.") && Setup.Succeeded("Hallow Hub 2.17 is installed.") && Setup.Succeeded("Hallow Toolkit's installer is running - finish it in its window.") && !Setup.Succeeded("Hallow Hub's installer is still running."), "result: \"not installed\" isn't a success (it used to show green)");
            Ok(Cli.Check("Places", false).Item1 == 2 && Cli.Check("Authenticator", false).Item2.Contains("isn't a Windows app") && Cli.Check("Frobnicator", false).Item1 == 2 && Cli.Check("Setup", false).Item1 == 2 && Cli.Check("Hub", false) == null, "check: not Windows apps / unknown / Setup itself refused; Hub ok");
            Ok(Cli.WindowInstall(new[] { "--install", "Hub" }) == "Hub" && Cli.WindowInstall(new[] { "--install", "Hub", "--quiet" }) == null && Cli.WindowInstall(new[] { "--update", "Mixer" }) == "Mixer" && Cli.WindowInstall(new string[0]) == null, "window hand-off: --install <App> opens the window and installs it");

            // ---- the real exe against the stand-in
            var exe = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\HallowSetup.exe"));
            if (!File.Exists(exe)) { Ok(false, "HallowSetup.exe is built"); return; }
            var local = Path.Combine(tmp, "local"); Directory.CreateDirectory(local);
            using (var gh = new FakeGitHub())
            {
                var env = new Dictionary<string, string> { { "HALLOW_SETUP_API", gh.Url }, { "HALLOW_SETUP_LOCAL", local }, { "HALLOW_SETUP_TOOLKIT", Path.Combine(tmp, "toolkit") }, { "HALLOW_SETUP_RUNKEY", @"Software\HallowSetupTest\Run" } };
                var stub = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SetupTests.exe");   // any exe with a file version (Setup's own) stands in for the app
                gh.Release("Hub", "v1.0", AppZip("Hub", stub));
                gh.Release("Mixer", "v1.0", AppZip("Mixer", stub), "sha256:" + new string('0', 64));                                       // a wrong checksum
                gh.Release("Vault", "v1.0", AppZip("Vault", stub), null, "https://example.com/Hallow.Vault.zip");                          // not GitHub's own address
                gh.Release("Places", "v1.0", new byte[] { 1 });

                var r = Setup_(exe, "--list --json", env);
                Dictionary<string, object> list = null; try { list = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(r.Item2); } catch { }
                var items = list == null ? new Dictionary<string, object>[0] : ((System.Collections.ArrayList)list["items"]).Cast<Dictionary<string, object>>().ToArray();
                Ok(r.Item1 == 0 && items.Length == Setup.Apps.Count + 1 + Setup.Others.Count, "--list --json: every app, Shield and the other parts (" + items.Length + ")");
                Ok(items.Any(i => (string)i["name"] == "Hub" && (bool)i["installable"] && !(bool)i["installed"]) && items.Any(i => (string)i["name"] == "Places" && !(bool)i["installable"] && (string)i["kind"] == "server") && items.Any(i => (string)i["name"] == "Toolkit" && (bool)i["needsAdmin"]), "--list: installable / installed / kind / needsAdmin");
                Ok(!gh.Hits.Any(), "--list without --check doesn't go online");
                r = Setup_(exe, "--list --check", env);
                Ok(r.Item1 == 0 && r.Item2.Contains("Hub") && r.Item2.Contains("latest 1.0") && r.Item2.Contains("not a Windows app"), "--list --check: newest versions from the (stand-in) releases:\n" + r.Item2);

                var outFile = Path.Combine(tmp, "out.txt");
                r = Setup_(exe, "--install Hub --quiet --out \"" + outFile + "\"", env);
                var hubExe = Path.Combine(local, @"Programs\Hallow Hub\HallowHub.exe");
                Ok(r.Item1 == 0 && r.Item2.StartsWith("ok Hallow Hub 1.0 is installed.") && File.Exists(hubExe), "--install Hub --quiet: downloaded, checksum checked, its own Install.ps1 ran: " + r.Item2);
                Ok(File.Exists(outFile) && File.ReadAllText(outFile).Trim() == r.Item2, "--out: the same line in a file");
                int hits = gh.Hits.Count(h => h.StartsWith("/dl/"));
                r = Setup_(exe, "--update hallow-hub --quiet", env); Ok(r.Item1 == 2, "--update with a wrong name: exit 2 (" + r.Item2 + ")");
                r = Setup_(exe, "--update \"Hallow Hub\" --quiet", env);
                Ok(r.Item1 == 0 && r.Item2.Contains("is up to date") && gh.Hits.Count(h => h.StartsWith("/dl/")) == hits, "--update Hub: already newer than the release - nothing downloaded: " + r.Item2);
                r = Setup_(exe, "--install Hub --quiet --force", env);
                Ok(r.Item1 == 0 && gh.Hits.Count(h => h.StartsWith("/dl/")) == hits + 1, "--force reinstalls");

                r = Setup_(exe, "--install Mixer --quiet", env);
                Ok(r.Item1 == 1 && r.Item2.Contains("doesn't match GitHub's checksum") && !File.Exists(Path.Combine(local, @"Programs\Hallow Mixer\HallowMixer.exe")), "a wrong checksum: exit 1, nothing installed: " + r.Item2);
                r = Setup_(exe, "--install Vault --quiet", env);
                Ok(r.Item1 == 1 && r.Item2.Contains("isn't from its own GitHub releases") && !Directory.Exists(Path.Combine(local, @"Programs\Hallow Vault")), "a download address outside the family's releases: refused before downloading: " + r.Item2);
                r = Setup_(exe, "--update Clips --quiet", env); Ok(r.Item1 == 2 && r.Item2.Contains("isn't installed"), "--update of an app that isn't installed: exit 2");
                r = Setup_(exe, "--install Places --quiet", env); Ok(r.Item1 == 2 && r.Item2.Contains("isn't a Windows app") && !gh.Hits.Any(h => h.StartsWith("/dl/Places")), "--install Places: not a Windows app, exit 2, nothing downloaded");
                r = Setup_(exe, "--install Toolkit --quiet", env); Ok(r.Item1 == 3 && r.Item2.Contains("admin"), "--install Toolkit --quiet: needs the window (admin), exit 3");
                r = Setup_(exe, "--install Nothing --quiet", env); Ok(r.Item1 == 2 && r.Item2.Contains("Apps: Mixer, Hub"), "unknown app: exit 2 with the list");
                r = Setup_(exe, "--install", env); Ok(r.Item1 == 2 && r.Item2.StartsWith("error use:"), "--install without a name: usage");
                r = Setup_(exe, "--list --json", env);
                Ok(r.Item2.Contains("\"name\":\"Hub\",\"title\":\"Hallow Hub\",\"kind\":\"app\",\"installable\":true,\"repo\":\"Dom428/hallow-hub\",\"installed\":true,\"version\":\"" + Program.Version + "\""), "--list: Hub now installed (the stand-in has Setup's own version, " + Program.Version + ")");
                Ok(!gh.Hits.Any(h => !h.StartsWith("/repos/Dom428/hallow-") && !h.StartsWith("/dl/")), "only the family's release addresses were asked for");
            }

            // ---- the window at 1366x768: the app list scrolls, the buttons and the footer stay inside
            var snap = Path.Combine(tmp, "snap");
            var s2 = Setup_(exe, "--snap \"" + snap + "\"", new Dictionary<string, string> { { "HALLOW_SETUP_LOCAL", local }, { "HALLOW_SETUP_TOOLKIT", Path.Combine(tmp, "toolkit") } }, 120000);
            var lay = File.Exists(Path.Combine(snap, "layout-728.txt")) ? File.ReadAllText(Path.Combine(snap, "layout-728.txt")) : "";
            Func<string, int> num = k => { var m = System.Text.RegularExpressions.Regex.Match(lay, k + "=(\\d+)"); return m.Success ? int.Parse(m.Groups[1].Value) : -1; };
            Ok(s2.Item1 == 0 && num("height") > 0 && num("scrollable") > 0 && num("viewport") > 150, "1366x768: the app list scrolls (" + lay + ")");
            Ok(num("buttons") > 0 && num("buttons") <= num("height") && num("footer") <= num("height") + 1, "1366x768: Install ticked / Check for updates and the footer are inside the window");
            Ok(num("rows") == Setup.Apps.Count + 2, "1366x768: every app, Shield and the 'also in the family' card are in the list");
        }
    }
}
