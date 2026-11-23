# Ghi chu doi chieu SRS goc - SRS-UM-001 v2.0

Tai lieu phu tro cho `NOTES.md`. Tom tat nhanh cac diem phat hien khi doi chieu
`docs/SRS_He_thong_Quan_ly_User_v2_0.pdf` voi code hien tai.

Chi tiet day du: `docs/SRS-Goc-Doi-Chieu.md`.

---

## 1. Vi tri cua PDF trong bo tai lieu

PDF la **ban rut gon cho AI Agent** cua SRS-UM-001 v2.0 (15 trang) - la **de dac ta
co tham quyen cao nhat**. Khac 5 file markdown trong `docs/`.

PDF va `SRS.md` khong giong nhau hoan toan. Cac diem lech da duoc ghi nhan:

| Dieu khoan | PDF | SRS.md / code |
|---|---|---|
| Thu muc Frontend | `wwwroot/css/site.css`, `js/users.js` | `wwwroot/css/app.css`, `js/site.js` |
| Layout frontend | `Services/ApiClients/`, `Models/` | `Services/`, `ViewModels/` |
| Tien to `84` khi search | doi khi con >= 5 chu so | code chua xu ly trong search |
| Cache-Control anh | `private, no-cache` | code: `private, max-age=300` |
| Nhan loai gia to | "CCCD / The can cuoc" | code: "CCCD - Can cuoc cong dan" |

## 2. PDF xac nhan huong Cloudinary

PDF muc 1.7 ghi ro:

> "Anh luu tren thu muc server qua IFileStorageService (de thay bang cloud)"

=> Cloudinary **di dung huong thiet ke goc**, khong phai tu y them tinh nang.

## 3. Khoang trong thuc te - 7 muc

| # | Muc | Dieu khoan PDF | Muc do |
|---|---|---|---|
| G1 | Khong ghi log truy cap anh | 6.1 - **PHAI** | Khong ro |
| G2 | `Cache-Control` anh sai dac ta | 4.3 #10 | Trung binh |
| G3 | Chua chuan hoa tien to `84` khi search | FR-02 | Trung binh |
| G4 | Thieu chuoi "Tim thay N ket qua cho 'tu khoa'" | FR-02 | Thap |
| G5 | Chua co retry cho GET | 1.5 | Thap |
| G6 | Nhan "CCCD / The can cuoc" | SCR-02, 1.7 | Thap |
| G7 | Ten file frontend khac PDF | 1.4 | Thap |

G1 va G3 la hai muc **co test case ro rang** trong PDF (muc 7) - nen uu tien sua truoc.

## 4. Diem xung dot khi chuyen Cloudinary

PDF 1.6, 3.1, 3.5 yeu cau anh luu trong thu muc `FileStorage:RootPath` voi
`FrontImagePath` / `BackImagePath` la duong dan tuong doi.

Quyet dinh "Cloudinary thay the hoan toan" **mau thuan truc tiep** voi 3 dieu khoan nay.

Xu ly de xuat:
1. Ghi nhan day la **ban dien bien** cua SRS-UM-001 (v2.1) trong PDF.
2. Migration giu nguyen cot cu (nullable) 1 release de rollback an toan.
3. Cap nhat PDF muc 1.3, 1.4, 1.6, 3.1, 3.3, 3.5 khi trien khai.

## 5. Dieu khoan PDF bi anh huong boi OCR

| Dieu khoan | Tac dong |
|---|---|
| 1.1 | "OCR hoac xac thuc giay to" phai bo khoi danh sach ngoai pham vi |
| 2.1 | Them FR-09 (quet OCR) |
| 3.3 | Doi `FrontImagePath` -> `FrontImagePublicId` |
| 3.5 | Viet lai luu tru file anh |
| 4.1, 4.4 | Them 503 (OCR service khong san sang) |
| 4.2 | Them API-12, API-13 |
| 5.3 | Mo rong SCR-06 + SCR-07 |
| 6.2 | Them truong OCR + quy tac tin cay |
| 7 | Them checklist nghiem thu OCR |
| 8.2 | Them cac buoc trien khai |

## 6. Cau hoi can xac nhan

| # | Cau hoi |
|---|---|
| Q1 | `Cache-Control` dung `private, no-cache` (PDF) hay `private, max-age=300` (code)? |
| Q2 | Co ban ghi log truy cap anh khong (PDF 6.1 dung chu PHAI)? |
| Q3 | Chap nhan doi muc 3.5 cua PDF khi chuyen Cloudinary chu? |
| Q4 | Co can retry cho GET khong (PDF 1.5)? |

## 7. Trang thai trien khai hien tai

Khong sua code trong luot nay - chi tai lieu.
Sau khi chu du an chot 4 cau hoi tren, thu tu uu tien:

1. G1 (ghi log truy cap anh)
2. G2 (Cache-Control)
3. G3 (chuan hoa `84` khi search)
4. G4, G6 (nguyen van ban)
5. G5 (retry GET)
