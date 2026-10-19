# Orbit 0.3.1 — sửa mất nội dung OCR trên trang dài

## Vấn đề đã tái hiện

Đại ca cung cấp PNG `Phan-tich-HPG_13-10-2026.png` và TXT lỗi. TXT có nhãn Trang 1/OCR, được tạo qua luồng PDF. Em kiểm tra PDF cùng tên trong cùng thư mục để tái hiện.

- PNG gốc: 2160×20365 pixel.
- PDF: 1145,495×10800 point; quy tắc cũ render thành 425×4000 pixel. Chữ nhỏ bị giảm còn khoảng 1/5 kích thước theo mỗi chiều.
- Chạy lại bản cũ trên bản sao PDF: 2.084 ký tự, 11,0 giây.
- Sau sửa trên cùng PDF: 13.908 ký tự, 81,6 giây.
- OCR trực tiếp PNG sau sửa: 13.736 ký tự, 75,7 giây.

Số ký tự chỉ cho thấy mức nội dung thu được, **không phải độ chính xác**. Thời gian là một lần chạy trên máy phát triển, không phải benchmark tốc độ. Kết quả tự động lưu ở `artifacts/ocr-hpg/{baseline-pdf,fixed-pdf,fixed-png}`; không sửa thủ công nội dung TXT và không ghi vào thư mục Desktop.

## Thay đổi

- Render PDF tối đa 300 DPI theo ngân sách 48 triệu pixel, cạnh tối đa 30000, giữ tỷ lệ. Không ép toàn bộ infographic về cạnh dài 4000 px.
- Ảnh dọc cao hơn 6000 px và cao hơn hai lần chiều rộng được OCR thành vùng lõi 2400 px, thêm ngữ cảnh 200 px mỗi đầu. Tâm bounding box của dòng xác định vùng sở hữu. Không deskew toàn bộ infographic chứa nhiều biểu đồ.
- Thông báo tiến độ theo vùng; hủy ở ranh giới vùng, không công bố file dang dở. Ảnh dài/PDF độ phân giải cao dùng nhiều RAM hơn bản cũ; đã quan sát khoảng 450 MB working set của tiến trình chẩn đoán, chưa đo đỉnh chuẩn hóa.
- Công cụ chẩn đoán cho developer: `Orbit.Tests.exe --ocr-file <nguồn> <thư mục-mới>` tạo bản sao và kết quả trong thư mục được chỉ định. Không ghi đè nguồn hoặc TXT cũ. `--ocr-variants` so sánh tiền xử lý trên một vùng ảnh.

## Kiểm chứng và phần chưa đạt

43 bài kiểm thử mặc định đạt, thêm hai bài cho độ phân giải trang dài và giữ dòng ở đầu/cuối/ranh giới vùng đúng một lần, gồm hủy giữa vùng. Mẫu tổng hợp cũ vẫn đọc được dấu tiếng Việt. Mẫu HPG thật lấy lại phần nhận định, tin cuối trang và nhiều dòng bảng bị mất trước đó.

Tesseract vẫn đọc sai dấu ở font của mẫu này và có lỗi số **25,13% → 28,13%**; cột đôi đôi lúc bị đọc xen kẽ. Không thể dùng số lượng ký tự hay confidence cao để kết luận chất lượng tốt. Bản này sửa lỗi mất nội dung do xử lý ảnh, chưa giải quyết triệt để chất lượng nhận dạng tiếng Việt; chưa có số đo CER/WER đầy đủ hoặc nghiệm thu chính xác với Đại ca.

Đã thử vie/vie+eng, màu/xám/phóng 1,5 lần và mô hình ONNX khác trên vùng cuối cùng. Chưa đạt yêu cầu tiếng Việt nên giữ engine hiện tại; không đóng gói dependency ONNX hoặc các mô hình thử. Ảnh và PDF chỉ được xử lý cục bộ.
