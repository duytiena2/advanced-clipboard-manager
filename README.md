# Advanced Clipboard Manager

A local-first, keyboard-first clipboard manager for Windows 10/11. It keeps your clipboard history, sorts each copy by type, and finds anything again instantly with **Ctrl+Shift+V**.

> Status: **Phase 1 + most of Phases 2–3** (see Roadmap). Name is a working title.

## Features

| | |
|---|---|
| Clipboard history | Every copy is saved, newest first. Supports text, images and files. Repeated copies are merged into one entry with a copy count. |
| Quick Paste | **Ctrl+Shift+V** opens a search palette. Arrow keys move, **Enter** pastes into the app you were in. |
| Auto-classification | Local rules, with a confidence score, detect SQL, JSON, XML, YAML, shell, code, logs, URLs (GitHub…), email, phone, numbers, IPs and markdown. |
| Search | SQLite FTS5 with prefix matching, Vietnamese without diacritics (`chao` finds `chào`, `don` finds `đơn`), and filters: `type:sql`, `type:snippet`, `type:image`, `pinned:true`, `workspace:x`, `after:2026-09-01`, `before:…`, `sensitive:true`. |
| Formatting | Copies keep their HTML/RTF formatting, so **Enter** pastes them formatted. **Ctrl+Shift+Enter** pastes plain text instead (for an image: the text found in it). |
| Paste by number | The first nine rows are numbered: **Ctrl+1…9** pastes one directly (**Ctrl+Shift+1…9** as plain text). |
| Text transforms | **Ctrl+K** (or right-click) pastes the item converted: UPPER/lower/Title/Sentence case, trim whitespace, join lines, remove blank lines, format/minify JSON, format SQL, Base64 and URL encode/decode. |
| Paste stack | Mark items with **Ctrl+Space** in the order you need them, press **Ctrl+S**, then each **Ctrl+V** in any app pastes the next one — handy for filling forms. It stops when used up, when you copy something else, or from the tray. |
| Snippets & templates | Reusable text that never expires: **Ctrl+N** saves the selection as a snippet, **Ctrl+E** edits one, or manage them in Settings. Variables: `{date}`, `{time}`, `{datetime}`, `{date:dd/MM/yyyy}`, `{clipboard}`, `{uuid}`. |
| Workspaces by app | Rules such as `Code → Dev`, `OUTLOOK → Mail` file copies into workspaces automatically. **Ctrl+W** cycles the workspace filter. |
| OCR | Copied images go through Windows' built-in OCR (offline), so screenshots are found by the text in them and can be pasted as text. Uses the OCR languages installed in Windows (add Vietnamese in *Settings › Time & language › Language*). |
| Sidebar | **Ctrl+D** docks Quick Paste to the right or left screen edge as an app bar (the space is reserved like the taskbar). It stays open and pastes into the app you used last. |
| Encryption | Optional: the history is encrypted with your Windows account (DPAPI), with no password needed. Text, formatting, OCR text and images are encrypted on disk, the search index lives in memory only, and duplicate detection uses a keyed hash. |
| Pin | **Ctrl+P**. Pinned items never expire and always appear first. |
| Auto-expiration | Retention per type: secrets 5 min, passwords 1 min, text 1 day, code/URL 7 days, images 1 hour. Configurable. |
| Sensitive content | API keys, tokens, JWTs, AWS keys, private keys, connection strings and passwords are detected, masked in the list, and kept out of the search index. They are hidden in preview until **Ctrl+R**, and wiped from the Windows clipboard when they expire. Private keys are never stored. |
| Privacy | Everything stays local in `%LOCALAPPDATA%\ClipboardManager`. It respects the "don't record me" flags set by password managers, and ships with an exclusion list (1Password, KeePass, Bitwarden…). There is no network access at all. |
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
| `Ctrl+W` | Next workspace |
| `Ctrl+R` | Reveal sensitive content |
| `Ctrl+T` | Pin window / keep open: compact palette that stays on screen and pastes into the app you used last |
| `Ctrl+D` | Dock as a sidebar: right → left → off |
| `F1` | Show all keys |
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
| Direct download | `dist\AdvancedClipboardManager-Setup-<ver>.exe` | Per-user install, no admin prompt, Start Menu shortcut, optional "Start with Windows", uninstall from Settings > Apps. |
| Portable | `publish\ClipboardManager.exe` | No install. |

