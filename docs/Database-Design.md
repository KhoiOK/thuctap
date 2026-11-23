# Database Design – User Management

- **Database:** SQL Server
- **ORM:** Entity Framework Core 10
- **Approach:** Code-First + Migrations
- **Timezone:** Lưu thời gian UTC
- **Primary key:** GUID / `uniqueidentifier`

## 1. Quy ước thiết kế

1. Tên bảng và entity sử dụng PascalCase, số nhiều.
2. Khóa chính GUID được sinh từ ứng dụng.
3. Các trường thời gian dùng `datetime2` và lưu UTC, ngoại trừ ngày sinh/ngày cấp/ngày hết hạn dùng kiểu `date`.
4. Enum lưu dạng số trong database, API truyền chuỗi.
5. Database không lưu binary của ảnh.
6. Đường dẫn ảnh lưu tương đối so với thư mục gốc FileStorage.
7. Không dùng cascade delete cho quan hệ User–IdentityDocuments.
8. User bị xóa mềm bằng `IsDeleted`.
9. Email và số điện thoại chỉ phải duy nhất trong nhóm User chưa bị xóa.
10. Mỗi User tối đa một giấy tờ cho mỗi loại.

## 2. Bảng Users

| Cột | SQL Server | Null | Ràng buộc / mô tả |
|---|---|---|---|
| Id | uniqueidentifier | No | PK, sinh ở ứng dụng |
| FullName | nvarchar(100) | No | Họ tên |
| FullNameSearch | nvarchar(100) | No | Họ tên chuẩn hóa để tìm kiếm không dấu |
| Email | nvarchar(255) | No | Lưu chữ thường |
| PhoneNumber | varchar(15) | No | Số điện thoại đã chuẩn hóa |
| DateOfBirth | date | Yes | Ngày sinh |
| Gender | tinyint | Yes | 1=Male, 2=Female, 3=Other |
| Address | nvarchar(500) | Yes | Địa chỉ |
| IsDeleted | bit | No | Default 0 |
| DeletedAt | datetime2 | Yes | Thời điểm xóa mềm UTC |
| CreatedAt | datetime2 | No | Thời điểm tạo UTC |
| UpdatedAt | datetime2 | Yes | Thời điểm cập nhật UTC |

### 2.1. Chuẩn hóa FullNameSearch

- Trim khoảng trắng đầu/cuối.
- Chuyển về chữ thường.
- Bỏ dấu tiếng Việt.
- Chuyển `đ` thành `d`.
- Chuẩn hóa khoảng trắng liên tiếp.
- Tính lại khi tạo hoặc cập nhật FullName.

Không dùng giá trị do Frontend gửi để gán trực tiếp cho `FullNameSearch`; Backend tự tính.

### 2.2. Chuẩn hóa Email

- Trim.
- Chuyển chữ thường.
- Validate định dạng.
- Kiểm tra trùng không phân biệt hoa thường.
- Unique index chỉ áp dụng cho User chưa xóa.

### 2.3. Chuẩn hóa PhoneNumber

- Loại khoảng trắng, dấu chấm và dấu gạch ngang.
- Đổi `+84` hoặc `84` thành `0`.
- Validate theo BR-02.
- Lưu giá trị chuẩn hóa.
- Unique index chỉ áp dụng cho User chưa xóa.

## 3. Bảng IdentityDocuments

| Cột | SQL Server | Null | Ràng buộc / mô tả |
|---|---|---|---|
| Id | uniqueidentifier | No | PK, sinh ở ứng dụng |
| UserId | uniqueidentifier | No | FK → Users.Id |
| DocumentType | tinyint | No | 1=CCCD, 2=CMND, 3=Passport |
| DocumentNumber | varchar(20) | No | Số giấy tờ đã chuẩn hóa |
| IssuedDate | date | Yes | Ngày cấp |
| ExpiryDate | date | Yes | Ngày hết hạn |
| FrontImagePath | nvarchar(500) | No | Đường dẫn tương đối ảnh trước/trang thông tin |
| BackImagePath | nvarchar(500) | Yes | Null với Passport |
| CreatedAt | datetime2 | No | Thời điểm tạo UTC |
| UpdatedAt | datetime2 | Yes | Thời điểm cập nhật UTC |

