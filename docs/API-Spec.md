# API Specification – User Management

- **Version:** v1
- **Base URL:** `https://{host}/api/v1`
- **Content-Type:** JSON UTF-8, ngoại trừ upload sử dụng `multipart/form-data`
- **API documentation:** `/swagger` trong Development

## 1. Quy ước chung

### 1.1. Kiểu dữ liệu

- ID: GUID.
- Date: `yyyy-MM-dd`.
- DateTime: ISO 8601 UTC, ví dụ `2026-10-05T03:15:00Z`.
- JSON property: camelCase.
- Gender: `Male`, `Female`, `Other`.
- DocumentType: `CCCD`, `CMND`, `Passport`.
- Giá trị enum lưu dạng số trong database nhưng truyền dạng chuỗi qua API.

### 1.2. Phân trang

Request query:

| Parameter | Kiểu | Mặc định | Quy tắc |
|---|---|---|---|
| page | int | 1 | >= 1 |
| pageSize | int | 10 | 1–100 |

Response:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 10,
  "totalItems": 0,
  "totalPages": 0
}
```

### 1.3. Quy ước HTTP status

| Status | Ý nghĩa |
|---|---|
| 200 | GET hoặc PUT thành công |
| 201 | POST thành công, kèm header Location |
| 204 | DELETE thành công, không có response body |
| 304 | Ảnh chưa thay đổi theo conditional request |
| 400 | Validation hoặc query parameter không hợp lệ |
| 404 | Không tìm thấy User, giấy tờ hoặc ảnh |
| 409 | Trùng dữ liệu hoặc vi phạm unique constraint nghiệp vụ |
| 413 | Request upload vượt giới hạn tổng dung lượng |
| 415 | Upload không dùng multipart/form-data |
| 500 | Lỗi không dự kiến |

### 1.4. Định dạng lỗi

Sử dụng Problem Details theo RFC 9457, bổ sung `errors` và `traceId`.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Dữ liệu không hợp lệ.",
  "status": 400,
  "errors": {
    "email": ["Email không hợp lệ."],
    "fullName": ["Họ tên là bắt buộc."]
  },
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00"
}
```

Quy ước:

- Tên field trong `errors` là camelCase.
- HTTP 400 dùng cho dữ liệu hoặc tham số không hợp lệ.
- HTTP 409 dùng cho email, số điện thoại, loại giấy tờ hoặc số giấy tờ bị trùng.
- HTTP 413 dùng cho tổng request upload vượt giới hạn.
- HTTP 415 dùng khi request upload không phải multipart/form-data.
- HTTP 500 không tiết lộ stack trace hoặc thông tin kỹ thuật nhạy cảm.
- Không trả lỗi database thô cho client.

## 2. Danh sách endpoint

| # | Method | Endpoint | Chức năng |
|---|---|---|---|
| 1 | GET | `/users` | Danh sách, tìm kiếm, phân trang |
| 2 | GET | `/users/{id}` | Chi tiết User kèm giấy tờ |
| 3 | POST | `/users` | Tạo User |
| 4 | PUT | `/users/{id}` | Cập nhật User |
| 5 | DELETE | `/users/{id}` | Xóa mềm User và xóa giấy tờ |
| 6 | GET | `/users/{userId}/identity-documents` | Danh sách giấy tờ |
| 7 | POST | `/users/{userId}/identity-documents` | Thêm giấy tờ và upload ảnh |
| 8 | PUT | `/users/{userId}/identity-documents/{documentId}` | Sửa giấy tờ/thay ảnh |
| 9 | DELETE | `/users/{userId}/identity-documents/{documentId}` | Xóa giấy tờ và ảnh |
| 10 | GET | `/users/{userId}/identity-documents/{documentId}/images/{side}` | Lấy ảnh |
| 11 | GET | `/users/{userId}/identity-documents/{documentId}` | Chi tiết một giấy tờ |

## 3. Chi tiết endpoint

### API-01 – Danh sách User

`GET /users`

Query parameters:

