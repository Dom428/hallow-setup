# Hallow Setup roadmap

## Done

| Version | What it added |
|---|---|
| **1.0** | Checklist of the seven Hallow apps with installed and latest versions; Install / Update with SHA-256-verified release zips and each app's own installer run as you; Toolkit's installer started for UAC; Start with Windows per app |
| **1.1** | Install ticked (several apps in sequence, Toolkit last, updates pre-ticked); Setup updates itself (SHA-256 checked, replaces its exe after closing and reopens) |
| **1.2** | Hallow Keys in the list (install / update with the checksum check, Start with Windows) |
| **1.3** | Uninstall per app: a dialog that says what goes (program folder, Start menu shortcut, Start with Windows entry) and what never goes (Vault's backups, Clips' videos, Keys' vault); the app is closed through its pipe first; `Also remove my settings and data` is unticked by default; everything goes to the Recycle Bin. Hallow Toolkit: its own uninstaller is started (UAC) when it has one, otherwise Setup says how to remove it |
| **1.4** | Hallow Assist in the list (install / update with the checksum check, Start with Windows, uninstall) |
| **1.5** | Hallow Guard in the list (install / update with the checksum check, Start with Windows, uninstall; the uninstall dialog says that startup entries Guard disabled stay in its backup key) |
| **1.6** | Hallow Shield (browser extension) in the list: latest release vs. the version in `%LOCALAPPDATA%\Hallow Shield\chrome` (`version.json`, or `manifest.json` for 1.2 and older); Install / Update downloads `hallow-shield-chrome-<v>.zip` only when clicked, refuses it without a matching GitHub SHA-256 digest, and replaces the folder (moved aside first, put back if anything fails); steps with **Open Chrome / Edge / Brave extensions** (started only on click, address copied too), **Copy folder path**, the Load unpacked steps; an optional Firefox copy; Uninstall to the Recycle Bin; tests: version compare, digest parsing and the SHA-256 check, a 1.2 copy read from manifest.json, bad / missing digest and a missing asset refused, dry run, update replacing old files, uninstall - all against a local stand-in for GitHub; `--snap` pictures |
| **1.7** | New app icon (box + download arrow) in the family style, every size drawn on its own (16-256 px); the list shows each app's own icon (64 px PNGs built into the exe, drawn at 32 px; the Font Awesome glyph if one is missing), including the new Hallow Guard, Assist, Remote and Shield icons |
| **1.8** | The window fits the screen: its height is capped to the working area of the monitor it's on (DPI-aware) and it's kept on that monitor when it grows; the app list is in a scroll viewer (thin dark scroll bars) with the header and buttons fixed; the Shield card scrolls into view when its steps open (Install or Load in browser); `--snap` also renders the window as on a 1920x1080 screen (1032 px working area) and a 1366x768 one (728 px). Hallow Shield 1.6+: after unpacking, its Hallow family bridge (`bridge\HallowShieldBridge.exe --install`, HKCU only) is registered, uninstall unregisters it and recycles its folder; the steps explain the fixed extension ID (remove the old card, Load unpacked again; export My filters first); tests against the local GitHub stand-in with a test registry root: a 1.5 zip registers nothing, a 1.6 zip registers Chrome / Edge / Brave / Firefox, an update while the bridge is in use, uninstall |
| **1.9** | Hallow Tasks in the list (install / update with the checksum check, Start with Windows `HallowTasks` = `"<exe>" --tray`, uninstall - closed through its pipe first so lowered priorities go back; the uninstall dialog says that startup entries Tasks disabled stay in `HKCU\Software\HallowTasks`); its icon in `src\icons\tasks.png` |

## Next

### Still to check by hand
- A real install / update from GitHub (tested against a local stand-in for GitHub only)
- Hallow Toolkit's installer through UAC
- Self-update from the real GitHub release (tested against the stand-in, including a real exe swap in a temp folder)
- Uninstalling a real app (tested against stand-in apps in a temp folder only). Hallow Hub doesn't answer `quit` on its pipe yet, so Setup asks before closing it
- The window's height cap and keep-on-screen on a real screen (tested with off-screen renders at 1032 and 728 px only)
- Hallow Shield from the real GitHub release, and **Open Chrome / Edge / Brave extensions** really opening each browser's page (the tests never start a browser; if one doesn't open its internal page from the command line, the copied address is the way)

### Later
- Hallow Shield: tick it for **Install ticked**, and an update check that notices a new Shield version on its own
- Uninstall per app - **done in 1.3** (Setup does the steps itself: the apps' own Uninstall.ps1 files ask in message boxes and some delete settings for good)
- Hallow Toolkit uninstaller (in Toolkit itself; Setup already starts one when it's there)
