# OCR & Cloudinary Specification – User Management

- **Mã tài liệu:** SPEC-OCR-CLD-001
- **Phiên bản:** v1.0
- **Trạng thái:** Đề xuất — chờ phê duyệt trước khi triển khai code
- **Bổ sung cho:** `SRS.md`, `API-Spec.md`, `Database-Design.md`, `UI-Spec.md`

---

## 0. Tóm tắt

Bổ sung 2 năng lực mới vào hệ thống User Management:

1. **Tự động quét OCR giấy tờ tùy thân.**
   Người dùng chụp/chọn ảnh mặt trước và mặt sau của CCCD → hệ thống tự động đọc
   số giấy tờ, họ tên, ngày sinh, giới tính, địa chỉ, ngày cấp, ngày hết hạn →
   điền sẵn vào form → người dùng kiểm tra/chỉnh sửa → bấm **Lưu**.

2. **Lưu trữ ảnh trên Cloudinary.**
   Thay thế hoàn toàn lưu trữ ảnh trên ổ đĩa cục bộ. Ảnh được upload lên
   Cloudinary bằng **signed upload**, database chỉ lưu `public_id` và metadata.

### 0.1. Quyết định đã chốt

| Hạng mục | Quyết định |
|---|---|
| OCR engine | **PaddleOCR** — chạy dưới dạng service Python riêng, giao tiếp REST |
| Lưu trữ ảnh | **Cloudinary — thay thế hoàn toàn** FileStorage local (không giữ fallback) |
| Trải nghiệm | **Auto-fill** — quét xong điền sẵn form, user review rồi mới Lưu |
| Phạm vi lần này | **Chỉ tài liệu.** Chưa viết code cho tới khi đặc tả được duyệt |

---

## 1. Bối cảnh và phạm vi

### 1.1. Vấn đề cần giải quyết

Hiện tại (SRS v2.0 §1.2) **OCR giấy tờ nằm ngoài phạm vi**, và người dùng phải nhập
tay toàn bộ thông tin giấy tờ. Với khối lượng hồ sơ lớn, đây là nút thắt về UX và
chất lượng dữ liệu (gõ sai số giấy tờ, sai ngày sinh).

Đồng thời, `FileStorage` local hiện tại (`LocalFileStorage`) đòi hỏi:

- Quyền ghi vào ổ đĩa của server → khó triển khai trên cloud/container.
- Sao lưu thủ công phải đồng bộ database + file storage (SRS §6).
- Không scale ngang được khi chạy nhiều instance.

### 1.2. Trong phạm vi

- Quét OCR ảnh mặt trước + mặt sau CCCD/CMND.
- Lưu trữ ảnh trên Cloudinary thay cho ổ đĩa cục bộ.
- Auto-fill form giấy tờ từ kết quả OCR.
- Ghi log kết quả OCR và lịch sử quét.

### 1.3. Ngoài phạm vi

- OCR Passport (chữ khắc, không có MRZ trên ảnh thường) — giữ nhập tay.
- OCR mặt sau CMND cũ (chất lượng scan kém) — hỗ trợ nhưng độ tin cậy thấp.
- Tự động tạo User từ OCR — vẫn phải tạo User trước.
- Tự động lưu không cần người dùng xác nhận.
- Xác thực/đăng nhập/phân quyền (vẫn ngoài phạm vi như SRS v2.0).
- Đa ngôn ngữ UI.
- Xoá vòng xoáy dữ liệu, giữ lại lịch sử quét.

### 1.4. Ảnh hưởng đến tài liệu gốc

| Tài liệu | Thay đổi |
|---|---|
| `SRS.md` | Thêm FR-13, FR-14, BR-13…BR-16; cập nhật §1.2 (bỏ "OCR giấy tờ" khỏi ngoài phạm vi); §7 thêm cấu hình |
| `API-Spec.md` | Thêm API-12, API-13; sửa API-07/08/09/10 cho Cloudinary |
| `Database-Design.md` | Sửa bảng `IdentityDocuments`; thêm bảng `OcrExtractionResults`; §6 viết lại |
| `UI-Spec.md` | Thêm SCR-07; nâng cấp SCR-06 §8 |
| `NOTES.md` | Ghi nhận điểm chưa rõ và quyết định |

---

## 2. Kiến trúc đích

```text
┌──────────┐
│ Browser  │  chọn/chụp 2 mặt
└────┬─────┘
     │ HTTPS
     ▼
┌──────────────────────┐
│  UserManagement.Web  │  (MVC + Tailwind)
│  - Upload ảnh tạm    │
│  - Gọi API scan      │
│  - Auto-fill form    │
└────┬─────────────────┘
     │ HTTP/JSON, multipart/form-data
     ▼
┌───────────────────────────────────────────────┐
│              UserManagement.Api               │
│                                               │
│  ┌─────────────────┐   ┌──────────────────┐  │
│  │ OcrOrchestrator │──▶│ PaddleOcrClient  │──┼──▶ PaddleOCR service (Python)
│  └────────┬────────┘   └──────────────────┘  │    :8100
│           │                                   │
│           │        ┌──────────────────┐       │
│           └───────▶│ CloudinaryStorage │──────┼──▶ Cloudinary API
│                    └──────────────────┘       │
│                     ┌──────────────────┐       │
│                     │  SQL Server      │──────┼──▶ IdentityDocuments
│                     └──────────────────┘       │
└───────────────────────────────────────────────┘
```

### 2.1. Ranh giới trách nhiệm

| Thành phần | Trách nhiệm |
|---|---|
| `UserManagement.Web` | Thu thập file, gọi API, hiển thị kết quả OCR, prefill form |
| `UserManagement.Api` | Validate file, orchestrate OCR + Cloudinary, quản lý transaction, ghi DB |
| `PaddleOCR` | Chỉ đọc text từ ảnh, trả về raw text + bounding box. **Không** hiểu nghiệp vụ CCCD |
| `Cloudinary` | Lưu, biến đổi và phân phối ảnh |
| `SQL Server` | Metadata, `public_id`, kết quả OCR đã chuẩn hóa |

> **Nguyên tắc:** Backend là nguồn xác thực cuối cùng. Mọi giá trị OCR đều là
> **dữ liệu gợi ý**, không bao giờ được tin cậy tuyệt đối. Giá trị OCR phải đi qua
> đúng bộ validator hiện có (BR-01…BR-12) trước khi lưu.

---

## 3. Đặc tả dịch vụ PaddleOCR

### 3.1. Vì sao tách thành service riêng

PaddleOCR là thư viện Python. Nhúng nó vào .NET không khả thi. Triển khai dưới
dạng container Python độc lập giúp:

- .NET stack không bị ràng buộc bởi native dependency.
- Có thể scale/ghi đè engine OCR độc lập.
- Nhiều instance API không cần load model nặng vào RAM.

### 3.2. Hợp đồng API

**Base URL:** lấy từ cấu hình `Ocr:BaseUrl`.

#### 3.2.1. `POST /ocr/identity-document`

`Content-Type: multipart/form-data`

| Field | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `front` | file | Có | Ảnh mặt trước / trang thông tin |
| `back` | file | Không | Ảnh mặt sau. Bỏ trống với Passport |
| `documentType` | string | Có | `CCCD`, `CMND`, `Passport` |

Response `200`:

