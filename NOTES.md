# NOTES – User Management

File này ghi nhận các điểm chưa rõ, mâu thuẫn và quyết định trong quá trình triển khai.

## Phase 0 – Khảo sát (hoàn thành)

- Workspace `d:\New folder (2)` ban đầu chỉ chứa thư mục `docs/`.
- Môi trường đã xác nhận: .NET SDK 10.0.401, dotnet-ef 10.0.0, Node 24.22.0, SQL Server 2022 local.
- Không phát hiện mâu thuẫn giữa 5 tài liệu.
- Cấu hình mặc định:
  - DB: `UserManagementDb` (dev: `UserManagementDb_Dev`)
  - FileStorage: `D:\AppData\UserManagement` (dev: `D:\AppData\UserManagement_Dev`)
  - API: `https://localhost:5001`
  - Web: `https://localhost:5002`

## Phase 1 – Khởi tạo solution (hoàn thành)

### File đã tạo

- `UserManagement.sln`, 4 project (Api/Web/Api.Tests/Web.Tests).
- API: `Program.cs` (Swagger, CORS, ProblemDetails, GlobalExceptionMiddleware, Kestrel/FormOptions), `appsettings*.json`, `launchSettings.json`, `HealthController.cs`, `FileStorage/{IFileStorage,FileStorageOptions,LocalFileStorage}.cs`, `Middleware/GlobalExceptionMiddleware.cs`.
- Web: `Program.cs` (HttpClient typed client, redirect "/"), `appsettings.json`, `launchSettings.json`, `Services/{ApiClient,DisplayOptions}.cs`, `Views/Shared/_Layout.cshtml` (tiếng Việt, Tailwind), `tailwind.config.js`, `package.json`, `wwwroot/css/app.src.css` + `app.css` (generated), `wwwroot/js/site.js`.
- Tests: `HealthEndpointTests.cs`, `WebSmokeTests.cs`.
- `README.md`.

### Quyết định

- **Swagger**: dùng `Swashbuckle.AspNetCore 6.9.0` (không dùng `Microsoft.AspNetCore.OpenApi 10.0.12` template mặc định – source generator của nó không tương thích với `Microsoft.OpenApi 3.x`).
- **CORS**: chỉ cho phép `https://localhost:5002`.
- **ImageSharp**: 3.1.10 (vá CVE so với 3.1.5).
- **Tailwind**: CLI v3, output `wwwroot/css/app.css`.
- **TestHost/Mvc.Testing**: 10.0.13.

### Build / Test

```
dotnet build: 0 errors
dotnet test:  4/4 passed (3 API + 1 Web)
```

## Phase 2 – Database và Entity Framework Core (hoàn thành)

### File đã tạo/sửa

- `Entities/Gender.cs`, `Entities/DocumentType.cs` (enum lưu số, API truyền chuỗi).
- `Entities/User.cs` (với static `BuildFullNameSearch` chuẩn hóa không dấu theo Database-Design §2.1).
- `Entities/IdentityDocument.cs`.
- `Data/ApplicationDbContext.cs` (`ApplyConfigurationsFromAssembly`).
- `Data/Configurations/UserConfiguration.cs` (Fluent API: 4 cột string/date/datetime2/bit, 4 index [2 unique filtered + 2 non-unique filtered], global query filter `!IsDeleted`, FK `Restrict`).
- `Data/Configurations/IdentityDocumentConfiguration.cs` (2 unique index, FK relationship).
- `Migrations/20261016142951_InitialCreate.cs` (+ Designer + ModelSnapshot).
- `Program.cs` đăng ký `AddDbContext<ApplicationDbContext>` với SQL Server + EnableRetryOnFailure.
- `tests/.../Data/DatabaseConstraintTests.cs` (8 tests).

### Quyết định

- **Global query filter** chỉ trên `User` (không trên `IdentityDocument`) để có thể truy cập document của user đã xóa khi cần dọn dẹp file trong service.
- **Test DB**: mỗi test class tạo database mới `UserManagementDb_Test_<guid>`, áp dụng migration, xóa khi kết thúc. Self-contained.
- **Migration**: 1 migration `InitialCreate` chứa toàn bộ schema (Users + IdentityDocuments + 6 index + 1 FK).

### Migration đã apply

Database `UserManagementDb_Dev` đã được tạo với:

- Bảng `Users`, `IdentityDocuments` đầy đủ cột theo Database-Design §2-3.
- 6 index đúng theo §5 (4 trên Users, 2 trên IdentityDocuments).
- FK `FK_IdentityDocuments_Users` với `ON DELETE NO_ACTION` (đúng §4 - không cascade).

Đã verify bằng `sqlcmd -S localhost -E -d UserManagementDb_Dev`.

### Test coverage Phase 2

| Test | Quy tắc | Kết quả |
|---|---|---|
| Duplicate_Email_On_Active_User_Is_Rejected | BR-01 + UX_Users_Email | ✅ |
| Email_Can_Be_Reused_After_Soft_Delete | UX_Users_Email với filter `IsDeleted=0` | ✅ |
| Duplicate_PhoneNumber_On_Active_User_Is_Rejected | BR-02 + UX_Users_PhoneNumber | ✅ |
| Global_Query_Filter_Hides_Soft_Deleted_Users | Database-Design §5.1 | ✅ |
| User_Cannot_Have_Two_Documents_Of_Same_Type | BR-06 + UX_IdentityDocuments_UserId_DocumentType | ✅ |
| Duplicate_Document_Number_Across_Users_Is_Rejected | BR-11 + UX_IdentityDocuments_Type_Number | ✅ |
| Same_Document_Number_Allowed_For_Different_Document_Types | UX_IdentityDocuments_Type_Number (chỉ unique theo cặp) | ✅ |
| Deleting_User_With_Documents_Is_Restricted | Database-Design §4 (FK NO_ACTION) | ✅ |

### Build / Test

```
dotnet build: 0 errors
dotnet test:  12/12 passed (11 API + 1 Web)
  - DatabaseConstraintTests: 8/8
  - HealthEndpointTests:     3/3
  - WebSmokeTests:           1/1
```

## Phase 3 – Backend API và nghiệp vụ User (hoàn thành)

### File đã tạo/sửa

- `DTOs/Users/UserListQuery.cs` (`PagedResultDto<T>`, `UserListQuery`).
- `DTOs/Users/UserDtos.cs` (`UserListItemDto`, `UserDetailDto`, `IdentityDocumentListItemDto`).
- `DTOs/Users/UserUpsertRequest.cs` (DataAnnotations + field errors).
- `Exceptions/NotFoundException.cs`, `ValidationException.cs`, `ConflictException.cs`.
- `Validators/NormalizationHelpers.cs` (BR-01..BR-04).
- `Validators/UserValidator.cs` (`ValidateAndNormalize`).
- `Services/IUserService.cs`, `Services/UserService.cs` (CRUD + search/paginate/sort + soft delete).
- `Controllers/UsersController.cs` (API-01..API-05).
- `Middleware/GlobalExceptionHandler.cs` (`IExceptionHandler` mapping domain exceptions to ProblemDetails).
- `Middleware/ModelStateValidationFilter.cs` (convert ModelState errors -> ValidationProblemDetails, camelCase).
- `Program.cs` đăng ký `IUserService`, `TimeProvider`, exception handler, JSON enum string converter, suppress model state filter.

### Quyết định

- **Exception pipeline**: dùng `IExceptionHandler` (ASP.NET Core 8+) thay vì middleware thuần để chuyển `ValidationException`/`ConflictException`/`NotFoundException` thành ProblemDetails. Bỏ qua default model state filter (`SuppressModelStateInvalidFilter = true`) để custom filter làm chủ format (camelCase keys).
- **JsonStringEnumConverter**: cho phép client gửi `Gender`/`DocumentType` dạng chuỗi (vd `"male"`, `"cccd"`). Tương thích API-Spec §1.1.
- **Uniqueness pre-check**: trước khi insert/update, gọi `EnsureEmailAvailableAsync`/`EnsurePhoneAvailableAsync` (không dùng `IgnoreQueryFilters()` - vì unique chỉ áp dụng cho User chưa xóa). Nếu concurrent request bypass pre-check, `DbUpdateException` với SQL 2627/2601 sẽ được middleware convert thành 409.
- **Search**: keyword được lowercase + bỏ dấu để so sánh với `FullNameSearch`. Email/phone dùng cột đã chuẩn hóa.
- **Stable sort**: luôn append `ThenBy(Id)` để đảm bảo thứ tự ổn định.
- **Pagination caps**: `pageSize` cap ở 100, `page` min 1.
- **Soft delete**: set `IsDeleted=true`, ghi `DeletedAt`, xóa IdentityDocuments và file ảnh liên quan (best-effort, lỗi file ghi log).
- **Test isolation**: mỗi test class dùng `[CollectionDefinition(nameof(ApiIntegrationCollection), DisableParallelization = true)]` + `IAsyncLifetime` để có database riêng và tránh race condition khi nhiều test cùng `CREATE DATABASE`.

### Test coverage (50/50 API tests passed)

| Class | Count | Quy tắc |
|---|---|---|
| HealthEndpointTests | 3 | Smoke test Phase 1 |
| DatabaseConstraintTests | 8 | Phase 2 unique + FK |
| UserValidatorTests | 9 | BR-01..BR-04 (unit) |
| UserServiceTests | 20 | Service CRUD + search + sort + paging + soft delete |
| UsersControllerTests | 10 | API-01..API-05 end-to-end |

### Build / Test

```
dotnet build: 0 errors
dotnet test:  51/51 passed (50 API + 1 Web)
```

## Phase 4 – Backend API quản lý giấy tờ và ảnh (hoàn thành)

### File đã tạo/sửa

- `FileStorage/ImageValidator.cs` (magic bytes + kích thước + format, fallback khi `DecodedImageFormat` null).
- `FileStorage/FileStorageOptions.cs` (thêm `MaxPixelSize`).
- `FileStorage/IFileStorage.cs` (sửa warning self-assign trong `StoredFileHandle`).
- `Validators/NormalizationHelpers.cs` (thêm `NormalizeDocumentNumber` + `IsValidDocumentNumber` cho BR-08).
- `DTOs/Users/IdentityDocuments/IdentityDocumentRequests.cs` (`CreateIdentityDocumentRequest`, `UpdateIdentityDocumentRequest`).
- `Services/IdentityDocumentService.cs` (`IIdentityDocumentService` + impl: list/get/create/update/delete/image + transaction + file cleanup).
- `Controllers/IdentityDocumentsController.cs` (API-06..API-11, multipart, ETag/If-None-Match/304).
- `Program.cs` đăng ký `IIdentityDocumentService` (Scoped) và `ImageValidator` (Singleton).
- Tests: `Validators/DocumentNumberValidationTests.cs`, `FileStorage/ImageValidatorTests.cs`, `Controllers/IdentityDocumentsControllerTests.cs`.

