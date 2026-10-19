# Orbit 0.5.0

Đại ca có thể xử lý ảnh, PDF và tài liệu Office tại máy bằng menu tròn. Em đã sửa nền vuông ngoài vòng, thêm nhận dạng nội dung file và WebP. File không được tải lên dịch vụ bên ngoài.

## Chạy ứng dụng

Yêu cầu Windows 11 x64; từ 0.5.0 bản ZIP đã kèm .NET 10 (self-contained), không cần cài .NET riêng. OCR cần Microsoft Visual C++ Redistributable x64 (2015–2022). Mở `dist/Orbit/Orbit.exe` hoặc `scripts/run.ps1`. Bản ZIP: `dist/Orbit-0.5.0-win-x64.zip` (khoảng 119 MB). Giải nén và giữ nguyên cả thư mục, gồm helper cập nhật, `tessdata` và `x64`; không gửi riêng EXE. Bản 0.3.x cần nâng lên 0.4.0 thủ công một lần.

## Cập nhật

Tải bản portable tại [GitHub Releases](https://github.com/zeusato/Orbit/releases/latest). Để phát hành bản mới: tăng `Version` trong `src/Orbit.App/Orbit.App.csproj`, thêm `docs/RELEASE-X.Y.Z.md`, commit và push `main`. GitHub Actions tự build/test/ký, tạo tag và publish release; không cần bấm nút. Phiên bản đã phát hành sẽ được bỏ qua, không ghi đè.

Từ 0.4.0, Orbit mặc định kiểm tra GitHub Releases `zeusato/Orbit` mỗi 12 giờ, tải ZIP mới và tự áp dụng khi ở khay, không có cửa sổ/tác vụ/kéo thả. Cài đặt cho phép tắt; menu khay và Cài đặt có **Cập nhật Orbit** để kiểm tra, áp dụng ngay hoặc hoãn 4 giờ. Gói phải qua kiểm tra chữ ký riêng và SHA-256; helper giữ backup và khôi phục khi bản mới lỗi khởi động. Chưa có certificate Authenticode, Windows vẫn có thể cảnh báo EXE portable. Chi tiết phát hành, cache/backup và giới hạn phục hồi: [UPDATES.md](docs/UPDATES.md).

Dấu X thu ứng dụng về khay; nhấp đúp biểu tượng khay để mở lại. Chuột phải chọn Thoát Orbit để kết thúc. Không cần quyền administrator. Bản này không dùng bộ cài; gói cập nhật có chữ ký riêng của Orbit, EXE chưa có chữ ký Authenticode.

Lần mở đầu, Orbit hỏi **Chạy cùng Windows?** Chọn **Bật cùng Windows** để tự chạy ở khay sau khi đăng nhập, không mở cửa sổ chính; không cần quyền administrator. **Không bật**, X hoặc Esc sẽ bỏ qua và không hỏi lại. Có thể đổi trong Cài đặt. Phiên đã bật startup không hỏi lại; phiên tự khởi động và chế độ kiểm thử không hiện lời hỏi. Bản cũ chưa từng chọn sẽ được hỏi một lần khi mở bản mới. Giữ thư mục Orbit tại vị trí đăng ký; nếu chuyển thư mục, tắt rồi bật lại startup trong Cài đặt.

## Menu tròn

1. Kéo file từ Explorer hoặc Desktop, giữ chuột trái và bấm Ctrl + Space. Trong Cài đặt → Phím gọi menu khi đang kéo, chọn Ctrl + Space, Ctrl + Shift + Space hoặc Shift + Space. Lưu để áp dụng ngay, không cần khởi động lại. Cấu hình cũ được giữ khi nâng cấp.
2. Sau khi menu xuất hiện, di chuyển con trỏ ít nhất khoảng 12 DIP để kích hoạt vùng chọn.
3. Thả vào công cụ cần dùng. Đổi định dạng chạy ngay; resize, crop và nén mở cửa sổ thông số.

Cũng có thể chọn file hoặc thả file vào cửa sổ chính rồi bấm công cụ. Menu nhận dạng nội dung và chỉ hiện công cụ phù hợp: JPEG không có lựa chọn chuyển sang JPEG. Tối đa sáu ô mỗi trang; giữ trên **Thêm…** khoảng 650 ms để sang trang mà vẫn kéo file, rồi rê sang công cụ muốn thả. Nếu thả vào Thêm…, bấm tiếp công cụ ở trang sau. Khi file đang nhận diện, có thể thả để giữ menu mở rồi chọn sau; chưa chạy tác vụ nào. Phím 1–6 chọn ô theo chiều kim đồng hồ từ trên cùng; Esc hủy. Vùng tâm và khe giữa các ô khi menu đã sẵn sàng không chạy tác vụ.

## Chỉnh ảnh

- Crop, resize và nén: cuộn để zoom tại con trỏ; nút **Vừa khung** và **100%** đổi mức xem. Resize/nén kéo ảnh để pan; crop dùng **Space + kéo** hoặc chuột giữa.
- Crop: kéo tám tay nắm hoặc bên trong vùng; **Shift + kéo** vẽ vùng mới. Preset tỷ lệ có đánh dấu; **Tự do** giữ nguyên vùng khi bỏ khóa. Mũi tên dịch 1 px, Shift + mũi tên dịch 10 px. Ô tọa độ sai chặn lưu; Enter lưu giá trị mới nhất; **Đặt lại vùng cắt** trở về toàn ảnh.
- Resize có nút kích thước gốc và thông tin đầu ra. Nén có xem trước JPEG theo chất lượng, checkbox ảnh gốc để so; preview tối đa 1600 px, file xuất vẫn giữ nguyên độ phân giải. Không ước đoán dung lượng từ preview.

Vòng kính chỉ vẽ bên trong hình tròn. Nền mờ là ảnh chụp trong RAM tại thời điểm mở menu, không cập nhật live; ảnh nền được bỏ khi đóng menu. Cửa sổ chính dùng Desktop Acrylic của Windows.

## Công cụ hiện có

| Nguồn | Thao tác |
| --- | --- |
| JPEG, PNG, WebP, BMP, TIFF, GIF tĩnh, ICO, AVIF, HEIC/HEIF, TGA | Chuyển PNG/JPEG/WebP/BMP/TIFF/GIF/ICO/PDF; resize, crop, xoay 90°, nén JPEG, khung nền; OCR → TXT |
| PDF có lớp chữ | Word: DOCX sửa được, giữ dòng/ngắt trang cơ bản, không phục dựng bảng/hình; không cần cài Word |
| PDF, gồm scan và trang trộn | Lấy chữ → TXT UTF-8; đọc lớp chữ hoặc OCR Việt/Anh theo từng trang; không giữ trình bày |
| PDF | Xuất tất cả trang thành PNG/JPEG 150 DPI; tách trang thành từng PDF; ghép nhiều PDF theo tên file |
| DOC/DOCX, XLS/XLSX, PPT/PPTX | Xuất PDF, cần Microsoft Word/Excel/PowerPoint tương ứng đã cài |
| DOCX | Trích văn bản thân tài liệu thành TXT UTF-8, không cần Word |
| File bất kỳ | Tạo ZIP riêng cho từng file; lưu mã SHA-256 |

Chọn từ hai PDF để hiện Ghép PDF. Batch tối đa 128 file và chỉ hiện các công cụ chung; crop chỉ nhận một ảnh. WebP xuất lossless và giữ alpha; JPEG ghép nền trắng; GIF có tối đa 256 màu; ICO thu về tối đa 256 px. AVIF, HEIC/HEIF và TGA là định dạng đầu vào. HEIC giải mã bằng libheif đi kèm, không cần HEIF Extension của Windows; file có nhiều ảnh lấy ảnh chính, HEIF chuỗi/ảnh động bị từ chối. Ảnh động và TIFF nhiều trang chưa được hỗ trợ.

**Khung nền** mở rộng canvas quanh ảnh (khoảng viền 0–40% cạnh dài, tỷ lệ Theo ảnh/1:1/4:5/16:9/9:16) và tô nền: màu, chuyển màu, họa tiết (chấm bi, sọc chéo, ô lưới, giấy), ảnh khác (có thể làm mờ) hoặc chính ảnh gốc làm mờ. Có bo góc, đổ bóng, xuất PNG hoặc JPEG 92%. Ảnh gốc giữ nguyên từng pixel; Orbit không tách hay sửa nền bên trong ảnh. Đầu ra `_frame` tối đa 60 megapixel.

## File đầu ra và giới hạn

Kết quả nằm cạnh file nguồn. Khi trùng tên, Orbit thêm `(1)`, `(2)`…; không ghi đè, di chuyển hoặc xóa nguồn. PDF xuất nhiều trang tạo thư mục `_images` hoặc `_pages`. Có thể mở kết quả/thư mục từ hàng tác vụ.

Ảnh áp dụng EXIF orientation trước khi xuất và không sao chép metadata. Codec mở rộng dùng 8 bit/kênh, không cam kết giữ HDR/16 bit. Giới hạn 256 MB/file, 60 megapixel/ảnh, 32768 px/cạnh và 200 trang/tác vụ PDF. Thư mục nguồn cần có quyền ghi.

Menu phân tích từng trang PDF trước khi hiện Word. Có trang scan thì chỉ hiện Lấy chữ; TXT đánh dấu số trang và trang dùng OCR. PDF đã có lớp OCR đọc được sẽ dùng lớp chữ đó, không tự sửa lỗi có sẵn. Thứ tự đọc PDF nhiều cột có thể sai; DOCX dùng Arial 11 và khổ A4, không cam kết bố cục giống bản gốc. PDF → Word ở đây là xuất nội dung chữ sửa được, không phải bản sao hình thức của PDF.

OCR chạy hoàn toàn cục bộ bằng mô hình tiếng Việt và Anh, không tự đoán/sửa nội dung bằng AI. Có nhắc đối chiếu dấu, tên và số; trang có độ tin cậy thấp hoặc không nhận được chữ có thông báo rõ trong TXT. Điểm tin cậy không phải phần trăm chính xác. Hiện đã kiểm thử chữ in rõ; chưa nghiệm thu tài liệu thực tế đa dạng, chữ viết tay hoặc scan mờ. Ảnh dọc dài được chia vùng đọc có phần chồng; tiến độ và hủy có hiệu lực giữa các vùng. PDF không còn bị thu cạnh dài về 4.000 px; giới hạn render là 48 megapixel và 30.000 px/cạnh. Vẫn còn lỗi dấu, số và thứ tự đọc cột trên mẫu HPG thực tế; đây là bản sửa mất nội dung, chưa chứng minh OCR đạt độ chính xác mong muốn.

Office → PDF mở tài liệu chỉ đọc, tắt macro trong phiên xử lý rồi khôi phục thiết lập. Tác vụ có thể phải đợi COM trả về trước khi hủy; tài liệu có mật khẩu, Protected View hoặc Office chưa kích hoạt có thể báo lỗi hoặc chờ Office. Chưa có PDF → Excel, phục dựng bảng/hình PDF → Word, xóa/thay nền AI hoặc audio/video.

## Phát triển và kiểm thử

SDK .NET 10.0.402 theo `global.json`; NuGet lấy từ nuget.org. Cache CLI/package nằm trong dự án.

```powershell
.\scripts\prepare-ocr.ps1
.\scripts\build.ps1 -Test
.\scripts\build.ps1 -Test -OfficeTest
.\scripts\package.ps1
.\scripts\run.ps1
```

72 kiểm thử mặc định; thêm 4 kiểm thử Office khi dùng `-OfficeTest`. Đặt `ORBIT_HEIC_SAMPLE` trỏ tới một ảnh HEIC thật để chạy thêm bài giải mã HEIC (không có encoder HEIC để tự tạo mẫu). `scripts/test-updater.ps1` kiểm tra helper bằng tiến trình thật (success/rollback cần khóa ký cục bộ; `-RecoverOnly` chỉ chạy kịch bản phục hồi cập nhật bị ngắt, không cần khóa); fixture chỉ dùng phiên bản 0.0.1/0.0.2 và không đụng app đang dùng. Kết quả ở `artifacts/tests` và `artifacts/updater-tests`. Thư viện và giấy phép phân phối nằm trong `third-party`; model OCR tải bằng script và không nằm trong Git. Khi clone mới, chạy `prepare-ocr.ps1` trước build.

`--render-previews <thư mục tuyệt đối>` tạo ảnh giao diện và kiểm tra codec WebP/PDF và khởi tạo OCR trong bản chạy. Render WPF không chứng minh backdrop native hoặc kéo thả Explorer. `--no-integrations` tắt tray/hook toàn cục cho phiên kiểm thử. Cài đặt mặc định ở `%LOCALAPPDATA%\Orbit\settings.json`; `ORBIT_DATA_DIR` đổi vị trí cho phiên thử. Lịch sử tác vụ đã xong lưu ở `history.json` cùng thư mục (tối đa 200 dòng, chỉ đường dẫn và kết quả); nút **Xóa lịch sử** trên cửa sổ chính chỉ xóa danh sách. Phiên `--no-integrations`/`--render-previews` không ghi lịch sử. `Orbit.Tests.exe --frame-samples <ảnh> <thư mục>` xuất mẫu các kiểu Khung nền để xem bằng mắt.

Chi tiết kiểm chứng và giới hạn: [bản 0.5.0](docs/RELEASE-0.5.0.md). OCR trên mẫu HPG thực tế vẫn còn lỗi dấu và số. Chưa nghiệm thu đa màn hình/DPI cao hoặc mọi trường hợp kéo thả.

Từ 0.5.0 dùng .NET 10 LTS, thay .NET 8 hết hỗ trợ ngày 10/11/2026, theo [chính sách Microsoft](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).
