# Kế hoạch Triển khai: Phân giải Tham số URL (URL Query Parameters Breakdown) & Copy Link Sạch

## 1. Mục tiêu
- [x] **Phân tách rõ ràng các thành phần URL**: Sửa mục `Path` trong URL Breakdown để chỉ hiển thị đường dẫn tài nguyên thuần túy (`uri.AbsolutePath`), không bị dính chuỗi query string loằng ngoằng.
- [x] **Phân giải chi tiết Query Parameters**: Hiển thị danh sách các cặp `Key = Value` trực quan, dễ đọc, đã được tự động giải mã URL decode (khoảng trắng, ký tự có dấu,...).
- [x] **Hành động 1 chạm (Quick Actions)**:
  - Cho phép copy nhanh từng giá trị của parameter (click icon `📋` cạnh param).
  - Bổ sung nút **"Copy clean link"** (Sao chép liên kết sạch) để loại bỏ các tham số theo dõi (tracking như `fbclid`, `utm_*`, `si`,...) giúp người dùng chia sẻ link gọn gàng.
- [x] **Trải nghiệm thân thiện cho người dùng phổ thông**: Tự động ẩn phần parameters nếu URL không có tham số; bố cục tinh gọn, không chiếm dụng không gian và không gây rối mắt.

---

## 2. Tiến độ triển khai

### Giai đoạn 1: Xây dựng Core Service & Logic (`ClipboardManager.Core`) - [HOÀN THÀNH]
- [x] Tạo file `src/ClipboardManager.Core/Services/UrlHelper.cs`:
  - Model: `UrlParameter(string Key, string Value, bool IsTracking)`.
  - Hàm `ParseQueryParameters(string url)`: bóc tách an toàn các cặp tham số, xử lý giải mã URL decode UTF-8.
  - Hàm `GetCleanUrl(string url)`: loại bỏ các tracking query params phổ biến (`fbclid`, `utm_source`, `utm_medium`, `gclid`, `si`, `igshid`,...).
  - Hàm `AnalyzeUrl(string url)`: trả về đầy đủ Scheme, Host, Path thuần và danh sách Parameters.
- [x] Cập nhật từ điển đa ngôn ngữ trong `src/ClipboardManager.Core/Services/LocalizationService.cs`.

### Giai đoạn 2: Viết Unit Tests (`tests/ClipboardManager.Core.Tests`) - [HOÀN THÀNH]
- [x] Tạo file `tests/ClipboardManager.Core.Tests/UrlHelperTests.cs`:
  - Test phân tích URL có nhiều params (Facebook, YouTube, Shopee).
  - Test giải mã URL encoded characters (`%20`, tiếng Việt UTF-8).
  - Test lọc bỏ tracking parameters và giữ nguyên query params nghiệp vụ.
  - Test các trường hợp biên: URL không có param, URL không có scheme, URL lỗi.
  - Chạy và đạt 100% test cases pass.

### Giai đoạn 3: Cập nhật Giao diện & Tương tác (`ClipboardManager.App`) - [HOÀN THÀNH]
- [x] Cập nhật `src/ClipboardManager.App/UI/QuickPasteWindow.xaml`:
  - Thêm nút `UrlCleanCopyButton` ("Copy clean link") cạnh nút `UrlCopyButton`.
  - Cập nhật khối `URL Breakdown`:
    - Dòng `Path` chỉ hiển thị `AbsolutePath` sạch.
    - Thêm vùng `UrlParamsContainer` gồm tiêu đề `Parameters (count)` và danh sách các thẻ tham số có icon copy.
- [x] Cập nhật `src/ClipboardManager.App/UI/QuickPasteWindow.xaml.cs`:
  - Sử dụng `UrlHelper.AnalyzeUrl(...)` khi render preview URL.
  - Xử lý sự kiện bấm "Copy clean link" và copy từng giá trị parameter kèm phản hồi thông báo tại thanh trạng thái (`StatusText`).

### Giai đoạn 4: Kiểm thử & Hoàn thiện - [HOÀN THÀNH]
- [x] Chạy `dotnet test` / `dotnet run --project tests/ClipboardManager.Core.Tests` đạt 138/138 tests passed.
- [x] Chạy `dotnet build` đạt 0 warning, 0 error.