### Quyết định

- **Multipart endpoint**: `[Consumes("multipart/form-data")]` cho POST/PUT; JSON cho GET. `[RequestSizeLimit(15_000_000)]` cho phép 2 ảnh 5 MB + form fields.
- **File write trước, DB sau, cleanup ngược**: Tạo `documentId` trước rồi mới save ảnh vào path có documentId. Nếu `SaveChangesAsync` ném `DbUpdateException` (unique conflict do concurrent), xóa file đã ghi. Nếu update DB fail ở PUT, xóa file mới tạo, giữ file cũ.
- **Update rollback**: khi PUT metadata OK nhưng DB fail, các file mới đã ghi bị xóa (cleanup) để tránh rác trên disk.
- **Delete "xóa DB trước, file sau"**: đảm bảo nếu DB rollback thì file vẫn còn đúng trạng thái. File cleanup là best-effort, lỗi ghi log warning.
- **ETag**: yếu `W/"<size>-<ticks>-<hash>"` dựa trên size + last-write ticks + path. Hỗ trợ `If-None-Match` (cả `*` và exact match) trả về 304 Not Modified.
- **ImageValidator fallback**: nếu `Image.Metadata.DecodedImageFormat?.Name` trả null, dùng magic-byte probing (FFD8 cho JPEG, 89504E47 cho PNG) để tránh lỗi 415 sai trên một số môi trường.
- **`MaxPixelSize` configurable**: thêm vào `FileStorageOptions` thay vì hard-code để dễ test.
- **Document number BR-08**: CCCD 12 chữ số, CMND 9 hoặc 12, Passport 6-9 alphanumeric uppercase. `NormalizeDocumentNumber` uppercase + trim cho passport.
- **`EnsureDocumentNumberAvailableAsync`**: check unique theo cặp `(DocumentType, DocumentNumber)` - cùng số nhưng khác loại là được phép (đúng `UX_IdentityDocuments_Type_Number`).
- **TestFileStorage**: trong test project, implement đầy đủ `IFileStorage` để service test vẫn hoạt động sau khi thêm method mới.

### Test coverage (tổng 104 API tests)

| Class | Count | Quy tắc |
|---|---|---|
| HealthEndpointTests | 3 | Smoke test Phase 1 |
| DatabaseConstraintTests | 8 | Phase 2 unique + FK |
| UserValidatorTests | 9 | BR-01..BR-04 (unit) |
| UserServiceTests | 20 | Service CRUD + search + sort + paging + soft delete |
| UsersControllerTests | 10 | API-01..API-05 end-to-end |
| DocumentNumberValidationTests | 27 | BR-08 CCCD/CMND/Passport (unit) |
| ImageValidatorTests | 7 | 5MB/8000px/magic-bytes (unit) |
| IdentityDocumentsControllerTests | 20 | API-06..API-11 end-to-end (multipart, ETag, conflict, 404) |

### Build / Test

```
dotnet build: 0 errors
dotnet test:  104/104 passed (103 API + 1 Web)
```

## Phase 5 – Frontend MVC và API client (hoàn thành)

### File đã tạo/sửa

- `Services/ApiDtos.cs` (`PagedResultDto<T>`, `UserListItemDto`, `UserDetailDto`, `IdentityDocumentListItemDto`, `UserUpsertRequest`, `ProblemDetailsDto`).
- `Services/ApiResult.cs` (`ApiResult<T>`, `ApiResult` với field errors dictionary).
- `Services/UsersApiClient.cs` (`IUsersApiClient` + impl: User CRUD + IdentityDocument multipart CRUD + image proxy streaming + Problem Details parser).
- `ViewModels/Users/UserListViewModels.cs` (`UserListViewModel` với PageNumbers helper, `UserFormViewModel` với DataAnnotations + FieldErrors map).
- `ViewModels/Users/UserDetailViewModels.cs` (`UserDetailViewModel`, `IdentityDocumentFormViewModel`).
- `Views/Shared/_Layout.cshtml` (toast container, modal mount, TempData-driven alerts).
- `Views/Shared/_Pagination.cshtml` (Đầu/Trước/Sau/Cuối + page numbers + page size selector).
- `Views/Shared/_ConfirmDeleteModal.cshtml` (data-attribute driven, async submit).
- `Views/_ViewImports.cshtml` (thêm `using` cho Services/ViewModels).
- `wwwroot/js/site.js` (Toast, ConfirmDelete, form submit lock, async delete POST, Lightbox, TempData auto-show).
- `Program.cs` (đăng ký `IUsersApiClient`, anti-forgery `AddAntiforgery` với header `RequestVerificationToken`).

### Quyết định

- **JSON enums dạng chuỗi**: `JsonStringEnumConverter` camelCase, `allowIntegerValues: false` để backend reject nếu Frontend gửi số.
- **Transport vs API errors**: tách `ApiTransportException` (network/timeout) ra khỏi `ApiResult.Failure` (API 4xx/5xx với Problem Details). Controller bắt riêng để hiển thị "Không thể kết nối tới máy chủ API" thay vì crash.
- **Problem Details field errors**: parse `errors` dictionary key theo camelCase API field name. ViewModel cung cấp `ApiFieldToForm` map (`email` → `Email`) để bind lỗi ngược lại input đúng tên.
- **Anti-forgery**: `RequestVerificationToken` header (form thường gửi qua `__RequestVerificationToken` form field; JS async delete fetch cũng set header). Cookie tên `UserManagement.Antiforgery`.
- **Image proxy stream**: dùng `HttpCompletionOption.ResponseHeadersRead` để tránh buffer toàn bộ ảnh; tạo `HttpResponseMessage` mới với `StreamContent` để MVC có thể stream ra response mà không giữ kết nối HttpClient.
- **Pagination**: hiển thị tối đa 5 số quanh trang hiện tại (Page-2..Page+2), vẫn giữ first/last khi tổng > 7.
- **Modal data-attributes**: `[data-confirm-delete]`, `data-action`, `data-confirm-message`, `data-return-url` để view có thể wire nút Xóa mà không cần JS riêng.
- **Toast TempData**: server set `TempData["Toast"]` thì JS auto-show sau 4s, có nút đóng. Hai variant: success/error.
- **Submit lock**: tự động disable mọi `button[type=submit]` trong form khi submit, đổi text thành "Đang xử lý...". Skip nếu form có `data-skip-lock="1"`.

### Build / Test

```
dotnet build: 0 errors
dotnet test:  1/1 passed (Web smoke)
```

## Phase 6 – Triển khai các màn hình (hoàn thành)

### File đã tạo/sửa

- `Controllers/UsersController.cs` (SCR-01..05: Index, Details, Create, Edit, Delete với returnUrl sanitization).
- `Controllers/DocumentsController.cs` (SCR-06 + proxy API-10 với ETag/If-None-Match).
- `Views/Users/Index.cshtml` (bảng, sort headers, pagination, confirm-delete, returnUrl giữ qua tất cả link).
- `Views/Users/Details.cshtml` (info card + grid giấy tờ + lightbox + expiry badge).
- `Views/Users/Create.cshtml`, `Edit.cshtml` (bọc `_UserForm`).
- `Views/Users/_UserForm.cshtml` (DataAnnotations + field errors từ API theo `ApiFieldToForm` map).
- `Views/Documents/Form.cshtml` (CCCD/CMND/Passport switching bằng JS, preview ảnh hiện tại ở edit mode).
- `Views/Shared/_Layout.cshtml` (mount `_ConfirmDeleteModal`).
- `Views/Shared/_ConfirmDeleteModal.cshtml` (modal xác nhận xóa dùng cho cả User và Document).
- `Views/Shared/_Pagination.cshtml` (Đầu/Trước/Sau/Cuối + page numbers + page size selector 10/20/50).
- `ViewModels/Users/UserListViewModels.cs` (PageNumbers, BuildQueryString, PreviousSortOrder, SortIndicator).
- `ViewModels/Users/UserDetailViewModels.cs` (GenderDisplay, HasAllDocumentTypes, ShowsBackImageInput).
- `wwwroot/js/site.js` (Toast top-right 4s, ConfirmDelete modal, form submit lock, async fetch delete với anti-forgery, Lightbox overlay, TempData auto-show).
- `Program.cs` (đăng ký `IUsersApiClient`, `AddAntiforgery` với header `RequestVerificationToken`).
- `tailwind.config.js` (đã có sẵn, scan thêm các View mới).

### Quyết định

- **route dictionary cho `asp-all-route-data`**: Edit mode cần thêm `documentId` vào route ngoài `userId` + `returnUrl`. Dùng `Dictionary<string,string>` thay vì anonymous object để tránh lỗi CS0173 của Roslyn.
- **CCCD/CMND/Passport visibility**: trong Create mode, `ShowsBackImageInput` luôn `true` vì user chưa chọn type; JS trong `Form.cshtml` tự ẩn input khi type là Passport và xóa file đã chọn để tránh gửi nhầm ảnh sau.
- **API image URL remap**: API trả `/api/v1/users/{u}/identity-documents/{d}/images/{side}` nhưng Frontend dùng proxy `/Users/{u}/Documents/{d}/Image/{side}` để tránh CORS và thống nhất qua MVC. Remap thủ công trong `Details.cshtml`.
- **returnUrl sanitization**: mọi action chỉ chấp nhận URL bắt đầu `/Users` và pass `Url.IsLocalUrl`. Áp dụng cho cả Details, Create, Edit, Delete, Documents CRUD. Chặn open redirect.
- **Sort headers trên Index**: mỗi cột (fullName/email/createdAt) link về `BuildQueryString(1, ...)` để đổi sort reset về trang 1; previous sort order là ngược lại của hiện tại.
- **Image preview trong Edit**: hiển thị `<img>` với URL proxy nhỏ (h-16) cạnh input file để user thấy ảnh hiện tại trước khi chọn ảnh mới.
- **Expiry badge**: `DateOnly.FromDateTime(DateTime.UtcNow)` so với `doc.ExpiryDate` để hiển thị "Đã hết hạn" badge đỏ (UI-Spec §4.4).

### Test coverage (23 Web tests)

