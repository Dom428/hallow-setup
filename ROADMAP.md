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

## Next

### Still to check by hand
- A real install / update from GitHub (tested against a local stand-in for GitHub only)
- Hallow Toolkit's installer through UAC
- Self-update from the real GitHub release (tested against the stand-in, including a real exe swap in a temp folder)
- Uninstalling a real app (tested against stand-in apps in a temp folder only). Hallow Hub doesn't answer `quit` on its pipe yet, so Setup asks before closing it

### Later
- Uninstall per app - **done in 1.3** (Setup does the steps itself: the apps' own Uninstall.ps1 files ask in message boxes and some delete settings for good)
- Hallow Toolkit uninstaller (in Toolkit itself; Setup already starts one when it's there)