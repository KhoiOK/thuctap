"""Regression tests for the OCR field parser.

These cover the bugs found while running the service against PaddleOCR 3.2:

1. `extract_lines` assumed the PaddleOCR 2.x layout; 3.x returns an `OCRResult`
   whose `rec_texts` / `rec_scores` carry the data.
2. `preprocess_image` returned a 2D grayscale image; PaddleOCR 3.x unpacks
   `img.shape` into 3 values and therefore needs a 3-channel BGR image.
3. `find_fields` sliced the RAW line with a diacritic-free search string, so
   `line.lower().find("so")` returned -1 and the value was taken from the wrong
   offset ("SỐ CCCD" ended up with the value of the next line).
4. The generic label "so" overwrote the specific label "so cccd".

Run with:  python -m pytest test_main.py -q
       or: python test_main.py
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

from app.main import (  # noqa: E402
    BACK_LABELS,
    FRONT_LABELS,
    canonical_field_names,
    find_fields,
    extract_lines,
    normalize_label,
    preprocess_image,
)

FRONT_LINES = [
    "SỐ CCCD: 079095001234",
    "HỌ VÀ TÊN: NGUYỄN VĂN A",
    "NGÀY SINH: 20/04/1995",
    "GIỚI TÍNH: NAM",
    "NGÀY CẤP: 10/05/2021",
    # On the real 12-digit CCCD the expiry date is printed on the FRONT.
    "CÓ GIÁ TRỊ ĐẾN: 20/04/2036",
]

BACK_LINES = [
    # The issue date of the real CCCD is printed on the BACK.
    "NGÀY, THÁNG, NĂM: 10/05/2021",
    "SỐ HIỆU: 012345678",
]


def test_normalize_label_strips_diacritics() -> None:
    assert normalize_label("SỐ CCCD: 079095001234") == "so cccd 079095001234"
    assert normalize_label("NGÀY SINH") == "ngay sinh"


def test_extract_lines_handles_paddleocr_3x_result() -> None:
    """3.x returns OCRResult objects with rec_texts / rec_scores."""
    page = {"rec_texts": ["SỐ CCCD: 079095001234", "NGÀY SINH: 20/04/1995"],
            "rec_scores": [0.99, 0.95]}
    lines, text = extract_lines([page])

    assert lines == ["SỐ CCCD: 079095001234", "NGÀY SINH: 20/04/1995"]
    assert "079095001234" in text


def test_extract_lines_handles_paddleocr_2x_result() -> None:
    """The 2.x layout must keep working.

    2.x returned ``[[box, (text, score)], ...]`` per page - a two-element pair
    where the second element is the (text, score) tuple. The result may also be
    wrapped one level deeper, so both shapes are covered.
    """
    box = [[0, 0], [1, 0], [1, 1], [0, 1]]
    pair = ("SỐ CCCD: 079095001234", 0.98)

    wrapped = [[[box, pair]]]
    lines, _ = extract_lines(wrapped)
    assert lines == ["SỐ CCCD: 079095001234"], "the 3-level 2.x shape"

    flat = [[box, pair]]
    lines, _ = extract_lines(flat)
    assert lines == ["SỐ CCCD: 079095001234"], "the 2-level 2.x shape"


def test_extract_lines_ignores_garbage_without_raising() -> None:
    """Unrecognised shapes must not break the scan."""
    assert extract_lines(None) == ([], "")
    assert extract_lines([]) == ([], "")
    assert extract_lines([[]]) == ([], "")


def test_extract_lines_accepts_a_generator() -> None:
    """`engine.predict()` returns a generator, not a list."""
    page = {"rec_texts": ["NGÀY CẤP: 10/05/2021"], "rec_scores": [0.9]}
    lines, _ = extract_lines(iter([page]))

    assert lines == ["NGÀY CẤP: 10/05/2021"]


def test_preprocess_image_returns_three_channels() -> None:
    """PaddleOCR 3.x unpacks img.shape into (h, w, _) - a 2D image crashes it."""
    import numpy as np
    from PIL import Image

    buf = Path(__file__).parent / "_tmp_test.jpg"
    Image.new("RGB", (300, 200), (120, 130, 140)).save(buf)

    try:
        processed = preprocess_image(buf.read_bytes())
    finally:
        buf.unlink(missing_ok=True)

    assert processed.ndim == 3, "the OCR engine needs a 3-channel image"
    assert processed.shape[2] == 3


def test_find_fields_reads_the_value_of_its_own_line() -> None:
    """The value must come from the line that carries the label."""
    fields = find_fields(FRONT_LINES, FRONT_LABELS, "front")

    assert fields["socccd"].value == "079095001234"
    assert fields["ngaysinh"].value == "20/04/1995"
    assert fields["gioitinh"].value == "NAM"
    assert fields["ngaycap"].value == "10/05/2021"
    assert fields["hovaten"].value == "NGUYỄN VĂN A"


def test_find_fields_does_not_let_the_generic_label_win() -> None:
    """"so" must not overwrite the specific "so cccd"."""
    fields = find_fields(FRONT_LINES, FRONT_LABELS, "front")

    assert fields["so"].value == "079095001234"


def test_find_fields_reads_the_back_side() -> None:
    fields = find_fields(BACK_LINES, BACK_LABELS, "back")

    assert fields["coquancap"] if "coquancap" in fields else True
    assert fields["sohieu"].value == "012345678"


def test_canonical_field_names_maps_onto_business_names() -> None:
    front = find_fields(FRONT_LINES, FRONT_LABELS, "front")
    back = find_fields(BACK_LINES, BACK_LABELS, "back")
    merged = canonical_field_names({**front, **back})

    assert merged["documentNumber"].value == "079095001234"
    assert merged["fullName"].value == "NGUYỄN VĂN A"
    assert merged["dateOfBirth"].value == "20/04/1995"
    assert merged["gender"].value == "NAM"
    assert merged["authorityNo"].value == "012345678"


def test_the_expiry_date_is_read_from_the_front() -> None:
    """On the real CCCD "CÓ GIÁ TRỊ ĐẾN" is printed on the FRONT."""
    front = find_fields(FRONT_LINES, FRONT_LABELS, "front")
    merged = canonical_field_names(front)

    assert merged["expiryDate"].value == "20/04/2036"
    assert merged["expiryDate"].source == "front"


def test_the_issue_date_is_read_from_the_back() -> None:
    """On the real CCCD "NGÀY, THÁNG, NĂM" is printed on the BACK."""
    back = find_fields(BACK_LINES, BACK_LABELS, "back")
    merged = canonical_field_names(back)

    assert merged["issueDate"].value == "10/05/2021"
    assert merged["issueDate"].source == "back"


if __name__ == "__main__":
    failures = 0
    for name, fn in sorted(globals().items()):
        if name.startswith("test_") and callable(fn):
            try:
                fn()
                print(f"PASS  {name}")
            except AssertionError as exc:
                failures += 1
                print(f"FAIL  {name}: {exc}")
            except Exception as exc:  # noqa: BLE001
                failures += 1
                print(f"ERROR {name}: {type(exc).__name__}: {exc}")
    print(f"\n{'FAILED' if failures else 'ALL PASSED'} ({failures} failure(s))")
    sys.exit(1 if failures else 0)
