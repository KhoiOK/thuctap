"""PaddleOCR service - FastAPI wrapper for Vietnamese identity document OCR.

The service only reads pixels: it returns the raw text per side plus the recognized
field labels. The interpretation of those labels (which field is "Số giấy tờ", how to
normalize dates, ...) lives in the .NET backend, which is the single source of truth
(see `docs/OCR-Cloudinary-Spec.md` §3.3).
"""

from __future__ import annotations

import logging
import re
import time
from typing import Any, Dict, List, Optional

import numpy as np
from fastapi import FastAPI, File, Form, UploadFile
from fastapi.responses import JSONResponse
from pydantic import BaseModel

logger = logging.getLogger("paddleocr")
logging.basicConfig(level=logging.INFO)

ENGINE_VERSION = "paddleocr-3.2.0"

app = FastAPI(
    title="PaddleOCR Service",
    version=ENGINE_VERSION,
    description="OCR service for Vietnamese CCCD / CMND two-sided scanning.",
)

# Label patterns (without diacritics, lower case) used to split the raw text into fields.
#
# The Vietnamese CCCD is printed BILINGUALLY: the label reads e.g. "Họ và tên / Full name".
# The English translation is separated from the Vietnamese label by " / ", so only the
# Vietnamese part is kept here - `find_fields` matches the label at the START of the
# line and the translation simply trails behind it.
FRONT_LABELS = [
    "so cccd",
    "so chung minh",
    "so cmnd",
    "so",
    "ho va ten",
    "ngay sinh",
    "gioi tinh",
    "dia chi",
    "noi thuong tru",
    "que quan",
    "ngay cap",
    # On the real 12-digit CCCD the expiry date is printed on the FRONT, in the
    # bottom-left corner: "CÓ GIÁ TRỊ ĐẾN: 05/10/2028" (plus the English
    # "Date of expiry"). It used to be missing here, so every real card came
    # back without an expiry date.
    "co gia tri den ngay",
    "co gia tri den",
    "gia tri den ngay",
    "gia tri den",
    "ngay het han",
    "date of expiry",
]

BACK_LABELS = [
    # The issue date of the real CCCD is printed on the BACK as
    # "Ngày, tháng, năm / Date, month, year: 22/04/2021".
    "ngay thang nam",
    "so hieu",
    "co quan cap",
    # The expiry labels are also searched on the back: older card layouts print
    # "Có giá trị đến" there instead of on the front. The side that actually
    # produced the value is reported as the field's source, so searching both
    # sides stays correct for every layout.
    "co gia tri den ngay",
    "co gia tri den",
    "gia tri den ngay",
    "gia tri den",
    "ngay het han",
    "date of expiry",
]

# The generic label "so" (số) must never win over a real "Số CCCD". It is also far too
# weak to be trusted on its own: on a real CCCD the header "SOCIALIST REPUBLIC OF VIET
# NAM" starts with "so" and would otherwise be read as the document number. The generic
# label is therefore only accepted when the value it yields is a pure digit sequence.
GENERIC_LABELS = {"so"}

# A Vietnamese CCCD number is 9 digits (CCCD) or 9-10 digits (CMND). The MRZ carries it
# twice, so the OCR text may hold any of these forms.
DOCUMENT_NUMBER_PATTERN = re.compile(r"\d{9,12}")


class OcrField(BaseModel):
    value: Optional[str] = None
    confidence: float = 0.0
    source: str = "front"
    boundingBox: Optional[List[float]] = None


class OcrResponse(BaseModel):
    success: bool = True
    engine: str = ENGINE_VERSION
    documentType: str = "CCCD"
    fields: Dict[str, OcrField] = {}
    rawText: Dict[str, str] = {}
    processingTimeMs: int = 0
    warnings: List[str] = []
    error: Optional[str] = None


class HealthResponse(BaseModel):
    status: str = "ok"
    engine: str = ENGINE_VERSION
    modelsLoaded: bool = False


# The PaddleOCR instance is created lazily so the container starts fast and the health
# endpoint can answer while the models are still loading.
_engine = None
_models_loaded = False


def get_engine():
    global _engine, _models_loaded
    if _engine is None:
        from paddleocr import PaddleOCR

        logger.info("Loading the PaddleOCR model ...")
        _engine = PaddleOCR(
            lang="vi",
            # The card is photographed in any orientation. Without the orientation
            # classifier the text lines are returned in a meaningless order and the
            # bounding boxes are vertical, so a label and its value cannot be paired.
            # The classifier rotates the card upright, which makes the reading order
            # reliable again.
            use_doc_orientation_classify=True,
            use_doc_unwarping=False,
            use_textline_orientation=False,
        )
        _models_loaded = True
        logger.info("The PaddleOCR model is ready.")
    return _engine


