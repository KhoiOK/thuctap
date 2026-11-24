# ĐỀ XUẤT LUỒNG THÊM USER NHANH

## 1. Mục tiêu

Xây dựng chức năng **Thêm User nhanh (Quick Create)** cho hệ thống User Management dựa trên kiến trúc và các thành phần frontend hiện có.

Mục tiêu là rút ngắn thao tác tạo người dùng, hạn chế nhập liệu thủ công và tận dụng chức năng OCR CCCD hiện tại. Người dùng vẫn có thể tạo User bằng cách nhập tay nếu không có ảnh CCCD hoặc không muốn sử dụng OCR.

## 2. Luồng nghiệp vụ đề xuất

### Bước 1: Mở chức năng thêm User

- Tại trang danh sách User, người quản trị nhấn nút **Thêm User nhanh**.
- Hệ thống mở form gọn (modal hoặc trang riêng).
- Không cần tạo một quy trình nhiều trang nếu form hiện tại có thể tái sử dụng.

### Bước 2: Chọn phương thức nhập thông tin

Người dùng chọn một trong hai phương thức:

- **Quét CCCD:** tải ảnh mặt trước và mặt sau CCCD để sử dụng chức năng OCR hiện có.
- **Nhập thủ công:** tự nhập thông tin vào biểu mẫu.

Quét CCCD là tùy chọn, không phải điều kiện bắt buộc để tạo User.

### Bước 3: Nhận diện và điền thông tin (nếu dùng OCR)

- Frontend gửi ảnh đến luồng OCR hiện tại.
- Hệ thống nhận kết quả OCR và hiển thị để người dùng kiểm tra.
- Những trường nhận diện được có thể được điền tự động vào form.
- Người dùng được phép chỉnh sửa thông tin OCR trước khi lưu.
- Không tự động lưu thông tin CCCD hoặc tạo giấy tờ khi người dùng chưa xác nhận.

### Bước 4: Bổ sung thông tin User

Form có thể hiển thị các trường phù hợp với nghiệp vụ hiện tại, ví dụ:

- Họ và tên.
- Email.
- Số điện thoại.
- Ngày sinh (nếu hệ thống yêu cầu).
- Thông tin CCCD (nếu có).

**Lưu ý:** Danh sách trường bắt buộc phải được đối chiếu với DTO, validation và quy tắc nghiệp vụ ở backend. Không tự ý biến các trường tùy chọn thành bắt buộc.

### Bước 5: Kiểm tra dữ liệu

Trước khi gửi yêu cầu tạo User, hệ thống cần:

- Kiểm tra các trường bắt buộc.
- Kiểm tra định dạng email và số điện thoại.
- Kiểm tra dữ liệu trùng lặp theo quy tắc backend.
- Hiển thị lỗi gần trường nhập tương ứng.
- Khóa nút lưu trong lúc gửi yêu cầu để tránh gửi trùng do nhấn nhiều lần.

### Bước 6: Tạo User

- Form gửi dữ liệu đến `UsersController`.
- Controller sử dụng `UsersApiClient` gọi API tạo User hiện có.
- Xử lý phản hồi thông qua `ApiResult`.
- Nếu thành công, hiển thị thông báo và điều hướng về danh sách User.
- Nếu thất bại, giữ lại dữ liệu người dùng đã nhập và hiển thị lỗi phù hợp.

### Bước 7: Hoàn tất giấy tờ (nếu có)

Nếu người dùng có sử dụng OCR/CCCD:

- Thực hiện đầy đủ bước xác nhận OCR theo thiết kế API hiện có.
- Chỉ tạo hoặc liên kết giấy tờ sau khi dữ liệu đã được xác nhận.
- Nếu tạo User thành công nhưng lưu giấy tờ thất bại, cần thông báo rõ hai trạng thái và cho phép người dùng tiếp tục bổ sung giấy tờ, tránh tạo lại User lần thứ hai.

## 3. Giao diện đề xuất

### 3.1. Điểm truy cập

Tại trang danh sách User, bổ sung nút:

`+ Thêm User nhanh`

Khi nhấn, mở modal hoặc form tạo nhanh.

### 3.2. Form tạo nhanh

Form gồm hai lựa chọn:

