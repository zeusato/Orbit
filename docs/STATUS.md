# Tiến độ Orbit

## Chuẩn bị phát hành 0.6.0 — 20/10/2026

Đại ca yêu cầu commit/push và phát hành bản hiện tại, giữ bốn lỗi đã chấp nhận. Đã tăng App/Updater lên 0.6.0, cập nhật README và tạo `RELEASE-0.6.0.md` với tính năng mới cùng giới hạn còn tồn tại. Phát hành qua workflow trên `main`; trạng thái và kết quả thực tế xem [GitHub Actions](https://github.com/zeusato/Orbit/actions/workflows/release.yml) và [Release 0.6.0](https://github.com/zeusato/Orbit/releases/tag/v0.6.0).

Kiểm tra lại đúng version 0.6.0 trước push: build **0 cảnh báo/0 lỗi**, **125/125 kiểm thử ứng dụng đạt** tại `artifacts/tests/20261020-135521-874`, log `artifacts/release-060-build-tests.log`. **13/13 nhóm kiểm tra script release đạt** (`artifacts/release-060-preparation-tests.log`); actionlint và kiểm tra whitespace đạt. HEIC thật/Office tùy chọn chưa chạy. Không dùng Computer Use.

## Chuẩn bị release bằng một lệnh — 20/10/2026

Đã triển khai theo lựa chọn của Đại ca:
- `scripts/prepare-release.ps1`: tăng patch/minor/major hoặc chọn version cụ thể, đồng bộ App/Updater/README, tạo release notes từ `-Notes` hoặc commit sau tag hiện tại. Hỗ trợ `-WhatIf`, từ chối đầu vào không nhất quán/trùng version, hoàn tác nếu ghi file thất bại; không commit/push/tag.
- `scripts/release-metadata.ps1` dùng chung cho chuẩn bị, đóng gói và CI để kiểm tra version/notes.
- Workflow build/test mọi push và PR vào main. Phát hành version mới từ main/tag chỉ sau khi kiểm tra đạt; đóng gói, ký và kiểm thử updater thật. Chặn downgrade, tag lệch và draft chưa hoàn chỉnh; giữ chẩn đoán khi lỗi. Hướng dẫn đầy đủ trong `UPDATES.md`.

Kiểm chứng: **13/13 nhóm kiểm tra release đạt**, log `artifacts/release-preparation-tests.log`, fixture `artifacts/release-tests/8a3fd29440da4746bd9f2bcd8469fb41`; build Release **0 cảnh báo/0 lỗi**. Actionlint 1.7.12 đạt, đã kiểm tra cú pháp bốn script và bốn khối PowerShell trong workflow. Không dùng Computer Use. Chưa chạy workflow mới trên GitHub vì chưa commit/push; phiên bản thật vẫn 0.5.0, không đóng gói lại bản đang dùng.

## Rà soát trước release — 20/10/2026

Lượt rà soát mới đã build thành công, **125/125 kiểm thử ứng dụng đạt** (`artifacts/tests/20261020-131727-593`), audit dependency .NET 10 không có cảnh báo; smoke bản portable và kiểm thử updater có ký success/rollback/recover đạt. Kiểm tra bổ sung tái hiện bốn lỗi bên dưới. Ngày 20/10/2026, Đại ca xác nhận chấp nhận các giới hạn này cho bản tới và dừng yêu cầu sửa; chúng không còn là điều kiện chặn release:
- Nén PDF có thể bỏ sót chữ ký đặt trực tiếp trong trường AcroForm, tạo đầu ra có chữ ký mất hiệu lực.
- PDF → ảnh và preview chưa bật render dữ liệu form, có thể xuất trang thiếu giá trị đã điền.
- Ghép/tách PDF chưa giữ AcroForm ở catalog, làm mất khả năng điền form của tài liệu đầu ra.
- Editor media có thể cho lưu theo giá trị thời gian cũ sau khi ô nhập sai mất focus.

Bằng chứng mới: `artifacts/prerelease-review-probe/results.log`. Bốn lỗi này vẫn tồn tại; bộ test hiện có chưa bao phủ các trường hợp đó. Lượt yêu cầu sửa đã dừng ở bước đọc mã nguồn, chưa thay đổi mã ứng dụng.

## Ctrl+Space mở vòng tiện ích — 20/10/2026

Theo sửa hướng UX của Đại ca:
- Ctrl+Space không giữ nút chuột mở vòng sáu tiện ích tại con trỏ, không cần Home. Click/phím 1–6 mở đúng mục; Ctrl+Space lần nữa/Esc/mất focus đóng vòng.
- Hotkey kéo file giữ lựa chọn cũ. Ctrl+Space cho tiện ích cố định, khớp modifier chính xác; không biến Shift+Space thành hotkey toàn cục khi đang gõ. Hook nuốt lặp Space/keyup tương ứng như luồng kéo.
- Dùng cùng RadialWindow và native circular region; vòng tiện ích không nhận drop, kết quả nhận diện file cũ bị hủy/loại bỏ. Quay lại kéo file khôi phục mode cũ.
- Bỏ cách xưng hô “Đại ca” khỏi toàn bộ chuỗi sản phẩm trong `src/Orbit.App`; chỉ giữ trong trao đổi với người dùng. Cập nhật gợi ý ở Home/Cài đặt.

Build 0 cảnh báo/lỗi; **125/125 kiểm thử đạt** tại `artifacts/tests/20261020-130647-046`, log `artifacts/quick-ring-test-run.log`. Ma trận modifier/chuột và WPF kiểm tra bật/tắt vòng, hình tròn native, sáu đích mở khi Home ẩn, không tự chạy tác vụ, loại nhận diện PDF về muộn và phục hồi mode kéo. Đã xem render `utilities-radial-ui.png`; sau đó đổi nhãn “Dọn text” thành “Dọn văn bản” và publish lại.

Đã cập nhật `dist/Orbit` từ `artifacts/quick-ring-portable`, đối chiếu hash và mở lại ở khay. Backup `artifacts/quick-ring-backup-20261020-130933`. Không dùng Computer Use hoặc giả lập phím vào desktop. Ctrl+Space thật từ ứng dụng khác, bấm ra ngoài, DPI/đa màn hình và kéo Explorer xen kẽ cần Đại ca thử trực tiếp.

## Tiện ích không kéo file — 20/10/2026

Đã triển khai và cập nhật bản chạy:
- Home/khay có lối vào **Tiện ích**, một cửa sổ dùng lại với sáu mục: tạo QR, đọc QR/barcode, ảnh ↔ Base64, Base64/URL văn bản, JSON, dọn văn bản.
- Tạo QR UTF-8 có preview, sao chép ảnh/lưu PNG. Đọc mã từ file hoặc clipboard/Ctrl+V, hiển thị nhiều kết quả và loại mã, không tự mở link.
- Base64 ảnh nhận raw/data URI, preview rồi lưu byte gốc; ảnh clipboard tạo PNG. Tối đa 8 MB, TGA dùng data URI để giữ format hint. Xuất trùng tên thêm số.
- Đổi mục giữ dữ liệu trong phiên; sửa đầu vào xóa kết quả cũ, dán quá dài bị chặn thay vì cắt ngắn. Xử lý nền, đóng cửa sổ bỏ kết quả về muộn, không ghi nội dung tiện ích vào lịch sử.
- Thêm ZXing.Net 0.16.11, kèm giấy phép Apache-2.0/notice trong `third-party` và portable.

Kiểm chứng: build Release **0 cảnh báo/0 lỗi**, **124/124 kiểm thử đạt** tại `artifacts/tests/20261020-115548-152`; log `artifacts/quick-tools-test-run.log`. Có QR tiếng Việt/emoji/link/xuống dòng, Code128/EAN13, QR nhiều mã/ngược màu, barcode xoay, Base64 giữ byte/alpha/JPEG/TGA, đầu vào lỗi/giới hạn/trùng tên, JSON số lớn, và luồng WPF giữ nội dung/chặn kết quả cũ/đóng khi chạy. Đã xem ảnh render tự động của QR, đọc mã, Base64 và JSON.

Đã publish tại `artifacts/quick-portable`, cập nhật `dist/Orbit` và kiểm tra hash bảy file (chương trình, dependency, giấy phép), mở lại dưới khay. Backup ở `artifacts/quick-tools-backup-20261020-115831`. Không đổi phiên bản/phát hành GitHub trong lượt này.

Không dùng Computer Use, không đọc/ghi clipboard thật khi kiểm thử. Chiều Đại ca nghiệm thu clipboard từ Snipping Tool/trình duyệt, quét QR bằng điện thoại, barcode bao bì thực tế, DPI/theme. Barcode ngoài QR/Code128/EAN13 chưa có kiểm thử riêng; ảnh đọc mã được giới hạn 4096 px/cạnh và 16 MP. HEIC thật và Office tùy chọn chưa chạy.

## Thay đổi đang phát triển sau 0.5.0 — 20/10/2026

Lượt bổ sung mục 2–3–4 Đại ca duyệt:
- Ctrl+V/nút Dán ảnh ở Home mở menu công cụ; ở composer thêm ảnh thành trang. Không chặn dán chữ trong ô nhập. Snapshot PNG lưu cục bộ trong thư mục dữ liệu `clipboard`, kết quả mặc định vào Pictures/Orbit hoặc thư mục cố định trong Cài đặt.
- Ghép/tách/nén PDF có tên và thư mục xuất riêng; kiểm tra tên Windows, giữ quy tắc chống ghi đè. Tách riêng dùng tên cho thư mục trang.
- Nén PDF với chất lượng 30–95; thay JPEG/raw/Flate RGB/Gray 8-bit phù hợp, giữ lớp chữ/kích thước trang. Bỏ qua mask (cả object mask được tham chiếu), màu đặc biệt, predictor/bộ lọc chưa hỗ trợ. Từ chối PDF có chữ ký số. Nếu kết quả không nhỏ hơn thì xuất bản sao nguyên nội dung; trạng thái hiển thị dung lượng thực trước/sau và giữ qua lịch sử.
- Sửa đóng lặp hộp hai nút khi WPF phát Deactivated trong lúc Close.
- Build Release **0 cảnh báo/0 lỗi**, **116/116 kiểm thử đạt** tại `artifacts/tests/20261020-114235-901`, log `artifacts/pdf-enhancements-test-run.log`. Có kiểm tra giảm byte thực, text extraction trước/sau, chênh lệch ảnh render, alpha mask, hủy, chữ ký, tên/thư mục, clipboard snapshot và luồng WPF tạo PDF thật.
- Đã publish self-contained và cập nhật `dist/Orbit`, đối chiếu hash ba file chương trình; mở lại dưới khay và xác nhận tiến trình chạy. Backup: `artifacts/pdf-enhancements-backup-20261020-114351`. Chưa đổi phiên bản hoặc phát hành GitHub.

Không dùng Computer Use. Kiểm thử dán dùng bitmap được truyền vào cùng luồng xử lý, không đọc/ghi clipboard của Đại ca; clipboard thật từ Snipping Tool/trình duyệt, phím Ctrl+V thực tế và DPI cao cần Đại ca nghiệm thu. HEIC thật và Office tùy chọn chưa chạy trong lượt này.

Đã triển khai trong mã nguồn:
- Ảnh → PDF có bước chọn **Chuyển ngay** hoặc **Thêm trang…**. Chuyển ngay giữ hành vi mỗi ảnh một PDF; thêm trang mở editor để tạo một PDF chung.
- Editor ghép nhận PDF và ảnh qua chọn file/kéo thả, xem trước từng trang, đổi thứ tự file, bỏ mục khỏi danh sách. Một PDF cũng có công cụ ghép; chọn trộn PDF/ảnh cũng dùng được.
- Tách một PDF theo khoảng trang như `1-3, 5`, xuất một PDF chung theo thứ tự nhập hoặc từng trang riêng. Kiểm tra khoảng sai, giữ số trang nguồn trong tên file xuất riêng.
- Giữ giới hạn 128 file/200 trang/256 MB mỗi file, ghi qua staging và không ghi đè nguồn. Dùng hàng đợi và nơi lưu hiện có; không thêm dependency hoặc thay phiên bản phát hành.

Kiểm chứng lượt này:
- SDK 10.0.402 tải từ Microsoft, đối chiếu SHA-512 và đặt riêng tại `.dotnet/sdk-local` do SDK trên PATH chỉ có 8/9.
- Build Release **0 cảnh báo/0 lỗi** tại `artifacts/pdf-build`.
- **108 đạt, 0 lỗi** tại `artifacts/tests/20261020-102908-900`; log đầy đủ `artifacts/pdf-test-run.log`. Bao gồm 8 kiểm thử bộ xử lý PDF mới và luồng editor WPF thực thi tạo file thật.
- Kiểm tra số trang, kích thước/tỷ lệ, màu trang theo thứ tự, hash nguồn, trùng tên, hủy và dọn staging, nơi lưu, nhập sai, đóng khi đang tải. Đã xem ảnh render WPF của ba cửa sổ và trang PDF xuất; sửa lỗi preview kéo vuông trang ngang.
- Lượt đầu có một bài Registry bị sandbox chặn; chạy lại ngoài sandbox trên khóa thử riêng đạt. Không có `ORBIT_HEIC_SAMPLE`, nên bài HEIC thật được bỏ qua; không chạy nhóm Office tùy chọn.

Chưa nghiệm thu thao tác kéo thả từ Explorer bằng chuột trên editor mới, DPI cao/đa màn hình và bộ PDF thực tế đa dạng. Đã publish self-contained và cập nhật bản dùng tại `dist/Orbit`; chưa phát hành GitHub. Bản sao trước khi cập nhật ở `artifacts/pdf-install-backup-20261020-103725`. Đã xác nhận lệnh Explorer `--tool ImagePdf` hiện cửa sổ lựa chọn và nút Thêm trang mở editor.

Theo yêu cầu tiếp theo của Đại ca, hộp chọn ảnh → PDF thu còn đúng hai nút nằm ngang, khoảng 284 × 58 DIP, bỏ tiêu đề/tên file/mô tả. Esc hoặc bấm ra ngoài để hủy. Không dùng Computer Use để kiểm tra các thay đổi tiếp theo; khi cần nghiệm thu thao tác thực tế, nhờ Đại ca thử.

Hộp chọn gọn: build 0 cảnh báo/lỗi; 108/108 test tại `artifacts/tests/20261020-104248-483`. Ảnh render tự động xác nhận khung 284 × 58 chỉ có hai nút. Sau khi Đại ca đóng editor và xác nhận, đã cập nhật `dist/Orbit`, đối chiếu hash ba file chương trình với bản self-contained tại `artifacts/pdf-portable` và mở lại dưới khay. Backup ở `artifacts/pdf-compact-backup-20261020-104539`.

## Bản hiện tại: 0.5.0

Đã triển khai:
- Chuyển sang .NET 10 LTS (SDK 10.0.402) và đóng gói self-contained, không cần cài .NET. ZIP 125 MB.
- Mở được HEIC/HEIF bằng libheif của Magick.NET.
- Công cụ **Khung nền**: viền, tỷ lệ, nền màu/chuyển màu/họa tiết/ảnh/ảnh gốc mờ, bo góc, đổ bóng.
- Tự phục hồi khi cập nhật bị ngắt giữa chừng.
- Tự dọn backup và bản tải cũ.
- Thanh tiến trình ở góc phải cho tác vụ chạy lâu, có Hủy/Mở Orbit, không lấy focus.
- Menu chuột phải Orbit với submenu theo loại file (gộp lệnh khi chọn nhiều file), nơi lưu tùy chọn, nhớ thiết lập công cụ, icon mới.
- Âm thanh/video bằng bộ xử lý media của Windows: MP4, nén video, cắt đoạn, tách/chuyển MP3/M4A/WAV, khung hình PNG, GIF ngắn, ghép âm thanh. Kiểm tra codec trước khi hiện công cụ; giới hạn 2 GB/1 giờ; chạy trên hàng đợi riêng.
- Lưu lịch sử tác vụ qua các lần mở app.

Build 0 warning/error; 100/100 kiểm thử tại `artifacts/tests/` (có giải mã HEIC thật với `ORBIT_HEIC_SAMPLE`). Kịch bản phục hồi bằng tiến trình thật đạt (`test-updater.ps1 -RecoverOnly`). Bản đóng gói chạy `--render-previews` đạt khi DOTNET_ROOT trỏ sai.

Chưa kiểm chứng trên máy này:
- Hai kịch bản helper có ký: cần private key, chạy trên CI khi push.
- Cập nhật thật từ 0.4.1 lên 0.5.0.
- Ảnh HEIC từ iPhone thật.
- Khung nền với canvas gần 60 MP.
- Thao tác chuột trên editor Khung nền.
- Video thật quay bằng điện thoại (xoay theo metadata, HEVC), file media dài gần 1 giờ, phát thử trên máy có nhiều thiết bị âm thanh.

Chi tiết ở [RELEASE-0.5.0.md](RELEASE-0.5.0.md).

## Bản 0.4.1

Cài đặt và cửa sổ Cập nhật Orbit hiển thị “Tác giả: Nguyễn Mạnh Quyết”, không còn địa chỉ nguồn tải. Build 0 warning/error; 57/57 kiểm thử đạt tại `artifacts/tests/20261019-181427-478`. Phát hành qua workflow tự động khi push main.

## Bản 0.4.0

Đã triển khai cập nhật portable từ `zeusato/Orbit`: kiểm tra/tải nền, lưu trạng thái tải qua restart, chữ ký RSA-PSS + SHA-256, kiểm tra ZIP/path/version, helper đợi app thoát, backup và rollback lỗi copy/khởi động. Có Settings và cửa sổ cập nhật, hoãn/tắt cập nhật, script ký và GitHub workflow tự tạo tag và publish release khi push main có phiên bản mới. Không thêm runtime/model mới. Chi tiết [UPDATES.md](UPDATES.md), [RELEASE-0.4.0.md](RELEASE-0.4.0.md).

57/57 kiểm thử tại `artifacts/tests/20261019-175755-577`; helper thực thi thành công cả nâng phiên bản và rollback bằng tiến trình fixture riêng tại `artifacts/updater-tests/c33da66873914583a5fe0dd93871c929`. Bản cũ đang chạy được giữ nguyên, build mới dùng artifacts-path riêng. Vòng đóng gói cuối đã đạt 57/57 kiểm thử, 0 warning/error và xác minh chữ ký thành công. Đã tự phát hành stable [v0.4.0](https://github.com/zeusato/Orbit/releases/tag/v0.4.0) qua push main ngày 19/10/2026. [CI](https://github.com/zeusato/Orbit/actions/runs/39241997812) đạt build/test/ký và cả hai kịch bản helper. Đã tải ZIP qua URL public không dùng token, xác minh chữ ký bằng public key của ứng dụng, SHA-256 và kích thước đều khớp manifest.

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