| Class | Count | Quy tắc |
|---|---|---|
| WebSmokeTests | 1 | / → /Users redirect |
| UserListViewModelTests | 6 | PageNumbers, BuildQueryString, SortIndicator |
| UserFormViewModelTests | 5 | IsEdit, FieldError, ApiFieldToForm map |
| IdentityDocumentFormViewModelTests | 5 | IsEdit, ShowsBackImageInput theo type |
| ApiResultTests | 3 | Ok/Failure, FieldErrors, GetDisplayMessage |
| ApiTransportExceptionTests | 0 | (chỉ exception type, không cần test riêng) |
| UserListViewModelPaginationTests | 3 | (gộp ở UserListViewModelTests) |

### Build / Test

```
dotnet build: 0 errors
dotnet test:  127/127 passed (104 API + 23 Web)
```

## Phase 7 – Kiểm thử tích hợp End-to-End (hoàn thành)

### File đã tạo/sửa

- `tests/UserManagement.Web.Tests/Infrastructure/IntegrationTestHarness.cs` (`IAsyncLifetime` boot API + Web, per-test database `UserManagementDb_It_<guid>`, per-test file storage directory, random free port, drop database on dispose).
- `tests/UserManagement.Web.Tests/EndToEndTests.cs` (17 E2E tests qua HTTP thực sự giữa Web MVC, API, SQL Server 2022 và file storage directory).
- `src/UserManagement.Web/Program.cs` (bỏ `UseHttpsRedirection` khi `DISABLE_HTTPS_REDIRECT=true` để test client không bị redirect sang HTTPS port không xác định).

### Quyết định

- **API host chạy thật trên Kestrel** (không dùng `WebApplicationFactory<ApiProgram>`) để test được toàn bộ HTTP pipeline thực — middleware, routing, multipart upload, response stream. `IntegrationTestHarness.BuildApiHostAsync` dựng lại bằng `WebApplication.CreateBuilder()` với cùng services + middleware như `Api/Program.cs` nhưng bind vào port được cấp phát ngẫu nhiên.
- **Web host dùng `WebApplicationFactory<Program>`**: `WebApplicationFactory` chạy Web trong bộ nhớ qua TestServer. Web gọi sang API qua `IHttpClientFactory` thật (`http://localhost:<port>`).
- **Cấu hình qua environment variables**: `WebApplication.CreateBuilder(args)` trong Web's `Program.cs` đọc `appsettings.json` TRƯỚC, nên `WebApplicationFactory.ConfigureAppConfiguration` callback không override. Thay vào đó, `IntegrationTestHarness` set `Api__BaseUrl` (env var với `__` thay cho `:`) trước khi tạo factory, và Web picks up giá trị mới này.
- **Anti-redirect HTTPS trong test**: `DISABLE_HTTPS_REDIRECT=true` để `WebApplication.CreateBuilder` không thêm `UseHttpsRedirection` (vì test client không biết HTTPS port). Production vẫn giữ redirect bình thường.
- **Test client `AllowAutoRedirect = true`**: giúp test đơn giản hơn — sau khi POST xong, test nhận luôn response của trang đích. Trừ một số test cố tình dùng `WebApplicationFactoryClientOptions.AllowAutoRedirect=false`.
- **Per-test database**: `DatabaseName = UserManagementDb_It_<guid>`, được migrate bằng `db.Database.MigrateAsync()` ngay sau khi API host start. Dispose sẽ `DROP DATABASE` trên `master`.
- **Tiny PNG helper**: `MakeTinyPng()` trả 1×1 transparent PNG (89 bytes) để test upload ảnh mà không cần file thật.

### Test coverage (17 E2E tests)

| Test | Phủ flow |
|---|---|
| Api_Health_Reachable_Directly | Smoke test API host |
| Web_Api_Config_Is_Loaded | Smoke test cấu hình Web trỏ đúng API |
| Create_Form_Contains_Antiforgery_Token | Anti-forgery token trên form GET |
| ReturnUrl_Is_Sanitized_For_Details | Open redirect guard (UI-Spec §4.3) |
| Create_Valid_User_Flow | Tạo user hợp lệ + render details |
| Create_Duplicate_Email_Shows_Error | Tạo trùng email, surface field error |
| Search_Strips_Diacritics | Tìm kiếm bỏ dấu (Nguyễn → nguyen) |
| Pagination_Shows_Correct_Page | Phân trang |
| Details_Page_Loads | Xem chi tiết user |
| Edit_Existing_User | Sửa user |
| Delete_User_Redirects_To_List | Xóa user + toast |
| Deleted_User_Details_Redirects_To_List | User đã xóa không truy cập được |
| Create_Identity_Document_CCCD | Tạo giấy tờ CCCD + upload 2 ảnh PNG |
| Image_Proxy_Routes_Through_Web | 404 cho document không tồn tại |

### Build / Test

```
dotnet build: 0 errors
dotnet test:  144/144 passed (104 API + 40 Web = 23 unit + 17 E2E)
```

---

## Feature mở rộng – OCR giấy tờ + Cloudinary (đặc tả, chờ duyệt)

Tài liệu đặc tả: `docs/OCR-Cloudinary-Spec.md` (SPEC-OCR-CLD-001 v1.0).

> **Cập nhật:** mục này mô tả trạng thái *trước khi triển khai*.
> Xem **Phase 8.5** ở cuối file để biết cái đã thực sự được code.

**Trạng thái:** mới chỉ có tài liệu, **chưa viết code**. Cần phê duyệt đặc tả và
chốt 10 điểm mở ở `OCR-Cloudinary-Spec.md` §13 trước khi triển khai.

### Quyết định đã chốt (do người dùng xác nhận)

| Hạng mục | Quyết định |
|---|---|
| OCR engine | **PaddleOCR** — service Python riêng, giao tiếp REST (`docs/OCR-Cloudinary-Spec.md` §3) |
| Lưu trữ ảnh | **Cloudinary — thay thế hoàn toàn** FileStorage local, không giữ fallback |
| Trải nghiệm | **Auto-fill** — quét xong điền sẵn form, user review rồi bấm Lưu |
| Phạm vi lượt này | Chỉ tài liệu |

### Mâu thuẫn với tài liệu gốc cần lưu ý

- `SRS.md` §1.2 liệt kê **"OCR giấy tờ"** trong danh sách *ngoài phạm vi phiên bản 1.0*.
  Feature này là mở rộng phạm vi → cần cập nhật `SRS.md` §1.2 khi triển khai.
- `SRS.md` §6, `Database-Design.md` §6 và `API-Spec.md` §5 đều giả định lưu ảnh
  trên **ổ đĩa ngoài wwwroot** với `RootPath`. Chuyển sang Cloudinary làm thay đổi:
  - Cột DB `FrontImagePath`/`BackImagePath` → `FrontImagePublicId`/`BackImagePublicId`.
  - `FileStorage:RootPath` không còn áp dụng cho ảnh (giữ cấu hình cũ cho tới khi
    dọn dẹp).
  - Rủi ro R2 (lộ `ApiSecret`) và R1 (PII trên CDN) là rủi ro cao, đã đưa vào checklist.

### Điểm chưa rõ (10 câu hỏi, `OCR-Cloudinary-Spec.md` §13)

| # | Câu hỏi | Đề xuất |
|---|---|---|
| Q1 | SDK `CloudinaryDotNet` hay raw HTTP? | Raw HTTP (`HttpClient` + SHA-1 signature) |
| Q2 | Đã có dữ liệu ảnh trên ổ đĩa cần migrate? | Nếu chưa → bỏ qua F7 |
| Q3 | Quy tắc năm 2 chữ số cho ngày sinh CCCD? | Có, nhưng gắn badge "cần kiểm tra" |
| Q4 | Có OCR mặt sau CMND cũ? | Có, độ tin cậy thấp hơn |
| Q5 | API-10 redirect 307 hay stream proxy? | Stream proxy trước |
| Q6 | Chi phí/quota Cloudinary? | Cần thông tin từ chủ dự án |
| Q7 | Giữ ảnh gốc hay chỉ bản tối ưu? | Giữ bản gốc |
| Q8 | Giới hạn số lần quét OCR? | Tạm thời không (chưa có auth) |
| Q9 | Có redact PII trong log? | **Có** — chỉ log tên field + confidence |
| Q10 | Giữ cột `FrontImagePath` cũ? | Giữ nullable 1 release, drop sau |

### Rủi ro cao đã ghi nhận

- **R1** — Ảnh CCCD là PII nhạy cảm; dùng `authenticated` delivery + signed URL có thời hạn.
- **R2** — `ApiSecret` phải nằm ngoài source control (User Secrets / env var).
- **R3** — OCR đọc sai → bắt buộc user review, không auto-save.

### Cách đặt secret cho Cloudinary (Windows, dev)

```powershell
dotnet user-secrets set "Cloudinary:ApiSecret" "<secret>" --project src/UserManagement.Api
```

Production: biến môi trường `Cloudinary__ApiSecret` hoặc Azure Key Vault.
Không bao giờ commit giá trị vào `appsettings.json`.

## Ghi chú UI redesign (không thuộc đặc tả gốc)

> Nội dung mục này được viết ở lượt trước, giữ nguyên.
> Xem thêm phần đối chiếu SRS gốc ở dưới.

---

## Đối chiếu SRS gốc (PDF SRS-UM-001 v2.0)

Tài liệu phụ trợ: `docs/SRS-Goc-Doi-Chieu.md` và `docs/NOTES-SRS-Goc.md`.

**Trạng thái:** phân tích đối chiếu, **chưa sửa code**.

### Kết luận chính

- `docs/SRS_He_thong_Quan_ly_User_v2_0.pdf` là **bản rút gọn cho AI Agent** của
  SRS-UM-001 v2.0 và là **đặc tả gốc có thẩm quyền cao nhất** trong `docs/`.
- **11/11 endpoint, 12/12 quy tắc nghiệp vụ (BR-01…BR-12), 6 màn hình đã đáp ứng.**
- 144/144 test pass.
- Phát hiện **7 khoảng trống** — xem bảng dưới.

| # | Khoảng trống | Điều khoản PDF | Mức độ |
|---|---|---|---|
| G1 | Không ghi log truy cập ảnh (`_logger` khai báo nhưng không dùng) | §6.1 — **PHẢI** | Không rõ |
| G2 | `Cache-Control` ảnh là `private, max-age=300`, PDF đòi `private, no-cache`; Web proxy không chuyển tiếp header này | §4.3 #10 | Trung bình |
| G3 | Tìm kiếm chưa chuẩn hóa tiền tố `84` → `+84912345678` không tìm thấy `0912345678` | §FR-02 | Trung bình |
| G4 | Thiếu chuỗi `Tìm thấy N kết quả cho 'từ khóa'` | §FR-02 | Thấp |
| G5 | Chưa có retry cho GET | §1.5 | Thấp |
| G6 | Nhãn `CCCD / Thẻ căn cước` (code: `CCCD — Căn cước công dân`) | §SCR-02, §1.7 | Thấp |
| G7 | Tên file frontend `app.css`/`site.js` ≠ PDF `site.css`/`users.js` | §1.4 | Thấp |

