# Advanced Clipboard Manager

[English](README.md) • [Tiếng Việt](README.vi.md) • [简体中文](README.zh.md) • [日本語](README.ja.md) • [한국어](README.ko.md) • [Español](README.es.md) • [Français](README.fr.md) • [Deutsch](README.de.md) • [Русский](README.ru.md) • [Português](README.pt.md)

Ein lokaler (**local-first**) und tastaturoptimierter (**keyboard-first**) Zwischenablage-Manager für Windows 10/11 und macOS. Speichert Ihren Kopierverlauf automatisch, kategorisiert Inhalte intelligent und findet alles blitzschnell mit **Ctrl+Shift+V** (oder Menüleiste auf macOS).

> Status: **Phasen 1–3 abgeschlossen, Phase 4 (macOS) in Arbeit** (siehe Roadmap).

<p align="center">
  <img src="docs/assets/screenshot.png" alt="Advanced Clipboard Manager UI and Settings" width="850" />
</p>

## Funktionen

| Funktion | Beschreibung |
|---|---|
| Zwischenablage-Verlauf | Speichert jeden Kopiervorgang chronologisch. Unterstützt formatierten Text, Bilder und Dateien. Mehrfaches Kopieren wird zusammengefasst. |
| Schnelles Einfügen (Quick Paste) | **Ctrl+Shift+V** öffnet das Suchfenster. Pfeiltasten zur Navigation, **Enter** fügt direkt in die zuvor aktive Anwendung ein. |
| Automatische Klassifizierung | Lokale Regeln erkennen: SQL, JSON, XML, YAML, Shell-Skripte, Quellcode, Protokolle, URLs (GitHub...), E-Mails, Telefonnummern, IP-Adressen und Markdown. |
| Dynamische Vorschau | Passt sich an den Inhalt an: Syntax-Highlighting für Code/SQL/JSON; Bildbetrachter mit Auflösung (`PNG · 1103 × 593`) und Offline-OCR-Texterkennung; URL-Karten; sensibles Ausblenden von Passwörtern/Schlüsseln mit Freigabe über **Ctrl+R**. |
| Blitzschnelle Suche | SQLite FTS5-Volltextsuche mit Präfixabgleich und Filtern: `type:sql`, `type:snippet`, `type:image`, `pinned:true`, `sensitive:true` etc. |
| Formatierungsoptionen | Behält HTML/RTF-Stile bei. **Enter** fügt formatiert ein, **Ctrl+Shift+Enter** als reinen Nur-Text (bei Bildern: den erkannten OCR-Text). |
| Schnelleinfügen nach Nummer | Die ersten 9 Zeilen sind nummeriert: **Ctrl+1…9** fügt direkt ein (`Shift` für Nur-Text). |
| Texttransformationen | **Ctrl+K** (oder Rechtsklick) transformiert Text vor dem Einfügen: GROSS-/kleinschreibung, Leerzeichen entfernen, JSON/SQL formatieren, Base64/URL en-/dekodieren. |
| Einfügestapel (Paste stack) | Mehrere Einträge mit **Ctrl+Space** markieren, mit **Ctrl+S** starten. Jedes nachfolgende **Ctrl+V** fügt nacheinander den nächsten Eintrag ein. |
| Textbausteine & Vorlagen (Snippets) | Wiederverwendbare Texte: **Ctrl+N** zum Speichern, **Ctrl+E** zum Bearbeiten. Variablen: `{date}`, `{time}`, `{datetime}`, `{date:yyyy-MM-dd}`, `{clipboard}`, `{uuid}`. |
| Offline-OCR für Bilder | Extrahierter Text aus Screenshots wird automatisch durch die Windows-OCR erkannt und durchsuchbar gemacht. |
| Seitenleiste (Sidebar) | **Ctrl+D** dockt das Fenster am Bildschirmrand an (AppBar-Modus). |
| Lokale Verschlüsselung | Optionale Windows DPAPI-Verschlüsselung (benutzerkontenbasiert, kein Passwort erforderlich). |
| Anheften (Pin) | **Ctrl+P** heftet wichtige Einträge oben an. Angeheftete Elemente verfallen nie. |
| Automatischer Ablauf | Einstellbare Aufbewahrungsdauer nach Typ: sensible Daten 5 Min., Passwörter 1 Min., Text 1 Tag, Code/URL 7 Tage, Bilder 1 Std. |
| Datenschutz | Alle Daten bleiben lokal in `%LOCALAPPDATA%\ClipboardManager` (Windows) oder `~/Library/Application Support/ClipboardManager` (macOS). Keine Netzwerkverbindungen. |

### Tastaturkürzel

| Taste | Aktion |
|---|---|
| `↑` `↓` `PgUp` `PgDn` | In der Liste navigieren |
| `Enter` | Einfügen (bei Mehrfachauswahl zusammengefügt) |
| `Ctrl+Shift+Enter` | Als reinen Nur-Text einfügen |
| `Ctrl+1` … `Ctrl+9` | Eintrag 1…9 direkt einfügen (`Shift` für Nur-Text) |
| `Ctrl+K` / Rechtsklick | Text transformieren und einfügen |
| `Ctrl+C` | In Zwischenablage kopieren ohne einzufügen |
| `Ctrl+P` | Anheften / Lösen |
| `Ctrl+Space` | Mehrfachauswahl (Reihenfolge bleibt erhalten) |
| `Ctrl+S` | Einfügestapel mit Auswahl starten |
| `Ctrl+N` / `Ctrl+E` | Als Snippet speichern / Snippet bearbeiten |
| `Ctrl+R` | Maskierten sensiblen Inhalt aufdecken |
| `Ctrl+T` | Fenster oben anheften / im Vordergrund halten |
| `Ctrl+D` | An Seitenleiste andocken: Rechts → Links → Aus |
| `Ctrl+L` | Teilungsverhältnis wechseln: 25/75, 30/70, 40/60, 50/50 |
| `Ctrl+M` | Kompakter Widget-Modus / Vollansicht umschalten |
| `Ctrl+Shift+T` | Acryl-Transparenzeffekt umschalten |
| `Ctrl+,` | Einstellungen öffnen |
| `F1` | Tastenkombinationen-Hilfe anzeigen |
| `Entf` | Eintrag löschen |
| `Esc` | Schließen |

## Kompilieren und Ausführen (Windows)

Voraussetzungen: Windows 10/11 x64 und [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`winget install Microsoft.DotNet.SDK.8`).

```powershell
.\build.ps1            # Kompilieren + Tests ausführen
.\build.ps1 -Run       # Kompilieren und starten (Ctrl+Shift+V)
.\build.ps1 -Publish   # Einzelne ausführbare .exe in .\publish\ erstellen
.\build.ps1 -Installer # Setup.exe in .\dist\ erstellen
.\build.ps1 -Msix      # Microsoft Store Paket (.msix) in .\dist\ erstellen
```

## Lizenz

[MIT](LICENSE) © [duytiena2](https://github.com/duytiena2)
