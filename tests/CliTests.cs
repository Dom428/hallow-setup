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

        // v1.20: Hallow Find - in the list with its icon; uninstall sends "--cmd quit" first and always deletes its file index
        // (%LOCALAPPDATA%\HallowFind) for good, while its settings follow the usual choice. Temp folders only: HALLOW_SETUP_LOCAL and
        // HALLOW_SETUP_APPDATA point into the test folder while this runs.
        static void NewApps120(string tmp)
        {
            var find = Setup.Find("Find");
            Ok(find != null && find.Repo == "Dom428/hallow-find" && find.Icon == "magnifying-glass" && Icons.Get("magnifying-glass") != null && Setup.Find("hallow find") == find, "1.20: Find (magnifying-glass) in the catalogue");
            Ok(Setup.Apps.IndexOf(find) > Setup.Apps.IndexOf(Setup.Find("Display")) && Setup.Apps.IndexOf(find) < Setup.Apps.IndexOf(Setup.Find("Toolkit")) && !find.NoTray && !find.IsToolkit, "1.20: after Display, before Toolkit, starts in the tray (--tray)");
            Ok(find.Desc.Contains("nothing leaves the PC"), "1.20: Find's description");
            {
                var pf = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\src\icons\find.png")); var s = File.Exists(pf) ? File.OpenRead(pf) : null;
                bool ok = false; if (s != null) using (s) { var b = new byte[24]; s.Read(b, 0, 24); ok = b[0] == 0x89 && b[1] == (byte)'P' && (b[16] << 24 | b[17] << 16 | b[18] << 8 | b[19]) == 64 && (b[20] << 24 | b[21] << 16 | b[22] << 8 | b[23]) == 64; }
                Ok(ok, "1.20: src\\icons\\find.png is there (64 x 64 PNG like the others)");
            }
            Ok(Uninstaller.QuitByCmdFirst(find) && !Uninstaller.QuitByCmdFirst(Setup.Find("Display")) && !Uninstaller.QuitByCmdFirst(Setup.Find("Hub")) && !Uninstaller.QuitByCmdFirst(null), "1.20: Find is told \"--cmd quit\" first (1.21: Files too), not Display or Hub");
            Ok(Uninstaller.QuitWaitMs(find) == 15000, "1.20: Find gets the usual 15 s after quit");
            var keepA = Environment.GetEnvironmentVariable("HALLOW_SETUP_APPDATA"); var keepL = Environment.GetEnvironmentVariable("HALLOW_SETUP_LOCAL");
            var ad = Path.Combine(tmp, "appdata120"); var lo = Path.Combine(tmp, "local120"); var outside = Path.Combine(tmp, "outside120");
            Directory.CreateDirectory(ad); Directory.CreateDirectory(lo); Directory.CreateDirectory(outside);
            try
            {
                Environment.SetEnvironmentVariable("HALLOW_SETUP_APPDATA", ad); Environment.SetEnvironmentVariable("HALLOW_SETUP_LOCAL", lo);
                var ix = Path.Combine(lo, "HallowFind");
                Ok(Uninstaller.DataDir(find) == Path.Combine(ad, "HallowFind") && Uninstaller.FindIndexDir() == ix, "1.20: settings in %APPDATA%\\HallowFind, the index in %LOCALAPPDATA%\\HallowFind");
                var note = Uninstaller.KeptNote(find);
                Ok(note != null && note.Contains("file index is removed too") && note.Contains(ix) && note.Contains("Also remove my settings and data"), "1.20: Find's note - index removed for good, settings follow the choice");
                Directory.CreateDirectory(Uninstaller.DataDir(find)); File.WriteAllText(Path.Combine(Uninstaller.DataDir(find), "settings.json"), "{\"Engine\":\"google\"}");
                Ok(Uninstaller.DataBlocked(find) == null && Uninstaller.KeptFolders(find) != null && Uninstaller.KeptFolders(find).Count == 0, "1.20: Find's settings can go when ticked (nothing kept)");
                // the wipe: every file in the index folder (sub folders too, read-only too), then the folders - nothing next to it
                Directory.CreateDirectory(Path.Combine(ix, "old"));
                foreach (var f in new[] { "index.bin", "index.bin.tmp", @"old\index.bin" }) File.WriteAllBytes(Path.Combine(ix, f), new byte[] { 1, 2, 3 });
                File.SetAttributes(Path.Combine(ix, "index.bin"), FileAttributes.ReadOnly);
                var sib = Path.Combine(lo, "HallowClips"); Directory.CreateDirectory(sib); File.WriteAllText(Path.Combine(sib, "keep.txt"), "mine");
                Ok(Uninstaller.WipeFindIndex(ix) == 0 && !Directory.Exists(ix) && File.Exists(Path.Combine(sib, "keep.txt")) && File.Exists(Path.Combine(Uninstaller.DataDir(find), "settings.json")), "1.20: the index folder deleted for good; Clips' folder and Find's settings untouched");
                Ok(Uninstaller.WipeFindIndex(ix) == 0, "1.20: no index folder - nothing to do");
                Ok(Uninstaller.WipeFindIndex(sib) == 0 && File.Exists(Path.Combine(sib, "keep.txt")) && Uninstaller.WipeFindIndex(null) == 0 && Uninstaller.WipeFindIndex("") == 0, "1.20: the wipe only ever runs in a folder called HallowFind");
                // a link inside is never followed: what it points at stays, and it's counted as left
                Directory.CreateDirectory(ix); File.WriteAllBytes(Path.Combine(ix, "index.bin"), new byte[] { 1 }); File.WriteAllText(Path.Combine(outside, "precious.txt"), "mine");
                var link = Path.Combine(ix, "link");
                bool made = false; try { using (var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + outside + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })) { p.StandardOutput.ReadToEnd(); p.WaitForExit(10000); } made = Directory.Exists(link); } catch { }
                if (made)
                {
                    int left = Uninstaller.WipeFindIndex(ix);
                    Ok(left == 1 && File.Exists(Path.Combine(outside, "precious.txt")) && !File.Exists(Path.Combine(ix, "index.bin")), "1.20: a junction in the index folder is never followed (counted as left)");
                    try { Directory.Delete(link); } catch { }   // the link only (not recursive)
                    try { Directory.Delete(ix); } catch { }
                }
                else Ok(true, "1.20: (junction check skipped - mklink /J unavailable)");
            }
            finally { Environment.SetEnvironmentVariable("HALLOW_SETUP_APPDATA", keepA); Environment.SetEnvironmentVariable("HALLOW_SETUP_LOCAL", keepL); }
        }

        // v1.21: Hallow Files - in the list with its icon and no Start with Windows (windowed, like Browser); uninstall runs
        // "HallowFiles.exe --cmd quit", then "HallowFiles.exe --undo-default-fm", then removes the program folder; its settings follow
        // the usual choice. A whole uninstall runs in test mode on temp folders: the two commands are only recorded (never run - they
        // would reach the real app's pipe and the real registry), the Recycle Bin is only recorded, the registry keys are made-up
        // names under HKCU that don't exist (only read / delete-if-there - nothing is written).
        static void NewApps121(string tmp)
        {
            var files = Setup.Find("Files");
            Ok(files != null && files.Repo == "Dom428/hallow-files" && files.Icon == "folder-open" && Icons.Get("folder-open") != null && Setup.Find("hallow files") == files && Setup.Find("HallowFiles") == files, "1.21: Files (folder-open) in the catalogue");
            Ok(Setup.Apps.IndexOf(files) == Setup.Apps.IndexOf(Setup.Find("Find")) + 1 && Setup.Apps.IndexOf(files) < Setup.Apps.IndexOf(Setup.Find("Toolkit")) && !files.IsToolkit, "1.21: after Find, before Toolkit");
            Ok(files.Desc.Contains("file manager") && files.Desc.Contains("dual pane"), "1.21: Files' description");
            {
                var pf = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\src\icons\files.png")); var s = File.Exists(pf) ? File.OpenRead(pf) : null;
                bool ok = false; if (s != null) using (s) { var b = new byte[24]; s.Read(b, 0, 24); ok = b[0] == 0x89 && b[1] == (byte)'P' && (b[16] << 24 | b[17] << 16 | b[18] << 8 | b[19]) == 64 && (b[20] << 24 | b[21] << 16 | b[22] << 8 | b[23]) == 64; }
                Ok(ok, "1.21: src\\icons\\files.png is there (64 x 64 PNG like the others)");
            }
            // no Start with Windows: the row shows "Opens from its shortcut" (NoTray) like Browser, and the switch can't write one
            Ok(files.NoTray && Setup.Find("Browser").NoTray && !Setup.Find("Find").NoTray && !Setup.Find("Hub").NoTray, "1.21: Files has no Start with Windows (NoTray, like Browser)");
            Ok(Uninstaller.QuitByCmdFirst(files) && Uninstaller.QuitWaitMs(files) == 30000, "1.21: Files hears \"--cmd quit\" first and gets 30 s (it lets a running copy finish)");
            Ok(Uninstaller.PointsAtFiles("\"C:\\Users\\x\\AppData\\Local\\Programs\\Hallow Files\\HallowFiles.exe\" \"%1\"") && !Uninstaller.PointsAtFiles("%SystemRoot%\\Explorer.exe") && !Uninstaller.PointsAtFiles(null)
                && Uninstaller.FilesCommandKeys.Length == 3 && Uninstaller.FilesCommandKeys.All(k => k.StartsWith(@"Software\Classes\") && k.EndsWith(@"\command")), "1.21: the leftover check only ever removes a folder-open command that names HallowFiles.exe");
            var names = new[] { "HALLOW_SETUP_API", "HALLOW_SETUP_LOCAL", "HALLOW_SETUP_RUNKEY", "HALLOW_SETUP_APPDATA", "HALLOW_SETUP_PROGRAMS", "HALLOW_SETUP_DESKTOP", "HALLOW_SETUP_PROGRAMDATA", "HALLOW_SETUP_REGROOT", "HALLOW_SETUP_PIPE" };
            var keep = names.ToDictionary(n => n, n => Environment.GetEnvironmentVariable(n));
            var root = Path.Combine(tmp, "files121"); var lo = Path.Combine(root, "local"); var ad = Path.Combine(root, "appdata"); var sm = Path.Combine(root, "startmenu"); var dt = Path.Combine(root, "desktop"); var pd = Path.Combine(root, "programdata");
            var fake = "HallowSetupTest-" + Guid.NewGuid().ToString("N");   // never created: every registry step finds nothing there
            try
            {
                foreach (var d in new[] { lo, ad, sm, dt, pd }) Directory.CreateDirectory(d);
                var vals = new Dictionary<string, string> { { "HALLOW_SETUP_API", "http://127.0.0.1:9/unused" }, { "HALLOW_SETUP_LOCAL", lo }, { "HALLOW_SETUP_RUNKEY", @"Software\" + fake + @"\Run" }, { "HALLOW_SETUP_APPDATA", ad },
                    { "HALLOW_SETUP_PROGRAMS", sm }, { "HALLOW_SETUP_DESKTOP", dt }, { "HALLOW_SETUP_PROGRAMDATA", pd }, { "HALLOW_SETUP_REGROOT", @"Software\" + fake }, { "HALLOW_SETUP_PIPE", fake } };
                foreach (var kv in vals) Environment.SetEnvironmentVariable(kv.Key, kv.Value);
                Ok(Setup.TestMode && Uninstaller.TestGuard() == null, "1.21: test mode with every place pointing at temp folders / made-up keys");
                Ok(!files.StartsWithWindows, "1.21: no Start with Windows value for Files (read from the made-up key)");
                Ok(Uninstaller.DataDir(files) == Path.Combine(ad, "HallowFiles") && Uninstaller.KeptFolders(files) != null && Uninstaller.KeptFolders(files).Count == 0, "1.21: settings in %APPDATA%\\HallowFiles, nothing kept");
                var note = Uninstaller.KeptNote(files);
                Ok(note != null && note.Contains("File Explorer") && note.Contains("Open folders with Hallow Files") && note.Contains("never touched"), "1.21: Files' uninstall note - File Explorer opens folders again");
                var prog = Path.Combine(lo, "Programs", "Hallow Files"); var data = Path.Combine(ad, "HallowFiles");
                Func<bool, UninstallResult> run = withData =>
                {
                    Directory.CreateDirectory(prog); File.WriteAllText(Path.Combine(prog, "HallowFiles.exe"), "not a real exe");
                    Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "settings.json"), "{}");
                    File.WriteAllText(Path.Combine(sm, "Hallow Files.lnk"), "x"); File.WriteAllText(Path.Combine(dt, "Hallow Files.lnk"), "x");
                    lock (Uninstaller.TestLog) Uninstaller.TestLog.Clear();
                    Uninstaller.RecordRecycle = true;
                    try { return Uninstaller.Uninstall(files, withData, false, p => { }); } finally { Uninstaller.RecordRecycle = false; }
                };
                var r = run(false); var log = Uninstaller.TestLog.ToList();
                int iQuit = log.IndexOf("run HallowFiles.exe --cmd quit"), iUndo = log.IndexOf("run HallowFiles.exe --undo-default-fm"), iProg = log.IndexOf("recycle " + prog);
                Ok(iQuit == 0 && iUndo == 1 && iProg > iUndo && log.Count(l => l.StartsWith("run ")) == 2, "1.21: uninstall order - --cmd quit, then --undo-default-fm, then the program folder (" + string.Join(" | ", log.Select(l => l.Replace(root, "<tmp>"))) + ")");
                Ok(log.Contains("recycle " + Path.Combine(sm, "Hallow Files.lnk")) && log.Contains("recycle " + Path.Combine(dt, "Hallow Files.lnk")) && log.IndexOf("recycle " + Path.Combine(sm, "Hallow Files.lnk")) > iUndo, "1.21: shortcuts go after the undo too");
                Ok(!log.Contains("recycle " + data) && r.Ok && r.Message.Contains("Folders open in File Explorer again") && r.Message.Contains("Your settings stay in " + data), "1.21: settings kept when not ticked; the result says File Explorer opens folders again (" + r.Message.Replace(root, "<tmp>") + ")");
                Ok(File.Exists(Path.Combine(prog, "HallowFiles.exe")) && File.Exists(Path.Combine(data, "settings.json")), "1.21: test mode deleted nothing (recycles only recorded)");
                r = run(true); log = Uninstaller.TestLog.ToList();
                Ok(log.IndexOf("recycle " + data) > log.IndexOf("recycle " + prog) && log.IndexOf("run HallowFiles.exe --undo-default-fm") < log.IndexOf("recycle " + data) && r.Message.Contains("Settings and data are in the Recycle Bin"), "1.21: ticked - settings removed after the undo (it reads its backup from them) and after the program folder");
            }
            finally
            {
                Uninstaller.RecordRecycle = false;
                foreach (var kv in keep) Environment.SetEnvironmentVariable(kv.Key, kv.Value);
            }
        }

        // v1.22: Hallow Net, Paint, Notes and Uninstall - the catalogue, their icons, Start with Windows (Net only), the uninstall
        // order (always "<exe> --cmd quit" first), Net's history wipe, Notes' backups of unsaved text only when ticked, and Hallow
        // Uninstall's "error busy removing" (wait - nothing removed, never forced). Test mode on temp folders only: the apps' exes are
        // never run (Uninstaller.CmdReply records the line, TestCmdReply is the answer), recycles are only recorded, the registry keys
        // are made-up names under HKCU that don't exist (read / delete-if-there only - nothing is written).
        static void NewApps122(string tmp)
        {
            var net = Setup.Find("Net"); var paint = Setup.Find("hallow paint"); var notes = Setup.Find("HallowNotes"); var un = Setup.Find("Hallow Uninstall");
            var four = new[] { net, paint, notes, un };
            Ok(four.All(a => a != null) && net.Repo == "Dom428/hallow-net" && paint.Repo == "Dom428/hallow-paint" && notes.Repo == "Dom428/hallow-notes" && un.Repo == "Dom428/hallow-uninstall", "1.22: Net, Paint, Notes and Uninstall in the catalogue with their repos");
            if (four.Any(a => a == null)) return;
            Ok(net.Icon == "gauge-high" && paint.Icon == "tool-brush" && notes.Icon == "file-lines" && un.Icon == "broom" && four.All(a => Icons.Get(a.Icon) != null), "1.22: icons gauge-high / tool-brush / file-lines / broom, path data in Icons.g.cs");
            var files = Setup.Find("Files"); int iF = Setup.Apps.IndexOf(files);
            Ok(Setup.Apps.IndexOf(net) == iF + 1 && Setup.Apps.IndexOf(paint) == iF + 2 && Setup.Apps.IndexOf(notes) == iF + 3 && Setup.Apps.IndexOf(un) == iF + 4 && Setup.Apps.IndexOf(un) < Setup.Apps.IndexOf(Setup.Find("Toolkit")) && four.All(a => !a.IsToolkit), "1.22: after Files, before Toolkit");
            Ok(net.Desc.Contains("ping") && net.Desc.Contains("no admin") && paint.Desc.Contains("layers") && notes.Desc.Contains("find in files") && un.Desc.Contains("leftovers"), "1.22: descriptions");
            // the catalogue icons: 64 x 64 PNGs, the 64 px frame of each app's own hallow.ico (compared when the sibling repo is on this PC)
            foreach (var a in four)
            {
                var n = a.Name.ToLowerInvariant();
                var pf = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\src\icons\" + n + ".png")); byte[] png = File.Exists(pf) ? File.ReadAllBytes(pf) : null;
                Ok(png != null && png.Length > 24 && png[0] == 0x89 && png[1] == (byte)'P' && (png[16] << 24 | png[17] << 16 | png[18] << 8 | png[19]) == 64 && (png[20] << 24 | png[21] << 16 | png[22] << 8 | png[23]) == 64, "1.22: src\\icons\\" + n + ".png is there (64 x 64 PNG like the others)");
                var ico = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\Hallow" + a.Name + @"\src\hallow.ico"));
                if (File.Exists(ico) && png != null) Ok(IcoFrame(File.ReadAllBytes(ico), 64) != null && IcoFrame(File.ReadAllBytes(ico), 64).SequenceEqual(png), "1.22: " + n + ".png is the 64 px frame of Hallow " + a.Name + "'s own hallow.ico");
            }
            // Start with Windows: Net starts in the tray (--tray, Setup's switch), the three windowed ones have none
            Ok(!net.NoTray && paint.NoTray && notes.NoTray && un.NoTray && files.NoTray && Setup.Find("Browser").NoTray && !Setup.Find("Find").NoTray, "1.22: Start with Windows - Net yes (--tray); Paint, Notes, Uninstall no (NoTray, like Files / Browser)");
            Ok(four.All(a => Uninstaller.QuitByCmdFirst(a) && Uninstaller.QuitAlways(a)) && Uninstaller.QuitAlways(files) && !Uninstaller.QuitAlways(Setup.Find("Find")) && !Uninstaller.QuitAlways(Setup.Find("Hub")) && !Uninstaller.QuitAlways(null), "1.22: all four hear \"--cmd quit\" first, always (Files too; not Find, not Hub)");
            Ok(Uninstaller.QuitWaitMs(paint) == 30000 && Uninstaller.QuitWaitMs(net) == 15000 && Uninstaller.QuitWaitMs(notes) == 15000 && Uninstaller.QuitWaitMs(un) == 15000, "1.22: Paint gets 30 s to write its recovery pictures, the others 15 s");
            Ok(Uninstaller.NeverForced(un) && !Uninstaller.NeverForced(paint) && !Uninstaller.NeverForced(notes) && !Uninstaller.NeverForced(net) && !Uninstaller.NeverForced(null), "1.22: only Hallow Uninstall is never force-stopped");
            Ok(Uninstaller.IsBusyReply("error busy removing") && Uninstaller.IsBusyReply(" error busy removing\r") && !Uninstaller.IsBusyReply("error busy") && !Uninstaller.IsBusyReply("ok") && !Uninstaller.IsBusyReply(null), "1.22: only \"error busy removing\" means wait (a plain \"error busy\" is a slow window, not a removal)");

            var names = new[] { "HALLOW_SETUP_API", "HALLOW_SETUP_LOCAL", "HALLOW_SETUP_RUNKEY", "HALLOW_SETUP_APPDATA", "HALLOW_SETUP_PROGRAMS", "HALLOW_SETUP_DESKTOP", "HALLOW_SETUP_PROGRAMDATA", "HALLOW_SETUP_REGROOT", "HALLOW_SETUP_PIPE" };
            var keep = names.ToDictionary(n => n, n => Environment.GetEnvironmentVariable(n));
            var root = Path.Combine(tmp, "apps122"); var lo = Path.Combine(root, "local"); var ad = Path.Combine(root, "appdata"); var sm = Path.Combine(root, "startmenu"); var dt = Path.Combine(root, "desktop"); var pd = Path.Combine(root, "programdata");
            var fake = "HallowSetupTest-" + Guid.NewGuid().ToString("N");   // never created: every registry step finds nothing there
            try
            {
                foreach (var d in new[] { lo, ad, sm, dt, pd }) Directory.CreateDirectory(d);
                var vals = new Dictionary<string, string> { { "HALLOW_SETUP_API", "http://127.0.0.1:9/unused" }, { "HALLOW_SETUP_LOCAL", lo }, { "HALLOW_SETUP_RUNKEY", @"Software\" + fake + @"\Run" }, { "HALLOW_SETUP_APPDATA", ad },
                    { "HALLOW_SETUP_PROGRAMS", sm }, { "HALLOW_SETUP_DESKTOP", dt }, { "HALLOW_SETUP_PROGRAMDATA", pd }, { "HALLOW_SETUP_REGROOT", @"Software\" + fake }, { "HALLOW_SETUP_PIPE", fake } };
                foreach (var kv in vals) Environment.SetEnvironmentVariable(kv.Key, kv.Value);
                Ok(Setup.TestMode && Uninstaller.TestGuard() == null, "1.22: test mode with every place pointing at temp folders / made-up keys");
                Ok(four.All(a => !a.StartsWithWindows), "1.22: no Start with Windows value read for any of them (the made-up key)");
                Ok(Uninstaller.NetHistoryDir() == Path.Combine(lo, "HallowNet") && Uninstaller.LocalDataDir(notes) == Path.Combine(lo, "HallowNotes") && Uninstaller.LocalDataDir(un) == Path.Combine(lo, "HallowUninstall")
                    && Uninstaller.LocalDataDir(net) == null && Uninstaller.LocalDataDir(paint) == null && four.All(a => Uninstaller.DataDir(a) == Path.Combine(ad, "Hallow" + a.Name)), "1.22: data folders - %APPDATA%\\Hallow<App>; Net's history, Notes' backups and Uninstall's backups in %LOCALAPPDATA%");
                // the dialog's words
                var nn = Uninstaller.KeptNote(net); var pn = Uninstaller.KeptNote(paint); var on = Uninstaller.KeptNote(notes); var un2 = Uninstaller.KeptNote(un);
                Ok(nn != null && nn.Contains("quit first") && nn.Contains("never starts or approves the helper") && nn.Contains("deleted for good") && nn.Contains(Uninstaller.NetHistoryDir()), "1.22: Net's note - quit first, helper never started, history deleted for good");
                Ok(pn != null && pn.Contains("quit first") && pn.Contains("recovery") && pn.Contains("Also remove my settings and data"), "1.22: Paint's note - quit first, unsaved pictures kept in recovery, they follow the data choice");
                Ok(on != null && on.Contains("ONLY if you tick") && on.Contains("Also remove my settings and data") && on.Contains(Path.Combine(lo, "HallowNotes", "backup")) && UninstallDialog.DataText(notes).Contains(Path.Combine(lo, "HallowNotes")) && UninstallDialog.DataText(notes).Contains("unsaved text"), "1.22: Notes' dialog says its unsaved-text backups go only when ticked (note and the tick box's line)");
                Ok(un2 != null && un2.Contains("registry backups") && un2.Contains("stay unless you tick") && un2.Contains("never stops") && UninstallDialog.DataText(un).Contains(Path.Combine(lo, "HallowUninstall")) && UninstallDialog.DataText(un).Contains("registry backups") && UninstallDialog.DataText(paint) == Uninstaller.DataDir(paint), "1.22: Uninstall's dialog says its registry backups stay unless ticked, and that Setup never stops it mid-removal");

                Func<HallowApp, bool, bool, UninstallResult> run = (a, withData, force) =>
                {
                    var prog = Path.Combine(lo, "Programs", a.Title); Directory.CreateDirectory(prog); File.WriteAllText(Path.Combine(prog, "Hallow" + a.Name + ".exe"), "not a real exe");
                    var data = Uninstaller.DataDir(a); Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "settings.json"), "{}");
                    File.WriteAllText(Path.Combine(sm, a.Title + ".lnk"), "x"); File.WriteAllText(Path.Combine(dt, a.Title + ".lnk"), "x");
                    lock (Uninstaller.TestLog) Uninstaller.TestLog.Clear();
                    Uninstaller.RecordRecycle = true;
                    try { return Uninstaller.Uninstall(a, withData, force, p => { }); } finally { Uninstaller.RecordRecycle = false; }
                };
                Func<string> logText = () => string.Join(" | ", Uninstaller.TestLog.Select(l => l.Replace(root, "<tmp>")));
                // the order for each: "<exe> --cmd quit" first, then the shortcuts and the program folder; settings kept when not ticked
                foreach (var a in four)
                {
                    Uninstaller.TestCmdReply = "ok";
                    var r = run(a, false, false); var log = Uninstaller.TestLog.ToList(); var prog = Path.Combine(lo, "Programs", a.Title);
                    int iQuit = log.IndexOf("run Hallow" + a.Name + ".exe --cmd quit"), iProg = log.IndexOf("recycle " + prog), iLnk = log.IndexOf("recycle " + Path.Combine(sm, a.Title + ".lnk"));
                    Ok(iQuit == 0 && iLnk > iQuit && iProg > iQuit && log.Count(l => l.StartsWith("run ")) == 1 && r.Ok && !r.Busy && !r.StillRunning, "1.22: " + a.Name + " - --cmd quit first, then shortcuts and program folder (" + logText() + ")");
                    Ok(!log.Contains("recycle " + Uninstaller.DataDir(a)) && r.Message.Contains("Your settings stay in " + Uninstaller.DataDir(a)), "1.22: " + a.Name + " - settings kept when not ticked (" + r.Message.Replace(root, "<tmp>") + ")");
                    r = run(a, true, false); log = Uninstaller.TestLog.ToList();
                    Ok(log.IndexOf("recycle " + Uninstaller.DataDir(a)) > log.IndexOf("recycle " + prog) && r.Message.Contains("Settings and data are in the Recycle Bin"), "1.22: " + a.Name + " - ticked: settings to the Recycle Bin after the program folder");
                }
                Uninstaller.TestCmdReply = null;
                // Net: its history always goes, for good - file by file, only the HallowNet folder; Setup never starts its helper
                var hist = Uninstaller.NetHistoryDir(); Directory.CreateDirectory(hist);
                foreach (var f in new[] { "history.csv", "history.csv.tmp", "timeline.csv" }) File.WriteAllText(Path.Combine(hist, f), "t,ping\n1,34\n");
                var rn = run(net, false, false); var ln = Uninstaller.TestLog.ToList();
                Ok(!Directory.Exists(hist) && rn.Message.Contains("Its history is deleted") && !ln.Contains("recycle " + hist) && Directory.Exists(Uninstaller.DataDir(net)), "1.22: Net - history deleted for good (not the Recycle Bin) with the settings kept (" + rn.Message.Replace(root, "<tmp>") + ")");
                Ok(!ln.Any(l => l.Contains("--helper") || l.Contains("runas")), "1.22: Net - Setup never starts its helper (" + logText() + ")");
                var notNet = Path.Combine(lo, "NotHallowNet"); Directory.CreateDirectory(notNet); File.WriteAllText(Path.Combine(notNet, "history.csv"), "x");
                Ok(Uninstaller.WipeNetHistory(notNet) == 0 && File.Exists(Path.Combine(notNet, "history.csv")) && Uninstaller.WipeNetHistory(null) == 0, "1.22: the history wipe only ever runs in a HallowNet folder");
                // Notes: the backups of unsaved text stay unless ticked
                var nb = Path.Combine(Uninstaller.LocalDataDir(notes), "backup"); Directory.CreateDirectory(nb); File.WriteAllText(Path.Combine(nb, "tab-1.txt"), "unsaved text");
                var ro = run(notes, false, false); var lno = Uninstaller.TestLog.ToList();
                Ok(lno[0] == "run HallowNotes.exe --cmd quit" && !lno.Any(l => l.StartsWith("recycle " + Uninstaller.LocalDataDir(notes))) && File.Exists(Path.Combine(nb, "tab-1.txt")) && ro.Message.Contains("backups of your unsaved tabs stay in " + Uninstaller.LocalDataDir(notes)), "1.22: Notes not ticked - its unsaved-text backups stay, and the result says where (" + ro.Message.Replace(root, "<tmp>") + ")");
                ro = run(notes, true, false); lno = Uninstaller.TestLog.ToList();
                Ok(lno.IndexOf("recycle " + Uninstaller.LocalDataDir(notes)) > lno.IndexOf("recycle " + Path.Combine(lo, "Programs", notes.Title)) && ro.Message.Contains("backups of your unsaved tabs are in the Recycle Bin"), "1.22: Notes ticked - the backups go to the Recycle Bin, after quit and the program folder (" + logText() + ")");
                // Hallow Uninstall busy removing: nothing removed, never forced - not even with force
                var ub = Path.Combine(Uninstaller.LocalDataDir(un), "backups", "removal-1"); Directory.CreateDirectory(ub); File.WriteAllText(Path.Combine(ub, "keys.reg"), "Windows Registry Editor Version 5.00");
                Uninstaller.TestCmdReply = "error busy removing";
                foreach (var force in new[] { false, true })
                {
                    var rb = run(un, true, force); var lb = Uninstaller.TestLog.ToList();
                    Ok(rb.Busy && !rb.Ok && !rb.StillRunning && rb.Message.Contains("wait") && rb.Message.Contains("never stops it") && lb.Count == 1 && lb[0] == "run HallowUninstall.exe --cmd quit"
                        && File.Exists(Path.Combine(lo, "Programs", un.Title, "HallowUninstall.exe")) && File.Exists(Path.Combine(ub, "keys.reg")), "1.22: Uninstall busy removing" + (force ? " (even with force)" : "") + " - stops after quit, removes nothing, says wait (" + rb.Message + ")");
                }
                Uninstaller.TestCmdReply = null;
                var ru = run(un, false, false); var lu = Uninstaller.TestLog.ToList();
                Ok(ru.Ok && !lu.Any(l => l.StartsWith("recycle " + Uninstaller.LocalDataDir(un))) && File.Exists(Path.Combine(ub, "keys.reg")) && ru.Message.Contains("history and registry backups stay in " + Uninstaller.LocalDataDir(un)), "1.22: Uninstall not ticked - its registry backups stay, and the result says so (" + ru.Message.Replace(root, "<tmp>") + ")");
                ru = run(un, true, false); lu = Uninstaller.TestLog.ToList();
                Ok(lu.Contains("recycle " + Uninstaller.LocalDataDir(un)) && ru.Message.Contains("registry backups are in the Recycle Bin"), "1.22: Uninstall ticked - history and registry backups to the Recycle Bin");
                Ok(Directory.Exists(Path.Combine(lo, "Programs", un.Title)) && File.Exists(Path.Combine(ub, "keys.reg")), "1.22: test mode deleted nothing it only recorded (program folder and backups still on disk)");
            }
            finally
            {
                Uninstaller.RecordRecycle = false; Uninstaller.TestCmdReply = null;
                foreach (var kv in keep) Environment.SetEnvironmentVariable(kv.Key, kv.Value);
            }
        }
        // the PNG frame of an .ico with this width (null if there's none)
        static byte[] IcoFrame(byte[] ico, int size)
        {
            if (ico == null || ico.Length < 6) return null;
            int n = BitConverter.ToUInt16(ico, 4);
            for (int i = 0; i < n && 6 + 16 * i + 16 <= ico.Length; i++)
            {
                int e = 6 + 16 * i, w = ico[e] == 0 ? 256 : ico[e], len = BitConverter.ToInt32(ico, e + 8), off = BitConverter.ToInt32(ico, e + 12);
                if (w != size || off < 0 || len <= 0 || off + len > ico.Length) continue;
                var f = new byte[len]; Array.Copy(ico, off, f, 0, len); return f;
            }
            return null;
        }

        static void CliTests(string tmp)        {
            // ---- the catalogue: every current family app or part
            NewApps119(tmp);
            NewApps120(tmp);
            NewApps121(tmp);
            NewApps122(tmp);
            foreach (var n in new[] { "Hub", "Toolkit", "Mixer", "Overlay", "Remote", "Vault", "Clips", "Keys", "Assist", "Guard", "Tasks", "Browser", "VPN", "Torrents", "Financials", "Email", "Home", "Gear", "Capture", "Display", "Find", "Files", "Net", "Paint", "Notes", "Uninstall" })
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
