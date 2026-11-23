# -*- coding: utf-8 -*-
"""Section 9 - part D: implementation step and open questions."""

PART_D = """
<h3>9.7 Bước triển khai bổ sung</h3>
<p><b>Bước 9 – OCR và Cloudinary.</b></p>
<ul>
<li>Tạo bảng <code>OcrExtractionResults</code> và thêm cột mới cho
<code>IdentityDocuments</code>; tạo Migration.</li>
<li>Triển khai <code>ICloudinaryStorage</code> và <code>CloudinaryStorage</code>; thay thế
<code>IFileStorage</code> cho lưu ảnh.</li>
<li>Triển khai <code>IOcrService</code>, <code>PaddleOcrClient</code>, bộ tách trường và
bộ chuẩn hoá kết quả OCR.</li>
<li>Triển khai endpoint #12 và #13; chuyển API-07 đến API-10 sang Cloudinary.</li>
<li>Triển khai dịch vụ PaddleOCR (Python/Docker) và thêm vào docker-compose.</li>
<li>Frontend: panel kết quả OCR, auto-fill, xử lý lỗi; không tự động submit.</li>
<li>Công cụ chuyển ảnh cũ lên Cloudinary; job dọn ảnh tạm hết hạn.</li>
<li>Viết test unit, integration và E2E cho OCR và lưu trữ Cloudinary.</li>
<li>Cập nhật README, NOTES và checklist bảo mật.</li>
</ul>
<p><b>Xong khi:</b> quét OCR chạy đúng trên ảnh CCCD thật, ảnh được lưu trên Cloudinary,
xoá giấy tờ xoá ảnh trên Cloudinary, OCR thất bại không chặn người dùng lưu giấy tờ.</p>

<h3>9.8 Điểm chưa rõ cần xác nhận</h3>
<ul>
<li>Có dùng SDK <code>CloudinaryDotNet</code> hay raw HTTP với chữ ký SHA-1? Đề xuất: raw
HTTP.</li>
<li>Đã có dữ liệu ảnh trên đĩa để cần chuyển sang Cloudinary chưa?</li>
<li>Quy tắc suy luận năm 2 chữ số cho ngày sinh CCCD có được chấp nhận không?</li>
<li>Có hỗ trợ OCR mặt sau CMND cũ không, hay chỉ CCCD?</li>
<li>Endpoint #10 dùng redirect 307 hay stream proxy? Đề xuất: stream proxy.</li>
</ul>
"""