def preprocess_image(raw: bytes) -> "np.ndarray":
    """Grayscale + contrast enhancement + deskew-free resize.

    The original image is never modified - this only prepares the copy that is fed to
    the OCR engine (`OCR-Cloudinary-Spec.md` §3.3.4).
    """
    import cv2

    buffer = np.frombuffer(raw, dtype=np.uint8)
    image = cv2.imdecode(buffer, cv2.IMREAD_COLOR)
    if image is None:
        raise ValueError("Không đọc được ảnh.")

    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)

    # CLAHE - local contrast enhancement, helps with uneven lighting on photos.
    clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))
    enhanced = clahe.apply(gray)

    # Resize to a height of 1600 px while keeping the aspect ratio.
    target_height = 1600
    if enhanced.shape[0] > target_height:
        scale = target_height / enhanced.shape[0]
        new_width = int(enhanced.shape[1] * scale)
        enhanced = cv2.resize(enhanced, (new_width, target_height), interpolation=cv2.INTER_AREA)

    # Light denoise.
    enhanced = cv2.fastNlMeansDenoising(enhanced, None, 10, 7, 21)

    # PaddleOCR 3.x expects a 3-channel BGR image (it unpacks img.shape into
    # src_h, src_w, _). The grayscale result above has only 2 dimensions, so the
    # enhanced image is converted back to BGR before it is handed to the engine.
    return cv2.cvtColor(enhanced, cv2.COLOR_GRAY2BGR)


def strip_diacritics(value: str) -> str:
    import unicodedata

    normalized = unicodedata.normalize("NFD", value)
    without_marks = "".join(c for c in normalized if unicodedata.category(c) != "Mn")
    return without_marks.replace("đ", "d").replace("Đ", "D").lower()


def normalize_label(value: str) -> str:
    import re

    text = strip_diacritics(value)
    text = re.sub(r"[^a-z0-9]+", " ", text)
    return " ".join(text.split())


def _vowel_insensitive(value: str) -> str:
    """Strips the vowels so a label survives an OCR that drops them.

    The OCR of a low-contrast photo regularly loses the vowels of a Vietnamese label:
    "Giới tính" is read as "Gii tnh" and "Có giá trị đến" as "C gia trj dn". Removing
    the vowels on both sides makes the two comparable.
    """
    return "".join(character for character in value if character not in "aeiou")


def _label_matches(normalized_line: str, label: str) -> bool:
    """True when a normalized line starts with the given label.

    The comparison is tried three times: exactly, then on the vowel-less form, and
    finally on the first word only. The last one makes the short label "so" match
    "so cccd" as well - the caller then decides which of the two is the better match.

    The first-word fallback is only used for the short generic labels. Applied to
    every label it would make "ngay cap" match "ngay sinh" (the birth date) - and the
    issue date of a CCCD is not even printed on the card.
    """
    if normalized_line.startswith(label):
        return True

    # The vowel-less comparison. The OCR may also DROP a whole letter, so the two
    # forms are compared loosely: the line must contain the consonants of the label
    # in the same order. "C gia trj dn" then matches "co gia tri den".
    line_skeleton = _vowel_insensitive(normalized_line).replace(" ", "")
    label_skeleton = _vowel_insensitive(label).replace(" ", "")
    if label_skeleton and _skeleton_contains(line_skeleton, label_skeleton):
        return True

    # "so cccd" should also match the label "so".
    first_word = label.split(" ", 1)[0]
    if len(first_word) <= 3 and normalized_line.startswith(first_word):
        return True

    return False


def _skeleton_contains(line_skeleton: str, label_skeleton: str) -> bool:
    """True when the consonants of the label appear in the line, in order.

    The OCR drops letters at random ("C gia trj dn" for "Có giá trị đến"), so the
    comparison is done on the consonants with a small tolerance: at most one
    consonant of the label may be missing.
    """
    if line_skeleton.startswith(label_skeleton):
        return True

    if len(label_skeleton) < 4:
        return False

    # Walk through the line and try to consume the label, allowing one skip.
    index = 0
    skipped = 0
    for character in line_skeleton:
        if index < len(label_skeleton) and character == label_skeleton[index]:
            index += 1
            continue
        if character in label_skeleton:
            skipped += 1
            if skipped > 1:
                return False
    return index >= len(label_skeleton) - 1


def _clean_value(value: str) -> str:
    """Removes the OCR noise that surrounds a field value.

    On a real CCCD the document number is printed as "số/No.048203001078": the OCR keeps
    the "số/" prefix and the "No." marker, so the value has to be cleaned before it can
    be compared with the expected 9-12 digit format.
    """
    text = value.strip()

    # The OCR of "Số" is often garbage ("sé", "se", "s0"). A prefix that ends in a
    # slash is dropped - but ONLY when what follows is a plain number. A date
    # ("05/10/2003") must survive untouched.
    if "/" in text:
        head, _, tail = text.partition("/")
        if len(head) <= 6 and re.fullmatch(r"\d{1,2}", head) and re.fullmatch(r"\d{1,2}[/-]\d{2,4}", tail):
            # "05/10/2003" is a date - keep it.
            pass
        elif len(head) <= 6 and re.search(r"\d", tail):
            text = tail.strip()

    # A trailing/leading "No." or "So" marker.
    text = re.sub(r"\bno\.?\s*", "", text, flags=re.IGNORECASE)
    text = re.sub(r"^s[o0]\s*[:.]?\s*", "", text, flags=re.IGNORECASE)

    return text.strip(" .,:;")


