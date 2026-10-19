# Tiến độ Orbit

## Bản hiện tại: 0.4.0

Đã triển khai cập nhật portable từ `zeusato/Orbit`: kiểm tra/tải nền, lưu trạng thái tải qua restart, chữ ký RSA-PSS + SHA-256, kiểm tra ZIP/path/version, helper đợi app thoát, backup và rollback lỗi copy/khởi động. Có Settings và cửa sổ cập nhật, hoãn/tắt cập nhật, script ký và GitHub workflow tự tạo tag và publish release khi push main có phiên bản mới. Không thêm runtime/model mới. Chi tiết [UPDATES.md](UPDATES.md), [RELEASE-0.4.0.md](RELEASE-0.4.0.md).

57/57 kiểm thử tại `artifacts/tests/20261019-175755-577`; helper thực thi thành công cả nâng phiên bản và rollback bằng tiến trình fixture riêng tại `artifacts/updater-tests/c33da66873914583a5fe0dd93871c929`. Bản cũ đang chạy được giữ nguyên, build mới dùng artifacts-path riêng. Vòng đóng gói cuối đã đạt 57/57 kiểm thử, 0 warning/error và xác minh chữ ký thành công. Chưa phát hành stable trên GitHub ở thời điểm ghi mục này.

## Bản 0.3.2

Rà và sửa UX crop/resize/nén cùng menu kéo thả. Crop có zoom tại con trỏ, pan, tám tay nắm, vẽ mới bằng Shift + kéo, preset tỷ lệ/đặt lại và kiểm tra số trước lưu. Resize có nút kích thước gốc và thông tin đầu ra; nén có preview JPEG chạy nền giới hạn 1600 px, so với ảnh gốc. Menu giữ trên Thêm để đổi trang và cho thả chờ nhận diện; phản hồi drop zone rõ hơn. Không thêm thư viện hay model mới.

49/49 kiểm thử đạt tại `artifacts/tests/20261019-170916-296`; build 0 lỗi/0 cảnh báo. Có đối chiếu từng pixel crop sau zoom 200% và pan, tỷ lệ/biên, nhập sai và lưu giá trị cuối, preview JPEG khi đổi chất lượng liên tiếp. Đã mở UI thật và quan sát home/menu, nhưng Đại ca dừng Computer Use bằng Escape trước khi hoàn tất thao tác crop native; không coi luồng chuột/Explorer là đã nghiệm thu. Xem [RELEASE-0.3.2.md](RELEASE-0.3.2.md).

## Bản 0.3.1

Đại ca báo OCR mất phần lớn chữ trên infographic HPG thực tế. Đã tái hiện: ảnh gốc 2160×20365, PDF cùng tên 1145,495×10800 pt bị render chỉ 425×4000 px ở 0.3.0. Bỏ giới hạn cạnh dài 4000, thay bằng ngân sách 48 triệu pixel/cạnh 30000; OCR ảnh dọc dài theo vùng 2400 px có chồng 200 px, chọn dòng theo tâm bounding box để tránh lặp tại đường nối. Có tiến độ/hủy giữa vùng, giữ nguồn.

43/43 kiểm thử mặc định đạt trước đóng gói, gồm hồi quy ảnh dài/đường nối/hủy. Đã chạy trên PDF và PNG thật: khôi phục nhiều đoạn nhỏ ở thân/cuối trang nhưng vẫn có lỗi dấu và số (ví dụ 25,13% đọc thành 28,13%). **Chưa đạt yêu cầu chính xác OCR trên tài liệu thực tế của Đại ca.** Chi tiết và số đo ở [RELEASE-0.3.1.md](RELEASE-0.3.1.md). Các thử nghiệm engine khác không đạt tiếng Việt nên không đưa vào bản phát hành.

## Bản 0.3.0

PDF có lớp chữ dùng được → DOCX sửa được, giữ dòng và ngắt trang cơ bản; tạo trực tiếp, không cần Microsoft Word. PDF scan hoặc PDF trộn → Lấy chữ TXT; từng trang dùng lớp chữ hoặc OCR Việt/Anh. Ảnh tĩnh có OCR → TXT ở trang công cụ sau. Không phục dựng bảng, ảnh, font hoặc bố cục PDF → Word. Nhận dạng trước khi hiện menu chạy nền và hủy khi đóng/chọn file khác.

45/45 kiểm thử đạt ở `artifacts/tests/20261019-160527-041`: 41 mặc định và 4 Office. Có OCR thực tế dấu tiếng Việt/số trên ảnh chữ in rõ, PDF trộn, DOCX mở và xuất lại bằng Word, bảo toàn nguồn/trùng tên/hủy, menu WPF thật. Đây là mẫu tổng hợp, chưa phải benchmark độ chính xác trên tài liệu thực tế đa dạng. Xem [RELEASE-0.3.md](RELEASE-0.3.md).

## Bản 0.2.2

Thêm Shift + Space vào Cài đặt, bên cạnh Ctrl + Space và Ctrl + Shift + Space. Lưu áp dụng ngay lên input hook và hướng dẫn ở cửa sổ chính; nhớ lựa chọn sau khi mở lại. Cấu hình HotkeyNeedsShift từ bản cũ được chuyển tương thích, không tự đổi phím người dùng đã chọn. Chỉ kích hoạt khi chuột trái đang kéo đủ xa, tổ hợp bổ trợ khớp chính xác và không giữ Alt/Windows. 33/33 kiểm thử mặc định đạt ở `artifacts/tests/20261019-153550-751`, gồm ma trận phím bổ trợ/trạng thái kéo, đọc cấu hình cũ và lưu/hủy từ cửa sổ WPF thật. Chưa tự động nghiệm thu toàn bộ thao tác Shift + Space trong phiên kéo của Explorer.

