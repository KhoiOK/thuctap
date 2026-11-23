# -*- coding: utf-8 -*-
"""Section 9 - part C: new endpoints and environment/risks."""

PART_C = """
<h3>9.5 Endpoint mới</h3>
<h4>#12 POST /users/{userId}/identity-documents/ocr/scan</h4>
<p><code>multipart/form-data</code>. Trường: <code>documentType</code> (bắt buộc),
<code>frontImage</code> (bắt buộc), <code>backImage</code> (bắt buộc với CCCD/CMND).
Status: 200, 400, 404, 413, 415, 503.</p>
<p>Quy trình: kiểm tra User (404) → validate multipart và kích thước (413) → validate từng
ảnh bằng magic bytes (415/400) → upload ảnh tạm lên Cloudinary → gọi dịch vụ PaddleOCR
(timeout 30 giây, lỗi trả 503) → chuẩn hoá và validate kết quả → lưu bản ghi OCR → trả 200.</p>
<p>Response 200 gồm <code>scanId</code>, <code>fields</code> (mỗi value, confidence,
source, isValid), <code>warnings</code>, <code>processingTimeMs</code> và
<code>publicId</code> tạm để tiện cho bước xác nhận.</p>

<h4>#13 POST /users/{userId}/identity-documents/ocr/confirm</h4>
<p><code>application/json</code>. Body gồm <code>scanId</code>, <code>documentType</code>,
<code>documentNumber</code>, <code>issuedDate</code>, <code>expiryDate</code> và
<code>publicId</code> tạm của hai mặt ảnh.</p>
<p>Quy trình: kiểm tra scanId còn hiệu lực (hết 24 giờ trả 400) → validate metadata theo
BR-08, BR-10 → kiểm tra trùng (409) → chuyển ảnh tạm sang folder chính thức → ghi
IdentityDocument trong transaction → commit → xoá ảnh tạm → trả 201.</p>
<p>Status: 201, 400, 404, 409, 413, 415.</p>

<h3>9.6 Môi trường và rủi ro</h3>
<table>
<tr><th>Mục</th><th>Nội dung</th></tr>
<tr><td>9.6.1 Dịch vụ PaddleOCR</td><td>Chạy dưới dạng container Python riêng, giao tiếp REST.
Base URL lấy từ cấu hình <code>Ocr:BaseUrl</code>. Không đọc PDF, không đọc file, chỉ đọc
ảnh. Phải có endpoint kiểm tra <code>GET /health</code>.</td></tr>
<tr><td>9.6.2 Cloudinary</td><td>PHẢI dùng signed upload (chữ ký SHA-1 phía server),
không dùng unsigned preset. <code>ApiSecret</code> phải nằm ngoài source control.
Không bao giờ log giá trị OCR như số giấy tờ, họ tên.</td></tr>
<tr><td>9.6.3 Rủi ro PII</td><td>Ảnh CCCD là dữ liệu cá nhân nhạy cảm. Phải dùng delivery
<code>authenticated</code>, không lộ <code>public_id</code> ra giao diện.</td></tr>
<tr><td>9.6.4 OCR thất bại</td><td>OCR là TÙY CHỌN không phải điều kiện để lưu. Khi OCR lỗi
(503) hoặc không đọc được (400), người dùng vẫn nhập tay và lưu giấy tờ bình thường.</td></tr>
<tr><td>9.6.5 Chuyển dữ liệu ảnh cũ</td><td>Migration giữ nguyên cột cũ trong 1 release để
rollback an toàn. Công cụ chuyển ảnh lên Cloudinary KHÔNG tự xoá file cũ.</td></tr>
</table>
"""