def _looks_like_document_number(value: str) -> bool:
    """True when the value is (or contains) a plausible ID number."""
    return bool(DOCUMENT_NUMBER_PATTERN.search(value))


def _is_label_like(normalized: str) -> bool:
    """True when a normalized line reads like a field LABEL rather than a value.

    This guards the weak generic labels ("so") against matching the card header
    "SOCIALIST REPUBLIC OF VIET NAM": the remainder after the label must not itself
    start with a word that is clearly not part of a label.
    """
    # Words that can never be the beginning of a field value.
    forbidden_starts = (
        "socialist",
        "republic",
        "cong hoa",
        "chu nghia",
        "independence",
        "freedom",
        "happiness",
        "doc lap",
        "tu do",
        "hanh phuc",
        "citizen",
        "identity",
        "card",
        "cuc",
        "co quan",
    )
    return not normalized.startswith(forbidden_starts)


# The English translation that trails the Vietnamese label on a bilingual card.
_TRANSLATION_SUFFIXES = (
    "full name",
    "date of birth",
    "sex",
    "place of residence",
    "place of origin",
    "nationality",
)


def _strip_translation(normalized: str) -> str:
    """Removes the trailing English translation from a bilingual label.

    "ho va ten / full name" -> "ho va ten"
    """
    text = normalized
    for suffix in _TRANSLATION_SUFFIXES:
        if text.startswith(suffix):
            return ""
        # The translation may follow the Vietnamese label after a slash, which
        # normalize_label has already turned into a space.
        marker = " " + suffix
        index = text.find(marker)
        if index > 0:
            text = text[:index]
            break
    return text.strip()


def _next_value_line(lines: List[str], normalized_lines: List[str], start: int) -> str:
    """Finds the line that carries the value of a label.

    On a real CCCD the value sits on the line right after its label, but the OCR
    sometimes inserts an unrelated line (e.g. the card header) in between. The search
    therefore skips over lines that look like a label or a card decoration.
    """
    skip = (
        "socialist",
        "republic",
        "independence",
        "freedom",
        "happiness",
        "doc lap",
        "tu do",
        "hanh phuc",
        "cong hoa",
        "chu nghia",
        "citizen identity card",
        "cuc cong dan",
        "co quan",
    )

    for offset in range(1, 4):
        index = start + offset
        if index >= len(lines):
            break

        candidate = lines[index].strip()
        if not candidate:
            continue

        normalized = normalized_lines[index]

        # Skip card decorations and header noise.
        if any(normalized.startswith(word) for word in skip):
            continue

        # Skip a line that is itself the label of the next field.
        if normalized.endswith(tuple(_TRANSLATION_SUFFIXES)):
            continue

        return candidate

    return ""


def find_fields(lines: List[str], labels: List[str], source: str) -> Dict[str, OcrField]:
    """Splits the OCR lines into labelled fields.

    The label is matched on the NORMALIZED line (no diacritics, lower case), so the
    value must be extracted from the NORMALIZED line as well - slicing the raw line
    with a diacritic-free search string returns -1 and shifts the value to the
    wrong place.

    The real CCCD prints the label and the value on SEPARATE lines
    ("Họ và tên / Full name:" then "NGUYỄN MINH KHÔI"), so when the label line carries
    no inline value the next line is used.
    """
    fields: Dict[str, OcrField] = {}

    normalized_lines = [normalize_label(line) for line in lines]

    for label in labels:
        for index, line in enumerate(lines):
            normalized = normalized_lines[index]
            if not normalized.startswith(label):
                continue

            # The bilingual card puts the English translation right after the
            # Vietnamese label - it is not part of the value.
            remainder = _strip_translation(normalized[len(label):].strip())

            # Take the value from the normalized line: everything after the first
            # ":" separator, or - when the OCR dropped it - the part of the
            # normalized line that follows the label.
            if ":" in line:
                value = line.split(":", 1)[1].strip()
            else:
                value = remainder

            # The label line carries no value -> the value is on a following line.
            if not value:
                value = _next_value_line(lines, normalized_lines, index)

            if not value:
                continue

            # The generic "so" label is only trusted when it yields a real number and
            # the line does not look like a card header.
            if label in GENERIC_LABELS:
                if not _looks_like_document_number(value):
                    continue
                if not _is_label_like(normalize_label(value)):
                    continue

            value = _clean_value(value)

            if not value:
                continue

            # A generic label must still yield a number.
            if label in GENERIC_LABELS and not _looks_like_document_number(value):
                continue

            key = label.replace(" ", "")
            # A more specific label already matched this line (e.g. "so cccd" beats
            # the generic "so"), so it must not be overwritten.
            fields.setdefault(key, OcrField(
                value=value,
                confidence=0.9,
                source=source,
                boundingBox=None,
            ))
            break

    return fields


