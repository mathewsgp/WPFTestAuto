"""
FLaUI.RobotFramework — Layer 4 driver wrapper for FlaUI.

Reference implementation notes (production deployment)
--------------------------------------------------------
In a real deployment, FlaUI is a .NET library (FlaUI.Core / FlaUI.UIA3)
and this wrapper would not call it in-process from Python. The standard
integration pattern is one of:

  1. Robot Framework Remote Library protocol: a small .NET "remote server"
     process hosts FlaUI and exposes these same keyword methods over
     XML-RPC; this Python class becomes a thin RemoteLibrary proxy
     (see: robotframework-remoteserver, or Robot Framework's built-in
     Remote library).
  2. pythonnet (clr) bridging: load FlaUI's .NET assemblies directly into
     the Python process via `import clr; clr.AddReference("FlaUI.UIA3")`.

Either way, the METHOD SIGNATURES below stay identical — this is the
"API parity" contract that lets Layer 3 swap FlaUI <-> WPFSpy <-> Sikuli
without any change to calling code. This file implements those same
signatures against the in-repo mock WPF application so the framework is
fully runnable and testable without Windows/.NET.
"""

import sys
import os

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "drivers", "mock_wpf_app"))
from mock_app import APP_INSTANCE, ElementNotFoundError, ElementNotInteractableError  # noqa: E402


class FlaUIDriver:
    """FlaUI driver — locates elements by AutomationId, Name, or Type+Index.
    
    Strategy Priority:
    1. AutomationId (most reliable)
    2. Name (second choice)
    3. Type + Index (sibling fallback)
    """

    name = "FlaUI"

    def find_element(self, strategy: dict):
        """Locates an element using FlaUI strategy.
        
        Args:
            strategy: Dict with searchBy and value keys.
                     Supports: AutomationId, Name, TypeAndIndex, XPath
        """
        search_by = strategy.get("searchBy", "AutomationId")
        value = strategy.get("value")
        
        if search_by == "AutomationId":
            ctrl = APP_INSTANCE.find_by_automation_id(value)
            if ctrl is None:
                raise ElementNotFoundError(f"FlaUI: no element with AutomationId='{value}'")
            return ctrl
        
        elif search_by == "Name":
            ctrl = APP_INSTANCE.find_by_name(value)
            if ctrl is None:
                raise ElementNotFoundError(f"FlaUI: no element with Name='{value}'")
            return ctrl
        
        elif search_by == "TypeAndIndex":
            # Parse "ControlType[index]" format
            import re
            match = re.match(r"(\w+)\[(\d+)\]", value)
            if match:
                ctrl_type = match.group(1)
                index = int(match.group(2))
                ctrl = APP_INSTANCE.find_by_control_type_and_index(ctrl_type, index)
                if ctrl is None:
                    raise ElementNotFoundError(f"FlaUI: no element {ctrl_type}[{index}]")
                return ctrl
            raise ElementNotFoundError(f"Invalid TypeAndIndex format: {value}")
        
        elif search_by == "XPath":
            ctrl = APP_INSTANCE.find_by_xpath(value)
            if ctrl is None:
                raise ElementNotFoundError(f"FlaUI: no element found for XPath '{value}'")
            return ctrl
        
        else:
            raise ElementNotFoundError(f"Unsupported FlaUI searchBy: {search_by}")

    def invoke(self, element):
        APP_INSTANCE.invoke(element)

    def set_value(self, element, value: str):
        APP_INSTANCE.set_value(element, value)

    def get_text(self, element) -> str:
        return APP_INSTANCE.get_text(element)

    def is_visible(self, element) -> bool:
        return APP_INSTANCE.is_visible(element)

    def toggle(self, element, state: bool = None):
        APP_INSTANCE.invoke(element)  # mock: toggle behaves as invoke

    def get_data_grid_content_ocr(self, element) -> str:
        """OCR-based DataGrid content extraction.
        
        Returns grid text in CSV format: SKU,Qty
        """
        # Get grid element text
        grid_text = APP_INSTANCE.get_text(element)
        
        # Parse grid text (format: "SKU,Qty\nSKU-1001,2\n...")
        if not grid_text or grid_text.strip() == "SKU,Qty":
            return "SKU,Qty"
        
        return grid_text


class FlaUILibrary:
    """Robot Framework library exposing FlaUI keywords directly (rarely
    used directly by test authors — Layer 3 is the normal entry point).
    Kept here to show the wrapper is independently usable/testable.
    """
    ROBOT_LIBRARY_SCOPE = "GLOBAL"

    def __init__(self):
        self.driver = FlaUIDriver()

    def flaui_find_element(self, automation_id):
        return self.driver.find_element({"searchBy": "AutomationId", "value": automation_id})

    def flaui_invoke(self, automation_id):
        el = self.flaui_find_element(automation_id)
        self.driver.invoke(el)

    def flaui_set_value(self, automation_id, value):
        el = self.flaui_find_element(automation_id)
        self.driver.set_value(el, value)

    def flaui_get_text(self, automation_id):
        el = self.flaui_find_element(automation_id)
        return self.driver.get_text(el)