### 3.1. Quy tắc DocumentType

| Giá trị DB | Giá trị API | Quy tắc ảnh |
|---:|---|---|
| 1 | CCCD | Front + Back |
| 2 | CMND | Front + Back |
| 3 | Passport | Front, Back phải null |

### 3.2. Chuẩn hóa DocumentNumber

- Trim khoảng trắng.
- CCCD: đúng 12 chữ số.
- CMND: 9 hoặc 12 chữ số.
- Passport: chuyển chữ hoa, 6–9 ký tự chữ cái hoặc số.
- Số giấy tờ phải duy nhất theo cặp `(DocumentType, DocumentNumber)`.
- Việc kiểm tra phải được thực hiện ở Backend và bảo vệ bằng unique index trong database.

## 4. Quan hệ

```text
Users
  |
  | 1
  |
  | 0..3
  v
IdentityDocuments
```

- Một User có từ 0 đến 3 IdentityDocuments.
- `IdentityDocuments.UserId` tham chiếu `Users.Id`.
- Dùng `ON DELETE RESTRICT`/`NO ACTION`, không dùng cascade.
- Việc xóa giấy tờ và file ảnh do Service xử lý rõ ràng.
- Xóa mềm User không xóa dòng Users khỏi database.

## 5. Index và constraint

| Tên | Bảng/cột | Loại | Điều kiện/mục đích |
|---|---|---|---|
| PK_Users | Users(Id) | Primary key | Định danh User |
| UX_Users_Email | Users(Email) | Unique filtered | `IsDeleted = 0` |
| UX_Users_PhoneNumber | Users(PhoneNumber) | Unique filtered | `IsDeleted = 0` |
| IX_Users_FullName | Users(FullName, Id) | Nonclustered filtered | `IsDeleted = 0`, hỗ trợ sắp xếp |
| IX_Users_CreatedAt | Users(CreatedAt, Id) | Nonclustered filtered | `IsDeleted = 0`, sắp xếp mặc định |
| PK_IdentityDocuments | IdentityDocuments(Id) | Primary key | Định danh giấy tờ |
| UX_IdentityDocuments_UserId_DocumentType | IdentityDocuments(UserId, DocumentType) | Unique | Tối đa một loại giấy tờ/User |
| UX_IdentityDocuments_Type_Number | IdentityDocuments(DocumentType, DocumentNumber) | Unique | Số giấy tờ duy nhất theo loại |
| FK_IdentityDocuments_Users | IdentityDocuments(UserId) | Foreign key | Tham chiếu Users.Id, không cascade |

### 5.1. Global query filter

Entity User phải có global query filter:

```csharp
builder.HasQueryFilter(u => !u.IsDeleted);
```

Các truy vấn thông thường không trả User đã xóa. Những nghiệp vụ đặc biệt cần truy cập dữ liệu đã xóa phải được xem xét rõ ràng, không được vô tình bỏ qua filter.

### 5.2. Filtered unique index

Email và PhoneNumber phải sử dụng filtered unique index tương đương:

```sql
CREATE UNIQUE INDEX UX_Users_Email
ON Users(Email)
WHERE IsDeleted = 0;

CREATE UNIQUE INDEX UX_Users_PhoneNumber
ON Users(PhoneNumber)
WHERE IsDeleted = 0;
```

Tạo index qua EF Core Migration. Không chỉ kiểm tra trùng ở tầng ứng dụng vì có thể xảy ra request đồng thời.

## 6. Lưu trữ ảnh

### 6.1. Cấu hình

```json
{
  "FileStorage": {
    "RootPath": "D:\\AppData\\UserManagement",
    "MaxFileSizeBytes": 5242880,
    "MaxRequestBodyBytes": 11534336
  }
}
```

Đây là ví dụ cấu hình. Môi trường triển khai phải chọn thư mục có quyền ghi phù hợp, không nằm trong `wwwroot`.