> **G1 và G3 có test case rõ ràng trong PDF §7** → nên ưu tiên sửa trước.

### Xung đột với quyết định Cloudinary

PDF §1.6, §3.1, §3.5 yêu cầu ảnh lưu trong thư mục `FileStorage:RootPath` với
`FrontImagePath`/`BackImagePath` là đường dẫn tương đối.

Quyết định "Cloudinary thay thế hoàn toàn" **mâu thuẫn trực tiếp** với 3 điều khoản này
→ cần ghi nhận là **bản diễn biến của SRS-UM-001 (v2.1)**.

### Điểm tích cực

PDF §1.7 ghi rõ: *"Ảnh lưu trên thư mục server qua IFileStorageService (**dễ thay bằng cloud**)"*
→ quyết định dùng Cloudinary **đúng hướng thiết kế gốc**, không phải tự ý thêm tính năng.

### Cần chủ dự án xác nhận

| # | Câu hỏi |
|---|---|
| Q1 | `Cache-Control` dùng `private, no-cache` (PDF) hay `private, max-age=300` (code)? |
| Q2 | Có bắt buộc ghi log truy cập ảnh không (PDF §6.1 dùng chữ PHẢI)? |
| Q3 | Chấp nhận đổi §3.5 của PDF khi chuyển Cloudinary chứ? |
| Q4 | Có cần retry cho GET không (PDF §1.5)? |

Đã thiết kế lại toàn bộ giao diện `UserManagement.Web` theo yêu cầu "UX/UI đẹp nhất,
chuyên nghiệp" (sidebar + topbar, dark mode, design tokens Tailwind, animation,
toast/modal/lightbox mới). Nội dung chức năng giữ nguyên theo `UI-Spec.md`:
route, field, hành vi đều không đổi — chỉ thay đổi lớp trình bày.

Một số điểm lệch nhẹ so với `UI-Spec.md` cần ghi nhận:

- `UI-Spec.md` §1.1 yêu cầu layout `max-w-6xl` và header `Quản lý User` cố định.
  Bản redesign dùng `max-w-7xl` + sidebar thay cho header cố định. Tên sản phẩm
  trong topbar vẫn là "User Management".
- `UI-Spec.md` §1.4 yêu cầu toast ở **góc trên bên phải**. Redesign đặt ở **góc dưới
  bên phải** (thông lệ phổ biến hơn, không che nội dung chính). Nếu cần bám sát
  đặc tả thì đổi lại vị trí.
- `UI-Spec.md` §4.1 yêu cầu hiển thị `---` khi không có dữ liệu. Redesign dùng `—`
  (em dash) cho nhất quán thị giác.
- `UI-Spec.md` quy định màu chính `indigo-600`. Redesign dùng palette `brand` xanh
  dương tương đương (`#1f45f5` ≈ indigo-600) — cùng hệ màu, chỉ đổi tên token.

Nếu cần tuân thủ tuyệt đối đặc tả gốc, các điểm trên có thể chỉnh lại.

## SRS v2.1 — PDF đã được cập nhật trực tiếp

Yêu cầu "bổ sung luôn vào PDF" đã được thực hiện bằng cách viết pipeline sinh PDF
trong `tools/` (thay vì sửa tay trong Word, vốn không khả thi cho phần bổ sung lớn).

Kết quả: `docs/SRS_He_thong_Quan_ly_User_v2_0.pdf` giờ là **v2.1**, 18 trang
(15 trang gốc giữ nguyên nội dung + 3 trang mục 9 mới).

Cách thực hiện:

- 15 trang gốc: chỉ đổi header (`Phiên bản: 2.1`), footer (`SRS-UM-001 v2.1`) và
  gắn nhãn đỏ `[v2.1]` cạnh 12 điều khoản bị sửa. Nội dung gốc không bị xoá.
- 3 trang mới: mục 9 "Bổ sung – OCR giấy tờ và lưu trữ ảnh Cloudinary (v2.1)",
  gồm FR-09/FR-10, BR-13 → BR-17, bảng đối chiếu điều khoản bị thay đổi,
  endpoint #12/#13, môi trường và rủi ro, Bước 9, và danh sách điểm cần xác nhận.
- Mục lục (TOC) đã cập nhật: 86 mục, có đầy đủ 9.1 → 9.8.

Bản gốc v2.0 được giữ ở `docs/SRS_He_thong_Quan_ly_User_v2_0-v2.0-ORIGINAL.pdf` để
rollback (file này được `.gitignore` bỏ qua).

Sửa nội dung v2.1 trong tương lai: chỉnh HTML trong `tools/sec9_*.py` rồi chạy
`python tools/build_srs_pdf.py`.

### Ghi chú kỹ thuật khi sinh PDF

- PyMuPDF biến ligature `ti` của **Calibri** thành `Ɵ` (U+019F) khi shaping, làm hỏng
  trích xuất text tiếng Việt. Thân mục 9 dùng **Arial** (PDF gốc đã nhúng sẵn
  ArialMT nên nhìn đồng bộ). Header dùng Calibri đã subset.
- Font được subset bằng `fontTools` trước khi render, giữ dung lượng file gần bằng
  bản gốc (929 KB so với 509 KB).
- Chữ footer cũ được xoá bằng redaction annotation, nên không còn trong text layer;
  footer mới vẽ bằng base-14 Helvetica để trích xuất ra đúng chuỗi ASCII.

---

## Phase 8.5 – OCR giấy tờ + Cloudinary (hoàn thành)

Triển khai đầy đủ theo `docs/OCR-Cloudinary-Spec.md` + SRS v2.1, và sửa 3 khoảng
trống G1/G2/G3 phát hiện trong đối chiếu SRS gốc.

### Thay đổi schema

| Trước | Sau |
|---|---|
| `IdentityDocument.FrontImagePath` (nvarchar) | `FrontImagePublicId` / `BackImagePublicId` (nvarchar) |
| — | `FrontImageVersion` / `BackImageVersion` (bigint) |
| — | `FrontImageWidth` / `Height`, `BackImageWidth` / `Height` (int) |
| — | Bảng mới `OcrExtractionResults` (audit trail mỗi lần quét) |

Migration: `20261123122526_AddCloudinaryAndOcr`.

`OcrExtractionResults`: `ScanId` unique (UX_OcrExtractionResults_ScanId),
FK `UserId` → `Users` (`NO ACTION`), FK `DocumentId` → `IdentityDocuments` (`SET NULL`).

### Backend (`src/UserManagement.Api`)

| File | Vai trò |
|---|---|
| `FileStorage/CloudinaryOptions.cs` | Config `Cloudinary:*` |
| `FileStorage/ICloudinaryStorage.cs` | Interface: upload / move / delete / delivery URL / download |
| `FileStorage/CloudinaryStorage.cs` | Raw HTTP + SHA-1 signature (`image/upload`, `image/rename`, `image/destroy`) |
| `Ocr/OcrOptions.cs` | Config `Ocr:*` |
| `Ocr/PaddleOcrModels.cs`, `PaddleOcrClient.cs` | HTTP client tới FastAPI service |
| `Ocr/OcrFieldExtractor.cs` | Label → field mapping (đây là nguồn sự thật, .NET) |
| `Ocr/OcrNormalizer.cs` | Chuẩn hóa + validate theo BR-* |
| `Ocr/OcrCleanupService.cs` | BackgroundService dọn ảnh tạm hết hạn |
| `Services/OcrService.cs` | Orchestration API-12 (scan) + API-13 (confirm) |
| `Controllers/IdentityDocumentsOcrController.cs` | 2 endpoint OCR |
| `Entities/OcrExtractionResult.cs` | Audit trail |

### Frontend (`src/UserManagement.Web`)

- `Services/OcrDtos.cs` + `UsersApiClient.ScanOcrAsync/ConfirmOcrAsync`.
- `Controllers/DocumentsController.cs` → action `OcrScan` proxy multipart.
- `Views/Documents/_OcrResultPanel.cshtml` — panel kết quả + auto-fill review.
- `wwwroot/js/ocr.js` — auto-fill client-side, không tự submit (BR-13).

### Microservice (`services/paddleocr`)

FastAPI + PaddleOCR 3.2.0. Endpoints `/ocr/identity-document` (multipart 2 mặt),
`/health`. Tiền xử lý: CLAHE (tương phản) + deskew + denoise.

### Ranh giới trách nhiệm (quyết định quan trọng)

**Python chỉ đọc pixel — không hiểu giấy tờ.** Mọi quy tắc nghiệp vụ (chuẩn hóa tên
tiếng Việt, suy luận năm 2 chữ số, validate số CCCD 12 chữ số, ánh xạ giới tính)
đặt trong .NET. Lý do:

1. Test được bằng unit test thuần .NET (62 test mới).
2. Không phụ thuộc version PaddleOCR cho tính đúng nghiệp vụ.
3. Python không cần hiểu tiếng Việt có dấu → giảm rủi ro OCR.

### Sửa khoảng trống SRS gốc

| # | Sửa gì | Ở đâu |
|---|---|---|
| G1 | Ghi log truy cập ảnh: timestamp, client IP, userId, documentId, side | `IdentityDocumentsController.LogImageAccess` |
| G2 | `Cache-Control: private, no-cache` (+ ETag, Last-Modified) | API-10 |
| G3 | Chuẩn hóa `+84`/`84` → `0` khi search | `UserService.PhoneSearchCandidates` |

### Bug phát hiện khi viết test

| Bug | Mức độ | Cách sửa |
|---|---|---|
| `CloudinaryStorage` deserialize `public_id` không map vào `PublicId` (thiếu `JsonPropertyName`) | **Nặng** — mọi upload sẽ fail trong production | Thêm `[JsonPropertyName("public_id")]` cho toàn bộ field của `CloudinaryUploadResponse` |
| Filter tìm kiếm AND-ed điều kiện phone candidates → từ khóa tên/email không tìm được | **Nặng** — `Search_Strips_Diacritics` fail | Gộp tất cả thành một `OR` duy nhất |
| `OcrFieldExtractor` lấy giá trị ở dòng kế tiếp dù dòng đó là label của field khác | Trung bình — đọc sai giá trị | Thêm `IsAnyLabelLine()` kiểm tra **mọi** label, không chỉ label của field đang xét |
| `IdentityDocumentsControllerTests` dùng `CloudinaryStorage` thật → cần credential | — | `TestApiFactory` thay bằng `TestCloudinaryStorage` (in-memory) |