```json
{
  "success": true,
  "engine": "paddleocr-3.2.0",
  "documentType": "CCCD",
  "fields": {
    "documentNumber": {
      "value": "079095001234",
      "confidence": 0.98,
      "source": "front",
      "boundingBox": [120, 340, 620, 395]
    },
    "fullName": {
      "value": "NGUYỄN VĂN A",
      "confidence": 0.95,
      "source": "front",
      "boundingBox": [120, 180, 700, 235]
    },
    "dateOfBirth": {
      "value": "1995-04-20",
      "confidence": 0.93,
      "source": "front",
      "boundingBox": [120, 280, 420, 325]
    },
    "gender": {
      "value": "male",
      "confidence": 0.9,
      "source": "front",
      "boundingBox": [460, 280, 560, 325]
    },
    "address": {
      "value": "12 LÊ LỢI, P. BẾN THÀNH, Q.1, TP.HCM",
      "confidence": 0.78,
      "source": "front",
      "boundingBox": [120, 430, 900, 520]
    },
    "issueDate": {
      "value": "2021-05-10",
      "confidence": 0.93,
      "source": "front",
      "boundingBox": [700, 280, 900, 325]
    },
    "expiryDate": {
      "value": "2036-04-20",
      "confidence": 0.96,
      "source": "back",
      "boundingBox": [700, 280, 900, 325]
    },
    "authorityNo": {
      "value": "012345678",
      "confidence": 0.71,
      "source": "back",
      "boundingBox": [120, 430, 460, 470]
    }
  },
  "rawText": {
    "front": "SỐ: 079095001234\nHỌ VÀ TÊN: NGUYỄN VĂN A\n...",
    "back": "CÓ GIÁ TRỊ ĐẾN NGÀY: 20/04/2036\n..."
  },
  "processingTimeMs": 1247,
  "warnings": [
    "Không đọc được 'Địa chỉ' rõ ràng, độ tin cậy thấp."
  ]
}
```

Response `422` (không đọc được gì):

```json
{
  "success": false,
  "engine": "paddleocr-3.2.0",
  "fields": {},
  "rawText": { "front": "", "back": "" },
  "processingTimeMs": 880,
  "error": "Không nhận dạng được vùng chữ trên ảnh mặt trước. Vui lòng chụp lại ảnh rõ nét hơn."
}
```

#### 3.2.2. `GET /health`

```json
{ "status": "ok", "engine": "paddleocr-3.2.0", "modelsLoaded": true }
```

### 3.3. Quy tắc chuẩn hóa kết quả OCR

Đây là phần **quan trọng nhất** — PaddleOCR chỉ trả về text thô. Việc hiểu
"12 chữ số này là số CCCD" phải nằm ở **Backend .NET**, không nằm trong service Python.

#### 3.3.1. Bảng ánh xạ nhãn → trường nghiệp vụ

| Nhãn trên giấy tờ | Trường nghiệp vụ | Mặt | Kiểu chuẩn hóa |
|---|---|---|---|
| `Số` / `Số CCCD` | `documentNumber` | trước | BR-08: CCCD 12 số, CMND 9/12 số |
| `Họ và tên` | `fullName` | trước | Bỏ dấu phân cách thừa, giữ dấu tiếng Việt |
| `Ngày sinh` | `dateOfBirth` | trước | `dd/MM/yyyy` → ISO `yyyy-MM-dd` |
| `Giới tính` | `gender` | trước | `Nam`→`Male`, `Nữ`→`Female`, `Khác`→`Other` |
| `Địa chỉ` | `address` | trước | Trim, bỏ xuống dòng, ≤ 500 ký tự |
| `Ngày cấp` | `issueDate` | trước | `dd/MM/yyyy` → ISO |
| `Có giá trị đến ngày` | `expiryDate` | sau | `dd/MM/yyyy` → ISO |
| `Số hiệu` | `authorityNo` | sau | Chuỗi số, không dùng để định danh |

#### 3.3.2. Quy tắc trích xuất

- Tách `rawText` thành các dòng, bỏ dòng rỗng.
- Với mỗi trường, tìm dòng chứa nhãn tương ứng (không phân biệt hoa thường,
  bỏ dấu, chuẩn hóa khoảng trắng).
- Lấy phần còn lại của dòng làm giá trị.
- Nếu nhãn không nằm cùng dòng, thử dòng kế tiếp (layout nhiều cột).
- Với `documentNumber`: trích toàn bộ chữ số trong vùng, giữ đúng 12 (CCCD).
- Với ngày tháng: hỗ trợ `dd/MM/yyyy`, `dd-MM-yyyy`, `dd.MM.yyyy`.

#### 3.3.3. Quy tắc ngày tháng trên CCCD mới

CCCD mới (từ 2023) ghi ngày sinh dạng `dd/MM/yyyy`. Với người sinh trước năm 2000,
ngày sinh chỉ có **2 chữ số năm** → mặc định `2000 + yy` nếu kết quả > hiện tại.

> **Điểm chưa rõ (xem `NOTES.md`):** cần xác nhận với nghiệp vụ có chấp nhận quy tắc
> suy luận năm 2 chữ số hay không.

#### 3.3.4. Tiền xử lý ảnh trước khi OCR

Service Python thực hiện (không ảnh hưởng ảnh gốc lưu trên Cloudinary):

1. Grayscale + tăng tương phản (CLAHE).
2. Chỉnh deskew (xoay nghiêng) nếu góc lệch > 1°.
3. Resize về chiều cao 1600 px (giữ tỷ lệ).
4. Lọc nhiễu nhẹ.

Ảnh gốc **không** bị thay đổi — Cloudinary vẫn lưu ảnh nguyên bản.

### 3.4. Ngưỡng tin cậy

| `confidence` | Hành vi UI |
|---|---|
| ≥ 0.85 | Field được điền, badge xanh "tin cậy cao" |
| 0.60 – 0.85 | Field được điền, badge vàng "cần kiểm tra" |
| < 0.60 | Field **không** tự điền, hiển thị gợi ý mờ để user tự nhập |

Cấu hình ngưỡng tối thiểu: `Ocr:MinConfidence` (mặc định `0.60`).

---

## 4. Đặc tả lưu trữ Cloudinary

### 4.1. Cấu hình

```json
{
  "Cloudinary": {
    "CloudName": "",
    "ApiKey": "",
    "ApiSecret": "",
    "Folder": "user-management",
    "UseSecureDelivery": true,
    "MaxFileSizeBytes": 5242880,
    "AllowedFormats": ["jpg", "png"],
    "AutoInvalidate": false
  }
}
```

| Khóa | Ý nghĩa | Bắt buộc |
|---|---|---|
| `CloudName` | Tên cloud trên Cloudinary | Có |
| `ApiKey` | API key | Có |
| `ApiSecret` | API secret — **không bao giờ commit vào source** | Có |
| `Folder` | Folder gốc trong Cloudinary | Có |
| `UseSecureDelivery` | Dùng `image/authenticated` delivery type (asset lưu với `type=authenticated`) | Khuyến nghị `true` |
| `MaxFileSizeBytes` | Giới hạn kích thước ảnh (khớp BR-09) | Có |
| `AllowedFormats` | Chỉ cho phép `jpg`, `png` | Có |
| `AutoInvalidate` | Tự invalidate CDN cache khi ảnh thay thế | Khuyến nghị `true` |

> **Bảo mật:** `ApiSecret` phải nằm trong User Secrets, biến môi trường
> (`CLOUDINARY__ApiSecret`) hoặc Key Vault — **không** đặt trong `appsettings.json`
> đã commit. Xem `.gitignore` và `NOTES.md` §Bảo mật.

> **Quan trọng — `type=authenticated` (xác minh trên cloud `smbl2hal`):** muốn ảnh
> không đọc được công khai thì **phải truyền `type=authenticated` khi upload**.
> `access_mode=authenticated` bị API **bỏ qua** (response vẫn trả
> `access_mode: public`) và ảnh vẫn đọc được. Ngoài ra, `rename` và `destroy`
> cũng **phải** kèm `type=authenticated`, nếu không Cloudinary sẽ tìm asset có
> type `upload` và trả về `not found` — ảnh sẽ **không** bị xoá.

### 4.2. Signed upload

Bắt buộc dùng **signed upload** (tính chữ ký SHA-1 server-side), **không** dùng
unsigned preset. Cách này đảm bảo chỉ Backend mới upload được.