1. **Nhập thủ công**
   - Họ và tên.
   - Email.
   - Số điện thoại.
   - Các trường bổ sung theo nghiệp vụ hiện tại.

2. **Quét CCCD**
   - Tải ảnh CCCD mặt trước.
   - Tải ảnh CCCD mặt sau.
   - Hiển thị kết quả OCR.
   - Cho phép kiểm tra và chỉnh sửa dữ liệu nhận diện.

Các nút thao tác:

- Hủy.
- Làm mới (nếu cần).
- Tạo User.

Không nên đưa quá nhiều trường ít sử dụng vào bước đầu. Các thông tin bổ sung có thể được cập nhật ở trang chi tiết User sau khi tạo.

## 4. Tận dụng thành phần hiện có

| Thành phần | Vai trò trong luồng mới |
|---|---|
| `UsersController` | Tiếp nhận và điều phối yêu cầu tạo User |
| `Views/Users/_UserForm.cshtml` | Tái sử dụng các trường nhập liệu và validation |
| `UsersApiClient` | Gọi API tạo User hiện có |
| `ApiResult` | Chuẩn hóa kết quả thành công/thất bại |
| `wwwroot/js/ocr.js` | Tái sử dụng xử lý OCR CCCD |
| `Views/Documents/_OcrResultPanel.cshtml` | Hiển thị kết quả OCR |
| Validation hiện tại | Kiểm tra dữ liệu đầu vào |
| Toast trong `site.js` | Thông báo kết quả thao tác |
| FormLock trong `site.js` | Hạn chế gửi form nhiều lần |

**Nguyên tắc:** Ưu tiên tái sử dụng code và API hiện có. Không tạo API tạo User mới nếu API hiện tại đã đáp ứng yêu cầu.

## 5. Luồng xử lý kỹ thuật

```text
Users/Index.cshtml
       |
       v
Nút "Thêm User nhanh"
       |
       v
Quick Create Form
       |
       +-------------------+
       |                   |
       v                   v
  Quét CCCD           Nhập thủ công
       |                   |
       v                   |
  OCR hiện có              |
       |                   |
       v                   |
  Hiển thị kết quả         |
  và người dùng xác nhận   |
       |                   |
       +---------+---------+
                 |
                 v
          Validate dữ liệu
                 |
                 v
          UsersController
                 |
                 v
           UsersApiClient
                 |
                 v
            Backend API
                 |
          +------+------+
          |             |
       Thành công     Thất bại
          |             |
          v             v
     Toast success   Hiển thị lỗi
          |
          v
      Danh sách User
          |
          v
   Bổ sung giấy tờ nếu cần
```

## 6. Quy tắc nghiệp vụ và trường hợp lỗi

1. Không bắt buộc quét CCCD mới được tạo User.
2. Dữ liệu OCR chỉ là dữ liệu gợi ý; người dùng phải kiểm tra trước khi xác nhận.
3. Cho phép sửa thông tin OCR trước khi lưu.
4. Kiểm tra trùng email/số điện thoại theo quy tắc backend.
5. Chống gửi yêu cầu tạo User nhiều lần khi người dùng nhấn nút liên tục.
6. Khi API tạo User thất bại, giữ lại dữ liệu form để người dùng sửa và thử lại.
7. Nếu User đã tạo thành công nhưng bước lưu giấy tờ thất bại, không tạo lại User; cung cấp luồng bổ sung giấy tờ.
8. Không hiển thị hoặc ghi log dữ liệu CCCD nhạy cảm không cần thiết.
9. Kiểm tra quyền truy cập chức năng tạo User ở backend, không chỉ ẩn nút trên giao diện.

## 7. Thứ tự triển khai đề xuất

| Ưu tiên | Công việc | Mục tiêu |
|---|---|---|
| 1 | Rà soát DTO, API tạo User và validation backend | Xác định chính xác dữ liệu cần gửi |
| 2 | Tái sử dụng `_UserForm.cshtml` cho Quick Create | Tránh viết lại form |
| 3 | Bổ sung nút và giao diện Thêm User nhanh | Hoàn thiện điểm truy cập |
| 4 | Kết nối form với `UsersController` và `UsersApiClient` | Tạo User bằng API hiện tại |
| 5 | Tích hợp OCR hiện có vào form nhanh | Hạn chế nhập liệu thủ công |
| 6 | Hoàn thiện bước xác nhận OCR/giấy tờ | Đảm bảo đúng luồng nghiệp vụ |
| 7 | Kiểm thử validation, trùng dữ liệu và gửi lặp | Hạn chế lỗi khi sử dụng thực tế |

