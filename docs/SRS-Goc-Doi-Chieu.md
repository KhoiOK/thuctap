# Kiem chuc SRS goc - SRS-UM-001 v2.0 vs code hien tai

- **Tai lieu goc:** docs/SRS_He_thong_Quan_ly_User_v2_0.pdf (SRS-UM-001 v2.0, 15 trang)
- **Muc dich:** doi chieu PDF (de dac ta co tham quyen) voi code da trien khai, xac dinh khoang trong thuc te.
- **Trang thai:** phan tich, chua sua code.

---

## 0. Vi tri cua PDF trong bo tai lieu

PDF la **ban rut gon cho AI Agent** cua SRS-UM-001 v2.0. No la tai lieu co tham quyen cao nhat,
va no **khac** 5 file markdown trong docs/ o mot so diem quan trong:

| Tai lieu | Ban | Vai tro |
|---|---|---|
| `SRS_He_thong_Quan_ly_User_v2_0.pdf` | PDF, 15 trang, rut gon | **De dac ta goc, co tham quyen** |
| `SRS.md` | Markdown | Phien ban tach rieng, doc lap |
| `API-Spec.md` | Markdown | Dac ta API 11 endpoint |
| `Database-Design.md` | Markdown | Dac ta DB |
| `UI-Spec.md` | Markdown | Dac ta UI |
| `AI-Coding-Plan.md` | Markdown | Ke hoach trien khai |

### 0.1. Diem PDF va SRS.md khac nhau can luu y

PDF va SRS.md **khong giong nhau hoan toan**. Khi trien khai tiep, phai chon mot nguon suy.