```text
timestamp   = 1758604800
signature  = SHA1("folder=<folder>&timestamp=<ts><api_secret>")
```

Header form-data gửi lên `https://api.cloudinary.com/v1_1/<cloud_name>/image/upload`:

| Field | Giá trị |
|---|---|
| `file` | Binary ảnh |
| `api_key` | Từ cấu hình |
| `timestamp` | Unix timestamp hiện tại |
| `signature` | SHA1 như trên |
| `folder` | `user-management/tmp/{scanId}` hoặc `user-management/identity-documents/{userId}` |
| `allowed_formats` | `jpg,png` |
| `max_file_size` | `5242880` |
| `use_filename` | `false` |
| `unique_filename` | `true` |

### 4.3. Cấu trúc folder trên Cloudinary

```text
user-management/
  tmp/
    {scanId}/
      front-{hash}.jpg
      back-{hash}.jpg
  identity-documents/
    {userId}/
      {documentId}/
        front-{hash}.jpg
        back-{hash}.jpg
```

- `tmp/{scanId}` — ảnh chờ xác nhận, dọn tự động sau 24h (Cloudinary lifecycle rule).
- `identity-documents/{userId}/{documentId}` — ảnh chính thức gắn với giấy tờ.

### 4.4. URL delivery

| Chế độ | URL pattern |
|---|---|
| Public (không khuyến nghị) | `https://res.cloudinary.com/{cloud}/image/upload/{publicId}` |
| Authenticated (**bắt buộc có chữ ký**) | `https://res.cloudinary.com/{cloud}/image/authenticated/s--{signature}--/{publicId}` |

Với `UseSecureDelivery = true`, dùng `image/authenticated`. Khi đó URL **phải kèm
chữ ký**, nếu không Cloudinary trả **401**. URL không có TTL — chữ ký ở đây là
chữ ký *tính từ public id*, không phải token hết hạn; nếu cần hết hạn thì dùng
Cloudinary **access token** (`__cld_token__`).

> **Xác minh thực tế trên cloud `smbl2hal`:**
>
> | URL | Kết quả |
> |---|---|
> | `image/authenticated/{publicId}` (không chữ ký) | **401** |
> | `image/authenticated/s--XXXXXXXX--/{publicId}` (sai chữ ký) | **401** |
> | `image/authenticated/s--{sig}--/{publicId}` (đúng chữ ký) | **200** |
> | `image/upload/{publicId}` với asset `type=authenticated` | **404** (không lộ) |
>
> Công thức chữ ký URL (khác hẳn chữ ký API):
>
> ```text
> signature = base64url(SHA1(publicId + api_secret))[0..8]
> ```
>
> `publicId` là public id **không kèm đuôi file** (Cloudinary lưu format riêng).
> Nếu public id đã có sẵn `.png`, URL cần đuôi file **gấp đôi**
> (`{publicId}.png` → `{publicId}.png.png`) — vì vậy code upload **không** ghi đuôi
> file vào public id.

### 4.5. Biến đổi ảnh (transformation)

Đặt transformation mặc định cho mọi ảnh:

```text
f_auto,q_auto:best,c_limit,w_2000,h_2000,dpr_auto
```

| Biến đổi | Mục đích |
|---|---|
| `f_auto` | Tự chọn định dạng (WebP/AVIF) theo trình duyệt |
| `q_auto:best` | Tự tối ưu chất lượng |
| `c_limit,w_2000,h_2000` | Giảm kích thước tối đa, không crop |
| `dpr_auto` | Theo mật độ điểm ảnh thiết bị |

Thêm `strip_metadata` để loại bỏ EXIF/GPS khỏi ảnh public.

> **Quyết định:** ảnh **gốc** (không transform) vẫn được lưu để có thể xử lý lại sau.
> Các bản biến đổi được sinh lazily bởi Cloudinary CDN.

### 4.6. Xóa ảnh

Khi xóa giấy tờ hoặc User:

1. Xóa bản ghi database (theo quy tắc hiện tại: DB trước, tài nguyên sau).
2. Gọi Cloudinary `destroy` cho từng `public_id`.
3. **Lỗi xóa Cloudinary chỉ ghi log**, không rollback database (BR-05, API-Spec §4).

Dùng API `delete_resources` với `invalidate: true` để xóa cả CDN cache:

```json
{
  "public_ids": ["user-management/identity-documents/{userId}/{documentId}/front-abc.jpg"],
  "invalidate": true
}
```

### 4.7. Thay thế FileStorage local

| Khía cạnh | Trước | Sau |
|---|---|---|
| Cột DB | `FrontImagePath` (relative path) | `FrontImagePublicId` (string) |
| Ghi ảnh | `IFileStorage.SaveAsync` → ổ đĩa | `ICloudinaryStorage.UploadAsync` → Cloudinary |
| Đọc ảnh | Mở `FileStream` | Trả URL có chữ ký, hoặc redirect 302 |
| Xóa | `File.Delete` | `delete_resources` |
| Validation | ImageSharp probe magic bytes | Giữ nguyên ImageSharp probe + thêm check Cloudinary |
| ETag | SHA-1 của size+ticks+path | SHA-1 của `public_id` + `version` |
| Backup | DB + thư mục file đồng bộ | Chỉ DB (ảnh đã ở dịch vụ cloud) |

---

## 5. Đặc tả API mới

Base URL: `https://{host}/api/v1`

### API-12 – Quét OCR giấy tờ

`POST /users/{userId}/identity-documents/ocr/scan`

`Content-Type: multipart/form-data`

| Field | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `documentType` | string | Có | `CCCD`, `CMND`, `Passport` |
| `frontImage` | file | Có | Ảnh mặt trước |
| `backImage` | file | Với CCCD/CMND | Ảnh mặt sau |

**Luồng xử lý (Server):**

```text
1.  Kiểm tra User tồn tại và chưa bị xóa            → 404 nếu không
2.  Validate multipart + kích thước tổng request      → 413 nếu vượt
3.  Validate từng ảnh (magic bytes, kích thước, pixel) → 415 / 400
4.  Upload ảnh vào Cloudinary folder tạm              → trả frontId/backId
5.  Gọi PaddleOCR service (timeout 30s)               → 503 nếu service lỗi
6.  Chuẩn hóa + validate kết quả OCR                  → bỏ field không hợp lệ
7.  Lưu bản ghi OcrExtractionResult (status=Completed) → ghi log
8.  Trả 200 với fields + publicId tạm
```

**Response `200`:**

```json
{
  "scanId": "9a1c4e7a-52b3-4f0e-8d36-6a0b1c2d3e4f",
  "documentType": "CCCD",
  "status": "Completed",
  "frontImagePublicId": "user-management/tmp/9a1c4e7a.../front-abc123.jpg",
  "backImagePublicId": "user-management/tmp/9a1c4e7a.../back-def456.jpg",
  "fields": {
    "documentNumber": { "value": "079095001234", "confidence": 0.98, "source": "front", "isValid": true },
    "fullName":     { "value": "Nguyễn Văn A", "confidence": 0.95, "source": "front", "isValid": true },
    "dateOfBirth":  { "value": "1995-04-20",  "confidence": 0.93, "source": "front", "isValid": true },
    "gender":       { "value": "Male",        "confidence": 0.9,  "source": "front", "isValid": true },
    "address":      { "value": "12 Lê Lợi…", "confidence": 0.78, "source": "front", "isValid": true },
    "issueDate":    { "value": "2021-05-10",  "confidence": 0.93, "source": "front", "isValid": true },
    "expiryDate":   { "value": "2036-04-20",  "confidence": 0.96, "source": "back",  "isValid": true },
    "authorityNo":  { "value": null,          "confidence": 0.71, "source": "back",  "isValid": false }
  },
  "warnings": ["Không đọc rõ 'Địa chỉ' — độ tin cậy 78%, vui lòng kiểm tra."],
  "processingTimeMs": 1247
}
```

