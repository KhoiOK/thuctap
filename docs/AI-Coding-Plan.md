# AI Coding Plan – User Management

- **Dự án:** User Management
- **Backend:** .NET 10 Web API
- **Frontend:** ASP.NET Core MVC + Tailwind CSS CLI
- **Database:** SQL Server + EF Core 10
- **Tài liệu đầu vào:** Toàn bộ file trong thư mục `docs/`

## 1. Mục tiêu

Triển khai hệ thống quản lý User hoàn chỉnh theo các tài liệu đặc tả đã được phê duyệt.

AI Agent phải tạo code chạy được, không chỉ tạo ví dụ minh họa hoặc mã giả.

Hệ thống gồm hai ứng dụng độc lập:

- `UserManagement.Api`: Backend REST API.
- `UserManagement.Web`: Frontend ASP.NET Core MVC.

## 2. Quy tắc bắt buộc cho AI Agent

### 2.1. Đọc tài liệu trước khi code

Trước khi thực hiện bất kỳ thay đổi nào:

1. Đọc `docs/SRS.md`.
2. Đọc `docs/API-Spec.md`.
3. Đọc `docs/Database-Design.md`.
4. Đọc `docs/UI-Spec.md`.
5. Đọc `docs/AI-Coding-Plan.md`.
6. Kiểm tra workspace hiện tại để xác định đã có project hoặc code nào.
7. Báo cáo ngắn gọn cấu trúc dự kiến và các điểm chưa rõ trước khi triển khai.

Không yêu cầu người dùng cung cấp lại thông tin đã có trong tài liệu.

### 2.2. Không tự ý thay đổi yêu cầu

- Không đổi .NET 10, SQL Server, EF Core 10 hoặc Tailwind CSS CLI.
- Không đổi route, HTTP method, tên field hoặc enum đã đặc tả.
- Không tự thêm đăng nhập, phân quyền hoặc chức năng ngoài phạm vi.
- Không tự đổi quan hệ database, unique index hoặc quy tắc xóa.
- Không lưu ảnh vào database hoặc thư mục `wwwroot`.
- Không thay thế Backend API bằng truy vấn database từ Frontend.
- Không tạo repository pattern phức tạp khi chưa có yêu cầu.
- Không thêm thư viện mới nếu tính năng có thể triển khai bằng công cụ hiện có và thư viện mới không cần thiết.
- Không sửa tài liệu gốc để hợp thức hóa code sai.

### 2.3. Cách xử lý điểm chưa rõ

Nếu có mâu thuẫn hoặc thiếu thông tin:

1. Ghi rõ file và mục liên quan.
2. Nêu phương án đề xuất và ảnh hưởng.
3. Không triển khai phần có thể gây thay đổi contract hoặc dữ liệu khi chưa được xác nhận.
4. Có thể tiếp tục các phần độc lập không bị ảnh hưởng.

Ghi nhận các điểm chưa rõ trong `NOTES.md`. Không tự suy đoán những quyết định có ảnh hưởng lớn.

### 2.4. Quy tắc chất lượng code

- Code phải biên dịch được.
- Sử dụng Dependency Injection.
- Sử dụng async/await cho I/O.
- Hỗ trợ CancellationToken ở các luồng phù hợp.
- Sử dụng DTO riêng cho API request/response.
- Không trả trực tiếp EF entity ra API.
- Validate ở Backend.
- Xử lý lỗi tập trung và dùng Problem Details.
- Không để lộ stack trace.
- Không hard-code connection string, API URL hoặc file path.
- Không đưa secret vào source control.
- Không xóa hoặc ghi đè code hiện có nếu chưa kiểm tra tác động.
- Không tạo các lớp chỉ để làm kiến trúc phức tạp hơn mà không có mục đích cụ thể.

## 3. Cấu trúc solution mục tiêu

