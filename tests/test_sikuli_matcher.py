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


class TestStubScreenCapture(unittest.TestCase):
    def test_returns_bgr_three_channel(self):
        c = StubScreenCapture(width=120, height=80)
        img = c.grab()
        self.assertEqual(img.shape, (80, 120, 3))
        self.assertEqual(img.dtype, np.uint8)


if __name__ == "__main__":
    unittest.main()
