# Microsoft Store Listing Optimization Guide
# Bản Mô Tả & Tối Ưu Hóa Trang Microsoft Store Cho Advanced Clipboard Manager

Tài liệu này cung cấp toàn bộ nội dung đã được tối ưu hóa theo tiêu chuẩn **App Store Optimization (ASO)** và **Product Marketing** chuyên nghiệp, sẵn sàng để copy trực tiếp vào [Microsoft Partner Center](https://partner.microsoft.com/dashboard).

---

## 🌟 TẠI SAO BẢN MÔ TẢ CŨ BỊ ĐÁNH GIÁ KÉM & CÁCH KHẮC PHỤC

1. **Thiếu hướng dẫn kích hoạt (Phím tắt vàng `Ctrl + Shift + V`):**
   - *Lỗi cũ:* Ứng dụng chạy ngầm dưới System Tray. Người dùng cài xong bấm chuột không thấy gì mở ra, tưởng ứng dụng bị hỏng hoặc lừa đảo.
   - *Khắc phục:* Đưa ngay phím tắt `Ctrl + Shift + V` lên dòng đầu tiên của mô tả ngắn và mô tả chi tiết.
2. **Không trả lời được câu hỏi "Tại sao không dùng `Win + V` có sẵn?":**
   - *Lỗi cũ:* Mô tả như một clipboard thông thường từ 10 năm trước, không tạo được động lực tải.
   - *Khắc phục:* Nhấn mạnh sự khác biệt vượt trội: lưu không giới hạn, tìm kiếm tiếng Việt không dấu siêu tốc (FTS5), OCR đọc chữ trong ảnh chụp màn hình, Paste Stack dán liên tiếp, làm đẹp/minify JSON & SQL, che giấu dữ liệu nhạy cảm và bảo mật mã hóa DPAPI phần cứng.
3. **Chưa phân loại giá trị theo nhóm người dùng mục tiêu:**
   - Lập trình viên (Dev), Dân văn phòng / Nhập liệu (Data Entry, Kế toán), Người sáng tạo nội dung (Content Creator).

---

# PHẦN 1: BẢN TIẾNG ANH (STORE GLOBAL - KHUYẾN NGHỊ CHÍNH)

### 1. App Subtitle / Short Description (Tối đa 100 ký tự)
```text
Press Ctrl+Shift+V anywhere. Supercharge your clipboard with OCR, Paste Stack & Smart Search.
```

---

### 2. Full Description (Mô tả chi tiết)

```text
🚀 INSTANT ACCESS: Press [Ctrl + Shift + V] anywhere to summon your smart clipboard!
(You can also click the clipboard icon in your system tray or customize your hotkey in Settings).

Tired of the 25-item limit and clunky experience of Windows Clipboard (Win + V)? 
Meet Advanced Clipboard Manager — the keyboard-first, privacy-focused productivity powerhouse designed for developers, accountants, researchers, and power users on Windows 10 & 11.

Never lose a copied snippet, link, screenshot, or color code again. Everything is indexed locally, instantly searchable, and accessible in milliseconds.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
🔥 WHY IT’S 10X BETTER THAN DEFAULT WINDOWS CLIPBOARD (Win + V):
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

⚡ 1. SMART OCR: SEARCH & EXTRACT TEXT INSIDE IMAGES
Copied an error message from a dialog? A diagram screenshot? A table from an image?
• Advanced Clipboard Manager automatically scans text inside images completely offline using native Windows OCR.
• Search past screenshots by the text inside them!
• Press Shift+Enter to paste extracted text directly without retyping.

📚 2. PASTE STACK (SEQUENTIAL MULTI-PASTE) — THE ULTIMATE TIME-SAVER
Need to copy 5 fields from an email and paste them one by one into an Excel sheet or web form?
• Select your items in order with [Ctrl + Space], then hit [Ctrl + S].
• Switch to your target app and simply press [Ctrl + V] repeatedly. Each paste automatically drops the next item in sequence!

🛠️ 3. INSTANT TEXT TRANSFORMS (Ctrl + K)
Modify your text before pasting without opening an online converter:
• Change Case: UPPERCASE, lowercase, Title Case, Sentence case.
• Clean Up: Trim whitespace, join into one line, remove empty lines.
• Developer Tools: Format & Minify JSON, format SQL queries, Base64 encode/decode, URL encode/decode.

🔍 4. BLISTERING-FAST FULL-TEXT SEARCH & SMART FILTERS
Powered by SQLite FTS5 database engine:
• Instant search across 10,000+ clips in under 100ms.
• Filter with ease: type:code, type:image, type:url, type:snippet, pinned:true, sensitive:true.
• Full Vietnamese non-diacritic accent-insensitive search (e.g. typing "chao" finds "chào").

🔒 5. ENTERPRISE-GRADE PRIVACY & SECRET SHIELD
Your secrets are safe from prying eyes:
• Auto-detects passwords, OpenAI / Stripe / AWS API keys, GitHub tokens, and private keys.
• Sensitive items are automatically masked and hidden until you press [Ctrl + R].
• Private keys are NEVER written to disk.
• Automatic auto-destruction: secrets expire and vanish after 1–5 minutes.
• Optional Windows DPAPI hardware encryption: all data stored locally on your SSD is encrypted using your Windows account. Zero cloud sync, zero tracking, 100% offline.

📌 6. REUSABLE SNIPPETS & DYNAMIC TEMPLATES
Save your daily canned replies, email templates, and code boilerplate forever with [Ctrl + N].
• Supports dynamic placeholders: {date}, {time}, {datetime}, {uuid}, and {clipboard}.

🖥️ 7. DOCKABLE SIDEBAR & FLOATING WIDGET
• Press [Ctrl + D] to snap the clipboard palette to the left or right edge of your screen as an AppBar (like an extra taskbar dock).
• Keep it visible alongside your spreadsheets, IDEs, or browsers for effortless multi-tasking.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
⌨️ ESSENTIAL KEYBOARD SHORTCUTS:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
• Ctrl + Shift + V : Open / Close Quick Paste
• ↑ / ↓ / PgUp / PgDn : Navigate items
• Enter : Paste selected item (or merge selected items)
• Ctrl + Shift + Enter : Paste as clean Plain Text (strips messy formatting)
• Ctrl + 1 … 9 : Paste items #1 to #9 instantly
• Ctrl + K : Open Text Transform menu (Case, JSON, SQL, Base64)
• Ctrl + Space : Multi-select items
• Ctrl + S : Start Sequential Paste Stack
• Ctrl + P : Pin / Unpin important clips
• Ctrl + N / Ctrl + E : Create / Edit reusable Snippet
• Ctrl + R : Reveal masked passwords and tokens
• Ctrl + D : Dock to Screen Edge (Right Sidebar → Left Sidebar → Floating)
• Esc : Close window

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
🛡️ 100% PRIVATE & OFFLINE — ZERO TELEMETRY
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
• No account required.
• No analytics or telemetry.
• No cloud servers. No background network requests.
• Honors "Don't Record" flags from 1Password, Bitwarden, KeePass, and LastPass.

Download Advanced Clipboard Manager now and reclaim your productivity!
```

---

### 3. Product Features (Bullet points ngắn cho mục Features trên Store)

1. **Global Quick Paste (Ctrl+Shift+V)**: Instant keyboard-first palette accessible from any app.
2. **Offline OCR Text Extraction**: Recognize and search text inside copied images and screenshots.
3. **Sequential Paste Stack**: Copy multiple items and paste them consecutively with Ctrl+V.
4. **Instant Text Transformations (Ctrl+K)**: JSON formatter/minifier, SQL beautifier, case converters, Base64 & URL encodings.
5. **Smart Sensitive Data Protection**: Auto-masks API keys, tokens & passwords with auto-expiration and zero cloud leakage.
6. **Ultra-Fast SQLite FTS5 Search**: Sub-100ms search across 10,000+ items with prefix filtering and multi-language support.
7. **Dockable Screen Sidebar (Ctrl+D)**: Snap the clipboard manager to screen edges as a persistent productivity dock.
8. **Reusable Snippets & Dynamic Tags**: Create templates with auto-expanding `{date}`, `{time}`, `{uuid}`, and `{clipboard}` tags.
9. **Zero-Formatting Paste (Ctrl+Shift+Enter)**: Strip font/color mess and paste pristine clean plain text.
10. **100% Offline & DPAPI Encrypted**: Absolute privacy with local hardware-backed Windows encryption.

---

### 4. What's New in this Version (Cho mục release notes)

```text
Version 0.2.0 Highlights:
• 🚀 Instant Hotkey Access: Press Ctrl+Shift+V anywhere to summon your clipboard.
• 🖼️ Offline OCR Engine: Automatic text recognition inside screenshots and images.
• 📚 Paste Stack: Sequential multi-item pasting for ultra-fast form filling and data entry.
• 🛠️ Text Transform Menu (Ctrl+K): One-click JSON formatting, SQL prettifier, Base64/URL encoding, and case conversion.
• 🔒 Secret Shield: Automated detection and masking for API tokens, passwords, and connection strings.
• 🖥️ Edge Docking (Ctrl+D): Pin clipboard as a side dock on Windows 10/11.
• ⚡ Blazing fast SQLite FTS5 search with accent-insensitive matching.
• 🎨 Modern Windows 11 Fluent Acrylic design with Light/Dark theme support.
```

---

### 5. Search Keywords / Tags (Từ khóa ASO tối ưu tìm kiếm trên Store)

```text
clipboard, clipboard manager, paste, copy paste, clipboard history, ocr, paste stack, snippet, developer tools, text transform, productivity, win v alternative, secure clipboard
```

---

# PHẦN 2: BẢN TIẾNG VIỆT (DÀNH CHO NGƯỜI DÙNG VIỆT NAM)

### 1. Mô tả ngắn (Tối đa 100 ký tự)
```text
Bấm Ctrl+Shift+V ở mọi nơi. Quản lý clipboard đỉnh cao: Đọc chữ trong ảnh OCR, dán liên tiếp, bảo mật.
```

---

### 2. Mô tả chi tiết (Full Description)

```text
🚀 TRUY CẬP TỨC THÌ: Bấm [Ctrl + Shift + V] ở bất kỳ đâu để mở bảng lịch sử sao chép!
(Bạn cũng có thể nhấp vào biểu tượng ở khay hệ thống bên dưới góc phải màn hình hoặc đổi phím tắt trong Cài đặt).

Bạn cảm thấy trình Clipboard mặc định của Windows (Win + V) quá tù túng, chỉ lưu tối đa 25 mục, không tìm kiếm được tiếng Việt, hay làm mất định dạng và không hỗ trợ xử lý dữ liệu?

Hãy trải nghiệm Advanced Clipboard Manager — công cụ quản lý bảng nhớ tạm (clipboard) thế hệ mới, tối ưu tuyệt đối cho bàn phím, hoạt động siêu nhẹ và an toàn bảo mật 100% cục bộ trên Windows 10 & Windows 11.

Không bao giờ sợ mất đoạn code, đường link, số tài khoản, mật khẩu hay ảnh chụp màn hình vừa copy!

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
🔥 NHỮNG TÍNH NĂNG ĐỈNH CAO VƯỢT TRỘI SO VỚI WIN + V:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

⚡ 1. NHẬN DIỆN CHỮ TRONG ẢNH (OCR OFFLINE)
Vừa chụp ảnh màn hình thông báo lỗi, tài liệu giấy hay bảng dữ liệu?
• Ứng dụng tự động quét chữ bên trong mọi bức ảnh sao chép hoàn toàn offline bằng Windows Media OCR.
• Tìm kiếm lại ảnh chụp màn hình cũ bằng chính nội dung chữ bên trong bức ảnh!
• Nhấn [Ctrl + Shift + Enter] để dán trực tiếp đoạn chữ trong ảnh ra văn bản mà không cần ngồi gõ lại từng chữ.

📚 2. PASTE STACK (DÁN HÀNG ĐỢI LIÊN TIẾP) — TRỢ THỦ ĐẮC LỰC KHI NHẬP LIỆU
Bạn cần copy 5 ô thông tin từ email rồi dán lần lượt vào từng ô trên web hoặc bảng tính Excel?
• Chọn các mục theo thứ tự cần dán bằng [Ctrl + Space], rồi bấm [Ctrl + S].
• Chuyển sang ứng dụng cần nhập liệu và chỉ việc nhấn [Ctrl + V] liên tục: Mỗi lần nhấn Ctrl+V sẽ tự động dán mục tiếp theo theo đúng thứ tự!

🛠️ 3. MENU BIẾN ĐỔI VĂN BẢN TỨC THÌ (Ctrl + K)
Xử lý dữ liệu ngay lập tức mà không cần mở các trang web convert online:
• Đổi kiểu chữ: Viết HOA, viết thường, Viết Hoa Chữ Đầu (Title Case), Viết hoa đầu câu.
• Dọn dẹp văn bản: Xóa khoảng trắng thừa, gộp toàn bộ thành 1 dòng, xóa các dòng trống.
• Công cụ chuyên sâu cho Lập trình viên: Định dạng đẹp & Nén gọn JSON (Pretty/Minify JSON), làm đẹp câu lệnh SQL (Format SQL), Mã hóa & Giải mã Base64 / URL.

🔍 4. TÌM KIẾM SIÊU TỐC — HỖ TRỢ TIẾNG VIỆT KHÔNG DẤU
Tích hợp SQLite FTS5 mạnh mẽ:
• Tìm kiếm hàng chục ngàn mục lịch sử trong chớp mắt (<100ms).
• Hỗ trợ gõ tiếng Việt không dấu: Gõ "chao" tìm ra "chào", gõ "don" tìm ra "đơn hàng".
• Bộ lọc thông minh: type:code (mã nguồn), type:image (hình ảnh), type:url (liên kết), type:snippet, pinned:true (mục ghim).

🔒 5. TỰ ĐỘNG BẢO VỆ DỮ LIỆU NHẠY CẢM & MÃ HÓA DPAPI
Không lo lộ lọt mật khẩu hay thông tin bí mật:
• Tự nhận diện API Key (OpenAI, Stripe, AWS...), Token GitHub, Private Key, Mật khẩu, Chuỗi kết nối Database.
• Tự động che giấu (masking) trên giao diện cho đến khi bạn bấm [Ctrl + R] để mở khóa.
• Private Key cam kết KHÔNG BAO GIỜ lưu xuống ổ cứng.
• Tự động hủy (xóa sạch) dữ liệu nhạy cảm sau 1 - 5 phút.
• Tùy chọn mã hóa toàn bộ cơ sở dữ liệu bằng Windows DPAPI gắn liền với tài khoản máy tính của bạn. Dữ liệu 100% lưu tại máy cá nhân, không máy chủ, không cloud.

📌 6. ĐOẠN VĂN MẪU TÁI SỬ DỤNG (SNIPPETS & TEMPLATES)
Lưu các đoạn văn mẫu, câu trả lời sẵn hay khung code thường dùng vĩnh viễn với [Ctrl + N].
• Hỗ trợ biến tự động điền: {date} (ngày), {time} (giờ), {uuid} (mã ngẫu nhiên), {clipboard} (nội dung vừa copy).

🖥️ 7. GẮN CỐ ĐỊNH CẠNH MÀN HÌNH (SIDEBAR DOCK - Ctrl + D)
• Nhấn [Ctrl + D] để ghim bảng clipboard cố định vào mép phải hoặc mép trái màn hình (dạng AppBar chiếm không gian như Taskbar).
• Cửa sổ luôn hiển thị bên cạnh để bạn vừa tra cứu vừa dán dữ liệu vào công việc chính mà không bị gián đoạn.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
⌨️ BẢNG PHÍM TẮT THAO TÁC CỰC NHANH:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
• Ctrl + Shift + V : Mở / Đóng bảng dán nhanh Quick Paste
• ↑ / ↓ / PgUp / PgDn : Di chuyển lựa chọn
• Enter : Dán mục đang chọn (hoặc dán gộp các mục đã đánh dấu)
• Ctrl + Shift + Enter : Dán văn bản thuần Plain Text (xóa sạch định dạng thừa)
• Ctrl + 1 … 9 : Dán nhanh tức thì mục số 1 đến 9
• Ctrl + K : Mở menu biến đổi văn bản (Hoa/thường, JSON, SQL, Base64...)
• Ctrl + Space : Chọn nhiều mục cùng lúc
• Ctrl + S : Kích hoạt dán liên tiếp (Paste Stack)
• Ctrl + P : Ghim / Bỏ ghim mục quan trọng
• Ctrl + N / Ctrl + E : Tạo mới / Chỉnh sửa đoạn mẫu (Snippet)
• Ctrl + R : Mở khóa xem nội dung nhạy cảm / mật khẩu
• Ctrl + D : Chuyển chế độ gắn cạnh màn hình (Phải → Trái → Tắt)
• Esc : Đóng cửa sổ

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
🛡️ 100% NỘI BỘ, KHÔNG TELEMETRY, AN TOÀN TUYỆT ĐỐI
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
• Không yêu cầu đăng ký tài khoản.
• Không thu thập dữ liệu hành vi người dùng (Zero telemetry).
• Không kết nối bất kỳ máy chủ mạng nào.
• Tôn trọng cờ bảo mật từ 1Password, Bitwarden, KeePass, LastPass.

Tải ngay Advanced Clipboard Manager để nâng cấp hiệu suất làm việc của bạn lên gấp nhiều lần!
```

---

### 3. Danh sách tính năng (Product Features - Bullet points)

1. **Dán nhanh toàn cục (Ctrl+Shift+V)**: Mở bảng clipboard siêu tốc từ bất kỳ phần mềm nào.
2. **Trích xuất chữ từ ảnh (OCR Offline)**: Tự động đọc và tìm kiếm chữ trong ảnh chụp màn hình.
3. **Dán liên tiếp theo hàng đợi (Paste Stack)**: Chọn nhiều mục và dán lần lượt từng mục bằng Ctrl+V cực kỳ tiện lợi.
4. **Biến đổi văn bản tức thì (Ctrl+K)**: Format JSON, làm đẹp SQL, đổi chữ hoa/thường, mã hóa Base64 & URL.
5. **Tự động che chắn dữ liệu nhạy cảm**: Phát hiện và ẩn API key, token, mật khẩu kèm cơ chế tự hủy an toàn.
6. **Tìm kiếm FTS5 siêu tốc & gõ tiếng Việt không dấu**: Tìm kiếm hàng vạn mục dưới 100ms với bộ lọc chuyên sâu.
7. **Ghim cố định cạnh màn hình (Ctrl+D)**: Biến clipboard thành Sidebar AppBar hỗ trợ đa nhiệm trực quan.
8. **Đoạn mẫu thông minh (Snippets)**: Lưu mẫu văn bản dùng lại mãi mãi kèm biến tự động {date}, {time}, {uuid}.
9. **Dán văn bản thuần túy (Ctrl+Shift+Enter)**: Loại bỏ font chữ, bảng biểu, màu sắc rác để dán văn bản sạch.
10. **Bảo mật tuyệt đối 100% Offline**: Mã hóa Windows DPAPI, không gửi bất kỳ dữ liệu nào qua Internet.

---

### 4. Có gì mới trong phiên bản này (What's New)

```text
Điểm mới trong phiên bản 0.2.0:
• 🚀 Kích hoạt siêu tốc: Bấm Ctrl+Shift+V ở mọi nơi để mở ngay lịch sử copy.
• 🖼️ OCR Offline thông minh: Nhận diện và tìm kiếm chữ trong ảnh chụp màn hình hoàn toàn offline.
• 📚 Paste Stack: Dán liên tiếp nhiều mục tự động bằng Ctrl+V khi điền biểu mẫu.
• 🛠️ Menu biến đổi văn bản (Ctrl+K): Làm đẹp JSON, SQL, đổi hoa/thường, mã hóa Base64 trong 1 click.
• 🔒 Bảo vệ dữ liệu nhạy cảm: Tự động ẩn token, mật khẩu, API key và tự động xóa sau thời gian cấu hình.
• 🖥️ Ghim cạnh màn hình (Ctrl+D): Bảng điều khiển sidebar cố định hỗ trợ làm việc song song.
• ⚡ Tìm kiếm tiếng Việt không dấu siêu tốc bằng SQLite FTS5.
• 🎨 Giao diện Fluent mờ Acrylic hiện đại chuẩn Windows 11.
```
