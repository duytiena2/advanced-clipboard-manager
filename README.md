# Advanced Clipboard Manager

[English](README.md) • [Tiếng Việt](README.vi.md) • [简体中文](README.zh.md) • [日本語](README.ja.md) • [한국어](README.ko.md) • [Español](README.es.md) • [Français](README.fr.md) • [Deutsch](README.de.md) • [Русский](README.ru.md) • [Português](README.pt.md)

A local-first, keyboard-first clipboard manager for Windows 10/11 and macOS. It keeps your clipboard history, sorts each copy by type, and finds anything again instantly with **Ctrl+Shift+V** (or menu bar on macOS).

> Status: **Phases 1–3 complete, Phase 4 (macOS) in progress** (see Roadmap). Name is a working title.

<p align="center">
  <img src="docs/assets/screenshot.png" alt="Advanced Clipboard Manager UI and Settings" width="850" />
</p>

## Features

| | |
|---|---|
| Clipboard history | Every copy is saved, newest first. Supports text, images and files. Repeated copies are merged into one entry with a copy count. |
| Quick Paste | **Ctrl+Shift+V** opens a search palette. Arrow keys move, **Enter** pastes into the app you were in. |
| Auto-classification | Local rules, with a confidence score, detect SQL, JSON, XML, YAML, shell, code, logs, URLs (GitHub…), email, phone, numbers, IPs and markdown. |
| Content-aware preview | Changes dynamically based on content: syntax highlighting for SQL, JSON, XML, YAML and code; image viewer with resolution (`PNG · 1103 × 593`) and OCR text inspector; rich URL card with browser launch and URL breakdown; and clear security status for sensitive content with **Ctrl+R** reveal. |
| Search | SQLite FTS5 with prefix matching, Vietnamese without diacritics (`chao` finds `chào`, `don` finds `đơn`), and filters: `type:sql`, `type:snippet`, `type:image`, `pinned:true`, `after:2026-09-01`, `before:…`, `sensitive:true`. |
| Formatting | Copies keep their HTML/RTF formatting, so **Enter** pastes them formatted. **Ctrl+Shift+Enter** pastes plain text instead (for an image: the text found in it). |
| Paste by number | The first nine rows are numbered: **Ctrl+1…9** pastes one directly (**Ctrl+Shift+1…9** as plain text). |
| Text transforms | **Ctrl+K** (or right-click) pastes the item converted: UPPER/lower/Title/Sentence case, trim whitespace, join lines, remove blank lines, format/minify JSON, format SQL, Base64 and URL encode/decode. |
| Paste stack | Mark items with **Ctrl+Space** in the order you need them, press **Ctrl+S**, then each **Ctrl+V** in any app pastes the next one — handy for filling forms. It stops when used up, when you copy something else, or from the tray. |
| Snippets & templates | Reusable text that never expires: **Ctrl+N** saves the selection as a snippet, **Ctrl+E** edits one, or manage them in Settings. Variables: `{date}`, `{time}`, `{datetime}`, `{date:dd/MM/yyyy}`, `{clipboard}`, `{uuid}`. |
| OCR | Copied images go through Windows' built-in OCR (offline, Windows 10/11), so screenshots are found by the text in them and can be pasted as text. Uses the OCR languages installed in Windows (add Vietnamese in *Settings › Time & language › Language*). |
| Sidebar | **Ctrl+D** docks Quick Paste to the right or left screen edge as an app bar (Windows, space is reserved like the taskbar). It stays open and pastes into the app you used last. |
| Encryption | Optional: the history is encrypted with your Windows account (DPAPI), with no password needed. Text, formatting, OCR text and images are encrypted on disk, the search index lives in memory only, and duplicate detection uses a keyed hash. |
| Pin | **Ctrl+P**. Pinned items never expire and always appear first. |
| Auto-expiration | Retention per type: secrets 5 min, passwords 1 min, text 1 day, code/URL 7 days, images 1 hour. Configurable. |
| Sensitive content | API keys, tokens, JWTs, AWS keys, private keys, connection strings and passwords are detected, masked in the list, and kept out of the search index. They are hidden in preview until **Ctrl+R**, and wiped from the Windows clipboard when they expire. Private keys are never stored. |
| Privacy | Everything stays local in `%LOCALAPPDATA%\ClipboardManager` (Windows) or `~/Library/Application Support/ClipboardManager` (macOS). It respects the "don't record me" flags set by password managers, and ships with an exclusion list (1Password, KeePass, Bitwarden…). There is no network access at all. |
| Multi-select & merge | **Ctrl+Space** marks items. **Enter** pastes them merged (one per line). |
| Tray app | Pause capture, clear history (keeps pinned items and snippets), stop a paste stack, settings, check for updates, start with Windows, open data folder. |

### Quick Paste keys