## 8. Các trường hợp cần kiểm thử

- Tạo User bằng nhập thủ công với dữ liệu hợp lệ.
- Tạo User bằng dữ liệu OCR đã được người dùng kiểm tra.
- OCR không nhận diện được ảnh hoặc trả về độ tin cậy thấp.
- Ảnh CCCD không đúng định dạng hoặc vượt giới hạn dung lượng.
- Thiếu trường bắt buộc.
- Email hoặc số điện thoại sai định dạng.
- Email hoặc số điện thoại bị trùng theo quy tắc hệ thống.
- Backend không hoạt động hoặc trả về lỗi.
- Người dùng nhấn nút tạo User nhiều lần.
- Tạo User thành công nhưng lưu giấy tờ thất bại.
- Người dùng hủy thao tác sau khi OCR đã điền dữ liệu.

## 9. Kết luận

Nên triển khai chức năng **Thêm User nhanh (Quick Create)** theo hướng sử dụng lại form, API CRUD và OCR hiện có. Người dùng có thể chọn nhập thủ công hoặc quét CCCD để tự động điền thông tin, sau đó kiểm tra dữ liệu và xác nhận tạo User.

Giải pháp này giúp giảm thao tác nhập liệu, hạn chế thay đổi kiến trúc hiện tại và phù hợp với quy trình quản lý User thực tế. Ưu tiên hoàn thiện luồng tạo User cơ bản trước, sau đó tích hợp đầy đủ bước xác nhận OCR và lưu giấy tờ.


---

# 10. Thiết kế giao diện trực quan – Thêm User nhanh

## 10.1. Bố cục trang danh sách User

