"""Root pytest configuration.

Adds TestAutoLayer to sys.path so that `from TestAutoLayer.api.xxx` absolute
imports resolve for all test modules under Tests/.
"""

import os
import sys

_REPO_ROOT = os.path.dirname(os.path.abspath(__file__))
_TL_API = os.path.join(_REPO_ROOT, "TestAutoLayer", "api")
if _TL_API not in sys.path:
    sys.path.insert(0, _TL_API)
