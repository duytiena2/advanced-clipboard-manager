# Advanced Clipboard Manager

[English](README.md) • [Tiếng Việt](README.vi.md) • [简体中文](README.zh.md) • [日本語](README.ja.md) • [한국어](README.ko.md) • [Español](README.es.md) • [Français](README.fr.md) • [Deutsch](README.de.md) • [Русский](README.ru.md) • [Português](README.pt.md)

Trình quản lý clipboard ưu tiên bảo mật cục bộ (**local-first**) và tối ưu thao tác nhanh qua bàn phím (**keyboard-first**) dành cho Windows 10/11 và macOS. Tự động lưu lịch sử sao chép, phân loại thông minh theo loại nội dung và tìm lại mọi thứ tức thì với **Ctrl+Shift+V** (hoặc menu bar trên macOS).

> Trạng thái: **Đã hoàn thành Phase 1–3, Phase 4 (macOS) đang triển khai** (xem Lộ trình phát triển). Tên gọi là tiêu đề dự kiến.

<p align="center">
  <img src="docs/assets/screenshot.png" alt="Giao diện Advanced Clipboard Manager và Cài đặt" width="850" />
</p>

## Tính năng nổi bật

| Tính năng | Mô tả |
|---|---|
| Lịch sử clipboard | Tự động lưu mọi nội dung sao chép, mục mới nhất nằm ở trên. Hỗ trợ văn bản (text), hình ảnh và tệp tin. Sao chép trùng lặp sẽ được gộp lại kèm số lần copy. |
| Mở nhanh (Quick Paste) | **Ctrl+Shift+V** mở bảng tìm kiếm nhanh. Phím mũi tên điều hướng, phím **Enter** dán ngay vào ứng dụng đang mở trước đó. |
| Tự động phân loại | Bộ quy tắc chạy cục bộ kèm điểm độ tin cậy giúp nhận diện: SQL, JSON, XML, YAML, shell, mã nguồn, nhật ký (log), URL (GitHub…), email, số điện thoại, số, địa chỉ IP và markdown. |
| Xem trước thông minh | Tự động thay đổi giao diện theo định dạng nội dung: tô màu cú pháp (syntax highlighting) cho SQL, JSON, XML, YAML và mã nguồn; xem ảnh với kích thước/định dạng (`PNG · 1103 × 593`) kèm trình đọc chữ OCR; thẻ URL trực quan hỗ trợ mở trình duyệt và phân tích chi tiết link; giao diện bảo vệ chuyên biệt cho dữ liệu nhạy cảm kèm phím bấm **Ctrl+R** để mở khóa. |
| Tìm kiếm thông minh | Tích hợp SQLite FTS5 tìm kiếm tiền tố (prefix matching), **hỗ trợ tiếng Việt không dấu** (`chao` tìm ra `chào`, `don` tìm ra `đơn`), kèm bộ lọc linh hoạt: `type:sql`, `type:snippet`, `type:image`, `pinned:true`, `after:2026-09-01`, `before:…`, `sensitive:true`. |
| Giữ nguyên định dạng | Giữ nguyên định dạng HTML/RTF khi copy, nhấn **Enter** dán nguyên định dạng. **Ctrl+Shift+Enter** dán văn bản thuần plain text (với hình ảnh: dán đoạn chữ nhận diện được từ ảnh). |
| Dán theo số thứ tự | 9 dòng đầu tiên được đánh số: **Ctrl+1…9** dán trực tiếp mục tương ứng (**Ctrl+Shift+1…9** dán văn bản thuần). |
| Biến đổi văn bản (Transforms) | **Ctrl+K** (hoặc chuột phải) chuyển đổi nội dung trước khi dán: chữ HOA/thường/Title/Sentence case, xóa khoảng trắng thừa, gộp dòng, xóa dòng trống, format/minify JSON, định dạng SQL, mã hóa & giải mã Base64 / URL. |
| Ngăn xếp dán liên tiếp (Paste stack) | Đánh dấu các mục bằng **Ctrl+Space** theo thứ tự bạn cần, bấm **Ctrl+S**, sau đó mỗi lần bấm **Ctrl+V** trong bất kỳ ứng dụng nào sẽ dán mục kế tiếp — cực kỳ tiện khi điền form hoặc nhập liệu. Tự động dừng khi dán hết, khi copy nội dung mới hoặc bấm dừng từ khay hệ thống. |
| Đoạn mẫu & template (Snippets) | Lưu trữ các đoạn văn bản tái sử dụng không bao giờ hết hạn: **Ctrl+N** lưu nội dung đang chọn làm snippet, **Ctrl+E** chỉnh sửa snippet, hoặc quản lý trong Cài đặt. Hỗ trợ biến động: `{date}`, `{time}`, `{datetime}`, `{date:dd/MM/yyyy}`, `{clipboard}`, `{uuid}`. |
| OCR nhận diện chữ trong ảnh | Hình ảnh sao chép được quét chữ tự động bằng công cụ OCR offline của Windows (Windows 10/11), giúp tìm kiếm ảnh chụp màn hình bằng nội dung chữ bên trong và dán ảnh dưới dạng văn bản. Dùng ngôn ngữ OCR cài trong Windows (thêm Tiếng Việt tại *Settings › Time & language › Language*). |
| Gắn cố định cạnh màn hình (Sidebar) | **Ctrl+D** gắn bảng Quick Paste cố định vào mép phải hoặc trái màn hình dạng AppBar (Windows, chiếm không gian cố định giống thanh taskbar). Cửa sổ luôn hiển thị và dán vào ứng dụng bạn dùng gần nhất. |
| Mã hóa dữ liệu | Tùy chọn: mã hóa toàn bộ lịch sử bằng tài khoản Windows (DPAPI), không cần nhập mật khẩu. Văn bản, định dạng, chữ OCR và ảnh được mã hóa trên ổ đĩa; chỉ mục tìm kiếm chỉ lưu trong bộ nhớ RAM; phát hiện trùng lặp bằng keyed hash. |
| Ghim mục quan trọng (Pin) | **Ctrl+P**. Các mục đã ghim không bao giờ bị xóa tự động và luôn xuất hiện ở đầu danh sách. |
| Tự động xóa theo thời gian | Cấu hình thời gian lưu trữ riêng cho từng loại: dữ liệu nhạy cảm (secrets) 5 phút, mật khẩu 1 phút, văn bản 1 ngày, code/URL 7 ngày, hình ảnh 1 giờ. Có thể tùy chỉnh trong cài đặt. |
| Bảo vệ dữ liệu nhạy cảm | Tự động phát hiện API key, token, JWT, AWS key, private key, connection string và mật khẩu; che dấu trong danh sách và không ghi vào chỉ mục tìm kiếm. Nội dung được ẩn ở khung xem trước cho đến khi bấm **Ctrl+R**, và tự động xóa khỏi clipboard hệ thống khi hết hạn. Riêng Private key không bao giờ bị lưu vào ổ đĩa. |
| Quyền riêng tư & bảo mật | Toàn bộ dữ liệu lưu trữ cục bộ tại `%LOCALAPPDATA%\ClipboardManager` (Windows) hoặc `~/Library/Application Support/ClipboardManager` (macOS). Tôn trọng cờ "không ghi lại" (don't record me) từ các ứng dụng quản lý mật khẩu (1Password, KeePass, Bitwarden…). Ứng dụng hoàn toàn không gửi bất kỳ dữ liệu nào qua mạng. |
| Chọn nhiều & gộp dòng | **Ctrl+Space** để đánh dấu nhiều mục. Nhấn **Enter** dán tất cả các mục đã chọn gộp lại cùng lúc (mỗi mục một dòng). |
| Khay hệ thống (System Tray) | Tạm dừng sao chép, xóa lịch sử (giữ lại mục ghim và snippet), dừng paste stack, mở cài đặt, kiểm tra cập nhật, khởi động cùng Windows, mở thư mục dữ liệu. |

### Bảng phím tắt Quick Paste

| Phím tắt | Thao tác |
|---|---|
| `↑` `↓` `PgUp` `PgDn` | Di chuyển điều hướng |
| `Enter` | Dán mục được chọn (nếu chọn nhiều mục sẽ gộp lại để dán) |
| `Ctrl+Shift+Enter` | Dán dưới dạng văn bản thuần (plain text) |
| `Ctrl+1` … `Ctrl+9` | Dán nhanh mục số 1…9 (kèm `Shift` để dán plain text) |
| `Ctrl+K` / Chuột phải | Mở menu biến đổi văn bản trước khi dán |
| `Ctrl+C` | Copy vào clipboard mà không thực hiện dán (hoặc copy đoạn bôi đen trong khung xem trước) |
| Click / bôi đen trong khung xem trước | Chọn văn bản một phần (`Ctrl+C` để copy, `Enter` để dán vùng chọn, menu chuột phải) |
| `Ctrl+P` | Ghim / bỏ ghim mục |
| `Ctrl+Space` | Đánh dấu chọn nhiều mục (giữ đúng thứ tự đánh dấu) |
| `Ctrl+S` | Bắt đầu chuỗi dán liên tiếp (paste stack) với các mục đã đánh dấu |
| `Ctrl+N` / `Ctrl+E` | Lưu thành snippet mới / chỉnh sửa snippet đang chọn |
| `Ctrl+R` | Hiển thị nội dung nhạy cảm đang bị che |
| `Ctrl+T` | Ghim cửa sổ palette luôn nổi trên màn hình và dán vào ứng dụng vừa kích hoạt |
| `Ctrl+D` | Gắn cửa sổ làm sidebar: phải → trái → tắt |
| `Ctrl+L` | Chuyển đổi tỷ lệ chia: 25/75, 30/70, 40/60, 50/50 |
| `Ctrl+M` | Chuyển đổi chế độ Widget thu nhỏ / cửa sổ mở rộng |
| `Ctrl+Shift+T` | Bật / tắt hiệu ứng kính mờ trong suốt (Acrylic glass) |
| `Ctrl+,` | Mở cửa sổ Cài đặt (Settings) |
| `F1` | Xem danh sách hướng dẫn toàn bộ phím tắt |
| `Del` | Xóa mục khỏi lịch sử (khi con trỏ nằm ở cuối ô tìm kiếm) |
| `Esc` | Đóng cửa sổ |

## Build & Chạy (Windows)

Yêu cầu môi trường: Windows 10/11 x64 và [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`winget install Microsoft.DotNet.SDK.8`).

```powershell
.\build.ps1            # Build + chạy toàn bộ bài test
.\build.ps1 -Run       # Build và khởi chạy ngay (icon khay hệ thống; bấm Ctrl+Shift+V)
.\build.ps1 -Publish   # Tạo file exe portable độc lập tại .\publish\
.\build.ps1 -Installer # Tạo bộ cài Setup.exe tại .\dist\ (tự cài Inno Setup qua winget nếu chưa có)
.\build.ps1 -Msix      # Tạo gói Microsoft Store (.msix) tại .\dist\
```

`build.ps1` sẽ tự động tải file `sqlite3.dll` chính thức (tích hợp FTS5) vào thư mục `lib\`. Nếu quá trình tải gặp sự cố, ứng dụng sẽ tự động chuyển sang sử dụng `winsqlite3.dll` có sẵn của Windows. Nếu bản SQLite đó thiếu FTS5, ứng dụng sẽ tìm kiếm khớp từ trong bộ nhớ RAM.

Bạn cũng có thể mở trực tiếp `AdvancedClipboardManager.sln` trong Visual Studio 2022 và nhấn **F5** với dự án khởi động là `ClipboardManager.App`.

## Các kênh phân phối

| Kênh | Tệp | Ghi chú |
|---|---|---|
| Microsoft Store (chính) | `dist\AdvancedClipboardManager_<ver>.0_x64.msix` | Được Store ký số. Cài đặt, tự cập nhật và gỡ cài đặt do Windows quản lý. "Khởi động cùng Windows" dùng StartupTask của gói. Dữ liệu lưu trong thư mục app package và tự xóa khi gỡ cài đặt. |
| Tải trực tiếp (Windows) | `dist\AdvancedClipboardManager-Setup-<ver>.exe` | Cài đặt theo user, không cần quyền admin UAC, tạo shortcut Start Menu, tùy chọn khởi động cùng Windows, gỡ cài đặt tại Settings > Apps. |
| Portable (Windows) | `publish\ClipboardManager.exe` | Chạy trực tiếp không cần cài đặt. |
| macOS (Apple Silicon) | `dist/AdvancedClipboardManager-osx-arm64.dmg` / `.zip` | Bộ cài file đĩa (.dmg) và gói ứng dụng `.app` độc lập dành cho chip Apple Silicon (M1/M2/M3/M4). |
| macOS (Intel) | `dist/AdvancedClipboardManager-osx-x64.dmg` / `.zip` | Bộ cài file đĩa (.dmg) và gói ứng dụng `.app` độc lập dành cho máy Mac Intel. |

**Phát hành lên Microsoft Store:**

1. Trên [Partner Center](https://partner.microsoft.com/dashboard), đặt tên ứng dụng và mở mục *Product identity*.
2. Sao chép `Package/Identity/Name`, `Package/Identity/Publisher` và `Package/Properties/PublisherDisplayName` vào các biến repository GitHub: `MSIX_IDENTITY_NAME`, `MSIX_PUBLISHER` và `MSIX_PUBLISHER_DISPLAY_NAME`. Bạn cũng có thể truyền trực tiếp qua `-MsixIdentityName`, `-MsixPublisher` và `-MsixPublisherDisplayName` khi chạy `build.ps1 -Msix`.
3. Build gói `.msix` (artifact CI `ClipboardManager-msix`), sau đó tải lên bản nộp mới. Store yêu cầu phải có URL chính sách quyền riêng tư (Privacy Policy) vì ứng dụng đọc clipboard.
4. Tăng số `<Version>` trong `ClipboardManager.App.csproj` trước mỗi lần nộp bản mới.

Để kiểm tra gói MSIX nội bộ, bật chế độ Developer Mode trong Windows, chạy `.\build.ps1 -Msix`, rồi chạy `Add-AppxPackage -Register .\publish\msix\AppxManifest.xml`. Logo và icon `.ico` được sinh bởi `packaging\generate-assets.ps1`.

**Phát hành bản phát hành GitHub Release:**

Khi đẩy một git tag phiên bản, GitHub Actions sẽ tự động kích hoạt build, chạy test và tạo bản GitHub Release kèm bộ cài Windows, file portable, gói MSIX cùng các bộ cài macOS (DMG / zip):

```powershell
git tag v0.2.0
git push origin v0.2.0
```

Ứng dụng tự động kiểm tra bản phát hành mới khi khởi động và qua menu khay hệ thống (**Check for updates…**), thông báo ngay khi có bản cập nhật mới.

## Kiểm thử (Tests)

Phần logic cốt lõi (Core) hoàn toàn không phụ thuộc vào Windows API, do đó bộ kiểm thử có thể chạy mượt mà trên cả Windows, Linux và macOS:

```bash
dotnet run --project tests/ClipboardManager.Core.Tests            # chạy toàn bộ test
dotnet run --project tests/ClipboardManager.Core.Tests -- Search  # lọc test theo tên
```

Bộ test bao gồm 102 ca kiểm thử bao phủ: phân loại nội dung, phát hiện dữ liệu nhạy cảm, chống trùng lặp, tìm kiếm FTS5 và bộ lọc, hết hạn, ghim mục, loại bỏ dữ liệu cũ (eviction), lưu trữ ảnh, gộp dòng, cài đặt, văn bản đa định dạng, các phép biến đổi (kèm bộ định dạng SQL), mã hóa (không lưu văn bản thô trên đĩa, bật/tắt mã hóa tại chỗ, phát hiện sai tài khoản), snippet & template, ngăn xếp dán, chỉ mục OCR, nâng cấp phiên bản CSDL và tìm kiếm 10.000 mục dưới 100 ms.

## Tích hợp liên tục CI (GitHub Actions)

Workflow `.github/workflows/build.yml` thực thi chạy kiểm thử core trên Ubuntu, tiến hành build/test/đóng gói trên `windows-latest` (file portable exe, Setup.exe, và MSIX), đồng thời đóng gói bộ cài macOS `.dmg` và `.zip` trên `macos-latest` (arm64 & x64). Khi có tag phiên bản, toàn bộ tài nguyên sẽ được gắn trực tiếp vào GitHub Release.

## Cài đặt (Settings)

Menu khay hệ thống → **Settings…** cho phép cấu hình toàn diện: phím tắt (áp dụng tức thì), tính năng ghi nhận clipboard, quy tắc quyền riêng tư, danh sách ứng dụng loại trừ, mã hóa, thời gian lưu trữ theo loại, OCR và danh sách snippets.

Ở tầng dưới, cấu hình được lưu trong file `settings.json` tại thư mục dữ liệu (`%LOCALAPPDATA%\ClipboardManager` trên Windows, `~/Library/Application Support/ClipboardManager` trên macOS; từ khay hệ thống → *Edit settings.json (advanced)*; cần khởi động lại ứng dụng nếu sửa file thủ công):

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

Giá trị lưu trữ bằng `0` nghĩa là "không bao giờ hết hạn". Các thông báo lỗi được ghi lại tại file `error.log` trong cùng thư mục.

Lịch sử đã mã hóa chỉ có thể mở được bởi chính tài khoản Windows đó trên cùng máy tính. Nếu bạn chuyển sang máy mới, hãy tắt tính năng mã hóa trước, hoặc bắt đầu một lịch sử mới trên máy đó.

## Cấu trúc kiến trúc (Architecture)

```
src/
  ClipboardManager.Core/        net8.0 — hoàn toàn độc lập nền tảng, không phụ thuộc gói NuGet ngoài
    Classification/             ContentClassifier, SensitiveDataDetector
    Storage/                    ClipboardRepository (SQLite + FTS5), Sqlite/ (wrapper P/Invoke)
    Search/                     SearchQuery (bộ phân tích cú pháp tìm kiếm và bộ lọc)
    Services/                   ClipboardService, ExpirationPolicy, MergeService, AppSettings,
                                TextTransforms + SqlFormatter, TemplateEngine, PasteStack
    Platform/                   IClipboardMonitor, IClipboardWriter, IHotkeyService, IPasteSimulator, IDataProtector, IOcrEngine
  ClipboardManager.App/         net8.0-windows10.0.19041.0 — Giao diện WPF Windows + bộ điều hợp Win32
  ClipboardManager.Mac/         net8.0 — Giao diện Avalonia macOS (bảng tìm kiếm Spotlight/Raycast + menu bar)
tests/ClipboardManager.Core.Tests/   Bộ chạy kiểm thử tự chứa (không cần xUnit)
packaging/
  Assets/                       Logo và icon app.ico
  mac/                          Info.plist, build-mac.sh
  AppxManifest.xml              Manifest gói Microsoft Store
  setup.iss                     Script bộ cài Inno Setup
  generate-assets.ps1           Tự động sinh các biến thể logo và icon
```

## Dành cho macOS

Yêu cầu môi trường: macOS 11+ (Apple Silicon hoặc Intel) và [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
# Chạy trực tiếp trên macOS
dotnet run --project src/ClipboardManager.Mac

# Build gói ứng dụng .app và bộ cài đĩa .dmg
./packaging/mac/build-mac.sh osx-arm64 0.1.0   # Apple Silicon (M1/M2/M3/M4)
./packaging/mac/build-mac.sh osx-x64 0.1.0     # Intel Mac
```

Phiên bản macOS chạy thường trực trên thanh Menu bar với cửa sổ tìm kiếm nổi dạng Raycast / Spotlight.

## Lộ trình phát triển (Roadmap)

- **Phase 2:** ~~giao diện settings~~, ~~snippets~~, ~~paste stack~~, ~~sidebar~~ (đã xong); các mục tiếp theo: gắn thẻ (tags), cửa sổ xem lại toàn bộ lịch sử, tùy chọn ký tự phân cách khi gộp dòng trên UI.
- **Phase 3:** ~~mã hóa cơ sở dữ liệu~~, ~~OCR hình ảnh~~ (đã xong); các mục tiếp theo: quy tắc thông minh (smart rules), AI tùy chọn (opt-in, tuyệt đối không dùng cho dữ liệu nhạy cảm).
- **Phase 4:** ~~hỗ trợ macOS~~ (đã xong); các mục tiếp theo: đồng bộ đa thiết bị mã hóa đầu cuối (E2EE), hỗ trợ Linux.

## Giấy phép (License)

[MIT](LICENSE) © [duytiena2](https://github.com/duytiena2)
