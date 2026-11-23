import pymupdf, re, io
doc = pymupdf.open(r"docs\SRS_He_thong_Quan_ly_User_v2_0-v2.0-ORIGINAL.pdf")
txt = "\n".join(doc[i].get_text() for i in range(doc.page_count))
doc.close()
for kw in ["no-cache","6.1","Truy cap anh","truy cập ảnh","nhat ky","Nhat ky"]:
    idxs = [m.start() for m in re.finditer(re.escape(kw), txt, re.I)]
    print("#"*25, kw, len(idxs))
    for i in idxs[:3]:
        print("   ...", txt[max(0,i-500):i+700].replace("\n"," "))
        print("   ---")