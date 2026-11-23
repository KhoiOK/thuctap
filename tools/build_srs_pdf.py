#!/usr/bin/env python
"""Build SRS v2.1 PDF: original 15 pages + new section 9.

Pipeline
--------
1. tools/apply_markers.py rewrites the 15 original pages in place:
   header/footer version bump and [v2.1] amendment markers.
2. This script renders section 9 into fresh A4 pages and appends them.
3. Footers, metadata and the table of contents are updated.

Run:  python tools/build_srs_pdf.py
"""
import os
import shutil
import subprocess
import sys

import pymupdf

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pdfkit import (MARGIN_L, MARGIN_R, BODY_TOP, BOTTOM_INSET, PAGE_W, PAGE_H,
                    css, make_archive)
from section9 import SECTION

HERE = os.path.dirname(os.path.abspath(__file__))
DOCS = os.path.abspath("docs")
BACKUP = os.path.join(DOCS, "SRS_He_thong_Quan_ly_User_v2_0-v2.0-ORIGINAL.pdf")
OUT = os.path.join(DOCS, "SRS_He_thong_Quan_ly_User_v2_0.pdf")

FOOTER = "SRS-UM-001 v2.1  |  Trang {}"
FOOTER_FONT = os.path.join(r"C:\Windows\Fonts", "calibri.ttf")

SUBSECTIONS = [
    ("9.1 Lý do", 2, "9.1 Lý do"),
    ("9.2 Yêu cầu chức năng mới", 2, "9.2 Yêu cầu chức năng mới"),
    ("9.3 Quy tắc nghiệp vụ mới", 2, "9.3 Quy tắc nghiệp vụ mới"),
    ("9.4 Thay đổi các điều khoản hiện có", 2, "9.4 Thay đổi các điều khoản hiện có"),
    ("9.5 Endpoint mới", 2, "9.5 Endpoint mới"),
    ("9.6 Môi trường và rủi ro", 2, "9.6 Môi trường và rủi ro"),
    ("9.7 Bước triển khai bổ sung", 2, "9.7 Bước triển khai bổ sung"),
    ("9.8 Điểm chưa rõ cần xác nhận", 2, "9.8 Điểm chưa rõ cần xác nhận"),
]


def ensure_backup():
    """Keep an untouched copy of the v2.0 document."""
    if not os.path.exists(BACKUP):
        shutil.copy2(OUT, BACKUP)
        print("backup created ->", BACKUP)


def subset_fonts():
    """Step 1: build font subsets so the embedded fonts stay small."""
    print("--- step 1: subset fonts")
    subprocess.run([sys.executable, os.path.join(HERE, "subset_fonts.py")],
                   check=True, stdout=subprocess.DEVNULL)


def apply_markers():
    """Step 2: header/footer version bump and amendment markers."""
    ensure_backup()
    print("--- step 2: apply markers to the 15 original pages")
    subprocess.run([sys.executable, os.path.join(HERE, "apply_markers.py")],
                   check=True)


def render_section(archive, css_text, tmp_path):
    """Render the section 9 HTML into a standalone PDF."""
    story = pymupdf.Story(html=f"<html><body>{SECTION}</body></html>",
                          user_css=css_text, archive=archive)
    writer = pymupdf.DocumentWriter(tmp_path)
    mediabox = pymupdf.paper_rect("a4")
    where = mediabox + (MARGIN_L, BODY_TOP, -MARGIN_R, -BOTTOM_INSET)
    pages = 0
    more = 1
    while more:
        dev = writer.begin_page(mediabox)
        more, _ = story.place(where)
        story.draw(dev)
        writer.end_page()
        pages += 1
    writer.close()
    return pages


def locate(src, first_new, total, needle):
    """Return the 1-based page number where needle first appears."""
    for i in range(first_new, total):
        if src[i].search_for(needle):
            return i + 1
    return first_new + 1


def main():
    subset_fonts()
    apply_markers()

    print("--- step 3: render section 9")
    archive = make_archive()
    css_text = css(family="ar", size=10.0)
    tmp = os.path.join(os.environ["TEMP"], "srs_section9.pdf")
    if os.path.exists(tmp):
        os.remove(tmp)
    n_new = render_section(archive, css_text, tmp)
    print("new pages rendered:", n_new)

    print("--- step 4: append and finalize")
    src = pymupdf.open(OUT)
    total_before = src.page_count
    new_doc = pymupdf.open(tmp)
    src.insert_pdf(new_doc, from_page=0, to_page=n_new - 1, annots=False, links=False)
    new_doc.close()

    total = src.page_count
    first_new = total_before

    for i in range(first_new, total):
        # ASCII-only footer, so the base-14 Helvetica is used: it extracts as
        # clean text without non-breaking spaces.
        txt = FOOTER.format(i + 1)
        font = pymupdf.Font(fontname="helv")
        width = font.text_length(txt, fontsize=8.0)
        x0 = (PAGE_W - width) / 2
        src[i].insert_text((x0, 806.4), txt, fontsize=8.0,
                           fontname="helv", color=(0.35, 0.35, 0.35))

    src.set_metadata({
        "title": "SRS – Hệ thống Quản lý User v2.1",
        "author": "Nguyễn Minh Khôi",
        "subject": "SRS-UM-001 v2.1 – bổ sung OCR giấy tờ và lưu trữ ảnh Cloudinary",
        "keywords": "SRS, User Management, OCR, PaddleOCR, Cloudinary, CCCD",
        "creator": "Microsoft Word 2016",
        "producer": "PyMuPDF",
    })

    toc = src.get_toc()
    entries = [[1, "9. Bổ sung – OCR giấy tờ và lưu trữ ảnh Cloudinary (v2.1)",
                first_new + 1]]
    for needle, level, label in SUBSECTIONS:
        entries.append([level, label, locate(src, first_new, total, needle)])
    toc.extend(entries)
    src.set_toc(toc)

    # PyMuPDF cannot save over the file it has open, so write to a temp file
    # and move it into place afterwards.
    staged = os.path.join(os.environ["TEMP"], "srs_v21_staged.pdf")
    src.save(staged, garbage=4, deflate=True)
    src.close()
    shutil.copy2(staged, OUT)
    os.remove(staged)
    print(f"saved -> {OUT}  pages: {total}")

    chk = pymupdf.open(OUT)
    print("verify pages:", chk.page_count, "| toc entries:", len(chk.get_toc()))
    chk.close()


if __name__ == "__main__":
    main()
