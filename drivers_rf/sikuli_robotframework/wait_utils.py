"""
Pre-click stability check + retry helpers for the real Sikuli driver.

`wait_until_stable(grab_fn, region, max_ms=250, threshold=2.0)` grabs
the given region twice in quick succession and returns True if the
mean absolute pixel diff is below `threshold` (per-channel). This is
the cheap-but-effective way to dodge the "clicked during repaint"
race that plagues image-driven clickers on WPF.

`retry_match(matcher, capture, template, ...)` calls
`matcher.match(capture.grab(...), template, ...)` up to N times with
a descending threshold ladder (0.85, 0.80, 0.75) so a transient
rendering glitch or a slight DPI mismatch doesn't immediately fail
the test. The first match above any threshold wins.
"""

from __future__ import annotations

import time
from typing import Callable, Optional, Sequence, Tuple

# numpy is imported lazily inside the helpers below so the module is
# importable on systems that don't have numpy installed (e.g. the IDE-
# generated test in environments that only have the mock-app path).

from image_matcher import ImageMatcher, Match


def wait_until_stable(
    grab_fn: Callable[[Optional[Tuple[int, int, int, int]]], "object"],
    region: Optional[Tuple[int, int, int, int]] = None,
    max_ms: int = 250,
    threshold: float = 2.0,
) -> bool:
    """Returns True once two consecutive grabs differ by less than
    `threshold` mean-abs pixel value, or False if `max_ms` elapses."""
    try:
        import numpy as _np  # type: ignore
    except ImportError:
        return True  # can't do the check without numpy; treat as stable

    start = time.monotonic()
    last = None
    while (time.monotonic() - start) * 1000 < max_ms:
        cur = grab_fn(region)
        if last is not None:
            diff = float(_np.mean(_np.abs(cur.astype(_np.int16) - last.astype(_np.int16))))
            if diff <= threshold:
                return True
        last = cur
        time.sleep(0.02)
    return False


def retry_match(
    capture_grab: Callable[[Optional[Tuple[int, int, int, int]]], "object"],
    matcher: ImageMatcher,
    template_bgr: "object",
    region: Optional[Tuple[int, int, int, int]] = None,
    thresholds: Sequence[float] = (0.85, 0.80, 0.75),
    attempts_per_threshold: int = 1,
    settle_ms: int = 60,
) -> Optional[Match]:
    """Try to match the template at each threshold in order. Between
    attempts, briefly wait for the screen to settle so a transient
    repaint or animation doesn't kill the match."""
    for threshold in thresholds:
        for _ in range(max(1, attempts_per_threshold)):
            if settle_ms > 0:
                time.sleep(settle_ms / 1000.0)
            screen = capture_grab(region)
            m = matcher.match(screen, template_bgr, region=region, threshold=threshold)
            if m is not None:
                return m
    return None


if __name__ == "__main__":  # pragma: no cover - manual smoke
    # Tiny demo: a stub capture that flips a pixel every 30ms.
    state = {"n": 0}

    def grab(region=None):
        import numpy as _np  # type: ignore

        state["n"] += 1
        img = _np.zeros((50, 50, 3), dtype=_np.uint8)
        if state["n"] >= 2:
            img[10:20, 10:20] = (200, 100, 50)
        return img

    ok = wait_until_stable(grab, max_ms=200)
    print("wait_until_stable:", ok)
