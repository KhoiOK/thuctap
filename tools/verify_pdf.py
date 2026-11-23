import pymupdf

doc = pymupdf.open(r"docs\SRS_He_thong_Quan_ly_User_v2_0.pdf")
print("pages:", doc.page_count)
print("metadata title:", doc.metadata["title"])
print("subject:", doc.metadata["subject"])
print()
toc = doc.get_toc()
print(f"--- TOC ({len(toc)} entries), last 10:")
for t in toc[-10:]:
    print("  ", t)

print()
print("--- footer check on every page:")
bad = []
for pno in range(doc.page_count):
    txt = doc[pno].get_text()
    want = f"SRS-UM-001 v2.1  |  Trang {pno+1}"
    if want not in txt.replace("\xa0", " "):
        bad.append(pno + 1)
print("  pages with wrong footer:", bad or "none")

print()
print("--- page 1 header line:")
for line in doc[0].get_text().splitlines()[:4]:
    if "Phiên bản" in line or "Mã:" in line:
        print("  ", line.strip())

print()
print("--- [v2.1] markers per page:")
for pno in range(15):
    n = doc[pno].get_text().count("[v2.1]")
    if n:
        print(f"   page {pno+1}: {n}")

print()
print("--- Vietnamese integrity check (looking for the U+019F ligature bug):")
full = "\n".join(doc[p].get_text() for p in range(doc.page_count))
print("   contains U+019F:", "\u019f" in full)
for probe in ("giấy tờ", "Bổ sung", "lưu trữ", "nghiệp vụ", "Cloudinary"):
    print(f"   {probe!r}: {'OK' if probe in full else 'MISSING'}")

print()
print("--- section 9 anchors:")
for h in ("9. Bổ sung", "9.1 Lý do", "9.5 Endpoint mới", "9.8 Điểm chưa rõ"):
    found = [pno + 1 for pno in range(doc.page_count) if doc[pno].search_for(h)]
    print(f"   {h!r}: page {found}")
doc.close()
