"""Runs the Python field extraction on a real photo and prints what each label produced.

Used to debug which label produced which value.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

from app.main import (  # noqa: E402
    BACK_LABELS,
    FRONT_LABELS,
    _build_lines,
    canonical_field_names,
    find_fields_geo,
    preprocess_image,
)
from paddleocr import PaddleOCR  # noqa: E402


def main() -> int:
    engine = PaddleOCR(
        lang="vi",
        use_doc_orientation_classify=True,
        use_doc_unwarping=False,
        use_textline_orientation=False,
    )

    for side, labels in (("front", FRONT_LABELS), ("back", BACK_LABELS)):
        path = Path(sys.argv[1]) if side == "front" else Path(sys.argv[2])
        result = list(engine.predict(preprocess_image(path.read_bytes())))
        page = result[0]
        lines = _build_lines(result)
        print("  lines: %d" % len(lines))

        print("=== %s ===" % side)
        geo = find_fields_geo(lines, labels, side)
        for key, field in geo.items():
            print("  raw  %-22s = %r" % (key, field.value))

        canonical = canonical_field_names(geo)
        for key, field in canonical.items():
            print("  canon %-21s = %r (source=%s)" % (key, field.value, field.source))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
