# User Management

Hệ thống quản lý User (CRUD + quản lý giấy tờ tùy thân) theo tài liệu đặc tả trong thư mục `docs/`.

## Cấu trúc solution

```
src/
  UserManagement.Api/     Backend REST API (.NET 10, EF Core 10, SQL Server)
  UserManagement.Web/     Frontend ASP.NET Core MVC (.NET 10, Tailwind CSS CLI)
services/
  paddleocr/              Microservice OCR Python (FastAPI + PaddleOCR 3.2.0)
tests/
  UserManagement.Api.Tests/    xUnit + WebApplicationFactory
  UserManagement.Web.Tests/    xUnit + WebApplicationFactory
docs/                      Tài liệu đặc tả (SRS, API, Database, UI, AI Coding Plan)
```

## Yêu cầu môi trường

- .NET SDK 10.0.401+
- Node.js 20+ (cho Tailwind CSS CLI)
- SQL Server 2022 (LocalDB hoặc full)
- dotnet-ef tool 10.0+ (`dotnet tool install -g dotnet-ef --version 10.0.0`)

## Cấu hình

Sao chép và chỉnh sửa các file `appsettings.json` nếu cần.

### `src/UserManagement.Api/appsettings.json`

| Khóa | Mô tả |
|---|---|
| `ConnectionStrings:Default` | Connection string SQL Server |
| `FileStorage:RootPath` | Thư mục lưu ảnh (ngoài wwwroot) |
| `FileStorage:MaxFileSizeBytes` | 5 MB |
| `FileStorage:MaxRequestBodyBytes` | ~11 MB |
| `Cloudinary:CloudName` | Cloud name của tài khoản Cloudinary |
| `Cloudinary:ApiKey` | API key (không nhạy cảm) |
| `Cloudinary:ApiSecret` | API secret — **KHÔNG** commit, dùng env var / user-secrets |
| `Cloudinary:Folder` | Folder gốc, mặc định `user-management` |
| `Cloudinary:UseSecureDelivery` | `true` → delivery type `authenticated` (BR-17) |
| `Cloudinary:AutoInvalidate` | `true` → xoá/xoay cũng invalidate CDN cache (BR-16) |
| `Ocr:Enabled` | `true` bật API-12/API-13; `false` → 503, UI giữ luồng nhập tay |
| `Ocr:BaseUrl` | Base URL PaddleOCR, mặc định `http://localhost:8100` |
| `Ocr:MinConfidence` | Ngưỡng confidence tối thiểu để auto-fill (BR-14) |
| `Ocr:HighConfidence` | Ngưỡng badge "confidence cao" |
| `Ocr:AllowTwoDigitYearInference` | Cho phép năm 2 chữ số (`20/04/95` → `1995-04-20`) |
| `Cors:AllowedOrigins` | Origin Frontend được phép |
| `App:TimeZoneId` | `Asia/Ho_Chi_Minh` |

### `src/UserManagement.Web/appsettings.json`

| Khóa | Mô tả |
|---|---|
| `Api:BaseUrl` | Base URL Backend API |
| `Api:TimeoutSeconds` | Timeout HTTP client |
| `Display:TimeZoneId` | `SE Asia Standard Time` (Windows) |

## Cách chạy Backend API

```bash
cd src/UserManagement.Api
dotnet run
```

API lắng nghe tại `https://localhost:5001`. Swagger UI: `https://localhost:5001/swagger`.

## Cách chạy Frontend

Trong lần đầu, build Tailwind CSS:

```bash
cd src/UserManagement.Web
npm install
npm run build:css
```

Sau đó chạy ứng dụng:

```bash
dotnet run
```

Web lắng nghe tại `https://localhost:5002`. Truy cập `/` sẽ tự redirect sang `/Users`.

## Cách build Tailwind CSS

```bash
# Build một lần (cho production)
npm run build:css

# Watch trong khi dev
npm run watch:css
```

## Cách chạy test

Tại thư mục gốc solution:

```bash
dotnet test
```

## Trạng thái triển khai

- [x] **Phase 0** – Khảo sát và lập kế hoạch
- [x] **Phase 1** – Khởi tạo solution
- [x] **Phase 2** – Database và Entity Framework Core
- [x] **Phase 3** – Backend API và nghiệp vụ User
- [x] **Phase 4** – Backend API quản lý giấy tờ và ảnh
- [x] **Phase 5** – Frontend MVC và API client
- [x] **Phase 6** – Triển khai các màn hình
- [x] **Phase 7** – Kiểm thử tích hợp End-to-End
- [x] **Phase 8** – Hoàn thiện và bàn giao