| Parameter | Kiểu | Mặc định | Mô tả |
|---|---|---|---|
| keyword | string | Không lọc | Tìm theo họ tên, email, số điện thoại; tối đa 100 ký tự |
| page | int | 1 | Trang hiện tại |
| pageSize | int | 10 | 1–100 |
| sortBy | string | createdAt | `fullName`, `email`, `createdAt` |
| sortOrder | string | desc | `asc` hoặc `desc` |

Quy tắc:

- Chỉ trả User chưa bị xóa.
- Họ tên tìm kiếm không phân biệt dấu.
- Email tìm kiếm không phân biệt hoa thường.
- Số điện thoại được chuẩn hóa theo BR-02.
- Sắp xếp phải thêm `Id` làm khóa phụ để đảm bảo ổn định.
- `sortBy` và `sortOrder` phải được kiểm tra bằng danh sách giá trị cho phép.
- Tham số không hợp lệ trả 400.

Response 200:

```json
{
  "items": [
    {
      "id": "3f2b6c1e-8a4d-4c55-9a1e-2d7f5b9c0a11",
      "fullName": "Nguyễn Văn A",
      "email": "a.nguyen@example.com",
      "phoneNumber": "0912345678",
      "gender": "Male",
      "identityDocumentCount": 2,
      "createdAt": "2026-10-05T03:15:00Z"
    }
  ],
  "page": 1,
  "pageSize": 10,
  "totalItems": 57,
  "totalPages": 6
}
```

### API-02 – Chi tiết User

`GET /users/{id}`

Path parameter `id` là GUID.

Status: 200, 404.

Response 200:

```json
{
  "id": "3f2b6c1e-8a4d-4c55-9a1e-2d7f5b9c0a11",
  "fullName": "Nguyễn Văn A",
  "email": "a.nguyen@example.com",
  "phoneNumber": "0912345678",
  "dateOfBirth": "1995-04-20",
  "gender": "Male",
  "address": "12 Lê Lợi, Quận 1, TP.HCM",
  "createdAt": "2026-10-05T03:15:00Z",
  "updatedAt": null,
  "identityDocuments": [
    {
      "id": "9c1d4e7a-52b3-4f0e-8d36-6a0b1c2d3e4f",
      "documentType": "CCCD",
      "documentNumber": "079095001234",
      "issuedDate": "2021-05-10",
      "expiryDate": "2036-04-20",
      "hasBackImage": true,
      "frontImageUrl": "/api/v1/users/3f2b6c1e-8a4d-4c55-9a1e-2d7f5b9c0a11/identity-documents/9c1d4e7a-52b3-4f0e-8d36-6a0b1c2d3e4f/images/front",
      "backImageUrl": "/api/v1/users/3f2b6c1e-8a4d-4c55-9a1e-2d7f5b9c0a11/identity-documents/9c1d4e7a-52b3-4f0e-8d36-6a0b1c2d3e4f/images/back",
      "createdAt": "2026-10-05T03:20:00Z",
      "updatedAt": null
    }
  ]
}
```

Quy tắc:

- `identityDocuments` là mảng rỗng nếu User chưa có giấy tờ.
- `backImageUrl` là null nếu giấy tờ không có ảnh mặt sau.
- Không trả đường dẫn file vật lý.
- User không tồn tại hoặc đã xóa trả 404.

### API-03 – Tạo User

`POST /users`

Content-Type: `application/json`

Request:

```json
{
  "fullName": "Nguyễn Văn A",
  "email": "a.nguyen@example.com",
  "phoneNumber": "0912345678",
  "dateOfBirth": "1995-04-20",
  "gender": "Male",
  "address": "12 Lê Lợi, Quận 1, TP.HCM"
}
```

Validation:

- `fullName`: bắt buộc, 2–100 ký tự sau trim.
- `email`: bắt buộc, hợp lệ, tối đa 255 ký tự.
- `phoneNumber`: bắt buộc, chuẩn hóa và kiểm tra theo BR-02.
- `dateOfBirth`: tùy chọn, từ 1900-01-01 đến hôm nay.
- `gender`: tùy chọn, chỉ `Male`, `Female`, `Other`.
- `address`: tùy chọn, tối đa 500 ký tự.

