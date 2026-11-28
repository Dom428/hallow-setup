// Hallow Setup - v1.3: uninstall one Hallow app (no window code).
// The apps' own Uninstall.ps1 files ask their own questions in message boxes (and some delete settings for good), so
// Setup does the same steps itself, quietly: close the app (its pipe "quit"), then remove the program folder, the
// Start menu / Desktop shortcuts, the Start with Windows value and the Apps & features entry. Settings and data are
// only removed when you tick it, and everything goes to the Recycle Bin - nothing is deleted for good.
// Never touched: Hallow Vault's backup folders, Hallow Clips' video folder, Hallow Keys' password vault.
// Deleted for good, always (v1.19 / v1.20): Hallow Capture's clipboard history and Hallow Find's file index - private, and nothing to keep.
// Hallow Files (v1.21) is asked to put File Explorer back as the folder opener ("--undo-default-fm") before its program folder goes.
// v1.22: Hallow Net, Paint, Notes and Uninstall always hear "<exe> --cmd quit" first. Hallow Net's history (%LOCALAPPDATA%\HallowNet) is
// deleted for good, always; Setup never starts or approves Net's admin helper. Hallow Notes' backups of unsaved tabs and Hallow
// Uninstall's history and registry backups (their %LOCALAPPDATA% folders) go only when you tick "Also remove my settings and data".
// Hallow Uninstall answers "error busy removing" while it removes an app: Setup then stops and says to wait - it never force-stops it.
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
        public bool Busy;   // v1.22: Hallow Uninstall is removing an app right now ("error busy removing") - wait, never force it
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
                else if (a.Name == "Capture")   // v1.19: the captures folder (its SaveRoot, else %USERPROFILE%\Hallow Captures)
                {
                    var f = Path.Combine(DataDir(a), "settings.json"); string dir = null;
                    if (File.Exists(f)) dir = Setting(f, "SaveRoot");
                    res.Add(!string.IsNullOrEmpty(dir) ? dir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Hallow Captures"));
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
            // v1.19: a display change Hallow Display couldn't put back yet - its restore.json lets a reinstall put it back, so the folder stays
            if (a.Name == "Display" && File.Exists(Path.Combine(DataDir(a), "restore.json"))) return "Hallow Display still has a display change to put back (restore.json), so its folder stays - reinstalling it puts the change back.";
            var kept = KeptFolders(a);
            if (kept == null) return "Couldn't read " + a.Title + "'s settings, so its data stays.";
            var d = DataDir(a);
            foreach (var k in kept) if (Inside(k, d)) return "Your " + (a.Name == "Vault" ? "backups are" : a.Name == "Capture" ? "captures are" : "clips are") + " inside " + d + ", so it stays.";
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
            if (a.Name == "Email") return "Your mail stays on your providers' servers. Ticking \"Also remove my settings and data\" removes only the encrypted cache and saved sign-ins on this PC (to the Recycle Bin).";
            if (a.Name == "Torrents") return "Your downloaded files are never touched. Its .torrent / magnet link handler is removed where it points at Hallow Torrents.";
            if (a.Name == "Capture") return "Your clipboard history is deleted (for good - it's encrypted for you and nobody should find it later). Your Hallow Captures folder stays" + (kept != null && kept.Count > 0 ? " (" + kept[0] + ")." : ".");
            if (a.Name == "Display") return "Any display change it made (a game profile's resolution, refresh rate, HDR or brightness, night mode) is put back first: Setup asks it to quit before anything is removed.";
            if (a.Name == "Find") return "Its file index is removed too, for good (" + FindIndexDir() + " - every file name on this PC; Hallow Find builds it again if you reinstall). Its settings follow \"Also remove my settings and data\".";   // v1.20
            if (a.Name == "Files") return "Folders open in File Explorer again: if you switched \"Open folders with Hallow Files\" on, Setup has Hallow Files put back exactly what it replaced before it's removed. Your files are never touched.";   // v1.21
            // v1.22
            if (a.Name == "Net") return "Setup asks Hallow Net to quit first (that stops its per-app helper cleanly) and never starts or approves the helper itself. Its 24-hour history and timeline are deleted for good (" + NetHistoryDir() + "). Its settings follow \"Also remove my settings and data\".";
            if (a.Name == "Paint") return "Setup asks Hallow Paint to quit first, so unsaved pictures are kept in its recovery folder (" + Path.Combine(DataDir(a), "recovery") + ") - they stay with its settings unless you tick \"Also remove my settings and data\". Your pictures are never touched.";
            if (a.Name == "Notes") return "Setup asks Hallow Notes to quit first, so it backs up any unsaved tab. Those backups of your unsaved text (" + Path.Combine(LocalDataDir(a), "backup") + ") are removed ONLY if you tick \"Also remove my settings and data\" - otherwise they stay for a reinstall. Your files are never touched.";
            if (a.Name == "Uninstall") return "Its history and registry backups (" + Path.Combine(LocalDataDir(a), "backups") + ") stay unless you tick \"Also remove my settings and data\" - they're what its \"Restore registry backup\" uses. If it's removing an app right now, Setup stops and asks you to wait - it never stops Hallow Uninstall mid-removal.";
            return null;
        }
        // v1.22: a second data folder in %LOCALAPPDATA% that follows "Also remove my settings and data" (null = none): Hallow Notes' backups
        // of unsaved tabs, Hallow Uninstall's history, registry backups and plans
        public static string LocalDataDir(HallowApp a)
        {
            return a != null && (a.Name == "Notes" || a.Name == "Uninstall") ? Path.Combine(Setup.Local, "Hallow" + a.Name) : null;
        }
        // v1.22: Hallow Net's 24-hour history and timeline (history.csv, timeline.csv - local only, never roams)
        public static string NetHistoryDir() { return Path.Combine(Setup.Local, "HallowNet"); }
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
            if (Setup.TestMode && RecordRecycle) { lock (TestLog) TestLog.Add("recycle " + path); return true; }   // v1.21: the uninstall-order test never reaches the Recycle Bin
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
            if (NeverForced(a)) force = false;   // v1.22: Hallow Uninstall is never force-stopped
            // 1. close it: ask it to quit (as its tray Exit does), wait for it
            // v1.21: Hallow Files always hears "--cmd quit" first (it saves its open tabs and lets a running copy finish) - even when Setup
            // sees no copy of it running, which costs nothing: with no Hallow Files to answer, the line just goes nowhere
            // (v1.22: Hallow Net, Paint, Notes and Uninstall too - their reply is read: Hallow Uninstall says "error busy removing" while
            // it removes an app, and then Setup stops right here, removes nothing and asks you to wait)
            string first = null;
            if (QuitAlways(a))
            {
                progress("Asking " + a.Title + " to quit...");
                first = CmdReply(a, "quit", 10000);
                if (IsBusyReply(first)) return BusyResult(a);
            }
            if (Processes(a).Length > 0)
            {
                progress(a.Name == "Display" ? "Closing Hallow Display (it puts your display settings back first)..." : "Closing " + a.Title + "...");
                // v1.20: Hallow Find hears "--cmd quit" first (what its own Uninstall.ps1 does: it saves and lets go of its index, then
                // exits); the pipe only if that didn't answer ok. Never in test mode: "--cmd" would reach the real app's pipe.
                bool cmdOk = QuitAlways(a) ? first != null && first.StartsWith("ok") : QuitByCmdFirst(a) && RunOwn(a, "--cmd quit", 10000);   // v1.21: test mode records it instead (false = the test pipe next)
                var q = cmdOk ? "ok" : Send(a, "quit", 3000);
                if (IsBusyReply(q)) return BusyResult(a);   // v1.22
                // v1.19: Hallow Display must hear "quit" (it puts every display change back, then exits) - if its pipe didn't answer,
                // the same line through its own "--cmd quit" (never in test mode: that would reach the real app's pipe)
                if (a.Name == "Display" && !(q != null && q.StartsWith("ok")) && !Setup.TestMode) CmdQuit(a);
                if (!WaitGone(a, QuitWaitMs(a)))
                {
                    // v1.22: Hallow Uninstall may be in the middle of a removal (an app's own uninstaller running, registry keys going) -
                    // Setup never offers to force it
                    if (NeverForced(a)) return new UninstallResult { Busy = true, Message = a.Title + " didn't close. If it's removing an app, let it finish, then close its window and click Uninstall again - Setup never stops it mid-removal. Nothing was removed." };
                    if (!force) return new UninstallResult { Message = a.Title + " didn't close.", StillRunning = true };
                    foreach (var p in Processes(a)) try { p.Kill(); } catch { }
                    if (!WaitGone(a, 5000)) return new UninstallResult { Message = "Couldn't close " + a.Title + " - close it from its tray icon and try again. Nothing was removed." };
                }
                Thread.Sleep(500);
            }
            var notes = new List<string>(); bool ok = true;
            // v1.21: Hallow Files - File Explorer back as the folder opener, while its exe (and its saved backup of what it replaced, in
            // %APPDATA%\HallowFiles) are still there; then any per-user command that still points at HallowFiles.exe is removed, so a
            // folder can never be left opening with a program that's gone
            if (a.Name == "Files")
            {
                progress("Putting File Explorer back as the folder opener...");
                bool undone = RunOwn(a, "--undo-default-fm", 15000);
                int left = RemoveFilesCommands();
                if (undone && left == 0) notes.Add("Folders open in File Explorer again.");
                else if (left == 0) notes.Add("Folders open in File Explorer again (Setup removed what pointed at Hallow Files).");
                else { ok = false; notes.Add("Some folders may still try to open with Hallow Files: remove HKEY_CURRENT_USER\\Software\\Classes\\Directory\\shell\\open\\command (and Drive\\shell\\open\\command) with Registry Editor, or reinstall Hallow Files and choose Undo - use Explorer again in its settings."); }
            }
            progress("Removing " + a.Title + "...");
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
            // v1.19: Hallow Capture's clipboard history always goes, for good (what its own uninstaller does) - before the data step,
            // so it never lands in the Recycle Bin. Its captures folder is never touched.
            if (a.Name == "Capture")
            {
                var left = WipeCaptureHistory(DataDir(a));
                if (left == 0) notes.Add("Your clipboard history is deleted.");
                else { ok = false; notes.Add("Couldn't delete " + left + (left == 1 ? " file" : " files") + " of your clipboard history in " + Path.Combine(DataDir(a), "history") + "."); }
            }
            // v1.20: Hallow Find's file index always goes, for good (its own uninstaller does the same); its settings in
            // %APPDATA%\HallowFind follow the data step below like every app's
            if (a.Name == "Find")
            {
                var left = WipeFindIndex(FindIndexDir());
                if (left == 0) notes.Add("Its file index is removed too.");
                else { ok = false; notes.Add("Couldn't delete " + left + (left == 1 ? " item" : " items") + " of its file index in " + FindIndexDir() + "."); }
            }
            // v1.22: Hallow Net's history and timeline always go, for good (its own uninstaller does the same) - ping numbers, network
            // changes and which apps used the connection are this PC's own business; its settings follow the data step below
            if (a.Name == "Net")
            {
                var left = WipeNetHistory(NetHistoryDir());
                if (left == 0) notes.Add("Its history is deleted.");
                else { ok = false; notes.Add("Couldn't delete " + left + (left == 1 ? " item" : " items") + " of its history in " + NetHistoryDir() + "."); }
            }
            // 5. settings and data, only when ticked, never a kept folder
            if (withData)
            {
                var why = DataBlocked(a); var d = DataDir(a);
                if (why != null) notes.Add(why);
                else if (Directory.Exists(d)) { if (Recycle(d)) notes.Add("Settings and data are in the Recycle Bin."); else { ok = false; notes.Add("Couldn't remove " + d + "."); } }
                if (a.Name == "Home") { var wv = Path.Combine(Setup.Local, "HallowHome"); if (Directory.Exists(wv) && !Recycle(wv)) { ok = false; notes.Add("Couldn't remove " + wv + "."); } }   // v1.16: the live view's WebView2 cache
                if (a.Name == "Email") { var wv = Path.Combine(Setup.Local, "HallowEmail"); if (Directory.Exists(wv) && !Recycle(wv)) { ok = false; notes.Add("Couldn't remove " + wv + "."); } }   // v1.17: the reader's WebView2 scratch folder
                if (a.Name == "Browser") { var prof = Path.Combine(Setup.Local, "HallowBrowser"); if (Directory.Exists(prof)) { if (Recycle(prof)) notes.Add("Browsing data (cookies, history) is in the Recycle Bin."); else { ok = false; notes.Add("Couldn't remove " + prof + "."); } } }
                // v1.22: Hallow Notes' backups of unsaved tabs / Hallow Uninstall's history and registry backups - only now, ticked
                var ld = LocalDataDir(a);
                if (ld != null && why == null && Directory.Exists(ld))
                {
                    if (Recycle(ld)) notes.Add(a.Name == "Notes" ? "The backups of your unsaved tabs are in the Recycle Bin too." : "Its history and registry backups are in the Recycle Bin too.");
                    else { ok = false; notes.Add("Couldn't remove " + ld + "."); }
                }
            }
            else
            {
                if (a.Name != "Keys" && a.Name != "Financials" && Directory.Exists(DataDir(a))) notes.Add("Your settings stay in " + DataDir(a) + ".");
                var ld = LocalDataDir(a);   // v1.22: kept, and said so
                if (ld != null && Directory.Exists(ld)) notes.Add(a.Name == "Notes" ? "The backups of your unsaved tabs stay in " + ld + "." : "Its history and registry backups stay in " + ld + ".");
            }
            var kn = a.Name == "Vault" ? "Your backups are still there." : a.Name == "Clips" ? "Your clips are still there." : a.Name == "Capture" ? "Your captures are still there." : a.Name == "Keys" ? "Your password vault is still in " + DataDir(a) + "." : a.Name == "Financials" ? "Your encrypted finance vault is still in " + DataDir(a) + "." : null;
            if (kn != null) notes.Insert(0, kn);
            return new UninstallResult { Ok = ok, Message = (ok ? a.Title + " is uninstalled." : a.Title + " is partly uninstalled.") + (notes.Count > 0 ? " " + string.Join(" ", notes) : "") };
        }
        // v1.19: how long to wait after "quit" - Hallow Display puts modes, HDR, brightness and gamma back first (DDC/CI is slow)
        // (v1.21: Hallow Files 30 s too - it lets a copy or move that's running finish before it exits)
        // (v1.22: Hallow Paint 30 s too - it writes every unsaved picture to its recovery folder first, and a picture can be 8K with layers)
        public static int QuitWaitMs(HallowApp a) { return a.Name == "Display" || a.Name == "Files" || a.Name == "Paint" ? 30000 : 15000; }
        // v1.19: "HallowDisplay.exe --cmd quit" (the same line over its pipe; waits up to 10 s for the reply)
        // (v1.20: true when it answered ok - exit code 0)
        static bool CmdQuit(HallowApp a)
        {
            try { using (var p = Process.Start(new ProcessStartInfo(a.Exe, "--cmd quit") { UseShellExecute = false, CreateNoWindow = true })) return p.WaitForExit(10000) && p.ExitCode == 0; } catch { return false; }
        }
        // v1.20: apps told to quit through their own "--cmd quit" before the pipe is tried (v1.21: Hallow Files too)
        public static bool QuitByCmdFirst(HallowApp a) { return a != null && (a.Name == "Find" || QuitAlways(a)); }
        // v1.22: apps that hear "<exe> --cmd quit" first even when Setup sees no copy running, with the reply read: Hallow Files (v1.21),
        // Hallow Net (stops its helper), Hallow Paint (unsaved pictures to recovery), Hallow Notes (backs up unsaved tabs), Hallow Uninstall
        public static bool QuitAlways(HallowApp a) { return a != null && (a.Name == "Files" || a.Name == "Net" || a.Name == "Paint" || a.Name == "Notes" || a.Name == "Uninstall"); }
        // v1.22: never force-stopped, not even after you say yes: Hallow Uninstall (a removal must not stop halfway)
        public static bool NeverForced(HallowApp a) { return a != null && a.Name == "Uninstall"; }
        // v1.22: "error busy removing" - Hallow Uninstall's answer to quit while an uninstall or removal runs
        public static bool IsBusyReply(string reply) { return reply != null && reply.Trim().StartsWith("error busy removing", StringComparison.OrdinalIgnoreCase); }
        static UninstallResult BusyResult(HallowApp a)
        {
            return new UninstallResult { Busy = true, Message = a.Title + " is removing an app right now - wait until it has finished, then click Uninstall again. Setup never stops it mid-removal, so nothing was removed." };
        }
        // v1.22: "<exe> --cmd <line>" and the reply it prints ("ok", "error busy removing"), or null when nothing answered (exit code 2 =
        // not running, no exe, or no answer in waitMs). In test mode the exe is never run: the line is recorded in TestLog (the same
        // "run <exe> --cmd quit" as RunOwn) and TestCmdReply is the answer.
        public static string TestCmdReply;
        public static string CmdReply(HallowApp a, string line, int waitMs)
        {
            if (Setup.TestMode) { lock (TestLog) TestLog.Add("run " + Path.GetFileName(a.Exe) + " --cmd " + line); return TestCmdReply; }
            try
            {
                if (!File.Exists(a.Exe)) return null;
                using (var p = Process.Start(new ProcessStartInfo(a.Exe, "--cmd " + line) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 }))
                {
                    var read = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(waitMs)) return null;   // the short-lived "--cmd" client ends by itself once its own wait is over
                    if (p.ExitCode == 2) return null;
                    var r = read.Wait(2000) ? (read.Result ?? "").Trim() : "";
                    return r.Length > 0 ? r.Split('\n')[0].Trim() : p.ExitCode == 0 ? "ok" : "error";
                }
            }
            catch { return null; }
        }
        // v1.21: the app's own exe with one switch (no window), true when it exits with 0 within waitMs. In test mode it is never
        // started - "HallowFiles.exe --cmd quit" would reach the real app's pipe and "--undo-default-fm" the real registry - the line
        // is only recorded in TestLog ("run HallowFiles.exe --cmd quit", in order with the recycles) and counts as not answered.
        public static readonly List<string> TestLog = new List<string>();
        public static bool RunOwn(HallowApp a, string args, int waitMs)
        {
            if (Setup.TestMode) { lock (TestLog) TestLog.Add("run " + Path.GetFileName(a.Exe) + " " + args); return false; }
            try { using (var p = Process.Start(new ProcessStartInfo(a.Exe, args) { UseShellExecute = false, CreateNoWindow = true })) return p.WaitForExit(waitMs) && p.ExitCode == 0; } catch { return false; }
        }
        // v1.21: test only - with RecordRecycle on (and test mode), Recycle() records "recycle <path>" in TestLog and deletes nothing,
        // so a test can run a whole uninstall without touching the Recycle Bin
        public static bool RecordRecycle;
        // v1.21: the per-user folder-opening commands Hallow Files can write (its README); a "command" key is removed only while its
        // command still names HallowFiles.exe. Returns how many are left pointing at it.
        public static readonly string[] FilesCommandKeys = { @"Software\Classes\Directory\shell\open\command", @"Software\Classes\Drive\shell\open\command",
            @"Software\Classes\CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}\shell\opennewwindow\command" };
        public static bool PointsAtFiles(string command) { return command != null && command.IndexOf("HallowFiles.exe", StringComparison.OrdinalIgnoreCase) >= 0; }
        static int RemoveFilesCommands()
        {
            int left = 0;
            foreach (var key in FilesCommandKeys)
            {
                try
                {
                    string cmd; using (var k = Registry.CurrentUser.OpenSubKey(RegRoot + key)) cmd = k == null ? null : k.GetValue("") as string;
                    if (!PointsAtFiles(cmd)) continue;
                    Registry.CurrentUser.DeleteSubKeyTree(RegRoot + key, false);
                    using (var k = Registry.CurrentUser.OpenSubKey(RegRoot + key)) if (k != null && PointsAtFiles(k.GetValue("") as string)) left++;
                }
                catch { left++; }
            }
            return left;
        }
        // v1.20: Hallow Find's file index - %LOCALAPPDATA%\HallowFind (index.bin: every file name on the PC; local only, never roams)
        public static string FindIndexDir() { return Path.Combine(Setup.Local, "HallowFind"); }
        // v1.20: the index folder deleted for good, file by file (a cache - it never goes to the Recycle Bin), then its empty folders.
        // Only a folder called HallowFind; links inside are never followed (counted as left). Returns how many entries are left.
        public static int WipeFindIndex(string indexDir)
        {
            if (string.IsNullOrEmpty(indexDir) || !string.Equals(Path.GetFileName(indexDir.TrimEnd('\\')), "HallowFind", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(indexDir)) return 0;
            try { if ((File.GetAttributes(indexDir) & FileAttributes.ReparsePoint) != 0) return 1; } catch { return 1; }
            return WipeTree(indexDir);
        }
        // v1.22: Hallow Net's history folder deleted for good the same way - only a folder called HallowNet, file by file, links never
        // followed, then the empty folders. Returns how many entries are left.
        public static int WipeNetHistory(string historyDir)
        {
            if (string.IsNullOrEmpty(historyDir) || !string.Equals(Path.GetFileName(historyDir.TrimEnd('\\')), "HallowNet", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(historyDir)) return 0;
            try { if ((File.GetAttributes(historyDir) & FileAttributes.ReparsePoint) != 0) return 1; } catch { return 1; }
            return WipeTree(historyDir);
        }
        static int WipeTree(string dir)
        {
            int left = 0;
            foreach (var sub in Directory.GetDirectories(dir))
            {
                bool link = true; try { link = (File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0; } catch { }
                if (link) left++; else left += WipeTree(sub);
            }
            foreach (var f in Directory.GetFiles(dir)) { try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { left++; } }
            try { if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir); } catch { }
            return left;
        }
        // v1.19: Hallow Capture's encrypted clipboard history (<data>\history: index.bin, img-<id>.bin and their .tmp files) deleted
        // for good, then the empty folder. Only that one folder, only those files, nothing else under it. Returns how many are left.
        public static int WipeCaptureHistory(string dataDir)
        {
            if (string.IsNullOrEmpty(dataDir) || !string.Equals(Path.GetFileName(dataDir.TrimEnd('\\')), "HallowCapture", StringComparison.OrdinalIgnoreCase)) return 0;
            var h = Path.Combine(dataDir, "history"); if (!Directory.Exists(h)) return 0;
            int left = 0;
            foreach (var f in Directory.GetFiles(h))
            {
                var n = Path.GetFileName(f).ToLowerInvariant();
                if (!(n.StartsWith("index.bin") || (n.StartsWith("img-") && n.EndsWith(".bin")) || n.EndsWith(".tmp"))) { left++; continue; }
                try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { left++; }
            }
            try { if (Directory.GetFileSystemEntries(h).Length == 0) Directory.Delete(h); } catch { }
            return left;
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