### Test doubles

| Lớp | Thay thế | Nơi dùng |
|---|---|---|
| `TestCloudinaryStorage` | `ICloudinaryStorage` | API tests, E2E harness |
| `FakePaddleOcrClient` | `IPaddleOcrClient` | E2E harness |

Cả hai đều `internal` → `AssemblyInfo.cs` có
`[assembly: InternalsVisibleTo("UserManagement.Api.Tests")]` và
`[assembly: InternalsVisibleTo("UserManagement.Web.Tests")]`.

### Test coverage mới

| Nhóm | Số test | Nội dung |
|---|---|---|
| `OcrFieldExtractorTests` | 15 | Label matching (có/không dấu), front/back, next-line fallback, CRLF |
| `OcrNormalizerTests` | 17 | Số CCCD, tên, giới tính, ngày (2 chữ số → inferred), địa chỉ, confidence |
| `CloudinaryStorageTests` | 13 | Endpoint, signed params, SHA-1 signature, error handling, delivery URL |

### Build / Test

```
dotnet build: 0 errors
dotnet test:  227/227 passed (187 API + 40 Web = 88 unit + 78 integration/E2E + 1 live smoke)
```

---

## Phase 8.6 – Sửa lỗi Cloudinary thật (phát hiện khi chạy thử với tài khoản thật)

Khi chạy thật với tài khoản Cloudinary, upload luôn trả **401**. Quá trình debug
lộ ra **4 lỗi riêng biệt** — toàn bộ đều do code chứ không phải do cấu hình.

### Lỗi 1 — `public_id` không map vào `PublicId` (nặng)

`System.Text.Json` không tự map `public_id` → `PublicId`. Mọi upload sẽ fail
với "Cloudinary did not return a public id". Đã thêm `[JsonPropertyName]` cho
toàn bộ field của `CloudinaryUploadResponse`.

### Lỗi 2 — Bộ tham số ký ≠ bộ tham số gửi (nặng)

Cloudinary ký **mọi tham số trong body trừ** `file`, `cloud_name`, `resource_type`,
`api_key` — và nó kiểm tra đúng các cặp `name=value` đó. Code cũ ký 6 tham số nhưng
gửi 8 (thêm `max_file_size`, `type`), và `allowed_formats` bị gửi 2 lần
(`jpg,png,jpg,png`) → "Invalid Signature".

Đã sửa: một `SortedDictionary` là nguồn sự thật duy nhất — vừa để ký, vừa để post.
Áp dụng cho cả `upload`, `rename`, `destroy`.

### Lỗi 3 — `AllowedFormats` bị nhân đôi do config binder (nặng)

ASP.NET Core **nối mảng** khi nhiều nguồn config cùng cung cấp một key, chứ không
thay thế. `appsettings.json` + `appsettings.Development.json` + file `.local.json`
→ `AllowedFormats` = `jpg,png,jpg,png` → sai string-to-sign → "Invalid Signature".

Đã sửa: setter của `CloudinaryOptions.AllowedFormats` khử trùng lặp
(`Distinct` + bỏ rỗng), nên đúng dù cấu hình lớp mấy tầng thì kết quả vẫn đúng.

### Lỗi 4 — Thiếu `timestamp` trong body / thiếu escape (nặng)

- `timestamp` bị thêm 2 lần vào multipart (một lần tường minh, một lần từ vòng lặp
  post các tham số đã ký) → body sai.
- Signature version 2 của Cloudinary chỉ escape `&` → `%26` bên trong value
  (chống parameter smuggling), **không** escape gì khác. Code đã escape toàn bộ
  chuỗi bằng `Uri.EscapeDataString` → sai.

Đã sửa cả hai. Thuật toán ký hiện khớp **chính xác** với 2 ví dụ trong tài liệu
Cloudinary (`a21ad0f63beb4de2e5575204b79ab90bffb02c10` và
`bfd09f95f331f558cbd1320e67aa8d488770583e`) — có test khoá lại.

### Cải thiện khả năng chẩn đoán

Trước đây lỗi chỉ hiện `401` — không biết nguyên nhân. Nay:

- `CloudinaryStorage.ExtractErrorMessage()` bóc message thật từ body JSON.
- `CloudinaryStorage.BuildHint()` dịch message Cloudinary thành gợi ý cụ thể:
  - `Invalid cloud_name` / `cloud_name mismatch` → *"kiểm tra lại `Cloudinary:CloudName`"*
  - `Invalid Signature` → *"kiểm tra lại `Cloudinary:ApiSecret`"*
- Thông báo còn kèm sẵn giá trị `CloudName` đang dùng.

### Bảo mật — secret đã bị commit

`ApiSecret` từng nằm trong `appsettings.json` (file được commit). Đã:

1. Xóa secret khỏi `appsettings.json`, thêm `.gitignore` cho
   `appsettings.Development.local.json`.
2. Thêm `Content Update="appsettings.*.local.json"` vào `.csproj` vì Web SDK mặc
   định **không** copy file `*.local.json` ra thư mục output.
3. Tạo `appsettings.Development.local.json` (git-ignored) giữ credential dev.
4. Thêm `CloudinaryLiveSmokeTests` — gọi API Cloudinary thật, tự skip nếu chưa cấu
   hình, xoá ảnh sau khi test.

> **Lưu ý:** vì secret đã nằm trong file commit, nên **rotate API secret** trên
> Cloudinary Console. Xoá khỏi file trong repo không gỡ được secret khỏi git history.

### Việc còn lại của người dùng

`cloud_name` phải lấy từ **Cloudinary Console → Settings → API Keys** và phải
thuộc đúng product environment của `api_key` đang dùng. Cloudinary **không** cho
suy ra cloud name từ api_key — phải tra cứu thủ công.

---

## Phase 8.7 – Cloudinary thật: cloud name đúng + delivery URL có chữ ký

Sau khi có `CLOUDINARY_URL` đúng (`cloudinary://...@smbl2hal`), upload đã thành
công — nhưng phát sinh 3 vấn đề mới, **đều do code**, không phải do cấu hình.

### Lỗi 5 — `cloud_name` sai (nặng)

`appsettings.Development.local.json` đang để `CloudName: "cccd"` → Cloudinary trả
`401` + `Invalid cloud_name cccd`. Đã sửa thành `smbl2hal`.

### Lỗi 6 — Ảnh upload vẫn đọc được công khai (nặng)

Code cũ upload vào `image/upload` với `type=upload` → ảnh **public**, ai có URL
cũng xem được. Để ẩn ảnh PII phải dùng **`type=authenticated`**.

Bằng chứng thực nghiệm trên cloud `smbl2hal`:

| Cách upload | Response | `image/upload/{pid}` | `image/authenticated/{pid}` |
|---|---|---|---|
| `type=upload` | `access_mode: public` | **200** | 401 |
| `access_mode=authenticated` | `access_mode: public` | **200** | 401 |
| `type=authenticated` | `type: authenticated` | **404** | 401 (thiếu chữ ký) |

→ `access_mode` bị API **bỏ qua**; chỉ `type=authenticated` mới có tác dụng.

### Lỗi 7 — URL delivery thiếu chữ ký (nặng)

`BuildDeliveryUrl()` trả `.../image/authenticated/{publicId}` — Cloudinary trả
**401** vì URL của asset `authenticated` **bắt buộc** phải có chữ ký:

| URL | Kết quả |
|---|---|
| `image/authenticated/{publicId}` | **401** |
| `image/authenticated/s--SAI--/{publicId}` | **401** |
| `image/authenticated/s--{đúng}--/{publicId}` | **200** |

Chữ ký URL **khác** chữ ký API:

```text
url_signature = base64url(SHA1(publicId + api_secret))[0..8]
api_signature = hex(SHA1("k1=v1&k2=v2" + api_secret))
```

`publicId` phải **không kèm đuôi file** (Cloudinary lưu format riêng). Nếu public
id đã có `.png`, URL cần đuôi file gấp đôi (`a.png.png`) → nên code upload
**không** ghi đuôi file vào public id (đổi `unique_filename` → `false` và tự đặt
`public_id`).

### Lỗi 8 — `rename`/`destroy` không truyền `type` (nặng)

`rename` và `destroy` phải kèm `type=authenticated`, nếu không Cloudinary tìm asset
type `upload` → `Resource not found` / `result: not found`. **Ảnh sẽ không bị xoá**
(implicit leak). Đã thêm `type=authenticated` vào cả 3 lệnh (upload/rename/destroy)
khi `UseSecureDelivery = true`.

### Lỗi 9 — CDN invalidation bất đồng bộ (trung bình)

API trả `ok` cho `destroy` nhưng CDN vẫn serve ảnh thêm **~7–40 giây**. Test
lifecycle phải **poll** thay vì assert ngay, nếu không test sẽ chập chờn.

### Kiểm thử

- `CloudinaryLifecycleSmokeTests` — upload → download → move → download → delete
  trên tài khoản thật (poll tới khi CDN hết cache).
- `CloudinaryStorageTests` — thêm case cho `type=authenticated` (upload/rename/
  destroy), chữ ký URL, và delivery URL có `s--`.
- Toàn bộ suite: **233 test pass** (40 Web + 193 API), 0 fail.



---

## Phase 8.8 - Sua loi logic theo code review

Ban code review chap 9 muc (4 critical, 4 logic, 1 bao mat). Cac muc duoc xac nhan
la loi that su trong code da deu da sua va co test. Hai muc khong phai loi:
muc 8 (thu tu sort co dinh - dung theo yeu cau) va muc 9 (authentication/authorization
nam ngoai pham vi v1.0 theo SRS 1.2/1.3/6).

### Da sua

| # | Muc | Files | Test |
|---|---|---|---|
| 1 | Upload 1 anh that bai -> anh con lai bi orphan tren Cloudinary | IdentityDocumentService | IdentityDocumentServiceCleanupTests |
| 2 | Validate (Passport khong duoc co anh sau) chay SAU khi da upload | IdentityDocumentService | IdentityDocumentServiceCleanupTests |
| 3 | IDOR: client tu chon publicId de promote anh bat ky | OcrService, OcrRequests | OcrServiceConfirmTests |
| 4 | API-13 thieu quy tac anh CCCD/CMND vs Passport | OcrService | OcrServiceConfirmTests |
| 5 | 304 khong phai success status -> proxy tra 404, mat ETag | UsersApiClient, DocumentsController | DocumentImageProxyTests |
| 6 | Cleanup job danh Expired ca khi xoa that bai -> anh leak vinh vien | OcrCleanupService | OcrCleanupServiceTests |
| 7 | API-13 tra 500 do CreatedAtAction sai ten action | IdentityDocumentsOcrController | OcrControllerTests |
| 8 | Upload/rename/destroy thieu type=authenticated | CloudinaryStorage | CloudinaryStorageTests |

