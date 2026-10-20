# Kiểm tra thủ công Orbit trên Windows

Đại ca có thể dùng checklist này với bản chạy trong `dist/Orbit`. Em dành riêng phần này cho các tình huống cần kiểm tra với cửa sổ thật, chuột, Explorer và cấu hình màn hình thực tế.

## Tiện ích không kéo file

- [ ] Để Orbit ở khay, dùng ứng dụng khác: Ctrl+Space không kéo file mở vòng tại con trỏ, không hiện Home. Bấm từng ô/phím 1–6 vào đúng công cụ.
- [ ] Giữ phím Space không bật/tắt lặp. Ctrl+Space lần nữa, Esc hoặc bấm ra ngoài đóng vòng, không chạy công cụ.
- [ ] Chỉ Ctrl+Space chính xác mở tiện ích; không giữ nút chuột. Đổi phím kéo sang Shift+Space: Shift+Space khi gõ vẫn bình thường, Ctrl+Space vẫn gọi tiện ích.
- [ ] Kéo file với phím cấu hình vẫn ra vòng file; dùng xen kẽ hai vòng, nhận diện PDF chậm không thay nội dung vòng tiện ích.

- [ ] Mở từ Home và khay; gọi lại chỉ đưa cửa sổ cũ ra trước, nội dung đang nhập còn nguyên khi đổi mục.
- [ ] Tạo QR từ link dài/tiếng Việt, sao chép sang ứng dụng khác, lưu PNG rồi dùng điện thoại quét đối chiếu nội dung.
- [ ] Dán ảnh chụp QR/barcode từ Snipping Tool và ảnh copy từ trình duyệt; thử barcode bao bì thực, ảnh nghiêng/mờ và nhiều mã.
- [ ] Đang gõ text thì Ctrl+V dán chữ; vùng đọc mã nhận ảnh. Clipboard không có nội dung phù hợp báo rõ.
- [ ] Ảnh → Base64 và ngược lại, raw/data URI; lưu vào tên file đã có phải tạo bản sao có số. Thử alpha, JPEG và ảnh sát giới hạn 8 MB.
- [ ] JSON lỗi không còn kết quả cũ; số rất lớn không bị làm tròn. Bỏ dòng trùng giữ dòng xuất hiện trước, phân biệt hoa/thường.
- [ ] Chuỗi dán quá dài bị từ chối, không cắt ngắn rồi xử lý; đóng khi đang đọc mã không báo lỗi hoặc mở lại cửa sổ.
- [ ] Kiểm tra độ tương phản, thanh cuộn, bàn phím và bố cục tại DPI 125–200%, cửa sổ nhỏ.

## PDF: tạo nhiều trang, ghép và tách

- [ ] Chụp ảnh bằng Snipping Tool, mở Home và Ctrl+V: menu ảnh xuất hiện, chưa tự chạy tác vụ. Clipboard trống/chỉ có chữ báo rõ.
- [ ] Ctrl+V trong composer thêm một trang; đang gõ tên/đường dẫn thì dán chữ vào ô đó. Clipboard từ trình duyệt và ảnh có alpha hiển thị đúng.
- [ ] Đổi tên/nơi lưu ở ghép, tách chung và tách riêng; kiểm tra đuôi .pdf, thư mục trang và trùng tên không ghi đè.
- [ ] Chọn Nén PDF, thử chất lượng 30/75/95 trên scan và PDF có chữ; đối chiếu dung lượng thật, vẫn chọn/copy chữ được. PDF đã tối ưu có thể báo không giảm; bản gốc giữ nguyên.
- [ ] Mở lại Orbit: tác vụ nén vẫn có dung lượng trước/sau. Thử PDF có chữ ký số: báo từ chối, không có đầu ra dở.