class TextLine:
    """One recognized text line together with its position on the card."""

    __slots__ = ("text", "score", "x0", "y0", "x1", "y1")

    def __init__(self, text: str, score: float = 0.0,
                 x0: float = 0.0, y0: float = 0.0, x1: float = 0.0, y1: float = 0.0):
        self.text = text
        self.score = score
        self.x0 = x0
        self.y0 = y0
        self.x1 = x1
        self.y1 = y1

    @property
    def cx(self) -> float:
        return (self.x0 + self.x1) / 2

    @property
    def cy(self) -> float:
        return (self.y0 + self.y1) / 2

    @property
    def height(self) -> float:
        return max(1.0, self.y1 - self.y0)


def _build_lines(result: Any) -> List[TextLine]:
    """Turns the PaddleOCR result into text lines with their bounding boxes."""
    lines: List[TextLine] = []

    if result is None:
        return lines

    if not isinstance(result, (list, tuple)):
        result = list(result)

    pages = result if isinstance(result, list) else [result]

    for page in pages:
        if not page or not hasattr(page, "get"):
            continue

        rec_texts = page.get("rec_texts")
        if rec_texts is None:
            continue

        rec_scores = page.get("rec_scores") or []
        rec_polys = page.get("rec_polys")

        for index, text in enumerate(rec_texts):
            if not text or not str(text).strip():
                continue

            try:
                score = float(rec_scores[index])
            except (IndexError, TypeError, ValueError):
                score = 0.0

            x0 = y0 = x1 = y1 = 0.0
            if rec_polys is not None and index < len(rec_polys):
                try:
                    import numpy as np

                    points = np.asarray(rec_polys[index], dtype=float)
                    x0 = float(points[:, 0].min())
                    x1 = float(points[:, 0].max())
                    y0 = float(points[:, 1].min())
                    y1 = float(points[:, 1].max())
                except (TypeError, IndexError, ValueError):
                    x0 = y0 = x1 = y1 = 0.0

            lines.append(TextLine(str(text).strip(), score, x0, y0, x1, y1))

    return lines


def _is_vertical(line: TextLine) -> bool:
    """True when a text line is much taller than it is wide.

    That happens when the card was photographed sideways: the OCR still reads the
    text, but the box is vertical and the reading order cannot be derived from it.
    """
    width = max(1.0, line.x1 - line.x0)
    return (line.y1 - line.y0) > width * 1.8


def _same_row(label: TextLine, candidate: TextLine) -> bool:
    """True when two lines sit on the same horizontal band.

    The boxes of a label and its inline value rarely have the same height, so the
    comparison is made between the vertical CENTRES: the value is on the same row
    when its centre is close to the centre of the label.
    """
    tolerance = max(label.height, candidate.height) * 0.6
    return abs(candidate.cy - label.cy) <= tolerance


def _find_value_for_label(
    label_line: TextLine,
    lines: List[TextLine],
    key: str = "",
) -> str:
    """Finds the value that belongs to a label, using the geometry of the card.

    On a Vietnamese CCCD the value is either

    - on the SAME line, right of the label ("Gii tính / Sex: Nam"), or
    - on the line DIRECTLY BELOW the label ("Ho và tên / Full name:" then the name).

    The second case is the common one and it is why the plain "next OCR line" rule
    fails: the OCR returns the lines in an order that does not follow the card.

    The card has TWO columns (the photo on the left, the data on the right), so a
    candidate must also START at roughly the same x position as the label. Without
    that check the label "Nơi thường trú" would pick up the value of the field in
    the left column.
    """
    # 1) On the same row, to the right of the label.
    inline = ""
    best_distance = None
    for candidate in lines:
        if candidate is label_line:
            continue
        if not _same_row(label_line, candidate):
            continue
        if candidate.x0 < label_line.x1 - 5:
            continue

        distance = candidate.x0 - label_line.x1
        if best_distance is None or distance < best_distance:
            best_distance = distance
            inline = candidate.text

    if inline:
        return inline.strip()

    # 1b) The value is printed INSIDE the label line, after the colon:
    #     "Gii tính / Sex: Nam Quc tch / Nationality: Vit Nam".
    own = _inline_value_of(label_line)
    if own:
        return own

    # 1c) A date that is glued to the label:
    #     "Ngày, tháng, năm / Date, month, year22/04/2021".
    if key in _DATE_LABELS:
        own_date = _date_in_line(label_line)
        if own_date:
            return own_date

    # 1d) The real CCCD prints NO "Số CCCD" label - the number line itself starts
    #     with "số": "số/No.048203001078". The number is therefore part of the
    #     label line.
    own_number = _number_in_line(label_line)
    if own_number:
        return own_number

    # 2) The line directly below the label, in the SAME column.
    below = None
    best_distance = None
    for candidate in lines:
        if candidate is label_line:
            continue
        if candidate.y0 < label_line.y1 - 5:
            continue
        if not candidate.y0 <= label_line.y1 + label_line.height * 1.2:
            continue

        # The value starts at the left edge of the label (or slightly to the right).
        # A candidate that starts far to the left belongs to another column.
        if candidate.x1 < label_line.x0 - 5:
            continue
        if candidate.x0 > label_line.x1 + label_line.height * 0.9:
            continue

        distance = candidate.y0 - label_line.y1
        if best_distance is None or distance < best_distance:
            best_distance = distance
            below = candidate

    if below is not None:
        return below.text.strip()

    return ""


