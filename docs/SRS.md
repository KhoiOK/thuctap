# SRS – Hệ thống quản lý User

- **Mã tài liệu:** SRS-UM-001
- **Phiên bản:** 2.1
- **Trạng thái:** Đặc tả triển khai

## 1. Tổng quan

### 1.1. Mục tiêu

Xây dựng website quản lý User cho Admin, hỗ trợ xem danh sách, tìm kiếm, phân trang, xem chi tiết, thêm, chỉnh sửa, xóa User và quản lý ảnh giấy tờ tùy thân CCCD, CMND, Passport.

### 1.2. Phạm vi

Trong phạm vi:

- Backend REST API.
- Frontend ASP.NET Core MVC.
- SQL Server và Entity Framework Core.
- CRUD User và quản lý giấy tờ tùy thân.
- Upload, xem, thay thế và xóa ảnh giấy tờ.
- Validation, xử lý lỗi, logging và kiểm thử.
- Tài liệu hướng dẫn cấu hình và chạy ứng dụng.

Ngoài phạm vi phiên bản 1.0:

- Đăng nhập, xác thực và phân quyền.
- Import/export Excel.
- Gửi email/SMS.
- Audit log đầy đủ lịch sử thay đổi.
- Đa ngôn ngữ.

### 1.3. Người sử dụng

Admin thao tác qua website. Phiên bản demo chưa có xác thực và phân quyền, vì vậy chỉ được chạy trên máy phát triển hoặc môi trường mạng nội bộ/VPN được kiểm soát. Không công khai ứng dụng trên Internet.

## 2. Công nghệ bắt buộc

| Thành phần | Công nghệ |
|---|---|
| Backend | .NET 10, ASP.NET Core Web API, Controller-based |
| Frontend | ASP.NET Core MVC, Razor Views |
| CSS | Tailwind CSS CLI |
| Database | SQL Server |
| ORM | Entity Framework Core 10, Code-First, Migrations |
| Giao tiếp | HTTP/JSON |
| Upload | multipart/form-data |
| API documentation | Swagger/OpenAPI |
| Unit test | xUnit |

Không sử dụng Tailwind CDN trong production.

## 3. Kiến trúc

```text
Browser
  |
  v
UserManagement.Web
ASP.NET Core MVC + Razor + Tailwind CSS
  |
  | HTTP/JSON, multipart/form-data
  v
UserManagement.Api
ASP.NET Core Web API
  |
  +---- SQL Server
  |
  +---- File Storage ngoài wwwroot
```

Nguyên tắc:

1. Web và API là hai ứng dụng độc lập.
2. Frontend không truy cập trực tiếp database.
3. Frontend gọi Backend API thông qua HttpClient.
4. Backend chịu trách nhiệm nghiệp vụ, validation, database và lưu file.
5. Database chỉ lưu metadata và đường dẫn tương đối của ảnh.
6. Ảnh phải được lưu ngoài `wwwroot`.
7. Connection string, API URL và thư mục lưu ảnh phải lấy từ cấu hình.
8. Backend tổ chức theo Controller → Service → DbContext.
9. Frontend Controller nhận input, gọi API client và trả View.
10. Sử dụng Dependency Injection, async/await, CancellationToken và ILogger.

## 4. Yêu cầu chức năng

### FR-01 – Danh sách User

- Hiển thị User chưa bị xóa.
- Cột: STT, Họ tên, Email, Số điện thoại, Giới tính, Số giấy tờ, Ngày tạo, Thao tác.
- Có phân trang và lựa chọn 10/20/50 dòng.
- Có sắp xếp theo Họ tên, Email, Ngày tạo.
- Mặc định `page=1`, `pageSize=10`, `sortBy=createdAt`, `sortOrder=desc`.
- Thứ tự sắp xếp ổn định bằng khóa phụ `Id`.
- Giữ keyword và bộ lọc khi chuyển trang hoặc sắp xếp.
- Có trạng thái rỗng, không có kết quả và lỗi API.

### FR-02 – Tìm kiếm

- Tìm theo Họ tên, Email, Số điện thoại.
- So khớp dạng contains.
- Họ tên không phân biệt dấu tiếng Việt.
- Email không phân biệt hoa thường.
- Chuẩn hóa số điện thoại theo BR-02.
- Keyword được trim, tối đa 100 ký tự.
- Keyword rỗng tương đương không lọc.
- Lưu trạng thái tìm kiếm trên query string.
- Có nút xóa bộ lọc.

### FR-03 – Chi tiết User

