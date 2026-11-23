# UI Specification – User Management

- **Framework:** ASP.NET Core MVC, Razor Views
- **CSS:** Tailwind CSS CLI
- **Ngôn ngữ:** Tiếng Việt
- **Base route:** `/Users`
- **Backend:** Tất cả thao tác dữ liệu thực hiện qua REST API

## 1. Quy ước giao diện

### 1.1. Layout chung

- Layout chính: `Views/Shared/_Layout.cshtml`.
- Header cố định có tiêu đề `Quản lý User`.
- Nội dung căn giữa, chiều rộng tối đa `max-w-6xl`.
- Footer đơn giản.
- Giao diện sử dụng tiếng Việt.
- Màu chính: `indigo-600`.
- Nút nguy hiểm và lỗi: `red-600`.
- Thông báo thành công: `green-600`.
- Nút lưu/thêm dùng màu chính; nút hủy/quay lại có viền trung tính; nút xóa dùng màu đỏ.

### 1.2. Responsive

- Hỗ trợ chiều rộng từ 360 px.
- Form một cột trên mobile.
- Form hai cột từ breakpoint `md`.
- Bảng danh sách có `overflow-x-auto`.
- Modal và lightbox không vượt quá màn hình.
- Các nút thao tác phải dễ sử dụng trên màn hình cảm ứng.

### 1.3. Trạng thái giao diện

Mỗi màn hình gọi API phải xử lý:

- Đang tải dữ liệu.
- Đang gửi dữ liệu.
- Thành công.
- Lỗi validation.
- Lỗi API hoặc mất kết nối.
- Dữ liệu rỗng.
- Không tìm thấy dữ liệu.

Khi gửi form:

- Disable nút submit để tránh gửi lặp.
- Hiển thị spinner hoặc trạng thái đang xử lý.
- Khi lỗi, khôi phục khả năng thao tác.
- Giữ dữ liệu người dùng đã nhập nếu có thể.

### 1.4. Toast và trợ năng

- Toast ở góc trên bên phải.
- Tự ẩn sau 4 giây.
- Có nút đóng.
- Mỗi input có label.
- Lỗi gắn với input qua `aria-describedby`.
- Modal dùng `role="dialog"`, có tiêu đề, nút đóng và hỗ trợ bàn phím.
- Không chỉ dùng màu sắc để truyền đạt trạng thái.

## 2. Danh sách route

| Method | Route | Màn hình | API |
|---|---|---|---|
| GET | `/Users` | SCR-01 | #1 |
| GET | `/Users/Details/{id}` | SCR-02 | #2, #10 qua proxy |
| GET, POST | `/Users/Create` | SCR-03 | #3 |
| GET, POST | `/Users/Edit/{id}` | SCR-04 | #2, #4 |
| POST | `/Users/Delete/{id}` | SCR-05/action | #5 |
| GET, POST | `/Users/{userId}/Documents/Create` | SCR-06 | #2, #7 |
| GET, POST | `/Users/{userId}/Documents/Edit/{documentId}` | SCR-06 | #11, #8 |
| POST | `/Users/{userId}/Documents/Delete/{documentId}` | SCR-05/action | #9 |
| GET | `/Users/{userId}/Documents/{documentId}/Image/{side}` | Proxy ảnh | #10 |

Trang chủ chuyển hướng đến `/Users`.

Các route POST dùng để xóa phải có anti-forgery token. Không dùng HTTP GET để thực hiện thao tác thay đổi dữ liệu.

## 3. SCR-01 – Danh sách User

Route: `/Users`

### 3.1. Thành phần

- Tiêu đề `Danh sách người dùng`.
- Tổng số User, ví dụ `57 người dùng`.
- Nút `Thêm User`.
- Ô tìm kiếm và nút `Tìm kiếm`.
- Nút `Xóa bộ lọc`, chỉ hiện khi có keyword.
- Bảng dữ liệu.
- Phân trang và lựa chọn số dòng mỗi trang.

### 3.2. Cột bảng

| Cột | Hành vi |
|---|---|
| STT | Số thứ tự theo trang |
| Họ tên | Có thể sắp xếp; nhấn tên để mở chi tiết |
| Email | Có thể sắp xếp |
| Số điện thoại | Hiển thị số đã chuẩn hóa |
| Giới tính | Male → Nam, Female → Nữ, Other → Khác |
| Số giấy tờ | Tổng số giấy tờ hiện có |
| Ngày tạo | Có thể sắp xếp |
| Thao tác | Xem, Sửa, Xóa |

### 3.3. Tìm kiếm và query string

Ví dụ:

```text
/Users?keyword=nguyen&page=2&pageSize=20&sortBy=fullName&sortOrder=asc
```