def _number_in_line(label_line: TextLine) -> str:
    """Returns the ID number that is printed inside the line itself.

    The real CCCD prints no "Số CCCD" label - the number line starts with "số":
    "số/No.048203001078".
    """
    match = DOCUMENT_NUMBER_PATTERN.search(label_line.text)
    if not match:
        return ""
    return match.group(0)


def _date_in_line(label_line: TextLine) -> str:
    """Returns the date that is printed inside the label line itself.

    The back of the card reads "Ngày, tháng, năm / Date, month, year22/04/2021" -
    the OCR glues the date to the label because there is no space between them.
    """
    match = _DATE_PATTERN.search(label_line.text)
    if not match:
        return ""
    return match.group(0)


def _inline_value_of(label_line: TextLine) -> str:
    """Extracts the value that follows the colon inside a bilingual label line.

    "Gii tính / Sex: Nam Quc tch / Nationality: Vit Nam" -> "Nam"
    """
    text = label_line.text
    if ":" not in text:
        return ""

    tail = text.split(":", 1)[1].strip()
    if not tail:
        return ""

    # Cut off the next bilingual pair ("... Nam Quc tch / Nationality: ...").
    tail = re.split(r"\s+[A-Za-z]+\s*/\s*", tail, maxsplit=1)[0].strip()

    # The gender is a single word ("Nam", "Nữ"). The OCR may glue the beginning of
    # the next field to it ("Nam Quc tch") - only the first word is kept.
    for known in ("nam", "nu", "nữ", "nam nu", "khac"):
        if tail.lower().startswith(known):
            return known.title() if known == "nu" else tail.split()[0]

    return tail.strip(" .,:;")


# The value of each field has a shape. A label that produces a value of the wrong
# shape is not a match - e.g. "Ngày cấp" must not pick up the date of birth.
_DATE_PATTERN = re.compile(r"\d{1,2}\s*[/-]\s*\d{1,2}\s*[/-]\s*\d{2,4}")

# The labels whose value is a date.
_DATE_LABELS = {
    "ngaysinh", "ngaycap", "cogiatridenngay", "cogiatriden",
    "giatridenngay", "giatriden", "ngayhethan", "ngaythangnam",
    "dateofexpiry",
}

# The labels whose value is an address - the value may wrap onto a second line.
_ADDRESS_LABELS = {"diachi", "noithuongtru", "quequan"}

# The labels whose value is the ID number.
_NUMBER_LABELS = {"socccd", "sochungminh", "socmnd", "so"}


def _value_matches_label(key: str, value: str) -> bool:
    """True when the value has the shape the label requires."""
    if key in _DATE_LABELS:
        return bool(_DATE_PATTERN.search(value))
    if key in _NUMBER_LABELS:
        return _looks_like_document_number(value)
    return True


def find_fields_geo(lines: List[TextLine], labels: List[str], source: str) -> Dict[str, OcrField]:
    """Pairs the labels with their values using the bounding boxes.

    This is the reliable way to read a real card: the geometry decides which value
    belongs to which label, while the plain text order of the OCR does not.
    """
    fields: Dict[str, OcrField] = {}

    normalized = [normalize_label(line.text) for line in lines]

    for label in labels:
        best_key = ""
        best_value = ""
        best_score = -1.0

        for index, line in enumerate(lines):
            text = normalized[index]
            if not _label_matches(text, label):
                continue

            # A vertical box means the card was read sideways - the geometry is
            # unusable, so this line is skipped.
            if _is_vertical(line):
                continue

            key = label.replace(" ", "")

            value = _find_value_for_label(line, lines, key)
            if not value:
                continue

            # The value must have the shape the label requires.
            if not _value_matches_label(key, value):
                continue

            # Prefer the most specific label ("so cccd" over "so") and, for the same
            # label, the line that sits highest on the card.
            specificity = len(label)
            score = specificity * 1000 - line.cy

            if score > best_score:
                best_score = score
                best_key = key
                best_value = value

        if not best_key:
            continue

        # The generic "so" label is only trusted when it yields a real number.
        if best_key in GENERIC_LABELS and not _looks_like_document_number(best_value):
            continue

        cleaned = _clean_value(best_value)
        if not cleaned:
            continue
        if best_key in GENERIC_LABELS and not _looks_like_document_number(cleaned):
            continue

        fields.setdefault(best_key, OcrField(
            value=cleaned,
            confidence=0.9,
            source=source,
            boundingBox=None,
        ))

    # The card has TWO address lines: the place of origin ("Quê quán") and the
    # place of residence ("Nơi thường trú"). Both belong to the address, so they are
    # merged instead of one overwriting the other.
    address_parts = []
    for key in ("quequan", "noithuongtru", "diachi"):
        field = fields.get(key)
        if field and field.value and field.value not in address_parts:
            address_parts.append(field.value)

    if address_parts:
        merged = OcrField(
            value=", ".join(address_parts),
            confidence=0.9,
            source=source,
            boundingBox=None,
        )
        fields["diachi"] = merged

    return fields


