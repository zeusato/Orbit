# Cập nhật portable qua GitHub Releases

Nguồn cố định: `https://github.com/zeusato/Orbit`. Client không cần Git, tài khoản GitHub hay token. ZIP vẫn framework-dependent: Windows 11 x64, .NET 8 Desktop Runtime x64; không có installer và không có Authenticode certificate.

## Trên client

- Từ 0.4.0, mặc định kiểm tra sau 30 giây mở app, sau đó tối đa mỗi 12 giờ (kể cả khi lần trước lỗi). Có thể tắt trong Cài đặt. Bản 0.3.x phải nâng lên 0.4.0 thủ công một lần.
- Tự tải stable release mới hơn theo số phiên bản; bỏ draft/prerelease. Lưu phiên bản tải sẵn và lựa chọn Để sau qua lần mở lại. Thông báo nằm ở Cài đặt → Cập nhật Orbit hoặc menu khay.
- Chỉ tải qua HTTPS từ GitHub/CDN GitHub; kiểm tra RSA-PSS/SHA-256 bằng public key nhúng, rồi kiểm tra SHA-256 và kích thước ZIP. Kiểm tra lại trong helper trước áp dụng; chặn downgrade và phiên bản assembly không khớp manifest.
- Tự áp dụng khi ở khay, mọi cửa sổ đã đóng, không có job/editor/menu/chuột kéo. Cập nhật ngay vẫn phải chờ công cụ đang xử lý và editor đóng. Để sau hoãn 4 giờ.
- Helper chạy từ bản sao trong `%LOCALAPPDATA%/Orbit/updates/<id>/runner`, xác nhận đúng PID/thời điểm khởi động/đường dẫn tiến trình Orbit rồi mới yêu cầu app thoát. Chỉ thay file sau khi tiến trình cũ đã thoát.
- ZIP có gốc `Orbit/`; từ chối traversal, ADS, tên thiết bị Windows, symlink, file trùng, settings.json và đường dẫn dành riêng. Giới hạn 256 MiB nén, 768 MiB giải nén, 2.000 entries.
- Sao lưu mọi file sẽ thay vào `.orbit-backup/<id>/files`, ghi journal, thay từng file bằng rename. Bản mới phải xác nhận khởi động qua event ngẫu nhiên trong 45 giây; thất bại thì dừng tiến trình mới, khôi phục file cũ, xóa file mới đã thêm và mở bản cũ.
- Không thay cấu hình trong LocalAppData, không đổi đường dẫn Orbit.exe nên startup giữ nguyên. Thư mục portable phải ghi được; không tự xin administrator.

## Phát hành

1. Tăng Version trong `src/Orbit.App/Orbit.App.csproj` và viết `docs/RELEASE-X.Y.Z.md`.
2. Commit và push lên `main`. Workflow `.github/workflows/release.yml` tự build/test/ký, kiểm thử helper và rollback, tạo tag `vX.Y.Z`, publish stable release và đánh dấu Latest. Không cần bấm Run workflow hay Publish. Phiên bản đã có release sẽ được bỏ qua; muốn phát hành thay đổi mới phải tăng phiên bản.
3. Release tự đính kèm `Orbit-X.Y.Z-win-x64.zip`, file `.zip.sha256`, `orbit-update.json`, `orbit-update.json.sig`. ZIP source code GitHub tự tạo không dùng để cập nhật. Client sẽ nhận bản mới ở lần kiểm tra tiếp theo.
4. Có thể kiểm tra cục bộ trước khi push: `./scripts/package.ps1 -RequireSignature` và `./scripts/test-updater.ps1` (hai tiến trình probe 0.0.1 → 0.0.2, không mở UI hoặc thay app đang dùng). Workflow cũng hỗ trợ push tag `v*` và chạy thủ công khi cần chạy lại một lần phát hành thất bại. Lỗi build/test/ký sẽ chặn phát hành.

Private key cục bộ: `.release/signing-key.private.pem` (đã gitignore). Public key: `assets/update-public-key.pem` (được commit và nhúng). GitHub Actions dùng repository secret `ORBIT_RELEASE_PRIVATE_KEY`; nội dung là private PEM, không phải GitHub token. Sao lưu private key ở nơi riêng an toàn; mất khóa sẽ không thể ký bản cập nhật mà client cũ chấp nhận. Không tự tạo lại hoặc đổi public key khi phát hành thông thường. Script package kiểm tra khóa ký khớp public key trước khi bàn giao.

Chữ ký riêng chỉ xác thực bản cập nhật cho Orbit, không làm Windows tin cậy EXE và không bảo đảm hết cảnh báo SmartScreen.

## Lỗi và giới hạn

- Mất mạng/rate limit: app vẫn chạy; Kiểm tra ngay để thử lại, không popup lặp.
- Không ghi được thư mục, file bị khóa, sai chữ ký/hash: giữ app đang dùng; xem trạng thái hoặc `update-error.txt` trong thư mục download.
- Kết quả helper: `.orbit-update-result.json` ở thư mục Orbit. Các backup/download hiện được giữ để chẩn đoán và phục hồi; chiếm thêm dung lượng ổ đĩa, chưa tự dọn. Sau khi xác nhận bản mới ổn và Orbit đã thoát, có thể lưu một backup gần nhất và dọn các bản cũ.
- Rollback tự động bảo vệ lỗi copy và lỗi khởi động trong phiên helper. Mất điện/kill helper giữa lúc thay file chưa có bootstrap recovery tự động: journal và backup vẫn còn; khôi phục `files` của backup vào thư mục Orbit khi app đã thoát. Không cam kết khôi phục tự động sau mọi dạng sự cố hệ thống.
- Không tải delta; mỗi bản lấy trọn ZIP khoảng 52 MB. Không thêm Python, PyTorch hoặc model OCR.
