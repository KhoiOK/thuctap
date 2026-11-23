# -*- coding: utf-8 -*-
"""Section 9 - part A: intro, motivation, functional requirements."""

PART_A = """
<h2>9. Bổ sung – OCR giấy tờ và lưu trữ ảnh Cloudinary (v2.1)</h2>

<p class="note"><b>Bản diễn biến.</b> Mục này ghi nhận phần mở rộng đã được duyệt cho
SRS-UM-001 v2.0. Các điều khoản bị thay thế được đánh dấu <b>[v2.1]</b> trong toàn bộ
tài liệu. Chi tiết kỹ thuật: <code>docs/OCR-Cloudinary-Spec.md</code>.</p>

<h3>9.1 Lý do</h3>
<p>Phiên bản v2.0 loại OCR ra khỏi phạm vi (mục 1.1) và lưu ảnh vào thư mục máy chủ
(mục 3.5). Hai hành động này làm chậm việc nhập liệu số lượng lớn và không triển khai
được trên hạ tầng máy chủ dịch vụ. v2.1 bổ sung quét tự động thông tin CCCD/CMND bằng
PaddleOCR và chuyển lưu ảnh sang Cloudinary.</p>
<p class="note">Điều khoản 1.7 của bản gốc đã dự trù hướng thiết kế: “Ảnh lưu trên thư
mục máy chủ qua <code>IFileStorageService</code> (dễ thay bằng cloud)”. v2.1 thực thi
hướng đó.</p>

<h3>9.2 Yêu cầu chức năng mới</h3>
<table>
<tr><th>Mã</th><th>Yêu cầu</th><th>API</th><th>Màn hình</th></tr>
<tr><td>FR-09</td><td>Quét tự động thông tin CCCD/CMND từ ảnh mặt trước và mặt sau.
Kết quả là dữ liệu gợi ý; người dùng PHẢI review trước khi lưu (BR-13).</td>
<td>POST <code>/users/{userId}/identity-documents/ocr/scan</code></td>
<td>SCR-07</td></tr>
<tr><td>FR-10</td><td>Lưu trữ ảnh trên Cloudinary thay cho thư mục cục bộ. Database lưu
<code>public_id</code> thay cho đường dẫn tương đối.</td>
<td>Thay thế toàn bộ API-07 đến API-10</td>
<td>SCR-02, SCR-06</td></tr>
</table>
"""