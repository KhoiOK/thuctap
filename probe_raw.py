import base64, hashlib, hmac, urllib.request, urllib.error, urllib.parse, time, json
import cloudinary, cloudinary.uploader

CN = "smbl2hal"
AK = "932363835562534"
SECRET = "88FIjuHGv9hsuVLxSayWa9_hYjo"
PNG = ("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==")
cloudinary.config(cloud_name=CN, api_key=AK, api_secret=SECRET)
ST = f"https://res.cloudinary.com/{CN}/image/authenticated"


def sig8(s):
    return base64.urlsafe_b64encode(hashlib.sha1((s + SECRET).encode()).digest()).decode()[:8]


def api_sign(params):
    """Cloudinary API request signature (hex SHA1 of sorted params + secret)."""
    payload = "&".join(f"{k}={v}" for k, v in sorted(params.items()) if v not in (None, ""))
    return hashlib.sha1((payload + SECRET).encode()).hexdigest()


def post(endpoint, params, files=None):
    params = {k: v for k, v in params.items() if v is not None}
    params["api_key"] = AK
    params["signature"] = api_sign(params)
    boundary = "----probe123"
    body = b""
    for k, v in params.items():
        body += f"--{boundary}\r\nContent-Disposition: form-data; name=\"{k}\"\r\n\r\n{v}\r\n".encode()
    for k, (fn, data, ct) in (files or {}).items():
        body += f"--{boundary}\r\nContent-Disposition: form-data; name=\"{k}\"; filename=\"{fn}\"\r\nContent-Type: {ct}\r\n\r\n".encode()
        body += data + b"\r\n"
    body += f"--{boundary}--\r\n".encode()
    req = urllib.request.Request(
        f"https://api.cloudinary.com/v1_1/{CN}/{endpoint}",
        data=body,
        headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()[:400]


print("=== 1) RAW REST upload with type=authenticated + explicit public_id (no ext) ===")
st, resp = post("image/upload",
                 {"timestamp": str(int(time.time())), "folder": "user-management/tmp/raw",
                  "public_id": "front-abc123", "type": "authenticated",
                  "use_filename": "false", "unique_filename": "true"},
                 {"file": ("front-abc123.png", base64.b64decode(PNG), "image/png")})
print("  status:", st)
print("  resp:", json.dumps(resp)[:300] if isinstance(resp, dict) else resp)
pid = resp.get("public_id") if isinstance(resp, dict) else None
fmt = resp.get("format") if isinstance(resp, dict) else None
ver = resp.get("version") if isinstance(resp, dict) else None

print("=== 2) signed URL without extension ===")
url_noext = f"{ST}/s--{sig8(pid)}--/{pid}"
url_ext = f"{ST}/s--{sig8(pid + '.' + fmt)}--/{pid}.{fmt}"
for label, u in [("no ext", url_noext), ("with ext", url_ext)]:
    try:
        with urllib.request.urlopen(u, timeout=20) as r:
            body = r.read()
            print(f"  {r.status}  {label}  ct={r.headers.get('Content-Type')} len={len(body)}")
    except urllib.error.HTTPError as e:
        print(f"  {e.code}  {label}")

print("=== 3) RAW REST rename with type=authenticated ===")
target = "user-management/identity-documents/uuid-1/doc-1/front-abc123"
st2, r2 = post("image/rename",
               {"timestamp": str(int(time.time())), "from_public_id": pid,
                "to_public_id": target, "type": "authenticated", "overwrite": "false"})
print("  status:", st2, "resp:", json.dumps(r2)[:250] if isinstance(r2, dict) else r2)
newpid = r2.get("public_id") if isinstance(r2, dict) else target
newfmt = r2.get("format") if isinstance(r2, dict) else fmt
try:
    with urllib.request.urlopen(f"{ST}/s--{sig8(newpid)}--/{newpid}", timeout=20) as r:
        print(f"  {r.status}  renamed no-ext signed")
except urllib.error.HTTPError as e:
    print(f"  {e.code}  renamed no-ext signed")

print("=== 4) RAW REST destroy with type=authenticated ===")
st3, r3 = post("image/destroy", {"timestamp": str(int(time.time())), "public_id": newpid,
                                  "type": "authenticated", "invalidate": "true"})
print("  status:", st3, "resp:", json.dumps(r3)[:250] if isinstance(r3, dict) else r3)
time.sleep(2)
try:
    with urllib.request.urlopen(f"{ST}/s--{sig8(newpid)}--/{newpid}?cb={int(time.time())}", timeout=20) as r:
        print(f"  {r.status}  STILL THERE after destroy")
except urllib.error.HTTPError as e:
    print(f"  {e.code}  gone after destroy")

print("=== 5) destroy WITHOUT type (old behaviour) ===")
st4, r4 = post("image/destroy", {"timestamp": str(int(time.time())), "public_id": pid, "invalidate": "true"})
print("  status:", st4, "resp:", json.dumps(r4)[:250] if isinstance(r4, dict) else r4)
