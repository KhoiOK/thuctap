"""PaddleOCR service - FastAPI wrapper for Vietnamese identity document OCR.

The service only reads pixels: it returns the raw text per side plus the recognized
field labels. The interpretation of those labels (which field is "Số giấy tờ", how to
normalize dates, ...) lives in the .NET backend, which is the single source of truth
(see `docs/OCR-Cloudinary-Spec.md` §3.3).
"""

from __future__ import annotations

import logging
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
FRONT_LABELS = [
    "so cccd",
    "so chung minh",
    "so cmnd",
    "so",
    "ho va ten",
    "ngay sinh",
    "gioi tinh",
    "dia chi",
    "ngay cap",
]

BACK_LABELS = [
    "co gia tri den ngay",
    "gia tri den ngay",
    "ngay het han",
    "so hieu",
    "co quan cap",
]


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
            use_doc_orientation_classify=False,
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

    return enhanced


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


def find_fields(lines: List[str], labels: List[str], source: str) -> Dict[str, OcrField]:
    """Splits the OCR lines into labelled fields."""
    fields: Dict[str, OcrField] = {}

    for label in labels:
        for index, line in enumerate(lines):
            normalized = normalize_label(line)
            if not normalized.startswith(label):
                continue

            remainder = line[line.lower().find(label.split()[0]):].split(":", 1)
            value = remainder[1].strip() if len(remainder) > 1 else ""

            if not value and index + 1 < len(lines):
                value = lines[index + 1].strip()

            if not value:
                continue

            key = label.replace(" ", "")
            fields[key] = OcrField(
                value=value,
                confidence=0.9,
                source=source,
                boundingBox=None,
            )
            break

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
        "ngaycap": "issueDate",
        "cogiatridenngay": "expiryDate",
        "giatridenngay": "expiryDate",
        "ngayhethan": "expiryDate",
        "sohieu": "authorityNo",
        "coquancap": "authorityNo",
    }

    result: Dict[str, OcrField] = {}
    for key, field in fields.items():
        canonical = mapping.get(key)
        if canonical is None:
            continue
        # The first label wins - "so" is the least specific one.
        result.setdefault(canonical, field)
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
        result = engine.ocr(front_image)
        front_lines, front_text = extract_lines(result)
        raw_text["front"] = front_text
        front_fields = find_fields(front_lines, FRONT_LABELS, "front")

        if back_bytes:
            back_image = preprocess_image(back_bytes)
            back_result = engine.ocr(back_image)
            back_lines, back_text = extract_lines(back_result)
            raw_text["back"] = back_text
            back_fields = find_fields(back_lines, BACK_LABELS, "back")

        fields = canonical_field_names({**front_fields, **back_fields})

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


def extract_lines(result: Any) -> Any:
    """Flattens the PaddleOCR result into plain text lines."""
    texts: List[str] = []
    confidences: List[float] = []

    if not result:
        return texts, ""

    # PaddleOCR 3.x returns [[[box, (text, score)], ...]]
    pages = result if isinstance(result, list) else [result]
    for page in pages:
        if not page:
            continue
        items = page[0] if len(page) == 1 and isinstance(page[0], list) else page
        for item in items:
            try:
                text, score = item[1][0], item[1][1]
                if text and text.strip():
                    texts.append(text.strip())
                    confidences.append(float(score))
            except (IndexError, TypeError, ValueError):
                continue

    return texts, "\n".join(texts)
