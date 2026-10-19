# Thiết kế giao diện Orbit

0.5.0:
- **Khung nền** dùng chung khung editor với crop/resize/nén, có xem trước ảnh thu nhỏ với zoom/pan. Ô Khung nền nằm sau Nén JPEG trên menu ảnh.
- Thứ tự cột phải:
  - Khoảng viền và tỷ lệ khung.
  - Nền (Màu / Chuyển màu / Họa tiết / Ảnh / Ảnh gốc mờ) với tùy chọn riêng của từng loại: ô mã `#RRGGBB` kèm 8 màu mẫu, 4 mẫu chuyển màu, góc theo quy ước CSS, 4 họa tiết, chọn ảnh nền và làm mờ.
  - Bo góc, đổ bóng.
  - PNG/JPEG.
  - Kích thước đầu ra.
- Mã màu sai báo lỗi tại chỗ và giữ màu hợp lệ trước đó.
- Cửa sổ chính có nút **Xóa lịch sử** cạnh Cài đặt; danh sách tác vụ được giữ qua các lần mở.

0.4.1: Cài đặt và cửa sổ Cập nhật Orbit có dòng “Tác giả: Nguyễn Mạnh Quyết”; không hiển thị địa chỉ nguồn tải cập nhật.

0.4.0: Cài đặt có bật/tắt tự tải và cập nhật khi rảnh, nút mở cửa sổ Cập nhật Orbit. Cửa sổ này hiển thị bản đang dùng, trạng thái/tỷ lệ tải, Kiểm tra ngay/Cập nhật ngay/Để sau 4 giờ. Có đường vào từ menu khay. Không hiện popup liên tục khi mất mạng. Editor, job, menu hoặc kéo chuột chặn áp dụng; tự động chỉ khi mọi cửa sổ đã đóng về khay.

## Cập nhật tương tác 0.3.2

- Crop/resize/nén: cuộn zoom tại con trỏ, nút −/+/Vừa khung/100%; nhãn tỷ lệ hiển thị theo pixel nguồn. Khung xem clip nội dung khi phóng to.
- Crop: Space + kéo hoặc chuột giữa để pan; kéo bên trong để dịch vùng, tám tay nắm góc/cạnh để đổi khung. Shift + kéo vẽ vùng mới kể cả bên trong khung cũ. Mũi tên dịch 1 px, Shift + mũi tên dịch 10 px. Tọa độ lưu luôn theo ảnh nguồn, độc lập zoom/pan.
- Preset tỷ lệ có trạng thái chọn, thêm Gốc/9:16; Tự do bỏ khóa nhưng giữ vùng hiện tại. Đặt lại khôi phục toàn ảnh và vừa khung. Ô tọa độ sai chặn lưu, Enter áp dụng và lưu giá trị mới nhất.
- Resize: có kích thước gốc, thông tin đầu ra và nhắc khi phóng lớn. Nén: preset 60/85/95, xem JPEG theo chất lượng sau 250 ms ngừng kéo, xử lý nền, tối đa một tác vụ preview; bỏ kết quả cũ/đã đóng. Preview giới hạn cạnh 1600 px, không dùng làm dự đoán dung lượng hoặc thay đổi độ phân giải xuất.
- Menu: vùng thả có feedback rõ; giữ trên Thêm 650 ms để chuyển trang trong lúc kéo. Sau đổi trang phải di chuyển trước khi thả để tránh chạy nhầm ô mới. PDF đang nhận diện có thể thả để chờ và bấm công cụ; thao tác này chưa chạy xử lý file. Vùng tâm/khe ở menu đã sẵn sàng vẫn không chạy công cụ.

Từ 0.3.1, tiến độ OCR ảnh dọc dài cập nhật theo từng vùng đọc; Hủy chờ xong vùng native đang nhận dạng, không phải toàn bộ ảnh dài. Không thay đổi menu hay các lựa chọn cài đặt của người dùng.

Từ 0.3, menu PDF phân tích từng trang trước khi hiện công cụ: chỉ PDF có lớp chữ dùng được ở mọi trang không trống mới có **Word**; mọi PDF đọc được có **Lấy chữ**. Word xuất DOCX chứa chữ sửa được, giữ dòng/trang cơ bản, không phục dựng bảng/hình; mô tả công cụ nêu rõ giới hạn. Lấy chữ xuất TXT, ưu tiên lớp chữ và chỉ OCR trang scan, đánh dấu từng trang. Với ảnh tĩnh, **OCR → TXT** nằm ở các trang Thêm. Không thêm cửa sổ cấu hình OCR; kết quả xuất cạnh nguồn qua hàng đợi hiện có.

Thiết kế 0.2 cập nhật theo yêu cầu Đại ca: menu thay đổi theo loại file, phân trang tối đa sáu ô, ẩn đích trùng nguồn. Nền menu là ảnh chụp cục bộ tại lúc mở, blur và clip hoàn toàn trong vòng tròn; không dùng accent blur trên HWND. Chi tiết thay thế cho quy tắc sáu công cụ cố định và vật liệu menu bên dưới nằm trong [RELEASE-0.2.md](RELEASE-0.2.md).

