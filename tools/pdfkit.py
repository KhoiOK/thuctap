#!/usr/bin/env python
"""Shared PDF helpers: fonts, styles, page geometry, rendering."""
import os
import pymupdf

FONTS = r"C:\Windows\Fonts"

# Font files used for inserted content.
#
# NOTE: Calibri is avoided for body text because PyMuPDF's shaping engine turns
# the "ti" ligature into U+019F (Latin capital letter Ɵ), which corrupts text
# extraction. Arial renders Vietnamese correctly and is already embedded in the
# source PDF (ArialMT), so the visual result matches the original closely.
#
# The fonts are subset by tools/subset_fonts.py before rendering, which keeps
# the generated PDF roughly the size of the original.
FONT_FILES = {
    "ar": ("arial.ttf", "arialbd.ttf", "ariali.ttf"),
    "sg": ("segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf"),
    "cs": ("consola.ttf", "consolab.ttf", "consolai.ttf"),
}

# Where subset_fonts.py writes its output.
SUBSET_DIR = os.path.join(os.environ.get("TEMP", "."), "fontsubsets")

# Maps a source font file name to its subset file name.
SUBSET_NAMES = {
    "arial.ttf": "arial-subset.ttf",
    "arialbd.ttf": "arialbd-subset.ttf",
    "ariali.ttf": "ariali-subset.ttf",
    "consola.ttf": "consola-subset.ttf",
    "consolab.ttf": "consolai-subset.ttf",
    "segoeui.ttf": "segoeui-subset.ttf",
    "segoeuib.ttf": "segoeuib-subset.ttf",
    "segoeuii.ttf": "segoeuii-subset.ttf",
}

# Page geometry (A4, same as source).
PAGE_W, PAGE_H = 595.32, 841.92
MARGIN_L = 56.7
MARGIN_R = 56.7
CONTENT_W = PAGE_W - MARGIN_L - MARGIN_R
BODY_TOP = 58.0
BODY_BOTTOM = 780.0
FOOTER_Y = 796.0

# PyMuPDF Rect offsets are measured from the edges, so the bottom inset is
# PAGE_H - BODY_BOTTOM (not BODY_BOTTOM itself).
BOTTOM_INSET = PAGE_H - BODY_BOTTOM


def content_rect():
    """Rectangle of the usable content area on an A4 page."""
    return pymupdf.Rect(MARGIN_L, BODY_TOP, PAGE_W - MARGIN_R, BODY_BOTTOM)


def font_path(name):
    """Return the path to use for a font, preferring the subset version."""
    subset = os.path.join(SUBSET_DIR, SUBSET_NAMES.get(name, ""))
    if SUBSET_NAMES.get(name) and os.path.exists(subset):
        return subset
    return os.path.join(FONTS, name)


def make_archive(keys=("ar", "cs")):
    """Build a PyMuPDF archive with the requested font families."""
    archive = pymupdf.Archive()
    for key in keys:
        for fname in FONT_FILES[key]:
            path = font_path(fname)
            if not os.path.exists(path):
                continue
            with open(path, "rb") as fh:
                archive.add(fh.read(), path=fname)
    return archive


def css(family="ar", size=10.0, line_height=1.35):
    """CSS mirroring the source PDF typography (10pt body text)."""
    files = FONT_FILES[family]
    faces = [
        f"@font-face {{font-family: {family}; src: url({files[0]});}}",
        f"@font-face {{font-family: {family}; font-weight: bold; src: url({files[1]});}}",
        f"@font-face {{font-family: {family}; font-style: italic; src: url({files[2]});}}",
    ]
    mono = FONT_FILES["cs"]
    faces += [
        f"@font-face {{font-family: mono; src: url({mono[0]});}}",
        f"@font-face {{font-family: mono; font-weight: bold; src: url({mono[1]});}}",
    ]
    body = f"""
    body {{ font-family: {family}; font-size: {size}pt; line-height: {line_height};
           color: #000; margin: 0; }}
    h1 {{ font-size: 18pt; font-weight: bold; margin: 0 0 10pt 0; }}
    h2 {{ font-size: 15pt; font-weight: bold; margin: 0 0 10pt 0; }}
    h3 {{ font-size: 12pt; font-weight: bold; margin: 0 0 8pt 0; }}
    h4 {{ font-size: 10pt; font-weight: bold; margin: 0 0 6pt 0; }}
    p {{ margin: 0 0 6pt 0; text-align: justify; }}
    ul, ol {{ margin: 0 0 6pt 0; padding-left: 16pt; }}
    li {{ margin: 0 0 3pt 0; }}
    code {{ font-family: mono; font-size: {size - 1.5}pt; }}
    pre {{ font-family: mono; font-size: {size - 1.5}pt; background: #f2f2f2;
           padding: 5pt; margin: 0 0 6pt 0; white-space: pre-wrap; }}
    table {{ border-collapse: collapse; width: 100%; margin: 0 0 8pt 0;
             font-size: {size - 0.5}pt; }}
    th, td {{ border: 0.5pt solid #999; padding: 3pt 4pt; text-align: left;
              vertical-align: top; }}
    th {{ background: #e8e8e8; font-weight: bold; }}
    .note {{ background: #f0f4ff; border-left: 2.5pt solid #4472c4;
             padding: 5pt 7pt; margin: 0 0 8pt 0; }}
    .warn {{ background: #fff4e5; border-left: 2.5pt solid #d18b00;
             padding: 5pt 7pt; margin: 0 0 8pt 0; }}
    """
    return "\n".join(faces) + "\n" + body
