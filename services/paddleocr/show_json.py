"""Pretty-prints the JSON of a curl call to an OCR endpoint.

Run with: python show_json.py <url> [field1=@file field2=@file ...]
"""
from __future__ import annotations

import json
import subprocess
import sys


def main() -> int:
    url = sys.argv[1]
    args = ["curl.exe", "-s", "-X", "POST", url]
    for extra in sys.argv[2:]:
        args += ["-F", extra]

    output = subprocess.run(args, capture_output=True).stdout.decode("utf-8", "replace")
    try:
        data = json.loads(output)
    except json.JSONDecodeError:
        print(output)
        return 1

    fields = data.get("fields", {})
    if fields:
        print("fields:")
        for key, value in fields.items():
            print(f"  {key:16} = {value.get('value')!r}  (source={value.get('source')})")
    else:
        print(json.dumps(data, ensure_ascii=False, indent=2)[:1500])

    if data.get("warnings"):
        print("warnings:", data["warnings"])

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
