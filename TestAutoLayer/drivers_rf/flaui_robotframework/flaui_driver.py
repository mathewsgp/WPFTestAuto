"""
FLaUI.RobotFramework — Layer 4 driver wrapper for FlaUI.

This module provides:
1. `FlaUIDriver` — Python driver class implementing the Layer 3 interface
   for the real SampleWpfApp using flaui-uiautomation-wrapper directly.
2. `FlaUILibrary` — Robot Framework library exposing FlaUI keywords directly.

No dependency on robotframework-flaui — all UI Automation calls go through
flaui-uiautomation-wrapper (Automation, AutomationElement, ConditionFactory,
Keyboard) using live element objects instead of XPath strings.
"""

import re
import sys
import os
import time
from typing import Any, Dict, List, Optional

# Initialize Python.NET bridge before any flaui import (same as flaui_adapter)
from flaui.lib.pythonnet_bridge import setup_pythonnet_bridge  # noqa: E402

setup_pythonnet_bridge()

from flaui.core.input import Keyboard  # noqa: E402
from flaui.core.definitions import ControlType  # noqa: E402
from flaui.lib.enums import UIAutomationTypes  # noqa: E402
from flaui.modules.automation import Automation  # noqa: E402

# Add parent directories to path for base driver, retry policy, and mock app
_base_dir = os.path.normpath(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(_base_dir, "..", "..", "api"))
sys.path.insert(0, os.path.join(_base_dir, "..", "..", "mock_wpf_app"))

from base_driver import BaseDriver, ElementHandle  # noqa: E402
from retry_policy import retry_with_backoff, default_retry_policy  # noqa: E402
from mock_app import ElementNotFoundError, ElementNotInteractableError  # noqa: E402

# WPF control type -> flaui ControlType enum mapping
_WPF_TO_UIA_TYPE = {
    "Window": ControlType.Window,
    "TextBox": ControlType.Edit,
    "PasswordBox": ControlType.Edit,
    "Button": ControlType.Button,
    "CheckBox": ControlType.CheckBox,
    "RadioButton": ControlType.RadioButton,
    "ComboBox": ControlType.ComboBox,
    "ListBox": ControlType.List,
    "DataGrid": ControlType.DataGrid,
    "TabControl": ControlType.Tab,
    "TabItem": ControlType.TabItem,
    "Label": ControlType.Text,
    "TextBlock": ControlType.Text,
    "ProgressBar": ControlType.ProgressBar,
    "Slider": ControlType.Slider,
    "Menu": ControlType.Menu,
    "MenuItem": ControlType.MenuItem,
    "TreeView": ControlType.Tree,
    "TreeViewItem": ControlType.TreeItem,
    "ListView": ControlType.List,
    "StatusBar": ControlType.StatusBar,
    "ToolBar": ControlType.ToolBar,
    "Separator": ControlType.Separator,
    "GroupBox": ControlType.Group,
    "Expander": ControlType.Group,
    "ScrollViewer": ControlType.ScrollBar,
    "Border": ControlType.Pane,
    "Grid": ControlType.Pane,
    "StackPanel": ControlType.Pane,
    "Canvas": ControlType.Pane,
    "Image": ControlType.Image,
    "MediaElement": ControlType.Image,
}


def _control_type_from_name(name: str) -> ControlType:
    """Resolve a WPF/UIA control type name to a flaui ControlType enum."""
    mapped = _WPF_TO_UIA_TYPE.get(name)
    if mapped is not None:
        return mapped
    try:
        return ControlType[name]
    except (KeyError, AttributeError):
        return ControlType.Window