- Hiển thị toàn bộ thông tin User và giấy tờ.
- Trường không có dữ liệu hiển thị `---`.
- Ngày hiển thị `dd/MM/yyyy`.
- Ngày giờ hiển thị theo múi giờ Việt Nam.
- Giấy tờ hết hạn có nhãn `Đã hết hạn`.
- User không tồn tại hoặc đã xóa trả 404.
- Có thao tác Chỉnh sửa, Xóa, Quay lại và Thêm giấy tờ.
- Ẩn Thêm giấy tờ khi đã có đủ ba loại.

### FR-04 – Thêm User

Các trường:

| Trường | Bắt buộc | Quy tắc |
|---|---|---|
| FullName | Có | 2–100 ký tự sau trim |
| Email | Có | Email hợp lệ, tối đa 255 ký tự, duy nhất |
| PhoneNumber | Có | Số di động Việt Nam hợp lệ, duy nhất |
| DateOfBirth | Không | 01/01/1900 đến hôm nay |
| Gender | Không | Male, Female, Other |
| Address | Không | Tối đa 500 ký tự |

Chuẩn hóa dữ liệu trước khi lưu. Thành công chuyển đến trang chi tiết và hiển thị thông báo thành công. Lỗi phải được hiển thị tại trường tương ứng.

### FR-05 – Chỉnh sửa User

- Tải dữ liệu hiện tại lên form.
- Cập nhật toàn bộ trường thông tin User.
- Trường tùy chọn bỏ trống được lưu thành null.
- Kiểm tra email và số điện thoại trùng, loại trừ chính User đang sửa.
- Cập nhật `UpdatedAt` theo UTC.
- Thành công chuyển đến trang chi tiết.
- Phiên bản 1.0 chưa triển khai kiểm soát xung đột cập nhật đồng thời.

### FR-06 – Xóa User

- Hiển thị modal xác nhận.
- Xóa mềm User bằng `IsDeleted=true`, ghi `DeletedAt`.
- Xóa vật lý giấy tờ và ảnh liên quan.
- User đã xóa không hiển thị và không truy cập được qua API.
- Email và số điện thoại được phép sử dụng lại sau khi User bị xóa.
- Xóa file thất bại phải ghi log nhưng không làm thất bại thao tác xóa User.

### FR-07 – Thêm giấy tờ

- Hỗ trợ CCCD, CMND, Passport.
- CCCD/CMND bắt buộc ảnh trước và sau.
- Passport chỉ cần ảnh trang thông tin.
- Cho nhập số giấy tờ, ngày cấp và ngày hết hạn.
- Có xem trước ảnh.
- Kiểm tra loại giấy tờ, số giấy tờ, dung lượng, định dạng và kích thước.
- Mỗi User chỉ có một giấy tờ mỗi loại.
- Số giấy tờ duy nhất theo cặp loại và số.
- Ghi file trước, sau đó ghi database.
- Nếu ghi database thất bại, dọn file mới.
- Vi phạm unique constraint do request đồng thời phải trả 409.

### FR-08 – Quản lý giấy tờ

**FR-08a – Xem ảnh**

- Nhấn ảnh thu nhỏ để mở lightbox.
- Ảnh tải qua API/proxy, không dùng đường dẫn file công khai.
- Hỗ trợ ETag, Last-Modified, Cache-Control và 304.
- Ghi log lượt xem ảnh.

**FR-08b – Sửa giấy tờ**

- Sửa số giấy tờ, ngày cấp, ngày hết hạn và thay ảnh.
- Không đổi loại giấy tờ.
- Ảnh không gửi lên được giữ nguyên.
- Thay ảnh theo thứ tự: ghi file mới → cập nhật DB → xóa file cũ.
- Nếu cập nhật DB thất bại, xóa file mới và giữ ảnh cũ.

**FR-08c – Xóa giấy tờ**

- Xác nhận trước khi xóa.
- Xóa DB trước, xóa file sau.
- Lỗi xóa file chỉ ghi log.
- URL ảnh sau khi xóa phải trả 404.

## 5. Quy tắc nghiệp vụ

### BR-01 – Email

Email duy nhất giữa các User chưa xóa, so sánh không phân biệt hoa thường. Trim và lưu chữ thường.

### BR-02 – Số điện thoại

- Bỏ khoảng trắng, dấu chấm và dấu gạch ngang.
- Đổi tiền tố `+84` hoặc `84` thành `0`.
- Sau chuẩn hóa phải khớp:

```regex
^0[35789][0-9]{8}$
```

Ví dụ `+84 912 345 678` → `0912345678`. `+840912345678` bị từ chối. Số điện thoại duy nhất giữa User chưa xóa.

### BR-03 – Họ tên

2–100 ký tự sau trim.

### BR-04 – Ngày sinh