```text
UserManagement/
├── docs/
│   ├── SRS.md
│   ├── API-Spec.md
│   ├── Database-Design.md
│   ├── UI-Spec.md
│   └── AI-Coding-Plan.md
├── src/
│   ├── UserManagement.Api/
│   │   ├── Controllers/
│   │   ├── DTOs/
│   │   │   ├── Users/
│   │   │   └── IdentityDocuments/
│   │   ├── Data/
│   │   │   ├── ApplicationDbContext.cs
│   │   │   └── Configurations/
│   │   ├── Entities/
│   │   ├── Services/
│   │   ├── Validators/
│   │   ├── Exceptions/
│   │   ├── Middleware/
│   │   ├── FileStorage/
│   │   ├── Properties/
│   │   ├── Program.cs
│   │   └── appsettings.json
│   └── UserManagement.Web/
│       ├── Controllers/
│       ├── Services/
│       ├── ViewModels/
│       ├── Views/
│       ├── wwwroot/
│       ├── Program.cs
│       └── appsettings.json
├── tests/
│   ├── UserManagement.Api.Tests/
│   └── UserManagement.Web.Tests/
├── UserManagement.sln
├── README.md
└── NOTES.md
```

Đây là cấu trúc mục tiêu, không phải yêu cầu tạo mọi file ngay từ đầu. Chỉ tạo các file thực sự cần thiết. Nếu workspace đã có cấu trúc hợp lý, hãy giữ nguyên và chỉ điều chỉnh khi cần.

## 4. Kế hoạch triển khai

### Phase 0 – Khảo sát và lập kế hoạch

Mục tiêu:

- Đọc toàn bộ tài liệu.
- Kiểm tra workspace.
- Xác định các điểm chưa rõ.
- Xác nhận cấu trúc solution.

Thực hiện:

1. Kiểm tra các project và file hiện có.
2. Xác định phiên bản SDK .NET.
3. Kiểm tra khả năng kết nối SQL Server.
4. Lập danh sách project, package và thành phần cần tạo.
5. Báo cáo kế hoạch trước khi thực hiện.

Điều kiện hoàn thành:

- Không còn mâu thuẫn chưa được nhận diện.
- Có kế hoạch thực hiện theo đúng các phase.
- Không ghi đè project hiện có ngoài phạm vi đã thống nhất.

### Phase 1 – Khởi tạo solution

Mục tiêu:

Tạo hai ứng dụng độc lập và cấu hình môi trường phát triển.

Thực hiện:

1. Tạo solution `UserManagement.sln`.
2. Tạo `UserManagement.Api` bằng ASP.NET Core Web API .NET 10.
3. Tạo `UserManagement.Web` bằng ASP.NET Core MVC .NET 10.
4. Cấu hình Nullable và ImplicitUsings.
5. Cấu hình Swagger trong Development.
6. Cấu hình appsettings cho connection string, API URL, file storage và CORS.
7. Cấu hình Tailwind CSS CLI cho Frontend.
8. Tạo README cơ bản.

Kiểm tra:

- Cả hai project restore package thành công.
- Cả hai project build thành công.
- Có thể chạy độc lập.
- Không có connection string hoặc secret nhạy cảm bị hard-code.

### Phase 2 – Database và Entity Framework Core

Mục tiêu:

Tạo schema đúng `Database-Design.md`.

Thực hiện:

1. Tạo `User` entity.
2. Tạo `IdentityDocument` entity.
3. Tạo `ApplicationDbContext`.
4. Tạo cấu hình entity bằng Fluent API.
5. Thiết lập quan hệ và delete behavior.
6. Thiết lập global query filter cho User.
7. Tạo các index và constraint.
8. Tạo Migration đầu tiên.
9. Cập nhật database thử nghiệm.
10. Kiểm tra các index trực tiếp trong database.

Kiểm thử tối thiểu:

- Email trùng ở User chưa xóa bị từ chối.
- Email của User đã xóa có thể được sử dụng lại.
- Số điện thoại trùng ở User chưa xóa bị từ chối.
- Một User không thể có hai giấy tờ cùng loại.
- Không thể trùng số giấy tờ theo loại.
- Quan hệ không cascade delete ngoài ý muốn.