# ---------------------------------------------------------------------------
# XPath parsing
# ---------------------------------------------------------------------------
# flaui-uiautomation-wrapper does not support XPath natively.  We parse simple
# XPath attribute patterns into flaui ConditionFactory conditions:
#
#   //*[@AutomationId='btnAttach']          -> by_automation_id("btnAttach")
#   //Button[@AutomationId='btnAttach']     -> by_control_type(Button) AND by_automation_id("btnAttach")
#   Window[@Name='MainWindow']/Button[@AutomationId='btnAttach']  -> parent->child chain
#   //ListItem[1]                            -> indexed child
#
# Unsupported XPath features: axes (following-sibling, .., etc.), contains(),
# position() predicates other than simple [N], union (|), etc.

_SEGMENT_RE = re.compile(
    r"""^
    (?P<type>\w+|\*)?          # optional element type (Window, Button, *, etc.)
    \[
        (?P<attr>\w+)          # attribute name (AutomationId, Name, Text, ControlType)
        =
        ['\"](?P<value>.*?)['\"]  # attribute value
    \]
    (?P<index>\[\d+\])?        # optional [N] index
    $""",
    re.VERBOSE,
)


def _parse_xpath_segment(segment: str):
    """Parse a single XPath segment like Button[@AutomationId='x'][1].

    Returns (control_type_or_None, attr_name, attr_value, index_or_None).
    """
    segment = segment.strip()
    m = _SEGMENT_RE.match(segment)
    if not m:
        # Fallback: treat the whole segment as a Name search
        return None, "Name", segment, None
    ctype = m.group("type")
    attr = m.group("attr")
    val = m.group("value")
    idx = None
    if m.group("index"):
        idx = int(m.group("index").strip("[]"))
    return ctype, attr, val, idx


def _condition_for(cf, ctype: Optional[str], attr: str, value: str):
    """Build a flaui condition (possibly combined) for one XPath segment."""
    conditions = []
    if ctype and ctype != "*":
        conditions.append(cf.by_control_type(_control_type_from_name(ctype)))
    if attr == "AutomationId":
        conditions.append(cf.by_automation_id(value))
    elif attr == "Name":
        conditions.append(cf.by_name(value))
    elif attr == "Text":
        conditions.append(cf.by_text(value))
    elif attr == "ControlType":
        conditions.append(cf.by_control_type(_control_type_from_name(value)))
    else:
        conditions.append(cf.by_name(value))

    if len(conditions) == 1:
        return conditions[0]
    result = conditions[0]
    for extra in conditions[1:]:
        result = result.And(extra)
    return result


def _resolve_xpath_path(automation, xpath: str):
    """Resolve a hierarchical XPath string to a flaui AutomationElement.

    Walks the path segment by segment, starting from the desktop, using
    find_first_descendant for the first segment and find_first_child for
    subsequent segments (parent->child).
    """
    cf = automation.cf
    xpath = xpath.strip()

    # Remove leading //
    if xpath.startswith("//"):
        xpath = xpath[2:]
    elif xpath.startswith("/"):
        xpath = xpath[1:]

    segments = [s for s in xpath.split("/") if s]
    if not segments:
        return None

    desktop = automation.automation_base.get_desktop()
    current = desktop

    for i, seg in enumerate(segments):
        ctype, attr, value, idx = _parse_xpath_segment(seg)
        cond = _condition_for(cf, ctype, attr, value)

        try:
            if i == 0:
                # First segment: search from desktop (all descendants)
                found = current.find_first_descendant(condition=cond)
            else:
                # Subsequent segments: direct children only (parent->child)
                found = current.find_first_child(condition=cond)
        except Exception:
            found = None

        if found is None:
            return None

        # Apply index if present on this segment
        if idx is not None:
            try:
                siblings = current.find_all_children(condition=cond)
                if 0 <= idx < len(siblings):
                    found = siblings[idx]
                else:
                    return None
            except Exception:
                return None

        current = found

    return current