**Response `400`** — OCR không đọc được gì đủ để hữu ích:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Không đọc được thông tin giấy tờ.",
  "status": 400,
  "detail": "Không nhận dạng được vùng chữ trên ảnh mặt trước. Vui lòng chụp lại ảnh rõ nét, đủ sáng và vuông góc.",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00"
}
```

**Status:** `200`, `400`, `404`, `413`, `415`, `503`.

> `503` khi PaddleOCR service không sẵn sàng — người dùng vẫn có thể nhập tay và
> lưu bình thường. OCR là **tùy chọn**, không phải điều kiện để lưu giấy tờ.

### API-12b – Xem trước OCR (không cần User tồn tại)

`POST /identity-documents/ocr/preview`

> **Vì sao cần endpoint này?** API-12 gắn `UserId` bắt buộc (FK tới `Users`) vì
> `OcrExtractionResult.UserId` và `IdentityDocument.UserId` đều là khóa ngoại bắt
> buộc. Nhưng luồng **Thêm User nhanh (Quick Create)** cần điền sẵn form *trước khi*
> User tồn tại. API-12b là bản rút gọn, **không phụ thuộc User**: không tạo bản ghi
> scan, không lưu ảnh tạm lên Cloudinary, chỉ trả về các field đã nhận diện để
> form pre-fill.

`Content-Type: multipart/form-data`

| Field | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `documentType` | string | Có | `CCCD`, `CMND`, `Passport` |
| `frontImage` | file | Có | Ảnh mặt trước |
| `backImage` | file | Với CCCD/CMND | Ảnh mặt sau |

**Luồng xử lý (Server):**

```text
1.  Validate multipart + kích thước tổng request      → 413 nếu vượt
2.  Validate từng ảnh (magic bytes, kích thước, pixel) → 415 / 400
3.  Gọi PaddleOCR service trực tiếp (timeout 30s)      → 503 nếu service lỗi
4.  Chuẩn hóa + validate kết quả OCR                   → bỏ field không hợp lệ
5.  KHÔNG upload Cloudinary, KHÔNG ghi OcrExtractionResult
6.  Trả 200 với fields + warnings
```

**Response `200`:**

```json
{
  "documentType": "CCCD",
  "fields": {
    "documentNumber": { "value": "079095001234", "confidence": 0.98, "source": "front", "isValid": true },
    "fullName":     { "value": "Nguyễn Văn A", "confidence": 0.95, "source": "front", "isValid": true },
    "dateOfBirth":  { "value": "1995-04-20",  "confidence": 0.93, "source": "front", "isValid": true }
  },
  "warnings": ["Không đọc rõ 'Địa chỉ' — độ tin cậy 78%, vui lòng kiểm tra."],
  "processingTimeMs": 1247
}
```

**Status:** `200`, `400`, `413`, `415`, `503`.

> Không có `404` (không cần User) và không có `409`. Vì không ghi ảnh xuống
> Cloudinary nên **không sinh rác** và không cần job dọn dẹp cho endpoint này.

### API-13 – Xác nhận giấy tờ đã quét

`POST /users/{userId}/identity-documents/ocr/confirm`

`Content-Type: application/json`

```json
{
  "scanId": "9a1c4e7a-52b3-4f0e-8d36-6a0b1c2d3e4f",
  "documentType": "CCCD",
  "documentNumber": "079095001234",
  "issuedDate": "2021-05-10",
  "expiryDate": "2036-04-20"
}
```

**Luồng xử lý:**

```text
1.  Kiểm tra scanId tồn tại và thuộc User
2.  Lấy publicId ảnh tạm từ BẢN GHI OcrExtractionResult (KHÔNG lấy từ client)
3.  Kiểm tra publicId nằm trong folder tmp/{scanId} của đúng scan này
4.  Kiểm tra ảnh tạm vẫn tồn tại trên Cloudinary
5.  Kiểm tra quy tắc ảnh theo loại giấy tờ (CCCD/CMND cần mặt sau, Passport không được có)
6.  Validate metadata theo BR-08, BR-10
7.  Kiểm tra trùng (loại giấy tờ, số giấy tờ)
8.  Tạo documentId
9.  Move ảnh tạm → folder identity-documents/{userId}/{documentId}
10. Ghi IdentityDocument + đánh dấu scan = Confirmed
11. Nếu ghi DB lỗi → xóa ảnh vừa move (best-effort) rồi trả lỗi
```

**Response `201`:** representation giấy tờ — giống hệt API-07.

**Status:** `201`, `400`, `404`, `409`, `413`, `415`.

#### Ràng buộc bảo mật của API-13

| Ràng buộc | Cách thực hiện |
|---|---|
| Client KHÔNG được chỉ định ảnh nào được dùng | `OcrConfirmRequest` không có `frontImagePublicId` / `backImagePublicId`. Public id lấy từ bản ghi `OcrExtractionResult` trong DB. |
| Ảnh phải thuộc đúng scan | `EnsureBelongsToScan` kiểm tra segment `tmp` đứng ngay trước `{scanId}` (chấp nhận cả định dạng `D` và `N`). |
| Ảnh phải còn tồn tại | `AssetExistsAsync` gọi Cloudinary trước khi move. |
| Quy tắc ảnh theo loại giấy tờ | CCCD/CMND bắt buộc có mặt sau; Passport không được có mặt sau. |
| Số giấy tờ trùng toàn hệ thống | BR-11: `(DocumentType, DocumentNumber)` unique toàn hệ thống, không phải theo user. |

### 5.1. Thay đổi cho API-07, API-08, API-09, API-10

| API | Thay đổi |
|---|---|
| **API-07** (POST) | Không đổi contract. Nội bộ dùng `ICloudinaryStorage` thay `IFileStorage`. Response `frontImageUrl`/`backImageUrl` giờ trỏ tới Cloudinary URL có chữ ký. |
| **API-08** (PUT) | Không đổi contract. Khi thay ảnh: upload ảnh mới → cập nhật DB → xóa ảnh cũ trên Cloudinary. `AutoInvalidate` bảo đảm CDN không còn serve ảnh cũ. |
| **API-09** (DELETE) | Không đổi contract. Xóa DB trước, gọi `delete_resources` sau, lỗi chỉ ghi log. |
| **API-10** (GET ảnh) | **Thay đổi lớn.** Xem §5.2. |

### 5.2. API-10 — Lấy ảnh giấy tờ (phiên bản Cloudinary)

`GET /users/{userId}/identity-documents/{documentId}/images/{side}`

**Phương án A — Redirect (khuyến nghị):**

```http
HTTP/1.1 307 Temporary Redirect
Location: https://res.cloudinary.com/demo/image/authenticated/s--sig--/user-management/identity-documents/.../front-abc
Cache-Control: private, max-age=300
```

- Ảnh tải thẳng từ CDN Cloudinary → giảm tải cho API.
- Cache-control ở tầng browser/proxy.
- Chữ ký trong URL là `base64url(SHA1(publicId + api_secret))[0..8]` (§4.4).

**Phương án B — Stream proxy (giữ nguyên hành vi hiện tại):**

```http
HTTP/1.1 200 OK
Content-Type: image/jpeg
ETag: W/"cloudinary-abc123-v1758604800"
Cache-Control: private, no-cache
```

- API tải ảnh từ Cloudinary rồi stream ra.
- Giữ nguyên hành vi ETag/304 hiện tại → **ít rủi ro hồi quy**.

> **Quyết định đề xuất:** triển khai **Phương án B (stream proxy)** trước để không
> phá vỡ `UsersApiClient` và ETag logic đã có. Chuyển sang phương án A ở phase sau
> sau khi đo lưu lượng. Xem `NOTES.md`.

**Status:** `200`, `304`, `404`.

**Không thay đổi:**
- Không trả đường dẫn file vật lý.
- Không cho truy cập ảnh tùy ý qua path do client cung cấp.
- Ảnh bị xóa → URL trả 404 ngay (kiểm tra `public_id` còn tồn tại trong DB).

### 5.3. Cấu hình mới

```json
{
  "Ocr": {
    "Enabled": true,
    "BaseUrl": "http://localhost:8100",
    "TimeoutSeconds": 30,
    "MinConfidence": 0.6,
    "MaxScanImageBytes": 10485760
  },
  "Cloudinary": {
    "CloudName": "",
    "ApiKey": "",
    "ApiSecret": "",
    "Folder": "user-management",
    "UseSecureDelivery": true,
    "MaxFileSizeBytes": 5242880,
    "AllowedFormats": ["jpg", "png"],
    "AutoInvalidate": true
  }
}
```

---

## 6. Đặc tả dữ liệu

### 6.1. Bảng `IdentityDocuments` — thay đổi

| Cột | Kiểu | Null | Thay đổi |
|---|---|---|---|
| `Id` | uniqueidentifier | No | — |
| `UserId` | uniqueidentifier | No | — |
| `DocumentType` | tinyint | No | — |
| `DocumentNumber` | varchar(20) | No | — |
| `IssuedDate` | date | Yes | — |
| `ExpiryDate` | date | Yes | — |
| ~~`FrontImagePath`~~ | ~~nvarchar(500)~~ | ~~No~~ | **BỎ** → thay bằng `FrontImagePublicId` |
| ~~`BackImagePath`~~ | ~~nvarchar(500)~~ | ~~Yes~~ | **BỎ** → thay bằng `BackImagePublicId` |
| `FrontImagePublicId` | nvarchar(500) | No | **MỚI** — Cloudinary public_id |
| `BackImagePublicId` | nvarchar(500) | Yes | **MỚI** — null với Passport |
| `FrontImageVersion` | bigint | Yes | **MỚI** — version Cloudinary, dùng cho ETag |
| `BackImageVersion` | bigint | Yes | **MỚI** |
| `FrontImageWidth` | int | Yes | **MỚI** — metadata hiển thị |
| `FrontImageHeight` | int | Yes | **MỚI** |
| `BackImageWidth` | int | Yes | **MỚI** |
| `BackImageHeight` | int | Yes | **MỚI** |
| `CreatedAt` | datetime2 | No | — |
| `UpdatedAt` | datetime2 | Yes | — |

**Luật:**

- `FrontImagePublicId` chứa `public_id` đầy đủ (bao gồm folder), tối đa 500 ký tự.
- `BackImagePublicId` null **chỉ** khi `DocumentType = Passport`.
- Không lưu URL Cloudinary đầy đủ trong DB (URL phụ thuộc `secure`/`type`/chữ ký).

### 6.2. Bảng mới `OcrExtractionResults`

Lưu lịch sử mỗi lần quét, phục vụ truy vết và cải thiện mô hình sau này.

| Cột | Kiểu | Null | Mô tả |
|---|---|---|---|
| `Id` | uniqueidentifier | No | PK, sinh ở ứng dụng |
| `UserId` | uniqueidentifier | No | FK → `Users.Id` |
| `ScanId` | uniqueidentifier | No | ID lần quét (unique) |
| `DocumentType` | tinyint | No | 1=CCCD, 2=CMND, 3=Passport |
| `Status` | tinyint | No | 1=Completed, 2=Failed, 3=Confirmed, 4=Expired, 5=Discarded |
| `FrontImagePublicId` | nvarchar(500) | Yes | Ảnh tạm (nếu upload thành công) |
| `BackImagePublicId` | nvarchar(500) | Yes | Ảnh tạm |
| `DocumentId` | uniqueidentifier | Yes | FK → `IdentityDocuments.Id`, null nếu chưa confirm |
| `RawResult` | nvarchar(max) | Yes | JSON thô từ PaddleOCR (kèm rawText) |
| `ExtractedFields` | nvarchar(max) | Yes | JSON đã chuẩn hóa |
| `ProcessingTimeMs` | int | Yes | Thời gian xử lý |
| `ErrorMessage` | nvarchar(1000) | Yes | Lý do thất bại |
| `CreatedAt` | datetime2 | No | UTC |
| `ExpiresAt` | datetime2 | Yes | Hạn dọn ảnh tạm (UTC), mặc định +24h |

**Index:**

| Tên | Cột | Loại | Mục đích |
|---|---|---|---|
| `PK_OcrExtractionResults` | `Id` | Primary key | Định danh |
| `UX_OcrExtractionResults_ScanId` | `ScanId` | Unique | Tra cứu theo scanId |
| `IX_OcrExtractionResults_UserId_CreatedAt` | `UserId`, `CreatedAt` | Nonclustered | Lịch sử quét theo user |
| `IX_OcrExtractionResults_Status_ExpiresAt` | `Status`, `ExpiresAt` | Nonclustered | Job dọn ảnh tạm hết hạn |
| `FK_OcrExtractionResults_Users` | `UserId` | FK | `ON DELETE NO_ACTION` |
| `FK_OcrExtractionResults_IdentityDocuments` | `DocumentId` | FK | `ON DELETE SET NULL` |

> **Quan hệ với `Users`:** dùng `NO_ACTION` (không cascade) để nhất quán với
> `IdentityDocuments`. Khi xóa User, service phải dọn `OcrExtractionResults` cùng
> các ảnh tạm còn sót.

### 6.3. Index `IdentityDocuments` — không đổi

Các index hiện có giữ nguyên:
`UX_IdentityDocuments_UserId_DocumentType`, `UX_IdentityDocuments_Type_Number`.

### 6.4. Migration

```bash
dotnet ef migrations add AddCloudinaryAndOcr \
  --project src/UserManagement.Api \
  --startup-project src/UserManagement.Api

