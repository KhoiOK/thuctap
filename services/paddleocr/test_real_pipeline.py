"""Runs the full field-extraction pipeline on a real CCCD photo and prints the result.

Run with: python test_real_pipeline.py <front-image> [back-image]
"""
from __future__ import annotations

import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

from app.main import (  # noqa: E402
    BACK_LABELS,
    FRONT_LABELS,
    _build_lines,
    canonical_field_names,
    find_fields_geo,
    get_engine,
    parse_mrz,
    preprocess_image,
)


def main() -> int:
    front_path = Path(sys.argv[1])
    back_path = Path(sys.argv[2]) if len(sys.argv) > 2 else None

    engine = get_engine()

    print("=== FRONT ===")
    front_result = list(engine.predict(preprocess_image(front_path.read_bytes())))
    front_lines = _build_lines(front_result)
    for line in sorted(front_lines, key=lambda l: (l.cy, l.cx)):
        print(
            f"  y={line.y0:7.1f}..{line.y1:7.1f} "
            f"x={line.x0:7.1f}..{line.x1:7.1f}  {line.text!r}"
        )

    front_fields = find_fields_geo(front_lines, FRONT_LABELS, "front")
    print("\n  fields:", {k: v.value for k, v in front_fields.items()})

    back_lines = []
    back_fields = {}
    mrz = {}
    if back_path:
        print("\n=== BACK ===")
        back_result = list(engine.predict(preprocess_image(back_path.read_bytes())))
        back_lines = _build_lines(back_result)
        for line in sorted(back_lines, key=lambda l: (l.cy, l.cx)):
            print(
                f"  y={line.y0:7.1f}..{line.y1:7.1f} "
                f"x={line.x0:7.1f}..{line.x1:7.1f}  {line.text!r}"
            )
        back_fields = find_fields_geo(back_lines, BACK_LABELS, "back")
        print("\n  fields:", {k: v.value for k, v in back_fields.items()})

        printed = ""
        for key in ("socccd", "sochungminh", "socmnd", "so"):
            candidate = front_fields.get(key)
            if candidate and candidate.value:
                printed = candidate.value
                break
        mrz = parse_mrz([line.text for line in back_lines], printed)
        print("  mrz:", mrz)

    fields = canonical_field_names({**front_fields, **back_fields})

    if mrz.get("mrzDocumentNumber"):
        current = fields.get("documentNumber")
        if current is None or not any(ch.isdigit() for ch in current.value or ""):
            from app.main import OcrField

            fields["documentNumber"] = OcrField(
                value=mrz["mrzDocumentNumber"], confidence=0.95, source="back",
                boundingBox=None,
            )

    print("\n=== RESULT ===")
    for key, field in fields.items():
        print(f"  {key:16} = {field.value!r}  (source={field.source})")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
