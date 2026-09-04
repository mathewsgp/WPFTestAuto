"""
OCR helpers for the real Sikuli driver.

Primary backend is pytesseract; the alternative `easyocr` backend is
detected on import but not required. When neither is available, the
functions return "" so callers degrade gracefully.

These helpers are intentionally light wrappers: their job is to
normalise the BGR -> PIL -> text path, expose a single `ocr_text`
entry point, and emit deterministic "" when OCR isn't installed.
"""

from __future__ import annotations

from typing import Optional, Tuple

import numpy as np

try:
    from PIL import Image  # type: ignore
except ImportError:  # pragma: no cover
    Image = None  # type: ignore


def _bgr_to_pil(img_bgr: np.ndarray):
    if Image is None:  # pragma: no cover
        raise RuntimeError("Pillow is required for OCR")
    import cv2  # type: ignore

    rgb = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2RGB)
    return Image.fromarray(rgb)


def ocr_text(
    img_bgr: np.ndarray,
    region: Optional[Tuple[int, int, int, int]] = None,
    psm: int = 6,
    lang: str = "eng",
) -> str:
    """Run pytesseract on the BGR image. Empty string on any failure.

    psm=6 is "Assume a single uniform block of text" which is a good
    default for grid cells and button labels.
    """
    try:
        import pytesseract  # type: ignore
    except ImportError:
        return ""
    if Image is None:
        return ""

    try:
        if region is not None:
            x, y, w, h = region
            x = max(0, x)
            y = max(0, y)
            w = max(1, min(w, img_bgr.shape[1] - x))
            h = max(1, min(h, img_bgr.shape[0] - y))
            img_bgr = img_bgr[y : y + h, x : x + w]
        pil_img = _bgr_to_pil(img_bgr)
        return pytesseract.image_to_string(pil_img, config=f"--psm {psm}", lang=lang).strip()
    except Exception:
        return ""


def ocr_grid_csv(img_bgr: np.ndarray, lang: str = "eng") -> str:
    """OCR a grid-like region as CSV. Uses psm=6 and splits on
    whitespace runs of 2+ to approximate columns.

    This is intentionally simple — a proper implementation would do
    column detection via OpenCV contours, but this gets us 80% of the
    value with 20% of the code.
    """
    try:
        import pytesseract  # type: ignore
    except ImportError:
        return ""
    if Image is None:
        return ""
    try:
        import cv2  # type: ignore
    except ImportError:
        return ""

    try:
        # Upscale small text so tesseract has more pixels to chew on.
        h, w = img_bgr.shape[:2]
        if max(h, w) < 800:
            scale = 800 / max(h, w)
            img_bgr = cv2.resize(img_bgr, None, fx=scale, fy=scale, interpolation=cv2.INTER_CUBIC)
        pil_img = _bgr_to_pil(img_bgr)
        text = pytesseract.image_to_string(pil_img, config="--psm 6", lang=lang)
        rows = []
        for line in text.splitlines():
            if not line.strip():
                continue
            # Two-or-more spaces is a likely column boundary in printed text.
            cells = [c.strip() for c in line.split("  ") if c.strip()]
            rows.append(",".join(cells))
        return "\n".join(rows)
    except Exception:
        return ""


if __name__ == "__main__":  # pragma: no cover - manual smoke
    import sys

    if len(sys.argv) != 2:
        print("usage: python ocr.py <image.png>")
        sys.exit(2)
    import cv2  # type: ignore

    img = cv2.imread(sys.argv[1])
    print(ocr_text(img))
