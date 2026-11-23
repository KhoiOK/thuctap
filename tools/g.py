import pymupdf, re
doc = pymupdf.open(r"docs\SRS_He_thong_Quan_ly_User_v2_0-v2.0-ORIGINAL.pdf")
txt = "\n".join(doc[i].get_text() for i in range(doc.page_count))
doc.close()
for kw in ["6.1", "Cache-Control", "84", "ghi log", "nhật ký"]:
    for m in re.finditer(re.escape(kw), txt, re.I):
        s = max(0, m.start()-320); e = min(len(txt), m.end()+320)
        print("="*20, kw)
        print(txt[s:e].replace("\n"," "))
        break