| Key | Action |
|---|---|
| `↑` `↓` `PgUp` `PgDn` | Navigate |
| `Enter` | Paste (merged if several are marked) |
| `Ctrl+Shift+Enter` | Paste as plain text |
| `Ctrl+1` … `Ctrl+9` | Paste item 1…9 (add `Shift` for plain text) |
| `Ctrl+K` / right-click | Transform, then paste |
| `Ctrl+C` | Copy to clipboard without pasting (or copy selection in preview) |
| Click / drag in preview | Select partial text (`Ctrl+C` to copy, `Enter` to paste selection, right-click menu) |
| `Ctrl+P` | Pin / unpin |
| `Ctrl+Space` | Mark for multi-select (marking order is kept) |
| `Ctrl+S` | Start a paste stack with the marked items |
| `Ctrl+N` / `Ctrl+E` | Save as snippet / edit the selected snippet |
| `Ctrl+R` | Reveal sensitive content |
| `Ctrl+T` | Pin window / keep open: compact palette that stays on screen and pastes into the app you used last |
| `Ctrl+D` | Dock as a sidebar: right → left → off |
| `Ctrl+L` | Cycle split ratio: 25/75, 30/70, 40/60, 50/50 |
| `Ctrl+M` | Toggle compact widget / expanded window mode |
| `Ctrl+Shift+T` | Toggle acrylic frosted glass transparency |
| `Ctrl+,` | Open settings dialog |
| `F1` | Show all keys / keyboard reference |
| `Del` | Delete item (when the cursor is at the end of the search text) |
| `Esc` | Close |

## Build & run (Windows)

Requirements: Windows 10/11 x64 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`winget install Microsoft.DotNet.SDK.8`).

```powershell
.\build.ps1            # build + run tests
.\build.ps1 -Run       # build and start (tray icon; press Ctrl+Shift+V)
.\build.ps1 -Publish   # single-file exe in .\publish\ (needs nuget.org for runtime packs)
.\build.ps1 -Installer # Setup.exe in .\dist\ (installs Inno Setup with winget if missing)
.\build.ps1 -Msix      # Microsoft Store package (.msix) in .\dist\
```

