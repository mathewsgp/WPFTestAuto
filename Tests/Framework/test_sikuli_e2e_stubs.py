"""
End-to-end smoke test for the Sikuli pipeline with stub backends.

Exercises the full path without needing a desktop or OpenCV:
  StubImageMatcher + StubScreenCapture + SikuliDriver(use_mock=False)
  -> find_element -> invoke -> get_text -> screenshot sink ->
     last_match_score

This proves the production driver wiring is correct even when the
underlying capture/matcher are replaced by no-op stubs.
"""

import os
import sys
import unittest
from io import BytesIO

import numpy as np

THIS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(THIS_DIR, "..", "..", "TestAutoLayer", "drivers_rf", "sikuli_robotframework"))

from SikuliLibrary import SikuliDriver  # noqa: E402
from image_matcher import StubImageMatcher  # noqa: E402
from screen_capture import StubScreenCapture  # noqa: E402


class TestSikuliEndToEndWithStubs(unittest.TestCase):
    """Run the real driver against stub backends so the test is
    hermetic and never needs a display or OpenCV."""

    def setUp(self):
        self._tmp = BytesIO()
        self.driver = SikuliDriver(
            matcher=StubImageMatcher(),
            capture=StubScreenCapture(width=640, height=480),
            use_mock=False,
            similarity=0.5,
            padding_px=2,
        )
        self.screenshots = []

        def sink(image_bgr, alias, ok, action):
            self.screenshots.append(
                {
                    "alias": alias,
                    "ok": ok,
                    "action": action,
                    "shape": None if image_bgr is None else tuple(image_bgr.shape),
                }
            )

        self.driver.screenshot_sink = sink

    def test_find_element_returns_handle_with_score(self):
        import tempfile

        with tempfile.TemporaryDirectory() as tmp:
            import cv2  # type: ignore

            tpl = os.path.join(tmp, "t.png")
            cv2.imwrite(tpl, np.zeros((10, 10, 3), dtype=np.uint8))
            handle = self.driver.find_element({"searchBy": "Image", "value": tpl, "alias": "btnX"})
        self.assertIsNotNone(handle)
        self.assertIsNotNone(self.driver.last_match_score)
        self.assertGreaterEqual(self.driver.last_match_score, 0.0)
        self.assertLessEqual(self.driver.last_match_score, 1.0)

    def test_invoke_emits_screenshot(self):
        import tempfile

        with tempfile.TemporaryDirectory() as tmp:
            import cv2  # type: ignore

            tpl = os.path.join(tmp, "t.png")
            cv2.imwrite(tpl, np.zeros((10, 10, 3), dtype=np.uint8))
            handle = self.driver.find_element({"searchBy": "Image", "value": tpl, "alias": "btnX"})
        self.driver.invoke(handle)
        self.assertEqual(len(self.screenshots), 1)
        self.assertEqual(self.screenshots[0]["action"], "invoke")
        self.assertTrue(self.screenshots[0]["ok"])
        self.assertEqual(self.screenshots[0]["shape"], (480, 640, 3))

    def test_set_value_emits_screenshot(self):
        import tempfile

        with tempfile.TemporaryDirectory() as tmp:
            import cv2  # type: ignore

            tpl = os.path.join(tmp, "t.png")
            cv2.imwrite(tpl, np.zeros((10, 10, 3), dtype=np.uint8))
            handle = self.driver.find_element({"searchBy": "Image", "value": tpl, "alias": "txtX"})
        self.driver.set_value(handle, "hello")
        self.assertEqual(len(self.screenshots), 1)
        self.assertEqual(self.screenshots[0]["action"], "set_value")
        self.assertTrue(self.screenshots[0]["ok"])

    def test_get_text_returns_empty_without_tesseract(self):
        import tempfile

        with tempfile.TemporaryDirectory() as tmp:
            import cv2  # type: ignore

            tpl = os.path.join(tmp, "t.png")
            cv2.imwrite(tpl, np.zeros((10, 10, 3), dtype=np.uint8))
            handle = self.driver.find_element({"searchBy": "Image", "value": tpl, "alias": "lblX"})
        text = self.driver.get_text(handle)
        self.assertEqual(text, "")

    def test_capture_screenshot_returns_png_bytes(self):
        import tempfile

        with tempfile.TemporaryDirectory() as tmp:
            import cv2  # type: ignore

            tpl = os.path.join(tmp, "t.png")
            cv2.imwrite(tpl, np.zeros((10, 10, 3), dtype=np.uint8))
            handle = self.driver.find_element({"searchBy": "Image", "value": tpl, "alias": "btnX"})
        png = self.driver.capture_screenshot(handle)
        self.assertIsInstance(png, bytes)
        self.assertTrue(png.startswith(b"\x89PNG\r\n\x1a\n"))

    def test_is_visible_and_enabled(self):
        import tempfile

        with tempfile.TemporaryDirectory() as tmp:
            import cv2  # type: ignore

            tpl = os.path.join(tmp, "t.png")
            cv2.imwrite(tpl, np.zeros((10, 10, 3), dtype=np.uint8))
            handle = self.driver.find_element({"searchBy": "Image", "value": tpl, "alias": "btnX"})
        self.assertTrue(self.driver.is_visible(handle))
        self.assertTrue(self.driver.is_enabled(handle))
        self.assertTrue(self.driver.is_actionable(handle))


if __name__ == "__main__":
    unittest.main()