## Tổng kết

- **227/227 tests passed** (187 API + 40 Web = 88 unit/viewmodel + 78 integration/E2E + 1 live Cloudinary smoke test).
- `dotnet build -c Release`: 0 errors.
- E2E tests chạy qua toàn bộ stack thật: Web MVC + Backend API + SQL Server 2022 + Cloudinary (in-memory double) + PaddleOCR (fake client).
- Tài liệu chi tiết triển khai và quyết định kỹ thuật: xem `NOTES.md`.

## Tính năng mở rộng — OCR giấy tờ + Cloudinary (đã triển khai)

Bổ sung tự động quét OCR CCCD/CMND (PaddleOCR) và lưu ảnh trên Cloudinary.

| Tài liệu | Nội dung |
|---|---|
| [`docs/OCR-Cloudinary-Spec.md`](docs/OCR-Cloudinary-Spec.md) | Đặc tả đầy đủ: kiến trúc, hợp đồng PaddleOCR, Cloudinary signed upload, API-12/API-13, schema DB, UI auto-fill, rủi ro, 10 điểm cần chốt |

**Trạng thái:** đã triển khai đầy đủ (Phase 8.5).

### Kiến trúc

```
services/paddleocr/          Microservice FastAPI + PaddleOCR 3.2.0 (:8100)
                              - /ocr/identity-document  (multipart, 2 mặt CCCD)
                              - /health
                              - Tiền xử lý: CLAHE + deskew + denoise

src/UserManagement.Api/
  FileStorage/CloudinaryStorage.cs   Signed upload (SHA-1), move (image/rename), delete (image/destroy)
  Ocr/                                PaddleOcrClient, OcrFieldExtractor, OcrNormalizer, OcrCleanupService
  Services/OcrService.cs             API-12 (scan) + API-13 (confirm)
  Entities/OcrExtractionResult.cs    Audit trail mỗi lần quét
```

Ranh giới trách nhiệm: **Python chỉ đọc pixel**, mọi quy tắc nghiệp vụ (chuẩn hóa tên,
ngày sinh, số CCCD, giới tính) nằm trong .NET để test được và giữ được tính nhất quán.

### Luồng hoạt động

1. `POST /api/v1/users/{userId}/identity-documents/ocr/scan` (multipart) →
   upload 2 ảnh vào folder tạm `tmp/{scanId}`, gọi PaddleOCR, trả về
   các field đã chuẩn hóa kèm confidence + cảnh báo.
2. Người dùng review trên UI (panel auto-fill, badge confidence, cờ "cần kiểm tra").
3. `POST /api/v1/users/{userId}/identity-documents/ocr/confirm` (json) →
   chuyển ảnh tạm sang folder vĩnh viễn `identity-documents/{userId}/{documentId}`,
   tạo `IdentityDocument`, ghi lại `OcrExtractionResult`.

Ảnh tạm hết hạn được dọn tự động bởi `OcrCleanupService` (BackgroundService).

### Khởi động PaddleOCR

```bash
cd services/paddleocr
pip install -r requirements.txt
uvicorn app.main:app --host 0.0.0.0 --port 8100
```

Hoặc dùng Docker:

```bash
docker build -t user-management-paddleocr services/paddleocr
docker run -p 8100:8100 user-management-paddleocr
```

Khi `Ocr:Enabled = false`, hai endpoint OCR trả 503 và UI giữ nguyên luồng nhập tay.

### Đối chiếu với SRS gốc (PDF)

`docs/SRS_He_thong_Quan_ly_User_v2_0.pdf` là **bản rút gọn cho AI Agent** của SRS-UM-001 v2.0
và là đặc tả gốc có thẩm quyền cao nhất.

| Tài liệu | Nội dung |
|---|---|
| [`docs/SRS-Goc-Doi-Chieu.md`](docs/SRS-Goc-Doi-Chieu.md) | Đối chiếu từng điều khoản PDF với code — 7 khoảng trống phát hiện được, thứ tự ưu tiên, test cần bổ sung |
| [`docs/NOTES-SRS-Goc.md`](docs/NOTES-SRS-Goc.md) | Bản tóm tắt ngắn của đối chiếu trên |

**Kết luận đối chiếu:** 11/11 endpoint, BR-01…BR-12 và 6 màn hình đã đáp ứng đúng.
7 khoảng trống nhỏ đã được sửa xong:

