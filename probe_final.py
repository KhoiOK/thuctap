import base64, hashlib, urllib.request, urllib.error, time, json
import cloudinary, cloudinary.api

CN = "smbl2hal"
AK = "932363835562534"
SECRET = "88FIjuHGv9hsuVLxSayWa9_hYjo"
PNG = ("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==")
ST = f"https://res.cloudinary.com/{CN}/image/authenticated"
cloudinary.config(cloud_name=CN, api_key=AK, api_secret=SECRET)


def api_sign(params):
    payload = "&".join(f"{k}={v}" for k, v in sorted(params.items())
                       if v not in (None, "") and k not in ("api_key", "file", "signature"))
    return hashlib.sha1((payload + SECRET).encode()).hexdigest()


def url_sig(s):
    return base64.urlsafe_b64encode(hashlib.sha1((s + SECRET).encode()).digest()).decode()[:8]


def post(endpoint, params, files=None):
    params = {k: v for k, v in params.items() if v is not None}
    params["signature"] = api_sign(params)
    params["api_key"] = AK
    boundary = "----probe123"
    body = b""
    for k, v in params.items():
        body += f"--{boundary}\r\nContent-Disposition: form-data; name=\"{k}\"\r\n\r\n{v}\r\n".encode()
    for k, (fn, data, ct) in (files or {}).items():
        body += (f"--{boundary}\r\nContent-Disposition: form-data; name=\"{k}\"; filename=\"{fn}\"\r\n"
                 f"Content-Type: {ct}\r\n\r\n").encode() + data + b"\r\n"
    body += f"--{boundary}--\r\n".encode()
    req = urllib.request.Request(f"https://api.cloudinary.com/v1_1/{CN}/{endpoint}", data=body,
                                 headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()[:300]


def check(label, url):
    try:
        with urllib.request.urlopen(url, timeout=20) as r:
            r.read()
            print(f"  {r.status}  {label}")
            return True
    except urllib.error.HTTPError as e:
        print(f"  {e.code}  {label}")
        return False


def admin(prefix):
    res = cloudinary.api.resources(prefix=prefix, type="authenticated", max_results=100)
    return [(x.get("public_id"), x.get("asset_id", "")[:8]) for x in res.get("resources", [])]


print("=== CLEANUP leftover probe assets ===")
for pfx in ("user-management/tmp/lifecycle-test", "user-management/tmp/probe",
            "user-management/tmp/probeA", "user-management/tmp/probeB", "user-management/tmp/probeT",
            "user-management/tmp/p1", "user-management/tmp/p2", "user-management/tmp/p3",
            "user-management/tmp/p4", "user-management/tmp/q1", "user-management/tmp/q2",
            "user-management/tmp/n1", "user-management/tmp/mv", "user-management/tmp/warm",
            "user-management/tmp/e1", "user-management/tmp/final", "user-management/tmp/raw",
            "user-management/identity-documents/00000000-0000-0000-0000-000000000000",
            "user-management/identity-documents/uuid1/doc1", "user-management/identity-documents/u1/d1",
            "user-management/identity-documents/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222"):
    for pid, aid in admin(pfx):
        st, r = post("image/destroy", {"public_id": pid, "timestamp": str(int(time.time())),
                                        "invalidate": "true", "type": "authenticated"})
        print(f"  destroy {st} {pid}")
time.sleep(2)

print("\n=== DEFINITIVE move test with a UNIQUE name ===")
UNIQ = "front-" + hashlib.md5(str(time.time()).encode()).hexdigest()
FOLDER = "user-management/tmp/lifecycle-test"
st, up = post("image/upload", {
    "allowed_formats": "jpg,png", "folder": FOLDER, "public_id": UNIQ,
    "timestamp": str(int(time.time())), "use_filename": "false", "unique_filename": "false",
    "type": "authenticated",
}, {"file": (f"{UNIQ}.png", base64.b64decode(PNG), "image/png")})
pid = up["public_id"]
print("uploaded:", pid)
print("admin tmp:", admin(FOLDER))

old_url = f"{ST}/s--{url_sig(pid)}--/{pid}"
check("download old BEFORE move", old_url)

TARGET = f"user-management/identity-documents/00000000-0000-0000-0000-000000000000/{UNIQ}"
st2, mv = post("image/rename", {
    "from_public_id": pid, "to_public_id": TARGET, "overwrite": "false",
    "timestamp": str(int(time.time())), "invalidate": "true", "type": "authenticated",
})
print("move:", st2, mv.get("public_id"))
newpid = mv["public_id"]

time.sleep(2)
print("admin tmp after move:", admin(FOLDER))
print("admin dest after move:", [p for p in admin("user-management/identity-documents/00000000-0000-0000-0000-000000000000") if UNIQ in p[0]])
check("old url AFTER move (no bust)", old_url)
check("old url AFTER move (bust)   ", old_url + f"?cb={int(time.time())}")
check("new url AFTER move          ", f"{ST}/s--{url_sig(newpid)}--/{newpid}")

print("--- destroy new ---")
st3, ds = post("image/destroy", {"public_id": newpid, "timestamp": str(int(time.time())),
                                  "invalidate": "true", "type": "authenticated"})
print("destroy:", st3, ds.get("result"))
time.sleep(2)
check("new url after destroy", f"{ST}/s--{url_sig(newpid)}--/{newpid}?cb={int(time.time())}")
