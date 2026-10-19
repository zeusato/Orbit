# Kế hoạch triển khai Orbit

Âm thanh/video, Đại ca chốt ngày 19/10/2026; đã triển khai trong 0.5.0 (xem RELEASE-0.5.0.md), FFmpeg vẫn để sau:
- **Công nghệ**: dùng bộ xử lý media có sẵn của Windows (Windows.Media.Transcoding/MediaComposition trên Media Foundation), không FFmpeg ở vòng đầu. Trước khi hiện công cụ, Orbit thử mở file; thiếu codec (HEVC, VP9/AV1, Windows N) thì báo cần cài gì.
- **Công cụ vòng đầu**:
  - Video → MP4 (H.264/AAC) và nén theo preset 1080p/720p/480p.
  - Audio ↔ MP3/M4A/WAV; tách âm thanh từ video.
  - Cửa sổ Cắt đoạn có xem trước, dùng chung cho cắt video/audio, chụp khung hình → PNG và GIF ngắn (≤ 20 giây).
  - Ghép nhiều file audio theo tên.
- **Giới hạn**: 2 GB và 1 giờ mỗi file. Kiểm tra ổ đĩa còn trống trước khi chạy; ghi file tạm rồi đổi tên; hủy thì xóa file tạm.
- **Nền tảng**:
  - Hàng đợi media riêng để không chặn ảnh/PDF; dùng bảng tiến trình góc phải.
  - Nhận dạng theo nội dung file (ftyp, EBML, RIFF, ASF, ID3, fLaC, OggS).
  - Fixture kiểm thử tự sinh bằng Windows, không tải mẫu.
- **Thứ tự**: giai đoạn 1 gồm nền tảng và các chuyển đổi chạy ngay; giai đoạn 2 gồm cửa sổ Cắt đoạn, khung hình, GIF và ghép; FFmpeg LGPL để sau, tùy nhu cầu.

Cập nhật 0.5.0 theo chốt của Đại ca:
- Nâng .NET 10 LTS, đóng gói self-contained.
- Bật HEIC.
- Thay "xóa/thay nền AI" bằng **Khung nền**: mở rộng canvas và tô nền màu/chuyển màu/họa tiết/ảnh; không can thiệp vào nền bên trong ảnh.
- Tự phục hồi khi cập nhật bị ngắt, tự dọn backup/bản tải, lưu lịch sử tác vụ.
- Bỏ qua bộ đo độ chính xác OCR ở vòng này.
- Âm thanh/video bàn tiếp sau khi các mục trên ổn định; đề xuất bước 1 dùng Media Foundation, FFmpeg LGPL là tùy chọn.
- Authenticode cần mua chứng chỉ nên chưa làm.

Cập nhật 0.4.0: thêm cập nhật portable từ GitHub Releases `zeusato/Orbit`. Manifest ký bằng khóa riêng, client kiểm tra trước khi tải/áp dụng; helper backup/rollback, không installer hoặc model mới. Push lên main với số phiên bản mới sẽ tự build/test/ký, tạo tag và publish release. Xem [UPDATES.md](UPDATES.md).

Cập nhật 0.3.2: ưu tiên UX các công cụ đang có, giữ ứng dụng nhỏ. Crop có viewport zoom/pan, tay nắm cạnh/góc và xác thực vùng cắt trước lưu. Resize/nén dùng chung viewport; nén có xem trước JPEG giới hạn kích thước. Menu kéo thả bỏ yêu cầu di chuyển lần hai sau nhận diện, hỗ trợ chờ nhận diện và giữ trên Thêm để chuyển trang. Không tích hợp Python/PyTorch/VietOCR theo quyết định của Đại ca.

Cập nhật 0.3.1: sửa lỗi thu nhỏ PDF dài gây mất chữ và chia vùng OCR. Mẫu HPG thực tế đã cho thấy giới hạn nhận dạng dấu/số của engine hiện tại; chưa coi chất lượng OCR là đã nghiệm thu. Bước chất lượng tiếp theo phải đánh giá nội dung đúng/sai trên file thực tế, không lấy số bài test hay số ký tự thay cho độ chính xác.

Cập nhật 0.3 theo yêu cầu Đại ca: PDF có chữ xuất DOCX chỉnh sửa được với dòng/trang cơ bản; scan xuất TXT bằng OCR Việt/Anh, không phục dựng trình bày. PDF trộn đọc từng trang bằng phương pháp phù hợp. Tiếp theo cần đánh giá trên tập tài liệu thực tế trước khi tuyên bố chất lượng OCR; xem [RELEASE-0.3.md](RELEASE-0.3.md).

Cập nhật 0.2: Đại ca yêu cầu ưu tiên WebP, menu theo loại file, PDF và Office. Các phần này đã được triển khai; xem [RELEASE-0.2.md](RELEASE-0.2.md) cho phạm vi hiện tại. Lộ trình bên dưới lưu lại kế hoạch ban đầu.

Orbit là công cụ Windows chạy ở khay hệ thống, giúp Đại ca xử lý file ngay trong thao tác kéo thả. Em chọn menu vòng phân đoạn, phong cách glassmorphism, xử lý cục bộ và giữ nguyên file nguồn. Ngày chốt kế hoạch: 19 tháng 10 năm 2026.

## Phạm vi bản đầu

