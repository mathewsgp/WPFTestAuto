"""
FLaUI.RobotFramework — Layer 4 driver wrapper for FlaUI.

This module provides:
1. `FlaUIDriver` — Python driver class implementing the Layer 3 interface
   for the real SampleWpfApp using the robotframework-flaui package.
2. `FlaUILibrary` — Robot Framework library exposing FlaUI keywords directly.
"""

import sys
import os
from typing import List, Optional

# Add parent directories to path for mock app fallback
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "mock_wpf_app"))
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", ".."))

# WPF control type -> UIA control type mapping
# UI Automation maps WPF controls to different UIA control types
_WPF_TO_UIA_TYPE = {
    "Window": "Window",
    "TextBox": "Edit",
    "PasswordBox": "Edit",
    "Button": "Button",
    "CheckBox": "CheckBox",
    "RadioButton": "RadioButton",
    "ComboBox": "ComboBox",
    "ListBox": "ListBox",
    "DataGrid": "DataGrid",
    "TabControl": "Tab",
    "TabItem": "TabItem",
    "Label": "Text",
    "TextBlock": "Text",
    "ProgressBar": "ProgressBar",
    "Slider": "Slider",
    "Menu": "Menu",
    "MenuItem": "MenuItem",
    "TreeView": "Tree",
    "TreeViewItem": "TreeItem",
    "ListView": "List",
    "StatusBar": "StatusBar",
    "ToolBar": "ToolBar",
    "Separator": "Separator",
    "GroupBox": "Group",
    "Expander": "Group",
    "ScrollViewer": "ScrollBar",
    "Border": "Pane",
    "Grid": "Pane",
    "StackPanel": "Pane",
    "Canvas": "Pane",
    "Image": "Image",
    "MediaElement": "Image",
}


def _translate_wpf_to_uia_xpath(xpath: str) -> str:
    """Translate WPF control types in XPath to UIA control types.
    
    Args:
        xpath: XPath with WPF control types (TextBox, PasswordBox, etc.)
        
    Returns:
        XPath with UIA control types (Edit, Button, etc.)
    """
    result = xpath
    for wpf_type, uia_type in _WPF_TO_UIA_TYPE.items():
        result = result.replace(f"{wpf_type}[", f"{uia_type}[")
    return result


