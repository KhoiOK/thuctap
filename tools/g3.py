import pymupdf, re
doc = pymupdf.open(r"docs\SRS_He_thong_Quan_ly_User_v2_0-v2.0-ORIGINAL.pdf")
txt = "\n".join(doc[i].get_text() for i in range(doc.page_count))
doc.close()
i = txt.find("6.1")
# find the section header 6.1 in the later part (implementation constraints)
for m in re.finditer(r"6\.1\s", txt):
    seg = txt[m.start():m.start()+1200]
    if "log" in seg.lower() or "ghi" in seg.lower():
        print("="*30, m.start())
        print(seg.replace("\n"," "))