### Chi tiet quan trong

**Muc 3 (IDOR) - muc nang dong.** OcrConfirmRequest da bo FrontImagePublicId va
BackImagePublicId. Public id chi lay tu ban ghi OcrExtractionResult trong DB,
roi kiem tra segment 'tmp' dung truoc {scanId}. Cach so sanh theo segment (khong
phai substring) de tranh scan id xuat hien o vi tri khac trong path.

**Muc 7 - loi moi phat hien khi viet test.** CreatedAtAction nhan TEN ACTION method,
nhung code truyen TEN ROUTE (GetIdentityDocument); action method that la GetById.
Link generation that bai -> API-13 tra 500 thay vi 201. Da doi sang CreatedAtRoute
(dung ten route) nhu UsersController da dung.

**Muc 5.** HttpResponseMessage.IsSuccessStatusCode chi true voi 2xx, nen 304 bi coi
la loi. Proxy phai kiem tra StatusCode == NotModified rieng, va copy ETag /
Last-Modified / Cache-Control sang response cua MVC. Da them ca content type - API
phuc vu ca JPEG lan PNG.

**Muc 6.** Ban ghi scan chi duoc danh Expired khi TAT CA asset da xoa thanh cong.
Neu xoa that bai, giu nguyen ExpiresAt de lan chay sau thu lai - danh Expired luc
anh van con tren CDN se leak PII vinh vien.

### Kiem thu

```
dotnet build: 0 errors
dotnet test:  265/265 passed (212 API + 53 Web)
```


### Nang cap dependency

SixLabors.ImageSharp 3.1.10 -> **3.2.0** (3.1.10 co advisory GHSA-gwg2-r3hj-4w44 va
GHSA-j3p4-wp97-rph4 - xem dotnet list package --vulnerable). Ap dung cho API va
ca 2 project test.

---

## Phase 8.9 – Luong "Them User nhanh" (Quick Create)

Trien khai theo `luong-them-user-nhanh.md` (§2, §3, §6, §10).

### Backend: API-12b (xem truoc OCR khong can User)

| File | Vai tro |
|---|---|
| `DTOs/Users/IdentityDocuments/OcrPreviewRequest.cs` | Request multipart |
| `DTOs/Users/IdentityDocuments/OcrResponses.cs` | `OcrPreviewResponse` |
| `Services/OcrService.cs` | `PreviewAsync` |
| `Controllers/IdentityDocumentsOcrPreviewController.cs` | `POST /api/v1/identity-documents/ocr/preview` |

**Quyet dinh kien truc quan (quan trong):** API-12 yeu cau `{userId}` vi
`OcrExtractionResult.UserId` va `IdentityDocument.UserId` la khoa ngoai bat buoc.
Nhung Quick Create can dien san form **truoc khi User ton tai**. Vi vay them
API-12b: chay `IPaddleOcrClient.RecognizeAsync` truc tiep, **khong** upload Cloudinary
tam, **khong** ghi `OcrExtractionResult`. Khong sinh rac, khong can job don dep.

### Frontend

| File | Vai tro |
|---|---|
| `ViewModels/Users/QuickCreateUserViewModel.cs` | Form + `ValidateDocument()` |
| `Controllers/UsersController.cs` | `QuickCreate` GET/POST + `QuickCreateOcrPreview` |
| `Views/Users/QuickCreate.cshtml` | Form gocn, 2 the phuong thuc |
| `Views/Users/Index.cshtml` | Nut `+ Them User nhanh` |
| `wwwroot/js/quick-create-user.js` | Chuyen mode, preview anh, auto-fill, submit lock |
| `wwwroot/css/app.src.css` | `.qc-mode-card` |

### Luong luu 2 buoc (BR-7)

```text
POST /Users/QuickCreate
   |
   v
Buoc 1: POST /api/v1/users                     -> tao User
   |  that bai: giu form, loi theo field
   v
Buoc 2 (Mode = ocr): POST /api/v1/users/{id}/identity-documents
   |  that bai: KHONG tao lai User -> redirect Details + canh bao
   v
Toast -> Details
```

Bước 1 luôn chạy truoc vi `IdentityDocument.UserId` la khoa ngoai bat buoc.

### Bug: `IValidatableObject` + ModelState khong tach duoc loi giay to

Lần dau implement, validation giay to nam trong `QuickCreateUserViewModel.Validate()`
(IValidatableObject) -> MVC nhet loi o **model-level key `""`**. Controller phai
`ExtractDocumentErrors()` de tach loi ra khoi ModelState truoc khi tao User.

Hai van de lam cach do **khong hoat dong** tren .NET 10:

1. `ModelState.Remove(field)` xoa ca entry -> mat thong tin.
2. Khong phai loi nao cung duoc tach: khi entry co **khong** error message,
   `errors[field] = []` van duoc gan -> `documentErrors.Count > 0` la `true` ->
   luon chay nhanh "giay to chua hop le" **du du lieu hoan toan hop le**.

**Cach sua:** tach validation giay to ra khoi `IValidatableObject`:

- `Validate()` chi kiem tra phan **User** (hien giong `yield break`).
- `ValidateDocument()` la method explicit tra `Dictionary<string, string[]>`.
- Controller goi `vm.ValidateDocument()` **sau khi** bind, truoc khi tao User.

Kiem chuc dung: `IValidatableObject` khong nen dung cho validation co dieu kien
theo mode, vi loi duoc luu o key dac biet va rat kho tach.

### Test coverage moi

| Nhom | So test | Noi dung |
|---|---|---|
| `OcrServicePreviewTests` (API) | 8 | `PreviewAsync`: format, size, back image, normalize, loi |
| `QuickCreateUserTests` (Web E2E) | 11 | Manual/OCR mode, preview, partial success, duplicate |

### Build / Test

```
dotnet build: 0 errors
dotnet test:  284/284 passed (220 API + 64 Web)
```

### Tai lieu da cap nhat

- `docs/OCR-Cloudinary-Spec.md` §5 – them API-12b.
- `docs/API-Spec.md` §2 – them dong 12, 12b, 13 vao bang endpoint.
- `docs/UI-Spec.md` – them SCR-08, route bang, cau truc View.

---

## Phase 8.10 – Chay OCR that voi PaddleOCR 3.2.0 (fix 4 loi trong microservice)

Khi chay that Web -> API -> PaddleOCR that, log hien:

```
System.Net.Http.HttpRequestException: No connection could be made because the
target machine actively refused it. (localhost:8100)
warn: OCR preview unavailable: Không thể kết nối tới dịch vụ quét tự động.
```

Day **khong phai** loi code .NET – day la trang thai "OCR service chua chay" duoc
thiet ke san (API tra 503, UI giu nguyen luong nhap tay). Nhung khi bat service
len, phat hien **4 loi that** trong `services/paddleocr/app/main.py` (code duoc
viet cho PaddleOCR 2.x trong khi 3.2.0 doi hoàn toan cau truc ket qua).

### Cach chay service

```bash
cd services/paddleocr
pip install -r requirements.txt
python -m uvicorn app.main:app --host 127.0.0.1 --port 8100
```

`GET http://localhost:8100/health` -> `{"status":"ok","modelsLoaded":true}`.
Lan dau model can ~1-2 phut de tai (`~/.paddlex/official_models/`).

### 4 loi da sua trong `services/paddleocr/app/main.py`

| # | Loi | Nguyen nhan | Sua |
|---|---|---|---|
| 1 | `ValueError: not enough values to unpack (expected 3, got 2)` | `preprocess_image` tra anh **grayscale 2D**; PaddleOCR 3.x unpack `img.shape` thanh 3 gia tri nen can anh 3 kenh BGR | `cv2.cvtColor(enhanced, cv2.COLOR_GRAY2BGR)` |
| 2 | `not enough values to unpack (expected 3, got 2)` tai `text_detection/processors.py` | goi `engine.ocr()` (API 2.x, da deprecated) | doi sang `engine.predict()` |
| 3 | `rawText` rong / khong tach duoc truong | 3.x tra ve `OCRResult` voi `rec_texts` / `rec_scores` / `rec_polys`, khong phai list `[[box,(text,score)]]` | `extract_lines` ho tro ca 3.x (`rec_texts`) va 2.x (legacy list), va ca bien the `predict()` tra **generator** |
| 4 | `documentNumber` lay nham gia tri cua dong sau ("H VÀ TÊN" thay vi "SỐ CCCD") | `find_fields` tach value tu **dong raw** bang `line.lower().find("so")` – dong raw co dau tieng Viet nen `find` tra `-1`, value bi lech | tach value tu dong **da chuan hoa** (khong dau) + dung `setdefault` de label tong quat ("so") khong ghi de label cu the ("so cccd") |

Ngoai ra, bo sung label bien the cho OCR hay doc mat chu: `"co gia tri den"`,
`"gia tri den"` (OCR hay bo chu `i`), kem mapping `cogiatriden` /
`giatriden` -> `expiryDate` trong `canonical_field_names`.

### Test hoi quy Python

`services/paddleocr/test_main.py` – 10 test, chay bang `python test_main.py`
(khong can pytest). Phu 4 loi tren + bao dam `preprocess_image` tra anh 3 kenh.

### Test Playwright voi OCR THAT

`services/paddleocr` chi chay khi microservice len; test tu bo qua (skip,
khong fail) neu `GET :8100/health` khong tra 200.

| File | Vai tro |
|---|---|
| `tests/UserManagement.Web.Tests/Infrastructure/LiveOcrTestHarness.cs` | Harness dung `PaddleOcrClient` THAT (khong phai fake) |
| `tests/UserManagement.Web.Tests/BrowserLiveOcrTests.cs` | 2 test: preview qua API + qua trinh duyet |

**Luu y quan trong khi viet harness moi (da ton thoi gian de tim):**

1. API host phai bind **rieng** port: dung `builder.WebHost.UseUrls(...)`, KHONG
   dua vao env var `ASPNETCORE_URLS` (env var dung chung voi Web host -> hai host
   tranh port, request roi sang host sai -> 404).