`build.ps1` downloads the official `sqlite3.dll` (with FTS5) into `lib\`. If the download fails, the app falls back to Windows' built-in `winsqlite3.dll`. If that build lacks FTS5, search matches terms in memory instead.

You can also open `AdvancedClipboardManager.sln` in Visual Studio 2022 and press F5 with `ClipboardManager.App` set as the startup project.

## Distribution

| Channel | File | Notes |
|---|---|---|
| Microsoft Store (main) | `dist\AdvancedClipboardManager_<ver>.0_x64.msix` | The Store signs it. Install, updates and uninstall are handled by Windows. "Start with Windows" uses the package's StartupTask. Data lives in the package folder and is removed on uninstall. |
| Direct download (Windows) | `dist\AdvancedClipboardManager-Setup-<ver>.exe` | Per-user install, no admin prompt, Start Menu shortcut, optional "Start with Windows", uninstall from Settings > Apps. |
| Portable (Windows) | `publish\ClipboardManager.exe` | No install. |
| macOS (Apple Silicon) | `dist/AdvancedClipboardManager-osx-arm64.dmg` / `.zip` | Disk image installer (.dmg) and standalone `.app` bundle for Apple Silicon (M1/M2/M3/M4). |
| macOS (Intel) | `dist/AdvancedClipboardManager-osx-x64.dmg` / `.zip` | Disk image installer (.dmg) and standalone `.app` bundle for Intel Macs. |

**Publishing to the Store:**

1. In [Partner Center](https://partner.microsoft.com/dashboard), reserve the app name, then open *Product identity*.
2. Copy `Package/Identity/Name`, `Package/Identity/Publisher` and `Package/Properties/PublisherDisplayName` into the `MSIX_IDENTITY_NAME`, `MSIX_PUBLISHER` and `MSIX_PUBLISHER_DISPLAY_NAME` repository variables. You can also pass them as `-MsixIdentityName`, `-MsixPublisher` and `-MsixPublisherDisplayName` to `build.ps1 -Msix`.
3. Build the `.msix` (CI artifact `ClipboardManager-msix`), then upload it in a new submission. Store review needs a privacy policy URL, because the app reads the clipboard.
4. Bump `<Version>` in `ClipboardManager.App.csproj` for every new submission.

To test the package locally, turn on Developer Mode, run `.\build.ps1 -Msix`, then run `Add-AppxPackage -Register .\publish\msix\AppxManifest.xml`. Logos and the `.ico` are generated by `packaging\generate-assets.ps1`.

**Publishing a GitHub Release:**

Pushing a version tag triggers GitHub Actions to build, test, and automatically create a new GitHub Release with the installer, portable exe, MSIX, and macOS DMG/zip packages:

```powershell
git tag v0.2.0
git push origin v0.2.0
```

The app automatically checks for newer releases on startup and via the tray menu (**Check for updates…**), notifying users when an update is ready.

## Tests

The core logic has no Windows dependency, so the tests run on Windows, Linux and macOS:

```bash
dotnet run --project tests/ClipboardManager.Core.Tests            # all tests
dotnet run --project tests/ClipboardManager.Core.Tests -- Search  # filter by name
```

The suite covers classification, sensitive-data detection, deduplication, FTS5 search and filters, expiration, pinning, eviction, image storage, merge, settings, rich text, transforms (including the SQL formatter), encryption (no plaintext on disk, toggling in place, wrong-account detection), snippets and templates, the paste stack, OCR indexing, schema upgrades, and a 10,000-item search under 100 ms.

## CI (GitHub Actions)

`.github/workflows/build.yml` runs the core tests on Ubuntu, builds, tests and packages on `windows-latest` (portable exe, Setup.exe, and MSIX), and packages macOS `.dmg` disk image installers and `.zip` bundles on `macos-latest` (arm64 & x64). On version tags, it attaches all Windows and macOS assets directly to the GitHub Release.

## Settings

Tray → **Settings…** covers everything: shortcut (applies immediately), capture, privacy rules, excluded apps, encryption, retention per type, OCR and snippets.

Underneath, settings live in `settings.json` in the data folder (`%LOCALAPPDATA%\ClipboardManager` on Windows, `~/Library/Application Support/ClipboardManager` on macOS; tray → *Edit settings.json (advanced)*; restart the app after editing it by hand):

```json
{
  "QuickPasteHotkey": "Ctrl+Shift+V",
  "MaxItems": 5000,
  "NeverStorePasswords": false,
  "NeverStorePrivateKeys": true,
  "ExcludedApplications": ["1Password", "KeePass", "KeePassXC", "Bitwarden", "LastPass"],
  "RetentionMinutes": { "Sensitive": 5, "password": 1, "Text": 1440, "Code": 10080, "Url": 10080, "Image": 60 },
  "EncryptDatabase": false,
  "OcrEnabled": true,
  "SidebarEdge": "None"
}
```

A retention of `0` means "never expire". Errors are logged to `error.log` in the same folder.

An encrypted history can only be opened by the same Windows account on the same PC. If you move to a new PC, turn encryption off first, or start a new history there.

## Architecture

```
src/
  ClipboardManager.Core/        net8.0 — platform-independent, zero NuGet dependencies
    Classification/             ContentClassifier, SensitiveDataDetector
    Storage/                    ClipboardRepository (SQLite + FTS5), Sqlite/ (P/Invoke wrapper)
    Search/                     SearchQuery (filter syntax parser)
    Services/                   ClipboardService, ExpirationPolicy, MergeService, AppSettings,
                                TextTransforms + SqlFormatter, TemplateEngine, PasteStack
    Platform/                   IClipboardMonitor, IClipboardWriter, IHotkeyService, IPasteSimulator, IDataProtector, IOcrEngine
  ClipboardManager.App/         net8.0-windows10.0.19041.0 — Windows WPF UI + Win32 adapters
  ClipboardManager.Mac/         net8.0 — macOS Avalonia UI (Spotlight/Raycast-style palette + menu bar)
tests/ClipboardManager.Core.Tests/   self-contained test runner (no xUnit)
packaging/
  Assets/                       logos, app.ico
  mac/                          Info.plist, build-mac.sh
  AppxManifest.xml              Store package manifest
  setup.iss                     Inno Setup script
  generate-assets.ps1           Logo and icon asset generation
```

## macOS

Requirements: macOS 11+ (Apple Silicon or Intel) and [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
# Run on macOS
dotnet run --project src/ClipboardManager.Mac

# Build standalone .app bundle and .dmg installer
./packaging/mac/build-mac.sh osx-arm64 0.1.0   # Apple Silicon (M1/M2/M3/M4)
./packaging/mac/build-mac.sh osx-x64 0.1.0     # Intel Mac
```

The macOS version runs in the menu bar with a Raycast/Spotlight-style floating palette.

## Roadmap

- **Phase 2:** ~~settings UI~~, ~~snippets~~, ~~paste stack~~, ~~sidebar~~ (done); still to do: tags, history window, choosing the merge separator in the UI
- **Phase 3:** ~~database encryption~~, ~~OCR~~ (done); still to do: smart rules, optional AI (opt-in, never for sensitive items)
- **Phase 4:** ~~macOS support~~ (done); still to do: sync with end-to-end encryption, Linux


## License

[MIT](LICENSE) © [duytiena2](https://github.com/duytiena2)