dotnet ef database update \
  --project src/UserManagement.Api \
  --startup-project src/UserManagement.Api
```

**Migration gồm:**

1. Tạo bảng `OcrExtractionResults` + index + FK.
2. Thêm 6 cột mới vào `IdentityDocuments`.
3. Đổi tên `FrontImagePath` → `FrontImagePublicId`, `BackImagePath` → `BackImagePublicId`
   (dùng `RenameColumn` để giữ dữ liệu).
4. **Không** drop cột cũ ngay — xem §6.5.

### 6.5. Chiến lược chuyển dữ liệu ảnh hiện có

Ảnh đang lưu trên ổ đĩa cục bộ (`D:\AppData\UserManagement_Dev`). Cần chuyển sang
Cloudinary.

**Công cụ migration:** `dotnet run --project src/UserManagement.Api -- --migrate-images`

Luồng:

```text
1. Quét tất cả IdentityDocuments có FrontImagePath khác null
2. Với mỗi document:
   a. Mở file cũ từ FileStorage local
   b. Upload lên Cloudinary (folder identity-documents/{userId}/{documentId})
   c. Cập nhật FrontImagePublicId / BackImagePublicId / version / kích thước
3. Commit theo lô (mỗi 50 document)
4. Ghi log tổng kết: thành công / thất bại
5. KHÔNG xóa file cũ — để admin tự xóa sau khi kiểm tra
```

**Quyết định:** file cũ **không tự động xóa**. Bản sao lưu được giữ lại để có
thể rollback. Admin tự dọn sau khi xác nhận dữ liệu trên Cloudinary đầy đủ.

> **Điểm chưa rõ:** đã có dữ liệu production chưa? Nếu chưa, có thể bỏ qua công cụ
> migration và chỉ dùng `EnsureDeleted`. Xem `NOTES.md`.

---

## 7. Đặc tả UI

### 7.1. SCR-07 – Quét tự động giấy tờ (nâng cấp SCR-06)

**Route:** `/Users/{userId}/Documents/Create` (nhúng vào SCR-06 hiện có)

#### 7.1.1. Layout màn hình

```text
┌─────────────────────────────────────────────────────────────┐
│  ← Chi tiết người dùng                                     │
│  Thêm giấy tờ tùy thân                                     │
├──────────────────────────────────┬──────────────────────────┤
│  [1] Loại giấy tờ  [CCCD ▾]     │  ┌─────────────────────┐  │
│  [2] Số giấy tờ    [079095001..]│  │  KẾ QUẢ QUÉT TỰ ĐỘNG│  │
│  [3] Ngày cấp      [10/05/2021]│  ├─────────────────────┤  │
│  [4] Ngày hết hạn  [20/04/2036]│  │ ✓ Số giấy tờ  98%   │  │
│                                  │  │ ✓ Họ và tên    95%   │  │
│  ┌────────────┐ ┌────────────┐  │  │ ⚠ Địa chỉ      78%   │  │
│  │ ẢNH TRƯỚC  │ │  ẢNH SAU   │  │  │ ○ Ngày sinh    62%   │  │
│  │  [chọn]    │ │  [chọn]    │  │  │ ✗ Ngày cấp    40%   │  │
│  │  [preview] │ │  [preview] │  │  └─────────────────────┘  │
│  └────────────┘ └────────────┘  │  [Quét lại] [Bỏ qua]    │
│                                  │                          │
│  [ Hủy bỏ ]      [ Lưu ]        │  ⚠ Vui lòng kiểm tra các  │
│                                  │    trường được đánh dấu ⚠  │
└──────────────────────────────────┴──────────────────────────┘
```

**Mobile (< 1024px):** panel kết quả nằm **trên** form, thu gọn thành accordion.

#### 7.1.2. Trạng thái nút quét

| Trạng thái | Nhãn nút | Hành vi |
|---|---|---|
| Chưa đủ 2 ảnh | `Quét tự động` (disabled) | Tooltip: "Vui lòng chọn đủ ảnh mặt trước và mặt sau" |
| Đủ ảnh | `Quét tự động` | Click → gọi API-12 |
| Đang quét | `Đang quét…` (spinner) | Disable form, hiện progress |
| Thành công | `Quét lại` | Cho phép quét lại với ảnh khác |
| Thất bại | `Thử lại` | Toast lỗi, form vẫn nhập tay được |

#### 7.1.3. Auto-fill

- Mỗi field có `confidence ≥ Ocr:MinConfidence` **và** giá trị hợp lệ theo validator
  → điền vào input, badge xanh/anh theo mức tin cậy.
- Field không đạt ngưỡng → **không điền**, hiển thị dạng text gợi ý mờ dưới input.
- Field OCR có giá trị nhưng **fail validation** (ví dụ số giấy tờ sai định dạng)
  → điền vào input và **gắn lỗi validation tương ứng** (giống `Model.GetFieldError`).
- Người dùng sửa bất kỳ field nào → badge của field đó chuyển thành "đã chỉnh sửa" (xám).
- **Không tự động submit.** Người dùng phải bấm `Lưu`.

#### 7.1.4. Xử lý lỗi

| Lỗi | Hành vi |
|---|---|
| OCR không đọc được (400) | Toast cảnh báo + panel hiển thị raw text để user tự nhập. Form vẫn dùng được. |
| PaddleOCR service lỗi (503) | Toast: "Dịch vụ quét tự động đang tạm thời không khả dụng. Bạn có thể nhập thủ công." Không chặn form. |
| Timeout | Tương tự 503. |
| Upload ảnh lỗi (413/415) | Toast lỗi tương ứng, không gọi OCR. |
| Mất mạng | Toast lỗi, cho phép thử lại. |

**Nguyên tắc:** OCR thất bại **không bao giờ** chặn người dùng lưu giấy tờ.

#### 7.1.5. Trạng thái OCR sau khi lưu

Trong card giấy tờ (SCR-02), hiển thị badge nhỏ:

| Badge | Điều kiện |
|---|---|
| `Quét tự động` (xanh) | Giấy tờ được tạo từ API-13 |
| `Nhập tay` (xám) | Giấy tờ tạo từ API-07 |

#### 7.1.6. Quy tắc bảo mật UI

- File ảnh **không** được gửi lên trước khi người dùng bấm `Quét tự động`.
- Bấm `Lưu` → gửi 1 request duy nhất tới API-13 (không upload ảnh lần 2).
- Nếu người dùng bỏ qua OCR và nhập tay → dùng API-07 như hiện tại.
- Mọi POST có anti-forgery token (theo `UI-Spec §1.4`).
- Hiển thị tiến trình quét; không khóa form vô thời hạn (timeout 30s).

### 7.2. Thay đổi UI hiện có

| Màn hình | Thay đổi |
|---|---|
| **SCR-02** (Details) | Thêm badge nguồn dữ liệu giấy tờ (`Quét tự động` / `Nhập tay`). |
| **SCR-06** (Form) | Thêm panel kết quả OCR + nút `Quét tự động` + nút `Bỏ qua`. |
| **SCR-06** | Ảnh hiện tại (Edit mode) hiển thị qua Cloudinary URL thay vì proxy local. |
| **Proxy ảnh** | Route `/Users/{userId}/Documents/{documentId}/Image/{side}` giữ nguyên, phía sau chuyển sang gọi Cloudinary. |

### 7.3. Thông báo lỗi

| Mã lỗi | Thông báo tiếng Việt |
|---|---|
| OCR_UNREADABLE | "Không đọc được thông tin trên ảnh. Vui lòng chụp lại rõ nét hơn hoặc nhập thủ công." |
| OCR_SERVICE_DOWN | "Dịch vụ quét tự động đang không khả dụng. Bạn có thể nhập thông tin thủ công." |
| OCR_TIMEOUT | "Quét tự động quá thời gian cho phép. Vui lòng thử lại hoặc nhập thủ công." |
| OCR_IMAGE_INVALID | "Ảnh không hợp lệ. Chỉ chấp nhận JPG/PNG, tối đa 5 MB." |
| CLOUDINARY_UPLOAD_FAILED | "Không thể lưu ảnh lên hệ thống lưu trữ. Vui lòng thử lại." |

---

## 8. Quy tắc nghiệp vụ mới

Bổ sung vào `SRS.md` §5:

### BR-13 – Quét OCR không tin cậy

- Kết quả OCR là **dữ liệu gợi ý**, không phải dữ liệu đã xác thực.
- Mọi giá trị OCR phải đi qua validator hiện có trước khi lưu.
- Người dùng phải có quyền sửa mọi field đã điền tự động.
- Hệ thống **không** tự động lưu giấy tờ sau khi quét.

### BR-14 – Ngưỡng tin cậy

- Field có `confidence < Ocr:MinConfidence` (mặc định 0.60) không được tự điền.
- Field có `confidence ≥ 0.85` hiển thị badge "tin cậy cao".
- Field OCR vi phạm validation nghiệp vụ → gắn lỗi tương ứng, không tự sửa giá trị.

### BR-15 – Ảnh tạm

- Ảnh upload trong lúc quét nằm ở folder `tmp/{scanId}`.
- Chỉ dùng để tạo giấy tờ trong 24 giờ.
- Hết 24h hoặc sau khi confirm → xóa.
- Job dọn dẹp chạy định kỳ (hoặc khi khởi động, quét `Status=Completed` và
  `ExpiresAt < now`).

### BR-16 – Duy trì ảnh trên Cloudinary

- Xóa giấy tờ → xóa ảnh trên Cloudinary.
- Xóa User → xóa toàn bộ ảnh của các giấy tờ thuộc User.
- Lỗi xóa Cloudinary → ghi log, không rollback database.
- Ảnh bị thay thế → `AutoInvalidate` để CDN không còn serve bản cũ.

### BR-17 – Quyền truy cập ảnh

- Ảnh phải được phục vụ qua `authenticated` delivery **kèm chữ ký** (§4.4).
- Asset phải upload với `type=authenticated` — `access_mode` không có tác dụng.
- Không đặt `public_id` trong HTML/mail/bất kỳ nơi công khai nào.
- Chữ ký URL **không có TTL** (nó là hash của public id, không phải token hết hạn).
  Nếu cần hết hạn thật sự → dùng Cloudinary **access token** (`__cld_token__`).

---

## 9. Kiến trúc triển khai dự kiến

### 9.1. Thành phần mới trong `UserManagement.Api`

```text
src/UserManagement.Api/
├── Ocr/
│   ├── OcrOptions.cs
│   ├── IOcrService.cs
│   ├── OcrService.cs                  # orchestrator: validate → upload → OCR → normalize
│   ├── PaddleOcrClient.cs             # HTTP client tới PaddleOCR service
│   ├── OcrFieldExtractor.cs           # rawText → trường nghiệp vụ
│   ├── OcrNormalizer.cs               # chuẩn hóa + validate từng field
│   ├── OcrModels.cs                   # OcrScanResult, OcrField, OcrConfidence
│   └── OcrJob.cs                      # BackgroundService dọn ảnh tạm hết hạn
├── FileStorage/
│   ├── ICloudinaryStorage.cs          # UploadAsync, DeleteAsync, BuildDeliveryUrl
│   ├── CloudinaryStorage.cs
│   └── CloudinaryOptions.cs
├── Entities/
│   ├── OcrExtractionResult.cs
│   └── OcrScanStatus.cs
├── DTOs/Users/IdentityDocuments/
│   ├── OcrScanRequest.cs
│   ├── OcrScanResponse.cs
│   └── OcrConfirmRequest.cs
├── Data/Configurations/
│   └── OcrExtractionResultConfiguration.cs
└── Controllers/
    └── IdentityDocumentsOcrController.cs   # API-12, API-13
