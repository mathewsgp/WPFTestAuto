"""
Sikuli.RobotFramework — Layer 4 driver wrapper for Sikuli.

Production driver
-----------------
Locates elements by template-image match against the live screen. The
matcher (image_matcher.py) and capture (screen_capture.py) are swappable
backends so the driver runs with or without OpenCV / mss / a desktop.

Method signatures stay identical to FlaUIDriver and WPFSpyDriver so the
rest of the framework can treat the three backends interchangeably.

Mock-app fallback
-----------------
When SIKULI_USE_MOCK=1 (default when no real capture backend is
available) the driver falls back to the in-repo mock_app tag-based
lookup so the existing headless self-tests keep passing. Set
SIKULI_USE_MOCK=0 to force the real driver.
"""

from __future__ import annotations

import os
import sys
import time
from typing import Any, List, Optional, Tuple

from image_matcher import ImageMatcher, Match, create_matcher
from screen_capture import ScreenCapture, create_capture

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "drivers", "mock_wpf_app"))
from mock_app import APP_INSTANCE, ElementNotFoundError, ElementNotInteractableError  # noqa: E402


def _resolve_template_path(value: str, image_path: Optional[str]) -> str:
    """Strategy.value may be the tag, the image filename, or a full path.
    The recorded YAML uses an imagePath-relative form like
    'sikuli/SampleWpfApp.OrdersWindow.btnLogout.png' — turn that into an
    absolute path relative to the repository root if it exists.
    """
    candidate = value
    if image_path and os.path.isabs(image_path):
        return image_path
    if image_path:
        candidate = image_path
    if os.path.isabs(candidate):
        return candidate
    here = os.path.dirname(os.path.abspath(__file__))
    repo_root = os.path.normpath(os.path.join(here, "..", "..", ".."))
    abs1 = os.path.join(repo_root, "repository", candidate)
    if os.path.exists(abs1):
        return abs1
    abs2 = os.path.join(repo_root, candidate)
    if os.path.exists(abs2):
        return abs2
    return candidate


def _load_template_bgr(path: str):
    try:
        import cv2  # type: ignore
    except ImportError as e:  # pragma: no cover
        raise RuntimeError("opencv-python is required to load templates") from e
    img = cv2.imread(path, cv2.IMREAD_COLOR)
    if img is None:
        raise ElementNotFoundError(f"Sikuli: template not found or unreadable: {path}")
    return img


class _RegionHandle:
    """What find_element returns: a Match the driver later clicks at."""

    __slots__ = ("match", "template_path", "hwnd", "alias")

    def __init__(self, match: Match, template_path: str, hwnd: Optional[int] = None, alias: str = ""):
        self.match = match
        self.template_path = template_path
        self.hwnd = hwnd
        self.alias = alias

    def __repr__(self) -> str:  # pragma: no cover
        return f"RegionHandle({self.alias or self.template_path} @ {self.match.rect}, score={self.match.score:.2f})"


