# Hallow Setup

One window to install, update, start and uninstall the Hallow family - [Hallow Mixer](https://github.com/Dom428/hallow-mixer), [Hallow Hub](https://github.com/Dom428/hallow-hub), [Hallow Vault](https://github.com/Dom428/hallow-vault), [Hallow Overlay](https://github.com/Dom428/hallow-overlay), [Hallow Remote](https://github.com/Dom428/hallow-remote), [Hallow Clips](https://github.com/Dom428/hallow-clips), [Hallow Keys](https://github.com/Dom428/hallow-keys), [Hallow Assist](https://github.com/Dom428/hallow-assist), [Hallow Guard](https://github.com/Dom428/hallow-guard) and [Hallow Toolkit](https://github.com/Dom428/hallow-toolkit) - plus the [Hallow Shield](https://github.com/Dom428/hallow-shield) browser extension.

- A checklist of every Hallow app with its own icon: what it does, the version on your PC and the newest one on GitHub
- **Install / Update** per app: nothing is downloaded until you click. The release zip is checked against the SHA-256 checksum GitHub publishes for it (no checksum or a mismatch = not installed), unpacked to a temp folder, and the app's own `Install.ps1` runs as you - never as admin
- **Hallow Toolkit** installs as admin, so Setup only starts its own installer and Windows asks you (UAC)
- **Install ticked**: several apps in one go, one after another
- **Updates itself** from its own GitHub releases (checksum checked, reopens by itself)
- **Uninstall** per app (the bin button): says what goes and closes the app first. Your settings stay unless you tick **Also remove my settings and data**, and everything goes to the Recycle Bin. Hallow Vault's backups, Hallow Clips' videos and Hallow Keys' password vault are never touched. Hallow Toolkit: Setup starts its own uninstaller (it needs admin) or tells you how to remove it
- **Start with Windows** per app: the app's own `Hallow<App>` value in HKCU\...\Run (`"<exe>" --tray`), the same one its Settings switch writes
- **Hallow Shield (browser extension)**: shows the newest release and the version in its folder (`version.json`, or `manifest.json` for 1.2 and older). **Install / Update** downloads `hallow-shield-chrome-<version>.zip` from Shield's latest GitHub release only when you click, checks it against GitHub's SHA-256 digest (no digest or a mismatch = not installed) and replaces the files in `%LOCALAPPDATA%\Hallow Shield\chrome`. That's safe: nothing of yours is in that folder - Shield keeps its settings, My filters and stats in the browser's own profile. Then a short list of steps: **Open Chrome / Edge / Brave extensions** (starts that browser on its extensions page only when you click, and copies the address in case the browser doesn't open its own pages from outside), **Copy folder path**, and *Developer mode on -> Load unpacked -> pick this folder; after an update, click the reload arrow on Shield's card*. A Firefox copy can go to `...\Hallow Shield\firefox` (Firefox only keeps unsigned add-ons until it restarts). **Uninstall** moves the folder to the Recycle Bin - remove Shield's card in the browser yourself. Setup never changes a browser on its own

Nothing to install for Setup itself: download **Hallow Setup.zip** from the [latest release](../../releases/latest), unblock and extract it, and run `HallowSetup.exe`.

Built with the C# compiler that ships with Windows (.NET Framework 4.8): run `build.ps1`. No admin needed.

## What's new

- **1.7** - New app icon: a box with a download arrow, in the Hallow family style. Every app in the list (and Hallow Shield) now shows its own app icon instead of a plain symbol.
- **1.6** - Hallow Shield, the browser extension, is in the list: install / update its folder with the checksum check, buttons to open Chrome's, Edge's or Brave's extensions page and copy the folder path, and uninstall to the Recycle Bin.
- **1.5** - Hallow Guard is in the list.
- **1.4** - Hallow Assist is in the list.
- **1.3** - Uninstall any Hallow app from Setup: the app closes first, settings stay unless you tick them, and everything goes to the Recycle Bin. Backups, clips and your password vault are never touched.
- **1.2** - Hallow Keys is in the list.
- **1.1** - Tick apps and click **Install ticked** to install or update them one after another (updates are ticked for you, Toolkit goes last). Hallow Setup updates itself too - same checksum check, then it reopens.
- **1.0** - First version: install, update and start-with-Windows for all seven Hallow apps, with checksum-verified downloads.

## Credits

Icons: [Font Awesome Free](https://fontawesome.com) 7.3.1 by Fonticons, Inc., licensed [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) (see FontAwesome-LICENSE.txt).