- Giữ keyword sau khi tìm.
- Khi đổi pageSize, chuyển về page 1.
- Khi đổi sort, giữ keyword và pageSize.
- Khi thay đổi keyword, chuyển về page 1.
- Nút Xóa bộ lọc xóa keyword và chuyển về page 1.
- Chỉ sử dụng các giá trị sort được Backend hỗ trợ.

### 3.4. Phân trang

- Nút Đầu, Trước, các số trang, Sau, Cuối.
- Hiển thị tối đa các số trang quanh trang hiện tại.
- Hiển thị thông tin, ví dụ `Hiển thị 11–20 trong 57`.
- Chọn 10, 20 hoặc 50 dòng.
- Không hiển thị liên kết đến trang không hợp lệ.

### 3.5. Trạng thái

- Chưa có User: hiển thị trạng thái rỗng và nút Thêm User.
- Không có kết quả tìm kiếm: thông báo `Không tìm thấy người dùng phù hợp`.
- Lỗi API: thông báo dễ hiểu và nút thử lại.
- Không hiển thị User đã xóa.

## 4. SCR-02 – Chi tiết User

Route: `/Users/Details/{id}`

### 4.1. Thông tin User

Hiển thị:

- Họ và tên.
- Email.
- Số điện thoại.
- Ngày sinh.
- Giới tính.
- Địa chỉ.
- Ngày tạo.
- Ngày cập nhật.

Giá trị không có dữ liệu hiển thị `---`.

### 4.2. Nút thao tác

- `Chỉnh sửa` → `/Users/Edit/{id}`.
- `Xóa` → modal xác nhận.
- `Thêm giấy tờ` → trang tạo giấy tờ; ẩn khi đủ ba loại.
- `Quay lại` → danh sách với query string trước đó.

### 4.3. returnUrl

Khi mở chi tiết từ danh sách, giữ query string của danh sách qua `returnUrl`.

- Truyền returnUrl qua link Chi tiết và Chỉnh sửa.
- Sau khi lưu hoặc hủy, quay lại đúng trạng thái danh sách nếu phù hợp.
- Chỉ chấp nhận URL nội bộ hợp lệ theo `Url.IsLocalUrl` và bắt đầu bằng `/Users`.
- Nếu không hợp lệ, chuyển về `/Users`.
- Không cho phép open redirect.

### 4.4. Danh sách giấy tờ

Mỗi giấy tờ là một card hiển thị:

- Loại giấy tờ.
- Số giấy tờ.
- Ngày cấp.
- Ngày hết hạn.
- Ảnh mặt trước/trang thông tin.
- Ảnh mặt sau nếu có.
- Nút Xem ảnh.
- Nút Sửa.
- Nút Xóa.

Nếu `ExpiryDate` nhỏ hơn ngày hiện tại tại múi giờ nghiệp vụ, hiển thị huy hiệu `Đã hết hạn`.

Passport không hiển thị ảnh mặt sau.

## 5. SCR-03 – Thêm User

Route: `/Users/Create`

### 5.1. Form

| Trường | Loại input | Bắt buộc |
|---|---|---|
| Họ và tên | text | Có |
| Email | email | Có |
| Số điện thoại | tel | Có |
| Ngày sinh | date | Không |
| Giới tính | select | Không |
| Địa chỉ | textarea | Không |

### 5.2. Nút

- `Thêm`: validate rồi gửi POST `/api/v1/users`.
- `Hủy`: quay về danh sách.
- Khi thành công: chuyển đến trang chi tiết.
- Khi thất bại: giữ input và hiển thị lỗi.

### 5.3. Validation giao diện

- Hiển thị lỗi ngay dưới trường.
- Trim khoảng trắng đầu/cuối khi gửi.
- Kiểm tra cơ bản ở Frontend để phản hồi nhanh.
- Backend vẫn là nơi kiểm tra cuối cùng.
- Không chỉ dựa vào validation phía trình duyệt.

## 6. SCR-04 – Chỉnh sửa User

Route: `/Users/Edit/{id}`

- Tải dữ liệu bằng GET `/api/v1/users/{id}`.
- Nếu User không tồn tại, hiển thị thông báo và điều hướng về danh sách.
- Form có cùng các trường như SCR-03.
- Gửi PUT `/api/v1/users/{id}`.
- Trường tùy chọn trống được gửi thành null.
- Email và số điện thoại trùng hiển thị lỗi tương ứng.
- Thành công chuyển đến trang chi tiết.
- Giữ `returnUrl` hợp lệ nếu có.

## 7. SCR-05 – Modal xác nhận xóa

Dùng cho xóa User và xóa giấy tờ.

### 7.1. Nội dung

- Tiêu đề xác nhận.
- Mô tả rõ đối tượng sắp xóa.
- Nút `Hủy`.
- Nút `Xóa`.