Ứng dụng native cho Windows 11, nhận file ảnh cục bộ từ Explorer hoặc Desktop. Khi đang kéo, bấm Ctrl và Space để gọi menu. Di chuyển tới một ô và thả để chuyển đổi hoặc mở cửa sổ chỉnh sửa. Có đường vào thay thế bằng chọn file hoặc thả vào cửa sổ chính để công cụ vẫn dùng được khi phím gọi xung đột.

Bản đầu có PNG, JPEG, resize, crop, xoay 90 độ và nén JPEG. Sáu vị trí giữ cố định. WebP và xóa hoặc thay nền nằm ở giai đoạn sau: không hiển thị như công cụ đã hoạt động khi chưa có bộ xử lý. Cửa sổ xem trước trong tài liệu thiết kế mô tả cả các tính năng tương lai.

Mọi đầu ra nằm cạnh file nguồn, có hậu tố khi cần và tự thêm số khi trùng tên. Chỉ báo sao chép cho nguồn kéo thả; không báo di chuyển. Ghi file tạm trước, công bố file hoàn chỉnh sau khi mã hóa thành công. Thư mục không ghi được phải báo lỗi rõ ràng.

## Lộ trình

| Giai đoạn | Dự kiến | Đầu ra và điều kiện hoàn thành |
| --- | --- | --- |
| Kiểm chứng native | 3 đến 5 ngày | JPEG từ Explorer, phím gọi khi kéo, nhận dữ liệu OLE, PNG cạnh nguồn; kiểm tra hủy và vùng thả |
| Ứng dụng nền | 1 tuần | Khay hệ thống, menu sáu vùng, cài đặt phím, DPI và vật liệu kính có nền đặc dự phòng |
| Bộ xử lý ảnh | 1 đến 2 tuần | Chuyển đổi, resize, nén, xoay, hàng đợi, không ghi đè, xử lý lỗi riêng từng file |
| Chỉnh sửa | 1 tuần | Crop và các cửa sổ thông số, preview, lịch sử tác vụ và batch cho công cụ phù hợp |
| Bản dùng thử | 1 tuần | Đóng gói, đo tài nguyên, thử nhiều màn hình và máy, tài liệu sử dụng |
| Mở rộng | 1 đến 2 tuần | WebP, xóa nền bằng mô hình chạy cục bộ, so sánh và thay nền |

Ước lượng 6 đến 8 tuần cho bản dùng thử có phần mở rộng, với một lập trình viên có kinh nghiệm Windows desktop. Đây là khung lập kế hoạch, không phải kết quả hiệu năng đã đo.

## Kiến trúc

- `Orbit.Core`: hợp đồng tác vụ, định vị vùng menu, quy tắc tên file. Không phụ thuộc giao diện.
- `Orbit.App`: WPF, Win32, khay hệ thống, cửa sổ, hook bàn phím, OLE drag and drop, bộ xử lý ảnh WIC, hàng đợi.
- `Orbit.Tests`: bộ kiểm chứng thực thi không cần framework kiểm thử ngoài; kiểm tra dữ liệu đầu ra và các quy tắc bảo vệ file.

Bản phát triển dùng .NET 8 có sẵn trên máy để build ngay, không cần tải gói xử lý ảnh. Trước phát hành rộng rãi chuyển sang .NET LTS còn thời hạn hỗ trợ dài và bản vá mới. .NET 8 kết thúc hỗ trợ ngày 10 tháng 11 năm 2026 theo [chính sách Microsoft](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

WIC cung cấp JPEG, PNG, BMP, TIFF và GIF tĩnh đầu vào. Bản đầu từ chối ảnh nhiều frame để tránh làm mất animation hoặc các trang mà người dùng không biết. WebP cần encoder bổ sung. Xóa nền cần mô hình, kiểm tra giấy phép, chất lượng, bộ nhớ và thời gian tải riêng.

## Tiêu chí nghiệm thu

- Phím gọi chỉ mở menu khi chuột trái đang kéo đủ xa. Không xử lý ảnh trong hook.
- Chỉ nhận file thật và hiệu ứng Copy. Vùng tâm, khe phân đoạn và file không hỗ trợ không chạy tác vụ.
- Không làm thay đổi hash của file nguồn. Không ghi đè file đầu ra có sẵn kể cả khi có tác vụ đồng thời.
- Đầu ra giải mã được, đúng định dạng và kích thước, có xử lý EXIF orientation.
- Tác vụ chạy ngoài UI; hủy không để lại file xuất dở. Một file lỗi không chặn các file khác.
- Kiểm tra 100, 125, 150 và 200 phần trăm DPI; màn hình phụ, tọa độ âm và mép màn hình.
- Mục tiêu phản hồi menu dưới 100 ms khi tiến trình đã sẵn sàng; ghi kết quả đo trước khi tuyên bố đạt.
- CPU nền gần 0 khi không có thao tác; không quét thư mục hoặc poll chuột liên tục.

## Nguồn kỹ thuật

- [WPF drag and drop](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/drag-and-drop-overview)
- [OLE DragEnter và hiệu ứng Copy](https://learn.microsoft.com/en-us/windows/win32/api/oleidl/nf-oleidl-idroptarget-dragenter)
- [Low level keyboard hook](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)
- [Acrylic](https://learn.microsoft.com/en-us/windows/apps/design/style/acrylic)
- [DWM system backdrop](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)