Điều kiện hoàn thành:

- Database được tạo bằng Migration.
- Schema, index và constraint đúng đặc tả.
- Không cần chỉnh database thủ công để ứng dụng hoạt động.

### Phase 3 – Backend API và nghiệp vụ User

Mục tiêu:

Triển khai các API-01 đến API-05.

Thực hiện:

1. Tạo DTO cho danh sách, chi tiết, tạo và cập nhật User.
2. Tạo User Service.
3. Tạo Users Controller.
4. Triển khai GET `/users`.
5. Triển khai GET `/users/{id}`.
6. Triển khai POST `/users`.
7. Triển khai PUT `/users/{id}`.
8. Triển khai DELETE `/users/{id}`.
9. Triển khai chuẩn hóa FullNameSearch, Email và PhoneNumber.
10. Triển khai tìm kiếm, phân trang và sắp xếp.
11. Triển khai Problem Details.
12. Chuyển lỗi unique constraint thành HTTP 409.

Yêu cầu:

- Không trả EF entity trực tiếp.
- Chỉ cho phép các trường sort đã quy định.
- Truy vấn chỉ lấy User chưa xóa.
- Sử dụng projection để chỉ lấy dữ liệu cần thiết.
- Tránh tải toàn bộ danh sách vào bộ nhớ trước khi phân trang.
- Sắp xếp ổn định bằng `Id`.
- Các trường tùy chọn và enum phải được validate đúng contract.

Điều kiện hoàn thành:

- API-01 đến API-05 chạy được.
- Response đúng tài liệu API.
- Đã kiểm thử validation, 404 và 409.
- Build thành công.

### Phase 4 – Backend API quản lý giấy tờ và ảnh

Mục tiêu:

Triển khai API-06 đến API-11 và bảo đảm an toàn khi xử lý file.

Thực hiện:

1. Tạo IdentityDocument DTO và Service.
2. Triển khai danh sách giấy tờ.
3. Triển khai thêm giấy tờ.
4. Triển khai cập nhật giấy tờ.
5. Triển khai xóa giấy tờ.
6. Triển khai chi tiết giấy tờ.
7. Triển khai lấy ảnh theo side.
8. Tạo dịch vụ FileStorage.
9. Validate dung lượng, magic bytes, định dạng và kích thước pixel.
10. Tạo tên file ngẫu nhiên và đường dẫn an toàn.
11. Thực hiện quy trình transaction và dọn file khi lỗi.
12. Xử lý ETag, Last-Modified và 304.
13. Ghi log khi xóa file thất bại.
14. Đảm bảo ảnh không được truy cập trực tiếp qua URL công khai.

Kiểm thử tối thiểu:

- CCCD/CMND thiếu ảnh mặt sau bị từ chối.
- Passport có ảnh mặt sau bị từ chối.
- File giả mạo extension bị từ chối.
- File vượt 5 MB bị từ chối.
- Ảnh vượt giới hạn pixel bị từ chối.
- Request vượt giới hạn tổng dung lượng trả 413.
- Sai Content-Type trả 415.
- Trùng giấy tờ trả 409.
- Giấy tờ không thuộc User trả 404.
- Thay ảnh thành công không còn dùng ảnh cũ.
- Cập nhật DB thất bại vẫn giữ được ảnh cũ.
- Xóa giấy tờ thành công làm URL ảnh cũ trả 404.

Điều kiện hoàn thành:

- Đủ 11 endpoint.
- File và database không bị lệch trong các luồng đã kiểm thử.
- Không lưu ảnh trong `wwwroot`.
- Không có đường dẫn file vật lý trong response.

### Phase 5 – Frontend MVC và API client

Mục tiêu:

Tạo lớp giao tiếp API và các thành phần giao diện cơ bản.

Thực hiện:

