HALLOW SETUP - install, update, start and uninstall the Hallow apps

HOW TO USE
1. Right-click the zip > Properties > tick "Unblock" > OK
2. Right-click the zip > Extract All
3. Double-click HallowSetup.exe (no admin needed, nothing to install)

WHAT IT DOES
- Shows every Hallow app (Mixer, Hub, Vault, Overlay, Remote, Clips, Keys, Assist, Guard, Tasks, Browser, VPN, Torrents, Financials, Home, Email, Gear, Capture, Display, Find, Files, Net, Paint, Notes, Uninstall, Doctor, Updates, Switch, Clock, Fonts, Toolkit) and the Hallow
  Shield browser extension, with the version on your PC and the newest one.
- Install / Update downloads that app's release from GitHub only when you click, checks it against
  GitHub's SHA-256 checksum, and runs the app's own installer as you (never as admin).
- Hallow Toolkit installs as admin: Setup starts its installer and Windows asks you first.
- Start with Windows: the same switch as in each app's own Settings.
- Uninstall (bin button): closes the app, removes it, and keeps your settings unless you tick them.
  Everything goes to the Recycle Bin. Backups, clips and your password and finance vaults are never touched.

HALLOW SHIELD (BROWSER EXTENSION)
- Install / Update puts Shield's newest release (checked against GitHub's SHA-256) into
  %LOCALAPPDATA%\Hallow Shield\chrome, replacing the old files. Your Shield settings live in the browser, not there.
- Then: Open Chrome / Edge / Brave extensions (the address is also copied, in case the browser doesn't open it),
  Developer mode on -> Load unpacked -> pick that folder. After an update, click the reload arrow on Shield's card.
- Uninstall moves the folder to the Recycle Bin; remove Shield's card in the browser yourself.
- Shield 1.6 and newer: Setup also registers the Hallow family bridge for your user (no admin), so Hallow Hub and
  Remote can show what Shield blocked - only once you turn on "Share counts with Hallow apps on this PC" in Shield.

WHAT'S NEW IN 1.23
- Hallow Doctor, Hallow Updates, Hallow Switch, Hallow Clock and Hallow Fonts are in the list. Updates, Switch and
  Clock can start with Windows; Doctor and Fonts are windowed apps, so they have no Start with Windows switch.
  Hallow Switch's "Replace Alt+Tab" is off by default (turn it on in Switch's own Settings if you want it).
- Uninstalling any of them asks it to quit first. If Doctor is analysing crash dumps, Updates is installing
  updates or Fonts is installing fonts, Setup tells you to wait and removes nothing.
- "Also remove my settings and data" says what it takes: Doctor's copied crash dumps and reports, Updates'
  history, Clock's alarms, timers and reminders, Fonts' favourites and collections (to the Recycle Bin).
  Fonts you installed with Hallow Fonts stay installed.

WHAT'S NEW IN 1.22
- Hallow Net, Hallow Paint, Hallow Notes and Hallow Uninstall are in the list. Net can start with Windows;
  Paint, Notes and Uninstall are windowed apps, so they have no Start with Windows switch.
- Uninstalling any of them asks it to quit first. Hallow Net's 24-hour history is deleted for good (Setup never
  starts its admin helper). Hallow Paint keeps unsaved pictures in its recovery folder with its settings.
  Hallow Notes' backups of your unsaved text are removed ONLY if you tick "Also remove my settings and data".
  Hallow Uninstall's history and registry backups stay unless you tick that box; if it's removing an app,
  Setup tells you to wait and never stops it.

WHAT'S NEW IN 1.21
- Hallow Files (a faster file manager: tabs, dual pane, previews, folder sizes, bulk rename) is in the list.
  It's a windowed app, so it has no Start with Windows switch.
- Uninstalling Hallow Files asks it to quit, then puts File Explorer back as the program that opens your
  folders (if you had switched that to Hallow Files) before removing it. Your files are never touched;
  its settings stay unless you tick them, like every app's.

