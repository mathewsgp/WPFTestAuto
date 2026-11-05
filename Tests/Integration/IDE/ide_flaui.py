"""IDE integration test helpers built directly on flaui-uiautomation-wrapper.

These helpers use the flaui wrapper's native API (Automation, Application,
AutomationElement, Keyboard, ConditionFactory) — there is no pywinauto
compatibility layer. Import them from the IDE integration tests instead of
the removed ``flaui_adapter`` module.
"""

from __future__ import annotations

import re
import time
from typing import List, Optional

# Initialize Python.NET bridge (loads the FlaUI C# assemblies) before any
# flaui module that references FlaUI.* is imported — mirrors flaui_adapter.
from flaui.lib.pythonnet_bridge import setup_pythonnet_bridge  # noqa: E402

setup_pythonnet_bridge()

from flaui.core.input import Keyboard  # noqa: E402
from flaui.core.definitions import ControlType  # noqa: E402
from flaui.lib.enums import UIAutomationTypes  # noqa: E402
from flaui.modules.automation import Automation  # noqa: E402

# ---------------------------------------------------------------------------
# Executable paths (relative to the repo root working directory)
# ---------------------------------------------------------------------------

IDE_EXE = r"bin\Debug\net9.0-windows\WpfTestIde.exe"
SAMPLE_APP_EXE = r"Tests\bin\SampleWPFApp\Debug\net9.0-windows\SampleWpfApp.exe"

# Control type name -> flaui ControlType enum value
_CONTROL_TYPE_MAP = {
    "Button": ControlType.Button,
    "CheckBox": ControlType.CheckBox,
    "Edit": ControlType.Edit,
    "ComboBox": ControlType.ComboBox,
    "ListItem": ControlType.ListItem,
    "Window": ControlType.Window,
    "Menu": ControlType.Menu,
    "TextBox": ControlType.Text,
    "List": ControlType.List,
    "Tab": ControlType.Tab,
    "TabItem": ControlType.TabItem,
    "RadioButton": ControlType.RadioButton,
    "Hyperlink": ControlType.Hyperlink,
    "TreeItem": ControlType.TreeItem,
    "DataItem": ControlType.DataItem,
    "Tree": ControlType.Tree,
    "DataGrid": ControlType.DataGrid,
    "Slider": ControlType.Slider,
    "Spinner": ControlType.Spinner,
    "ProgressBar": ControlType.ProgressBar,
    "Calendar": ControlType.Calendar,
    "Header": ControlType.Header,
    "HeaderItem": ControlType.HeaderItem,
    "Thumb": ControlType.Thumb,
    "TitleBar": ControlType.TitleBar,
    "Image": ControlType.Image,
}

# Keyboard shortcuts for IDE menu paths that flaui menu traversal may not
# reach reliably (WPF menus expose items as separate Automation elements).
MENU_SHORTCUTS = {
    ("File", "Save Script"): "^s",
    ("File", "Open Script"): "^o",
    ("Run", "Run Script"): "^r",
}


# ---------------------------------------------------------------------------
# Element wrapper
# ---------------------------------------------------------------------------