1. Tạo API client sử dụng `IHttpClientFactory`.
2. Cấu hình Base URL và timeout.
3. Tạo ViewModel riêng cho từng màn hình.
4. Tạo layout chung.
5. Tạo toast và modal xác nhận.
6. Tạo pagination component.
7. Tạo cơ chế parse Problem Details.
8. Cấu hình Tailwind CLI và build CSS.
9. Thêm anti-forgery token cho form POST.

Điều kiện hoàn thành:

- Frontend không truy cập database.
- URL API được lấy từ cấu hình.
- Xử lý được lỗi kết nối, timeout và lỗi validation.
- Không hiển thị stack trace.

### Phase 6 – Triển khai các màn hình

Thực hiện theo thứ tự:

1. SCR-01 – Danh sách và tìm kiếm User.
2. SCR-03 – Thêm User.
3. SCR-04 – Sửa User.
4. SCR-02 – Chi tiết User.
5. SCR-05 – Modal xác nhận xóa.
6. SCR-06 – Thêm/Sửa giấy tờ.
7. Proxy ảnh và lightbox.

Yêu cầu:

- Route đúng `UI-Spec.md`.
- Query string giữ được trạng thái tìm kiếm và phân trang.
- Form có validation và hiển thị lỗi theo trường.
- Có trạng thái loading, empty, success và error.
- Không gửi form nhiều lần do nhấn nút liên tiếp.
- Không cho phép thay đổi loại giấy tờ khi sửa.
- Ảnh không được gửi lên nếu người dùng chưa chọn và xác nhận lưu.
- Hỗ trợ responsive từ 360 px.
- Tất cả thao tác dữ liệu đi qua API.

### Phase 7 – Kiểm thử tích hợp

Mục tiêu:

Xác minh Frontend, Backend, SQL Server và FileStorage hoạt động cùng nhau.

Kiểm tra các luồng:

1. Tạo User hợp lệ.
2. Tạo User trùng email.
3. Tạo User trùng số điện thoại.
4. Tìm kiếm User không dấu.
5. Phân trang và sắp xếp.
6. Xem chi tiết User.
7. Thêm giấy tờ CCCD.
8. Thêm giấy tờ CMND.
9. Thêm Passport.
10. Từ chối giấy tờ thiếu ảnh bắt buộc.
11. Thay ảnh và xác nhận ảnh mới được hiển thị.
12. Xóa giấy tờ.
13. Xóa User.
14. Kiểm tra User đã xóa không truy cập được.
15. Kiểm tra file cũ được dọn hoặc có log để xử lý sau.
16. Kiểm tra timeout và lỗi API.
17. Kiểm tra lỗi upload vượt dung lượng.
18. Kiểm tra không thể truy cập file qua URL công khai.

Điều kiện hoàn thành:

- Các luồng chính hoạt động đúng.
- Lỗi được xử lý nhất quán.
- Không có lỗi nghiêm trọng còn tồn tại.
- Các quy tắc nghiệp vụ được kiểm tra ở Backend.

### Phase 8 – Hoàn thiện và bàn giao

Thực hiện:

1. Build toàn bộ solution ở chế độ Release.
2. Chạy test.
3. Kiểm tra cấu hình production.
4. Kiểm tra Tailwind CSS output.
5. Kiểm tra không có secret trong source.
6. Hoàn thiện README.
7. Liệt kê Migration.
8. Liệt kê các endpoint.
9. Liệt kê các trường hợp test đã chạy và kết quả.
10. Ghi rõ các hạn chế chưa giải quyết.

README tối thiểu phải có:

- Yêu cầu môi trường.
- Cách cấu hình SQL Server.
- Cách chạy Migration.
- Cách cấu hình FileStorage.
- Cách chạy Backend API.
- Cách chạy Frontend.
- Cách build Tailwind CSS.
- Cách chạy test.
- Cách truy cập Swagger.
- Lưu ý bảo mật của phiên bản demo chưa có đăng nhập.

