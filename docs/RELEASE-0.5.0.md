# Orbit 0.5.0 — .NET 10, HEIC, Khung nền, âm thanh và video

## Nền tảng

- Chuyển toàn bộ sang .NET 10 LTS (SDK 10.0.402, runtime 10.0.13) vì .NET 8 hết hỗ trợ ngày 10/11/2026.
- ZIP giờ là **self-contained**: không cần cài .NET Desktop Runtime. OCR vẫn cần Microsoft Visual C++ Redistributable x64. ZIP khoảng 125 MB, giải nén khoảng 267 MB (0.4.1: khoảng 52 MB).
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

## Âm thanh và video

Dùng bộ xử lý media có sẵn của Windows (Media Foundation qua Windows.Media), không FFmpeg. Định dạng đọc được phụ thuộc codec trên máy: MP4/MOV (H.264), MP3, AAC/M4A, WAV, WMV/WMA, FLAC thường có sẵn. HEVC, VP9/WebM, AV1 cần extension từ Microsoft Store; Windows bản N cần Media Feature Pack. Trước khi hiện công cụ, Orbit dựng thử chuỗi giải mã. Nếu thiếu codec, tâm menu ghi rõ (ví dụ "Thiếu codec HEVC") và chỉ còn ZIP/SHA-256.

| Nguồn | Công cụ |
| --- | --- |
| Video (MP4, MOV, MKV/WebM, AVI, WMV) | MP4 (H.264/AAC, giữ độ phân giải), Nén video (Gốc/1080p/720p/480p × Cao/Vừa/Thấp), Cắt đoạn, tách âm thanh M4A/MP3, Khung hình → PNG, GIF ngắn |
| Âm thanh (MP3, M4A, WAV, WMA, FLAC, AAC, Ogg nếu Windows đọc được) | MP3 (192 kbps), M4A (AAC), WAV, Cắt đoạn, Ghép âm thanh (từ 2 file, theo tên) |

- **Cửa sổ chung** cho Cắt đoạn / Khung hình / GIF / Nén:
  - Khung hình xem trước là ảnh tĩnh giải mã đúng thời điểm, không phải phát video liên tục.
  - Thanh thời gian có hai tay nắm; ô nhập `m:ss.fff` hoặc số giây.
  - Nút "Đầu/Cuối = tại đây"; phím [ ] đặt đầu/cuối, mũi tên nhích 0,1 giây (Shift: 1 giây), Space phát/dừng.
  - **Phát thử** phát âm thanh của đoạn đã chọn, kèm cập nhật khung hình.
- **Cắt đoạn**: video xuất MP4 mã hóa lại, giữ độ phân giải; audio giữ MP3/M4A/WAV, định dạng khác ra M4A.
- **GIF ngắn**: tối đa 20 giây; 10/12/15 fps; rộng 320/480/640 px (không vượt video gốc); 256 màu.
- **Nén video**: áp mức độ phân giải lên cạnh ngắn, không phóng to. Dung lượng hiển thị chỉ là ước tính theo bitrate mục tiêu.
- **Giới hạn**: 2 GB và 1 giờ mỗi file, tổng ghép tối đa 1 giờ. Orbit kiểm tra ổ đĩa còn trống trước khi chạy, ghi file tạm rồi đổi tên; hủy thì xóa file tạm.
- **Hàng đợi media riêng**: video dài không chặn ảnh/PDF; tiến độ hiện trong danh sách và trên bảng góc phải.
- Gói tăng khoảng 6 MB (ZIP 125 MB) do thư viện cầu nối WinRT; app target Windows 10 2004 (19041) trở lên.
- **Chưa làm**:
  - Xoay theo metadata của video quay điện thoại: chưa kiểm chứng.
  - Phụ đề, chuẩn hóa âm lượng, giữ nguyên luồng không mã hóa lại (stream copy).
  - WebM/MKV khi máy thiếu codec.

## Menu chuột phải, nơi lưu, nhớ thiết lập, icon