Nút **+ Thêm User nhanh** được đặt ở khu vực tiêu đề trang, gần ô tìm kiếm và bộ lọc để người quản trị dễ tìm thấy.

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ USER MANAGEMENT                                      [🔔] [Admin ▾]          │
├──────────────────────────────────────────────────────────────────────────────┤
│ Quản lý người dùng                                                           │
│ Theo dõi, tìm kiếm và quản lý thông tin người dùng                           │
│                                                                              │
│ [🔎 Tìm theo tên, email, số điện thoại...] [Vai trò ▾] [Trạng thái ▾]        │
│                                                                              │
│                                      [+ Thêm User nhanh]  [Thêm User đầy đủ] │
├──────────────────────────────────────────────────────────────────────────────┤
│ Tổng User       Đang hoạt động       Chờ xác minh       Đã khóa               │
│    1.250              1.120                85               45                │
├──────────────────────────────────────────────────────────────────────────────┤
│ Họ và tên       Email              Số điện thoại       Trạng thái   Thao tác  │
│ Nguyễn Văn A    a@gmail.com        090...              Hoạt động    [⋯]       │
│ Trần Thị B      b@gmail.com        091...              Hoạt động    [⋯]       │
│ Lê Văn C        c@gmail.com        092...              Chờ xác minh [⋯]       │
└──────────────────────────────────────────────────────────────────────────────┘
```

*Các số liệu và dòng dữ liệu trong hình chỉ là ví dụ minh họa, không phải dữ liệu thực tế của hệ thống.*

## 10.2. Giao diện modal Thêm User nhanh

Khi nhấn **Thêm User nhanh**, mở modal ở giữa màn hình. Modal chia thành ba phần: chọn phương thức, nhập thông tin và xác nhận.

```text
┌─────────────────────────────────────────────────────────────────────┐
│  THÊM USER MỚI                                                [✕]   │
│  Tạo hồ sơ người dùng nhanh chóng                                   │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  CHỌN PHƯƠNG THỨC NHẬP                                              │
│  ┌──────────────────────────┐  ┌─────────────────────────────────┐  │
│  │  ✍ NHẬP THỦ CÔNG         │  │  ▣ QUÉT CCCD                    │  │
│  │  Tự điền thông tin       │  │  OCR tự nhận diện thông tin     │  │
│  │  [Đang chọn]             │  │  [Chọn phương thức]              │  │
│  └──────────────────────────┘  └─────────────────────────────────┘  │
│                                                                     │
│  THÔNG TIN CƠ BẢN                                                   │
│  Họ và tên *                                                        │
│  [Nguyễn Văn A................................................]     │
│                                                                     │
│  Email *                         Số điện thoại *                    │
│  [example@gmail.com.........]    [0901234567...................]    │
│                                                                     │
│  Ngày sinh                         Vai trò                          │
│  [dd/mm/yyyy.................]    [Chọn vai trò ▾..............]    │
│                                                                     │
│  ───────────────────────────────────────────────────────────────   │
│  * Trường bắt buộc được xác định theo quy tắc backend hiện tại.     │
│                                                                     │
│                                  [Hủy]  [Tạo User]                  │
└─────────────────────────────────────────────────────────────────────┘
```

**Hành vi giao diện:**

- Khi chọn **Nhập thủ công**, hiển thị form thông tin cơ bản.
- Khi chọn **Quét CCCD**, hiển thị khu vực tải ảnh mặt trước và mặt sau.
- Không bắt buộc người dùng phải quét CCCD.
- Nút **Tạo User** chỉ khả dụng khi các trường bắt buộc hợp lệ.

## 10.3. Giao diện khi chọn Quét CCCD

```text
┌─────────────────────────────────────────────────────────────────────┐
│  THÊM USER MỚI                                                [✕]   │
├─────────────────────────────────────────────────────────────────────┤
│  [Nhập thủ công]                 [▣ Quét CCCD – Đang chọn]          │
│                                                                     │
│  TẢI ẢNH CCCD                                                       │
│  ┌────────────────────────────┐  ┌───────────────────────────────┐  │
│  │       MẶT TRƯỚC            │  │         MẶT SAU               │  │
│  │                            │  │                               │  │
│  │       ⬆                    │  │          ⬆                    │  │
│  │  Chọn ảnh mặt trước        │  │   Chọn ảnh mặt sau            │  │
│  │  PNG, JPG                  │  │   PNG, JPG                    │  │
│  └────────────────────────────┘  └───────────────────────────────┘  │
│                                                                     │
│                       [Bắt đầu nhận diện OCR]                       │
│                                                                     │
│  KẾT QUẢ NHẬN DIỆN                                                  │
│  Họ và tên *                 [NGUYỄN VĂN A.....................]    │
│  Số CCCD                     [.................................]    │
│  Ngày sinh                   [.................................]    │
│  Địa chỉ                     [.................................]    │
│                                                                     │
│  ⚠ Vui lòng kiểm tra và chỉnh sửa thông tin trước khi xác nhận.    │
│                                                                     │
│                                  [Hủy]  [Xác nhận thông tin]        │
└─────────────────────────────────────────────────────────────────────┘
```

**Trạng thái cần thiết:**

| Trạng thái | Cách hiển thị |
|---|---|
| Chưa tải ảnh | Hiển thị vùng chọn ảnh |
| Đang tải ảnh | Hiển thị tiến trình tải |
| Đang OCR | Hiển thị trạng thái đang nhận diện |
| OCR thành công | Hiển thị dữ liệu nhận diện để kiểm tra |
| OCR độ tin cậy thấp | Cảnh báo và yêu cầu kiểm tra thủ công |
| OCR thất bại | Hiển thị lỗi và cho phép thử lại hoặc nhập tay |

Không được tự động xác nhận hoặc lưu dữ liệu CCCD chỉ vì OCR trả về kết quả.

## 10.4. Giao diện xác nhận trước khi tạo

```text
┌─────────────────────────────────────────────────────────────────────┐
│  KIỂM TRA THÔNG TIN USER                                             │
├─────────────────────────────────────────────────────────────────────┤
│  Họ và tên:       Nguyễn Văn A                                      │
│  Email:           example@gmail.com                                 │
│  Số điện thoại:   0901234567                                        │
│  Nguồn dữ liệu:   OCR CCCD / Nhập thủ công                          │
│                                                                     │
│  ✓ Thông tin bắt buộc đã được kiểm tra                              │
│                                                                     │
│                 [Quay lại chỉnh sửa]  [Xác nhận tạo User]           │
└─────────────────────────────────────────────────────────────────────┘
```

Chỉ cần hiển thị bước xác nhận riêng nếu nghiệp vụ yêu cầu. Với form ngắn, có thể bỏ bước này và cho phép tạo trực tiếp sau khi validation thành công.

## 10.5. Giao diện thông báo kết quả

**Tạo thành công**

```text
┌─────────────────────────────────────────────────────────────┐
│  ✓ Tạo User thành công                                      │
│  Người dùng đã được thêm vào hệ thống.                      │
│                                                             │
│  [Đóng]                         [Xem chi tiết User]          │
└─────────────────────────────────────────────────────────────┘
```

**Tạo thất bại**

```text
┌─────────────────────────────────────────────────────────────┐
│  ! Chưa thể tạo User                                        │
│  Email đã tồn tại hoặc dữ liệu không hợp lệ.                │
│  Vui lòng kiểm tra thông tin và thử lại.                    │
│                                                             │
│  Dữ liệu đã nhập được giữ lại để chỉnh sửa.                 │
└─────────────────────────────────────────────────────────────┘
```

Nếu tạo User thành công nhưng lưu giấy tờ thất bại, thông báo phải phân biệt rõ: **User đã được tạo; giấy tờ chưa được lưu**. Cung cấp thao tác tiếp tục bổ sung giấy tờ, không yêu cầu tạo lại User.

## 10.6. Quy tắc trải nghiệm người dùng (UX)

- Modal có chiều rộng vừa phải, không chiếm toàn bộ màn hình desktop.
- Trên điện thoại, modal chuyển thành giao diện toàn màn hình hoặc form một cột.
- Nhãn trường luôn hiển thị bên trên ô nhập; không chỉ dùng placeholder làm nhãn.
- Trường bắt buộc có dấu `*`; lỗi hiển thị ngay dưới trường tương ứng.
- Nút chính dùng một kiểu nhất quán: **Tạo User** hoặc **Xác nhận thông tin**.
- Khi đang gửi dữ liệu, khóa nút thao tác và hiển thị trạng thái đang xử lý.
- Khi đóng modal có dữ liệu chưa lưu, hỏi người dùng có muốn hủy thay đổi hay không.
- Không hiển thị đầy đủ số CCCD trong thông báo hoặc log không cần thiết.
- Hỗ trợ bàn phím, focus rõ ràng và nhãn truy cập cho các điều khiển.

## 10.7. Gợi ý cấu trúc triển khai giao diện

Có thể bổ sung một partial view riêng cho modal Quick Create, nhưng vẫn tái sử dụng các trường và validation từ form hiện tại.

```text
Views/
└── Users/
    ├── Index.cshtml
    ├── _UserForm.cshtml
    ├── _QuickCreateModal.cshtml     # Giao diện modal tạo nhanh
    └── Details.cshtml

wwwroot/
└── js/
    ├── site.js
    └── quick-create-user.js         # Điều khiển modal và luồng tạo nhanh
```

Đây là cấu trúc gợi ý, cần kiểm tra cách tổ chức partial view và JavaScript hiện tại trước khi tạo file mới. Tránh sao chép toàn bộ logic validation hoặc OCR sang một luồng độc lập.

## 10.8. Tiêu chí nghiệm thu giao diện

- Người quản trị tìm thấy nút **Thêm User nhanh** ngay tại trang danh sách.
- Có thể chuyển đổi giữa nhập tay và quét CCCD.
- Có thể tạo User mà không cần OCR.
- Kết quả OCR được hiển thị để người dùng kiểm tra và chỉnh sửa.
- Form hiển thị lỗi validation rõ ràng.
- Không gửi yêu cầu tạo User lặp khi nhấn nhiều lần.
- Có thông báo rõ ràng cho thành công, thất bại và trường hợp lưu giấy tờ không thành công.
- Giao diện hiển thị tốt trên desktop và màn hình điện thoại.
- Các thành phần giao diện sử dụng lại style, layout và quy ước hiện tại của dự án.
