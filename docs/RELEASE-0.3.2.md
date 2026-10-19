# Orbit 0.3.2 — tương tác công cụ

Theo yêu cầu Đại ca, em cải thiện các công cụ hiện có, không thêm engine OCR hoặc phụ thuộc mới.

## Thay đổi

- Crop có zoom tại con trỏ, 100%, vừa khung, Space + kéo/chuột giữa để pan, tám tay nắm, vẽ vùng mới bằng Shift + kéo. Preset giữ tỷ lệ khi kéo cạnh/góc và chạm biên; Tự do giữ vùng đã chọn.
- Sửa lưu crop dùng giá trị cũ khi ô nhập chưa mất focus; số không hợp lệ hoặc vượt biên được báo ngay và chặn lưu. Enter lưu, Esc hủy. Nút đặt lại khôi phục toàn ảnh.
- Resize/nén đều có zoom/pan. Resize có nút kích thước gốc, thông tin đầu ra và nhắc phóng lớn. Nén có preset chất lượng và preview JPEG nền trắng theo chất lượng; checkbox xem gốc. Preview tối đa 1600 px, xuất vẫn dùng ảnh gốc. Không hứa dung lượng trước mã hóa.
- Drop zone đổi viền/nội dung khi nhận file; từ chối thư mục. Menu không yêu cầu rê lần hai sau nhận diện; không vẽ lại màu toàn vòng liên tục khi vẫn ở một ô. Giữ Thêm 650 ms để sang trang khi đang kéo; yêu cầu di chuyển sau chuyển trang. Thả trong lúc nhận diện giữ menu mở, chờ chọn công cụ rõ ràng.
- Sửa vùng tiêu đề bắt thao tác kéo từ nút đóng; nhấp đúp tiêu đề phóng to/khôi phục. Editor có kích thước tối thiểu tránh đè preview và ô nhập.

## Kiểm chứng

Build Release 0 cảnh báo/0 lỗi. 49/49 kiểm thử đạt tại `artifacts/tests/20261019-170916-296`. Các bài hồi quy mới kiểm tra pixel ảnh crop sau zoom 200%/pan, biên/tỷ lệ, ô nhập sai và lưu giá trị mới qua hàng đợi thật, preview JPEG khi đổi chất lượng liên tiếp. Đầu ra thử nghiệm nằm trong artifacts, không sửa file gốc.

Đã mở ứng dụng thật, quan sát home/menu và chọn ảnh từ dialog Windows. Công cụ điều khiển đưa home lên trước khi click menu nổi nên không dùng kết quả đó để kết luận crop lỗi. Đã mở editor riêng bằng `Orbit.Tests --editor-preview <ảnh> Crop`; Đại ca dừng Computer Use bằng Escape trước khi hoàn tất thử chuột. Chưa nghiệm thu thao tác zoom/pan/drag native; các kết quả crop ở trên là kiểm thử WPF và pixel đầu ra.

Chưa nghiệm thu toàn bộ Explorer/Desktop, nhiều màn hình, DPI hỗn hợp hoặc high contrast. Giới hạn OCR 0.3.1 vẫn giữ nguyên; không bổ sung Python/PyTorch hay model mới.
