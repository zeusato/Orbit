# Orbit 0.5.0 — .NET 10, HEIC và Khung nền

## Nền tảng

- Chuyển toàn bộ sang .NET 10 LTS (SDK 10.0.402, runtime 10.0.13) vì .NET 8 hết hỗ trợ ngày 10/11/2026.
- ZIP giờ là **self-contained**: không cần cài .NET Desktop Runtime. OCR vẫn cần Microsoft Visual C++ Redistributable x64. ZIP khoảng 119 MB, giải nén khoảng 242 MB (0.4.1: khoảng 52 MB).
- Bản 0.4.x tự cập nhật lên được: helper cũ tải ZIP mới, kiểm tra chữ ký và khởi động bản self-contained. Gói vẫn có đủ các file mà helper 0.4.x yêu cầu.
- Helper cập nhật giờ chép cả runtime mà nó cần (theo `Orbit.Updater.deps.json`) ra thư mục chạy riêng. Script đóng gói kiểm tra helper bản sao khởi động được khi không có .NET dùng chung.

## Ảnh HEIC/HEIF

- Mở ảnh HEIC/HEIF (ảnh iPhone) bằng bộ giải mã libheif có sẵn trong Magick.NET. Không cần HEIF Extension của Windows.
- Nhận dạng theo brand trong `ftyp`; HEIF chứa AV1 được xử lý như AVIF.
- HEIC có nhiều ảnh cấp cao sẽ lấy ảnh chính (primary), giống trình xem ảnh. HEIF dạng chuỗi hoặc ảnh động (`msf1`, `hevs`) bị từ chối rõ ràng.
- HEIC chỉ là định dạng đầu vào; Orbit không xuất HEIC.

## Khung nền

Công cụ mới trên menu ảnh, sau Nén JPEG. Orbit mở rộng khung ra xung quanh ảnh và tô nền; ảnh gốc được giữ nguyên từng pixel, không tách hay can thiệp vào nền bên trong ảnh.

- Khoảng viền 0–40% cạnh dài; tỷ lệ khung Theo ảnh, 1:1, 4:5, 16:9, 9:16. Khung chỉ nới ra, không bao giờ cắt ảnh.
- Nền:
  - **Màu**: chọn từ bảng màu hoặc nhập mã `#RRGGBB`.
  - **Chuyển màu**: 4 mẫu có sẵn, hai màu tự chọn, hướng theo quy ước CSS.
  - **Họa tiết**: chấm bi, sọc chéo, ô lưới hoặc giấy.
  - **Ảnh**: một ảnh khác, làm mờ hoặc không.
  - **Ảnh gốc mờ**: chính ảnh đó phóng đầy khung và làm mờ.
- Bo góc 0–20% cạnh ngắn, đổ bóng mềm, xuất PNG hoặc JPEG 92%.
- Có xem trước trực tiếp. Batch dùng cùng thông số, khung tính theo tỷ lệ của từng ảnh. Canvas vượt 60 MP hoặc 32768 px/cạnh bị từ chối kèm hướng dẫn.

## Thanh tiến trình góc phải

- Tác vụ chạy quá 0,7 giây (OCR, PDF nhiều trang, Office, batch) hiện một bảng nhỏ ở góc phải dưới, đúng vị trí thông báo hoàn tất. Bảng gồm: tên file, thanh tiến độ, "Công cụ · %" và số thứ tự trong lượt (ví dụ 2/8).
- Có nút **Mở Orbit** và **Hủy** (với nhiều tác vụ là **Hủy tất cả**).
- Bảng không lấy focus khỏi ứng dụng đang dùng. Bảng ẩn khi cửa sổ chính đang ở phía trước (danh sách đã có tiến độ), và tự đóng khi xong; sau đó thông báo hoàn tất như cũ.
- Tác vụ ngắn không hiện bảng. Khi hàng đợi rỗng, timer dừng để không tốn CPU nền.
- Tiến độ là con số công cụ báo về: OCR theo vùng/trang, PDF theo trang. Bước chưa báo % (Office COM, mở file) hiện thanh chạy liên tục, không phải phần trăm ước đoán.

## Cập nhật và lịch sử tác vụ

- **Phục hồi khi cập nhật bị ngắt**: nếu helper dừng giữa lúc thay file (mất điện, bị kill), lần mở Orbit sau sẽ phát hiện journal dở. Orbit giao cho helper chép riêng khôi phục toàn bộ bản cũ rồi mở lại. Chỉ tự thử một lần; nếu thất bại, Orbit hiện hướng dẫn thay vì lặp lại.
- **Tự dọn dẹp**: giữ bản backup hoàn tất mới nhất và bản tải đang chờ. Xóa các bản tải cũ hơn 10 phút và các backup cũ đã hoàn tất hoặc đã rollback. Không động vào journal dở hoặc thư mục không phải của Orbit.
- **Lịch sử tác vụ** được lưu qua các lần mở app trong `%LOCALAPPDATA%\Orbit\history.json` (tối đa 200 dòng). File chỉ ghi đường dẫn, công cụ và kết quả. Nút **Xóa lịch sử** chỉ xóa danh sách, không xóa file kết quả.

## Kiểm chứng

- Build 0 cảnh báo/0 lỗi; 74/74 kiểm thử mặc định (gồm thanh tiến trình: hiện ở góc phải với tác vụ OCR thật, không lấy focus, tự ẩn khi xong và khi cửa sổ chính ở phía trước), có bài giải mã HEIC thật khi đặt `ORBIT_HEIC_SAMPLE`. File mẫu là `example.heic` của libheif, chỉ dùng để thử trên máy, không đưa vào repo.
- Khung nền: kiểm tra từng pixel ảnh gốc được giữ 1:1, màu viền, chuyển màu, 4 họa tiết, nền ảnh có và không mờ, ảnh gốc mờ, bo góc, đổ bóng, JPEG. Editor WPF thật được kiểm tra luồng đổi tỷ lệ, chọn mẫu chuyển màu, từ chối mã màu sai và lưu ra ảnh đúng kích thước.
- Phục hồi bằng tiến trình thật (`scripts/test-updater.ps1 -RecoverOnly`): install bị trộn phiên bản được trả về 0.0.1, file mới thêm bị gỡ, file người dùng giữ nguyên.
- Đã chạy `dist/Orbit/Orbit.exe --render-previews` với DOTNET_ROOT trỏ sai: render giao diện, WebP, PDF và OCR đều đạt.
- Chưa kiểm chứng trên máy này:
  - Hai kịch bản helper có ký (nâng phiên bản và rollback): cần private key, sẽ chạy trên CI khi push.
  - Cập nhật thật từ 0.4.1 cài sẵn lên 0.5.0.
  - Ảnh HEIC chụp từ iPhone thật.
  - Khung nền ở canvas lớn gần 60 MP về thời gian và bộ nhớ.
  - Thao tác chuột trong editor Khung nền trên màn hình thật.