def canonical_field_names(fields: Dict[str, OcrField]) -> Dict[str, OcrField]:
    """Maps the engine's label keys onto the business field names used by the backend."""
    mapping = {
        "socccd": "documentNumber",
        "sochungminh": "documentNumber",
        "socmnd": "documentNumber",
        "so": "documentNumber",
        "hovaten": "fullName",
        "ngaysinh": "dateOfBirth",
        "gioitinh": "gender",
        "diachi": "address",
        "noithuongtru": "address",
        "quequan": "address",
        "ngaycap": "issueDate",
        "cogiatridenngay": "expiryDate",
        "cogiatriden": "expiryDate",
        "giatridenngay": "expiryDate",
        "giatriden": "expiryDate",
        "ngayhethan": "expiryDate",
        "sohieu": "authorityNo",
        "coquancap": "authorityNo",
        "ngaythangnam": "issueDate",
        "dateofexpiry": "expiryDate",
    }

    result: Dict[str, OcrField] = {}

    # "diachi" holds the merged address (the place of origin and the place of
    # residence), so it must win over the two single fields.
    priority = ["diachi", "socccd", "sochungminh", "socmnd", "so"]

    ordered = [key for key in priority if key in fields]
    ordered += [key for key in fields if key not in priority]

    for key in ordered:
        canonical = mapping.get(key)
        if canonical is None:
            continue
        # The first label wins - "so" is the least specific one.
        result.setdefault(canonical, fields[key])
    return result


def _mrz_number_candidates(digit_stream: str) -> List[str]:
    """All the digit groups of the MRZ that could hold the document number.

    The personal-number field of a CCCD is 12 digits (the 9-digit document number
    plus three more) and is followed by a check digit. The candidates are returned
    as "number + check digit" so that the check can be verified.
    """
    candidates: List[str] = []

    for start in range(0, max(0, len(digit_stream) - 9)):
        number = digit_stream[start:start + 9]
        if len(number) < 9:
            break
        if not number.isdigit():
            continue

        check = digit_stream[start + 9] if start + 9 < len(digit_stream) else ""
        if not check.isdigit():
            continue

        candidate = number + check
        if candidate not in candidates:
            candidates.append(candidate)

    return candidates


# The ICAO 9303 weights for the check digit of a 9-digit number.
_MRZ_WEIGHTS = (7, 3, 1, 9, 5, 8, 4, 2, 6)


def _mrz_check_digit_ok(candidate: str) -> bool:
    """True when the check digit that follows the candidate is correct.

    The MRZ uses the ICAO 9303 weights, so a real document number is confirmed by
    the digit behind it - the OCR may shift the digits around, but it cannot forge
    the check digit.
    """
    if len(candidate) < 10:
        return False

    total = 0
    for index, character in enumerate(candidate[:9]):
        if not character.isdigit():
            return False
        total += int(character) * _MRZ_WEIGHTS[index]

    expected = total % 10
    actual = candidate[9]

    return actual.isdigit() and int(actual) == expected