## Bản 0.2.1

Thêm lời hỏi chạy cùng Windows ở lần mở thủ công đầu tiên. Bật sẽ đăng ký trong tài khoản hiện tại với `--background`; Không bật/X/Esc lưu lựa chọn và không hỏi lại. Có thể đổi trong Cài đặt. Không hiện trong phiên tự chạy hoặc chế độ kiểm thử, không hỏi lại khi đã đăng ký. 31/31 kiểm thử đạt ở `artifacts/tests/20261019-151144-838`, gồm cửa sổ WPF thật, lưu lựa chọn, lỗi đăng ký và thử lại; bài test dùng callback thay registry để không thay đổi startup của Đại ca. Chưa kiểm chứng qua đăng xuất/đăng nhập Windows thực tế.

## Bản 0.2.0

Em đã hoàn thành thay đổi theo yêu cầu mới của Đại ca: vòng tròn không có nền vuông, menu theo nội dung file, WebP và các bộ xử lý PDF/Office. Chi tiết, danh sách định dạng và giới hạn nằm trong [RELEASE-0.2.md](RELEASE-0.2.md). 33 kiểm thử đã đạt, gồm xuất PDF thật qua Word, Excel và PowerPoint. Những mục bên dưới là lịch sử 0.1.1, không phải phạm vi hiện tại.

## Bản 0.1.1 — 19/10/2026

Em đã sửa lỗi Đại ca báo: `A TwoWay or OneWayToSource binding cannot work on the read-only property 'Progress'`. `ProgressBar.Value` mặc định dùng binding hai chiều, nhưng tiến độ do hàng đợi quản lý. Đổi thành `BindingMode.OneWay`; không mở quyền ghi vào model để che lỗi.

Kiểm thử WPF bổ sung đã tái hiện đúng exception trước khi sửa (20 đạt, 1 lỗi). Sau khi sửa, 21 kiểm thử đạt. Luồng tích hợp tạo cửa sổ thật, mở menu với JPEG, kích hoạt nút PNG, tạo file PNG đọc lại được, materialize hai hàng tác vụ thành công/lỗi và xác nhận thanh tiến độ đạt 100. Bộ kiểm thử trước đây chỉ kiểm tra engine và queue nên đã bỏ sót lỗi binding này.

## Giao diện kính

- Giảm lớp tint chính từ khoảng 87% xuống 14–16%; panel dùng lớp trắng mỏng, thêm ánh sáng gradient nhẹ. Chữ giữ nguyên độ đục.
- Cửa sổ chính/chỉnh sửa dùng Desktop Acrylic qua DWM. Bỏ border WPF bo 22 DIP chồng lên góc native; DWM quản lý một đường viền, góc và bóng duy nhất.
- Đã mở ứng dụng thật và quan sát blur nền phía sau cửa sổ chính bằng công cụ điều khiển Windows.
- Menu dùng cửa sổ layered, clip ellipse WPF và HRGN native; kiểm thử xác nhận vùng trung tâm nằm trong cửa sổ, các góc nằm ngoài. Menu dùng accent blur riêng vì system backdrop vẽ một mặt phẳng chữ nhật.
- Accent policy không phải hợp đồng SDK ổn định. Khi API thất bại, tắt kính hoặc bật high contrast, menu dùng nền đặc. Cần kiểm tra lại trên các bản Windows mục tiêu trước phát hành rộng rãi.
- Ảnh chụp cửa sổ của công cụ có thể chứa cả hình chữ nhật backdrop của cửa sổ phụ; không dùng ảnh render WPF hay HRESULT để kết luận hình dáng blur cuối cùng trên mọi desktop. Cần đối chiếu trực tiếp phần rìa menu với nền desktop khác nhau.

## Đã triển khai

- Khay hệ thống, một instance, phím gọi khi kéo, menu sáu phân đoạn, chọn file và drop zone.
- PNG/JPEG, resize, crop trực tiếp, xoay, nén JPEG; hàng đợi STA, tiến độ, hủy và báo lỗi.
- Ghi đầu ra cạnh nguồn, không ghi đè; xử lý trùng tên nguyên tử; chuẩn hóa EXIF; từ chối ảnh nhiều frame/APNG.
- Cài đặt sáng/tối/theo hệ thống, kính và startup tùy chọn.
- Đóng gói framework-dependent: `dist/Orbit/Orbit.exe`, ZIP `dist/Orbit-0.1.1-win-x64.zip`.

## Còn cần kiểm chứng

- Kéo file từ Explorer và Desktop với Ctrl Space xuyên suốt, nhiều màn hình, DPI 125–200%, monitor tọa độ âm.
- Giao diện sáng, high contrast, tắt transparency của Windows, chất lượng cạnh menu và nền sáng/tối khác nhau.
- Crop bằng chuột, batch lớn, quyền ghi thư mục và hủy encoder đang chạy trên máy thực.
- Hiệu năng menu dưới 100 ms và tài nguyên nền chưa có số đo nghiệm thu.

## Chưa có trong bản đầu

- WebP và xóa hoặc thay nền bằng mô hình AI.
- PDF, Office, audio và video.
- File ảo từ email hoặc trình duyệt, ảnh nhiều frame và thư mục.
- Bộ cài có chữ ký và tự cập nhật.

Kết quả kiểm thử nằm trong `artifacts/tests/<thời điểm>/results.txt`. Build 0 cảnh báo, 0 lỗi; giới hạn kiểm chứng ở trên vẫn áp dụng.