**Publishing to the Store:**

1. In [Partner Center](https://partner.microsoft.com/dashboard), reserve the app name, then open *Product identity*.
2. Copy `Package/Identity/Name`, `Package/Identity/Publisher` and `Package/Properties/PublisherDisplayName` into the `MSIX_IDENTITY_NAME`, `MSIX_PUBLISHER` and `MSIX_PUBLISHER_DISPLAY_NAME` repository variables. You can also pass them as `-MsixIdentityName`, `-MsixPublisher` and `-MsixPublisherDisplayName` to `build.ps1 -Msix`.
3. Build the `.msix` (CI artifact `ClipboardManager-msix`), then upload it in a new submission. Store review needs a privacy policy URL, because the app reads the clipboard.
4. Bump `<Version>` in `ClipboardManager.App.csproj` for every new submission.

To test the package locally, turn on Developer Mode, run `.\build.ps1 -Msix`, then run `Add-AppxPackage -Register .\publish\msix\AppxManifest.xml`. Logos and the `.ico` are generated by `packaging\generate-assets.ps1`.

**Publishing a GitHub Release:**

Pushing a version tag triggers GitHub Actions to build, test, and automatically create a new GitHub Release with the installer, portable exe, and MSIX:

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

The suite covers classification, sensitive-data detection, deduplication, FTS5 search and filters, expiration, pinning, eviction, image storage, merge, settings, rich text, transforms (including the SQL formatter), workspace rules, encryption (no plaintext on disk, toggling in place, wrong-account detection), snippets and templates, the paste stack, OCR indexing, schema upgrades, and a 10,000-item search under 100 ms.

## CI (GitHub Actions)

`.github/workflows/build.yml` runs the core tests on Ubuntu, then builds, tests and packages on `windows-latest`. It uploads three artifacts: the portable exe, Setup.exe and the MSIX.

## Settings

Tray → **Settings…** covers everything: shortcut (applies immediately), capture, privacy rules, excluded apps, encryption, retention per type, workspace rules, OCR and snippets.

Underneath, settings live in `settings.json` in the data folder (tray → *Edit settings.json (advanced)*; restart the app after editing it by hand):

```json
{
  "QuickPasteHotkey": "Ctrl+Shift+V",
  "MaxItems": 5000,
  "NeverStorePasswords": false,
  "NeverStorePrivateKeys": true,
  "ExcludedApplications": ["1Password", "KeePass", "KeePassXC", "Bitwarden", "LastPass"],
  "RetentionMinutes": { "Sensitive": 5, "password": 1, "Text": 1440, "Code": 10080, "Url": 10080, "Image": 60 },
  "WorkspaceRules": [ { "App": "Code", "Workspace": "Dev" }, { "App": "OUTLOOK", "Workspace": "Mail" } ],
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
  ClipboardManager.App/         net8.0-windows10.0.19041.0 — WPF UI + Win32 adapters
    Platform/                   AddClipboardFormatListener, RegisterHotKey, SendInput, startup (Run key / MSIX StartupTask),
                                DPAPI, Windows.Media.Ocr, app bar (sidebar), paste-key hook (paste stack)
    UI/                         QuickPasteWindow, SettingsWindow, SnippetDialog, TrayIcon, PasteStackController
tests/ClipboardManager.Core.Tests/   self-contained test runner (no xUnit)
packaging/                      AppxManifest.xml (MSIX), setup.iss (Inno Setup), Assets/ (logos, app.ico)
```

**Cross-platform later:** Core already runs on Linux and macOS. A macOS/Linux version would replace only `ClipboardManager.App`, for example with an Avalonia UI plus platform adapters that implement the `Platform/` interfaces.

## Roadmap

- **Phase 2:** ~~settings UI~~, ~~per-app workspace rules~~, ~~snippets~~, ~~paste stack~~, ~~sidebar~~ (done); still to do: tags, history window, choosing the merge separator in the UI
- **Phase 3:** ~~database encryption~~, ~~OCR~~ (done); still to do: smart rules, optional AI (opt-in, never for sensitive items)
- **Phase 4:** sync with end-to-end encryption, macOS/Linux

## License

[MIT](LICENSE) © [duytiena2](https://github.com/duytiena2)