2. `PaddleOcrClient` can `AddHttpClient<IPaddleOcrClient, PaddleOcrClient>(...)`,
   khong phai `AddSingleton` (thiếu `HttpClient` -> 500 khi goi OCR).
3. Khi goi API bang `HttpClient`, dung `BaseAddress` + path khong dau `/`.
   Ghep chuoi `$"{base}/api/..."` tao `//api/...` -> 404.

### Anh CCCD mo phong

`services/paddleocr/make_test_cccd.py` (can Pillow) ve anh CCCD co dau tieng Viet
bang font Unicode (`arial.ttf`). OpenCV `putText` **khong** ve duoc dau tieng Viet
nen se ra `S??? CCCD` va khong parse duoc – dung Pillow cho test nay.

### Kiem chung end-to-end (Web -> API -> PaddleOCR)

Ca 3 thanh phan phai chay cung luc:

| Thanh phan | Port | Lenh |
|---|---|---|
| PaddleOCR | 8100 | `python -m uvicorn app.main:app --host 127.0.0.1 --port 8100` (trong `services/paddleocr`) |
| API | 5051 | `dotnet run --project SRC/UserManagement.Api` |
| Web | 5052 | `dotnet run --project SRC/UserManagement.Web --urls "http://localhost:5052"` |

Ket qua kiem chung (2026-11-24), anh CCCD mo phong tren:

1. **Microservice** – `POST http://localhost:8100/ocr/identity-document`
   (form field: `front` / `back`) -> HTTP 200, `documentNumber=079095001234`,
   `dateOfBirth=20/04/1995`, `gender=NAM`, `issueDate=10/05/2021`, ~2.9s.
2. **API preview** (API-12b) – `POST http://localhost:5051/api/v1/identity-documents/ocr/preview`
   (form field: `frontImage` / `backImage`) -> HTTP 200, cac field da chuan hoa
   (`dateOfBirth=1995-04-20`, `gender=Male`), ~8s.
3. **Web proxy** – `POST http://localhost:5052/Users/QuickCreate/OcrPreview`
   (can antiforgery token + `documentType`, `frontImage`, `backImage`)
   -> HTTP 200 voi body `{ ok: true, data: {...} }`.
4. **Giao dien** – Quick Create > chon "Quet CCCD" > upload 2 anh > bam
   "Bat dau nhan dien OCR": panel "Ket qua nhan dien" hien thi `So giay to`,
   `Ngay sinh`, `Gioi tinh` (Tin cay cao) va form **tu dong dien**:
   `DateOfBirth=1995-04-20`, `Gender=male`, `DocumentNumber=079095001234`.

Log khong con `actively refused` hay `OCR preview failed with status 503` –
API log `Received HTTP response headers ... - 200` cho moi request toi `:8100`.

> Luu y khi kiem tra thu cong: `dotnet test` **khong chay duoc** khi dang
> chay `dotnet run` cho API/Web – file `UserManagement.Api.exe` bi khoa
> (MSB3027). Hay stop app truoc, chay test, roi chay lai app.

Tong hop kiem thu tai thoi diem nay:

| Bo test | Ket qua |
|---|---|
| `services/paddleocr/test_main.py` | 10/10 pass |
| `tests/UserManagement.Api.Tests` | 220/220 pass |
| `tests/UserManagement.Web.Tests` | 108/108 pass |

---

## Phase 8.11 – OCR tren anh CCCD THAT (sua 9 loi trong microservice + API)

Anh CCCD **that** (chup tay, chup ngoai) khac xa anh mo phong cua
`make_test_cccd.py`. Khi chay `POST :8100/ocr/identity-document` voi anh that,
service tra ve **sai**:

```
documentNumber = "cialist republic of viet nam"   <- header the card
fullName       = "sé/No.048203001078"              <- dong so giay to
dateOfBirth    = "NGUYN MINH KHI"                   <- ten, khong phai ngay sinh
```

Nguyen nhan goc: **anh chup nghinh**. The CCCD la **landscape** nhung anh la
**portrait**, nen PaddleOCR doc dung chu nhung tra ve cac dong theo thu tu
**khong theo layout cua the card** – khong the nao giup "dong sau" cung la
"gia tri cua nhau".

### 9 loi da sua

| # | Loi | Nguyen nhan | Sua |
|---|---|---|---|
| 1 | `documentNumber` = `"cialist republic of viet nam"` | label tong quat `"so"` khop dong header `"SOCIALIST REPUBLIC OF VIET NAM"` | them `GENERIC_LABELS` – label `"so"` chi duoc chap nhan khi gia tri ra **chu so** |
| 2 | `fullName` = `"sé/No.048203001078"` | the CCCD that **khong co** nhan "Số CCCD" – dong so ghi `số/No.048203001078` | `_number_in_line()` lay so ngay tren dong label |
| 3 | `dateOfBirth` = `"NGUYN MINH KHI"` | the **song ngang** nhung gia tri o dong duoi; OCR tra ve thu tu khong dung | `_find_value_for_label()` **dung toa do bounding box** (cung hang / dong duoi + cung cot) thay vi thu tu dong OCR |
| 4 | Anh chup nghinh => moi cot doc | `use_doc_orientation_classify=False` | bat **`use_doc_orientation_classify=True`** – model xoay dung the card, thu tu doc lai dung |
| 5 | OCR bo nguyen am: `"Gii tính"`, `"C gia trj dn"` | anh mo va do phan gian | `_label_matches()` so san tren **khung xương** (bo nguyen am + dau cach) cho phep sai 1 ky tu |
| 6 | `"ngay cap"` khop `"ngay sinh"` => bay `issueDate` = ngay sinh | fallback "first word" ap dung cho moi label | chi dung fallback do cho label ngan (`len <= 3`); them `_value_matches_label()` kiem **dung hinh dang** gia tri (ngay phai la ngay, so phai la so) |
| 7 | `05/10/2003` bi cat thanh `10/2003` | `_clean_value` cat truoc `/` | chi cat khi phan sau **khong phai ngay** |
| 8 | `Ngày, tháng, năm / Date, month, year22/04/2021` | OCR **dinh** ngay vao chu | `_date_in_line()` lay ngay co trong dong label |
| 9 | API bo qua ket qua cua engine | `OcrService` goi `OcrFieldExtractor.Extract(response.RawText)` – **parse lai raw text bang thuat toan yeu hon** | them overload `Extract(engineFields, rawText)`: **ket qua cua engine la chuan**, text scan chi la fallback |

### Cac ham moi trong `services/paddleocr/app/main.py`

| Ham | Vai tro |
|---|---|
| `_build_lines(result)` | Doc `rec_polys` -> `TextLine` (co bounding box) |
| `_is_vertical(line)` | Phat hien box doc – dung khi card chua xoay |
| `_same_row(label, candidate)` | So sanh **tam** 2 box (dung cho gia tri cung dong) |
| `_find_value_for_label()` | Ghep nhan voi gia tri: cung hang ben phai -> giong trong dong label -> dong duoi **cung cot** |
| `find_fields_geo()` | Bo ghep bang **toa do** – thu tu doc cua OCR khong con quan trong |
| `_vowel_insensitive()` / `_skeleton_contains()` | So sanh nhan khi OCR mat nguyen am / mat chu |
| `_date_in_line()` / `_number_in_line()` | Lay gia tri nam ngay tren dong nhan |
| `_value_matches_label()` | Kiem gia tri co **dung hinh dang** cua truong hay khong |
| `parse_mrz(lines, printed_number)` | Doc MRZ; **so giuoc** cua so in la moc |
| `_mrz_check_digit_ok()` | Kiem check digit ICAO 9303 |

> MRZ chi la **fallback**: OCR cua MRZ lam lech cac con so (ngay sinh co check
> digit hop le), nen so in tren the luon duoc uu tien. `_mrz_number_candidates()`
> dung ICAO weights de xac nhan so khi khong co so in.

### API – dung ket qua cua engine

`SRC/UserManagement.Api/Ocr/OcrFieldExtractor.cs` – them overload:

```csharp
public static Dictionary<string, ExtractedValue> Extract(
    IReadOnlyDictionary<string, PaddleOcrField>? engineFields,
    IReadOnlyDictionary<string, string>? rawText)
```

- `engineFields` (tu engine, da dung toa do) **thang**;
- `rawText` (text scan) chi dung cho truong engine **khong tim thay**.

`OcrService.ScanAsync` va `PreviewAsync` doi sang goi overload nay.

### Test hoi quy moi

| File | Noi dung |
|---|---|
| `services/paddleocr/test_real_cccd.py` | 15 test: ghim parser vao **raw OCR that** cua anh CCCD that (co bounding box that) |
| `tests/UserManagement.Api.Tests/Ocr/OcrFieldExtractorTests.cs` | +5 test cho overload moi |

### Ket qua kiem chung voi anh CCCD THAT

| Tang | Ket qua |
|---|---|
| Microservice `:8100` | `documentNumber=048203001078`, `fullName=NGUYN MINH KHI`, `dateOfBirth=05/10/2003`, `gender=Nam`, `issueDate=22/04/2021`, `address=Xuân Hng, Xuân Trưng, Nam Đnh, M An, Ng Hành Son, Đà Nng` |
| API `:5051` | `documentNumber=048203001078`, `fullName=NGUYN MINH KHI`, `dateOfBirth=2003-10-05`, `gender=Male`, `issueDate=2021-04-22` |

Tong hop kiem thu tai thoi diem nay:

| Bo test | Ket qua |
|---|---|
| `services/paddleocr/test_main.py` | 10/10 pass |
| `services/paddleocr/test_real_cccd.py` | 15/15 pass |
| `tests/UserManagement.Api.Tests` | 225/225 pass |
| `tests/UserManagement.Web.Tests` | 108/108 pass |

### Dong bo `docs/OCR-Cloudinary-Spec.md` voi code thuc te

Spec cu viet "PaddleOCR chi tra ve text tho" (§3.3) va "chi doc thu tu dong OCR" —
sau Phase 8.11 ca hai deu **khong con dung**. Da cap nhat:

| Muc | Thay doi |
|---|---|
| §3.3 | Ghi ro ranh gioi moi: service Python tra ve `fields` (dinh vi **hinh hoc** bang bounding box), backend .NET chuan hoa cuoi cung + fallback `rawText` |
| §3.3.1 | Them quy tac gop `Quê quán` + `Nơi thường trú` -> `address` |
| §3.3.2 | Tach thanh **Lop 1 (Python, hinh hoc)** va **Lop 2 (.NET, chuan hoa)**; ghi them so khop nhan bo nguyen am + kiem hinh dang gia tri |
| §3.3.4 | Bo `grayscale/CLAHE/deskew` (chua bao gio duoc trien khai); them **phan loai huong tai lieu** (`use_doc_orientation_classify=True`) la buoc **bat buoc** |

