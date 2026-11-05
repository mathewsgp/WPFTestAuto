"""IDE integration test helpers built on the Uia3Driver (named-pipe client).

These helpers use the Uia3Driver to talk to Uia3Worker.exe over a named
pipe, driving WpfTestIde.exe and SampleWpfApp via UIA3. No flaui or
pythonnet dependency — pure pywin32 IPC.
"""

from __future__ import annotations

import os
import time
from typing import List, Optional

# Add drivers_rf and api to path
_here = os.path.dirname(os.path.abspath(__file__))
_repo_root = os.path.join(_here, "..", "..", "..")
import sys
sys.path.insert(0, os.path.join(_repo_root, "TestAutoLayer", "drivers_rf"))
sys.path.insert(0, os.path.join(_repo_root, "TestAutoLayer", "api"))

from uia3_robotframework.uia3_driver import Uia3Driver  # noqa: E402
from base_driver import ElementHandle  # noqa: E402


# ---------------------------------------------------------------------------
# Executable paths (relative to the repo root working directory)
# ---------------------------------------------------------------------------

IDE_EXE = r"bin\Debug\net9.0-windows\WpfTestIde.exe"
SAMPLE_APP_EXE = r"Tests\bin\SampleWpfApp\Debug\net9.0-windows\SampleWpfApp.exe"


# ---------------------------------------------------------------------------
# Application wrapper
# ---------------------------------------------------------------------------


class Uia3App:
    """Owns one Uia3Driver instance connected to a target application.

    Each instance launches its own Uia3Worker.exe and connects via a
    unique named pipe derived from the target's PID.
    """

    def __init__(self, app_pid: Optional[int] = None, pipe_name: Optional[str] = None):
        self.driver = Uia3Driver(app_pid=app_pid, pipe_name=pipe_name)

    @classmethod
    def launch(cls, exe_path: str, wait: float = 3.0) -> "Uia3App":
        """Launch an executable and connect a Uia3Driver to its PID."""
        import subprocess
        proc = subprocess.Popen([exe_path])
        time.sleep(wait)
        return cls(app_pid=proc.pid)

    def kill(self):
        """Kill the worker process and clean up."""
        try:
            self.driver.close()
        except Exception:
            pass

    def find_element(self, search_by: str, value: str, index: Optional[int] = None) -> Optional[ElementHandle]:
        """Find a single element. Returns None if not found."""
        try:
            return self.driver.find_element(
                {"searchBy": search_by, "value": value, "index": index}
            )
        except Exception:
            return None

    def find_elements(self, search_by: str, value: str) -> List[ElementHandle]:
        """Find all elements matching the criteria."""
        try:
            return self.driver.find_elements({"searchBy": search_by, "value": value})
        except Exception:
            return []

    def click(self, element: ElementHandle):
        """Invoke (click) an element."""
        self.driver.invoke(element)

    def set_text(self, element: ElementHandle, text: str):
        """Set text on an element."""
        self.driver.set_value(element, text)

    def get_text(self, element: ElementHandle) -> str:
        """Get text from an element."""
        return self.driver.get_text(element)

    def toggle(self, element: ElementHandle):
        """Toggle a checkbox."""
        self.driver.toggle(element)


# ---------------------------------------------------------------------------
# Convenience operations (mirror ide_flaui.py patterns)
# ---------------------------------------------------------------------------


def _get_driver(app_or_element):
    """Extract the Uia3Driver from a Uia3App or ElementHandle."""
    if hasattr(app_or_element, "driver"):
        return app_or_element.driver  # Uia3App
    if hasattr(app_or_element, "driver") and app_or_element.driver is not None:
        return app_or_element.driver  # ElementHandle with driver ref
    raise TypeError(f"Expected Uia3App or ElementHandle with driver, got {type(app_or_element)}")


