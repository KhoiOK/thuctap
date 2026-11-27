"""Dumps the raw PaddleOCR result (texts + polygons) for a real CCCD photo.

Run with: python dump_ocr.py <image-path> [output-json]

The polygons are needed to restore the PHYSICAL reading order: the OCR engine returns
the text lines in an arbitrary order, so "the next line" is not the value that belongs
to a label.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

from app.main import preprocess_image  # noqa: E402


def main() -> int:
    image_path = Path(sys.argv[1])
    output_path = Path(sys.argv[2]) if len(sys.argv) > 2 else None

    from paddleocr import PaddleOCR

    engine = PaddleOCR(
        lang="vi",
        use_doc_orientation_classify=False,
        use_doc_unwarping=False,
        use_textline_orientation=False,
    )

    result = list(engine.predict(preprocess_image(image_path.read_bytes())))

    dump = []
    for page in result:
        texts = page.get("rec_texts") or []
        scores = page.get("rec_scores") or []
        polys = page.get("rec_polys")

        for index, text in enumerate(texts):
            box = None
            if polys is not None and index < len(polys):
                poly = polys[index]
                try:
                    points = [[float(p[0]), float(p[1])] for p in poly]
                    xs = [p[0] for p in points]
                    ys = [p[1] for p in points]
                    box = {
                        "x": min(xs),
                        "y": min(ys),
                        "w": max(xs) - min(xs),
                        "h": max(ys) - min(ys),
                        "cx": (min(xs) + max(xs)) / 2,
                        "cy": (min(ys) + max(ys)) / 2,
                    }
                except (TypeError, IndexError, ValueError):
                    box = None

            dump.append({
                "index": index,
                "text": text,
                "score": float(scores[index]) if index < len(scores) else 0.0,
                "box": box,
            })

    payload = json.dumps(dump, ensure_ascii=False, indent=2)
    if output_path:
        output_path.write_text(payload, encoding="utf-8")
        print(f"wrote {output_path} ({len(dump)} lines)")
    else:
        print(payload)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