def _resolve_locator(automation, locator: Dict[str, Any]):
    """Resolve a locator dict to a flaui AutomationElement.

    Supports:
      searchBy + value: AutomationId, Name, ControlType, Text, XPath
      parent_searchBy + parent_value: parent element locator (parent->child)
      index: 0-based index among matches (indexed finding)
    """
    if automation is None:
        return None

    cf = automation.cf
    search_by = locator.get("searchBy", "AutomationId")
    value = locator.get("value", "")
    index = locator.get("index", None)

    # Determine starting element
    parent_sb = locator.get("parent_searchBy")
    parent_val = locator.get("parent_value")
    start = None

    if parent_sb and parent_val:
        # Find parent first, then search under it
        parent_cond = _build_condition(cf, parent_sb, parent_val)
        try:
            desktop = automation.automation_base.get_desktop()
            start = desktop.find_first_descendant(condition=parent_cond)
        except Exception:
            start = None

    if start is None:
        # Default: start from the desktop root
        try:
            start = automation.automation_base.get_desktop()
        except Exception:
            return None

    # XPath value: parse hierarchical path
    if search_by == "XPath":
        found = _resolve_xpath_path(automation, value)
        if found is None:
            return None
        # Apply index from locator if present
        if index is not None:
            try:
                siblings = start.find_all_children(condition=_build_condition(cf, "AutomationId", value))
                if 0 <= int(index) < len(siblings):
                    found = siblings[int(index)]
                else:
                    return None
            except Exception:
                return None
        return found

    # Non-XPath: build a single condition
    cond = _build_condition(cf, search_by, value)

    try:
        if index is not None:
            siblings = start.find_all_children(condition=cond)
            if 0 <= int(index) < len(siblings):
                return siblings[int(index)]
            return None
        found = start.find_first_descendant(condition=cond)
        return found
    except Exception:
        return None


def _build_condition(cf, search_by: str, value: str):
    """Build a flaui ConditionFactory condition from searchBy/value."""
    if search_by == "AutomationId":
        return cf.by_automation_id(value)
    if search_by == "Name":
        return cf.by_name(value)
    if search_by == "ControlType":
        return cf.by_control_type(_control_type_from_name(value))
    if search_by == "Text":
        return cf.by_text(value)
    if search_by == "XPath":
        # Simple //*[@Attr='value'] without path hierarchy
        return _condition_for(cf, None, "Name", value)
    return cf.by_name(value)


# ---------------------------------------------------------------------------
# Element wrapper
# ---------------------------------------------------------------------------


class _FlauiElement:
    """Wraps a flaui AutomationElement for use with the BaseDriver contract."""

    def __init__(self, element, automation: Automation):
        self._element = element
        self._automation = automation

    @property
    def raw_element(self):
        return self._element

    @property
    def name(self) -> str:
        try:
            return self._element.name or ""
        except Exception:
            return ""

    @property
    def automation_id(self):
        try:
            return self._element.automation_id
        except Exception:
            return None

    @property
    def is_visible(self) -> bool:
        try:
            return not bool(self._element.is_offscreen)
        except Exception:
            return False

    @property
    def is_enabled(self) -> bool:
        try:
            return bool(getattr(self._element, "is_enabled", True))
        except Exception:
            return True

    @property
    def text(self) -> str:
        try:
            return self._element.text
        except Exception:
            return self.name or ""

    def click(self):
        try:
            self._element.click()
        except Exception:
            try:
                self._element.raw_element.Click(False)
            except Exception:
                pass

    def invoke(self):
        try:
            self._element.invoke()
        except Exception:
            try:
                self._element.raw_element.Invoke()
            except Exception:
                pass

    def toggle(self):
        try:
            self._element.toggle()
        except Exception:
            try:
                self._element.raw_element.Toggle()
            except Exception:
                pass

    def set_text(self, text: str):
        try:
            self._element.as_text_box().enter(text)
            return
        except Exception:
            pass
        try:
            self._element.raw_element.Text = text
        except Exception:
            pass

    def double_click(self):
        try:
            self._element.click()
            time.sleep(0.1)
            self._element.click()
        except Exception:
            pass

    def right_click(self):
        try:
            self._element.raw_element.RightClick(False)
        except Exception:
            pass

    def press_keys(self, keys: str):
        Keyboard.type(keys)

    def hover(self):
        try:
            self._element.set_focus()
        except Exception:
            pass

    def scroll(self, direction: str):
        # flaui-uiautomation-wrapper does not expose element scroll directly.
        # Attempt via raw element scrollable pattern if available.
        try:
            scrollable = getattr(self._element.raw_element, "GetScrollPattern", None)
            if scrollable:
                sp = scrollable()
                if direction.lower() in ("down", "pagedown"):
                    sp.Scroll(0, 1)  # horizontalScrollPercent, verticalScrollPercent
                elif direction.lower() in ("up", "pageup"):
                    sp.Scroll(0, -1)
        except Exception:
            pass


