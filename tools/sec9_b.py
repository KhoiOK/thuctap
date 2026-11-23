# -*- coding: utf-8 -*-
"""Section 9 - part B: business rules and clause changes."""

PART_B = """
<h3>9.3 Quy tắc nghiệp vụ mới</h3>
<table>
<tr><th>Mã</th><th>Quy tắc</th></tr>
<tr><td>BR-13</td><td>Kết quả OCR là dữ liệu gợi ý, không phải dữ liệu đã xác thực. Mọi
giá trị OCR PHẢI đi qua bộ validator hiện có trước khi lưu. Hệ thống KHÔNG tự động
lưu giấy tờ sau khi quét.</td></tr>
<tr><td>BR-14</td><td>Trường có <code>confidence</code> dưới <code>Ocr:MinConfidence</code>
(mặc định 0.60) không được điền tự động. Trường có confidence từ 0.85 trở lên hiển thị
huy hiệu “tin cậy cao”. Trường OCR vi phạm validation nghiệp vụ sẽ gắn lỗi tương ứng,
không tự sửa giá trị.</td></tr>
<tr><td>BR-15</td><td>Ảnh upload trong lúc quét nằm ở folder <code>tmp/{scanId}</code>, chỉ
dùng để tạo giá trị gợi ý trong 24 giờ. Hết 24 giờ hoặc sau khi xác nhận thì xoá.</td></tr>
<tr><td>BR-16</td><td>Xoá giấy tờ sẽ xoá ảnh trên Cloudinary. Xoá User sẽ xoá toàn bộ ảnh
của các giấy tờ thuộc User. Lỗi xoá Cloudinary chỉ ghi log, không rollback database.</td></tr>
<tr><td>BR-17</td><td>Ảnh PHẢI được phục vụ qua delivery <code>authenticated</code> hoặc
signed URL. Không đặt <code>public_id</code> trong HTML hay mail. URL có chữ ký có thời
hạn.</td></tr>
</table>

<h3>9.4 Thay đổi các điều khoản hiện có</h3>
<table>
<tr><th>Điều khoản</th><th>Bản gốc</th><th>v2.1</th></tr>
<tr><td>1.1</td><td>“OCR hoặc xác thực giấy tờ” nằm ngoài phạm vi</td>
<td>[v2.1] Bỏ mục đó; OCR được đưa vào phạm vi với điều kiện là dữ liệu gợi ý.</td></tr>
<tr><td>1.3</td><td>“Api → SQL Server + File Storage (thư mục ảnh)”</td>
<td>[v2.1] “Api → SQL Server + Cloudinary”.</td></tr>
<tr><td>1.4</td><td><code>IFileStorageService (LocalFileStorageService)</code></td>
<td>[v2.1] <code>ICloudinaryStorage (CloudinaryStorage)</code>.</td></tr>
<tr><td>1.6</td><td>Khoá <code>FileStorage:RootPath</code></td>
<td>[v2.1] Thêm section <code>Cloudinary</code> và <code>Ocr</code>.
<code>FileStorage:RootPath</code> không còn dùng cho ảnh.</td></tr>
<tr><td>3.1</td><td>“Database chỉ lưu đường dẫn tương đối”</td>
<td>[v2.1] “Database chỉ lưu <code>public_id</code> của Cloudinary”.</td></tr>
<tr><td>3.3</td><td><code>FrontImagePath</code>, <code>BackImagePath</code></td>
<td>[v2.1] <code>FrontImagePublicId</code>, <code>BackImagePublicId</code> (kèm
<code>FrontImageVersion</code>, <code>BackImageVersion</code>, kích thước ảnh).</td></tr>
<tr><td>3.5</td><td>Cấu trúc thư mục <code>{RootPath}/identity-documents/...</code></td>
<td>[v2.1] Signed upload lên Cloudinary; folder
<code>{Folder}/identity-documents/{userId}/{documentId}</code>; ảnh tạm ở
<code>{Folder}/tmp/{scanId}</code>.</td></tr>
<tr><td>4.1</td><td>Mã lỗi HTTP</td>
<td>[v2.1] Thêm 503 Service Unavailable (dịch vụ OCR không sẵn sàng).</td></tr>
<tr><td>4.2</td><td>11 endpoint</td>
<td>[v2.1] Thêm endpoint #12 (ocr/scan) và #13 (ocr/confirm).</td></tr>
<tr><td>5.3</td><td>SCR-06 form giấy tờ</td>
<td>[v2.1] Thêm panel kết quả OCR, nút “Quét tự động”, auto-fill có huy hiệu tin cậy.</td></tr>
<tr><td>6.2</td><td>Bảng validation</td>
<td>[v2.1] Thêm trường OCR và quy tắc tin cậy theo BR-14.</td></tr>
<tr><td>7</td><td>Checklist nghiệm thu</td>
<td>[v2.1] Thêm checklist FR-09, FR-10 và BR-13 đến BR-17.</td></tr>
<tr><td>8.2</td><td>Bước 0 đến Bước 8</td>
<td>[v2.1] Thêm Bước 9 (OCR + Cloudinary).</td></tr>
</table>
"""