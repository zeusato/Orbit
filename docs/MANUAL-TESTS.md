# Kiểm tra thủ công Orbit trên Windows

Đại ca có thể dùng checklist này với bản chạy trong `dist/Orbit`. Em dành riêng phần này cho các tình huống cần kiểm tra với cửa sổ thật, chuột, Explorer và cấu hình màn hình thực tế.

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
