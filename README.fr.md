# Advanced Clipboard Manager

[English](README.md) • [Tiếng Việt](README.vi.md) • [简体中文](README.zh.md) • [日本語](README.ja.md) • [한국어](README.ko.md) • [Español](README.es.md) • [Français](README.fr.md) • [Deutsch](README.de.md) • [Русский](README.ru.md) • [Português](README.pt.md)

Un gestionnaire de presse-papiers axé sur le stockage local (**local-first**) et optimisé pour le clavier (**keyboard-first**) pour Windows 10/11 et macOS. Il conserve automatiquement votre historique, classe intelligemment chaque copie par type et retrouve instantanément n'importe quel élément avec **Ctrl+Shift+V** (ou via la barre des menus sur macOS).

> Statut : **Phases 1–3 terminées, Phase 4 (macOS) en cours** (voir feuille de route).

<p align="center">
  <img src="docs/assets/screenshot.png" alt="Advanced Clipboard Manager UI and Settings" width="850" />
</p>

## Fonctionnalités

| Fonctionnalité | Description |
|---|---|
| Historique du presse-papiers | Enregistre chaque copie, de la plus récente à la plus ancienne. Prend en charge le texte, les images et les fichiers. Les doublons sont fusionnés avec un compteur de copies. |
| Collage rapide (Quick Paste) | **Ctrl+Shift+V** ouvre la palette de recherche. Touches fléchées pour naviguer, **Entrée** pour coller dans l'application active. |
| Classification automatique | Règles locales intelligentes détectant : SQL, JSON, XML, YAML, scripts shell, code source, logs, URLs (GitHub...), e-mails, téléphones, chiffres, adresses IP et Markdown. |
| Aperçu contextuel dynamique | L'aperçu s'adapte au contenu : coloration syntaxique pour le code/SQL/JSON ; visualiseur d'images avec résolution (`PNG · 1103 × 593`) et extraction de texte par OCR ; carte détaillée pour les URLs ; et masquage sécurisé des données sensibles avec **Ctrl+R** pour afficher. |
| Recherche instantanée | Moteur SQLite FTS5 avec correspondance de préfixes et filtres puissants : `type:sql`, `type:snippet`, `type:image`, `pinned:true`, `sensitive:true`, etc. |
| Conservation de la mise en forme | Conserve le style HTML/RTF lors du collage avec **Entrée**. Utilisez **Ctrl+Shift+Entrée** pour coller en texte brut (ou le texte OCR d'une image). |
| Collage par numéro | Les 9 premières lignes sont numérotées : **Ctrl+1…9** colle directement l'élément (`Shift` pour le texte brut). |
| Transformations de texte | **Ctrl+K** (ou clic droit) pour transformer avant de coller : majuscules/minuscules, suppression des espaces, fusion de lignes, formatage JSON/SQL, encodage/décodage Base64 et URL. |
| Pile de collage séquentielle (Paste stack) | Marquez plusieurs éléments dans l'ordre voulu avec **Ctrl+Espace**, lancez avec **Ctrl+S**. Chaque **Ctrl+V** dans vos applications colle l'élément suivant. |
| Extraits et modèles (Snippets) | Textes réutilisables qui n'expirent jamais : **Ctrl+N** pour créer, **Ctrl+E** pour modifier. Variables dynamiques : `{date}`, `{time}`, `{datetime}`, `{date:yyyy-MM-dd}`, `{clipboard}`, `{uuid}`. |
| OCR hors ligne pour images | Le moteur OCR intégré de Windows analyse automatiquement le texte présent dans les captures d'écran pour permettre la recherche et le collage sous forme de texte. |
| Ancrage latéral (Sidebar) | **Ctrl+D** ancre la palette sur le côté gauche ou droit de l'écran en mode barre d'applications (AppBar). |
| Chiffrement local sécurisé | Chiffrement optionnel via votre compte Windows (DPAPI) sans mot de passe requis. Vos données sont chiffrées sur le disque. |
| Épingler (Pin) | **Ctrl+P** pour épingler vos favoris. Les éléments épinglés n'expirent jamais et restent en haut de liste. |
| Expiration automatique | Durée de conservation paramétrable par type : données sensibles 5 min, mots de passe 1 min, texte 1 jour, code/URL 7 jours, images 1 heure. |
| Confidentialité absolue | Stockage 100% local dans `%LOCALAPPDATA%\ClipboardManager` (Windows) ou `~/Library/Application Support/ClipboardManager` (macOS). Aucune connexion réseau. |

### Raccourcis clavier

| Raccourci | Action |
|---|---|
| `↑` `↓` `PgUp` `PgDn` | Naviguer dans la liste |
| `Entrée` | Coller (fusionne si plusieurs éléments sont sélectionnés) |
| `Ctrl+Shift+Entrée` | Coller en texte brut |
| `Ctrl+1` … `Ctrl+9` | Coller directement l'élément 1…9 (`Shift` pour texte brut) |
| `Ctrl+K` / Clic droit | Ouvrir le menu de transformation |
| `Ctrl+C` | Copier dans le presse-papiers sans coller |
| `Ctrl+P` | Épingler / Désépingler |
| `Ctrl+Espace` | Sélection multiple (conserve l'ordre) |
| `Ctrl+S` | Démarrer la pile de collage séquentielle |
| `Ctrl+N` / `Ctrl+E` | Créer un snippet / Modifier le snippet sélectionné |
| `Ctrl+R` | Révéler les données sensibles masquées |
| `Ctrl+T` | Garder la fenêtre toujours au premier plan |
| `Ctrl+D` | Ancrer en barre latérale : Droite → Gauche → Désactivé |
| `Ctrl+L` | Changer le ratio de division : 25/75, 30/70, 40/60, 50/50 |
| `Ctrl+M` | Basculer en mode widget compact / fenêtre standard |
| `Ctrl+Shift+T` | Activer / Désactiver la transparence acrylique |
| `Ctrl+,` | Ouvrir la fenêtre des Paramètres |
| `F1` | Afficher la liste complète des raccourcis |
| `Suppr` | Supprimer l'élément sélectionné |
| `Échap` | Fermer la palette |

## Compilation et exécution (Windows)

Prérequis : Windows 10/11 x64 et [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`winget install Microsoft.DotNet.SDK.8`).

```powershell
.\build.ps1            # Compiler + lancer les tests
.\build.ps1 -Run       # Compiler et exécuter (Ctrl+Shift+V)
.\build.ps1 -Publish   # Générer un exe autonome dans .\publish\
.\build.ps1 -Installer # Créer l'installeur Setup.exe dans .\dist\
.\build.ps1 -Msix      # Créer le package Microsoft Store (.msix)
```

## Licence

[MIT](LICENSE) © [duytiena2](https://github.com/duytiena2)
