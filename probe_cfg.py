import cloudinary, cloudinary.api as a, json
cloudinary.config(cloud_name="smbl2hal", api_key="932363835562534",
                  api_secret="88FIjuHGv9hsuVLxSayWa9_hYjo")
cfg = a.config()
for k in ("cloud_name", "private_cdn", "secure_distribution", "cdn_subdomain", "secure_cdn_subdomain"):
    print(k, "=", cfg.get(k))
print("--- usage ---")
try:
    print(json.dumps(a.usage(), indent=1)[:600])
except Exception as e:
    print("usage error:", e)
