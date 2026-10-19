# Orbit 0.2.0 — 19/10/2026

Đại ca yêu cầu bỏ nền thừa ngoài vòng, tự nhận dạng file, thêm WebP và ưu tiên PDF/Office. Em đã triển khai các thay đổi sau.

## Menu tròn

- Loại bỏ hoàn toàn accent/system backdrop trên HWND của menu. Các API này vẫn vẽ hình chữ nhật dù HRGN đã là hình tròn; ảnh Đại ca gửi xác nhận lỗi đó.
- Menu lấy một ảnh nền nhỏ tại vị trí chuẩn bị mở, blur trong WPF, phủ tint rồi clip toàn bộ nội dung ở bán kính 190 DIP. Ngoài vòng có alpha bằng 0. Ảnh nền chỉ nằm trong RAM, được bỏ khi đóng menu; không lưu hay gửi ra mạng.
- Đây là nền chụp tại lúc mở, không phải blur live. Menu đứng yên trong một phiên nên không phải cập nhật ảnh nền liên tục. Cửa sổ chính vẫn dùng Desktop Acrylic live.
- Nhận diện bất đồng bộ từ chữ ký JPEG/PNG/WebP/PDF… và các thành phần trong gói Office. Không chỉ nhìn phần đuôi. TGA dùng header kết hợp đuôi, rồi decoder kiểm tra lúc xử lý.
- Không hiện đích trùng định dạng thực tế của bất kỳ file nào trong nhóm. Batch chỉ có công cụ chung; crop chỉ hiện với một ảnh. File hỗn hợp không bị đưa vào công cụ sai loại.
- Mỗi trang tối đa sáu phân đoạn; “Thêm…” chuyển trang. Nếu thả vào “Thêm…”, menu tiếp tục ở chế độ bấm chọn, chưa chạy tác vụ.

## Định dạng và công cụ

| Nguồn | Công cụ thực thi |
| --- | --- |
| JPEG, PNG, WebP, BMP, TIFF, GIF tĩnh, ICO, AVIF, TGA | PNG, JPEG, WebP lossless, BMP, TIFF, GIF tĩnh, ICO, PDF; resize, crop, xoay, nén JPEG |
| PDF | Xuất tất cả trang thành PNG/JPEG ở 150 DPI; tách trang; ghép nhiều PDF theo tên file |
| DOC/DOCX, XLS/XLSX, PPT/PPTX | Xuất PDF bằng Microsoft Office tương ứng đã cài trên máy |
| DOCX | Trích phần văn bản thân tài liệu thành TXT UTF-8, không cần Word |
| File bất kỳ | Tạo ZIP riêng cho từng file; lưu mã SHA-256 |

WebP giữ alpha và xuất lossless. GIF giới hạn 256 màu. ICO giữ tỷ lệ, thu về tối đa 256 px. Bộ xử lý mới là 8 bit/kênh; không cam kết giữ dữ liệu HDR/16 bit của nguồn. ICO nhiều kích cỡ lấy ảnh lớn nhất; ảnh động và TIFF nhiều trang bị từ chối. AVIF chỉ có đầu vào trong bản này. HEIC/HEIF chưa được bật vì chưa kiểm chứng decoder của gói hiện tại; không quảng cáo như định dạng hoạt động.

PDF nhiều trang tạo thư mục `_images` hoặc `_pages` cạnh nguồn. Các file tạm chỉ được công bố sau khi toàn bộ tác vụ hoàn tất, có hậu tố khi trùng tên. Giới hạn 256 MB/file, tối đa 200 trang/tác vụ PDF và 60 megapixel/ảnh.

Office mở tài liệu chỉ đọc, không thêm vào Recent, tắt macro trong phiên tự động rồi khôi phục thiết lập, đóng đúng tài liệu do Orbit mở. Không thoát Office nếu còn tài liệu khác. Không cần cài Office để dùng công cụ ảnh/PDF. Tác vụ Office có thể phải chờ COM trả về trước khi hủy; file có mật khẩu/Protected View và trạng thái Office chưa kích hoạt có thể báo lỗi hoặc chờ xử lý của Office. Chưa có PDF → Word/Excel, OCR, xóa nền AI hoặc audio/video.

## Kiểm chứng

- 33 kiểm thử đạt với `scripts/build.ps1 -Test -OfficeTest`, build không có cảnh báo/lỗi.
- Kiểm thử codec đọc lại pixel/alpha WebP và đầu ra BMP/TIFF/GIF/ICO, AVIF/TGA; từ chối WebP động.
- Kiểm thử PDF có hai trang: xuất ảnh, tách, ghép, hủy và giữ nguyên hash nguồn.
- Xuất thật DOCX, XLSX, PPTX mẫu qua Word/Excel/PowerPoint trên máy này; PDF đọc được và đúng số trang.
- Chạy chính `dist/Orbit/Orbit.exe` sau publish: xuất và đọc lại WebP, tạo PDF rồi xuất/đọc PNG thành công. Kết quả tại `artifacts/package-smoke/render-result.txt`; lần kiểm thử mặc định cuối đạt 30/30 tại `artifacts/tests/20261019-135450-432`.
- Kiểm thử WPF: menu JPEG có WebP và không có JPEG; mọi pixel cách tâm hơn 192 DIP có alpha bằng 0; chạy nút PNG và kiểm tra hàng tác vụ đạt 100%.
- Quan sát cửa sổ thật bằng công cụ Windows: menu chỉ còn vòng tròn, nền vuông không còn. Chưa nghiệm thu đa màn hình/DPI 125–200%, drag từ mọi ứng dụng, tài liệu Office phức tạp hoặc hiệu năng dưới 100 ms.

## Phụ thuộc

Magick.NET-Q8-x64 15.0.0; PDFtoImage 5.4.0 (PDFium + SkiaSharp); PDFsharp 6.2.4. Bản đóng gói chỉ lấy runtime native win-x64, vẫn cần .NET 8 Desktop Runtime. Các notice/license đi kèm trong `third-party`.

Nguồn kỹ thuật: [Magick.NET](https://github.com/dlemstra/Magick.NET), [PDFtoImage](https://github.com/sungaila/PDFtoImage), [PDFsharp](https://docs.pdfsharp.net/).
