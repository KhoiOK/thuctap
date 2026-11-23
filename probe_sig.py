import base64, hashlib, urllib.request, urllib.error
import cloudinary, cloudinary.uploader

CN = "smbl2hal"
SECRET = "88FIjuHGv9hsuVLxSayWa9_hYjo"
PNG = ("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==")
cloudinary.config(cloud_name=CN, api_key="932363835562534", api_secret=SECRET)
ST = f"https://res.cloudinary.com/{CN}/image/authenticated"


def check(label, url):
    try:
        with urllib.request.urlopen(url, timeout=20) as r:
            print(f"  {r.status}  {label}")
            return r.status == 200
    except urllib.error.HTTPError as e:
        print(f"  {e.code}  {label}")
        return False


def sig8(s):
    return base64.urlsafe_b64encode(hashlib.sha1((s + SECRET).encode()).digest()).decode()[:8]


r = cloudinary.uploader.upload(f"data:image/png;base64,{PNG}",
                               public_id="user-management/tmp/n1/front-abc",
                               type="authenticated")
pid, fmt, ver = r["public_id"], r["format"], r["version"]
print(f"public_id={pid!r} format={fmt!r} version={ver}")

print("--- does the URL need the format extension? ---")
check("path=pid  sign=pid        ", f"{ST}/s--{sig8(pid)}--/{pid}")
check("path=pid  sign=pid.fmt    ", f"{ST}/s--{sig8(pid + '.' + fmt)}--/{pid}")
check("path=pid.fmt sign=pid.fmt ", f"{ST}/s--{sig8(pid + '.' + fmt)}--/{pid}.{fmt}")

print("--- SDK view of the same ---")
u, _ = cloudinary.utils.cloudinary_url(pid, type="authenticated", sign_url=True)
print("  SDK (no format):", u)
u2, _ = cloudinary.utils.cloudinary_url(pid, format=fmt, type="authenticated", sign_url=True)
print("  SDK (with fmt)  :", u2)
check("SDK no format ", u.replace("http://", "https://"))
check("SDK with fmt  ", u2.replace("http://", "https://"))

print("--- what does the upload response secure_url look like? ---")
print("  ", r["secure_url"])