- [ ] Thả một ảnh vào ô PDF trên vòng menu: hiện lựa chọn Chuyển ngay / Thêm trang; đóng hoặc Esc không tạo file.
- [ ] Chuyển ngay tạo một PDF một trang; chọn nhiều ảnh tạo PDF riêng theo từng ảnh.
- [ ] Thêm trang mở editor có ảnh đã chọn; thêm ảnh và PDF bằng nút chọn file và kéo thả từ Explorer.
- [ ] Lên/Xuống đổi thứ tự file; bỏ mục chỉ thay danh sách. PDF xuất giữ thứ tự đó, tỷ lệ ảnh và nội dung chữ của PDF.
- [ ] Chọn một PDF hoặc trộn PDF/ảnh: vòng menu có Ghép PDF; thêm một PDF/ảnh khác rồi xuất được.
- [ ] Tách trang `1-3, 5`: bật xuất riêng tạo đúng bốn file mang số trang nguồn; tắt tạo một PDF bốn trang. Thử `5, 1` để kiểm tra thứ tự.
- [ ] Trang 0, trang vượt số trang, khoảng ngược bị chặn. Để trống lấy tất cả.
- [ ] Dùng PDF dọc/ngang/xoay, ảnh HEIC thật, tên file dài; preview đúng tỷ lệ và điều hướng từng trang.
- [ ] Thêm file lỗi, quá 128 file hoặc quá 200 trang: thông báo lỗi, danh sách cũ giữ nguyên. Đóng khi đang tải/preview không tạo tác vụ.
- [ ] Thử DPI 125–200%, theme tối, cửa sổ thu nhỏ; kéo thả chỉ báo Copy và không ảnh hưởng nguồn.

## Luồng kéo thả cốt lõi

- [ ] Mở Orbit không dùng administrator; kéo một JPEG trong Explorer, bấm Ctrl và Space khi vẫn giữ chuột, di chuyển vào PNG rồi thả.
- [ ] PNG xuất trong thư mục nguồn, mở được; JPEG vẫn còn nguyên byte.
- [ ] Lặp lại từ Desktop và khi Explorer đang ở màn hình phụ.
- [ ] Gọi menu rồi thả ngay, không di chuyển thêm: không có tác vụ.
- [ ] Thả ở tâm, khe phân đoạn, ngoài vòng: không chạy công cụ Orbit. Thao tác thả ngoài Orbit trở lại hành vi của ứng dụng bên dưới.
- [ ] Esc đóng menu và hủy native drag; không để vòng treo trên màn hình.
- [ ] Ctrl kéo file khi không bấm Space vẫn hoạt động theo Windows.
- [ ] Ctrl Space khi không giữ chuột không mở menu.
- [ ] Đổi tổ hợp trong Cài đặt rồi kiểm tra tổ hợp cũ và mới.
- [ ] Kéo file không phải ảnh hoặc thư mục có đuôi ảnh: không làm hỏng nguồn, báo không hỗ trợ hoặc lỗi thích hợp.
- [ ] Nguồn chỉ cho phép Move bị từ chối; Orbit không báo Move.

## Bộ công cụ

- [ ] 0.5.0: mở ZIP 0.5.0 trên máy không cài .NET; OCR, WebP, PDF chạy được.
- [ ] 0.5.0: bản 0.4.1 đang chạy tự cập nhật lên 0.5.0; sau đó Cập nhật Orbit vẫn kiểm tra được bản kế tiếp.
- [ ] 0.5.0: ảnh HEIC chụp từ iPhone (dọc, ngang, Live Photo) mở đúng chiều, xuất JPEG/PNG đúng.
- [ ] 0.5.0: Khung nền: kéo slider, đổi tỷ lệ, 5 kiểu nền, chọn ảnh nền, bo góc/bóng; Lưu ra đúng như xem trước; batch nhiều ảnh khác tỷ lệ.
- [ ] 0.5.0: Khung nền trên ảnh lớn (~40–60 MP): thời gian, bộ nhớ, thông báo khi vượt giới hạn.
- [ ] 0.5.0: tắt Orbit, mở lại: lịch sử tác vụ còn; Xóa lịch sử không xóa file kết quả.
- [ ] 0.5.0: chuột phải ảnh/PDF/Office/MP4/MP3 → Orbit: submenu đúng loại; chọn 10 ảnh → một lượt; Orbit chưa chạy vẫn xử lý; "Mở menu tròn" hiện vòng tại con trỏ; tắt trong Cài đặt thì mục biến mất.
- [ ] 0.5.0: Nơi lưu = thư mục con/thư mục cố định với PDF tách trang, ghép âm thanh, Office → PDF; thư mục cố định trên ổ đã rút báo lỗi rõ.
- [ ] 0.5.0: icon rõ ở taskbar, Alt+Tab, khay (100–200% DPI), Explorer (exe) và menu chuột phải.
- [ ] 0.5.0: video điện thoại (dọc, HEVC nếu có extension, có xoay), MOV, MKV/WebM, WMV; MP3/M4A/FLAC/WMA: menu đúng công cụ, thiếu codec báo rõ.
- [ ] 0.5.0: Cắt đoạn kéo tay nắm, Phát thử nghe đúng đoạn, lưu đúng thời lượng; GIF 20 giây; Khung hình đúng thời điểm; Nén 720p/480p trên video 1080p/4K; Ghép nhiều MP3.
- [ ] 0.5.0: video dài gần 1 giờ: thời gian xử lý, Hủy giữa chừng không để file .orbit-*, ảnh/PDF vẫn chạy song song.
- [ ] 0.5.0: kéo PDF scan vào Lấy chữ khi đang dùng app khác: bảng tiến trình hiện góc phải, không cướp focus, Hủy dừng tác vụ, bảng biến mất khi xong; với nhiều màn hình/DPI bảng nằm đúng góc màn hình chính.

