"""FlaUI UI Automation adapter — drop-in replacement for pywinauto in IDE tests.

Initializes the Python.NET bridge once and exposes a pywinauto-compatible
interface (Application, Desktop, keyboard, helper functions) backed by
flaui-uiautomation-wrapper.
"""

import re
import time

# Initialize Python.NET bridge before any C#-backed FlaUI import
from flaui.lib.pythonnet_bridge import setup_pythonnet_bridge

setup_pythonnet_bridge()

from flaui.lib.enums import UIAutomationTypes
from flaui.modules.automation import Automation
from flaui.core.input import Keyboard
from flaui.core.definitions import ControlType

# Map pywinauto control_type strings to flaui ControlType enum values
_CONTROL_TYPE_MAP = {
    "Button": ControlType.Button,
    "CheckBox": ControlType.CheckBox,
    "Edit": ControlType.Edit,
    "ComboBox": ControlType.ComboBox,
    "ListItem": ControlType.ListItem,
    "Window": ControlType.Window,
    "Menu": ControlType.Menu,
    "TextBox": ControlType.Text,
    "WindowItem": ControlType.Window,
    "DataItem": ControlType.DataItem,
    "TreeItem": ControlType.TreeItem,
    "List": ControlType.List,
    "Tab": ControlType.Tab,
    "TabItem": ControlType.TabItem,
    "RadioButton": ControlType.RadioButton,
    "Hyperlink": ControlType.Hyperlink,
    "Image": ControlType.Image,
    "Spinner": ControlType.Spinner,
    "Slider": ControlType.Slider,
    "ProgressBar": ControlType.ProgressBar,
    "Calendar": ControlType.Calendar,
    "DataGrid": ControlType.DataGrid,
    "Header": ControlType.Header,
    "HeaderItem": ControlType.HeaderItem,
    "Thumb": ControlType.Thumb,
    "TitleBar": ControlType.TitleBar,
    "Tree": ControlType.Tree,
}


class PywinautoTimeout(Exception):
    """Raised when a control is not found within timeout (pywinauto-compatible)."""
    pass


class _Rect:
    def __init__(self, left, top, right, bottom):
        self.left = left
        self.top = top
        self.right = right
        self.bottom = bottom

    @property
    def width(self):
        return self.right - self.left

    @property
    def height(self):
        return self.bottom - self.top


class _ElementInfo:
    def __init__(self, element):
        self._element = element

    @property
    def automation_id(self):
        try:
            return self._element.automation_id
        except Exception:
            return None


class _ElementWrapper:
    """Wraps a FlaUI AutomationElement to provide a pywinauto-like interface."""

    def __init__(self, element, automation):
        self._element = element
        self._automation = automation

    def window_text(self):
        try:
            return self._element.name or ""
        except Exception:
            return ""

    def exists(self):
        return self._element is not None

    def is_visible(self):
        try:
            return not bool(self._element.is_offscreen)
        except Exception:
            return False

    def set_focus(self):
        try:
            self._element.set_focus()
        except Exception:
            pass

    def click_input(self):
        try:
            self._element.as_button().click()
        except Exception:
            try:
                self._element.raw_element.Click(False)
            except Exception:
                pass

    def invoke(self):
        try:
            self._element.as_button().invoke()
        except Exception:
            try:
                self._element.raw_element.Invoke()
            except Exception:
                pass

    def toggle(self):
        try:
            self._element.as_check_box().toggle()
        except Exception:
            try:
                self._element.raw_element.Toggle()
            except Exception:
                pass

    def set_edit_text(self, text):
        try:
            self._element.as_text_box().enter(text)
        except Exception:
            try:
                self._element.raw_element.Text = text
            except Exception:
                pass

    def type_keys(self, keys):
        Keyboard.type(keys)

    @property
    def element_info(self):
        return _ElementInfo(self._element)

    def automation_id(self):
        try:
            return self._element.automation_id
        except Exception:
            return None

    def rectangle(self):
        try:
            rect = self._element.bounding_rectangle
            return _Rect(rect.left, rect.top, rect.right, rect.bottom)
        except Exception:
            return _Rect(0, 0, 0, 0)

    def descendants(self, control_type=None):
        cf = self._automation.cf
        condition = None
        if control_type:
            ct = _CONTROL_TYPE_MAP.get(control_type)
            if ct:
                condition = cf.by_control_type(ct)
        try:
            if condition:
                elements = self._element.find_all_descendants(condition=condition)
            else:
                elements = self._element.find_all_descendants()
        except Exception:
            elements = []
        return [_ElementWrapper(e, self._automation) for e in elements]

    def wait(self, state, timeout=10):
        end_time = time.time() + timeout / 1000.0
        while time.time() < end_time:
            if state == "visible" and self.is_visible():
                return True
            time.sleep(0.1)
        return False

    def child_window(self, title=None, control_type=None):
        cf = self._automation.cf
        condition = None
        if title:
            condition = cf.by_name(title)
        elif control_type:
            ct = _CONTROL_TYPE_MAP.get(control_type)
            if ct:
                condition = cf.by_control_type(ct)
        try:
            element = self._element.find_first_descendant(condition=condition)
        except Exception:
            element = None
        if element:
            return _ElementWrapper(element, self._automation)
        return None


class _WindowWrapper(_ElementWrapper):
    """Wraps a FlaUI Window element."""

    def menu_select(self, path, timeout=10):
        try:
            menu = self._element.find_first_descendant(
                condition=self._automation.cf.by_control_type(ControlType.Menu)
            )
            if menu:
                menu.as_menu().select(path)
                return
        except Exception:
            pass
        raise PywinautoTimeout(f"Menu '{path}' not found")