```

### 9.2. Thành phần mới trong `UserManagement.Web`

```text
src/UserManagement.Web/
├── Services/
│   ├── IUsersApiClient.cs              # thêm ScanOcrAsync, ConfirmOcrAsync
│   └── UsersApiClient.cs
├── ViewModels/Users/
│   └── OcrScanViewModel.cs             # fields, confidence, warnings
└── Views/Documents/
    ├── Form.cshtml                     # + panel kết quả OCR
    └── _OcrResultPanel.cshtml          # panel hiển thị kết quả
```

### 9.3. Thành phần mới — PaddleOCR service

```text
services/paddleocr/
├── Dockerfile
├── requirements.txt
├── app/
│   ├── main.py                 # FastAPI
│   ├── ocr_engine.py           # PaddleOCR wrapper + preprocessing
│   ├── postprocess.py          # rawText → labels (echo of .NET rules, dùng để test)
│   └── models/
└── docker-compose.yml
```

> **Lưu ý:** quy tắc trích xuất nhãn nên triển khai **một lần** ở .NET (source of
> truth). Python chỉ trả raw text + bounding box. Tuy nhiên, để debug dễ dàng,
> service Python có thể trả kèm bounding box theo từng dòng.

### 9.4. Package cần thêm

**`UserManagement.Api`:**

| Package | Phiên bản | Mục đích |
|---|---|---|
| `CloudinaryDotNet` | 2.x | SDK chính thức cho Cloudinary |

> **Khuyến nghị:** cân nhắc dùng **raw HTTP** (`HttpClient` + HMAC-SHA1 signature)
> thay vì thêm SDK, để:
> - Không thêm dependency lớn.
> - Kiểm soát chính xác payload upload (multipart streaming).
> - Dễ test (mock `HttpMessageHandler`).
>
> Cần chốt ở Phase 0 của feature. Xem `NOTES.md`.

**`UserManagement.Web`:** không thêm package.

**`paddleocr` service:**

```
fastapi==0.115.*
uvicorn[standard]==0.32.*
paddleocr==3.2.*
paddlepaddle==3.0.*
opencv-python-headless
numpy
```

---

## 10. Kiểm thử

### 10.1. Unit test

| Lớp | Số test | Nội dung |
|---|---|---|
| `OcrFieldExtractorTests` | ~18 | Trích xuất từ rawText: có/không nhãn, nhãn ở dòng kế, nhiều khoảng trắng, nhãn không dấu |
| `OcrNormalizerTests` | ~15 | Chuẩn hóa ngày (`dd/MM/yyyy`, `dd-MM-yyyy`, `dd.MM.yyyy`, năm 2 số), giới tính, validate BR-08, BR-10 |
| `CloudinarySignatureTests` | ~5 | Tính SHA-1 signature đúng chuẩn Cloudinary |
| `CloudinaryStorageTests` | ~6 | Upload thành công, upload fail, delete nhiều file, build delivery URL |
| `PaddleOcrClientTests` | ~5 | Timeout, 422, 503, malformed JSON, success |
| `OcrServiceTests` | ~10 | Orchestration: upload fail → không gọi OCR; OCR fail → dọn ảnh tạm; confirm flow |

### 10.2. Integration test (API)

| Test | Flow |
|---|---|
| `Scan_CCCD_Returns_Normalized_Fields` | Upload 2 ảnh → 200, field đã chuẩn hóa |
| `Scan_Rejects_Invalid_Image` | Ảnh không phải ảnh → 415 |
| `Scan_Rejects_Oversized_Body` | > `MaxRequestBodyBytes` → 413 |
| `Scan_Unknown_User_Returns_404` | → 404 |
| `Scan_When_Ocr_Service_Down_Returns_503` | Service down → 503, form vẫn dùng được |
| `Scan_When_Ocr_Unreadable_Returns_400` | → 400 + detail thân thiện |
| `Confirm_Creates_Document_With_Scanned_Images` | Confirm → 201, ảnh đã move |
| `Confirm_Rejects_Duplicate_Document_Number` | → 409 |
| `Confirm_Rejects_Invalid_Document_Number` | → 400 |
| `Confirm_Expired_Scan_Returns_400` | Scan > 24h → 400 |
| `Delete_Document_Removes_Cloudinary_Assets` | Xóa DB + gọi Cloudinary destroy |
| `Image_Endpoint_Returns_200_And_ETag` | API-10 trả ảnh qua Cloudinary |
| `Image_Endpoint_Supports_If_None_Match` | → 304 |

### 10.3. E2E test (Web)

| Test | Flow |
|---|---|
| `Ocr_Button_Disabled_Until_Both_Images_Selected` | UI state |
| `Ocr_Scan_Auto_Fills_Form_Fields` | Mock API → form được điền |
| `Ocr_Scan_Failure_Keeps_Form_Usable` | 503 → user vẫn nhập tay và lưu được |
| `Ocr_Low_Confidence_Field_Not_Auto_Filled` | confidence 0.4 → không điền |
| `Ocr_Invalid_Value_Surfaces_Field_Error` | Số giấy tờ sai format → lỗi dưới input |
| `Ocr_Confirm_Saves_Document_And_Redirects` | Lưu → chi tiết, badge "Quét tự động" |

### 10.4. Test thủ công

1. Chụp CCCD thật bằng điện thoại (ánh sáng yếu, góc nghiêng 10°, có đèn flash phản chiếu).
2. Kiểm tra OCR đọc đúng số giấy tờ, họ tên, ngày sinh.
3. Kiểm tra ảnh hiển thị đúng trên Cloudinary qua proxy.
4. Kiểm tra xóa giấy tờ → ảnh biến mất khỏi Cloudinary (kiểm tra dashboard).
5. Kiểm tra thay ảnh → CDN không còn trả ảnh cũ.
6. Kiểm tra CDN cache: URL ảnh cũ trả 404 sau khi xóa.

---

## 11. Rủi ro và giảm thiểu

| # | Rủi ro | Mức | Giảm thiểu |
|---|---|---|---|
| R1 | Ảnh CCCD là **PII nhạy cảm** lưu trên CDN | 🔴 Cao | Dùng `authenticated` delivery, signed URL có thời hạn, không lộ `public_id` ra UI, cân nhắc folder riêng cho PII, xem xét Cloudinary "access control" |
| R2 | `ApiSecret` lộ trong source | 🔴 Cao | User Secrets / biến môi trường, `.gitignore`, kiểm tra secret trước khi commit (Phase 8 checklist) |
| R3 | OCR đọc sai → dữ liệu sai trong DB | 🟡 Trung bình | Auto-fill có badge + ngưỡng tin cậy + bắt buộc user review, không auto-save |
| R4 | PaddleOCR nặng, chậm, cần GPU | 🟡 Trung bình | Timeout 30s, async job nếu cần, CPU-only mode cho dev, cache model trong container |
| R5 | Chi phí Cloudinary (bandwidth + storage) | 🟡 Trung bình | `f_auto,q_auto`, `c_limit`, định dạng thumbnail cho danh sách, theo dõi usage |
| R6 | Phụ thuộc thêm service → độ phức tạp vận hành | 🟡 Trung bình | `docker-compose` gộp API + Web + PaddleOCR + SQL Server; OCR bị fail thì app vẫn chạy |
| R7 | Ảnh local cũ mất khi đổi hệ thống | 🟡 Trung bình | Công cụ migration không xóa file cũ, backup trước khi chạy |
| R8 | URL Cloudinary có chữ ký hết hạn → ảnh hỏng trong cache | 🟢 Thấp | TTL ngắn (15–30 phút), hoặc dùng authenticated delivery không chữ ký theo thời gian |
| R9 | Người dùng lạm dụng OCR để spam tài nguyên | 🟢 Thấp | Chưa có auth → giới hạn tần suất theo IP (đơn giản), giới hạn kích thước, timeout |

---

## 12. Kế hoạch triển khai

| Phase | Nội dung | Phụ thuộc |
|---|---|---|
| **F0** | Chốt quyết định còn mở (SDK vs raw HTTP, có dữ liệu cũ không, quy tắc năm 2 số) | — |
| **F1** | Database: entity `OcrExtractionResult`, cột mới `IdentityDocuments`, migration | F0 |
| **F2** | `ICloudinaryStorage` + `CloudinaryStorage` + `CloudinaryOptions`, thay thế `IFileStorage` | F0 |
| **F3** | `IOcrService` + `PaddleOcrClient` + `OcrFieldExtractor` + `OcrNormalizer` | F0 |
| **F4** | API-12 (`ocr/scan`) + API-13 (`ocr/confirm`), sửa API-07…10 sang Cloudinary | F1, F2, F3 |
| **F5** | PaddleOCR service (Python/Docker) + docker-compose | F3 |
| **F6** | Frontend: panel kết quả OCR, auto-fill, trạng thái lỗi | F4 |
| **F7** | Tool migration ảnh cũ → Cloudinary | F2 |
| **F8** | Job dọn ảnh tạm hết hạn | F1 |
| **F9** | Test: unit + integration + E2E | F4, F6 |
| **F10** | Cập nhật `README.md`, `NOTES.md`, checklist bảo mật | Tất cả |

**Ước lượng:** F2 + F3 + F4 là phần lớn công việc. F5 có thể chạy song song với F2–F4
vì chỉ phụ thuộc hợp đồng API §3.2.

---

## 13. Điểm chưa rõ cần xác nhận

| # | Câu hỏi | Tác động | Đề xuất |
|---|---|---|---|
| Q1 | Dùng `CloudinaryDotNet` SDK hay raw HTTP (`HttpClient` + SHA-1)? | Ảnh hưởng F2, dependency | **Raw HTTP** — kiểm soát payload, dễ test, không thêm SDK lớn |
| Q2 | Hệ thống đã có dữ liệu ảnh trên ổ đĩa cần migrate sang Cloudinary không? | Ảnh hưởng F7 | Nếu chưa có dữ liệu production → bỏ qua F7, chỉ dùng `EnsureDeleted` |
| Q3 | Quy tắc suy luận năm 2 chữ số cho ngày sinh CCCD (`20` → `2020`) có được chấp nhận? | Ảnh hưởng F3, BR-14 | Có, nhưng **luôn hiển thị badge "cần kiểm tra"** cho field được suy luận |
| Q4 | Có cần hỗ trợ OCR mặt sau CMND cũ không, hay chỉ CCCD? | Ảnh hưởng F3, F6 | Hỗ trợ CMND nhưng đánh dấu độ tin cậy thấp hơn |
| Q5 | `Api-10` dùng redirect (307) hay stream proxy? | Ảnh hưởng F4, F6 | **Stream proxy** trước, redirect sau khi đo lưu lượng |
| Q6 | Chi phí Cloudinary dự kiến và quota có đủ không? | Ảnh hưởng R5 | Cần thông tin từ chủ dự án |
| Q7 | Có cần giữ lại ảnh gốc hay chỉ giữ bản đã tối ưu? | Ảnh hưởng R5, F2 | Giữ bản gốc (đã chọn ở §4.5), Cloudinary lưu free |
| Q8 | Có cần giới hạn số lần quét OCR cho người dùng không? | Ảnh hưởng R9 | Tạm thời không, vẫn chưa có auth |
| Q9 | Tên PII trong log có cần redact không? | Ảnh hưởng R1 | Có — không log giá trị OCR (số giấy tờ, họ tên), chỉ log tên field + confidence |
| Q10 | Migration có cần giữ lại cột `FrontImagePath` cũ trong DB? | Ảnh hưởng F1 | Giữ nullable trong 1 release để rollback an toàn, drop ở release sau |

---

## 14. Checklist nghiệm thu

### Backend

- [ ] Migration tạo bảng `OcrExtractionResults` và 6 cột mới.
- [ ] `ICloudinaryStorage` upload/delete/build URL hoạt động, không lộ secret.
- [ ] Signed upload đúng chuẩn Cloudinary.
- [ ] API-12 trả field đã chuẩn hóa + `confidence` + `isValid`.
- [ ] API-13 tạo giấy tờ và move ảnh tạm → ảnh chính thức.
- [ ] Ảnh tạm được dọn sau 24h.
- [ ] Xóa giấy tờ → xóa ảnh Cloudinary, lỗi chỉ ghi log.
- [ ] Thay ảnh → CDN invalidate ảnh cũ.
- [ ] OCR lỗi → 503, **không** 500.
- [ ] Không log giá trị PII từ OCR.
- [ ] `ApiSecret` không nằm trong source.

### Database

- [ ] `IdentityDocuments.FrontImagePublicId` not null.
- [ ] `BackImagePublicId` null chỉ khi Passport.
- [ ] `UX_OcrExtractionResults_ScanId` unique.
- [ ] FK `OcrExtractionResults → Users` là `NO_ACTION`.
- [ ] FK `OcrExtractionResults → IdentityDocuments` là `SET NULL`.

### Frontend

- [ ] Nút `Quét tự động` chỉ active khi đủ ảnh.
- [ ] Field có confidence ≥ ngưỡng được điền kèm badge.
- [ ] Field thấp confidence không tự điền.
- [ ] Field OCR sai format hiển thị lỗi validation.
- [ ] OCR lỗi → form vẫn nhập tay và lưu được.
- [ ] Không tự động submit.
- [ ] Không upload ảnh trước khi bấm `Quét tự động`.
- [ ] Badge nguồn dữ liệu hiển thị ở SCR-02.
- [ ] Responsive từ 360 px.

### Vận hành

- [ ] `docker-compose` chạy được API + Web + PaddleOCR.
- [ ] PaddleOCR health endpoint trả 200.
- [ ] README có hướng dẫn cấu hình Cloudinary + OCR.
- [ ] Có tài liệu dựng PaddleOCR service (CPU/GPU, model, port).