class FlauiElement:
    """Thin wrapper around a flaui AutomationElement for IDE integration tests."""

    def __init__(self, element, automation):
        self._element = element
        self._automation = automation

    @property
    def cf(self):
        return self._automation.cf

    @property
    def name(self) -> str:
        try:
            return self._element.name or ""
        except Exception:
            return ""

    @property
    def title(self) -> str:
        try:
            return self._element.title or ""
        except Exception:
            return self.name

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

    def exists(self) -> bool:
        return self._element is not None

    def set_focus(self):
        try:
            self._element.set_focus()
        except Exception:
            pass

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
        """Set text on an Edit/TextBox control (enter() first, Text property fallback)."""
        try:
            self._element.as_text_box().enter(text)
            return
        except Exception:
            pass
        try:
            self._element.raw_element.Text = text
        except Exception:
            pass

    def get_text(self) -> str:
        try:
            return self._element.text
        except Exception:
            return self.name or ""

    def select_combo(self, text: str):
        """Select an item in a ComboBox by text.

        Tries: editable_text setter -> expand + select matching item -> select().
        """
        try:
            combo = self._element.as_combo_box()
        except Exception:
            combo = None
        if combo is None:
            return
        try:
            if combo.is_editable:
                combo.editable_text = text
                return
        except Exception:
            pass
        try:
            combo.expand()
            time.sleep(0.3)
            for item in combo.items:
                try:
                    if (item.text or item.name or "") == text:
                        item.select()
                        time.sleep(0.3)
                        combo.collapse()
                        return
                except Exception:
                    continue
            combo.collapse()
        except Exception:
            try:
                combo.collapse()
            except Exception:
                pass
        try:
            combo.select(text)
        except Exception:
            pass
        try:
            combo.collapse()
        except Exception:
            pass

    def type_keys(self, keys: str):
        Keyboard.type(keys)

    def wait_visible(self, timeout=10):
        end_time = time.time() + timeout / 1000.0
        while time.time() < end_time:
            if self.is_visible:
                return True
            time.sleep(0.1)
        return False

    def find_child(self, automation_id=None, control_type=None, name=None):
        """Find first descendant matching automation_id, control_type, or name."""
        cf = self.cf
        cond = None
        if automation_id:
            cond = cf.by_automation_id(automation_id)
        elif name:
            cond = cf.by_name(name)
        elif control_type:
            ct = _CONTROL_TYPE_MAP.get(control_type)
            if ct:
                cond = cf.by_control_type(ct)
        try:
            el = (
                self._element.find_first_descendant(condition=cond)
                if cond
                else self._element.find_first_descendant()
            )
        except Exception:
            el = None
        return FlauiElement(el, self._automation) if el else None

    def find_all(self, control_type=None):
        """Find all descendants, optionally filtered by control_type."""
        cf = self.cf
        cond = None
        if control_type:
            ct = _CONTROL_TYPE_MAP.get(control_type)
            if ct:
                cond = cf.by_control_type(ct)
        try:
            els = (
                self._element.find_all_descendants(condition=cond)
                if cond
                else self._element.find_all_descendants()
            )
        except Exception:
            els = []
        return [FlauiElement(e, self._automation) for e in els]

    def rectangle(self):
        try:
            rect = self._element.bounding_rectangle
            return (rect.left, rect.top, rect.right, rect.bottom)
        except Exception:
            return (0, 0, 0, 0)


# ---------------------------------------------------------------------------
# Application wrapper
# ---------------------------------------------------------------------------


class FlauiApp:
    """Owns one flaui Automation instance and its launched application.

    Each instance owns its own Automation so launching multiple applications
    (e.g. IDE + SampleWpfApp) does not share state.
    """

    def __init__(self, ui_type=UIAutomationTypes.UIA3):
        self.automation = Automation(ui_type)
        self.app = self.automation.application

    def launch(self, exe_path):
        self.app.launch(exe_path)
        time.sleep(2)
        return self

    def kill(self):
        try:
            self.app.kill()
        except Exception:
            pass

    def main_window(self, title_pattern=".*WPF Test IDE.*", timeout=30):
        """Return the main window matching title_pattern, waiting for it to appear."""
        pattern = re.compile(title_pattern)
        end_time = time.time() + timeout
        last_err = None
        while time.time() < end_time:
            try:
                win = self.app.get_main_window(self.automation)
                if pattern.search(win.name or ""):
                    return FlauiElement(win, self.automation)
            except Exception as e:
                last_err = e
            time.sleep(0.5)
        raise TimeoutError(f"Main window '{title_pattern}' not found: {last_err}")

    def desktop_windows(self):
        """Return all top-level windows using the automation's desktop element."""
        result = []
        try:
            desktop = self.automation.automation_base.get_desktop()
            cond = self.automation.cf.by_control_type(ControlType.Window)
            found = desktop.find_all_descendants(condition=cond)
            for elem in found:
                result.append(FlauiElement(elem, self.automation))
        except Exception:
            pass
        return result

    def find_window_by_title(self, title_substring, timeout=15):
        """Find a top-level window whose title contains title_substring."""
        end_time = time.time() + timeout
        found = None
        while time.time() < end_time and found is None:
            try:
                for w in self.desktop_windows():
                    try:
                        if title_substring in (w.title or w.name):
                            found = w
                            break
                    except Exception:
                        continue
            except Exception:
                pass
            if found is None:
                time.sleep(0.5)
        if found is not None:
            try:
                found.wait_visible(timeout=5)
            except Exception:
                pass
        return found


