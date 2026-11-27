"""Generate a synthetic Vietnamese CCCD front/back image for smoke-testing the OCR path.

OpenCV's `putText` cannot render Vietnamese diacritics (they need a TTF font), so
the text is drawn with Pillow using a Unicode-capable font. The image contains the
exact labels the service parses ("SỐ CCCD", "HỌ VÀ TÊN", ...) so the end-to-end OCR
call can be verified without a real ID card.
"""
from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

# Candidate fonts with Vietnamese glyph coverage.
FONT_CANDIDATES = [
    r"C:\Windows\Fonts\arial.ttf",
    r"C:\Windows\Fonts\calibri.ttf",
    r"C:\Windows\Fonts\segoeui.ttf",
    r"C:\Windows\Fonts\tahoma.ttf",
    r"C:\Windows\Fonts\verdana.ttf",
]


def load_font(size: int = 44) -> ImageFont.FreeTypeFont:
    for candidate in FONT_CANDIDATES:
        if Path(candidate).exists():
            print(f"using font {candidate}")
            return ImageFont.truetype(candidate, size)
    raise FileNotFoundError("No Unicode-capable font found on this machine.")


def make_card(lines: list[str], path: str, w: int = 1400, h: int = 900) -> None:
    font = load_font()
    img = Image.new("RGB", (w, h), (245, 245, 245))
    draw = ImageDraw.Draw(img)
    draw.rectangle([20, 20, w - 20, h - 20], outline=(40, 40, 40), width=3)

    y = 90
    for line in lines:
        draw.text((70, y), line, font=font, fill=(20, 20, 20))
        y += 80

    img.save(path, quality=95)
    print(f"wrote {path}")


if __name__ == "__main__":
    front = sys.argv[1] if len(sys.argv) > 1 else "front.jpg"
    back = sys.argv[2] if len(sys.argv) > 2 else "back.jpg"

    make_card(
        [
            "SỐ CCCD: 079095001234",
            "HỌ VÀ TÊN: NGUYỄN VĂN A",
            "NGÀY SINH: 20/04/1995",
            "GIỚI TÍNH: NAM",
            "NGÀY CẤP: 10/05/2021",
        ],
        front,
    )
    make_card(
        [
            "CÓ GIÁ TRỊ ĐẾN NGÀY: 20/04/2036",
            "SỐ HIỆU: 012345678",
        ],
        back,
    )
