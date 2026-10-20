# Orbit 0.6.0 — PDF và vòng tiện ích

## Ảnh và PDF

- Ảnh → PDF có hộp chọn gọn với hai nút: **Chuyển ngay** tạo PDF riêng cho mỗi ảnh; **Thêm trang…** mở editor để thêm ảnh/PDF và xuất chung một file.
- **Ghép PDF và ảnh**: thêm bằng chọn file hoặc kéo thả, xem trước các trang, đổi thứ tự file, bỏ mục khỏi danh sách. Có thể bắt đầu từ một PDF.
- **Tách trang PDF** theo khoảng như `1-3, 5`; xuất chung theo thứ tự đã nhập hoặc thành các file riêng.
- **Nén PDF** bằng cách nén lại ảnh nhúng phù hợp; giữ lớp chữ và kích thước trang. Hiển thị dung lượng thực trước/sau. Nếu không giảm dung lượng, xuất bản sao nguyên nội dung.
- Ghép/tách/nén có tên và thư mục xuất riêng. Khi trùng tên, tự thêm số; không sửa, di chuyển hay xóa nguồn.
- **Dán ảnh** bằng Ctrl+V hoặc nút Dán ảnh ở Home để mở menu; trong editor ghép, ảnh được thêm thành trang. Ảnh clipboard lưu cục bộ, kết quả mặc định vào Pictures/Orbit hoặc thư mục đã cấu hình.

## Vòng tiện ích khi không kéo file

Orbit đang chạy ở khay: bấm **Ctrl+Space** để mở vòng sáu tiện ích tại con trỏ, chọn bằng chuột hoặc phím 1–6. Esc, bấm ra ngoài hoặc Ctrl+Space lần nữa đóng vòng. Không cần mở cửa sổ chính; hotkey kéo file vẫn theo Cài đặt.

- **Tạo QR** từ link/text UTF-8, xem trước, sao chép ảnh hoặc lưu PNG.
- **Đọc QR/barcode** từ file hoặc ảnh clipboard, hiển thị nội dung và loại mã, không tự mở link.
- **Ảnh ↔ Base64**: hỗ trợ chuỗi thuần/data URI, xem trước và lưu ảnh; giữ byte gốc khi đọc từ file.
- **Base64/URL văn bản**, **JSON** kiểm tra/định dạng/thu gọn và **Dọn văn bản**.

Đổi mục giữ nội dung trong phiên; sửa đầu vào xóa kết quả cũ. Chỉ đọc clipboard khi được yêu cầu, không ghi nội dung tiện ích vào lịch sử. Bổ sung ZXing.Net theo giấy phép Apache-2.0, kèm license/notice trong bản portable.

## Phát hành

- `scripts/prepare-release.ps1` đồng bộ version App/Updater, README và tạo release notes; sau đó commit/push như thường.
- GitHub Actions build/test mọi push. Version mới trên `main` được đóng gói self-contained, ký và kiểm thử updater trước khi tạo tag/publish.
- Giữ Windows 11 x64, .NET 10 đi kèm và cơ chế cập nhật có chữ ký riêng; EXE chưa có chứng chỉ Authenticode.

## Giới hạn đã biết

Các lỗi dưới đây vẫn tồn tại trong bản này:

- Nén PDF có thể không phát hiện chữ ký số nằm trực tiếp trong trường AcroForm; việc ghi lại PDF làm chữ ký mất hiệu lực. Không dùng nén cho PDF cần giữ chữ ký số.
- PDF → ảnh và preview có thể thiếu giá trị đã điền trong form.
- Ghép/tách PDF chưa giữ đầy đủ AcroForm, có thể làm mất khả năng điền form của đầu ra.
- Editor media có thể lưu theo giá trị thời gian hợp lệ trước đó sau khi ô nhập sai mất focus. Kiểm tra lại thời gian trước khi lưu.

Barcode đã kiểm thử trên QR/Code128/EAN-13 tự sinh; không cam kết mọi loại mã hoặc ảnh mờ. Clipboard thật, kéo thả/DPI/đa màn hình và file thực tế đa dạng chưa được nghiệm thu hết. HEIC thật và Office là các nhóm kiểm thử tùy chọn.

## Kiểm chứng

Trước push: build Release không có cảnh báo/lỗi, 125/125 kiểm thử ứng dụng và 13/13 nhóm kiểm tra chuẩn bị release đạt trên máy phát triển. HEIC thật và Office tùy chọn không nằm trong lượt chạy này.

Build và bộ kiểm thử ứng dụng, script chuẩn bị release, đóng gói và các kịch bản updater success/rollback/recover là điều kiện của workflow phát hành. Kết quả chạy cho commit của bản này có trong [GitHub Actions](https://github.com/zeusato/Orbit/actions/workflows/release.yml). Không dùng Computer Use trong lượt phát hành.