Từ 01/01/1900 đến hôm nay nếu được cung cấp.

### BR-05 – Xóa User

Xóa mềm User, xóa vật lý giấy tờ và ảnh liên quan.

### BR-06 – Số lượng giấy tờ

Tối đa một giấy tờ mỗi loại trên một User, tổng tối đa ba.

### BR-07 – Ảnh giấy tờ

CCCD/CMND có ảnh trước và sau. Passport chỉ có ảnh trang thông tin.

### BR-08 – Số giấy tờ

- CCCD: 12 chữ số.
- CMND: 9 hoặc 12 chữ số.
- Passport: trim, viết hoa, 6–9 ký tự chữ cái hoặc số.

### BR-09 – Ảnh

- JPG/JPEG/PNG.
- Tối đa 5 MB mỗi ảnh.
- Kích thước tối đa 8.000 × 8.000 pixel.
- Kiểm tra magic bytes, không tin phần mở rộng hoặc Content-Type.
- Nên loại bỏ EXIF.

### BR-10 – Ngày giấy tờ

Ngày cấp không lớn hơn hôm nay. Ngày hết hạn phải lớn hơn hoặc bằng ngày cấp nếu cả hai cùng có.

### BR-11 – Số giấy tờ duy nhất

Cặp `(DocumentType, DocumentNumber)` duy nhất toàn hệ thống. Trùng trả 409 với thông báo `Số giấy tờ đã được đăng ký cho người dùng khác.`

### BR-12 – Giấy tờ hết hạn

Vẫn cho phép lưu. Nếu ngày hết hạn nhỏ hơn hôm nay, hiển thị `Đã hết hạn`.

## 6. Bảo mật và phi chức năng

- Demo không có đăng nhập/phân quyền, chỉ chạy trong môi trường được kiểm soát.
- Dùng HTTPS và CORS chỉ cho phép origin Frontend.
- Mọi POST form ở Frontend phải có anti-forgery token.
- Dùng EF Core truy vấn có tham số.
- Razor phải mã hóa đầu ra.
- Không lưu ảnh trong `wwwroot`.
- Không trả stack trace hoặc thông tin nhạy cảm.
- Không ghi đầy đủ số giấy tờ, email, số điện thoại vào log thông thường.
- Log truy cập ảnh gồm thời điểm, địa chỉ nguồn, UserId, DocumentId và side.
- Tên file lưu do server sinh.
- `returnUrl` chỉ chấp nhận URL nội bộ hợp lệ bắt đầu bằng `/Users`.
- Upload phải truyền stream, tránh đọc toàn bộ file vào bộ nhớ.
- Sao lưu database và file storage đồng bộ.

## 7. Cấu hình

Backend:

| Khóa | Ý nghĩa |
|---|---|
| `ConnectionStrings:Default` | SQL Server |
| `FileStorage:RootPath` | Thư mục lưu ảnh ngoài `wwwroot` |
| `FileStorage:MaxFileSizeBytes` | `5242880` |
| `FileStorage:MaxRequestBodyBytes` | `11534336` |
| `Cors:AllowedOrigins` | Origin Frontend |
| `App:TimeZoneId` | `Asia/Ho_Chi_Minh` |

Frontend:

| Khóa | Ý nghĩa |
|---|---|
| `Api:BaseUrl` | Base URL Backend API |
| `Api:TimeoutSeconds` | Mặc định 30 giây |
| `Display:TimeZoneId` | Múi giờ hiển thị; Windows dùng `SE Asia Standard Time` |

Không hard-code các giá trị trên trong code.

## 8. Tài liệu liên quan

- `./API-Spec.md`: đặc tả 11 endpoint.
- `./Database-Design.md`: bảng, quan hệ, index, lưu file.
- `./UI-Spec.md`: route và 6 màn hình.
- `./AI-Coding-Plan.md`: thứ tự triển khai.

Nếu tài liệu mâu thuẫn, ghi nhận vào `NOTES.md` và yêu cầu xác nhận trước khi triển khai phần bị ảnh hưởng.

## 9. Tiêu chí nghiệm thu tổng thể

- Đủ 11 endpoint và 6 màn hình.
- CRUD, tìm kiếm, phân trang và sắp xếp hoạt động đúng.
- Backend thực thi BR-01 đến BR-12.
- Upload, thay ảnh và xóa ảnh đúng thứ tự.
- Trả lỗi đúng contract.
- Tạo database mới được bằng EF Core Migrations.
- Không công khai file ảnh.
- Build thành công, không có lỗi.
- Có README hướng dẫn cấu hình, chạy ứng dụng và tạo database.
- Có kiểm thử các trường hợp thành công, validation, trùng dữ liệu và lỗi file storage.