### 7.2. Hành vi

- Chỉ gửi request sau khi người dùng xác nhận.
- POST từ Frontend có anti-forgery token.
- Frontend gọi API DELETE tương ứng.
- Disable nút khi đang xử lý.
- Nếu thành công, hiển thị toast.
- Nếu thất bại, giữ nguyên dữ liệu trên giao diện và hiển thị lỗi.
- Xóa User thành công: quay về danh sách.
- Xóa giấy tờ thành công: tải lại hoặc cập nhật danh sách giấy tờ.

## 8. SCR-06 – Thêm/Sửa giấy tờ

### 8.1. Route tạo

`/Users/{userId}/Documents/Create`

### 8.2. Route sửa

`/Users/{userId}/Documents/Edit/{documentId}`

### 8.3. Trường nhập liệu

| Trường | Loại | Bắt buộc |
|---|---|---|
| Loại giấy tờ | Select | Có khi tạo |
| Số giấy tờ | Text | Có |
| Ngày cấp | Date | Không |
| Ngày hết hạn | Date | Không |
| Ảnh mặt trước/trang thông tin | File | Có khi tạo |
| Ảnh mặt sau | File | Có với CCCD/CMND khi tạo |

### 8.4. Hành vi theo loại giấy tờ

- CCCD: hiện input ảnh trước và ảnh sau.
- CMND: hiện input ảnh trước và ảnh sau.
- Passport: chỉ hiện input ảnh trang thông tin.
- Khi sửa, loại giấy tờ hiển thị nhưng không cho thay đổi.
- Ảnh không được chọn mới sẽ giữ nguyên ảnh hiện tại khi sửa.
- Hiển thị ảnh hiện tại và preview ảnh mới.
- Khi chọn Passport, ảnh mặt sau phải được loại bỏ khỏi form.
- Không cho gửi request không phù hợp với loại giấy tờ.

### 8.5. Validation ảnh

- Chỉ JPG/JPEG/PNG.
- Mỗi file tối đa 5 MB.
- Thông báo lỗi hiển thị rõ trường ảnh nào không hợp lệ.
- Kiểm tra phía Frontend chỉ nhằm hỗ trợ UX; Backend phải kiểm tra lại nội dung file.
- Không tự động gửi ảnh lên trước khi người dùng bấm Lưu.

### 8.6. Xem ảnh

- Nhấn ảnh thu nhỏ để mở lightbox.
- Có nút đóng.
- Có thể đóng bằng phím Escape.
- Không dùng URL file vật lý.
- Ảnh tải qua route proxy Frontend, route này gọi API-10.
- Không hiển thị ảnh của giấy tờ/User không tồn tại.

## 9. API client và lỗi giao diện

Frontend sử dụng `IHttpClientFactory`.

- Base URL lấy từ `Api:BaseUrl`.
- Timeout lấy từ `Api:TimeoutSeconds`.
- Không hard-code URL Backend trong Controller.
- Xử lý lỗi mạng, timeout, 400, 404, 409, 413, 415 và 500.
- Parse `errors` trong Problem Details để gắn lỗi với field tương ứng.
- Nếu lỗi không gắn được với field, hiển thị thông báo chung.
- Không hiển thị stack trace hoặc nội dung lỗi nội bộ.

## 10. Cấu trúc View đề xuất

```text
UserManagement.Web/
  Controllers/
    UsersController.cs
  Services/
    ApiClient.cs
    UsersApiClient.cs
  ViewModels/
    Users/
      UserListViewModel.cs
      UserDetailViewModel.cs
      UserFormViewModel.cs
      IdentityDocumentFormViewModel.cs
  Views/
    Shared/
      _Layout.cshtml
      _Toast.cshtml
      _Pagination.cshtml
      _ConfirmDeleteModal.cshtml
      _ValidationScriptsPartial.cshtml
    Users/
      Index.cshtml
      Details.cshtml
      Create.cshtml
      Edit.cshtml
      _UserForm.cshtml
      _IdentityDocumentForm.cshtml
  wwwroot/
    css/
      app.css
    js/
      users.js
      identity-documents.js
```

## 11. Tiêu chí nghiệm thu UI

- Đủ các route và màn hình theo tài liệu.
- Tìm kiếm, phân trang và sắp xếp giữ trạng thái.
- Form có validation và hiển thị lỗi rõ ràng.
- Có trạng thái loading, empty, success và error.
- Modal xác nhận hoạt động đúng.
- Upload hỗ trợ preview ảnh.
- Lightbox xem ảnh hoạt động.
- Giao diện responsive từ 360 px.
- Không có form POST thiếu anti-forgery token.
- Không truy cập database trực tiếp từ Frontend.