def parse_mrz(lines: List[str], printed_number: str = "") -> Dict[str, str]:
    """Reads the machine-readable zone of the back side.

    The MRZ (2 lines of 30 characters) encodes the ID number in a fixed position and is
    therefore far more reliable than the printed field - the OCR of a photo routinely
    mangles the number ("số/No.048203001078").

    The OCR of the MRZ itself is not trustworthy though: on a real photo the two MRZ
    lines came out as

        0310055M2810054VNM<<<<<<<<<<<2
        IDVNM2030010787048203001078<<7

    i.e. the document code, the dates and the number are interleaved and the check
    digits are mixed in. Instead of trusting fixed offsets the number is located by
    SEARCHING: the printed number is looked for inside the digit stream, and when that
    fails the longest plausible digit group is used.

    Returns the fields that could be read - an empty dict when no MRZ was found.
    """
    result: Dict[str, str] = {}

    digit_stream = ""
    data_line_digits = ""
    raw_text = ""

    for line in lines:
        text = re.sub(r"[^A-Za-z0-9<]", "", line or "")
        if len(text) < 15:
            continue

        # The name line: "NGUYEN<MINH<KHOI<<<<<<". It consists of letters and the
        # "<" filler only - a line with digits belongs to the data line.
        if "<<<" in text and "IDVNM" not in text.upper() and not re.search(r"\d", text):
            parts = [part for part in text.split("<") if part.strip()]
            if parts:
                result.setdefault("mrzName", " ".join(parts))

        digits = re.sub(r"[^0-9]", "", text)
        digit_stream += digits
        raw_text += digits

        # The data line is the one that carries the document code ("IDVNM"). Only
        # that line holds the personal-number field with the document number.
        if "IDVNM" in text.upper():
            data_line_digits += digits

    # The digit stream of the data line is the relevant one; the other MRZ lines
    # would only add noise.
    search_stream = data_line_digits or digit_stream

    if not search_stream:
        return result

    # --- The document number ---
    number = ""
    anchored = False

    # 1) The printed number is the most reliable anchor - look for it in the MRZ.
    #    The OCR may split the number across two lines, so the search runs over
    #    the raw MRZ text as well as over the digit stream.
    printed = re.sub(r"\D", "", printed_number or "")
    if len(printed) >= 9:
        if printed in search_stream:
            number = printed
            anchored = True
        elif printed in raw_text:
            number = printed
            anchored = True
        else:
            # The OCR may have inserted a character in the middle of the number.
            for start in range(len(raw_text) - len(printed) + 1):
                window = raw_text[start:start + len(printed)]
                if len(window) == len(printed) and sum(
                    1 for a, b in zip(window, printed) if a == b
                ) >= len(printed) - 2:
                    number = printed
                    anchored = True
                    break

    # 2) The MRZ data line ends with the personal-number field, which holds the
    #    document number (9 digits) followed by three more digits and a check
    #    digit. The OCR shifts the digits around, so the candidates are scored by
    #    their ICAO check digit - only the real number passes it.
    #
    #    The date of birth also has a valid check digit, so the candidates are
    #    tried from the END of the line: the personal-number field is the last
    #    field of the MRZ.
    if not number:
        for candidate in reversed(_mrz_number_candidates(search_stream)):
            if _mrz_check_digit_ok(candidate):
                number = candidate[:9]
                break

    # 3) Otherwise take the longest digit group that looks like an ID number.
    if not number:
        candidates = re.findall(r"\d{9,12}", search_stream)
        if candidates:
            number = max(candidates, key=len)

    # 4) A 12-digit group is the MRZ personal-number field, which holds the ID
    #    number (9 digits) followed by three more digits. The first nine are the
    #    document number. The number that was found through the printed anchor is
    #    NOT truncated - it is already the exact value.
    if number and len(number) == 12 and number[:9] != number[3:12] and not anchored:
        number = number[:9]

    if number:
        result["mrzDocumentNumber"] = number

    return result

@app.get("/health", response_model=HealthResponse)
def health() -> HealthResponse:
    return HealthResponse(status="ok", engine=ENGINE_VERSION, modelsLoaded=_models_loaded)


@app.post("/ocr/identity-document")
async def ocr_identity_document(
    front: UploadFile = File(...),
    back: Optional[UploadFile] = File(None),
    documentType: str = Form("CCCD"),
) -> JSONResponse:
    started = time.perf_counter()
    warnings: List[str] = []

    try:
        front_bytes = await front.read()
        if not front_bytes:
            raise ValueError("Ảnh mặt trước rỗng.")

        back_bytes = await back.read() if back is not None else b""

        engine = get_engine()

        raw_text: Dict[str, str] = {}
        front_fields: Dict[str, OcrField] = {}
        back_fields: Dict[str, OcrField] = {}

        front_image = preprocess_image(front_bytes)
        result = engine.predict(front_image)
        front_lines, front_text = extract_lines(result)
        raw_text["front"] = front_text

        # The geometry-aware pairing is the primary strategy: it is the only one that
        # works on a real photo, where the OCR returns the lines in an order that does
        # not follow the card. The plain text parser stays as a fallback for a flat
        # scan (a PDF export, a perfectly aligned photo).
        front_geo = _build_lines(result)
        front_fields = find_fields_geo(front_geo, FRONT_LABELS, "front")
        if not front_fields:
            front_fields = find_fields(front_lines, FRONT_LABELS, "front")

        mrz: Dict[str, str] = {}

        if back_bytes:
            back_image = preprocess_image(back_bytes)
            back_result = engine.predict(back_image)
            back_lines, back_text = extract_lines(back_result)
            raw_text["back"] = back_text

            back_geo = _build_lines(back_result)
            back_fields = find_fields_geo(back_geo, BACK_LABELS, "back")
            if not back_fields:
                back_fields = find_fields(back_lines, BACK_LABELS, "back")

            # The printed number (when it could be read) is the anchor for the MRZ
            # search - the OCR of the MRZ itself is not reliable enough to be parsed
            # at fixed offsets.
            printed_number = ""
            for key in ("socccd", "sochungminh", "socmnd", "so"):
                candidate = front_fields.get(key)
                if candidate and candidate.value:
                    printed_number = candidate.value
                    break

            mrz = parse_mrz(back_lines, printed_number)

        fields = canonical_field_names({**front_fields, **back_fields})

        # The MRZ is machine-readable and therefore more reliable than the printed
        # field: on a photo the OCR of "Số CCCD" is regularly mangled
        # ("số/No.048203001078"). The MRZ value wins, but only when the printed
        # value is missing or does not look like a number at all.
        if mrz.get("mrzDocumentNumber"):
            mrz_number = mrz["mrzDocumentNumber"]
            current = fields.get("documentNumber")
            if current is None or not _looks_like_document_number(current.value or ""):
                fields["documentNumber"] = OcrField(
                    value=mrz_number,
                    confidence=0.95,
                    source="back",
                    boundingBox=None,
                )
                warnings.append("Số giấy tờ được đọc từ vùng MRZ (mã máy đọc).")

        # The name from the MRZ is diacritic-free ("NGUYEN MINH KHOI") - it is only
        # used as a fallback because the printed name keeps its diacritics.
        if "fullName" not in fields and mrz.get("mrzName"):
            fields["fullName"] = OcrField(
                value=mrz["mrzName"],
                confidence=0.8,
                source="back",
                boundingBox=None,
            )

        if not fields:
            processing = int((time.perf_counter() - started) * 1000)
            return JSONResponse(
                status_code=422,
                content={
                    "success": False,
                    "engine": ENGINE_VERSION,
                    "documentType": documentType,
                    "fields": {},
                    "rawText": raw_text,
                    "processingTimeMs": processing,
                    "error": (
                        "Không nhận dạng được vùng chữ trên ảnh mặt trước. "
                        "Vui lòng chụp lại ảnh rõ nét hơn."
                    ),
                },
            )

        if not front_fields:
            warnings.append("Không đọc được 'Số giấy tờ' rõ ràng.")

        processing = int((time.perf_counter() - started) * 1000)
        return JSONResponse(
            status_code=200,
            content={
                "success": True,
                "engine": ENGINE_VERSION,
                "documentType": documentType,
                "fields": {k: v.model_dump() for k, v in fields.items()},
                "rawText": raw_text,
                "processingTimeMs": processing,
                "warnings": warnings,
            },
        )

    except Exception as exc:  # noqa: BLE001 - the API must always answer with a payload
        logger.exception("OCR failed")
        processing = int((time.perf_counter() - started) * 1000)
        return JSONResponse(
            status_code=500,
            content={
                "success": False,
                "engine": ENGINE_VERSION,
                "documentType": documentType,
                "fields": {},
                "rawText": {},
                "processingTimeMs": processing,
                "error": f"Lỗi khi quét ảnh: {exc}",
            },
        )


