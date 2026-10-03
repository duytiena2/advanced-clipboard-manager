# Quy ước Icon và Giao diện trong Dự án Clipboard Manager

Tài liệu này ghi lại các quy chuẩn icon dùng chung trong ứng dụng Clipboard Manager, nhằm đảm bảo tính đồng bộ, nhất quán (Fluent Design / Windows 11) trên toàn bộ hệ thống giao diện.

---

## 1. Font Icon chuẩn của hệ thống

Ứng dụng sử dụng font biểu tượng hệ thống chuẩn của Windows:
```xml
FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets"
```
Font này có sẵn trên Windows 10 và Windows 11, đảm bảo hiển thị sắc nét ở mọi độ phân giải DPI, không bị vỡ hình và tải tức thì.

---

## 2. Bảng mã Glyph Icons điều hướng Settings

Dưới đây là các mã glyph được sử dụng thống nhất cho các danh mục cài đặt trong `SettingsWindow`:

| Danh mục | Unicode / Glyph XAML | Ý nghĩa | Sử dụng tương ứng trong dự án |
| :--- | :--- | :--- | :--- |
| **General** (Cài đặt chung) | `&#xE713;` (`\uE713`) | Bánh răng cài đặt (Settings / Cog) | Nút mở Settings trên Quick Paste (`SettingsGlyph`) |
| **Privacy** (Quyền riêng tư & bảo mật) | `&#xEA18;` (`\uEA18`) / `&#xE72E;` (`\uE72E`) | Chiếc khiên bảo vệ (Shield / Privacy) / Khóa mật mã | Nhãn và biểu tượng mục nhạy cảm (`ContentKind.Sensitive`) |
| **Retention** (Thời gian lưu trữ) | `&#xE81C;` (`\uE81C`) | Lịch sử / Đồng hồ quay lui (History) | Icon lịch sử clipboard trong `QuickPasteWindow` |
| **Workspaces** (Không gian làm việc) | `&#xE8B7;` (`\uE8B7`) | Cặp tài liệu / Dự án (Folder / Workspaces) | Icon workspace filter `WorkspaceButtonText` trong Quick Paste |
| **Snippets** (Mẫu văn bản) | `&#xE8C8;` (`\uE8C8`) | Mảnh tài liệu / Đoạn văn bản (Snippet) | Định nghĩa loại snippet `ContentKind.Snippet` trong `ItemViewModel.cs` |

---

## 3. Bảng mã Glyph Icons cho các nút thao tác (Action Buttons)

| Nút bấm | Unicode / Glyph XAML | Ý nghĩa | Vị trí áp dụng |
| :--- | :--- | :--- | :--- |
| **Add / New** | `&#xE710;` (`\uE710`) | Dấu cộng (+) | `AddRuleButton`, `NewSnippetButton` |
| **Edit** | `&#xE70F;` (`\uE70F`) | Chiếc bút chì (Pencil) | `EditSnippetButton` |
| **Remove / Delete** | `&#xE74D;` (`\uE74D`) | Thùng rác (Delete / Trash) | `RemoveRuleButton`, `DeleteSnippetButton` |
| **Save** | `&#xE74E;` (`\uE74E`) | Đĩa lưu (Save) | `SaveButton` |
| **Info / Hint** | `&#xE946;` (`\uE946`) | Biểu tượng thông tin (Info) | Các hộp gợi ý và giải thích |

---

## 4. Bảng mã Glyph cho các loại dữ liệu Clipboard (ItemViewModel.cs)

Được định nghĩa trong `ClipboardManager.App/UI/ItemViewModel.cs`:
- Hình ảnh (`Image`): `\uEB9F`
- Mã nguồn (`Code` / `SQL` / `Shell`): `\uE71D`, `\uE756`, `\uE943`
- Đường dẫn (`URL`): `\uE71B`
- Nhạy cảm / Mật khẩu (`Sensitive`): `\uE72E`
- Email: `\uE715`
- Số điện thoại (`Phone`): `\uE717`
- Số liệu (`Number`): `\uE8EF`
- Tập tin (`Files`): `\uE8B7`
- Snippet: `\uE8C8`

---

*Tài liệu này được lưu trữ để mọi tính năng hoặc cửa sổ mới (Settings, Dialogs, Context Menu) tiếp tục tuân thủ theo cùng một ngôn ngữ thiết kế.*