| Muc | PDF (SRS goc) | SRS.md |
|---|---|---|
| So man hinh | 6 (SCR-01..06) | 6 (SCR-01..06) — khop |
| So endpoint | 11 (#1..#11) | 11 — khop |
| Thu muc Frontend | `wwwroot/css/site.css`, `js/users.js` | `wwwroot/css/app.css`, `js/site.js` |
| Layout | header co dinh "Quan ly User", `max-w-6xl` | cung vay |
| Toast | goc tren phai | goc tren phai |
| Gia tri rong | `---` | `---` |
| Hop le hoa so DT | PDF: `84` chi doi khi co >= 5 chu so | SRS.md: `+84` hoac `84` doi thong qua |
| Layout frontend | `Services/ApiClients/`, `Models/` | `Services/`, `ViewModels/` |
| Cache-Control anh | PDF: `private, no-cache` | API-Spec: `private, no-cache`; code hien tai: `private, max-age=300` |

**Ket luan:** Hai sai lech tren la khong quan trong ve hanh vi, tru `Cache-Control` va
quy tac `84` can chon mot ben. Da ghi vao `NOTES.md`.

### 0.2. PDF xac nhan tinh huong Cloudinary

PDF muc 1.7 (Gia dinh da chot) ghi ro:

> "Anh luu tren thu muc server qua IFileStorageService (de thay bang cloud)"

=> Thay bang Cloudinary **di dung huong thiet ke goc**, khong phai tu y them tinh nang.
Dieu nay xac nhan quyet dinh cua chu du an la hop le.

---

## 1. Doi chieu PDF voi code hien tai

### 1.1. Kiem tra tuong thich - KHONG co van de

| Yeu cau PDF | Muc | Code | Trang thai |
|---|---|---|---|
| 11 endpoint dung route/method | 4.2 | `UsersController` (API-01..05), `IdentityDocumentsController` (API-06..11) | DAT |
| Route param dung constraint `guid` | 1.5 | `[Route("api/v1/users/{userId:guid}/identity-documents")]`, Web `[HttpGet("Details/{id:guid}")]` | DAT |
| Global query filter `!IsDeleted` | 3.4 | `UserConfiguration` | DAT |
| Filtered unique index Email/Phone | 3.4 | `UX_Users_Email`, `UX_Users_PhoneNumber` | DAT |
| FK `ON DELETE RESTRICT` | 3.4 | `IdentityDocumentConfiguration` | DAT |
| BR-01 email trung -> 409 | 2.3 | `EnsureEmailAvailableAsync` + `ConflictException` | DAT |
| BR-02 chuan hoa + regex | 2.3 | `NormalizationHelpers.NormalizePhoneNumber` + `PhoneRegex` | DAT |
| BR-03 ho ten 2-100 | 2.3 | `UserValidator` | DAT |
| BR-04 ngay sinh 1900..today | 2.3 | `IsValidDateOfBirth` | DAT |
| BR-05 xoa mem + xoa vat ly gia to | 2.3 | `UserService.SoftDeleteAsync` | DAT |
| BR-06 toi da 1 gia to moi loai | 2.3 | `EnsureDocumentTypeSlotAvailableAsync` | DAT |
| BR-07 CCCD/CMND 2 anh, Passport 1 anh | 2.3 | `ValidateMetadata` | DAT |
| BR-08 so gia to | 2.3 | `IsValidDocumentNumber` | DAT |
| BR-09 magic bytes, 5MB, 8000px | 2.3 | `ImageValidator` | DAT |
| BR-10 ngay cap/het han | 2.3 | `ValidateMetadata` | DAT |
| BR-11 so gia to trung -> 409 | 2.3 | `EnsureDocumentNumberAvailableAsync` | DAT |
| BR-12 huy badge "Da het han" | 2.3 | `Details.cshtml` so sanh `ExpiryDate` | DAT |
| Problem Details + `errors` + `traceId` | 4.4 | `GlobalExceptionHandler` + `ModelStateValidationFilter` | DAT |
| 413 -> ProblemDetails | 4.1 | `BadHttpRequestException` mapping trong `GlobalExceptionHandler` | DAT |
| Ghi file truoc -> DB sau -> xoa file cu | FR-07/08 | `IdentityDocumentService` | DAT |
| Anti-forgery moi form POST | 1.5 | `[ValidateAntiForgeryToken]` + `AddAntiforgery` | DAT |
| Upload truyen stream | 1.5 | `StreamContent` trong `UsersApiClient` | DAT |
| Proxy anh khong dung `frontImageUrl` | 1.5 | `Details.cshtml` tu dung `/Users/{u}/Documents/{d}/Image/{side}` | DAT |
| `returnUrl` chi nhan `/Users` | FR-03 | `SanitizeReturnUrl` | DAT |
| `Try lai` khi API loi | FR-01 | `vm.ErrorMessage` + nut | DAT |
| 404 id khong phai GUID | FR-03 | Constraint `guid` lam route khong match -> 404 | DAT |

### 1.2. Khoang trong thuc te - CAN XEM LAI

#### G1. Khong ghi log truy cap anh (PDF muc 6.1 - **PHAI**)

PDF 6.1 yeu cau:

> "Moi luot xem anh PHAI ghi log (thoi diem, dia chi nguon, userId, documentId, side)"

**Code hien tai:** `IdentityDocumentsController` co `_logger` nhung **khong bao gio** du dung.
`GetImage` chi tra ve stream. Chi co log o `IdentityDocumentService` cho viec xoa file that bai.

**Muc do:** Khong ro - PDF dung chu **PHAI** (bat buoc), SRS.md 6 cung noi "Log truy cap anh gom thoi diem,
dia chi nguon, UserId, DocumentId va side".

**De xuat:** them 1 dong log `LogInformation` trong `GetImage`:
thoi diem, `HttpContext.Connection.RemoteIpAddress`, userId, documentId, side.
**Khong** ghi so gia to day du (PDF cam ro).

#### G2. Cache-Control anh (PDF 4.3 #10)

PDF 4.3 #10 va SRS.md deu quy dinh: `Cache-Control: private, no-cache`.

**Code hien tai (API):** `IdentityDocumentsController.GetImage` dat `private, max-age=300`.

**Code hien tai (Web proxy):** `DocumentsController.Image` **khong** chuyen tiep `Cache-Control` tu API ra
response cua Frontend (chi chuyen tiep `ETag` va `Last-Modified`).

**Tac dong:**
- `max-age=300` sai voi dac ta -> tranh chup man hinh se thay the.
- Khong chuyen tiep `Cache-Control` o Web -> mac dinh cua browser/proxy khong duoc control.

**De xuat:** doi API sang `private, no-cache` va chuyen tiep header nay o Web proxy.
Khi dung Cloudinary, can xem lai (xem `OCR-Cloudinary-Spec.md` muc 5.2).

#### G3. Chuan hoa tien to `84` khi tim kiem (PDF FR-02)

PDF FR-02 quy dinh ro:

> "Tu khoa giong so dien thoai: ... neu bat dau bang +84 (hoac 84 va co tu 5 chu so tro len)
> thi doi tien to thanh 0 roi so voi so dien thoai (+84912345678 -> 0912345678).
> Tu khoa goc van so voi ho ten va email."

**Code hien tai (`UserService.NormalizeKeyword`):** chi chuan hoa `keyword` thanh chu thuong + bo dau.
Khong co logic doi tien to `84`.

**Hau qua:** tim kiem `84912345678` **khong tim thay** user co so `0912345678`.

**Lưu y:** `NormalizePhoneNumber` (dung khi *luu*) co xu ly `84` khi length > 9, nhung:
- dieu kien PDF la `>= 5 chu so` (sau tien to), code la `length > 9` (tong dai).
- va ham search **khong goi** ham normalize do.

**De xuat:** tach `NormalizeSearchKeyword` rieng cho search:
- thu `keyword` (gia nguyen ban dau) -> kiem tra co phai so dien thoai -> chuan hoa `84` -> so sanh `PhoneNumber`.
- giu nguyen ban chuan hoa de so sanh `FullNameSearch` va `Email`.

#### G4. Hien thi "Tim thay N ket qua cho 'tu khoa'" (PDF FR-02)

PDF FR-02 yeu cau hien thi dung chu: **"Tim thay N ket qua cho 'tu khoa'"**.

**Code hien tai (`Index.cshtml`):** hien thi **"N ket qua"** (stat-pill) va empty state
**"Khong tim thay ket qua"**.

**Muc do:** Thap - thong bao thay the, van dung thong tin. Nhung PDF danh nguyen van ban
("dung nguyen van ban trong tai lieu", muc 8.1).

**De xuat:** doi stat-pill thanh `Tim thay N ket qua cho 'tu khoa'` khi co keyword,
giu `N ket qua` khi khong co keyword.

#### G5. `Cache-Control` + `Retry` cho GET (PDF 1.5)

PDF 1.5: "Goi Api bang typed HttpClient ... retry cho loi mang tam thoi voi GET."

**Code hien tai (`UsersApiClient`):** khong co retry policy nao cho GET.

**Muc do:** Thap - polling resilience, khong anh huong dung chuc.

**De xuat:** them Polly `HttpPolicyHandler` hoac vong lap retry don gian cho GET
(khong retry khi `ApiTransportException` do timeout).

#### G6. Loai giay to CCCD/CMND hien thi "CCCD / The can cuoc" (PDF SCR-02)

PDF SCR-02: huy badge loai giay to: **"CCCD / The can cuoc"**, "CMND", "Passport".
PDF 1.7: "Van cho nhap CMND (9 hoac 12 so); nhan hien thi 'CCCD / The can cuoc'."

**Code hien tai (`Form.cshtml`):** `<option value="CCCD">CCCD — Can cuoc cong dan</option>`.

**Muc do:** Thap - khac cach dien dat, van dung nghhia.

**De xuat:** doi nhan hien thi thanh `CCCD / Thẻ căn cước` de khop PDF.

#### G7. Frontend dung `site.css` / `users.js` (PDF 1.4)

PDF 1.4 quy dinh `wwwroot/css/site.css` (output Tailwind) va `js/users.js`.

**Code hien tai:** `wwwroot/css/app.src.css` -> `app.css`, `wwwroot/js/site.js`.

**Muc do:** Thap - ten file, khong anh huong hanh vi. Da duoc ghi trong `NOTES.md` (UI redesign).

**De xuat:** khong can doi. Neu can, dat them file alias de khop ten trong PDF.

---

## 2. OCR + Cloudinary doi voi PDF goc

PDF la ban goc, nen moi mo rong phai **ghi ro vao PDF** (hoac ban dien bien ghi nhan).
Doi chieu 2 tinh nang da chot voi cac dieu khoan cua SRS-UM-001 v2.0:

### 2.1. OCR - diem nao cua PDF bi anh huong

| Dieu khoan PDF | Noi dung | Tac dong |
|---|---|---|
| 1.1 (Ngoai pham vi) | "OCR hoac xac thuc giay to" nam **ngoai pham vi** | **Mo pham vi** - phai bo muc do tu danh sach ngoai pham vi |
| 2.1 (Danh sach tinh nang) | FR-01..FR-08 | **Them FR-09** (quet OCR) |
| 3.3 (Bang IdentityDocuments) | `FrontImagePath` / `BackImagePath` (duong dan tuong doi) | **Doi thanh** `FrontImagePublicId` / `BackImagePublicId` |
| 3.5 (Luu tru file anh) | "Thu muc goc FileStorage:RootPath", cau truc `{RootPath}/identity-documents/...` | **Viet lai** cho Cloudinary |
| 4.2 (Danh sach endpoint) | 11 endpoint | **Them #12, #13** |
| 4.1 (Ma HTTP) | 200/201/204/400/404/409/413/415/500 | **Them 503** (OCR service khong san sang) |
| 4.4 (Dinh dang loi) | Bang status + title + type + errors | **Them dong 503** + error code OCR |
| 5.3 (SCR-06) | Form them/sua gia to | **Mo rong SCR-06** them panel ket qua OCR + nut "Quet tu dong" |
| 6.2 (Validation) | Bang truong + thong bao loi | **Them truong OCR** + quy tac tin cay |
| 7 (Acceptance Criteria) | Checklist FR-01..FR-08 | **Them checklist OCR** |
| 8.2 (Cac buoc) | Buoc 0..8 | **Them buoc trien khai OCR/Cloudinary** |

### 2.2. Cloudinary - diem nao cua PDF bi anh huong

| Dieu khoan PDF | Noi dung | Tac dong |
|---|---|---|
| 1.3 (Kien truc) | "Api -> SQL Server + File Storage (thu muc anh)" | **Doi** thanh "Cloudinary" |
| 1.4 (Backend API) | `IFileStorageService (LocalFileStorageService)` | **Thay** bang `ICloudinaryStorage (CloudinaryStorage)` |
| 1.6 (Cau hinh) | `FileStorage:RootPath`, `MaxFileSizeBytes`, `MaxRequestBodyBytes` | **Them** section `Cloudinary` + `Ocr`; `RootPath` khong con dung cho anh |
| 3.1 (Quy uoc) | "Database khong luu anh, chi luu duong dan tuong doi" | **Doi** thanh "public_id" |
| 3.5 (Luu tru file anh) | Cau truc thu muc, `{guid}`, `{ext}` tu magic bytes | **Viet lai** cho signed upload + folder Cloudinary |
| 6.1 (Bao mat) | "Anh luu ngoai wwwroot, chi truy cap qua Api" | **Bao dam tuong doi**: dung `authenticated` delivery, khong lo `public_id` ra UI |
| 6.1 (Bao mat) | "NEN bao ve so gia to o muc luu tru (TDE)" | Van con huu luc |
| 6.1 (Bao mat) | "Moi luot xem anh PHAI ghi log" | Van con, xem G1 |

### 2.3. Dieu khoan PDF **xung dot** voi quyet dinh moi

PDF 3.5 va 1.6 yeu cau anh luu trong thu muc `FileStorage:RootPath`.
Quyet dinh "Cloudinary thay the hoan toan" **mau thuan truc tiep** voi dieu khoan nay.

=> Phai sua PDF (ban dien bien) hoac ban ghi nhan danh chuc trong `NOTES.md`.
`OCR-Cloudinary-Spec.md` muc 6.4 da quy dinh migration giu nguyen cot cu de rollback an toan.

### 2.4. Dien bien PDF de xuat

Them vao cuoi PDF (truoc muc 7 "Acceptance Criteria") mot muc moi:

> **1.8 Bo sung - OCR giay to va luu tru anh Cloudinary (v2.1)**
>
> - FR-09: Quet tu dong thong tin CCCD/CMND tu anh mat truoc + mat sau bang PaddleOCR.
>   Ket qua la **du lieu goi y**, nguoi dung phai review truoc khi luu (BR-13).
> - FR-10: Luu tru anh tren Cloudinary thay cho thu muc cuc bo. Database luu `public_id`.
> - API-12 `POST /users/{userId}/identity-documents/ocr/scan`
> - API-13 `POST /users/{userId}/identity-documents/ocr/confirm`
> - BR-13..BR-17 (xem `OCR-Cloudinary-Spec.md` muc 8)
> - SCR-07: Panel ket qua OCR canh form gia to, nut "Quet tu dong", auto-fill.
> - Thay doi mau so gia do: them 503 (OCR service khong san sang).
>
> Chi tiet: `docs/OCR-Cloudinary-Spec.md`.

---

## 3. Khuy nghi cua trien khai

### 3.1. Thu tu uu tien

| # | Viec | Ly do | effort |
|---|---|---|---|
| 1 | **G1** - ghi log truy cap anh | PDF dung chu **PHAI**, la gap bao mat | Nho |
| 2 | **G2** - sua `Cache-Control` | Sai dac ta, co tac dong hanh vi cache | Nho |
| 3 | **G3** - chuan hoa `84` khi search | Gap chuc nang tim kiem, PDF co test case | Trung binh |
| 4 | **G4** - chuoi "Tim thay N ket qua cho ..." | Doi chieu nguyen van ban | Rat nho |
| 5 | **G6** - nhan "CCCD / The can cuoc" | Doi chieu nguyen van ban | Rat nho |
| 6 | **G5** - retry GET | Resilience, khong gap dung chuc | Trung binh |
| 7 | **G7** - ten file frontend | Khong anh huong | Bo qua |

### 3.2. Cac diem nen bo sung test khi sua

| Muc | Test can them |
|---|---|
| G1 | Log ghi lai `userId`, `documentId`, `side`; **khong** ghi so gia to day du |
| G2 | Response 200 co `Cache-Control: private, no-cache`; Web proxy van lai header |
| G3 | Search `84912345678` -> tim thay user so `0912345678`; `+84912345678` -> tim thay; `0912` -> kiem tra `contains`; `@gmail.com` -> kiem tra email |
| G4 | Render co keyword -> hien thi "Tim thay N ket qua cho 'X'" |

### 3.3. Cac diem can xac nhan voi chu du an

| # | Cau hoi | Ly do can hoi |
|---|---|---|
| Q1 | Co dung `Cache-Control: private, no-cache` (PDF) hay `private, max-age=300` (code) khong? | PDF va code dang lech nhau - phai chon mot |
| Q2 | Co bao gio phai ghi log truy cap anh (PDF 6.1) hay coi la khong bat buoc? | PDF dung chu PHAI nhung dang trien khai khong co |
| Q3 | Khi chuyen Cloudinary, co chap nhan doi muc 3.5 cua PDF khong? | Chay theo de dac ta goc hay theo tai lieu mo rong? |
| Q4 | Co bao gio can `retry` cho GET khong? | PDF 1.5 co yeu cau, thuong chi can khi trien khai production |

---

## 4. Checklist doi chieu

### Da kiem tra - dat

- [x] 11 endpoint dung route, method, path param
- [x] Constraint `guid` tren moi route co id
- [x] Global query filter `!IsDeleted`
- [x] 4 filtered index + 2 unique index + FK RESTRICT
- [x] BR-01..BR-12 duoc hien thuc day du
- [x] Problem Details co `errors` + `traceId`, dung status code
- [x] 413 -> ProblemDetails
- [x] Thu tu ghi file -> DB -> xoa file cu
- [x] Anti-forgery tren moi form POST
- [x] Upload truyen stream, khong doc het file vao bo nho
- [x] Proxy anh dung route Frontend, khong dung `frontImageUrl` cua API
- [x] `returnUrl` chi nhan duong dan noi bo bat dau bang `/Users`
- [x] 404 cho id khong phai GUID
- [x] 144/144 test pass (104 API + 40 Web)

### Can xem lai - da ghi o muc 1.2

- [ ] G1 - ghi log truy cap anh (PDF 6.1, PHAI)
- [ ] G2 - `Cache-Control` anh (PDF 4.3 #10)
- [ ] G3 - chuan hoa tien to `84` khi tim kiem (PDF FR-02)
- [ ] G4 - chuoi "Tim thay N ket qua cho 'tu khoa'" (PDF FR-02)
- [ ] G5 - retry cho GET (PDF 1.5)
- [ ] G6 - nhan "CCCD / The can cuoc" (PDF SCR-02, 1.7)
- [ ] G7 - ten file frontend `site.css` / `users.js` (PDF 1.4)

### Can bo sung theo OCR + Cloudinary

- [ ] FR-09 quet OCR + FR-10 luu Cloudinary (PDF 2.1)
- [ ] Doi cot `FrontImagePath` -> `FrontImagePublicId` (PDF 3.3)
- [ ] Viet lai muc 3.5 luu tru file anh (PDF 3.5)
- [ ] API-12, API-13 (PDF 4.2)
- [ ] Status 503 (PDF 4.1, 4.4)
- [ ] SCR-07 panel ket qua OCR (PDF 5.3)
- [ ] BR-13..BR-17 (PDF 2.3)
- [ ] Checklist nghiem thu OCR (PDF 7)