def _is_legacy_item_list(candidate: Any) -> bool:
    """True when the list holds PaddleOCR 2.x items, i.e. [box, (text, score)]."""
    if not isinstance(candidate, (list, tuple)) or not candidate:
        return False

    for item in candidate:
        if not isinstance(item, (list, tuple)) or len(item) < 2:
            return False
        entry = item[1]
        if not isinstance(entry, (list, tuple)) or len(entry) < 2:
            return False
        if not isinstance(entry[0], str):
            return False

    return True


def extract_lines(result: Any) -> Any:
    """Flattens the PaddleOCR result into plain text lines.

    PaddleOCR 3.x returns an ``OCRResult`` object (or a list of them) whose
    ``rec_texts`` / ``rec_scores`` / ``rec_polys`` keys carry the data. The 2.x
    layout was ``[[[box, (text, score)], ...]]`` - both shapes are supported here
    so the service keeps working whichever engine version is installed.
    """
    texts: List[str] = []
    confidences: List[float] = []

    if result is None:
        return texts, ""

    # PaddleOCR 3.x `predict()` returns a generator of OCRResult objects.
    if not isinstance(result, (list, tuple)):
        result = list(result)

    # ---- PaddleOCR 3.x: OCRResult with rec_texts / rec_scores ----
    pages = result if isinstance(result, list) else [result]
    for page in pages:
        if not page:
            continue

        if hasattr(page, "get"):
            rec_texts = page.get("rec_texts")
            if rec_texts is not None:
                rec_scores = page.get("rec_scores") or []
                for index, text in enumerate(rec_texts):
                    if not text or not str(text).strip():
                        continue
                    texts.append(str(text).strip())
                    try:
                        confidences.append(float(rec_scores[index]))
                    except (IndexError, TypeError, ValueError):
                        confidences.append(0.0)
                continue

    # ---- PaddleOCR 2.x: [[box, (text, score)], ...] ----
    # The legacy result holds the items directly ([[box, (text, score)], ...]) or
    # wrapped one level deeper ([[[box, (text, score)], ...]]).
    if _is_legacy_item_list(result):
        items = result
    elif len(result) == 1 and isinstance(result[0], list) and _is_legacy_item_list(result[0]):
        items = result[0]
    else:
        items = []

    for item in items:
        try:
            if not isinstance(item, (list, tuple)) or len(item) < 2:
                continue
            entry = item[1]
            if not isinstance(entry, (list, tuple)) or len(entry) < 2:
                continue
            text, score = entry[0], entry[1]
            if isinstance(text, str) and text.strip():
                texts.append(text.strip())
                confidences.append(float(score))
        except (IndexError, TypeError, ValueError):
            continue

    return texts, "\n".join(texts)