class SikuliDriver:
    """Sikuli driver — locates elements by template image match.

    Resolution order:
      1. Real driver (matcher + capture) when both backends are usable
         and SIKULI_USE_MOCK != '1'.
      2. Mock-app fallback for headless self-tests.
    """

    name = "Sikuli"

    def __init__(
        self,
        matcher: Optional[ImageMatcher] = None,
        capture: Optional[ScreenCapture] = None,
        use_mock: Optional[bool] = None,
        similarity: float = 0.85,
        padding_px: int = 4,
    ):
        self._matcher: ImageMatcher = matcher or create_matcher()
        self._capture: ScreenCapture = capture or create_capture()

        if use_mock is None:
            use_mock = os.environ.get("SIKULI_USE_MOCK", "")
            if use_mock == "":
                use_mock = not (self._matcher.is_available() and self._capture.is_available())
            else:
                use_mock = use_mock == "1"
        self._use_mock = bool(use_mock)

        self._default_similarity = float(similarity)
        self._padding_px = int(padding_px)

        # Optional last-captured screenshot sink (set by framework/tests).
        self._screenshot_sink = None  # callable(image_bgr, alias, ok)

    # ---------- mock-app helpers ----------
    def _use_real(self) -> bool:
        return not self._use_mock and self._matcher.is_available() and self._capture.is_available()

    def _mock_handle(self, tag: str):
        ctrl = APP_INSTANCE.find_by_image_tag(tag)
        if ctrl is None:
            raise ElementNotFoundError(f"Sikuli: no on-screen match for image '{tag}'")
        return ctrl

    # ---------- low-level match ----------
    def _find_on_screen(
        self,
        template_path: str,
        region: Optional[Tuple[int, int, int, int]] = None,
        threshold: Optional[float] = None,
    ) -> Match:
        screen = self._capture.grab(region)
        template = _load_template_bgr(template_path)
        m = self._matcher.match(screen, template, region=region, threshold=threshold or self._default_similarity)
        if m is None:
            raise ElementNotFoundError(
                f"Sikuli: no on-screen match for template '{template_path}' "
                f"(threshold={threshold or self._default_similarity})"
            )
        return m

    # ---------- public driver interface ----------
    def find_element(self, strategy: dict):
        search_by = strategy.get("searchBy", "Image")
        if search_by != "Image":
            raise ElementNotFoundError(f"Sikuli: only Image search supported, got {search_by}")

        tag = strategy.get("value") or strategy.get("imagePath")
        similarity = float(strategy.get("similarity", self._default_similarity))
        region = strategy.get("region")
        hwnd = strategy.get("hwnd")
        alias = strategy.get("alias", "")

        if not tag:
            raise ElementNotFoundError("Sikuli: Image strategy requires 'value' or 'imagePath'")

        if self._use_real():
            template_path = _resolve_template_path(str(tag), strategy.get("imagePath"))
            if region is None and hwnd:
                # When the AUT HWND is known, capture that specific window
                # for a stable, occlusion-resistant match.
                screen = self._capture.grab_window(int(hwnd))
                import cv2  # type: ignore

                template = _load_template_bgr(template_path)
                m = self._matcher.match(screen, template, threshold=similarity)
                if m is None:
                    raise ElementNotFoundError(
                        f"Sikuli: no on-screen match for template '{template_path}' in hwnd={hwnd}"
                    )
            else:
                m = self._find_on_screen(template_path, region=tuple(region) if region else None, threshold=similarity)
            return _RegionHandle(m, template_path, hwnd=int(hwnd) if hwnd else None, alias=str(alias))

        # Mock fallback
        return self._mock_handle(str(tag))

    def find_elements(self, strategy: dict) -> List:
        """Return all matches above threshold.

        Real-driver path: re-grab the screen once and return every
        non-overlapping Match whose score is above threshold.

        Mock path: returns the single matching control (the mock_app
        only tracks one element per tag).
        """
        search_by = strategy.get("searchBy", "Image")
        if search_by != "Image":
            return []
        tag = strategy.get("value") or strategy.get("imagePath")
        if not tag:
            return []
        if self._use_real():
            try:
                template_path = _resolve_template_path(str(tag), strategy.get("imagePath"))
                template = _load_template_bgr(template_path)
                screen = self._capture.grab()
                matches: List[_RegionHandle] = []
                try:
                    import cv2  # type: ignore
                except ImportError:  # pragma: no cover
                    return []

                result = cv2.matchTemplate(screen, template, cv2.TM_CCOEFF_NORMED)
                th, tw = template.shape[:2]
                threshold = float(strategy.get("similarity", self._default_similarity))
                while True:
                    _, max_val, _, max_loc = cv2.minMaxLoc(result)
                    if max_val < threshold:
                        break
                    matches.append(
                        _RegionHandle(
                            Match(x=int(max_loc[0]), y=int(max_loc[1]), w=int(tw), h=int(th), score=float(max_val)),
                            template_path,
                        )
                    )
                    # Zero out a region around the found match so the next
                    # minMaxLoc returns a different one.
                    x0 = max(0, int(max_loc[0]) - tw // 2)
                    y0 = max(0, int(max_loc[1]) - th // 2)
                    x1 = min(result.shape[1], int(max_loc[0]) + tw + tw // 2)
                    y1 = min(result.shape[0], int(max_loc[1]) + th + th // 2)
                    cv2.rectangle(result, (x0, y0), (x1, y1), 0, thickness=-1)
                return matches
            except Exception:
                return []
        ctrl = APP_INSTANCE.find_all_by_image_tag(str(tag))
        return [c for c in (ctrl or []) if c is not None]

    # ---------- interaction primitives ----------
    def _click_region(self, region: _RegionHandle, button: str = "left", count: int = 1) -> None:
        if not self._use_real():
            APP_INSTANCE.invoke(region)
            return
        try:
            import pyautogui  # type: ignore
        except ImportError as e:  # pragma: no cover
            raise RuntimeError(
                "pyautogui is required for real Sikuli clicks; install with: pip install pyautogui"
            ) from e
        cx, cy = region.match.center
        pyautogui.moveTo(cx, cy, duration=0.05)
        pyautogui.click(cx, cy, button=button, clicks=count)

    def _type_text(self, region: Optional[_RegionHandle], text: str) -> None:
        if not self._use_real():
            APP_INSTANCE.set_value(region, text)
            return
        try:
            import pyperclip  # type: ignore
            import pyautogui  # type: ignore
        except ImportError as e:  # pragma: no cover
            raise RuntimeError(
                "pyperclip + pyautogui are required for real Sikuli text entry; install both"
            ) from e
        if region is not None:
            cx, cy = region.match.center
            pyautogui.click(cx, cy)
        pyperclip.copy(text)
        pyautogui.hotkey("ctrl", "v")

    # ---------- public actions (API parity with FlaUI/WPFSpy drivers) ----------
    def invoke(self, element):
        if self._use_real():
            self._click_region(element)
        else:
            APP_INSTANCE.invoke(element)

    def set_value(self, element, value: str):
        self._type_text(element, str(value))

    def get_text(self, element) -> str:
        if not self._use_real():
            return APP_INSTANCE.get_text(element)
        # Phase 1.5 will replace this with pytesseract OCR. For now we
        # just hand back the element's alias as a deterministic label so
        # tests can run without OCR installed.
        return getattr(element, "alias", "") or ""

    def is_visible(self, element) -> bool:
        if not self._use_real():
            return APP_INSTANCE.is_visible(element)
        # A Match that succeeded implies the element is currently visible.
        return getattr(element, "match", None) is not None

    def is_enabled(self, element) -> bool:
        if not self._use_real():
            return APP_INSTANCE.is_enabled(element)
        return True  # image match can't infer disabled state reliably

    def is_actionable(self, element) -> bool:
        return self.is_visible(element) and self.is_enabled(element)

    def get_attribute(self, element, attribute_name: str) -> Optional[str]:
        if not self._use_real():
            return APP_INSTANCE.get_attribute(element, attribute_name)
        if attribute_name.lower() in ("x", "y"):
            return getattr(element.match, attribute_name.lower(), None)
        if attribute_name.lower() == "score":
            return element.match.score
        if attribute_name.lower() == "template":
            return element.template_path
        return None

    def capture_screenshot(self, element=None) -> bytes:
        if not self._use_real():
            return APP_INSTANCE.capture_screenshot(element)
        try:
            import cv2  # type: ignore
        except ImportError:  # pragma: no cover
            return b""
        region = None
        if element is not None and getattr(element, "match", None) is not None:
            region = element.match.rect
        img = self._capture.grab(region=region)
        ok, buf = cv2.imencode(".png", img)
        if not ok:
            return b""
        return bytes(buf)

    def double_click(self, element):
        if self._use_real():
            self._click_region(element, count=2)
        else:
            APP_INSTANCE.double_click(element)

    def right_click(self, element):
        if self._use_real():
            self._click_region(element, button="right")
        else:
            APP_INSTANCE.right_click(element)

    def press_keys(self, element, keys: str):
        if not self._use_real():
            APP_INSTANCE.press_keys(element, keys)
            return
        try:
            import pyautogui  # type: ignore
        except ImportError as e:  # pragma: no cover
            raise RuntimeError("pyautogui is required for real Sikuli key press") from e
        if element is not None and getattr(element, "match", None) is not None:
            cx, cy = element.match.center
            pyautogui.click(cx, cy)
        pyautogui.press(keys)

    def drag_drop(self, element, target_element):
        if not self._use_real():
            APP_INSTANCE.drag_drop(element, target_element)
            return
        try:
            import pyautogui  # type: ignore
        except ImportError as e:  # pragma: no cover
            raise RuntimeError("pyautogui is required for real Sikuli drag/drop") from e
        sx, sy = element.match.center
        tx, ty = (
            target_element.match.center
            if hasattr(target_element, "match")
            else (int(target_element[0]), int(target_element[1]))
        )
        pyautogui.moveTo(sx, sy, duration=0.1)
        pyautogui.dragTo(tx, ty, duration=0.4, button="left")

    def hover(self, element):
        if not self._use_real():
            APP_INSTANCE.hover(element)
            return
        try:
            import pyautogui  # type: ignore
        except ImportError as e:  # pragma: no cover
            raise RuntimeError("pyautogui is required for real Sikuli hover") from e
        cx, cy = element.match.center
        pyautogui.moveTo(cx, cy, duration=0.1)

    def scroll(self, element, direction: str):
        if not self._use_real():
            APP_INSTANCE.scroll(element, direction)
            return
        try:
            import pyautogui  # type: ignore
        except ImportError as e:  # pragma: no cover
            raise RuntimeError("pyautogui is required for real Sikuli scroll") from e
        cx, cy = element.match.center
        clicks = 5 if direction.lower() in ("down", "pagedown") else -5
        pyautogui.scroll(clicks, x=cx, y=cy)

    def get_data_grid_content_ocr(self, element) -> str:
        if not self._use_real():
            return APP_INSTANCE.get_data_grid_content_ocr(element) if hasattr(APP_INSTANCE, "get_data_grid_content_ocr") else ""
        # Phase 1.5 will wire pytesseract here. For now, return an empty
        # CSV so the calling keyword can continue to a clean failure.
        return ""

    def toggle(self, element, state: bool = None):
        if self._use_real():
            # Image-only driver: a click is the only way to flip a checkbox.
            self._click_region(element)
        else:
            APP_INSTANCE.invoke(element)

    def close(self):
        # No persistent resources to release for either backend.
        return None


class SikuliLibrary:
    """Robot Framework library exposing Sikuli keywords directly (rarely
    used directly by test authors — Layer 3 is the normal entry point).
    """

    ROBOT_LIBRARY_SCOPE = "GLOBAL"

    def __init__(self, similarity: float = 0.85, padding_px: int = 4):
        self.driver = SikuliDriver(similarity=similarity, padding_px=padding_px)

    def sikuli_find_element(self, image_tag, similarity: float = None):
        strategy = {"searchBy": "Image", "value": image_tag}
        if similarity is not None:
            strategy["similarity"] = float(similarity)
        return self.driver.find_element(strategy)

    def sikuli_invoke(self, image_tag, similarity: float = None):
        el = self.sikuli_find_element(image_tag, similarity=similarity)
        self.driver.invoke(el)

    def sikuli_set_value(self, image_tag, value, similarity: float = None):
        el = self.sikuli_find_element(image_tag, similarity=similarity)
        self.driver.set_value(el, value)

    def sikuli_get_text(self, image_tag, similarity: float = None):
        el = self.sikuli_find_element(image_tag, similarity=similarity)
        return self.driver.get_text(el)
