#!/usr/bin/env python
"""Apply v2.1 amendments to the original SRS pages.

Edits applied to the 15 source pages:

1. Header version bump on page 1: "Phiên bản: 2.0" -> "Phiên bản: 2.1".
2. Footer version bump on every page: "SRS-UM-001 v2.0" -> "SRS-UM-001 v2.1".
   The old footer is removed with a redaction annotation (so it disappears
   from the text layer as well as visually) and the new one is drawn with
   the base-14 Helvetica, which extracts as clean ASCII.
3. Amendment markers: a small "[v2.1]" tag next to the headings of the
   clauses that section 9 amends.

The header needs Vietnamese glyphs, so a *subset* Calibri is used; using the
full system font there would embed ~1.7 MB and triple the file size.
"""
import os
import shutil

import pymupdf

BACKUP = os.path.abspath(r"docs\SRS_He_thong_Quan_ly_User_v2_0-v2.0-ORIGINAL.pdf")
OUT = os.path.abspath(r"docs\SRS_He_thong_Quan_ly_User_v2_0.pdf")
SUBSET_DIR = os.path.join(os.environ.get("TEMP", "."), "fontsubsets")
FONT = os.path.join(SUBSET_DIR, "calibri-vn-subset.ttf")

# Fallback if the subset has not been built yet.
if not os.path.exists(FONT):
    FONT = os.path.join(r"C:\Windows\Fonts", "calibri.ttf")

# (page index, heading text) -> clauses amended by section 9.
MARKERS = [
    (0, "1.1 Mục tiêu"),
    (0, "1.3 Kiến trúc tổng thể"),
    (0, "1.4 Backend API"),
    (1, "1.6 Cấu hình"),
    (4, "3.1 Quy ước"),
    (5, "3.3 Bảng IdentityDocuments"),
    (5, "3.5 Lưu trữ file ảnh"),
    (6, "4.2 Danh sách endpoint"),
    (9, "5.3 Chi tiết màn hình"),
    (11, "6.2 Validation"),
    (12, "7. Acceptance Criteria"),
    (13, "8.2 Các bước"),
]

TAG = "[v2.1]"
TAG_COLOR = (0.72, 0.11, 0.11)
TAG_SIZE = 8.0
GREY = (0.35, 0.35, 0.35)


def redact_text(page, rect):
    """Remove text under `rect` from both the page and the text layer."""
    page.add_redact_annot(rect, fill=(1, 1, 1))
    page.apply_redactions(images=pymupdf.PDF_REDACT_IMAGE_NONE)


def bump_header(doc):
    """Page 1: 'Phiên bản: 2.0' -> 'Phiên bản: 2.1'."""
    page = doc[0]
    hits = page.search_for("Phiên bản: 2.0")
    if not hits:
        print("  !! header version not found")
        return False
    r = hits[0]
    redact_text(page, pymupdf.Rect(r.x0 - 1, r.y0 - 2, r.x1 + 2, r.y1 + 2))
    # "Phiên bản" needs Vietnamese glyphs, so a subset Calibri is used here.
    page.insert_text((r.x0, r.y1 - 1.0), "Phiên bản: 2.1", fontsize=10.0,
                     fontname="calvn", fontfile=FONT, color=(0, 0, 0))
    return True


def bump_footers(doc):
    """Every page: 'SRS-UM-001 v2.0' -> 'SRS-UM-001 v2.1'."""
    count = 0
    for pno in range(doc.page_count):
        page = doc[pno]
        hits = page.search_for("SRS-UM-001 v2.0")
        if not hits:
            continue
        r = hits[0]
        redact_text(page, pymupdf.Rect(r.x0 - 1, r.y0 - 1.5, r.x1 + 1, r.y1 + 1.5))
        new = f"SRS-UM-001 v2.1  |  Trang {pno + 1}"
        font = pymupdf.Font(fontname="helv")
        width = font.text_length(new, fontsize=8.0)
        x0 = (page.rect.width - width) / 2
        page.insert_text((x0, r.y1 - 1.0), new, fontsize=8.0,
                         fontname="helv", color=GREY)
        count += 1
    return count


def add_markers(doc):
    added = 0
    for pno, heading in MARKERS:
        page = doc[pno]
        hits = page.search_for(heading)
        if not hits:
            print(f"  !! heading not found: {heading} (page {pno + 1})")
            continue
        r = hits[0]
        x = r.x1 + 3
        if x > page.rect.width - 60:
            x = r.x0 - 34
        page.insert_text((x, r.y1 - 1.5), TAG, fontsize=TAG_SIZE,
                         fontname="helv", color=TAG_COLOR)
        added += 1
    return added


def main():
    doc = pymupdf.open(BACKUP)
    ok = bump_header(doc)
    n_foot = bump_footers(doc)
    n_mark = add_markers(doc)
    print(f"header bumped: {ok} | footers bumped: {n_foot} | markers: {n_mark}")

    staged = os.path.join(os.environ["TEMP"], "srs_marked.pdf")
    doc.save(staged, garbage=4, deflate=True)
    doc.close()
    shutil.copy2(staged, OUT)
    os.remove(staged)
    print("saved", OUT)


if __name__ == "__main__":
    main()
