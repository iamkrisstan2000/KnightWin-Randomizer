# KnightWin Randomizer

![KnightWin wheel and knight logo](KnightWin.Randomizer/Assets/app-icon.png)

**Version 1.0.0** · Made by **KnightWin** · Native WPF for Windows 11 · [GPL-3.0-only](LICENSE)

Give a batch of files fresh names without losing their extensions. KnightWin previews every change before you commit, keeps files in place, avoids overwrites, and offers one-step Undo. No Electron, installer, account, or separate .NET runtime needed for the release EXE.

## Get started

1. Download `KnightWinRandomizer-v1.0.0.exe` from this project's GitHub Release. Close older versions before running it.
2. Add files or a folder (or drop them into the queue). **Expand folders** decides whether a dropped folder is included; **Include subfolders** walks its whole tree.
3. Pick **Total random**, **Date + random**, or **Custom + random**. Review the proposed names in the queue or tap **Review**.
4. Click **Rename files**. The wheel spins while the batch runs; each row keeps a ✓ or ✗ outcome. **Undo** restores the previous batch.

Generated random parts are **6–16 characters**. File extensions always stay intact. Files are renamed where they are, never moved or overwritten. Hidden/system files are skipped by default. Settings save under `%AppData%\KnightWin\Randomizer\settings.json`.

> The EXE is currently unsigned. Windows SmartScreen may ask for **More info → Run anyway**. Only run files downloaded from the official project page.

## Personalize it

- **App mode:** follow Windows, Light, or Dark. Choose System accent, a color, or adaptive Black/White.
- **Acrylic:** Windows 11 22H2+ requests system acrylic; older supported builds use a fallback. Actual blur depends on Windows and desktop settings.
- **About:** the RAM chip updates once a second while Settings is open. Double-click to switch between MB and the app's percentage of physical RAM. Click the knight logo to open the [project page](https://github.com/iamkrisstan2000/KnightWin-Randomizer).
- **Keyboard:** `Ctrl+O` add files · `Ctrl+Shift+O` add a folder · `Ctrl+R` rename · `Ctrl+Z` undo · `Ctrl+B` collapse rail · `Ctrl+,` Settings · `Space` re-roll a preview name when not focused on a button or text box. The wheel itself is display-only.

## Build from source

Use the **.NET 8 SDK on Windows 11 x64**. From the repository root:

```powershell
dotnet publish KnightWin.Randomizer/KnightWinRandomizer.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

The EXE is written under `KnightWin.Randomizer/bin/Release/net8.0-windows/win-x64/publish/`. To run checks:

```powershell
dotnet run --project tests/tests.csproj -c Release
python tests/ui_invariants.py
python tests/contrast_gate.py
```

Python is only needed for the source-level checks; Pillow is only needed if you regenerate the icon with `KnightWin.Randomizer/Resources/make_icon.py`. A Windows CI workflow also builds and runs the checks when the source is pushed to GitHub. Real-desktop acrylic and visual behavior still require a Windows 11 GUI check.

## ☕ Support the creator — fuel the next spin!

KnightWin is free software, and every spin stays yours. If this little tool saved you time, a coffee helps the creator keep polishing it. **Support is optional; no features are locked behind a donation.**

[![Support KnightWin on Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/N7D7292WUD)

## License & credits

Copyright © 2026 **KnightWin**. Released under the **GNU General Public License version 3 only**; see [LICENSE](LICENSE). The knight mark was supplied by the project creator. The program comes **without warranty**. You may share and modify it under GPL-3.0-only.

Source and issues: https://github.com/iamkrisstan2000/KnightWin-Randomizer