# ---------------------------------------------------------------------------
# FlaUIDriver — BaseDriver implementation
# ---------------------------------------------------------------------------


class FlaUIDriver(BaseDriver):
    """Real FlaUI driver using flaui-uiautomation-wrapper directly.

    Replaces the robotframework-flaui dependency.  Locates elements via
    flaui ConditionFactory (AutomationId, Name, ControlType, Text) and
    parses simple XPath strings for parent->child and indexed finding.
    """

    @property
    def name(self) -> str:
        return "FlaUI"

    def __init__(self, app_pid: Optional[int] = None):
        self._app_pid = app_pid
        self._automation: Optional[Automation] = None
        self._app = None
        self._connected = False
        self._init_library()

    def _init_library(self):
        """Initialize flaui automation and attach to the application."""
        try:
            self._automation = Automation(UIAutomationTypes.UIA3)
            self._app = self._automation.application
            self._connected = False

            def _attach():
                if self._app_pid:
                    try:
                        self._app.attach(self._app_pid)
                        self._connected = True
                        return
                    except Exception:
                        pass

                # Fallback: find SampleWpfApp window on the desktop
                try:
                    desktop = self._automation.automation_base.get_desktop()
                    cond = self._automation.cf.by_control_type(ControlType.Window)
                    found = desktop.find_all_descendants(condition=cond)
                    for elem in found:
                        try:
                            if "SampleWpfApp" in (elem.name or ""):
                                self._connected = True
                                return
                        except Exception:
                            continue
                except Exception:
                    pass

                if not self._connected:
                    raise ConnectionError(
                        "FlaUI: Could not attach to application. Ensure the app is running."
                    )

            retry_with_backoff(_attach, default_retry_policy())
        except ImportError as e:
            raise ImportError(
                "flaui-uiautomation-wrapper not available. "
                "Install with: pip install flaui-uiautomation-wrapper"
            ) from e

    def _ensure_attached(self):
        if not self._connected or self._automation is None:
            self._init_library()

    # ------------------------------------------------------------------
    # Element resolution
    # ------------------------------------------------------------------

    def _resolve_element(self, locator: Dict[str, Any]):
        """Resolve a locator dict to a raw flaui AutomationElement."""
        self._ensure_attached()
        return _resolve_locator(self._automation, locator)

    def _wrap(self, element) -> _FlauiElement:
        return _FlauiElement(element, self._automation)

    # ------------------------------------------------------------------
    # BaseDriver interface
    # ------------------------------------------------------------------

    def find_element(self, locator: Dict[str, Any]) -> ElementHandle:
        self._ensure_attached()
        element = _resolve_locator(self._automation, locator)
        if element is None:
            search_by = locator.get("searchBy", "AutomationId")
            value = locator.get("value", "")
            raise ElementNotFoundError(
                f"FlaUI: element not found: searchBy={searchBy}, value={value}"
            )
        return ElementHandle(
            locator=dict(locator),
            driver_name=self.name,
            found_at=time.time(),
        )

    def find_elements(self, locator: Dict[str, Any]) -> List[ElementHandle]:
        self._ensure_attached()
        cf = self._automation.cf
        search_by = locator.get("searchBy", "AutomationId")
        value = locator.get("value", "")
        index = locator.get("index", None)

        parent_sb = locator.get("parent_searchBy")
        parent_val = locator.get("parent_value")

        start = None
        if parent_sb and parent_val:
            parent_cond = _build_condition(cf, parent_sb, parent_val)
            try:
                desktop = self._automation.automation_base.get_desktop()
                start = desktop.find_first_descendant(condition=parent_cond)
            except Exception:
                start = None

        if start is None:
            try:
                start = self._automation.automation_base.get_desktop()
            except Exception:
                return []

        if search_by == "XPath":
            found = _resolve_xpath_path(self._automation, value)
            els = [found] if found else []
        else:
            cond = _build_condition(cf, search_by, value)
            try:
                els = start.find_all_descendants(condition=cond)
            except Exception:
                els = []

        if index is not None:
            try:
                idx = int(index)
                els = els[idx : idx + 1] if 0 <= idx < len(els) else []
            except (ValueError, TypeError):
                els = []

        return [
            ElementHandle(locator=dict(locator), driver_name=self.name, found_at=time.time())
            for _ in els
        ]

    def invoke(self, element: ElementHandle) -> None:
        self._ensure_attached()
        el = self._resolve_element(element.locator)
        if el is None:
            raise ElementNotInteractableError(
                f"FlaUI: cannot invoke element: {element.locator}"
            )
        try:
            el.click()
        except Exception:
            try:
                el.raw_element.Click(False)
            except Exception:
                pass

    def set_value(self, element: ElementHandle, value: str) -> None:
        self._ensure_attached()
        el = self._resolve_element(element.locator)
        if el is None:
            raise ElementNotInteractableError(
                f"FlaUI: cannot set value on element: {element.locator}"
            )
        try:
            el.as_text_box().enter(value)
            return
        except Exception:
            pass
        try:
            el.raw_element.Text = value
        except Exception:
            raise ElementNotInteractableError(
                f"FlaUI: cannot set value on element: {element.locator}"
            )

    def get_text(self, element: ElementHandle) -> str:
        self._ensure_attached()
        el = self._resolve_element(element.locator)
        if el is None:
            return ""
        try:
            return el.text
        except Exception:
            return el.name or ""

    def is_visible(self, element: ElementHandle) -> bool:
        self._ensure_attached()
        el = self._resolve_element(element.locator)
        if el is None:
            return False
        try:
            return not bool(el.is_offscreen)
        except Exception:
            return False

    def is_enabled(self, element: ElementHandle) -> bool:
        self._ensure_attached()
        el = self._resolve_element(element.locator)
        if el is None:
            return False
        try:
            return bool(getattr(el, "is_enabled", True))
        except Exception:
            return True

    def toggle(self, element: ElementHandle, state: Optional[bool] = None) -> bool:
        self._ensure_attached()
        el = self._resolve_element(element.locator)
        if el is None:
            return False
        try:
            if state is None:
                el.toggle()
            else:
                el.toggle()  # flaui toggle() has no state param; call regardless
            return bool(getattr(el.raw_element, "TogglePattern", None) and el.raw_element.TogglePattern.ToggleState)
        except Exception:
            pass
        try:
            el.toggle()
            return True
        except Exception:
            return False

    def double_click(self, element: ElementHandle) -> None:
        self._ensure_attached()
        el = self._resolve_element(element.locator)
        if el is not None:
            el.double_click()

    def right_click(self, element: ElementHandle) -> None:
        self._ensure_attached()
        el = self._resolve_element(element.locator)
        if el is not None:
            el.right_click()

    def press_keys(self, element: ElementHandle, keys: str) -> None:
        self._ensure_attached()
        Keyboard.type(keys)

    def drag_drop(self, element: ElementHandle, target_element: ElementHandle) -> None:
        self._ensure_attached()
        src = self._resolve_element(element.locator)
        dst = self._resolve_element(target_element.locator)
        if src is None or dst is None:
            return
        try:
            src.raw_element.DragDrop(dst.raw_element)  # type: ignore[attr-defined]
        except Exception:
            pass

    def hover(self, element: ElementHandle) -> None:
        self._ensure_attached()
        el = self._resolve_element(element.locator)
        if el is not None:
            el.hover()

    def scroll(self, element: ElementHandle, direction: str) -> None:
        self._ensure_attached()
        el = self._resolve_element(element.locator)
        if el is not None:
            el.scroll(direction)

    def get_attribute(
        self, element: ElementHandle, attribute_name: str
    ) -> Optional[str]:
        self._ensure_attached()
        el = self._resolve_element(element.locator)
        if el is None:
            return None
        try:
            val = getattr(el.raw_element, attribute_name, None)
            return str(val) if val is not None else None
        except Exception:
            return None

    def get_data_grid_content_ocr(self, element: ElementHandle) -> str:
        self._ensure_attached()
        el = self._resolve_element(element.locator)
        if el is None:
            return ""
        try:
            data = el.raw_element.GetAllDataFromGrid()  # type: ignore[attr-defined]
            if isinstance(data, list):
                import csv
                import io

                output = io.StringIO()
                writer = csv.writer(output)
                for row in data:
                    writer.writerow(row if isinstance(row, list) else [row])
                return output.getvalue()
            return str(data)
        except Exception:
            return ""

    def capture_screenshot(self, element: Optional[ElementHandle] = None) -> bytes:
        try:
            if self._automation is not None:
                # flaui-uiautomation-wrapper does not expose screenshot capture
                pass
        except Exception:
            pass
        return b""

    def close(self) -> None:
        try:
            if self._app is not None:
                self._app.close()
        except Exception:
            pass
        self._connected = False


