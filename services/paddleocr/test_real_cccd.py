"""Regression test against the RAW OCR text of a REAL Vietnamese CCCD photo.

The synthetic image used in `make_test_cccd.py` has a much simpler layout than a real
card. The real one is:

- BILINGUAL - the label reads "Họ và tên / Full name:" and the value sits on the NEXT
  line, not after the label.
- The number is printed as "số/No.048203001078", which the OCR mangles into
  "só/No.048203001078" - and there is NO "Số CCCD" label at all.
- The card header "SOCIALIST REPUBLIC OF VIET NAM" starts with "so" and used to be
  read as the document number.
- The photo is taken sideways, so the plain OCR line order does not follow the card.
- The MRZ is OCR'd unreliably - the two lines come out interleaved and mangled.

This test pins the parser to the text PaddleOCR 3.2 actually produced for a real
photo, so the fixes cannot regress unnoticed.

Run with: python test_real_cccd.py
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

from app.main import (  # noqa: E402
    BACK_LABELS,
    FRONT_LABELS,
    TextLine,
    _build_lines,
    _label_matches,
    canonical_field_names,
    find_fields,
    find_fields_geo,
    normalize_label,
    parse_mrz,
)

# The raw text PaddleOCR 3.2.0 produced for the front side of a real CCCD photo.
REAL_FRONT_LINES = [
    "Date of expiry",
    "Có giá trị dên:",
    "*05/10/2028",
    "Mý An, Ng Hành Sơn, Đà Nng",
    "Noi thuòng tru / Place of residence:",
    "Xuân Hng, Xuân Trưng, Nam Đnh",
    "Quê quán / Place of origin:",
    "Gii tính/ Sex: Nam Quc tch/ Nationality Vit Nam",
    "Ngày sinh / Date of birth:",
    "NGUYN MINH KHI",
    "Ho và tên / Full name:",
    "sé/No.048203001078",
    "Citizen Identity Card",
    "CN CUC CÔNG DN",
    "CONG HA XA HI CHU NGHIA VIT NAM",
    "Independence - Freedom -Happiness",
    "SOCIALIST REPUBLIC OF VIET NAM",
    "Đc lp  T do - Hạnh phúc",
    "0",
    "05/10/2003",
    "T 54",
]

# The raw text PaddleOCR 3.2.0 produced for the back side of a real CCCD photo.
REAL_BACK_LINES = [
    "Seo con 3cm 1cm5 tren sau",
    "Dc diém nhan dang I Personal identfication.",
    "NGUYEN<MINH<KHOI<<<<",
    "0310055M2810054VNM<<<<<<<<<<<2",
    "IDVNM2030010787048203001078<<7",
    "QUN LY HNH CHİNH V TRAT TU XA HI",
    "FOR ADMINISIRATIVE MANAGEMENT OF SOCIALORDER",
    "DIRECTOR GENERAL OF THE POLICE DEPARTMENT",
    "CUC TRƯNG CC CÀNH SA",
    "Phm Công Nguyên",
    "Leftindex finger",
    "Ngón tró trái",
]

# The real values on the card.
EXPECTED_NUMBER = "048203001078"
EXPECTED_NAME = "NGUYN MINH KHI"
EXPECTED_DOB = "05/10/2003"
EXPECTED_EXPIRY = "05/10/2028"
EXPECTED_ISSUE = "22/04/2021"

failures: list[str] = []


def check(condition: bool, message: str) -> None:
    if not condition:
        failures.append(message)


# ---------------------------------------------------------------------------
# The geometry-aware parser (the one that works on a real photo).
# ---------------------------------------------------------------------------

# The lines of the front side WITH their real bounding boxes. The photo was taken
# sideways, so the card is landscape: x grows to the right, y downwards.
GEOMETRY_FRONT = [
    # (text, x0, y0, x1, y1)
    ("CONG HÓA XA HOI CHU NGHIA VIT NAM", 302, 177, 763, 215),
    ("Đc lp - Tư do - Hanh phúc", 406, 205, 663, 239),
    ("SOCIALIST REPUBLIC OF VIET NAM", 371, 232, 695, 263),
    ("Independence - Freedom -Happiness", 376, 252, 691, 284),
    ("CN CƯÓC CÔNG DN", 301, 287, 764, 342),
    ("0", 513, 345, 568, 380),
    ("Citizen Identity Card", 300, 348, 515, 384),
    ("só/No.048203001078", 298, 396, 724, 450),
    ("Ho và tên / Full name:", 300, 447, 530, 480),
    ("NGUYN MINH KHI", 297, 477, 658, 529),
    ("Ngày sinh / Date of birth:", 300, 521, 558, 552),
    ("05/10/2003", 579, 528, 736, 567),
    ("Gii tính / Sex: Nam Quc tch / Nationality: Vit Nam", 298, 558, 923, 602),
    ("Quê quán / Place of origin:", 299, 605, 570, 636),
    ("Xuân Hng, Xuân Trưng, Nam Đnh", 296, 633, 805, 673),
    ("Noi thuòng trú I Place of residence:", 298, 666, 670, 695),
    ("T 54", 662, 668, 767, 710),
    ("Có giá trj dn:", 39, 676, 167, 706),
    ("05/10/2028", 164, 691, 289, 718),
    ("Date of expiry", 41, 700, 158, 724),
    ("M An, Ng Hành Son, Đà Nng", 293, 706, 748, 746),
]

# The lines of the BACK side with their real bounding boxes. The issue date is
# printed here as "Ngày, tháng, năm / Date, month, year: 22/04/2021".
GEOMETRY_BACK = [
    ("Dac diem nhan dang / Personal identification.", 150, 165, 520, 200),
    ("Seo con 3cm 1cm5 trén sau", 175, 200, 400, 225),
    ("Ngay, thang, năm /Date, month, year22/04/2021", 185, 225, 520, 262),
    ("cánh mūi trái", 100, 215, 255, 245),
    ("CUC TRUNG CC CÀNH ST", 210, 245, 490, 280),
    ("QUN L HNH CHÍNH V TRT TƯ X HI", 190, 265, 510, 295),
    ("DIRECTOR GENERAL OF THE POLICE DEPARTMENT", 200, 275, 505, 305),
    ("FOR ADMINISTRATIVE MANAGEMENT OF SOCIAL ORDER", 195, 290, 510, 320),
    ("Phm Công Nguyên", 350, 455, 620, 495),
    ("IDVNM2030010787048203001078<<7", 240, 505, 730, 540),
    ("0310055M2810054VNM<<<<<<<<<<<2", 235, 545, 730, 580),
    ("NGUYEN<MINH<KHOI<<", 240, 590, 730, 625),
]


def _geometry_lines() -> list[TextLine]:
    return [TextLine(text, 0.9, x0, y0, x1, y1) for text, x0, y0, x1, y1 in GEOMETRY_FRONT]


def _geometry_back_lines() -> list[TextLine]:
    return [TextLine(text, 0.9, x0, y0, x1, y1) for text, x0, y0, x1, y1 in GEOMETRY_BACK]


def test_the_geometry_parser_reads_the_number() -> None:
    fields = find_fields_geo(_geometry_lines(), FRONT_LABELS, "front")
    check("documentNumber" not in fields, "The number must not be a canonical name yet.")
    number = fields.get("socccd") or fields.get("so")
    check(number is not None, "No document number field at all.")
    if number is not None:
        check(
            number.value == EXPECTED_NUMBER,
            f"Wrong document number: {number.value!r} (expected {EXPECTED_NUMBER!r})",
        )


def test_the_geometry_parser_reads_the_name() -> None:
    fields = find_fields_geo(_geometry_lines(), FRONT_LABELS, "front")
    check("hovaten" in fields, "'ho va ten' label not found.")
    if "hovaten" in fields:
        check(
            fields["hovaten"].value == EXPECTED_NAME,
            f"Wrong name: {fields['hovaten'].value!r} (expected {EXPECTED_NAME!r})",
        )


def test_the_geometry_parser_reads_the_date_of_birth() -> None:
    fields = find_fields_geo(_geometry_lines(), FRONT_LABELS, "front")
    check("ngaysinh" in fields, "'ngay sinh' label not found.")
    if "ngaysinh" in fields:
        check(
            fields["ngaysinh"].value == EXPECTED_DOB,
            f"Wrong date of birth: {fields['ngaysinh'].value!r} (expected {EXPECTED_DOB!r})",
        )


def test_the_geometry_parser_reads_the_gender() -> None:
    fields = find_fields_geo(_geometry_lines(), FRONT_LABELS, "front")
    check("gioitinh" in fields, "'gioi tinh' label not found.")
    if "gioitinh" in fields:
        check(
            fields["gioitinh"].value == "Nam",
            f"Wrong gender: {fields['gioitinh'].value!r} (expected 'Nam')",
        )


def test_the_geometry_parser_does_not_read_the_card_header() -> None:
    fields = find_fields_geo(_geometry_lines(), FRONT_LABELS, "front")
    for key, field in fields.items():
        if key in ("socccd", "so"):
            check(
                field.value.isdigit(),
                f"The card header was read as the number: {field.value!r}",
            )


def test_the_geometry_parser_merges_the_address() -> None:
    fields = find_fields_geo(_geometry_lines(), FRONT_LABELS, "front")
    check("diachi" in fields, "The merged address is missing.")
    if "diachi" in fields:
        value = fields["diachi"].value
        check("Xuân Hng" in value, f"The place of origin is missing: {value!r}")
        check("M An" in value, f"The place of residence is missing: {value!r}")


def test_the_full_result_of_the_geometry_parser() -> None:
    fields = find_fields_geo(_geometry_lines(), FRONT_LABELS, "front")
    canonical = canonical_field_names(fields)

    check(
        canonical.get("documentNumber") is not None
        and canonical["documentNumber"].value == EXPECTED_NUMBER,
        f"Wrong document number in the result: {canonical.get('documentNumber')}",
    )
    check(
        canonical.get("fullName") is not None
        and canonical["fullName"].value == EXPECTED_NAME,
        f"Wrong full name in the result: {canonical.get('fullName')}",
    )
    check(
        canonical.get("dateOfBirth") is not None
        and canonical["dateOfBirth"].value == EXPECTED_DOB,
        f"Wrong date of birth in the result: {canonical.get('dateOfBirth')}",
    )
    check(
        canonical.get("gender") is not None
        and canonical["gender"].value == "Nam",
        f"Wrong gender in the result: {canonical.get('gender')}",
    )
    # The issue date is NOT printed on the front of a CCCD - it must not be faked
    # from the date of birth.
    check(
        "issueDate" not in canonical,
        f"The issue date was invented: {canonical.get('issueDate')}",
    )


def test_the_geometry_parser_reads_the_expiry_date_from_the_front() -> None:
    """On the real CCCD "CÓ GIÁ TRỊ ĐẾN" is printed on the FRONT, bottom-left.

    It used to be missing from FRONT_LABELS, so every real card came back without
    an expiry date.
    """
    fields = find_fields_geo(_geometry_lines(), FRONT_LABELS, "front")
    canonical = canonical_field_names(fields)

    check(
        canonical.get("expiryDate") is not None,
        f"The expiry date is missing: {canonical.get('expiryDate')}",
    )
    if canonical.get("expiryDate"):
        check(
            canonical["expiryDate"].value == EXPECTED_EXPIRY,
            f"Wrong expiry date: {canonical['expiryDate'].value!r} (expected {EXPECTED_EXPIRY!r})",
        )
        check(
            canonical["expiryDate"].source == "front",
            f"The expiry date must come from the front: {canonical['expiryDate'].source!r}",
        )


def test_the_geometry_parser_reads_the_issue_date_from_the_back() -> None:
    """On the real CCCD "NGÀY, THÁNG, NĂM" is printed on the BACK."""
    fields = find_fields_geo(_geometry_back_lines(), BACK_LABELS, "back")
    canonical = canonical_field_names(fields)

    check(
        canonical.get("issueDate") is not None,
        f"The issue date is missing: {canonical.get('issueDate')}",
    )
    if canonical.get("issueDate"):
        check(
            canonical["issueDate"].value == EXPECTED_ISSUE,
            f"Wrong issue date: {canonical['issueDate'].value!r} (expected {EXPECTED_ISSUE!r})",
        )
        check(
            canonical["issueDate"].source == "back",
            f"The issue date must come from the back: {canonical['issueDate'].source!r}",
        )


# ---------------------------------------------------------------------------
# The label matching itself.
# ---------------------------------------------------------------------------

def test_the_label_matching_survives_missing_vowels() -> None:
    # The OCR of a low-contrast photo drops the vowels: "Gii tính", "C gia trj dn".
    check(
        _label_matches(normalize_label("Gii tính / Sex: Nam"), "gioi tinh"),
        "'Gii tính' must match the label 'gioi tinh'.",
    )
    check(
        _label_matches(normalize_label("C gia trj dn:"), "co gia tri den"),
        "'C gia trj dn' must match the label 'co gia tri den'.",
    )


def test_the_label_matching_does_not_confuse_the_dates() -> None:
    # "ngay cap" (issue date) must NOT match "ngay sinh" (date of birth).
    check(
        not _label_matches(normalize_label("Ngày sinh / Date of birth:"), "ngay cap"),
        "'ngay cap' must not match 'ngay sinh'.",
    )


def test_the_label_matching_finds_the_bilingual_labels() -> None:
    check(
        _label_matches(normalize_label("Ho và tên / Full name:"), "ho va ten"),
        "The bilingual name label must match.",
    )
    check(
        _label_matches(normalize_label("Quê quán / Place of origin:"), "que quan"),
        "The bilingual origin label must match.",
    )


# ---------------------------------------------------------------------------
# The MRZ.
# ---------------------------------------------------------------------------

def test_the_mrz_finds_the_number_with_the_printed_number_as_anchor() -> None:
    mrz = parse_mrz(REAL_BACK_LINES, EXPECTED_NUMBER)
    check(
        mrz.get("mrzDocumentNumber") == EXPECTED_NUMBER,
        f"Wrong MRZ document number: {mrz.get('mrzDocumentNumber')!r}",
    )


def test_the_mrz_finds_a_number_without_an_anchor() -> None:
    """The MRZ is only a FALLBACK.

    The OCR of the MRZ is not trustworthy - the digits come out shifted, so the
    number read from the MRZ can be the date of birth. The printed number of the
    card therefore always wins, and the MRZ is only consulted when the front side
    holds no usable number.
    """
    mrz = parse_mrz(REAL_BACK_LINES)
    number = mrz.get("mrzDocumentNumber")
    check(number is not None, "No document number found in the MRZ.")
    if number:
        check(len(number) >= 9, f"MRZ number too short: {number!r}")
        check(
            number != EXPECTED_DOB.replace("/", ""),
            "The MRZ must not return the date of birth as the number.",
        )


def test_the_mrz_reads_the_name() -> None:
    mrz = parse_mrz(REAL_BACK_LINES)
    check(
        mrz.get("mrzName") == "NGUYEN MINH KHOI",
        f"Wrong MRZ name: {mrz.get('mrzName')!r}",
    )


# ---------------------------------------------------------------------------
# The plain-text parser (the fallback for a flat scan).
# ---------------------------------------------------------------------------

def test_the_plain_parser_does_not_read_the_card_header() -> None:
    fields = find_fields(REAL_FRONT_LINES, FRONT_LABELS, "front")
    for key in ("socccd", "so"):
        if key in fields:
            check(
                fields[key].value.isdigit(),
                f"The plain parser read the card header as {key}: {fields[key].value!r}",
            )


def test_the_printed_number_wins_over_the_mrz() -> None:
    """The MRZ must never overwrite a number that was read from the card.

    On a real photo the OCR of the MRZ shifts the digits, so the MRZ can hold the
    date of birth instead of the number. The printed value is authoritative.
    """
    front = find_fields_geo(_geometry_lines(), FRONT_LABELS, "front")
    printed = ""
    for key in ("socccd", "sochungminh", "socmnd", "so"):
        candidate = front.get(key)
        if candidate and candidate.value:
            printed = candidate.value
            break

    check(printed == EXPECTED_NUMBER, f"The printed number is wrong: {printed!r}")

    mrz = parse_mrz(REAL_BACK_LINES, printed)
    check(
        mrz.get("mrzDocumentNumber") == EXPECTED_NUMBER,
        "The MRZ must confirm the printed number.",
    )


def main() -> int:
    tests = [value for name, value in sorted(globals().items())
             if name.startswith("test_") and callable(value)]

    for test in tests:
        try:
            test()
            print(f"PASS  {test.__name__}")
        except Exception as exc:  # noqa: BLE001
            failures.append(f"{test.__name__}: {exc}")
            print(f"FAIL  {test.__name__}: {exc}")

    if failures:
        print(f"\n{len(failures)} FAILURE(S):")
        for failure in failures:
            print(f"  - {failure}")
        return 1

    print(f"\nALL PASSED ({len(tests)} tests)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