WHAT'S NEW IN 1.20
- Hallow Find (instant search for files, apps, sums and settings - nothing leaves the PC) is in the list.
- Uninstalling Hallow Find asks it to quit first, then always deletes its file index for good (every file
  name on the PC, in %LOCALAPPDATA%\HallowFind). Its settings stay unless you tick them, like every app's.

WHAT'S NEW IN 1.19
- Hallow Capture (screenshots, OCR and a private clipboard history) and Hallow Display (brightness,
  per-game display profiles, window layouts, night mode) are in the list.
- Uninstalling Hallow Capture deletes its clipboard history for good; your Hallow Captures folder stays.
- Uninstalling Hallow Display asks it to quit first, so every display change it made is put back.

WHAT'S NEW IN 1.18
- Hallow Hub and Hallow Remote can hand one app to Setup: HallowSetup.exe --install <App> (or --update <App>,
  --list). Add --quiet for no window.
- Hallow Gear is in the list. Hallow Places (on your NAS) and Hallow Authenticator (on your phone) are shown with a link.
- The window can be resized and maximised; the app list scrolls on small screens.
- Downloads must come from the app's own GitHub releases, as well as match GitHub's checksum.

WHAT'S NEW IN 1.17
- Hallow Email (a private email client: one inbox for all your accounts, trackers and phishing
  blocked, a private spam filter) is in the list. Uninstalling it never touches your mail on the servers.

WHAT'S NEW IN 1.16
- Hallow Home (your pets' feeder and fountain, cameras and lock through Home Assistant) is in the list.
  Uninstalling it never touches Home Assistant on your NAS or your saved snapshots.

WHAT'S NEW IN 1.15
- Hallow Financials (private personal finance in an encrypted vault) is in the list. Uninstalling it never
  touches its encrypted vault or backups.

WHAT'S NEW IN 1.14
- Hallow Torrents (a game-aware torrent client that only runs over your VPN) is in the list. Uninstalling it
  never touches your downloads and removes its .torrent / magnet link handler.

WHAT'S NEW IN 1.13
- Hallow VPN (your NordVPN subscription on Windows' own IKEv2 VPN client) is in the list. Uninstalling it removes
  its Windows VPN connection and, if its kill switch is installed, asks Windows to remove that too.

WHAT'S NEW IN 1.12
- Light mode readability fixes: with a violet or other mid-tone accent, the filled buttons (Install, Update,
  Uninstall) use a slightly darker accent with white text, so their labels are easy to read.

WHAT'S NEW IN 1.11
- Hallow Browser (a fast Chromium browser for gamers with Hallow Shield built in) is in the list.
  Setup never makes it your default browser.

WHAT'S NEW IN 1.10
- Light and dark mode: Setup follows the mode picked in Hallow Toolkit, or pick its own Look at the bottom right
  (Follow Hallow / Light / Dark).

WHAT'S NEW IN 1.9
- Hallow Tasks (a better Task Manager, no admin) is in the list.

WHAT'S NEW IN 1.8
- The window always fits your screen and the app list scrolls (the Shield steps were cut off on a 1080p screen).
- Hallow Shield 1.6: its Hallow family bridge is registered when Shield is installed or updated.

WHAT'S NEW IN 1.7
- New app icon (a box with a download arrow), and every app in the list shows its own icon.

WHAT'S NEW IN 1.6
- Hallow Shield, the browser extension, is in the list.

WHAT'S NEW IN 1.5
- Hallow Guard is in the list.

WHAT'S NEW IN 1.4
- Hallow Assist is in the list.

WHAT'S NEW IN 1.3
- Uninstall per app (to the Recycle Bin; settings only when you tick them).

WHAT'S NEW IN 1.2
- Hallow Keys is in the list.

WHAT'S NEW IN 1.1
- Install ticked: tick apps and install or update them one after another.
- Hallow Setup updates itself (checksum checked, reopens by itself).

WHAT'S NEW IN 1.0
- First version.

CREDITS
Icons: Font Awesome Free 7.3.1 by Fonticons, Inc. (https://fontawesome.com), licensed CC BY 4.0 - see FontAwesome-LICENSE.txt.