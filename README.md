# Advanced Clipboard Manager

A local-first, keyboard-first clipboard manager for Windows 10/11. It keeps your clipboard history, sorts each copy by type, and finds anything again instantly with **Ctrl+Shift+V**.

> Status: **MVP (Phase 1)**. Name is a working title.

## Features (MVP)

| | |
|---|---|
| Clipboard history | Every copy is saved, newest first. Supports text, images and files. Repeated copies are merged into one entry with a copy count. |
| Quick Paste | **Ctrl+Shift+V** opens a search palette. Arrow keys move, **Enter** pastes into the app you were in. |
| Auto-classification | Local rules, with a confidence score, detect SQL, JSON, XML, YAML, shell, code, logs, URLs (GitHub…), email, phone, numbers, IPs and markdown. |
| Search | SQLite FTS5 with prefix matching, Vietnamese without diacritics (`chao` finds `chào`), and filters: `type:sql`, `pinned:true`, `workspace:x`, `after:2026-09-01`, `before:…`, `sensitive:true`. |
| Pin | **Ctrl+P**. Pinned items never expire and always appear first. |
| Auto-expiration | Retention per type: secrets 5 min, passwords 1 min, text 1 day, code/URL 7 days, images 1 hour. Configurable. |
| Sensitive content | API keys, tokens, JWTs, AWS keys, private keys, connection strings and passwords are detected, masked in the list, and kept out of the search index. They are hidden in preview until **Ctrl+R**, and wiped from the Windows clipboard when they expire. Private keys are never stored. |
| Privacy | Everything stays local in `%LOCALAPPDATA%\ClipboardManager`. It respects the "don't record me" flags set by password managers, and ships with an exclusion list (1Password, KeePass, Bitwarden…). There is no network access at all. |
| Multi-select & merge | **Ctrl+Space** marks items. **Enter** pastes them merged (one per line). |
| Tray app | Pause capture, clear history, start with Windows, open data folder, edit settings. |

### Quick Paste keys

| Key | Action |
|---|---|
| `↑` `↓` `PgUp` `PgDn` | Navigate |
| `Enter` | Paste (merged if several are marked) |
| `Ctrl+C` | Copy to clipboard without pasting |
| `Ctrl+P` | Pin / unpin |
| `Ctrl+Space` | Mark for multi-select |
| `Ctrl+R` | Reveal sensitive content |
| `Ctrl+T` | Keep open: the palette stays on screen (drag it to a screen edge) and pastes into the app you used last |
| `Del` | Delete item (when the cursor is at the end of the search text) |
| `Esc` | Close |

## Build & run (Windows)

Requirements: Windows 10/11 x64 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`winget install Microsoft.DotNet.SDK.8`).

```powershell
.\build.ps1            # build + run tests
.\build.ps1 -Run       # build and start (tray icon; press Ctrl+Shift+V)
.\build.ps1 -Publish   # single-file exe in .\publish\ (needs nuget.org for runtime packs)
```

`build.ps1` downloads the official `sqlite3.dll` (with FTS5) into `lib\`. If the download fails, the app falls back to Windows' built-in `winsqlite3.dll`. If that build lacks FTS5, search falls back to `LIKE`.

You can also open `AdvancedClipboardManager.sln` in Visual Studio 2022 and press F5 with `ClipboardManager.App` set as the startup project.

## Tests

The core logic has no Windows dependency, so the tests run on Windows, Linux and macOS:

```bash
dotnet run --project tests/ClipboardManager.Core.Tests            # all tests
dotnet run --project tests/ClipboardManager.Core.Tests -- Search  # filter by name
```

The suite covers classification, sensitive-data detection, deduplication, FTS5 search and filters, expiration, pinning, eviction, image storage, merge, settings, and a 10,000-item search under 100 ms.

## CI (GitHub Actions)

`ci/github-workflow-build.yml` runs the core tests on Ubuntu, then builds, tests and publishes on `windows-latest`, uploading the exe as an artifact. To enable it, move the file to `.github/workflows/build.yml` and push to GitHub.

## Settings

`%LOCALAPPDATA%\ClipboardManager\settings.json` (tray → *Edit settings*). Restart the app after editing.

```json
{
  "QuickPasteHotkey": "Ctrl+Shift+V",
  "MaxItems": 5000,
  "NeverStorePasswords": false,
  "NeverStorePrivateKeys": true,
  "ExcludedApplications": ["1Password", "KeePass", "KeePassXC", "Bitwarden", "LastPass"],
  "RetentionMinutes": { "Sensitive": 5, "password": 1, "Text": 1440, "Code": 10080, "Url": 10080, "Image": 60 }
}
```

A retention of `0` means "never expire". Errors are logged to `error.log` in the same folder.

## Architecture

```
src/
  ClipboardManager.Core/        net8.0 — platform-independent, zero NuGet dependencies
    Classification/             ContentClassifier, SensitiveDataDetector
    Storage/                    ClipboardRepository (SQLite + FTS5), Sqlite/ (P/Invoke wrapper)
    Search/                     SearchQuery (filter syntax parser)
    Services/                   ClipboardService, ExpirationPolicy, MergeService, AppSettings
    Platform/                   IClipboardMonitor, IClipboardWriter, IHotkeyService, IPasteSimulator
  ClipboardManager.App/         net8.0-windows — WPF UI + Win32 adapters
    Platform/                   AddClipboardFormatListener, RegisterHotKey, SendInput, Run-key startup
    UI/                         QuickPasteWindow, TrayIcon
tests/ClipboardManager.Core.Tests/   self-contained test runner (no xUnit)
```

**Cross-platform later:** Core already runs on Linux and macOS. A macOS/Linux version would replace only `ClipboardManager.App`, for example with an Avalonia UI plus platform adapters that implement the `Platform/` interfaces.

## Roadmap

- **Phase 2:** settings UI, workspaces UI, tags, history window, merge separators, per-app rules
- **Phase 3:** smart rules, database encryption, OCR, optional AI (opt-in, never for sensitive items)
- **Phase 4:** sync with end-to-end encryption, macOS/Linux
