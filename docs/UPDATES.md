# Cập nhật portable qua GitHub Releases

Nguồn cố định: `https://github.com/zeusato/Orbit`. Client không cần Git, tài khoản GitHub hay token. Từ 0.5.0, ZIP là bản self-contained (.NET 10 đi kèm), chạy trên Windows 11 x64 mà không cần cài .NET. Không có installer và chưa có Authenticode certificate.

## Trên client

- Từ 0.4.0, mặc định kiểm tra sau 30 giây mở app, sau đó tối đa mỗi 12 giờ (kể cả khi lần trước lỗi). Có thể tắt trong Cài đặt. Bản 0.3.x phải nâng lên 0.4.0 thủ công một lần.
- Tự tải stable release mới hơn theo số phiên bản; bỏ draft/prerelease. Lưu phiên bản tải sẵn và lựa chọn Để sau qua lần mở lại. Thông báo nằm ở Cài đặt → Cập nhật Orbit hoặc menu khay.
- Chỉ tải qua HTTPS từ GitHub/CDN GitHub; kiểm tra RSA-PSS/SHA-256 bằng public key nhúng, rồi kiểm tra SHA-256 và kích thước ZIP. Kiểm tra lại trong helper trước áp dụng; chặn downgrade và phiên bản assembly không khớp manifest.
- Tự áp dụng khi ở khay, mọi cửa sổ đã đóng, không có job/editor/menu/chuột kéo. Cập nhật ngay vẫn phải chờ công cụ đang xử lý và editor đóng. Để sau hoãn 4 giờ.
- Helper chạy từ bản sao trong `%LOCALAPPDATA%/Orbit/updates/<id>/runner`, xác nhận đúng PID/thời điểm khởi động/đường dẫn tiến trình Orbit rồi mới yêu cầu app thoát. Chỉ thay file sau khi tiến trình cũ đã thoát. Từ 0.5.0, bản sao gồm helper và toàn bộ runtime mà `Orbit.Updater.deps.json` liệt kê (khoảng 190 file, 78 MB), nên helper chạy được khi máy không có .NET.
- ZIP có gốc `Orbit/`; từ chối traversal, ADS, tên thiết bị Windows, symlink, file trùng, settings.json và đường dẫn dành riêng. Giới hạn 256 MiB nén, 768 MiB giải nén, 2.000 entries. Bản 0.5.0 nằm trong giới hạn này: khoảng 119 MB nén, 242 MB giải nén, 312 file.
- Sao lưu mọi file sẽ thay vào `.orbit-backup/<id>/files`, ghi journal, thay từng file bằng rename. Bản mới phải xác nhận khởi động qua event ngẫu nhiên trong 45 giây; thất bại thì dừng tiến trình mới, khôi phục file cũ, xóa file mới đã thêm và mở bản cũ.
- Không thay cấu hình trong LocalAppData, không đổi đường dẫn Orbit.exe nên startup giữ nguyên. Thư mục portable phải ghi được; không tự xin administrator.

## Nâng từ 0.4.x lên 0.5.0

Helper 0.4.x (chạy trên .NET 8 của máy) vẫn là helper áp dụng bản 0.5.0. Gói 0.5.0 giữ đủ các file mà helper cũ bắt buộc phải có (`Orbit.Updater.dll`, `.deps.json`, `.runtimeconfig.json`…). Bản mới khởi động bằng runtime đi kèm. Từ 0.5.0 trở đi, helper mới tự chép kèm runtime. Kịch bản thật từ 0.4.1 đã cài sẵn chưa được chạy trên máy phát triển; cần theo dõi lần cập nhật đầu tiên.

## Phát hành