### 6.2. Cấu trúc thư mục

```text
{RootPath}/
  identity-documents/
    {userId}/
      {documentId}-front-{guid}.jpg
      {documentId}-back-{guid}.png
```

- `{guid}` được sinh mới cho mỗi lần upload.
- Extension chỉ được xác định từ nội dung ảnh đã xác minh.
- Không dùng tên file client gửi để xây dựng đường dẫn.
- Đường dẫn lưu DB là tương đối so với RootPath.

Ví dụ:

```text
identity-documents/3f2b.../9c1d...-front-7a40....jpg
```

### 6.3. Quy tắc upload

- JPG/JPEG/PNG.
- Tối đa 5 MB mỗi ảnh.
- Tối đa 8.000 × 8.000 pixel.
- Kiểm tra magic bytes.
- Không tin Content-Type hoặc extension của client.
- Nên loại bỏ EXIF.
- Không tải toàn bộ ảnh lớn vào bộ nhớ nếu có thể xử lý bằng stream.
- Không để đường dẫn file vật lý xuất hiện trong API response.

### 6.4. Thứ tự ghi và xóa

**Thêm giấy tờ**

1. Validate metadata và file.
2. Ghi file mới.
3. Tạo bản ghi trong transaction.
4. Commit.
5. Nếu DB thất bại, rollback và xóa file mới.

**Thay ảnh**

1. Ghi file mới.
2. Cập nhật đường dẫn trong DB.
3. Commit.
4. Xóa file cũ.
5. Nếu DB thất bại, xóa file mới và giữ ảnh cũ.

**Xóa giấy tờ/User**

1. Xóa bản ghi giấy tờ hoặc đánh dấu User đã xóa.
2. Commit database.
3. Xóa file liên quan.
4. Nếu xóa file thất bại, ghi log và xử lý dọn dẹp sau.

## 7. Thời gian

- Lưu CreatedAt, UpdatedAt, DeletedAt theo UTC.
- Dùng `DateTime.UtcNow` hoặc `TimeProvider` được inject để thuận tiện kiểm thử.
- Ngày sinh, ngày cấp, ngày hết hạn dùng `DateOnly` ở C# nếu phù hợp.
- Chuyển đổi UTC+7 chỉ ở lớp hiển thị.
- Khi kiểm tra ngày hết hạn, sử dụng ngày hiện tại theo múi giờ nghiệp vụ `Asia/Ho_Chi_Minh`.

## 8. Entity và cấu hình EF Core

Các entity tối thiểu:

- `User`
- `IdentityDocument`

Các lớp cấu hình:

- `UserConfiguration : IEntityTypeConfiguration<User>`
- `IdentityDocumentConfiguration : IEntityTypeConfiguration<IdentityDocument>`

DbContext:

- `ApplicationDbContext`
- Khai báo DbSet cho Users và IdentityDocuments.
- Áp dụng cấu hình bằng `ApplyConfigurationsFromAssembly`.
- Thiết lập quan hệ và delete behavior.
- Thiết lập filtered unique index bằng Fluent API.

## 9. Migration và khởi tạo

- Sử dụng EF Core 10.
- Không sửa trực tiếp database production bằng tay.
- Mọi thay đổi schema phải có Migration.
- Migration đầu tiên tạo đủ bảng, index, constraint và quan hệ.
- README phải hướng dẫn `dotnet ef migrations add InitialCreate` và `dotnet ef database update`.
- Không tự động xóa database khi ứng dụng khởi động.

## 10. Tiêu chí nghiệm thu database

- Tạo database mới được từ Migration.
- Đủ bảng và trường như tài liệu.
- Unique index hoạt động đúng.
- User đã xóa không xuất hiện trong truy vấn thông thường.
- Mỗi User không thể có hai giấy tờ cùng loại.
- Không thể đăng ký trùng số giấy tờ theo loại.
- Xóa User không cascade ngoài ý muốn.
- Database chỉ lưu đường dẫn ảnh, không lưu binary ảnh.
- Tất cả thời điểm được lưu UTC.