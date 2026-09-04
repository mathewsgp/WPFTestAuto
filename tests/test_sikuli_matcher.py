"""Unit tests for the Sikuli image matcher and screen capture stubs.

Runs without a desktop, OpenCV at template-creation time, or a real
display. Exercises the contracts the production driver depends on.
"""

import os
import sys
import unittest

import numpy as np

THIS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(THIS_DIR, "..", "drivers_rf", "sikuli_robotframework"))

from image_matcher import StubImageMatcher, OpenCvImageMatcher, MultiScaleImageMatcher  # noqa: E402
from screen_capture import StubScreenCapture  # noqa: E402


def _make_screen_and_template(seed=0, w=200, h=200, tw=40, th=40, tx=50, ty=50):
    rs = np.random.RandomState(seed)
    screen = rs.randint(0, 255, (h, w, 3), dtype=np.uint8)
    cv2 = None
    try:
        import cv2 as _cv2  # type: ignore

        cv2 = _cv2
    except ImportError:
        return screen, np.zeros((th, tw, 3), dtype=np.uint8)
    cv2.rectangle(screen, (tx, ty), (tx + tw, ty + th), (200, 100, 50), thickness=-1)
    cv2.circle(screen, (tx + 10, ty + 10), 6, (0, 0, 0), thickness=-1)
    template = screen[ty : ty + th, tx : tx + tw].copy()
    return screen, template


class TestStubMatcher(unittest.TestCase):
    def test_stub_returns_full_template_rect(self):
        m = StubImageMatcher()
        out = m.match(np.zeros((100, 100, 3), np.uint8), np.zeros((20, 30, 3), np.uint8))
        self.assertIsNotNone(out)
        self.assertEqual((out.w, out.h), (30, 20))
        self.assertAlmostEqual(out.score, 1.0)


class TestOpenCvMatcher(unittest.TestCase):
    def setUp(self):
        try:
            import cv2  # noqa: F401
        except ImportError:
            self.skipTest("opencv-python not installed")

    def test_finds_known_template(self):
        m = OpenCvImageMatcher()
        screen, template = _make_screen_and_template()
        r = m.match(screen, template, threshold=0.95)
        self.assertIsNotNone(r)
        self.assertEqual((r.x, r.y), (50, 50))

    def test_rejects_missing_template(self):
        m = OpenCvImageMatcher()
        screen, _ = _make_screen_and_template(seed=0)
        # Random unrelated template:
        rs = np.random.RandomState(99)
        template = rs.randint(0, 255, (40, 40, 3), dtype=np.uint8)
        r = m.match(screen, template, threshold=0.95)
        self.assertIsNone(r)

    def test_threshold_filters_low_scores(self):
        m = OpenCvImageMatcher()
        screen, _ = _make_screen_and_template()
        # A random unrelated template should never reach a 0.99 score
        # against the test screen, so a 0.99 threshold should reject it.
        rs = np.random.RandomState(7)
        template = rs.randint(0, 255, (40, 40, 3), dtype=np.uint8)
        r = m.match(screen, template, threshold=0.99)
        self.assertIsNone(r)


class TestMultiScaleMatcher(unittest.TestCase):
    def setUp(self):
        try:
            import cv2  # noqa: F401
        except ImportError:
            self.skipTest("opencv-python not installed")

    def test_finds_at_90_percent_scale(self):
        import cv2

        m = MultiScaleImageMatcher(scales=[0.9, 1.0])
        screen, template = _make_screen_and_template()
        scaled = cv2.resize(template, None, fx=0.9, fy=0.9, interpolation=cv2.INTER_AREA)
        r = m.match(screen, scaled, threshold=0.9)
        self.assertIsNotNone(r)

    def test_default_scales_include_one(self):
        m = MultiScaleImageMatcher()
        self.assertIn(1.0, m._scales)
        self.assertGreaterEqual(len(m._scales), 2)

    def test_env_overrides_scales(self):
        os.environ["SIKULI_SCALES"] = "0.8,1.0,1.2"
        try:
            m = MultiScaleImageMatcher()
            self.assertEqual(m._scales, [0.8, 1.0, 1.2])
        finally:
            del os.environ["SIKULI_SCALES"]


