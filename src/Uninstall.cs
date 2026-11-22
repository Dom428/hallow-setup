// Hallow Setup - v1.3: uninstall one Hallow app (no window code).
// The apps' own Uninstall.ps1 files ask their own questions in message boxes (and some delete settings for good), so
// Setup does the same steps itself, quietly: close the app (its pipe "quit"), then remove the program folder, the
// Start menu / Desktop shortcuts, the Start with Windows value and the Apps & features entry. Settings and data are
// only removed when you tick it, and everything goes to the Recycle Bin - nothing is deleted for good.
// Never touched: Hallow Vault's backup folders, Hallow Clips' video folder, Hallow Keys' password vault.
// Hallow Toolkit installs as admin: Setup only starts its own uninstaller (Windows asks you) or tells you what to do.
// Developer test overrides (on top of Setup.cs's): HALLOW_SETUP_APPDATA, HALLOW_SETUP_PROGRAMS (Start menu Programs),
// HALLOW_SETUP_DESKTOP, HALLOW_SETUP_PROGRAMDATA, HALLOW_SETUP_REGROOT (HKCU key the Uninstall / NativeMessagingHosts
// keys live under), HALLOW_SETUP_PIPE (pipe name prefix instead of HallowFamily). In test mode all of them must be set.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace HallowSetup
{
    public class UninstallResult
    {
        public string Message; public bool Ok; public bool StillRunning;   // StillRunning: it didn't close - ask before forcing it
    }

    public static class Uninstaller
    {
        static string Env(string n) { var v = Environment.GetEnvironmentVariable(n); return string.IsNullOrEmpty(v) ? null : v; }
        public static string AppData { get { return Env("HALLOW_SETUP_APPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }
        public static string StartMenu { get { return Env("HALLOW_SETUP_PROGRAMS") ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs); } }
        public static string Desktop { get { return Env("HALLOW_SETUP_DESKTOP") ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } }
        public static string ProgramData { get { return Env("HALLOW_SETUP_PROGRAMDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData); } }
        static string RegRoot { get { var r = Env("HALLOW_SETUP_REGROOT"); return r == null ? "" : r.TrimEnd('\\') + "\\"; } }
        static string PipePrefix { get { return Env("HALLOW_SETUP_PIPE") ?? "HallowFamily"; } }
        static readonly string[] TestVars = { "HALLOW_SETUP_LOCAL", "HALLOW_SETUP_RUNKEY", "HALLOW_SETUP_APPDATA", "HALLOW_SETUP_PROGRAMS", "HALLOW_SETUP_DESKTOP", "HALLOW_SETUP_PROGRAMDATA", "HALLOW_SETUP_REGROOT", "HALLOW_SETUP_PIPE" };
        // in test mode every place must point at the test's own folders / keys, so a test can never reach a real app
        public static string TestGuard()
        {
            if (!Setup.TestMode) return null;
            var miss = TestVars.Where(v => Env(v) == null).ToList();
            return miss.Count == 0 ? null : "Test mode without " + string.Join(", ", miss) + " - nothing uninstalled.";
        }

        public static string ProgramDir(HallowApp a) { return Path.GetDirectoryName(a.Exe); }
        // the app's settings and data folder (Clips keeps its in Local, the rest in Roaming)
        public static string DataDir(HallowApp a)
        {
            if (a.IsToolkit) return null;
            return a.Name == "Clips" ? Path.Combine(Setup.Local, "HallowClips") : Path.Combine(AppData, "Hallow" + a.Name);
        }

        // copies of the app running from its installed exe (only that path, so a copy somewhere else is never touched)
        public static Process[] Processes(HallowApp a)
        {
            if (a.IsToolkit) return new Process[0];
            var list = new List<Process>();
            foreach (var p in Process.GetProcessesByName("Hallow" + a.Name))
            {
                string path = null; try { path = p.MainModule.FileName; } catch { }
                if (path != null ? string.Equals(Path.GetFullPath(path), Path.GetFullPath(a.Exe), StringComparison.OrdinalIgnoreCase) : !Setup.TestMode) list.Add(p);
            }
            return list.ToArray();
        }

        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        static string Setting(string file, string key)
        {
            var d = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(file)); object v;
            return d != null && d.TryGetValue(key, out v) ? v as string : null;
        }
        // folders that hold your own files and are never removed: Vault's backups (and its second copy), Clips' videos.
        // null = couldn't read the app's settings, so data removal isn't safe.
        public static List<string> KeptFolders(HallowApp a)
        {
            var res = new List<string>();
            try
            {
                if (a.Name == "Vault")
                {
                    var f = Path.Combine(DataDir(a), "vault.json"); string dir = null, copy = null;
                    if (File.Exists(f)) { dir = Setting(f, "VaultDir"); copy = Setting(f, "CopyDir"); }
                    res.Add(!string.IsNullOrEmpty(dir) ? dir : VaultDefault());
                    if (!string.IsNullOrEmpty(copy)) res.Add(copy);
                }
                else if (a.Name == "Clips")
                {
                    var f = Path.Combine(DataDir(a), "clips.json"); string dir = null;
                    if (File.Exists(f)) dir = Setting(f, "Folder");
                    res.Add(!string.IsNullOrEmpty(dir) ? dir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Hallow Clips"));
                }
                else if (a.Name == "Keys" || a.Name == "Financials") res.Add(DataDir(a));   // the password vault / the finance vault: Setup never removes them
            }
            catch { return null; }
            return res;
        }
        // Hallow Vault's own default (Saves.cs DefaultRoot): a second fixed drive with 20 GB free, else Documents
        static string VaultDefault()
        {
            try
            {
                var sys = Path.GetPathRoot(Environment.SystemDirectory);
                var other = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady && !string.Equals(d.RootDirectory.FullName, sys, StringComparison.OrdinalIgnoreCase) && d.AvailableFreeSpace > (20L << 30))
                    .OrderByDescending(d => d.AvailableFreeSpace).FirstOrDefault();
                if (other != null) return Path.Combine(other.RootDirectory.FullName, "Hallow Vault");
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Hallow Vault");
        }
        static string Full(string p) { return Path.GetFullPath(p).TrimEnd('\\') + "\\"; }
        static bool Inside(string path, string folder) { return Full(path).StartsWith(Full(folder), StringComparison.OrdinalIgnoreCase); }
        // can "Also remove my settings and data" be offered? (null = yes, else why not)
        public static string DataBlocked(HallowApp a)
        {
            if (a.IsToolkit) return "Hallow Toolkit keeps its settings in its own folder.";
            if (a.Name == "Keys") return "Your password vault stays in " + DataDir(a) + " - Hallow Setup never removes it.";
            if (a.Name == "Financials") return "Your encrypted finance vault and its backups stay in " + DataDir(a) + " - Hallow Setup never removes them.";
            var kept = KeptFolders(a);
            if (kept == null) return "Couldn't read " + a.Title + "'s settings, so its data stays.";
            var d = DataDir(a);
            foreach (var k in kept) if (Inside(k, d)) return "Your " + (a.Name == "Vault" ? "backups are" : "clips are") + " inside " + d + ", so it stays.";
            return null;
        }
        // one line for the dialog about what is never touched (or null)
        public static string KeptNote(HallowApp a)
        {
            var kept = KeptFolders(a);
            if (a.Name == "Vault") return "Your backups are never touched" + (kept != null && kept.Count > 0 ? " (" + string.Join(", ", kept) + ")." : ".");
            if (a.Name == "Clips") return "Your saved clips are never touched" + (kept != null && kept.Count > 0 ? " (" + kept[0] + ")." : ".");
            if (a.Name == "Keys") return "Your password vault is never touched (" + DataDir(a) + ").";
            if (a.Name == "Financials") return "Your encrypted finance vault and its backups are never touched (" + DataDir(a) + ").";
            if (a.Name == "Guard") return "Startup entries you disabled with Hallow Guard stay in its backup (HKCU\\Software\\HallowGuard) - reinstall Hallow Guard to put them back.";
            if (a.Name == "Tasks") return "Startup entries you disabled with Hallow Tasks stay in its backup (HKCU\\Software\\HallowTasks) - reinstall Hallow Tasks to put them back.";
            if (a.Name == "VPN") return "Windows' \"Hallow VPN\" connection is disconnected and removed. If its kill switch is installed, Windows asks for permission to remove it too. NordVPN's root certificate stays.";
            if (a.Name == "Home") return "Home Assistant on your NAS isn't touched (revoke Hallow Home's token there if you like). Snapshots in Pictures\\Hallow Home stay.";
            if (a.Name == "Torrents") return "Your downloaded files are never touched. Its .torrent / magnet link handler is removed where it points at Hallow Torrents.";
            return null;
        }
        // v1.13: how many of Hallow VPN's kill switch firewall rules exist (read-only: netsh lists the rules)
        public static int VpnKillSwitchRules()
        {
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"), "advfirewall firewall show rule name=all dir=out") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (var p = Process.Start(psi)) { var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(15000); return o.Split('\n').Count(l => l.Contains("Hallow VPN kill switch - ")); }
            }
            catch { return 0; }
        }
        public static string[] Shortcuts(HallowApp a) { return new[] { Path.Combine(StartMenu, a.Title + ".lnk"), Path.Combine(Desktop, a.Title + ".lnk") }; }

        // ---- the family pipe (HALLOW-FAMILY.md): one line in, one line back
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool WaitNamedPipe(string name, int timeout);
        public static string PipeName(HallowApp a) { return PipePrefix + "." + a.Name + "." + Environment.UserName; }
        public static string Send(HallowApp a, string line, int replyMs)
        {
            try
            {
                if (!WaitNamedPipe(@"\\.\pipe\" + PipeName(a), 1) && Marshal.GetLastWin32Error() == 2) return null;   // 2 = no such pipe
                using (var c = new NamedPipeClientStream(".", PipeName(a), PipeDirection.InOut))
                {
                    c.Connect(1500);
                    var b = new UTF8Encoding(false).GetBytes(line + "\n"); c.Write(b, 0, b.Length); c.Flush();
                    string result = null;
                    var t = new Thread(() =>
                    {
                        try { var buf = new List<byte>(); var one = new byte[1]; while (buf.Count < 65536) { int n = c.Read(one, 0, 1); if (n <= 0 || one[0] == (byte)'\n') break; buf.Add(one[0]); } result = Encoding.UTF8.GetString(buf.ToArray()).TrimEnd('\r'); }
                        catch { }
                    }) { IsBackground = true };
                    t.Start(); if (!t.Join(replyMs)) return null;
                    return result;
                }
            }
            catch { return null; }
        }

        // ---- Recycle Bin (SHFileOperation with FOF_ALLOWUNDO). FOF_WANTNUKEWARNING: if Windows can't recycle something
        // (too big, no Recycle Bin on that drive) it asks you instead of deleting it for good.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT { public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo; public ushort fFlags; public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
        const uint FO_DELETE = 3; const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;
        // true when the path is gone (or wasn't there)
        public static bool Recycle(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return true;
            var full = Path.GetFullPath(path).TrimEnd('\\');
            if (full.Length < 8 || Path.GetPathRoot(full).TrimEnd('\\') == full) return false;   // never a drive root
            var op = new SHFILEOPSTRUCT { wFunc = FO_DELETE, pFrom = full + "\0\0", fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING) };
            var r = SHFileOperation(ref op);
            return r == 0 && !op.fAnyOperationsAborted && !File.Exists(full) && !Directory.Exists(full);
        }

        // ---- the uninstall itself. force = close it even if it didn't quit when asked (you said yes to that).
        public static UninstallResult Uninstall(HallowApp a, bool withData, bool force, Action<string> progress)
        {
            var g = TestGuard(); if (g != null) return new UninstallResult { Message = g };
            if (a.IsToolkit) return new UninstallResult { Message = "Hallow Toolkit uninstalls from its own uninstaller." };
            if (!a.Installed) return new UninstallResult { Message = a.Title + " isn't installed." };
            var dir = ProgramDir(a);
            if (!string.Equals(Path.GetFileName(dir), a.Title, StringComparison.OrdinalIgnoreCase)) return new UninstallResult { Message = "Unexpected folder " + dir + " - nothing removed." };
            // 1. close it: ask it to quit (as its tray Exit does), wait for it
            if (Processes(a).Length > 0)
            {
                progress("Closing " + a.Title + "...");
                Send(a, "quit", 3000);
                if (!WaitGone(a, 15000))
                {
                    if (!force) return new UninstallResult { Message = a.Title + " didn't close.", StillRunning = true };
                    foreach (var p in Processes(a)) try { p.Kill(); } catch { }
                    if (!WaitGone(a, 5000)) return new UninstallResult { Message = "Couldn't close " + a.Title + " - close it from its tray icon and try again. Nothing was removed." };
                }
                Thread.Sleep(500);
            }
            progress("Removing " + a.Title + "...");
            var notes = new List<string>(); bool ok = true;
            // 2. Mixer: every app back to full volume and its normal output, and no EQ left on (what its own uninstaller does)
            if (a.Name == "Mixer")
            {
                try { var p = Process.Start(new ProcessStartInfo(a.Exe, "--reset") { UseShellExecute = false, CreateNoWindow = true }); if (!p.WaitForExit(20000)) notes.Add("Hallow Mixer's volume reset didn't finish."); }
                catch (Exception e) { notes.Add("Couldn't reset volumes: " + e.Message); }
                try { var eq = Path.Combine(ProgramData, @"HallowMixer\eq.txt"); if (File.Exists(eq)) File.WriteAllText(eq, "# Hallow Mixer was uninstalled - no EQ.\r\n", new UTF8Encoding(true)); } catch { }
            }
            // v1.13: Hallow VPN - hang up and remove its per-user "Hallow VPN" connection (no admin). Its kill switch firewall rules
            // would keep blocking the internet without the VPN, so Hallow VPN's own elevated helper removes them - Windows asks (UAC).
            if (a.Name == "VPN")
            {
                try { using (var p = Process.Start(new ProcessStartInfo("rasdial.exe", "\"Hallow VPN\" /disconnect") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })) p.WaitForExit(10000); } catch { }
                try { using (var p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"if (Get-VpnConnection -Name 'Hallow VPN' -ErrorAction SilentlyContinue) { Remove-VpnConnection -Name 'Hallow VPN' -Force }\"") { UseShellExecute = false, CreateNoWindow = true })) p.WaitForExit(30000); }
                catch { notes.Add("Couldn't remove Windows' \"Hallow VPN\" connection - remove it in Settings > Network & internet > VPN."); }
                if (VpnKillSwitchRules() > 0)
                {
                    progress("Removing Hallow VPN's kill switch (Windows asks for permission)...");
                    try { using (var p = Process.Start(new ProcessStartInfo(a.Exe, "--elevated killswitch-off") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden })) p.WaitForExit(120000); } catch { }
                    if (VpnKillSwitchRules() > 0) { ok = false; notes.Add("The kill switch's firewall rules are still there and block the internet without the VPN: remove the rules named \"Hallow VPN kill switch - ...\" in Windows Defender Firewall's advanced settings, or reinstall Hallow VPN and remove it there."); }
                    else notes.Add("Hallow VPN's kill switch is removed.");
                }
            }
            // 3. Start with Windows, Apps & features entry, Keys' browser link
            try { using (var k = Registry.CurrentUser.OpenSubKey(Setup.RunKey, true)) if (k != null) k.DeleteValue("Hallow" + a.Name, false); } catch (Exception e) { ok = false; notes.Add("Start with Windows entry: " + e.Message); }
            try { Registry.CurrentUser.DeleteSubKeyTree(RegRoot + @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Hallow" + a.Name, false); } catch { }
            if (a.Name == "Browser")   // v1.11: its "a browser you can pick" registration
            {
                foreach (var k in new[] { @"Software\Clients\StartMenuInternet\HallowBrowser", @"Software\Classes\HallowBrowserURL" }) try { Registry.CurrentUser.DeleteSubKeyTree(RegRoot + k, false); } catch { }
                try { using (var k = Registry.CurrentUser.OpenSubKey(RegRoot + @"Software\RegisteredApplications", true)) if (k != null) k.DeleteValue("Hallow Browser", false); } catch { }
            }
            if (a.Name == "Torrents")   // v1.14: its per-user .torrent / magnet handler - only where it points at Hallow Torrents
            {
                try
                {
                    string cmd = null; using (var k = Registry.CurrentUser.OpenSubKey(RegRoot + @"Software\Classes\magnet\shell\open\command")) if (k != null) cmd = k.GetValue("") as string;
                    if (cmd != null && cmd.IndexOf("HallowTorrents.exe", StringComparison.OrdinalIgnoreCase) >= 0) Registry.CurrentUser.DeleteSubKeyTree(RegRoot + @"Software\Classes\magnet", false);
                }
                catch { }
                try { using (var k = Registry.CurrentUser.OpenSubKey(RegRoot + @"Software\Classes\.torrent", true)) if (k != null && (k.GetValue("") as string) == "HallowTorrents.torrent") k.DeleteValue("", false); } catch { }
                try { using (var k = Registry.CurrentUser.OpenSubKey(RegRoot + @"Software\Classes\.torrent\OpenWithProgids", true)) if (k != null) k.DeleteValue("HallowTorrents.torrent", false); } catch { }
                foreach (var k in new[] { @"Software\Classes\HallowTorrents.torrent", @"Software\Classes\HallowTorrents.magnet", @"Software\HallowTorrents" }) try { Registry.CurrentUser.DeleteSubKeyTree(RegRoot + k, false); } catch { }
                try { using (var k = Registry.CurrentUser.OpenSubKey(RegRoot + @"Software\RegisteredApplications", true)) if (k != null) k.DeleteValue("Hallow Torrents", false); } catch { }
            }
            if (a.Name == "Financials") Recycle(Path.Combine(DataDir(a), "hello.bin"));   // the Windows Hello unlock key, not the vault
            if (a.Name == "Keys")
            {
                foreach (var b in new[] { @"Software\Google\Chrome", @"Software\Microsoft\Edge", @"Software\BraveSoftware\Brave-Browser" })
                    try { Registry.CurrentUser.DeleteSubKeyTree(RegRoot + b + @"\NativeMessagingHosts\com.hallow.keys", false); } catch { }
                foreach (var f in new[] { "hello.bin", "session.key", "native-host.json" }) Recycle(Path.Combine(DataDir(a), f));   // the unlock session, not the vault
            }
            // 4. shortcuts and the program folder (to the Recycle Bin)
            foreach (var s in Shortcuts(a)) if (!Recycle(s)) { ok = false; notes.Add("Couldn't remove " + s + "."); }
            if (!Recycle(dir)) { ok = false; notes.Add("Couldn't remove " + dir + " (is a file in it open?)."); }
            // 5. settings and data, only when ticked, never a kept folder
            if (withData)
            {
                var why = DataBlocked(a); var d = DataDir(a);
                if (why != null) notes.Add(why);
                else if (Directory.Exists(d)) { if (Recycle(d)) notes.Add("Settings and data are in the Recycle Bin."); else { ok = false; notes.Add("Couldn't remove " + d + "."); } }
                if (a.Name == "Home") { var wv = Path.Combine(Setup.Local, "HallowHome"); if (Directory.Exists(wv) && !Recycle(wv)) { ok = false; notes.Add("Couldn't remove " + wv + "."); } }   // v1.16: the live view's WebView2 cache
                if (a.Name == "Browser") { var prof = Path.Combine(Setup.Local, "HallowBrowser"); if (Directory.Exists(prof)) { if (Recycle(prof)) notes.Add("Browsing data (cookies, history) is in the Recycle Bin."); else { ok = false; notes.Add("Couldn't remove " + prof + "."); } } }
            }
            else if (a.Name != "Keys" && a.Name != "Financials" && Directory.Exists(DataDir(a))) notes.Add("Your settings stay in " + DataDir(a) + ".");
            var kn = a.Name == "Vault" ? "Your backups are still there." : a.Name == "Clips" ? "Your clips are still there." : a.Name == "Keys" ? "Your password vault is still in " + DataDir(a) + "." : a.Name == "Financials" ? "Your encrypted finance vault is still in " + DataDir(a) + "." : null;
            if (kn != null) notes.Insert(0, kn);
            return new UninstallResult { Ok = ok, Message = (ok ? a.Title + " is uninstalled." : a.Title + " is partly uninstalled.") + (notes.Count > 0 ? " " + string.Join(" ", notes) : "") };
        }
        static bool WaitGone(HallowApp a, int ms)
        {
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end) { if (Processes(a).Length == 0) return true; Thread.Sleep(250); }
            return Processes(a).Length == 0;
        }

        // ---- Hallow Toolkit: its own uninstaller, if it has one (it needs admin, so Windows asks you)
        public static string ToolkitUninstaller()
        {
            foreach (var n in new[] { "UNINSTALL.bat", "Uninstall.bat", "Uninstall.ps1" })
            {
                var f = Path.Combine(Setup.ToolkitDir, n); if (File.Exists(f)) return f;
            }
            return null;
        }
        public static string StartToolkitUninstaller()
        {
            var f = ToolkitUninstaller(); if (f == null) return "Hallow Toolkit has no uninstaller.";
            if (Setup.DryRun) return "Test run - would start " + Path.GetFileName(f) + " as admin.";
            var psi = f.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + f + "\"")
                : new ProcessStartInfo(f);
            psi.UseShellExecute = true; psi.Verb = "runas"; psi.WorkingDirectory = Setup.ToolkitDir;
            try { Process.Start(psi); }
            catch (System.ComponentModel.Win32Exception e) { if (e.NativeErrorCode == 1223) return "Cancelled - Hallow Toolkit's uninstaller needs your OK."; throw; }
            return "Hallow Toolkit's uninstaller is running - finish it in its window.";
        }
    }
}
