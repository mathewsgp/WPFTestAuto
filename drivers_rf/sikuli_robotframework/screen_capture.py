"""
Sikuli screen-capture backends.

  MssScreenCapture        — full-desktop capture using the `mss` library.
                            Cross-platform, no native deps beyond libffi.
  PrintWindowScreenCapture — per-window capture via Win32 PrintWindow.
                            Stable even when the window is occluded,
                            Windows-only.

The env var SIKULI_SCREEN_CAPTURE selects the backend:
  "mss"        -> MssScreenCapture (default if available)
  "printwindow" -> PrintWindowScreenCapture (Windows only)
  "stub"       -> NullScreenCapture (returns a fixed-size black BGR
                 buffer; used in unit tests / mock-app flow)
"""

from __future__ import annotations

import os
import sys
import threading
from typing import Optional, Tuple

import numpy as np

# numpy is a hard requirement for the BGR buffer format. It's already in
# the project via opencv-python transitively, so this never triggers
# ImportError in practice.


class ScreenCapture:
    """Abstract base for screen capture backends."""

    name: str = "base"

    def grab(self, region: Optional[Tuple[int, int, int, int]] = None) -> np.ndarray:
        """Return a BGR image covering the given screen region.

        If region is None, the entire primary monitor is returned.
        Coordinates are screen-space (top-left origin).
        """
        raise NotImplementedError

    def grab_window(self, hwnd: int) -> np.ndarray:
        """Return a BGR image of the window with the given HWND.

        Falls back to grab() on platforms where this is unsupported.
        """
        return self.grab()

    def is_available(self) -> bool:
        return True


class StubScreenCapture(ScreenCapture):
    """Returns a 1x1 black BGR buffer. Used for headless self-tests."""

    name = "stub"

    def __init__(self, width: int = 1, height: int = 1):
        self._w = width
        self._h = height

    def grab(self, region=None):
        return np.zeros((self._h, self._w, 3), dtype=np.uint8)

    def is_available(self) -> bool:
        return True


class MssScreenCapture(ScreenCapture):
    """Full-desktop capture via mss.

    mss is pip-installable on all platforms supported by the project.
    Caches the mss instance on first use; it isn't safe to construct
    mss multiple times in the same process because of internal
    threading state.
    """

    name = "mss"

    _lock = threading.Lock()
    _instance = None

    @classmethod
    def _get_mss(cls):
        with cls._lock:
            if cls._instance is None:
                import mss  # type: ignore

                cls._instance = mss.mss()
            return cls._instance

    def is_available(self) -> bool:
        try:
            import mss  # type: ignore  # noqa: F401

            return True
        except ImportError:
            return False

    def grab(self, region=None):
        try:
            import mss  # type: ignore
            import cv2  # type: ignore
        except ImportError as e:  # pragma: no cover - import guard
            raise RuntimeError(
                "mss + opencv-python are required for MssScreenCapture; "
                "install with: pip install mss opencv-python"
            ) from e

        sct = self._get_mss()
        if region is None:
            mon = sct.monitors[1]
        else:
            x, y, w, h = region
            mon = {"left": int(x), "top": int(y), "width": int(w), "height": int(h)}

        raw = sct.grab(mon)
        bgr = cv2.cvtColor(np.array(raw), cv2.COLOR_BGRA2BGR)
        return bgr


class PrintWindowScreenCapture(ScreenCapture):
    """Windows-only per-window capture via PrintWindow.

    Useful when the AUT window is partially occluded or you want to
    match a template captured at recording time without depending on
    what else is on screen.
    """

    name = "printwindow"

    def is_available(self) -> bool:
        return sys.platform == "win32"

    def grab_window(self, hwnd: int) -> np.ndarray:
        if not self.is_available():  # pragma: no cover
            raise RuntimeError("PrintWindowScreenCapture is Windows-only")
        try:
            import ctypes
            from ctypes import wintypes
            import cv2  # type: ignore
        except ImportError as e:  # pragma: no cover
            raise RuntimeError("opencv-python is required") from e

        user32 = ctypes.windll.user32
        gdi32 = ctypes.windll.gdi32

        rect = wintypes.RECT()
        user32.GetWindowRect(hwnd, ctypes.byref(rect))
        width = rect.right - rect.left
        height = rect.bottom - rect.top
        if width <= 0 or height <= 0:
            return np.zeros((1, 1, 3), dtype=np.uint8)

        hdc_window = user32.GetWindowDC(hwnd)
        hdc_mem = gdi32.CreateCompatibleDC(hdc_window)
        hbm = gdi32.CreateCompatibleBitmap(hdc_window, width, height)
        gdi32.SelectObject(hdc_mem, hbm)

        # PW_RENDERFULLCONTENT = 0x2 (Win 8.1+) so DWM-composed
        # windows render their content into the DC.
        user32.PrintWindow(hwnd, hdc_mem, 0x2)

        class BITMAPINFOHEADER(ctypes.Structure):
            _fields_ = [
                ("biSize", wintypes.DWORD),
                ("biWidth", wintypes.LONG),
                ("biHeight", wintypes.LONG),
                ("biPlanes", wintypes.WORD),
                ("biBitCount", wintypes.WORD),
                ("biCompression", wintypes.DWORD),
                ("biSizeImage", wintypes.DWORD),
                ("biXPelsPerMeter", wintypes.LONG),
                ("biYPelsPerMeter", wintypes.LONG),
                ("biClrUsed", wintypes.DWORD),
                ("biClrImportant", wintypes.DWORD),
            ]

        bmi = BITMAPINFOHEADER()
        bmi.biSize = ctypes.sizeof(BITMAPINFOHEADER)
        bmi.biWidth = width
        bmi.biHeight = -height  # top-down
        bmi.biPlanes = 1
        bmi.biBitCount = 32
        bmi.biCompression = 0  # BI_RGB

        buf_len = width * height * 4
        buf = (ctypes.c_ubyte * buf_len)()
        gdi32.GetDIBits(hdc_mem, hbm, 0, height, buf, ctypes.byref(bmi), 0)

        gdi32.DeleteObject(hbm)
        gdi32.DeleteDC(hdc_mem)
        user32.ReleaseDC(hwnd, hdc_window)

        arr = np.frombuffer(buf, dtype=np.uint8).reshape(height, width, 4)
        bgr = cv2.cvtColor(arr, cv2.COLOR_BGRA2BGR)
        return bgr


def create_capture() -> ScreenCapture:
    """Factory honouring the SIKULI_SCREEN_CAPTURE env var."""
    choice = (os.environ.get("SIKULI_SCREEN_CAPTURE", "") or "").strip().lower()
    if choice == "stub":
        return StubScreenCapture()
    if choice == "printwindow":
        c = PrintWindowScreenCapture()
        if c.is_available():
            return c
    mss_cap = MssScreenCapture()
    if mss_cap.is_available():
        return mss_cap
    return StubScreenCapture()


if __name__ == "__main__":  # pragma: no cover - manual smoke
    cap = create_capture()
    print("capture:", cap.name, "available:", cap.is_available())
    img = cap.grab()
    print("grabbed:", img.shape, img.dtype)