Status: 201, 400, 409.

- 201: trả representation của User như API-02, `identityDocuments` là mảng rỗng.
- Header `Location`: `/api/v1/users/{id}`.
- 409: email hoặc số điện thoại đã tồn tại.

### API-04 – Cập nhật User

`PUT /users/{id}`

Content-Type: `application/json`

Request body có cùng cấu trúc API-03.

Quy tắc:

- PUT ghi đè toàn bộ thông tin được quản lý bởi request.
- Trường tùy chọn bỏ trống được lưu thành null.
- Email và số điện thoại được kiểm tra trùng, loại trừ chính User đang cập nhật.
- Chuẩn hóa dữ liệu trước khi lưu.
- Cập nhật `UpdatedAt` theo UTC.

Status: 200, 400, 404, 409.

Response 200: representation của User như API-02.

### API-05 – Xóa User

`DELETE /users/{id}`

Quy tắc:

1. Tìm User chưa bị xóa.
2. Đánh dấu `IsDeleted=true`.
3. Ghi `DeletedAt` theo UTC.
4. Xóa vật lý các bản ghi IdentityDocuments và file ảnh liên quan.
5. Lỗi xóa file phải được ghi log, không làm thất bại thao tác xóa User.

Status: 204, 404.

Không trả body khi thành công.

### API-06 – Danh sách giấy tờ

`GET /users/{userId}/identity-documents`

Status: 200, 404.

- Trả danh sách giấy tờ của User.
- Mỗi phần tử có cùng cấu trúc với một phần tử trong `identityDocuments` của API-02.
- User không tồn tại hoặc đã xóa trả 404.

### API-07 – Thêm giấy tờ

`POST /users/{userId}/identity-documents`

Content-Type: `multipart/form-data`

Fields:

| Field | Kiểu | Bắt buộc | Quy tắc |
|---|---|---|---|
| documentType | string | Có | CCCD, CMND, Passport |
| documentNumber | string | Có | Theo BR-08 |
| issuedDate | date | Không | `yyyy-MM-dd` |
| expiryDate | date | Không | `yyyy-MM-dd` |
| frontImage | file | Có | JPG/PNG, tối đa 5 MB |
| backImage | file | Với CCCD/CMND | JPG/PNG, tối đa 5 MB |

Quy tắc:

- CCCD/CMND bắt buộc `frontImage` và `backImage`.
- Passport bắt buộc `frontImage`, không gửi `backImage`.
- Kiểm tra magic bytes, kích thước ảnh và số pixel.
- Kiểm tra số lượng giấy tờ và tính duy nhất.
- Không tin tên file hoặc Content-Type do client gửi.
- Tổng request vượt `FileStorage:MaxRequestBodyBytes` trả 413.
- Upload không phải multipart/form-data trả 415.

Status: 201, 400, 404, 409, 413, 415.

Response 201: representation của giấy tờ vừa tạo, cùng cấu trúc phần tử `identityDocuments` của API-02.

### API-08 – Cập nhật giấy tờ/thay ảnh

`PUT /users/{userId}/identity-documents/{documentId}`

Content-Type: `multipart/form-data`

Fields:

| Field | Kiểu | Bắt buộc | Quy tắc |
|---|---|---|---|
| documentNumber | string | Có | Theo BR-08 |
| issuedDate | date | Không | `yyyy-MM-dd` |
| expiryDate | date | Không | `yyyy-MM-dd` |
| frontImage | file | Không | Ảnh mới nếu cần thay |
| backImage | file | Không | Ảnh mới nếu cần thay |

Không nhận `documentType` trong API này.

Quy tắc:

- Không thay đổi loại giấy tờ.
- Ảnh không gửi lên được giữ nguyên.
- Nếu có ảnh mới, ghi file mới trước, cập nhật database sau, rồi xóa file cũ.
- Nếu cập nhật database thất bại, dọn file mới và giữ nguyên dữ liệu/ảnh cũ.
- Kiểm tra tính duy nhất của số giấy tờ.
- Kiểm tra request size và content type.