1. Tăng Version trong `src/Orbit.App/Orbit.App.csproj` và `src/Orbit.Updater/Orbit.Updater.csproj`, rồi viết `docs/RELEASE-X.Y.Z.md`.
2. Commit và push lên `main`. Workflow `.github/workflows/release.yml` tự build/test/ký, kiểm thử helper và rollback, tạo tag `vX.Y.Z`, publish stable release và đánh dấu Latest. Không cần bấm Run workflow hay Publish. Phiên bản đã có release sẽ được bỏ qua; muốn phát hành thay đổi mới phải tăng phiên bản.
3. Release tự đính kèm `Orbit-X.Y.Z-win-x64.zip`, file `.zip.sha256`, `orbit-update.json`, `orbit-update.json.sig`. ZIP source code GitHub tự tạo không dùng để cập nhật. Client sẽ nhận bản mới ở lần kiểm tra tiếp theo.
4. Có thể kiểm tra cục bộ trước khi push: `./scripts/package.ps1 -RequireSignature` và `./scripts/test-updater.ps1`. Script chạy tiến trình probe 0.0.1 → 0.0.2 cho các kịch bản success/rollback/recover, không mở UI hoặc thay app đang dùng. Không có khóa ký thì dùng `-RecoverOnly`. `package.ps1` cũng tự chép helper ra thư mục riêng và chạy thử khi DOTNET_ROOT trỏ sai, để chắc helper không phụ thuộc .NET cài sẵn. Workflow còn hỗ trợ push tag `v*` và chạy thủ công khi cần chạy lại một lần phát hành thất bại. Lỗi build/test/ký sẽ chặn phát hành.

Private key cục bộ: `.release/signing-key.private.pem` (đã gitignore). Public key: `assets/update-public-key.pem` (được commit và nhúng). GitHub Actions dùng repository secret `ORBIT_RELEASE_PRIVATE_KEY`; nội dung là private PEM, không phải GitHub token. Sao lưu private key ở nơi riêng an toàn; mất khóa sẽ không thể ký bản cập nhật mà client cũ chấp nhận. Không tự tạo lại hoặc đổi public key khi phát hành thông thường. Script package kiểm tra khóa ký khớp public key trước khi bàn giao.

Chữ ký riêng chỉ xác thực bản cập nhật cho Orbit, không làm Windows tin cậy EXE và không bảo đảm hết cảnh báo SmartScreen.

## Lỗi, phục hồi và dọn dẹp

- Mất mạng/rate limit: app vẫn chạy; Kiểm tra ngay để thử lại, không popup lặp.
- Không ghi được thư mục, file bị khóa, sai chữ ký/hash: giữ app đang dùng; xem trạng thái hoặc `update-error.txt` trong thư mục download.
- Kết quả helper: `.orbit-update-result.json` ở thư mục Orbit.
- Rollback tự động bảo vệ lỗi copy và lỗi khởi động trong phiên helper.
- **Cập nhật bị ngắt** (mất điện, kill helper giữa lúc thay file) để lại journal không có `completed.txt` hay `rolled-back.txt`. Từ 0.5.0, xử lý như sau:
  - Lần mở Orbit sau phát hiện journal đó (khi không còn helper giữ `.orbit-update.lock`), chép helper ra thư mục riêng, chạy `--recover` rồi thoát.
  - Helper ghi `recovery-attempted.txt`, khôi phục toàn bộ snapshot, đánh dấu `rolled-back.txt` và mở lại Orbit.
  - Chỉ tự thử một lần. Nếu thất bại, Orbit hiện thông báo thay vì lặp lại.
  - Nếu Orbit.exe hỏng đến mức không mở được, vẫn khôi phục thủ công được: chép `files` của backup vào thư mục Orbit khi app đã thoát.
- **Tự dọn** khi khởi động, sau khi kiểm tra bản tải đang chờ:
  - Giữ backup hoàn tất mới nhất, mọi journal dở, bản tải đang chờ và các thư mục tải mới tạo dưới 10 phút.
  - Xóa các backup cũ đã hoàn tất hoặc đã rollback, và các thư mục tải cũ.
  - Chỉ xóa thư mục tên GUID của Orbit, không đi theo junction.
- Không tải delta; mỗi bản lấy trọn ZIP (khoảng 119 MB từ 0.5.0). Không thêm Python, PyTorch hoặc model OCR.