## 5. Quy tắc làm việc theo từng giai đoạn

- Mỗi lần chỉ triển khai một phase.
- Sau mỗi phase, báo cáo file đã tạo/sửa.
- Chạy build hoặc test phù hợp trước khi xác nhận hoàn thành phase.
- Không báo build thành công nếu chưa chạy lệnh build và kiểm tra kết quả.
- Không tự động chuyển sang phase tiếp theo khi phase hiện tại chưa đạt điều kiện hoàn thành.
- Nếu build/test lỗi, ưu tiên sửa lỗi của phase hiện tại trước.
- Tránh thay đổi code không liên quan khi sửa lỗi.
- Không xóa test chỉ để làm cho build/test thành công.
- Không bỏ qua validation hoặc thay đổi nghiệp vụ để vượt qua test.

## 6. Lệnh kiểm tra tham khảo

Tại thư mục solution:

```bash
dotnet restore
dotnet build
dotnet test
```

Tạo Migration:

```bash
dotnet ef migrations add InitialCreate \
  --project src/UserManagement.Api \
  --startup-project src/UserManagement.Api
```

Cập nhật database:

```bash
dotnet ef database update \
  --project src/UserManagement.Api \
  --startup-project src/UserManagement.Api
```

Các lệnh trên là ví dụ cho môi trường có cài .NET SDK và `dotnet-ef`. Trên Windows PowerShell, có thể nhập mỗi lệnh trên một dòng hoặc dùng dấu backtick để xuống dòng.

## 7. Checklist hoàn thành cuối cùng

### Backend

- [ ] Đủ 11 API endpoint.
- [ ] Đúng HTTP method, route, request và response.
- [ ] Có Problem Details.
- [ ] Có validation theo BR-01 đến BR-12.
- [ ] Có xử lý 400, 404, 409, 413, 415, 500.
- [ ] Có transaction và dọn file đúng quy tắc.
- [ ] Có logging và traceId.

### Database

- [ ] Có Users và IdentityDocuments.
- [ ] Đủ các cột và kiểu dữ liệu.
- [ ] Có filtered unique index.
- [ ] Có foreign key và delete behavior đúng.
- [ ] Có global query filter.
- [ ] Tạo database được bằng Migration.

### Frontend

- [ ] Có đủ 6 màn hình.
- [ ] Có CRUD User.
- [ ] Có tìm kiếm, phân trang và sắp xếp.
- [ ] Có form giấy tờ và upload ảnh.
- [ ] Có preview và lightbox.
- [ ] Có loading, empty, success và error.
- [ ] Có anti-forgery token.
- [ ] Responsive từ 360 px.

### Bàn giao

- [ ] Build Release thành công.
- [ ] Test đã chạy và có báo cáo.
- [ ] README đầy đủ.
- [ ] Không có secret trong source.
- [ ] File ảnh không nằm trong `wwwroot`.
- [ ] Hạn chế còn lại được ghi rõ.
- [ ] Chưa triển khai xác thực/phân quyền đúng với phạm vi demo.

## 8. Prompt khởi động cho AI Agent

Khi bắt đầu làm việc với Continue hoặc AI Agent khác, sử dụng prompt sau:

> Đọc toàn bộ 5 tài liệu trong thư mục `docs/`. Kiểm tra cấu trúc workspace hiện tại. Chưa viết code ngay.
>
> Hãy thực hiện Phase 0 của `AI-Coding-Plan.md`: tóm tắt yêu cầu, đề xuất cấu trúc solution, xác định package cần thiết và liệt kê các điểm chưa rõ hoặc mâu thuẫn.
>
> Không thay đổi bất kỳ file nào cho đến khi báo cáo Phase 0 hoàn tất. Không tự ý thay đổi các yêu cầu trong tài liệu.

Sau khi Phase 0 được duyệt, yêu cầu Agent triển khai lần lượt từng phase và kiểm tra build/test trước khi tiếp tục.