# ---------------------------------------------------------------------------
# FlaUILibrary — Robot Framework keyword wrapper (flaui-uiautomation-wrapper)
# ---------------------------------------------------------------------------


class FlaUILibrary:
    """Robot Framework library exposing FlaUI keywords directly.

    Uses flaui-uiautomation-wrapper instead of robotframework-flaui.
    """

    ROBOT_LIBRARY_SCOPE = "GLOBAL"

    def __init__(self):
        try:
            from flaui.lib.pythonnet_bridge import setup_pythonnet_bridge

            setup_pythonnet_bridge()
            from flaui.modules.automation import Automation
            from flaui.lib.enums import UIAutomationTypes

            self._automation = Automation(UIAutomationTypes.UIA3)
            self._cf = self._automation.cf
        except ImportError as e:
            raise ImportError(
                f"flaui-uiautomation-wrapper not available: {e}. "
                "Install with: pip install flaui-uiautomation-wrapper"
            )

    # -- Desktop / window helpers --

    def _desktop(self):
        return self._automation.automation_base.get_desktop()

    # -- Keywords --

    def flaui_find_element(self, automation_id: str):
        """Find an element by AutomationId. Returns the raw element or None."""
        try:
            cond = self._cf.by_automation_id(automation_id)
            return self._desktop().find_first_descendant(condition=cond)
        except Exception:
            return None

    def flaui_invoke(self, automation_id: str):
        """Invoke (click) an element by AutomationId."""
        el = self.flaui_find_element(automation_id)
        if el is not None:
            try:
                el.click()
            except Exception:
                try:
                    el.raw_element.Click(False)
                except Exception:
                    pass

    def flaui_set_value(self, automation_id: str, value: str):
        """Set text on an element by AutomationId."""
        el = self.flaui_find_element(automation_id)
        if el is not None:
            try:
                el.as_text_box().enter(value)
            except Exception:
                try:
                    el.raw_element.Text = value
                except Exception:
                    pass

    def flaui_get_text(self, automation_id: str):
        """Get text from an element by AutomationId."""
        el = self.flaui_find_element(automation_id)
        if el is not None:
            try:
                return el.text
            except Exception:
                return el.name or ""
        return ""