class FlaUIDriver:
    """Real FlaUI driver — locates and interacts with elements in the
    running SampleWpfApp using the robotframework-flaui package.
    
    The real FlaUI library uses XPath strings as element identifiers,
    so this driver stores the resolved XPath and passes it to all
    subsequent operations.
    
    Strategy Priority:
    1. AutomationId (most reliable)
    2. Name (second choice)
    3. XPath (fallback)
    """

    name = "FlaUI"

    def __init__(self, app_pid: Optional[int] = None):
        """Initialize the FlaUI driver.
        
        Args:
            app_pid: Process ID of the SampleWpfApp to attach to.
                    If None, will try to find an existing SampleWpfApp.
        """
        self._app_pid = app_pid
        self._lib = None
        self._connected = False
        self._init_library()

    def _init_library(self):
        """Initialize the FlaUILibrary and attach to the application."""
        try:
            import FlaUILibrary
            self._lib = FlaUILibrary.FlaUILibrary()
            self._rf_lib_class = FlaUILibrary.FlaUILibrary
            self._connected = False
        except ImportError as e:
            raise ImportError("FlaUILibrary not available. Install with: pip install robotframework-flaui") from e

    def _ensure_attached(self):
        """Ensure the driver is attached to the application."""
        if self._connected and self._lib is not None:
            return

        if self._lib is None:
            self._init_library()

        # Try to attach by PID first if we have one
        if self._app_pid:
            try:
                self._lib.attach_application_by_pid(self._app_pid)
                self._connected = True
                return
            except Exception:
                pass

        # Fallback: attach by name
        try:
            self._lib.attach_application_by_name("SampleWpfApp")
            self._connected = True
        except Exception:
            try:
                self._lib.attach_application_by_name("dotnet")
                self._connected = True
            except Exception:
                pass

        if not self._connected:
            raise ConnectionError("FlaUI: Could not attach to application. Ensure the app is running.")

    def _to_xpath(self, element) -> str:
        """Convert element handle to XPath string for FlaUI keywords.
        
        The real FlaUI library uses XPath strings as identifiers.
        This driver returns XPath strings from find_element, so
        element handles are already XPath strings.
        """
        if isinstance(element, str):
            return element
        # If we ever get an AutomationElement, we can't convert it back to XPath
        # This should not happen with the current implementation
        raise TypeError(f"FlaUIDriver expects XPath string handles, got {type(element)}")

    def find_element(self, strategy: dict):
        """Locates an element using FlaUI strategy.
        
        Args:
            strategy: Dict with searchBy and value keys.
                      
        Returns:
            XPath string identifying the found element.
            
        Raises:
            ElementNotFoundError: If no matching element is found.
        """
        from api.exceptions import ElementNotFoundError
        
        search_by = strategy.get("searchBy", "AutomationId")
        value = strategy.get("value")
        
        if search_by == "AutomationId":
            xpath = f"//*[@AutomationId='{value}']"
            try:
                self._ensure_attached()
                self._lib.find_one_element(xpath)
                return xpath
            except Exception as e:
                raise ElementNotFoundError(f"FlaUI: no element with AutomationId='{value}': {e}")
        
        elif search_by == "Name":
            xpath = f"//*[@Name='{value}']"
            try:
                self._ensure_attached()
                self._lib.find_one_element(xpath)
                return xpath
            except Exception as e:
                raise ElementNotFoundError(f"FlaUI: no element with Name='{value}': {e}")
        
        elif search_by == "XPath":
            uia_xpath = _translate_wpf_to_uia_xpath(value)
            try:
                self._ensure_attached()
                self._lib.find_one_element(uia_xpath)
                return uia_xpath
            except Exception as e:
                raise ElementNotFoundError(f"FlaUI: no element found for XPath '{value}' (translated: '{uia_xpath}'): {e}")
        
        else:
            raise ElementNotFoundError(f"Unsupported FlaUI searchBy: {search_by}")

    def find_elements(self, strategy: dict) -> List:
        """Locates all elements matching the FlaUI strategy.
        
        Args:
            strategy: Dict with searchBy and value keys.
                      
        Returns:
            List of XPath strings for all matching elements (may be empty).
        """
        search_by = strategy.get("searchBy", "AutomationId")
        value = strategy.get("value")
        
        try:
            self._ensure_attached()
            if search_by == "AutomationId":
                xpath = f"//*[@AutomationId='{value}']"
                self._lib.find_all_elements(xpath)
                return [xpath]
            elif search_by == "Name":
                xpath = f"//*[@Name='{value}']"
                self._lib.find_all_elements(xpath)
                return [xpath]
            elif search_by == "XPath":
                uia_xpath = _translate_wpf_to_uia_xpath(value)
                self._lib.find_all_elements(uia_xpath)
                return [uia_xpath]
            else:
                return []
        except Exception:
            return []

    def invoke(self, element):
        """Click/invoke an element."""
        xpath = self._to_xpath(element)
        self._ensure_attached()
        self._lib.click(xpath)

    def set_value(self, element, value: str):
        """Set text value on an input element."""
        xpath = self._to_xpath(element)
        self._ensure_attached()
        try:
            self._lib.set_text_to_textbox(xpath, value)
        except Exception:
            pass
        try:
            self._lib.select_combobox_item_by_name(xpath, value)
        except Exception:
            raise ElementNotInteractableError(f"FlaUI: cannot set value on element: {xpath}")

    def get_text(self, element) -> str:
        """Get the text content of an element."""
        xpath = self._to_xpath(element)
        self._ensure_attached()
        try:
            text = self._lib.get_text_from_textbox(xpath)
            if text:
                return text
        except Exception:
            pass
        try:
            texts = self._lib.get_all_selected_texts_from_combobox(xpath)
            if texts:
                return texts[0]
        except Exception:
            pass
        try:
            data = self._lib.get_all_data_from_grid(xpath)
            if isinstance(data, list):
                lines = []
                for row in data:
                    lines.append(",".join(row if isinstance(row, list) else [str(row)]))
                return "\n".join(lines)
            return str(data)
        except Exception:
            pass
        try:
            return self._lib.get_name_from_element(xpath)
        except Exception:
            return ""

    def is_visible(self, element) -> bool:
        """Check if an element is visible."""
        xpath = self._to_xpath(element)
        self._ensure_attached()
        try:
            return self._lib.is_visible(xpath)
        except Exception:
            return False

    def is_enabled(self, element) -> bool:
        """Check if an element is enabled."""
        xpath = self._to_xpath(element)
        self._ensure_attached()
        try:
            return self._lib.is_element_enabled(xpath)
        except Exception:
            return False

    def is_actionable(self, element) -> bool:
        """Check if an element is both visible and enabled."""
        return self.is_visible(element) and self.is_enabled(element)

    def get_attribute(self, element, attribute_name: str) -> Optional[str]:
        """Get a specific attribute value from an element."""
        xpath = self._to_xpath(element)
        self._ensure_attached()
        try:
            return self._lib.get_property_from_element(xpath, attribute_name)
        except Exception:
            return None

    def capture_screenshot(self, element=None) -> bytes:
        """Capture a screenshot."""
        try:
            return self._lib.take_screenshot()
        except Exception:
            return b""

    def toggle(self, element, state: bool = None):
        """Toggle a checkbox or toggle button."""
        xpath = self._to_xpath(element)
        self._ensure_attached()
        if state is None:
            self._lib.click(xpath)
        else:
            self._lib.set_checkbox_state(xpath, state)

    def double_click(self, element):
        """Double-click an element."""
        xpath = self._to_xpath(element)
        self._ensure_attached()
        self._lib.double_click(xpath)

    def right_click(self, element):
        """Right-click an element."""
        xpath = self._to_xpath(element)
        self._ensure_attached()
        self._lib.right_click(xpath)

    def press_keys(self, element, keys: str):
        """Press keys into an element."""
        xpath = self._to_xpath(element)
        self._ensure_attached()
        self._lib.press_keys(keys, xpath)

    def drag_drop(self, element, target_element):
        """Drag an element and drop it on a target."""
        source_xpath = self._to_xpath(element)
        target_xpath = target_element if isinstance(target_element, str) else target_element.get("xpath", "")
        self._ensure_attached()
        self._lib.drag_and_drop(source_xpath, target_xpath)

    def hover(self, element):
        """Hover over an element."""
        xpath = self._to_xpath(element)
        self._ensure_attached()
        self._lib.move_to(xpath)

    def scroll(self, element, direction: str):
        """Scroll an element in a direction."""
        xpath = self._to_xpath(element)
        self._ensure_attached()
        if direction.lower() in ("down", "pagedown"):
            self._lib.scroll_down(xpath, 50)
        elif direction.lower() in ("up", "pageup"):
            self._lib.scroll_up(xpath, 50)
        else:
            self._lib.scroll_down(xpath, 50)

    def get_data_grid_content_ocr(self, element) -> str:
        """OCR-based DataGrid content extraction.
        
        Returns grid text in CSV format: SKU,Qty
        """
        xpath = self._to_xpath(element)
        self._ensure_attached()
        try:
            data = self._lib.get_all_data_from_grid(xpath)
            if isinstance(data, list):
                import io, csv
                output = io.StringIO()
                writer = csv.writer(output)
                for row in data:
                    writer.writerow(row if isinstance(row, list) else [row])
                return output.getvalue()
            return str(data)
        except Exception:
            return ""

    def close(self):
        """Clean up driver resources."""
        try:
            if self._lib is not None:
                self._lib.close_application()
        except Exception:
            pass
        self._connected = False


class FlaUILibrary:
    """Robot Framework library exposing FlaUI keywords directly (rarely
    used directly by test authors — Layer 3 is the normal entry point).
    Kept here to show the wrapper is independently usable/testable.
    """
    ROBOT_LIBRARY_SCOPE = "GLOBAL"

    def __init__(self):
        try:
            from FlaUILibrary import FlaUILibrary as RFlaUILibrary
            self._lib = RFlaUILibrary()
        except ImportError as e:
            raise ImportError(f"FlaUILibrary not available: {e}. Install with: pip install robotframework-flaui")

    def flaui_find_element(self, automation_id):
        return self._lib.find_one_element(f"//*[@AutomationId='{automation_id}']")

    def flaui_invoke(self, automation_id):
        el = self.flaui_find_element(automation_id)
        self._lib.click(el)

    def flaui_set_value(self, automation_id, value):
        el = self.flaui_find_element(automation_id)
        self._lib.set_text_to_textbox(el, value)

    def flaui_get_text(self, automation_id):
        el = self.flaui_find_element(automation_id)
        return self._lib.get_text_from_textbox(el)
