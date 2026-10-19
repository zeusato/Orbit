# Orbit 0.4.0 — cập nhật portable

Thêm tự cập nhật từ GitHub Releases `zeusato/Orbit`, giữ bản ZIP portable. Không cần Git hay token trên client; chưa có chữ ký Authenticode, Windows vẫn có thể cảnh báo.

- Mặc định tự kiểm tra mỗi 12 giờ, tải nền và chờ Orbit ở khay, không còn editor/tác vụ/kéo thả. Có tùy chọn tắt, Kiểm tra ngay, Cập nhật ngay và Để sau 4 giờ.
- Kiểm tra chữ ký RSA-PSS/SHA-256, hash/kích thước ZIP, đúng version và đường dẫn giải nén. Chặn downgrade, không ghi settings của người dùng.
- Helper riêng chờ app thoát, sao lưu trước khi thay file, mở bản mới và yêu cầu xác nhận khởi động. Lỗi copy/khởi động thì khôi phục bản cũ. Có journal và log; mất điện giữa lúc cập nhật có thể cần phục hồi thủ công.
- Có công cụ ký và workflow tự tạo tag, publish release sau khi build/test thành công. Chỉ cần tăng phiên bản, viết release notes và push main; phiên bản đã phát hành được bỏ qua, không ghi đè.
- Không thêm thư viện ngoài hoặc model mới. Tải cả ZIP; cache và backup được giữ để phục hồi, chưa tự dọn.

Bản 0.3.x cần tải và giải nén 0.4.0 thủ công một lần vì chưa có updater. Từ 0.4.0 trở đi mới dùng luồng cập nhật tự động.

Kiểm thử: 57/57 bộ hồi quy, cùng hai thử nghiệm helper bằng tiến trình thật (nâng phiên bản và rollback lỗi khởi động), trước vòng đóng gói cuối. Chưa thử qua proxy doanh nghiệp/antivirus, thư mục mạng hoặc mất điện. Chi tiết vận hành tại [UPDATES.md](UPDATES.md).