## 9. Bổ sung (v2.1) — OCR giấy tờ và lưu trữ ảnh Cloudinary

Bản đặc tả đầy đủ của phần mở rộng nằm trong
`docs/SRS_He_thong_Quan_ly_User_v2_0.pdf` mục 9, và
`docs/OCR-Cloudinary-Spec.md` cho chi tiết kỹ thuật.

### 9.1. Yêu cầu chức năng mới

| Mã | Yêu cầu | API |
|---|---|---|
| FR-09 | Quét tự động thông tin CCCD/CMND từ ảnh mặt trước và mặt sau; kết quả là dữ liệu gợi ý, người dùng PHẢI review trước khi lưu | `POST /users/{userId}/identity-documents/ocr/scan` |
| FR-10 | Lưu trữ ảnh trên Cloudinary; database lưu `public_id` thay cho đường dẫn tương đối | Thay thế API-07 → API-10 |

### 9.2. Quy tắc nghiệp vụ mới

| Mã | Quy tắc |
|---|---|
| BR-13 | Kết quả OCR là dữ liệu gợi ý, phải qua bộ validator trước khi lưu; không tự động lưu |
| BR-14 | `confidence` dưới `Ocr:MinConfidence` (mặc định 0.60) không điền tự động; từ 0.85 trở lên hiện huy hiệu "tin cậy cao" |
| BR-15 | Ảnh tạm nằm ở folder `tmp/{scanId}`, hết hạn sau 24 giờ |
| BR-16 | Xoá giấy tờ sẽ xoá ảnh trên Cloudinary; lỗi xoá chỉ ghi log, không rollback database |
| BR-17 | Ảnh phục vụ qua delivery `authenticated` hoặc signed URL; không lộ `public_id` ra giao diện |

### 9.3. Thay đổi các điều khoản hiện có

| Điều khoản | Bản gốc | v2.1 |
|---|---|---|
| 1.1 | OCR nằm ngoài phạm vi | OCR vào phạm vi, với điều kiện là dữ liệu gợi ý |
| 1.3 | Api → SQL Server + File Storage | Api → SQL Server + Cloudinary |
| 1.4 | `IFileStorageService (LocalFileStorageService)` | `ICloudinaryStorage (CloudinaryStorage)` |
| 1.6 | Khoá `FileStorage:RootPath` | Thêm section `Cloudinary` và `Ocr` |
| 3.1 | Database lưu đường dẫn tương đối | Database lưu `public_id` |
| 3.3 | `FrontImagePath`, `BackImagePath` | `FrontImagePublicId`, `BackImagePublicId` (+ version, kích thước) |
| 3.5 | Thư mục `{RootPath}/identity-documents/...` | Folder `{Folder}/identity-documents/{userId}/{documentId}`, ảnh tạm ở `{Folder}/tmp/{scanId}` |
| 4.1 | Mã lỗi HTTP | Thêm 503 Service Unavailable |
| 4.2 | 11 endpoint | Thêm #12 `ocr/scan` và #13 `ocr/confirm` |
| 5.3 | SCR-06 form giấy tờ | Thêm panel kết quả OCR và nút "Quét tự động" |
| 6.2 | Bảng validation | Thêm trường OCR và quy tắc tin cậy |
| 7 | Checklist nghiệm thu | Thêm checklist FR-09, FR-10, BR-13 → BR-17 |
| 8.2 | Bước 0 → Bước 8 | Thêm Bước 9 (OCR + Cloudinary) |

### 9.4. Endpoint mới

**#12 POST /users/{userId}/identity-documents/ocr/scan**

`multipart/form-data`: `documentType`, `frontImage`, `backImage` (bắt buộc với CCCD/CMND).
Status: 200, 400, 404, 413, 415, 503.

**#13 POST /users/{userId}/identity-documents/ocr/confirm**

`application/json`: `scanId`, `documentType`, `documentNumber`, `issuedDate`, `expiryDate`,
`publicId` tạm của hai mặt ảnh. Status: 201, 400, 404, 409, 413, 415.

### 9.5. Điểm chưa rõ cần xác nhận

- Dùng SDK `CloudinaryDotNet` hay raw HTTP với chữ ký SHA-1? Đề xuất: raw HTTP.
- Đã có dữ liệu ảnh trên đĩa để cần chuyển sang Cloudinary chưa?
- Quy tắc suy luận năm 2 chữ số cho ngày sinh CCCD có được chấp nhận không?
- Có hỗ trợ OCR mặt sau CMND cũ không, hay chỉ CCCD?
- Endpoint #10 dùng redirect 307 hay stream proxy? Đề xuất: stream proxy.