| # | Khoảng trống | Trạng thái |
|---|---|---|
| G1 | §6.1 PDF yêu cầu **PHẢI** ghi log truy cập ảnh | ✅ `IdentityDocumentsController.LogImageAccess` — timestamp, IP, userId, documentId, side |
| G2 | §6.2 PDF yêu cầu `Cache-Control: private, no-cache` | ✅ API-10 trả `private, no-cache` + ETag + Last-Modified |
| G3 | §FR-02 PDF yêu cầu chuẩn hóa tiền tố `84` khi tìm kiếm | ✅ `UserService.PhoneSearchCandidates` — `+84…`/`84…` → `0…` |

> PDF §1.7 ghi rõ *"Ảnh lưu trên thư mục server qua IFileStorageService (dễ thay bằng cloud)"*
> → quyết định dùng Cloudinary **đúng hướng thiết kế gốc**.

### Cấu hình Cloudinary & PaddleOCR

**Quan trọng:** `cloud_name`, `api_key` và `api_secret` phải thuộc **cùng một product
environment** trên Cloudinary. Nếu sai cloud name, Cloudinary trả 401 kèm
`Invalid cloud_name` hoặc `cloud_name mismatch` — không phải lỗi chữ ký.

Đặt secret qua biến môi trường (không commit vào source):

```powershell
# Cloudinary
$env:Cloudinary__CloudName  = "your-cloud"
$env:Cloudinary__ApiKey     = "your-key"
$env:Cloudinary__ApiSecret  = "your-secret"

# PaddleOCR
$env:Ocr__BaseUrl = "http://localhost:8100"
$env:Ocr__Enabled = "true"
```

Hoặc dùng file local (được `.gitignore` bỏ qua):

`src/UserManagement.Api/appsettings.Development.local.json`

```json
{
  "Cloudinary": {
    "CloudName": "your-cloud",
    "ApiKey": "your-key",
    "ApiSecret": "your-secret"
  }
}
```

Lấy 3 giá trị này ở **Cloudinary Console → Settings → API Keys**.


Hoặc dùng User Secrets khi phát triển:

```powershell
dotnet user-secrets set "Cloudinary:ApiSecret" "<secret>" --project src/UserManagement.Api
```

Xem `.env.example` để có mẫu đầy đủ. **Không** đặt `ApiSecret` vào `appsettings.json` đã commit.

## Lưu ý bảo mật

Phiên bản demo chưa có xác thực/phân quyền. Chỉ chạy trong môi trường nội bộ/VPN được kiểm soát. Không công khai ứng dụng ra Internet.

Xem chi tiết trong `docs/SRS.md` §1.3 và `docs/AI-Coding-Plan.md` §2.2.

Ảnh giấy tờ tùy thân là **PII nhạy cảm**. Khi dùng Cloudinary, bắt buộc dùng
`authenticated` delivery và không lộ `public_id` ra giao diện.

## Sinh lại SRS PDF (v2.1)

Tài liệu SRS nằm trong `docs/SRS_He_thong_Quan_ly_User_v2_0.pdf`. PDF được sinh tự động
từ mã nguồn trong `tools/`, nên khi sửa nội dung chỉ cần sửa HTML trong
`tools/sec9_*.py` rồi chạy lại lệnh build:

```powershell
python tools/build_srs_pdf.py
```

Pipeline gồm 4 bước:

| Bước | Script | Việc |
|---|---|---|
| 1 | `tools/subset_fonts.py` | Subset font (Arial, Calibri, Consolas) để file PDF không phình to |
| 2 | `tools/apply_markers.py` | Đổi header/footer sang v2.1 và gắn nhãn `[v2.1]` vào 12 điều khoản bị sửa |
| 3 | `tools/build_srs_pdf.py` | Render mục 9 (OCR + Cloudinary) bằng PyMuPDF Story rồi nối vào cuối |
| 4 | `tools/verify_pdf.py` | Kiểm tra footer, TOC, marker và dấu tiếng Việt |

Bản gốc v2.0 được giữ lại ở `docs/SRS_He_thong_Quan_ly_User_v2_0-v2.0-ORIGINAL.pdf`
(được `.gitignore` bỏ qua) để có thể rollback.

Lưu ý kỹ thuật: PyMuPDF biến ligature `ti` của Calibri thành `Ɵ` (U+019F) khi shaping,
làm hỏng trích xuất text. Vì vậy phần thân mới dùng Arial (font này PDF gốc đã nhúng
sẵn), còn header chỉ dùng Calibri đã subset.
#   t h u c t a p  
 