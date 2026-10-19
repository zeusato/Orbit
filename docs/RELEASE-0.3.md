# Orbit 0.3.0 — PDF có chữ và OCR

Đại ca yêu cầu PDF dạng text chuyển sang Word, dạng scan chỉ lấy chữ, không lo trình bày. Em triển khai hai luồng tự chọn theo nội dung trang, chạy cục bộ.

## Sử dụng

- PDF có lớp chữ: chọn **Word** để tạo DOCX sửa được. Không cần cài Microsoft Word. Nội dung chữ, xuống dòng và ngắt trang cơ bản được giữ; dùng Arial 11, khổ A4. Không chuyển hình, bảng, header/footer thành đối tượng tương ứng và không cam kết giữ số trang hay bố cục hiển thị giống PDF. Văn bản nhiều cột có thể sai thứ tự đọc.
- PDF scan/trộn: chọn **Lấy chữ** để tạo `_text.txt` UTF-8. Mỗi trang có nhãn nguồn: lớp chữ, OCR hoặc trống. Không bỏ qua trang scan trong PDF trộn. Không hiện Word cho PDF có trang cần OCR.
- Ảnh tĩnh: trong **Thêm…**, chọn **OCR → TXT**. Có hỗ trợ tiếng Việt và tiếng Anh, không phục dựng trình bày.
- File đầu ra nằm cạnh nguồn, thêm số khi trùng tên. Không ghi đè hay thay đổi nguồn. Hủy/xảy ra lỗi sẽ xóa file tạm và không công bố kết quả dở dang.

## OCR và nhận dạng

PdfPig 0.1.16 đọc lớp chữ. PDF có ảnh chiếm hơn 55% diện tích và dưới 80 chữ/số được xem là trang cần OCR, tránh dùng mỗi số trang trên một bản scan. Trang không có chữ nhưng có nội dung đồ họa cũng được OCR. Đây là heuristic: không chứng minh lớp chữ đúng hay đầy đủ; không OCR riêng từng ảnh chèn trong một trang đã được xác định có lớp chữ. PDF scan đã có lớp OCR đầy đủ sẽ dùng lớp chữ sẵn có, kể cả lỗi của lớp đó.

OCR dùng Tesseract 5.2.0 và tessdata_best vie+eng, LSTM-only. Xóa alpha trên nền trắng, grayscale và deskew nhẹ; không tự xoay trang 90° hoặc sửa nội dung bằng mô hình sinh. PDF render tối đa 300 DPI, giới hạn cạnh dài 4000 px và giữ đúng tỷ lệ. Ảnh OCR dùng giới hạn 60 megapixel chung. Mỗi file tối đa 256 MB, PDF tối đa 200 trang.

TXT có lời nhắc đối chiếu dấu, tên riêng và số. Điểm tin cậy trung bình dưới 0,7 có nhãn cần kiểm tra; không diễn giải thành phần trăm chính xác. Trang scan không nhận được chữ có nhãn rõ. Ảnh không nhận được chữ báo lỗi. Hủy kiểm tra giữa trang và sau native OCR; không ngắt engine giữa lúc nhận dạng một trang.

## Kiểm chứng

45/45 bài kiểm thử đạt tại `artifacts/tests/20261019-160527-041`, gồm 41 mặc định và 4 kiểm thử Office:

- Phân loại PDF chữ, scan và trộn; giao của công cụ khi chọn nhiều file; menu WPF thật ẩn/hiện Word đúng.
- Mẫu chữ in rõ có “Tiếng Việt”, “Cộng hòa xã hội chủ nghĩa Việt Nam”, mã `123456789`, số `1.250.000`; xác nhận chữ/số trên ảnh và trang scan PDF.
- PDF trộn giữ cả nội dung trực tiếp lẫn OCR và nhãn trang.
- DOCX có text node sửa được và ngắt trang; tiếng Việt từ PDF xuất bởi Word được giữ, DOCX kết quả mở và xuất lại thành PDF bằng Word thật.
- Không ghi đè, giữ hash nguồn, hủy không công bố file dở và xử lý PDF hỏng.

Chưa đánh giá định lượng CER/WER trên bộ tài liệu người dùng; không có cam kết chính xác cho scan mờ, bảng dày, công thức, chữ viết tay hoặc bố cục nhiều cột. Không suy rộng kết quả mẫu tổng hợp thành độ chính xác chung.

## Phân phối và phát triển

Giữ nguyên thư mục phát hành, gồm `tessdata` và `x64`. Yêu cầu .NET 8 Desktop Runtime x64; thư viện OCR cần Microsoft Visual C++ Redistributable x64 (2015–2022). Chưa kiểm chứng máy sạch chưa có runtime. Hai mô hình tăng dung lượng khoảng 28 MB trước nén, chỉ tải vào bộ nhớ khi có tác vụ OCR. Chưa đo tài nguyên nền/throughput dài hạn.

Chạy `scripts/prepare-ocr.ps1` để tải hoặc kiểm tra mô hình theo commit/SHA-256; sau đó `scripts/build.ps1 -Test -OfficeTest` và `scripts/package.ps1`. Nguồn và giấy phép ở `third-party/OCR-NOTICES.md`. PDF → DOCX không gọi COM hoặc thay đổi thiết lập Word; Office → PDF vẫn dùng COM như trước.