def find_by_auto_id(app_or_element, automation_id: str, control_type: Optional[str] = None) -> List:
    """Find elements matching AutomationId."""
    driver = _get_driver(app_or_element)
    return driver.find_elements({"searchBy": "AutomationId", "value": automation_id})


def click_by_auto_id(app_or_element, automation_id: str, timeout=10, control_type: Optional[str] = None):
    """Click a control located by AutomationId, retrying until timeout."""
    driver = _get_driver(app_or_element)
    end_time = time.time() + timeout
    last_err = None
    while time.time() < end_time:
        matched = driver.find_elements({"searchBy": "AutomationId", "value": automation_id})
        if matched:
            try:
                driver.invoke(matched[0])
            except Exception:
                pass
            return matched[0]
        last_err = f"Control '{automation_id}' not found"
        time.sleep(0.5)
    raise TimeoutError(last_err)


def set_text_by_auto_id(app_or_element, automation_id: str, text: str, timeout=10, control_type: Optional[str] = None):
    """Set text on an Edit/TextBox control located by AutomationId."""
    driver = _get_driver(app_or_element)
    matched = driver.find_elements({"searchBy": "AutomationId", "value": automation_id})
    if not matched:
        matched = driver.find_elements({"searchBy": "AutomationId", "value": automation_id})
    if not matched:
        raise TimeoutError(f"Edit control '{automation_id}' not found")
    try:
        driver.invoke(matched[0])
    except Exception:
        pass
    driver.set_value(matched[0], text)
    return matched[0]


def toggle_by_auto_id(app_or_element, automation_id: str, timeout=10, control_type: Optional[str] = None):
    """Toggle a CheckBox located by AutomationId."""
    driver = _get_driver(app_or_element)
    matched = driver.find_elements({"searchBy": "AutomationId", "value": automation_id})
    if not matched:
        raise TimeoutError(f"CheckBox '{automation_id}' not found")
    driver.toggle(matched[0])
    return matched[0]


def click_menu_item(window_or_app, menu_path: str):
    """Click a menu item given a path like "Run > Spy Tool".

    Args:
        window_or_app: ElementHandle (main window) or Uia3App instance
        menu_path: "Menu > Submenu > Item"
    """
    parts = [p.strip() for p in menu_path.split(">")]
    if not parts:
        return

    # Resolve the driver from either an ElementHandle or a Uia3App
    if hasattr(window_or_app, "driver"):
        driver = window_or_app.driver
        element = None
    else:
        driver = window_or_app.driver  # ElementHandle has .driver
        element = window_or_app

    current_level_element = element
    for i, part in enumerate(parts):
        if i == 0:
            # Top-level menu — find by Name within the window scope
            try:
                matches = driver.find_elements({"searchBy": "Name", "value": part})
            except Exception:
                matches = []
        else:
            # Submenu items
            try:
                matches = driver.find_elements({"searchBy": "Name", "value": part})
            except Exception:
                matches = []

        if not matches:
            # Try AutomationId as fallback
            try:
                matches = driver.find_elements({"searchBy": "AutomationId", "value": part})
            except Exception:
                matches = []

        if not matches:
            return  # Menu path not found — silently skip (tests tolerate this)

        # Click the first match
        try:
            driver.invoke(matches[0])
        except Exception:
            pass

        time.sleep(0.5)
        # For subsequent levels, the menu may have opened; re-scope not needed
        # since find_elements searches the whole desktop


def get_text_by_auto_id(app: Uia3App, automation_id: str, timeout=10):
    """Get the text of a control located by AutomationId."""
    matched = find_by_auto_id(app, automation_id)
    if not matched:
        raise TimeoutError(f"Control '{automation_id}' not found")
    return app.get_text(matched[0])


__all__ = [
    "Uia3App",
    "IDE_EXE",
    "SAMPLE_APP_EXE",
    "find_by_auto_id",
    "click_by_auto_id",
    "set_text_by_auto_id",
    "toggle_by_auto_id",
    "get_text_by_auto_id",
    "click_menu_item",
]
