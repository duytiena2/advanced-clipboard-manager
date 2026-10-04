# Advanced Clipboard Manager

[English](README.md) • [Tiếng Việt](README.vi.md) • [简体中文](README.zh.md) • [日本語](README.ja.md) • [한국어](README.ko.md) • [Español](README.es.md) • [Français](README.fr.md) • [Deutsch](README.de.md) • [Русский](README.ru.md) • [Português](README.pt.md)

ローカルファースト (Local-first)、キーボード優先 (Keyboard-first) で設計された Windows 10/11 および macOS 向けの高性能クリップボード管理ツールです。コピー履歴を自動保存し、種類ごとに自動分類。**Ctrl+Shift+V**（macOS ではメニューバー対応）で瞬時に履歴を検索・貼り付けできます。

> ステータス：**フェーズ 1〜3 完了、フェーズ 4 (macOS) 進行中**（ロードマップ参照）。

<p align="center">
  <img src="docs/assets/screenshot.png" alt="Advanced Clipboard Manager UI and Settings" width="850" />
</p>

## 主な機能

| 機能 | 説明 |
|---|---|
| クリップボード履歴 | すべてのコピー履歴を新しい順に自動保存。テキスト、画像、ファイルをサポート。重複コピーは回数カウント付きで1つの項目に統合されます。 |
| クイックペースト (Quick Paste) | **Ctrl+Shift+V** で検索パレットを表示。矢印キーで移動し、**Enter** を押すと直前に使用していたアプリへ瞬時に貼り付けます。 |
| 自動コンテンツ分類 | ローカルルールと信頼度スコアにより、SQL、JSON、XML、YAML、シェル、コード、ログ、URL (GitHub 等)、メール、電話番号、数値、IP アドレス、Markdown を自動判別します。 |
| コンテンツ連動プレビュー | 内容に応じてプレビュー画面が自動最適化：SQL、JSON、XML、YAML、各種コードのシンタックスハイライト、解像度表示（`PNG · 1103 × 593`）およびオフライン OCR 文字認識、URL カードによるリンク詳細とブラウザ起動、機密データの安全なマスク表示と **Ctrl+R** による解除。 |
| スマート検索 | SQLite FTS5 による高速プレフィックス検索、ベトナム語の声調なし曖昧検索、豊富なフィルタ構文：`type:sql`、`type:snippet`、`type:image`、`pinned:true`、`after:2026-09-01`、`sensitive:true` など。 |
| 書式の保持 | HTML/RTF の装飾スタイルを保持し、**Enter** で書式付きペースト。**Ctrl+Shift+Enter** でプレーンテキストとして貼り付け（画像の場合は OCR 検出テキストを貼り付け）。 |
| 数字キーによる即時ペースト | 最初の 9 行に番号が割り振られ、**Ctrl+1…9** で直接ペースト（`Shift` を加えるとプレーンテキスト）。 |
| テキスト変換 (Transforms) | **Ctrl+K**（または右クリック）で貼り付け前に変換：大文字/小文字/タイトルケース/文頭大文字、前後の空白削除、改行の結合、空行削除、JSON 整形/圧縮、SQL 整形、Base64 および URL のエンコード/デコード。 |
| 連続ペーストスタック (Paste stack) | **Ctrl+Space** で複数の項目を順番に選択し、**Ctrl+S** でスタックを開始。任意のアプリで **Ctrl+V** を押すごとに順番に次の項目が貼り付けられます（フォーム入力やデータ移行に最適）。 |
| スニペットと定型文 (Snippets) | 期限切れにならないテキストを登録：**Ctrl+N** で選択中の内容をスニペット化、**Ctrl+E** で編集、設定画面で一元管理。動的変数に対応：`{date}`、`{time}`、`{datetime}`、`{date:yyyy-MM-dd}`、`{clipboard}`、`{uuid}`。 |
| オフライン OCR 画像文字認識 | コピーした画像は Windows 内蔵のオフライン OCR（Windows 10/11）によって自動認識され、画像内のテキストで検索したり、テキストとしてペースト可能。 |
| サイドバー固定ドッキング | **Ctrl+D** でパレットを画面の左右端に常駐ドッキング（AppBar 方式、タスクバーのように作業領域を確保）。 |
| ローカル暗号化 | Windows アカウント（DPAPI）による暗号化に対応（パスワード入力不要）。テキスト、書式、OCR、画像がディスク上で暗号化され、検索インデックスはメモリ上でのみ動作します。 |
| ピン留め (Pin) | **Ctrl+P** でお気に入り登録。ピン留めされた項目は期限切れにならず、常にリストの先頭に表示されます。 |
| 自動期限切れ管理 | データ種類ごとに保持期間を設定可能：機密情報 5分、パスワード 1分、テキスト 1日、コード/URL 7日、画像 1時間（設定で自由に変更可能）。 |
| プライバシー保護 | データはすべてローカルの `%LOCALAPPDATA%\ClipboardManager` (Windows) または `~/Library/Application Support/ClipboardManager` (macOS) にのみ保存。パスワードマネージャーの記録防止フラグを完全尊重し、外部通信は一切行いません。 |

### ショートカットキー一覧

| キー | 動作 |
|---|---|
| `↑` `↓` `PgUp` `PgDn` | 履歴リストの移動 |
| `Enter` | 貼り付け（複数選択時は改行で結合して貼り付け） |
| `Ctrl+Shift+Enter` | プレーンテキストとして貼り付け |
| `Ctrl+1` … `Ctrl+9` | 1〜9番目の項目を直接貼り付け（`Shift` 併用でプレーンテキスト） |
| `Ctrl+K` / 右クリック | テキスト変換メニューを表示して貼り付け |
| `Ctrl+C` | 貼り付けずにクリップボードへコピー |
| プレビュー内ドラッグ | テキストの一部を選択（`Ctrl+C` でコピー、`Enter` で貼り付け） |
| `Ctrl+P` | ピン留め / ピン留め解除 |
| `Ctrl+Space` | 複数選択（選択順序を保持） |
| `Ctrl+S` | 選択項目で連続ペーストスタックを開始 |
| `Ctrl+N` / `Ctrl+E` | スニペットとして保存 / スニペットを編集 |
| `Ctrl+R` | マスクされた機密データを表示 |
| `Ctrl+T` | ウィンドウのピン留め（最前面固定） |
| `Ctrl+D` | サイドバーにドッキング：右 → 左 → オフ |
| `Ctrl+L` | 分割比率の切り替え：25/75, 30/70, 40/60, 50/50 |
| `Ctrl+M` | コンパクトウィジェット / 通常ウィンドウの切り替え |
| `Ctrl+Shift+T` | アクリルすりガラス透明効果の切り替え |
| `Ctrl+,` | 設定ウィンドウを開く |
| `F1` | ショートカットキー一覧ヘルプの表示 |
| `Del` | 選択中の履歴を削除（検索ボックス末尾時） |
| `Esc` | ウィンドウを閉じる |

## ビルドと実行 (Windows)

必要環境：Windows 10/11 x64 および [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`winget install Microsoft.DotNet.SDK.8`)。

```powershell
.\build.ps1            # ビルド + テスト実行
.\build.ps1 -Run       # ビルドして実行 (Ctrl+Shift+V で起動)
.\build.ps1 -Publish   # .\publish\ に単一ファイル exe を出力
.\build.ps1 -Installer # .\dist\ に Setup.exe インストーラーを生成
.\build.ps1 -Msix      # .\dist\ に Microsoft Store パッケージ (.msix) を生成
```

## ライセンス

[MIT](LICENSE) © [duytiena2](https://github.com/duytiena2)