- [ ] 0.3.2: cuộn zoom tại con trỏ; 100%/vừa khung; pan bằng Space + kéo/chuột giữa trên crop; kéo ảnh trên resize/nén.
- [ ] 0.3.2: Shift + kéo vẽ vùng mới khi đang zoom; kéo tám tay nắm với tỷ lệ khóa; đầu ra đúng vùng đã chọn.
- [ ] 0.3.2: gõ ô crop rồi Enter/lưu ngay; số sai chặn lưu; preset Tự do giữ vùng cắt hiện tại.
- [ ] 0.3.2: preview nén đổi theo slider, xem gốc, đóng khi đang tạo preview không có lỗi.
- [ ] 0.3.2: giữ Thêm để đổi trang khi kéo; rê tiếp rồi thả; thả PDF đang nhận diện giữ menu chờ chọn; hủy không chạy tác vụ.

- [ ] Chọn ảnh trực tiếp từ cửa sổ chính và bấm PNG hoặc JPEG.
- [ ] Trùng tên đầu ra tạo tên có số; không hỏi ghi đè hoặc sửa file cũ.
- [ ] Resize một ảnh dọc và một ảnh ngang cùng batch, giữ tỷ lệ từng ảnh.
- [ ] Crop bằng kéo vùng, kéo góc, preset và các ô tọa độ; kiểm tra ảnh đầu ra đúng vùng.
- [ ] Xoay ảnh có EXIF orientation và kiểm tra hướng cuối cùng.
- [ ] Nén ảnh có alpha xuất JPEG nền trắng; chất lượng có thể làm kích thước tăng với một số nguồn.
- [ ] Hủy queued job và running job. Không có đầu ra dở trong luồng hủy bình thường.
- [ ] File hỏng, không còn tồn tại, bị khóa, thư mục không ghi được: hiện lỗi trong hàng đợi, không chặn file hợp lệ tiếp theo.
- [ ] Đóng cửa sổ chính trong khi xử lý vẫn chạy; chọn Thoát trong khay khi còn tác vụ có yêu cầu xác nhận hủy.

## Giao diện và môi trường

- [ ] Sáng, tối, nền đặc và high contrast; chữ và số trong input vẫn đọc được.
- [ ] DWM backdrop thực tế; không chỉ dựa vào mã trả về thành công của API.
- [ ] 100, 125, 150 và 200 phần trăm DPI; không lệch giữa vùng sáng và vùng thả.
- [ ] Hai màn hình khác DPI, màn hình bên trái có tọa độ âm, cạnh và góc work area.
- [ ] Lần mở thứ hai đưa cửa sổ instance đang chạy ra trước, không tạo hai biểu tượng khay.
- [ ] Startup chỉ bật khi người dùng chọn; chạy lại ở khay với `--background`.
- [ ] Kiểm tra xung đột phím với bộ gõ và các ứng dụng thường dùng.

## Đo hiệu năng

Ghi cấu hình máy, số file và kích thước ảnh khi đo. Phân biệt thời gian hiện menu, đọc file, chuyển đổi và ghi đĩa. Mục tiêu dưới 100 ms cho menu chưa được coi là đạt nếu chưa có kết quả đo. Kiểm tra CPU khi để nền và bộ nhớ với ảnh lớn, batch và nhiều cửa sổ chỉnh sửa.