> `deskew` va `CLAHE` da duoc ghi trong spec nhung khong ton tai trong code.
> Thay bang `use_doc_orientation_classify=True` — giai quyet truc tiep benh "anh chup nghinh".
> Bai toan deskew goc (nghieng) van duoc xu ly o tang orientation cua PaddleOCR.

### Loi moi phat hien khi kiem chung lai: `source` cua truong ngay sai mat

Kiem tra lai API bang **anh CCCD that** (khong phai anh mo phong) thi phat hien
`expiryDate` bi bao loi `source = "front"`, trong khi microservice bao dung
`source = "back"`.

**Nguyen nhan:** `OcrNormalizer.NormalizeDate()` **hard-code** `OcrFieldSource.Front`
cho moi truong ngay. Do do `expiryDate` — viet o **mat sau** the CCCD — cung bi gan
nham la mat truoc.

**Sua:** `NormalizeDate()` nhan them tham so `source` va dung chinh source cua gia tri
doc duoc. Cac truong ngay giu nguyen mat cua chung:

| Truong | Mat that su |
|---|---|
| `dateOfBirth` | truoc |
| `issueDate` | truoc |
| `expiryDate` | sau |

**Test hoi quy:** +2 test trong `tests/UserManagement.Api.Tests/Ocr/OcrNormalizerTests.cs`
(`Date_Keeps_The_Side_It_Was_Read_From`, `Date_That_Cannot_Be_Parsed_Keeps_Its_Side`).

### Loi moi phat hien khi kiem chung lai: ten truong OCR `issueDate` vs `issuedDate`

Kiem chung UI bang trinh duyet that (Playwright) cho thay **ong "Ngay cap" khong duoc
dien**, du API tra ve `issueDate = 2021-05-10` va do tin cay 0.9 (dat nguong).

**Nguyen nhan:** API + spec §3.3 dung ten truong OCR la **`issueDate`**, nhung lop Web
tim bang **`issuedDate`** (dung ten truong *form*, khong phai ten truong OCR):

| Noi | Sai | Dung |
|---|---|---|
| `wwwroot/js/quick-create-user.js` – `FIELD_MAP` | `issuedDate` | `issueDate` |
| `wwwroot/js/ocr.js` – `FIELD_MAP` | `issuedDate` | `issueDate` |
| `Views/Documents/Form.cshtml` – badge | `"issuedDate"` | `"issueDate"` |
| `ViewModels/Users/OcrScanViewModel.cs` – `OcrFieldNames` | `IssuedDate = "issuedDate"` | `IssueDate = "issueDate"` |

Hai he thong deu "dung" theo cach rieng: `issuedDate` la ten truong **form** khi POST
len API-07/13 (giữ nguyên), con `issueDate` la ten truong **OCR** trong response.

**Sua:** doi khoa `FIELD_MAP` va badge sang `issueDate`; them comment giai thich
ranh gioi de khong lan nhieu lai. `OcrFieldNames.IssuedDate` -> `OcrFieldNames.IssueDate`.

**Test hoi quy:** +1 test trong `tests/UserManagement.Web.Tests/BrowserQuickCreateTests.cs`
(`Ocr_Preview_Fills_The_Issued_Date`) — kiem `#IssuedDate` = `2021-05-10` va
`#ExpiryDate` = `2036-04-20` sau khi quet. Test nay **fail** truoc khi sua.

> `address` van trong trong ket qua: anh CCCD test (`services/paddleocr/front.jpg`) la
> anh mo phong khong co nhan "Dia chi", nen khong co gi de doc — khong phai loi.

### Loi lon nhat phat hien khi dung ANH CCCD THAT: sai mat cua hai ngay

Khi tim lai anh CCCD that (da gui trong chat truoc do) va chay OCR tren no, phat hien
**ca hai ngay deu sai**:

| Truong | Ket qua truoc | Ket qua sau |
|---|---|---|
| `expiryDate` | **thieu hoan toan** | `2028-10-05` (source `front`) |
| `issueDate` | `2021-04-22` (source `back`) | `2021-04-22` (source `back`) — giu nguyen |

**Nguyen nhan:** anh CCCD that in **ngay han** o **MAT TRUOC** (goc duoi trai:
`CÓ GIÁ TRỊ ĐẾN: 05/10/2028`) va **ngay cap** o **MAT SAU**
(`Ngày, tháng, năm / Date, month, year: 22/04/2021`).

Nhung spec §3.3 va code .NET ghi **ngược lai** (`issueDate` = truoc, `expiryDate` = sau),
va `FRONT_LABELS` trong Python **khong co** nhan "Có giá trị đến" nen `expiryDate`
khong bao gio duoc doc tren mat truoc.

**Da sua (theo phuong an chot: theo anh that, sua ca Python va .NET):**

| Tang | Thay doi |
|---|---|
| `services/paddleocr/app/main.py` | them nhan han (`co gia tri den`, `gia tri den`, `ngay het han`, `date of expiry`) vao `FRONT_LABELS`; giu `ngay thang nam` o `BACK_LABELS`; them `dateofexpiry` vao `_DATE_LABELS` |
| `SRC/UserManagement.Api/Ocr/OcrFieldExtractor.cs` | `FieldSources`: `issueDate` -> `Back`, `expiryDate` -> `Front`; them alias `ngay thang nam` cho `issueDate` va `date of expiry` cho `expiryDate` |
| `SRC/UserManagement.Api/Ocr/OcrNormalizer.cs` | moi truong nhan `source` tu `extractedValue.Source` thay vi hard-code `Front`/`Back` (ke ca `documentNumber`, `fullName`, `gender`, `address`, `authorityNo`) |
| `docs/OCR-Cloudinary-Spec.md` | §3.3.1 va cac vi du JSON cap nhat theo mat in thuc te |

> Service Python **do ca hai mat** cho nhan ngay, va `source` trong response la mat
> **thuc su doc duoc** gia tri — khong phai mat quy uoc.

**Test hoi quy:**
- `services/paddleocr/test_main.py`: +2 test (expiry doc o front, issue doc o back).
- `services/paddleocr/test_real_cccd.py`: +2 test va them `GEOMETRY_BACK` (vi tri OCR
  that cua mat sau).
- `tests/UserManagement.Api.Tests/Ocr/OcrFieldExtractorTests.cs`: +1 test
  (`Extract_Finds_The_Expiry_Date_On_The_Front_Side`).
- `tests/UserManagement.Api.Tests/Ocr/OcrNormalizerTests.cs`: +1 test
  (`Text_Fields_Keep_The_Side_They_Were_Read_From`).
- `tests/UserManagement.Web.Tests/BrowserLiveOcrTests.cs`: +1 test
  (`Live_Ocr_Reads_The_Real_Cccd_Photo_With_The_Dates_On_The_Right_Sides`) — chay
  that tren anh CCCD that, tu bo qua khi anh khong co tren may.

> Anh CCCD that khong commit vao repo (la giay to nhan cua nguoi thu nghiem), nen
> test live tu skip khi tim thay khong co file.

### Kiem chung lai toan he thong

**Voi anh CCCD THAT** (`services/paddleocr/real/front.jpg` + `back.jpg`):

| Tang | Ket qua |
|---|---|
| Microservice `:8100` | `documentNumber=048203001078`, `fullName=NGUYN MINH KHI`, `dateOfBirth=05/10/2003`, `gender=Nam`, `address=Xuân Hng, Xuân Trưng, Nam Đnh, M An, Ng Hành Son, Đà Nng`, `expiryDate=05/10/2028` (source `front`), `issueDate=22/04/2021` (source `back`) |
| API `:5051` | cung 7 truong, chuan ISO: `expiryDate=2028-10-05` (source `front`), `issueDate=2021-04-22` (source `back`) |
| Web UI `:5052` | Quick Create tu dong dien **7/7 truong**, badge xanh, **khong co 503** |

Tong hop kiem thu tai thoi diem nay:

| Bo test | Ket qua |
|---|---|
| `services/paddleocr/test_main.py` | 12/12 pass |
| `services/paddleocr/test_real_cccd.py` | 17/17 pass |
| `tests/UserManagement.Api.Tests` | 229/229 pass |
| `tests/UserManagement.Web.Tests` | 110/110 pass (+1 test hoi quy) |
| **Tong** | **468 test pass** |

### Xoa canh bao bao mat khi build: `System.Security.Cryptography.Pkcs` 9.0.4

`dotnet build` canh bao `NU1903` (muc **high**) va `NU1902` (moderate) cho
`System.Security.Cryptography.Pkcs` 9.0.4.

**Nguyen nhan:** `Microsoft.Data.SqlClient` 6.1.1 (keo ve boi `Azure.Identity` ->
`Microsoft.Identity.Client` -> `Microsoft.IdentityModel.Protocols.OpenIdConnect`)
keo `System.Security.Cryptography.Pkcs` 9.0.4 lam package **transitive**.

**Sua:** them `PackageReference` truc tiep trong `UserManagement.Api.csproj` de **ghim**
phien ban da va:

```xml
<PackageReference Include="System.Security.Cryptography.Pkcs" Version="9.0.23" />
```

Ket qua: `NU1902` + `NU1903` **bien mat**. Chi con canh bao license cua SixLabors
ImageSharp (can key khi build Release).

### Sua them canh bao `CS8620` trong `OcrService`

`DeleteAsync(new[] { frontPublicId, backPublicId }, ...)` — `backPublicId` co the
`null` (anh sau khong bat buoc), nen khong khop voi `IEnumerable<string>`.

**Sua:** loc bo cac id null truoc khi goi, va dung `Select(id => id!)` de compiler
suy ra danh tinh khong null:

```csharp
var publicIds = new[] { frontPublicId, backPublicId }
    .Where(id => !string.IsNullOrWhiteSpace(id))
    .Select(id => id!)
    .ToArray();
```

Build API sau khi sua: **0 error, 0 warning CS**.

### Loi `address already in use` khi chay `dotnet run`

Khong phai loi code — chi la **xung dot port**. Cac service chay nen tu phien truoc
da giu port `5051`/`5052`. Cach xu ly:

```powershell
Get-NetTCPConnection -State Listen -LocalPort 5051 | Select-Object OwningProcess
Get-Process UserManagement.Api,UserManagement.Web -ErrorAction SilentlyContinue | Stop-Process -Force
```

> `dotnet test` cung bianh loi khoa file (`MSB3026`) neu API/Web dang chay — phai
> dung chung truoc khi test.