class TestRetryMatchThresholdLadder(unittest.TestCase):
    def setUp(self):
        try:
            import cv2  # noqa: F401
        except ImportError:
            self.skipTest("opencv-python not installed")

    def test_exact_template_matches_at_strict_threshold(self):
        import drivers_rf.sikuli_robotframework.wait_utils as wait_utils
        from importlib import reload

        reload(wait_utils)

        screen, template = _make_screen_and_template()
        matcher = OpenCvImageMatcher()
        m = wait_utils.retry_match(
            lambda r: screen,
            matcher,
            template,
            thresholds=(0.85, 0.80, 0.75),
            attempts_per_threshold=1,
            settle_ms=0,
        )
        self.assertIsNotNone(m)

    def test_strict_threshold_rejects_unrelated_template(self):
        import drivers_rf.sikuli_robotframework.wait_utils as wait_utils
        from importlib import reload

        reload(wait_utils)

        screen, _ = _make_screen_and_template()
        rs = np.random.RandomState(123)
        unrelated = rs.randint(0, 255, (40, 40, 3), dtype=np.uint8)
        matcher = OpenCvImageMatcher()
        m = wait_utils.retry_match(
            lambda r: screen,
            matcher,
            unrelated,
            thresholds=(0.99,),
            attempts_per_threshold=1,
            settle_ms=0,
        )
        self.assertIsNone(m)


class TestStubScreenCapture(unittest.TestCase):
    def test_returns_bgr_three_channel(self):
        c = StubScreenCapture(width=120, height=80)
        img = c.grab()
        self.assertEqual(img.shape, (80, 120, 3))
        self.assertEqual(img.dtype, np.uint8)


class TestOcrGracefulDegrade(unittest.TestCase):
    def test_ocr_text_returns_empty_when_tesseract_missing(self):
        # Force the import inside ocr_text to fail by stubbing sys.modules
        # entries before calling. This simulates a clean install with no
        # pytesseract.
        from importlib import reload
        import drivers_rf.sikuli_robotframework.ocr as ocr_mod

        original = sys.modules.pop("pytesseract", None)
        sys.modules["pytesseract"] = None  # type: ignore[assignment]
        try:
            reload(ocr_mod)
            out = ocr_mod.ocr_text(np.zeros((50, 50, 3), np.uint8))
            self.assertEqual(out, "")
        finally:
            if original is not None:
                sys.modules["pytesseract"] = original
            else:
                sys.modules.pop("pytesseract", None)
            reload(ocr_mod)

    def test_ocr_grid_csv_returns_empty_when_tesseract_missing(self):
        from importlib import reload
        import drivers_rf.sikuli_robotframework.ocr as ocr_mod

        original = sys.modules.pop("pytesseract", None)
        sys.modules["pytesseract"] = None  # type: ignore[assignment]
        try:
            reload(ocr_mod)
            out = ocr_mod.ocr_grid_csv(np.zeros((100, 200, 3), np.uint8))
            self.assertEqual(out, "")
        finally:
            if original is not None:
                sys.modules["pytesseract"] = original
            else:
                sys.modules.pop("pytesseract", None)
            reload(ocr_mod)


class TestScreenshotSink(unittest.TestCase):
    def test_sink_receives_screenshot_on_real_driver(self):
        from drivers_rf.sikuli_robotframework.SikuliLibrary import SikuliDriver
        from drivers_rf.sikuli_robotframework.image_matcher import StubImageMatcher
        from drivers_rf.sikuli_robotframework.screen_capture import StubScreenCapture

        d = SikuliDriver(
            matcher=StubImageMatcher(),
            capture=StubScreenCapture(width=200, height=200),
            use_mock=False,  # force real path
            similarity=0.5,
        )
        received = []

        def sink(image_bgr, alias, ok, action):
            received.append((alias, ok, action, image_bgr.shape if image_bgr is not None else None))

        d.screenshot_sink = sink
        # Directly drive the emitter to keep the test self-contained.
        d._emit_screenshot(element=None, ok=True, action="invoke")
        self.assertEqual(len(received), 1)
        alias, ok, action, shape = received[0]
        self.assertEqual(ok, True)
        self.assertEqual(action, "invoke")
        self.assertEqual(shape, (200, 200, 3))

    def test_sink_does_not_fire_when_unset(self):
        from drivers_rf.sikuli_robotframework.SikuliLibrary import SikuliDriver
        from drivers_rf.sikuli_robotframework.image_matcher import StubImageMatcher
        from drivers_rf.sikuli_robotframework.screen_capture import StubScreenCapture

        d = SikuliDriver(
            matcher=StubImageMatcher(),
            capture=StubScreenCapture(width=10, height=10),
            use_mock=False,
        )
        d._emit_screenshot(element=None, ok=True, action="invoke")
        # No exception means success; the sink is None and _emit_screenshot
        # returns early.
        self.assertIsNone(d._screenshot_sink)


if __name__ == "__main__":
    unittest.main()