Đại ca đã chọn phương án vòng phân đoạn trong bản mẫu. Em dùng kính mờ xanh xám, đường viền sáng mảnh, bóng mềm và xanh ngọc cho vùng chọn. Giao diện luôn có nền đặc dự phòng để giữ chữ rõ khi tắt hiệu ứng trong suốt.

## Menu tròn

Đường kính mục tiêu 400 DIP, tâm khoảng 112 DIP. Sáu phân đoạn 60 độ với khe hở nhỏ; mỗi ô có icon và tên ngắn. Menu đứng yên sau khi gọi, điều chỉnh vị trí trong vùng làm việc của màn hình trước khi nhận thao tác.

Thứ tự bản đầu từ trên cùng theo chiều kim đồng hồ: PNG, JPEG, Resize, Crop, Xoay 90 độ, Nén ảnh. Khi có WebP và mô hình xóa nền, bổ sung cấu hình công cụ ưu tiên thay vì tự đổi vị trí quen thuộc.

Tâm hiển thị tên hoặc số lượng file và trạng thái kéo thả. Chưa nhận được IDataObject thì hiện hướng dẫn di chuyển vào vòng. Vùng tâm và khe không thực hiện hành động. Chỉ tô sáng ô đủ điều kiện. Thả vào ô hợp lệ mới thực thi; Esc kết thúc phiên và đóng menu.

Phím mặc định Ctrl và Space, có tùy chọn Ctrl Shift và Space hoặc Shift và Space (từ 0.2.2). Cài đặt lưu lựa chọn và áp dụng ngay; các phím bổ trợ phải khớp chính xác. Chỉ can thiệp Space khi đang kéo; không chiếm Ctrl dùng để sao chép. Không tuyên bố tổ hợp nào không xung đột với mọi ứng dụng.

## Vật liệu

- Cửa sổ chỉnh ảnh và cài đặt dùng Desktop Acrylic với tint mỏng. Từ 0.1.1, góc ngoài do DWM quyết định để tránh hai lớp bo góc lệch nhau; không vẽ thêm border WPF bo 22 DIP bên trong.
- Cửa sổ menu dùng layered window, clip ellipse WPF, vùng native hình tròn và lớp vẽ phân đoạn. Menu dùng accent blur riêng; system backdrop chỉ dùng cho cửa sổ thường. Nếu API không hỗ trợ hoặc người dùng tắt kính, dùng nền đặc.
- Accent policy là đường tương thích ngoài hợp đồng SDK ổn định, cần kiểm tra trực tiếp trên từng bản Windows mục tiêu. Mục tiêu dài hạn là composition visual có clip hình học và host backdrop được hỗ trợ chính thức.
- Tránh dựa vào API blur cũ không còn tạo hiệu ứng trên Windows hiện đại. Không xem CSS blur trong bản mẫu là bằng chứng đã có blur native.
- Chữ 12 đến 14 DIP, tiêu đề 18 đến 24 DIP; không giảm độ rõ của vùng nhập liệu để tăng độ trong suốt.
- Chuyển trạng thái ngắn, không animation lặp. Tôn trọng thiết lập chuyển động và tương phản của hệ thống.

## Cửa sổ tương tác

| Cửa sổ | Nội dung |
| --- | --- |
| Chính | Hướng dẫn gọi menu, vùng nhận file, chọn file, tác vụ gần đây |
| Resize | Preview, preset cạnh dài, chiều rộng, chiều cao, giữ tỷ lệ |
| Crop | Preview đúng tỷ lệ ảnh, vùng cắt trực tiếp, tỷ lệ preset, tọa độ và kích thước chính xác |
| Nén JPEG | Preview, chất lượng và quy tắc lưu; không hứa dung lượng trước khi mã hóa |
| Tác vụ | Trạng thái xếp hàng, đang chạy, hoàn tất, hủy hoặc lỗi; mở đầu ra và thư mục |
| Cài đặt | Phím gọi, giao diện, kính, tự chạy khi đăng nhập; từ 0.2.1 hỏi Bật/Không bật ngay lần mở đầu và nhớ lựa chọn |
| Xóa nền giai đoạn sau | So sánh trước sau, nền trong suốt hoặc màu, xem trước và lưu bản sao |

Kích thước cửa sổ chỉnh sửa khoảng 820 × 620 DIP, có thể thay đổi kích thước. Nút hành động chính đặt cuối cửa sổ. Cửa sổ thường có thể nhận focus; menu được gọi trong drag không chiếm focus của nguồn.

## Trạng thái và dữ liệu

`Ẩn → Được gọi → Nhận file → Chọn vùng → Thả → Xếp hàng hoặc chỉnh sửa → Hoàn tất hoặc lỗi`.

Tác vụ nhanh đóng menu ngay và chạy nền. Crop cần một file; nếu kéo nhiều file, vùng crop bị vô hiệu hóa với lý do. Resize và nén cho batch dùng cùng thông số, với tùy chọn giữ tỷ lệ từng ảnh. Không tự đưa file lên mạng.

## Bản mẫu

`docs/prototype/orbit-glass.html` là bản mẫu giao diện, có dữ liệu minh họa. File này không phải ứng dụng Windows hoặc bộ chuyển đổi file. Mã native trong `src` là phần thực thi.

Mở `docs/prototype/index.html` trong trình duyệt để xem bản mẫu độc lập cùng các điều khiển chuyển phương án.
