#!/usr/bin/env python
"""Subset the fonts used by the generated section so the PDF stays small.

PyMuPDF embeds whole system fonts when rendering HTML, which adds roughly
2 MB to the output. This script builds a subset containing only the glyphs
that section 9 actually uses, so the final PDF is close to the size of the
original.
"""
import os
import sys

from fontTools import subset
from fontTools.ttLib import TTFont

FONTS = r"C:\Windows\Fonts"

# Font file -> subset output name.
TARGETS = [
    ("arial.ttf", "arial-subset.ttf"),
    ("arialbd.ttf", "arialbd-subset.ttf"),
    ("ariali.ttf", "ariali-subset.ttf"),
    ("consola.ttf", "consola-subset.ttf"),
    ("consolab.ttf", "consolai-subset.ttf"),
    ("calibri.ttf", "calibri-vn-subset.ttf"),
    ("calibrib.ttf", "calibrib-vn-subset.ttf"),
]


def text_of_section():
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from section9 import SECTION
    return SECTION


def build(chars, outdir, extra_text=()):
    """Subset each target font to `chars`. Returns {src_name: out_path}."""
    os.makedirs(outdir, exist_ok=True)
    results = {}
    for src_name, out_name in TARGETS:
        src = os.path.join(FONTS, src_name)
        if not os.path.exists(src):
            continue
        out = os.path.join(outdir, out_name)
        options = subset.Options()
        options.layout_features = ["*"]
        options.name_IDs = ["*"]
        options.notdef_outline = True
        options.recalc_bounds = True
        options.drop_tables += ["DSIG"]
        options.desubroutinize = True
        font = TTFont(src)
        subsetter = subset.Subsetter(options=options)
        subsetter.populate(text=chars)
        subsetter.subset(font)
        font.flavor = None
        font.save(out)
        font.close()
        results[src_name] = out
    return results


if __name__ == "__main__":
    section = text_of_section()
    chars = set(section)
    # Always keep the ASCII range and common punctuation.
    chars |= set(chr(c) for c in range(0x20, 0x7F))
    chars |= set("–—’‘“”→×°•…«»")
    # Text that tools/apply_markers.py draws into the original pages.
    chars |= set("Phiên bản: 2.1SRS-UM-001 v2.1  |  Trang [v2.1]")
    chars = "".join(sorted(chars))

    outdir = os.path.join(os.environ["TEMP"], "fontsubsets")
    results = build(chars, outdir)
    print(f"subset {len(results)} fonts for {len(chars)} unique characters")
    for src, out in results.items():
        print(f"   {src:16} {os.path.getsize(out):>8} bytes -> {out}")