Status: 200, 400, 404, 409, 413, 415.

Response 200: representation giấy tờ sau cập nhật.

### API-09 – Xóa giấy tờ

`DELETE /users/{userId}/identity-documents/{documentId}`

Quy tắc:

- Xác nhận giấy tờ thuộc User.
- Xóa bản ghi database trước, xóa file sau.
- Lỗi xóa file chỉ ghi log.
- URL ảnh sau khi xóa trả 404.

Status: 204, 404.

### API-10 – Lấy ảnh giấy tờ

`GET /users/{userId}/identity-documents/{documentId}/images/{side}`

`side` chỉ nhận `front` hoặc `back`.

Status: 200, 304, 404.

Response 200:

- Body là nội dung file ảnh.
- Content-Type là `image/jpeg` hoặc `image/png`.
- Có `ETag`, `Last-Modified`, `Cache-Control: private, no-cache`.
- Hỗ trợ `If-None-Match`, trả 304 khi ảnh chưa thay đổi.
- Không tìm thấy User, giấy tờ, ảnh hoặc side không hợp lệ thì trả 404.
- Không trả đường dẫn file vật lý.
- Không cho truy cập file tùy ý thông qua path do client cung cấp.
- Khi ảnh bị thay thế hoặc giấy tờ bị xóa, URL phải phản ánh trạng thái mới ngay; không để cache cũ làm lộ ảnh đã xóa.

### API-11 – Chi tiết giấy tờ

`GET /users/{userId}/identity-documents/{documentId}`

Status: 200, 404.

Trả về một giấy tờ với cấu trúc như phần tử `identityDocuments` của API-02. Dùng để điền form sửa.

Trả 404 khi User không tồn tại, đã xóa, giấy tờ không tồn tại hoặc giấy tờ không thuộc User.

## 4. Quy tắc xử lý file và transaction

### Thêm giấy tờ

1. Kiểm tra User tồn tại và chưa bị xóa.
2. Validate metadata.
3. Validate file theo magic bytes, dung lượng, kích thước và định dạng.
4. Kiểm tra trùng nghiệp vụ.
5. Ghi file mới bằng tên server sinh.
6. Ghi IdentityDocument trong transaction.
7. Nếu database thất bại, rollback và xóa file vừa tạo.
8. Nếu unique constraint bị vi phạm do request đồng thời, trả 409 và dọn file mới.

### Thay ảnh

1. Validate ảnh mới.
2. Ghi file mới với tên mới.
3. Cập nhật đường dẫn trong database.
4. Commit database.
5. Xóa file cũ.
6. Nếu bước cập nhật database thất bại, xóa file mới, giữ nguyên file cũ.
7. Nếu xóa file cũ thất bại sau khi commit, ghi log để xử lý sau.

### Xóa giấy tờ/User

1. Xóa bản ghi database hoặc đánh dấu User đã xóa theo nghiệp vụ.
2. Xóa các file liên quan sau khi database thành công.
3. Nếu xóa file thất bại, ghi log.
4. Không rollback thao tác database chỉ vì file cũ không xóa được.

## 5. Bảo mật và cấu hình

- Phiên bản demo chưa có xác thực; không public ra Internet.
- CORS chỉ cho origin Frontend.
- Ảnh lưu ngoài `wwwroot`.
- File chỉ được đọc thông qua API đã xác định.
- Dùng ILogger và traceId.
- Không trả stack trace.
- `FileStorage:RootPath` nằm ngoài web root.
- `FileStorage:MaxFileSizeBytes = 5242880`.
- `FileStorage:MaxRequestBodyBytes = 11534336`.
- IIS/reverse proxy phải có giới hạn request tương thích với cấu hình ứng dụng.

## 6. Quy tắc tương thích

- Không đổi route, tên field hoặc enum khi chưa được xác nhận.
- Mọi thay đổi contract phải cập nhật tài liệu API và frontend tương ứng.
- Backend là nguồn xác thực cuối cùng cho mọi quy tắc nghiệp vụ.
- Không tin validation phía Frontend thay cho Backend.