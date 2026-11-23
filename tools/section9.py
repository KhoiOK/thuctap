# -*- coding: utf-8 -*-
"""HTML for the new SRS section 9 (OCR + Cloudinary).

The section is split into four chunks so each file stays a comfortable size
for the editor. Edit the parts, then run tools/build_srs_pdf.py to regenerate
the PDF.
"""

from sec9_a import PART_A
from sec9_b import PART_B
from sec9_c import PART_C
from sec9_d import PART_D

SECTION = PART_A + PART_B + PART_C + PART_D
