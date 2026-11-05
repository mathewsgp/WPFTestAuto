"""
Uia3Library — Robot Framework library exposing Uia3Driver keywords directly.

Mirrors the pattern used by FlaUILibrary / WPFSpyLibrary. Rarely used
directly by test authors — Layer 3 (DriverAgnosticApi) is the normal
entry point — but useful for standalone UIA3 automation scripts.
"""

from __future__ import annotations

import os
import sys

_this_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(_this_dir, "..", "..", "api"))
sys.path.insert(0, os.path.join(_this_dir, "..", "..", "mock_wpf_app"))

from uia3_driver import Uia3Driver  # noqa: E402


class Uia3Library:
    """Robot Framework library exposing Uia3Driver keywords directly."""

    ROBOT_LIBRARY_SCOPE = "GLOBAL"

    def __init__(self, app_pid: Optional[int] = None, pipe_name: Optional[str] = None):
        self.driver = Uia3Driver(app_pid=app_pid, pipe_name=pipe_name)

    # -- Convenience keyword wrappers --

    def uia3_find_element(self, search_by: str, value: str, index: Optional[str] = None):
        """Find an element by search criteria. Returns an element handle."""
        idx = int(index) if index is not None else None
        return self.driver.find_element({"searchBy": search_by, "value": value, "index": idx})

    def uia3_invoke(self, element):
        """Invoke (click) an element."""
        self.driver.invoke(element)

    def uia3_set_value(self, element, value: str):
        """Set text value on an element."""
        self.driver.set_value(element, value)

    def uia3_get_text(self, element) -> str:
        """Get text from an element."""
        return self.driver.get_text(element)

    def uia3_click(self, element):
        """Click (invoke) an element by alias/handle."""
        self.driver.invoke(element)

    def uia3_close(self):
        """Close the driver and worker process."""
        self.driver.close()