class Application:
    """Pywinauto-compatible Application wrapper around flaui Application.

    Each instance owns its own Automation instance so that launching
    multiple applications (e.g. IDE + SampleWpfApp) does not share state.
    """

    def __init__(self, backend="uia"):
        self.backend = backend
        self._automation = Automation(UIAutomationTypes.UIA3)
        self._app = None
        self._connect_title = None

    def start(self, exe_path):
        self._app = self._automation.application
        self._app.launch(exe_path)
        return self

    def connect(self, title_re=None, timeout=10):
        self._connect_title = title_re
        return self

    def kill(self):
        if self._app:
            try:
                self._app.kill()
            except Exception:
                pass

    def window(self, title_re=None, title=None, control_type=None):
        if self._app is None:
            raise PywinautoTimeout("No application started")
        pattern = title_re or title or self._connect_title
        try:
            main_window = self._app.get_main_window(self._automation)
            if pattern:
                if re.search(pattern, main_window.name or ""):
                    return _WindowWrapper(main_window, self._automation)
                # Try to find a top-level window matching the pattern
                try:
                    all_windows = self._app.get_all_top_level_windows(self._automation)
                    for w in all_windows:
                        if re.search(pattern, w.name or ""):
                            return _WindowWrapper(w, self._automation)
                except Exception:
                    pass
            return _WindowWrapper(main_window, self._automation)
        except Exception as e:
            raise PywinautoTimeout(f"Window not found: {e}")


class Desktop:
    """Pywinauto-compatible Desktop for enumerating top-level windows."""

    def __init__(self, backend="uia"):
        self._automation = Automation(UIAutomationTypes.UIA3)

    def windows(self):
        """Return all top-level windows using the automation's desktop element."""
        result = []
        try:
            desktop = self._automation.automation_base.get_desktop()
            condition = self._automation.cf.by_control_type(ControlType.Window)
            found = desktop.find_all_descendants(condition=condition)
            for elem in found:
                result.append(_ElementWrapper(elem, self._automation))
        except Exception:
            pass
        return result


class keyboard:
    """Pywinauto-compatible keyboard module."""

    @staticmethod
    def send_keys(keys):
        Keyboard.type(keys)


# ---------------------------------------------------------------------------
# Module-level helper functions (pywinauto-compatible)
# ---------------------------------------------------------------------------

def _get_automation(window):
    return getattr(window, "_automation", None)


def _find_by_auto_id(window, automation_id, control_type=None):
    """Find all descendants matching AutomationId."""
    automation = _get_automation(window)
    if automation is None:
        return []
    cf = automation.cf
    condition = cf.by_automation_id(automation_id)
    try:
        elements = window._element.find_all_descendants(condition=condition)
    except Exception:
        elements = []
    matched = []
    for e in elements:
        try:
            if e.automation_id == automation_id:
                matched.append(_ElementWrapper(e, automation))
        except Exception:
            continue
    return matched


def click_by_auto_id(window, automation_id, timeout=10, control_type=None):
    """Click any control by AutomationId (searches all descendants)."""
    matched = _find_by_auto_id(window, automation_id, control_type)
    if not matched:
        raise PywinautoTimeout(f"Control '{automation_id}' not found")
    ctrl = matched[0]
    try:
        window.set_focus()
    except Exception:
        pass
    time.sleep(0.2)
    ctrl.click_input()
    return ctrl


def set_text_by_auto_id(window, automation_id, text, timeout=10):
    """Set text on an Edit/TextBox control by AutomationId."""
    matched = _find_by_auto_id(window, automation_id, "Edit")
    if not matched:
        matched = _find_by_auto_id(window, automation_id)
    if not matched:
        raise PywinautoTimeout(f"Edit control '{automation_id}' not found")
    ctrl = matched[0]
    try:
        ctrl.click_input()
    except Exception:
        pass
    ctrl.set_edit_text(text)
    return ctrl


def toggle_by_auto_id(window, automation_id, timeout=10):
    """Toggle a CheckBox by AutomationId."""
    matched = _find_by_auto_id(window, automation_id, "CheckBox")
    if not matched:
        raise PywinautoTimeout(f"CheckBox '{automation_id}' not found")
    matched[0].toggle()
    return matched[0]


def get_text_by_auto_id(window, automation_id, timeout=10):
    """Get the text of a control by AutomationId."""
    matched = _find_by_auto_id(window, automation_id)
    if not matched:
        raise PywinautoTimeout(f"Control '{automation_id}' not found")
    return matched[0].window_text()


def click_menu_item(window, menu_path, timeout=10):
    """Click a menu item by path, e.g. 'File > Save Script'."""
    parts = [p.strip() for p in menu_path.split(">")]
    try:
        window.menu_select(" > ".join(parts), timeout=timeout)
        return
    except Exception:
        pass
    # Fall back to keyboard shortcuts (defined in IDE InputBindings)
    shortcut_map = {
        ("File", "Save Script"): "^s",
        ("File", "Open Script"): "^o",
        ("Run", "Run Script"): "^r",
    }
    key = (parts[0], parts[1] if len(parts) > 1 else None)
    if key in shortcut_map:
        Keyboard.type(shortcut_map[key])
        return
    raise PywinautoTimeout(f"Menu '{menu_path}' not found and no keyboard shortcut available")


# Expose names that tests import directly
__all__ = [
    "Application",
    "Desktop",
    "keyboard",
    "PywinautoTimeout",
    "_find_by_auto_id",
    "click_by_auto_id",
    "set_text_by_auto_id",
    "toggle_by_auto_id",
    "get_text_by_auto_id",
    "click_menu_item",
]
