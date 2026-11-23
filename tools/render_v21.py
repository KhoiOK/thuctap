import pymupdf, os

PDF = r"docs\SRS_He_thong_Quan_ly_User_v2_0.pdf"
doc = pymupdf.open(PDF)
outdir = os.path.join(os.environ["TEMP"], "srs_v21")
os.makedirs(outdir, exist_ok=True)
for i in (0, 5, 15, 16, 17):
    pix = doc[i].get_pixmap(dpi=115)
    p = os.path.join(outdir, f"p{i+1}.png")
    pix.save(p)
    print(p)
doc.close()