# ---------------------------------------------------------------------------
# Thin, flaui-backed convenience operations (used by the test modules)
# ---------------------------------------------------------------------------


def find_by_auto_id(element: FlauiElement, automation_id: str, control_type: Optional[str] = None) -> List[FlauiElement]:
    """Find descendants matching AutomationId (optionally filtered by control type)."""
    matched = element.find_child(automation_id=automation_id, control_type=control_type)
    return [matched] if matched else []


def click_by_auto_id(element: FlauiElement, automation_id: str, timeout=10, control_type: Optional[str] = None):
    """Click a control located by AutomationId, retrying until timeout."""
    end_time = time.time() + timeout
    last_err = None
    while time.time() < end_time:
        matched = find_by_auto_id(element, automation_id, control_type)
        if matched:
            try:
                element.set_focus()
            except Exception:
                pass
            matched[0].click()
            return matched[0]
        last_err = f"Control '{automation_id}' not found"
        time.sleep(0.5)
    raise TimeoutError(last_err)


def set_text_by_auto_id(element: FlauiElement, automation_id: str, text: str, timeout=10, control_type: Optional[str] = None):
    """Set text on an Edit/TextBox control located by AutomationId."""
    matched = find_by_auto_id(element, automation_id, "Edit")
    if not matched:
        matched = find_by_auto_id(element, automation_id)
    if not matched:
        raise TimeoutError(f"Edit control '{automation_id}' not found")
    try:
        matched[0].click()
    except Exception:
        pass
    matched[0].set_text(text)
    return matched[0]


def toggle_by_auto_id(element: FlauiElement, automation_id: str, timeout=10, control_type: Optional[str] = None):
    """Toggle a CheckBox located by AutomationId."""
    matched = find_by_auto_id(element, automation_id, "CheckBox")
    if not matched:
        raise TimeoutError(f"CheckBox '{automation_id}' not found")
    matched[0].toggle()
    return matched[0]


def get_text_by_auto_id(element: FlauiElement, automation_id: str, timeout=10):
    """Get the text of a control located by AutomationId."""
    matched = find_by_auto_id(element, automation_id)
    if not matched:
        raise TimeoutError(f"Control '{automation_id}' not found")
    return matched[0].get_text()


def click_menu_item(element: FlauiElement, menu_path: str, timeout=10):
    """Click a menu item by path, e.g. 'File > Save Script'.

    Tries flaui native menu traversal first, then falls back to the IDE's
    keyboard shortcuts (Ctrl+S save, Ctrl+O open, Ctrl+R run).
    """
    parts = [p.strip() for p in menu_path.split(">")]
    key = (parts[0], parts[1] if len(parts) > 1 else None)

    # flaui native: walk the Menu/MenuItem tree one part at a time.
    try:
        menu_el = element.find_child(control_type="Menu")
        current = menu_el
        for part in parts:
            if current is None:
                break
            item = None
            try:
                item = current.as_menu().get_item_by_name(part)
            except Exception:
                item = None
            if item is None:
                for m in current.find_all(control_type="MenuItem"):
                    if m.name == part or m.text == part or m.automation_id == part:
                        item = m
                        break
            if item is None:
                break
            try:
                item.expand()
            except Exception:
                pass
            time.sleep(0.2)
            current = item
        else:
            current.invoke()
            return
    except Exception:
        pass

    # Keyboard shortcut fallback for known IDE bindings.
    shortcut = MENU_SHORTCUTS.get(key)
    if shortcut:
        Keyboard.type(shortcut)
        return
    raise TimeoutError(f"Menu '{menu_path}' not found and no keyboard shortcut available")


__all__ = [
    "FlauiApp",
    "FlauiElement",
    "IDE_EXE",
    "SAMPLE_APP_EXE",
    "find_by_auto_id",
    "click_by_auto_id",
    "set_text_by_auto_id",
    "toggle_by_auto_id",
    "get_text_by_auto_id",
    "click_menu_item",
]