- **Menu chuột phải**: chuột phải vào file → **Orbit** → submenu theo loại file. Mục đầu tiên là "Mở menu tròn Orbit…", tiếp theo là các công cụ của loại đó; ví dụ JPEG không có "Chuyển sang JPEG", MP3 có "Ghép âm thanh", PDF có "Word" và "Ghép PDF".
  - Ghi vào `HKCU\Software\Classes` của tài khoản hiện tại, không cần quyền admin. Đăng ký lại mỗi lần Orbit khởi động để luôn trỏ đúng exe; tắt trong Cài đặt thì gỡ. Chỉ gỡ các khóa do Orbit tạo, và không ghi đè mục "Orbit" của chương trình khác.
  - Windows 11: nằm trong **Hiển thị thêm tùy chọn** (Shift + chuột phải mở thẳng). Menu rút gọn mới của Windows 11 cần gói MSIX có chữ ký số nên chưa làm.
  - Chọn nhiều file: Explorer có thể mở Orbit một lần cho mỗi file. Orbit đầu tiên mở pipe chỉ cho người dùng hiện tại ngay khi khởi động; các tiến trình sau chuyển lệnh qua đó rồi thoát, và lệnh tới trong 0,4 giây được gộp thành một lượt.
  - Menu dựng theo đuôi file nên Orbit kiểm tra nội dung trước khi chạy. Sai loại, thiếu codec hoặc sai số lượng file (Crop chỉ 1 file, Ghép từ 2 file) thì báo bằng thông báo khay, không chạy.
  - Nếu xóa hoặc chuyển thư mục Orbit, hãy tắt tùy chọn này trước, nếu không menu sẽ trỏ tới exe không còn.
- **Nơi lưu kết quả** (Cài đặt): cạnh file nguồn (mặc định), thư mục con `Orbit` cạnh file nguồn (không lồng `Orbit\Orbit`), hoặc một thư mục cố định. Nơi lưu được chốt lúc tác vụ vào hàng; không ghi đè, trùng tên vẫn thêm (1), (2)…
- **Nhớ thiết lập lần trước** khi bấm Lưu: khung nền (gồm ảnh nền nếu file vẫn còn), chất lượng nén JPEG, cạnh dài và khóa tỷ lệ của Resize, tỷ lệ Crop, mức nén video, fps/độ rộng GIF. Cài đặt có nút **Đặt lại thiết lập công cụ**.
- **Icon mới**: hành tinh xanh ngọc với vòng quỹ đạo và vệ tinh hổ phách, vẽ bằng vector và render riêng 10 cỡ từ 16 đến 256 px. Dùng cho exe, cửa sổ, taskbar và menu chuột phải; icon khay được vẽ đúng kích cỡ theo DPI.

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

- Build 0 cảnh báo/0 lỗi; 100/100 kiểm thử mặc định. Phần mới được kiểm tra gồm: Registry của menu chuột phải trên một khóa thử riêng, pipe giữa hai tiến trình, gộp lệnh, nơi lưu, nhớ thiết lập trên editor thật, và icon. Ngoài ra có 14 bài audio/video trên fixture tự sinh bằng Windows (video đỏ→xanh có tiếng, WAV tone). Các bài này đo thời lượng, kích thước, màu khung hình tại thời điểm chọn, đoạn cắt, số khung GIF, thứ tự ghép, hủy không để file dở, và cửa sổ Cắt đoạn thật. Máy không có Media Foundation (Windows Server trên CI) sẽ ghi SKIP cho phần chạy media. Thanh tiến trình được kiểm tra với tác vụ OCR thật: hiện ở góc phải, không lấy focus, tự ẩn khi xong và khi cửa sổ chính ở phía trước. Có thêm bài giải mã HEIC thật khi đặt `ORBIT_HEIC_SAMPLE`. File mẫu là `example.heic` của libheif, chỉ dùng để thử trên máy, không đưa vào repo.
- Khung nền: kiểm tra từng pixel ảnh gốc được giữ 1:1, màu viền, chuyển màu, 4 họa tiết, nền ảnh có và không mờ, ảnh gốc mờ, bo góc, đổ bóng, JPEG. Editor WPF thật được kiểm tra luồng đổi tỷ lệ, chọn mẫu chuyển màu, từ chối mã màu sai và lưu ra ảnh đúng kích thước.
- Phục hồi bằng tiến trình thật (`scripts/test-updater.ps1 -RecoverOnly`): install bị trộn phiên bản được trả về 0.0.1, file mới thêm bị gỡ, file người dùng giữ nguyên.
- Đã thử menu chuột phải thật: Explorer liệt kê "Orbit ›" với submenu đúng cho JPEG và MP4; 3 tiến trình `--tool` khởi động cùng lúc khi Orbit chưa chạy cho ra 3 file và chỉ một Orbit ở khay.
- Đã chạy `dist/Orbit/Orbit.exe --render-previews` với DOTNET_ROOT trỏ sai: render giao diện, WebP, PDF, OCR và media (dựng video, nén 240p, lưu khung hình) đều đạt.
- Chưa kiểm chứng trên máy này:
  - Hai kịch bản helper có ký (nâng phiên bản và rollback): cần private key, sẽ chạy trên CI khi push.
  - Cập nhật thật từ 0.4.1 cài sẵn lên 0.5.0.
  - Ảnh HEIC chụp từ iPhone thật.
  - Khung nền ở canvas lớn gần 60 MP về thời gian và bộ nhớ.
  - Thao tác chuột trong editor Khung nền trên màn hình thật.
