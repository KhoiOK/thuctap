import json
import re
import sys
import urllib.request
import http.cookiejar
import uuid

BASE = "http://localhost:5052"
front = sys.argv[1]
back = sys.argv[2] if len(sys.argv) > 2 else None

jar = http.cookiejar.CookieJar()
opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))

html = opener.open(BASE + "/Users/QuickCreate").read().decode("utf-8", "replace")
m = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', html)
if not m:
    m = re.search(r'value="([^"]+)"[^>]*name="__RequestVerificationToken"', html)
token = m.group(1)
print("token len:", len(token))

boundary = "----CursorBoundary" + uuid.uuid4().hex
nl = "\r\n"
parts = []


def field(name, value):
    parts.append(f"--{boundary}")
    parts.append(f'Content-Disposition: form-data; name="{name}"')
    parts.append("")
    parts.append(value)


def file_field(name, path):
    parts.append(f"--{boundary}")
    parts.append(f'Content-Disposition: form-data; name="{name}"; filename="{path.split("/")[-1].split(chr(92))[-1]}"')
    parts.append("Content-Type: image/jpeg")
    parts.append("")
    with open(path, "rb") as fh:
        parts.append(fh.read().decode("latin-1"))


field("documentType", "CCCD")
field("__RequestVerificationToken", token)
file_field("frontImage", front)
if back:
    file_field("backImage", back)
parts.append(f"--{boundary}--")

body = (nl.join(parts)).encode("latin-1")

req = urllib.request.Request(
    BASE + "/Users/QuickCreate/OcrPreview",
    data=body,
    headers={"Content-Type": f"multipart/form-data; boundary={boundary}"},
)
try:
    resp = opener.open(req)
    print("HTTP", resp.status)
    print(resp.read().decode("utf-8", "replace"))
except urllib.error.HTTPError as e:
    print("HTTP", e.code)
    print(e.read().decode("utf-8", "replace"))
