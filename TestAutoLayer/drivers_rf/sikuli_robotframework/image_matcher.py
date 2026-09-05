"""
Sikuli image-matching backends.

Three implementations behind a small interface so the framework can run
with or without a desktop / OpenCV:

  StubImageMatcher        — returns a fixed rect, no OpenCV required. Used
                            when the opencv-python extra isn't installed or
                            in the existing mock-app tests.
  OpenCvImageMatcher      — TM_CCOEFF_NORMED template match.
  MultiScaleImageMatcher  — wraps another matcher and retries at 0.9x and
                            1.1x to absorb DPI / OS scaling variance.

The env var SIKULI_MATCHER selects the matcher:
  "stub"   -> StubImageMatcher
  "opencv" -> OpenCvImageMatcher (default if available)
  "multi"  -> MultiScaleImageMatcher around OpenCv
"""

from __future__ import annotations

import os
import sys
from dataclasses import dataclass
from typing import Optional, Tuple


@dataclass
class Match:
    """A successful match result.

    x, y are screen-space top-left; w, h are template-space dimensions;
    score is in [0.0, 1.0].
    """

    x: int
    y: int
    w: int
    h: int
    score: float

    @property
    def center(self) -> Tuple[int, int]:
        return (self.x + self.w // 2, self.y + self.h // 2)

    @property
    def rect(self) -> Tuple[int, int, int, int]:
        return (self.x, self.y, self.w, self.h)


class ImageMatcher:
    """Abstract base for image matchers."""

    name: str = "base"

    def match(
        self,
        screen_bgr,
        template_bgr,
        region: Optional[Tuple[int, int, int, int]] = None,
        threshold: float = 0.85,
    ) -> Optional[Match]:
        raise NotImplementedError

    def is_available(self) -> bool:
        return True


class StubImageMatcher(ImageMatcher):
    """No-op matcher that always returns the centre of `region` (or
    0,0,template_w,template_h when region is None). Keeps the
    in-repo mock-app flow working when opencv-python is absent."""

    name = "stub"

    def match(self, screen_bgr, template_bgr, region=None, threshold=0.85):
        th, tw = template_bgr.shape[:2]
        if region is not None:
            rx, ry, rw, rh = region
            return Match(x=rx, y=ry, w=tw, h=th, score=1.0)
        return Match(x=0, y=0, w=tw, h=th, score=1.0)


class OpenCvImageMatcher(ImageMatcher):
    """TM_CCOEFF_NORMED template match using OpenCV.

    screen_bgr / template_bgr are numpy arrays (BGR). `region` is
    (x, y, w, h) in screen space; the match is constrained to that
    sub-rect, then translated back to screen coordinates for the
    returned Match.
    """

    name = "opencv"

    def __init__(self):
        try:
            import cv2  # type: ignore
            import numpy as np  # type: ignore
        except ImportError as e:  # pragma: no cover - import guard
            raise RuntimeError(
                "opencv-python is required for OpenCvImageMatcher; "
                "install with: pip install opencv-python numpy"
            ) from e
        self._cv2 = cv2
        self._np = np

    def is_available(self) -> bool:
        try:
            import cv2  # type: ignore
            import numpy  # type: ignore
            return True
        except ImportError:
            return False

    def match(self, screen_bgr, template_bgr, region=None, threshold=0.85):
        cv2, np = self._cv2, self._np

        search = screen_bgr
        offset_x, offset_y = 0, 0
        if region is not None:
            rx, ry, rw, rh = region
            rx = max(0, rx)
            ry = max(0, ry)
            rw = max(1, min(rw, screen_bgr.shape[1] - rx))
            rh = max(1, min(rh, screen_bgr.shape[0] - ry))
            search = screen_bgr[ry : ry + rh, rx : rx + rw]
            offset_x, offset_y = rx, ry

        if template_bgr.shape[0] > search.shape[0] or template_bgr.shape[1] > search.shape[1]:
            return None

        result = cv2.matchTemplate(search, template_bgr, cv2.TM_CCOEFF_NORMED)
        _, max_val, _, max_loc = cv2.minMaxLoc(result)
        if max_val < threshold:
            return None

        th, tw = template_bgr.shape[:2]
        return Match(
            x=offset_x + max_loc[0],
            y=offset_y + max_loc[1],
            w=tw,
            h=th,
            score=float(max_val),
        )


class MultiScaleImageMatcher(ImageMatcher):
    """Wraps another matcher and retries at scaled template sizes so DPI
    variance between recorded and replayed sessions doesn't break the
    match.

    Scales: 0.9x, 1.0x, 1.1x by default; can be tuned via the SIKULI_SCALES
    env var (comma-separated floats).
    """

    name = "multi"

    def __init__(self, inner: Optional[ImageMatcher] = None, scales=None):
        self._inner = inner or OpenCvImageMatcher()
        env = os.environ.get("SIKULI_SCALES", "")
        if scales is None:
            if env:
                scales = [float(s) for s in env.split(",") if s.strip()]
            else:
                scales = [0.9, 1.0, 1.1]

        if 1.0 not in scales:
            scales = sorted(scales + [1.0])
        self._scales = scales

    def is_available(self) -> bool:
        return self._inner.is_available()

    def match(self, screen_bgr, template_bgr, region=None, threshold=0.85):
        try:
            import cv2  # type: ignore
        except ImportError:  # pragma: no cover
            return self._inner.match(screen_bgr, template_bgr, region, threshold)

        best = None
        th, tw = template_bgr.shape[:2]
        for s in self._scales:
            if s == 1.0:
                scaled = template_bgr
            else:
                nw, nh = max(1, int(tw * s)), max(1, int(th * s))
                scaled = cv2.resize(template_bgr, (nw, nh), interpolation=cv2.INTER_AREA)
            m = self._inner.match(screen_bgr, scaled, region, threshold)
            if m is not None and (best is None or m.score > best.score):
                best = m
        return best


def create_matcher() -> ImageMatcher:
    """Factory honouring the SIKULI_MATCHER env var."""
    choice = (os.environ.get("SIKULI_MATCHER", "") or "").strip().lower()
    if choice == "stub":
        return StubImageMatcher()
    opencv = OpenCvImageMatcher()
    if not opencv.is_available():
        return StubImageMatcher()
    if choice == "multi":
        return MultiScaleImageMatcher(opencv)
    return opencv


if __name__ == "__main__":  # pragma: no cover - manual smoke
    import argparse
    import pathlib

    p = argparse.ArgumentParser()
    p.add_argument("screen", type=pathlib.Path)
    p.add_argument("template", type=pathlib.Path)
    p.add_argument("--threshold", type=float, default=0.85)
    args = p.parse_args()

    try:
        import cv2  # type: ignore
    except ImportError:
        print("opencv-python not installed", file=sys.stderr)
        sys.exit(2)

    screen = cv2.imread(str(args.screen))
    template = cv2.imread(str(args.template))
    m = create_matcher().match(screen, template, threshold=args.threshold)
    print(m)
