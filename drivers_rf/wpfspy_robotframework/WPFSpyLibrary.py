"""
WPFSpy.RobotFramework — Layer 4 driver wrapper for WPFSpy.

Two implementations live in this file, selected by the WPFSPY_MODE
environment variable:

  WPFSPY_MODE=real (Windows only)
    WPFSpyRealDriver — a REAL Named Pipe IPC client that talks to the
    actual in-process Spy Agent (WpfSpyAgent/, a .NET class library)
    hosted by the real SampleWpfApp/ (a .NET/WPF application). This is
    the genuine implementation: driver -> Named Pipe -> injected agent ->
    live WPF visual tree. Requires pywin32 and a running SampleWpfApp
    built with WPFSPY_AGENT_ENABLED=1. See docs/WPFSPY_MODULE.md and
    docs/PROTOCOL.md for the full wire protocol and build/run steps.

  WPFSPY_MODE=mock (default)
    WPFSpyMockDriver — talks to the in-repo Python mock WPF application
    (drivers/mock_wpf_app/) instead, so the rest of the framework (Layers
    1-3, the repositories, the reusable modules, the recorder) stays
    runnable and demonstrable on any OS, including this Linux sandbox,
    without requiring Windows/.NET/a real injected agent.

Both classes expose IDENTICAL method signatures
(find_element/invoke/set_value/get_text/is_visible/toggle) — the same
"API parity" contract FlaUIDriver implements — so Layer 3 never needs to
know or care which one is active.
"""

import sys
import time
import os
import json
import base64
import csv
import io
from typing import List, Optional

_THIS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(_THIS_DIR, "..", "..", "drivers", "mock_wpf_app"))
from mock_app import APP_INSTANCE, ElementNotFoundError, ElementNotInteractableError  # noqa: E402


PIPE_NAME = os.environ.get("WPFSPY_PIPE_NAME", "WPFSpyAgentPipe")


# ---------------------------------------------------------------------------
# REAL driver — Named Pipe client talking to the actual injected Spy Agent
# ---------------------------------------------------------------------------
class WPFSpyRealDriver:
    """Real WPFSpy driver: sends line-delimited JSON commands over a
    Windows Named Pipe to the in-process Spy Agent hosted by
    SampleWpfApp (see WpfSpyAgent/SpyAgentHost.cs). Requires pywin32 and
    Windows — only imported/used when WPFSPY_MODE=real.

    The agent re-resolves the target element fresh from the live visual
    tree on every call (by Name), so `find_element` here just confirms
    the element currently exists and returns a lightweight handle; it
    does not cache a live object reference across calls. This avoids
    stale-element issues across page/window navigation.
    """

    name = "WPFSpy"

    def __init__(self, pipe_name: str = PIPE_NAME):
        self.pipe_name = pipe_name

    def _send(self, command: str, **params) -> dict:
        import win32file  # pywin32 — Windows only

        pipe_path = rf"\\.\pipe\{self.pipe_name}"
        request = json.dumps({"command": command, **params}) + "\n"
        print(f"[WPFSpyReal] _send: command={command}, params={params}")

        max_pipe_retries = 20
        pipe_retry_delay = 0.5
        last_pipe_error = None
        for _ in range(max_pipe_retries):
            try:
                handle = win32file.CreateFile(
                    pipe_path,
                    win32file.GENERIC_READ | win32file.GENERIC_WRITE,
                    0, None,
                    win32file.OPEN_EXISTING,
                    0, None,
                )
                break
            except Exception as e:
                last_pipe_error = e
                time.sleep(pipe_retry_delay)
        else:
            raise last_pipe_error

        try:
            win32file.WriteFile(handle, request.encode("utf-8"))
            buffer = b""
            while not buffer.endswith(b"\n"):
                _, chunk = win32file.ReadFile(handle, 4096)
                if not chunk:
                    break
                buffer += chunk
            result = json.loads(buffer.decode("utf-8"))
            print(f"[WPFSpyReal] _send: response={result}")
            return result
        finally:
            win32file.CloseHandle(handle)

    def find_element(self, locator: dict):
        """Locates a single element using WPFSpy strategy.
        
        Args:
            locator: Dict with searchBy and value keys.
                    {"searchBy": "XPath", "value": "..."} or {"searchBy": "Name", "value": "..."}
                    
        Returns:
            ElementHandle for the found element.
            
        Raises:
            ElementNotFoundError: If no matching element is found.
        """
        search_by = locator.get("searchBy", "XPath")
        value = locator.get("value")
        
        # Retry logic to handle timing after ResetState or window transitions
        max_retries = 15
        retry_delay = 0.5
        
        for attempt in range(max_retries):
            if search_by == "XPath":
                response = self._send("FindByXPath", xpath=value)
                if response.get("success"):
                    return {"xpath": value}
            else:
                response = self._send("Find", name=value)
                if response.get("success"):
                    return {"name": value}
            
            if attempt < max_retries - 1:
                time.sleep(retry_delay)
        
        # All retries failed
        if search_by == "XPath":
            raise ElementNotFoundError(
                f"WPFSpy: no element found for XPath '{value}' after {max_retries} attempts"
            )
        raise ElementNotFoundError(
            f"WPFSpy: no element with Name='{value}' after {max_retries} attempts"
        )

    def find_elements(self, locator: dict) -> List[dict]:
        """Locates all elements matching the WPFSpy strategy.
        
        Note: The real WPFSpy agent doesn't support find_elements natively.
        This implementation uses the real driver for single-element finding.
        
        Args:
            locator: Dict with searchBy and value keys.
            
        Returns:
            List of ElementHandles for all matching elements.
        """
        # For real driver, we return a single-element list
        # In production, the C# agent should implement FindElements
        try:
            element = self.find_element(locator)
            return [element]
        except ElementNotFoundError:
            return []

    def invoke(self, element):
        """Click/invoke an element."""
        if "xpath" in element:
            response = self._send("Invoke", xpath=element["xpath"])
        else:
            response = self._send("Invoke", name=element["name"])
        if not response.get("success"):
            raise ElementNotInteractableError(response.get("error"))

    def set_value(self, element, value: str):
        """Set text value on an input element."""
        if "xpath" in element:
            response = self._send("SetValue", xpath=element["xpath"], value=value)
        else:
            response = self._send("SetValue", name=element["name"], value=value)
        if not response.get("success"):
            raise ElementNotInteractableError(response.get("error"))

    def get_text(self, element) -> str:
        """Get the text content of an element."""
        if "xpath" in element:
            response = self._send("GetText", xpath=element["xpath"])
        else:
            response = self._send("GetText", name=element["name"])
        if not response.get("success"):
            raise ElementNotInteractableError(response.get("error"))
        return response.get("data", "")

    def is_visible(self, element) -> bool:
        """Check if an element is visible."""
        max_retries = 3
        retry_delay = 0.2
        for attempt in range(max_retries):
            if "xpath" in element:
                response = self._send("IsVisible", xpath=element["xpath"])
            else:
                response = self._send("IsVisible", name=element["name"])
            if response.get("success"):
                return response.get("data") == "true"
            if attempt < max_retries - 1:
                time.sleep(retry_delay)
        return False

    def is_enabled(self, element) -> bool:
        """Check if an element is enabled.
        
        Note: This requires the C# agent to implement IsEnabled command.
        Falls back to using IsVisible if IsEnabled is not supported.
        """
        if "xpath" in element:
            response = self._send("IsEnabled", xpath=element["xpath"])
        else:
            response = self._send("IsEnabled", name=element["name"])
        
        if response.get("success"):
            return response.get("data") == "true"
        
        # Fallback: assume enabled if we can find it
        return True

    def is_actionable(self, element) -> bool:
        """Check if an element is both visible and enabled."""
        return self.is_visible(element) and self.is_enabled(element)

    def get_attribute(self, element, attribute_name: str) -> Optional[str]:
        """Get a specific attribute value from an element.
        
        Note: This requires the C# agent to implement GetAttribute command.
        For now, returns None as this is not yet implemented in the agent.
        """
        # In production, this would call a GetAttribute command
        return None

    def capture_screenshot(self, element=None) -> bytes:
        """Capture a screenshot.
        
        Note: This requires the C# agent to implement CaptureScreenshot command.
        For now, returns empty bytes.
        """
        # In production, this would call a CaptureScreenshot command
        return b""

    def toggle(self, element, state: bool = None):
        """Toggle a checkbox or toggle button."""
        if "xpath" in element:
            response = self._send("Toggle", xpath=element["xpath"])
        else:
            response = self._send("Toggle", name=element["name"])
        if not response.get("success"):
            raise ElementNotInteractableError(response.get("error"))

    def get_data_grid_content_ocr(self, element) -> str:
        """Captures a screenshot of the DataGrid element and
        runs OCR on it to extract cell content as CSV text."""
        if "xpath" in element:
            response = self._send("GetDataGridContentOcr", xpath=element["xpath"])
        else:
            response = self._send("GetDataGridContentOcr", name=element["name"])
        if not response.get("success"):
            raise ElementNotInteractableError(response.get("error"))
        base64_image = response.get("data", "")
        if not base64_image:
            return ""
        try:
            import pytesseract
            from PIL import Image
        except ImportError:
            raise RuntimeError(
                "OCR requires pytesseract and Pillow. "
                "Install with: pip install pytesseract Pillow"
            )
        image_bytes = base64.b64decode(base64_image)
        image = Image.open(io.BytesIO(image_bytes))
        ocr_text = pytesseract.image_to_string(image)
        return self._parse_ocr_to_csv(ocr_text)

    @staticmethod
    def _parse_ocr_to_csv(ocr_text: str) -> str:
        """Parses OCR text into CSV format. Assumes the OCR output
        is a tabular layout where rows are separated by newlines
        and columns are separated by whitespace or tabs."""
        lines = [line.strip() for line in ocr_text.strip().splitlines() if line.strip()]
        if not lines:
            return ""
        output = io.StringIO()
        writer = csv.writer(output)
        for line in lines:
            # Split on whitespace for column detection;
            # preserve quoted fields if they exist.
            cells = line.split()
            writer.writerow(cells)
        return output.getvalue()
    
    def close(self):
        """Clean up driver resources."""
        pass  # Named pipe connections are stateless

    # -------------------------------------------------------------------------
    # UIA Event Recording Methods
    # -------------------------------------------------------------------------
    def start_recording(self) -> dict:
        """Start recording UI Automation events.
        
        Hooks into the WPF application to capture user interactions
        (clicks, text input, focus changes, selections) in real-time.
        
        Returns:
            dict with status message.
        """
        response = self._send("StartRecording")
        if response.get("success"):
            print("[WPFSpy] UIA Event Recording started")
        return response

    def stop_recording(self) -> dict:
        """Stop recording UI Automation events.
        
        Returns:
            dict with status message.
        """
        response = self._send("StopRecording")
        if response.get("success"):
            print("[WPFSpy] UIA Event Recording stopped")
        return response

    def get_recording_status(self) -> dict:
        """Get the current recording status.
        
        Returns:
            dict with isRecording (bool) and eventCount (int).
        """
        response = self._send("GetRecordingStatus")
        if response.get("success"):
            import json
            return json.loads(response.get("data", "{}"))
        return {"isRecording": False, "eventCount": 0}

    def get_recorded_events(self) -> dict:
        """Get all recorded events from the current recording session.
        
        Returns:
            dict with elements, steps, and sequence arrays.
        """
        response = self._send("GetRecordedEvents")
        if not response.get("success"):
            raise RuntimeError(f"Failed to get recorded events: {response.get('error')}")
        
        import json
        return json.loads(response.get("data", "{}"))

    def clear_recording(self) -> dict:
        """Clear all recorded events.
        
        Returns:
            dict with status message.
        """
        response = self._send("ClearRecording")
        if response.get("success"):
            print("[WPFSpy] Recording events cleared")
        return response


# ---------------------------------------------------------------------------
# MOCK driver — talks to the in-repo Python mock app (default, cross-platform)
# ---------------------------------------------------------------------------
class WPFSpyMockDriver:
    """Cross-platform stand-in used when WPFSPY_MODE is not 'real'. Talks
    directly to drivers/mock_wpf_app/ instead of a real IPC channel, but
    logs each call as `[WPFSpy IPC]` so the round trip is visible in test
    output the same way the real driver's Named Pipe traffic would be.
    """

    name = "WPFSpy"

    def _log_ipc(self, command: str, **payload):
        print(f"[WPFSpy IPC] -> {command}({payload})")

    def find_element(self, strategy: dict):
        """Locates a single element using WPFSpy strategy.
        
        Args:
            strategy: Dict with searchBy and value keys.
                    Supports: XPath, AutomationId, Name, TypeAndIndex
                    
        Returns:
            ElementHandle for the found element.
            
        Raises:
            ElementNotFoundError: If no matching element is found.
        """
        search_by = strategy.get("searchBy", "XPath")
        value = strategy.get("value")
        
        if search_by == "XPath":
            self._log_ipc("FindByXPath", xpath=value)
            ctrl = APP_INSTANCE.find_by_xpath(value)
            if ctrl is None:
                raise ElementNotFoundError(f"WPFSpy: no element found for XPath '{value}'")
            return ctrl
        
        elif search_by == "Name":
            self._log_ipc("Find", name=value)
            ctrl = APP_INSTANCE.find_by_name(value)
            if ctrl is None:
                raise ElementNotFoundError(f"WPFSpy: no element with Name='{value}'")
            return ctrl
        
        elif search_by == "TypeAndIndex":
            # Parse "ControlType[index]" format
            import re
            match = re.match(r"(\w+)\[(\d+)\]", value)
            if match:
                ctrl_type = match.group(1)
                index = int(match.group(2))
                self._log_ipc("FindByTypeAndIndex", type=ctrl_type, index=index)
                ctrl = APP_INSTANCE.find_by_control_type_and_index(ctrl_type, index)
                if ctrl is None:
                    raise ElementNotFoundError(f"WPFSpy: no element {ctrl_type}[{index}]")
                return ctrl
            raise ElementNotFoundError(f"Invalid TypeAndIndex format: {value}")
        
        else:
            raise ElementNotFoundError(f"Unsupported WPFSpy searchBy: {search_by}")

    def find_elements(self, strategy: dict) -> List:
        """Locates all elements matching the WPFSpy strategy.
        
        Args:
            strategy: Dict with searchBy and value keys.
                    Supports: XPath, Name, Type, AutomationId
                    
        Returns:
            List of ElementHandles for all matching elements.
        """
        search_by = strategy.get("searchBy", "XPath")
        value = strategy.get("value")
        
        if search_by == "XPath":
            self._log_ipc("FindAllByXPath", xpath=value)
            return APP_INSTANCE.find_all_by_xpath(value)
        
        elif search_by == "Name":
            self._log_ipc("FindAll", name=value)
            return APP_INSTANCE.find_all_by_name(value)
        
        elif search_by == "AutomationId":
            self._log_ipc("FindAllByAutomationId", automationId=value)
            return APP_INSTANCE.find_all_by_automation_id(value)
        
        elif search_by == "Type":
            self._log_ipc("FindAllByType", type=value)
            return APP_INSTANCE.find_all_by_control_type(value)
        
        else:
            return []

    def invoke(self, element):
        """Click/invoke an element."""
        if hasattr(element, "xpath") and element.xpath:
            self._log_ipc("Invoke", xpath=element.xpath)
        else:
            self._log_ipc("Invoke", name=element.name)
        APP_INSTANCE.invoke(element)

    def set_value(self, element, value: str):
        """Set text value on an input element."""
        if hasattr(element, "xpath") and element.xpath:
            self._log_ipc("SetValue", xpath=element.xpath, value=value)
        else:
            self._log_ipc("SetValue", name=element.name, value=value)
        APP_INSTANCE.set_value(element, value)

    def get_text(self, element) -> str:
        """Get the text content of an element."""
        if hasattr(element, "xpath") and element.xpath:
            self._log_ipc("GetText", xpath=element.xpath)
        else:
            self._log_ipc("GetText", name=element.name)
        return APP_INSTANCE.get_text(element)

    def is_visible(self, element) -> bool:
        """Check if an element is visible."""
        if hasattr(element, "xpath") and element.xpath:
            self._log_ipc("IsVisible", xpath=element.xpath)
        else:
            self._log_ipc("IsVisible", name=element.name)
        return APP_INSTANCE.is_visible(element)

    def is_enabled(self, element) -> bool:
        """Check if an element is enabled."""
        if hasattr(element, "xpath") and element.xpath:
            self._log_ipc("IsEnabled", xpath=element.xpath)
        else:
            self._log_ipc("IsEnabled", name=element.name)
        return APP_INSTANCE.is_enabled(element)

    def is_actionable(self, element) -> bool:
        """Check if an element is both visible and enabled."""
        return self.is_visible(element) and self.is_enabled(element)

    def get_attribute(self, element, attribute_name: str) -> Optional[str]:
        """Get a specific attribute value from an element."""
        self._log_ipc("GetAttribute", name=getattr(element, "name", None), attribute=attribute_name)
        return APP_INSTANCE.get_attribute(element, attribute_name)

    def capture_screenshot(self, element=None) -> bytes:
        """Capture a screenshot."""
        self._log_ipc("CaptureScreenshot", element=getattr(element, "name", None))
        return APP_INSTANCE.capture_screenshot(element)

    def toggle(self, element, state: bool = None):
        """Toggle a checkbox or toggle button."""
        if hasattr(element, "xpath") and element.xpath:
            self._log_ipc("Toggle", xpath=element.xpath)
        else:
            self._log_ipc("Toggle", name=element.name)
        APP_INSTANCE.invoke(element)

    def get_data_grid_content_ocr(self, element) -> str:
        """Mock OCR driver — returns CSV data based on mock app state."""
        self._log_ipc("GetDataGridContentOcr", name=getattr(element, "name", None))
        
        # Get the actual grid content from mock app
        from mock_app import APP_INSTANCE
        
        # Find the DataGrid control
        grid = APP_INSTANCE.find_by_name("OrdersGrid")
        if grid:
            # Return the grid's text content as CSV
            return f"{grid.text}\n"
        
        return "SKU,Qty\n"
    
    def close(self):
        """Clean up driver resources."""
        pass  # Mock driver doesn't need cleanup

    # -------------------------------------------------------------------------
    # UIA Event Recording Methods (Mock implementation)
    # -------------------------------------------------------------------------
    def start_recording(self) -> dict:
        """Start recording UI Automation events (mock).
        
        In production with real WPF app, this hooks into the application.
        In mock mode, this is a no-op that returns success.
        """
        self._log_ipc("StartRecording")
        self._recording_events = []
        self._is_recording = True
        print("[WPFSpy Mock] UIA Event Recording started (mock mode)")
        return {"success": True, "data": "Recording started"}

    def stop_recording(self) -> dict:
        """Stop recording UI Automation events (mock)."""
        self._log_ipc("StopRecording")
        self._is_recording = False
        print("[WPFSpy Mock] UIA Event Recording stopped")
        return {"success": True, "data": "Recording stopped"}

    def get_recording_status(self) -> dict:
        """Get the current recording status (mock)."""
        self._log_ipc("GetRecordingStatus")
        return {
            "isRecording": getattr(self, '_is_recording', False),
            "eventCount": len(getattr(self, '_recording_events', []))
        }

    def get_recorded_events(self) -> dict:
        """Get recorded events (mock).
        
        In mock mode, this simulates recorded events from the scripted interactions.
        """
        self._log_ipc("GetRecordedEvents")
        
        # Return simulated recorded events
        return {
            "elements": {
                "LoginPage.txtUsername": {
                    "automationId": "txtUsername",
                    "name": "UsernameInput",
                    "controlType": "TextBox",
                    "xpath": "/Window[@AutomationId='MainWindow']/TextBox[@AutomationId='txtUsername']"
                },
                "LoginPage.txtPassword": {
                    "automationId": "txtPassword",
                    "name": "PasswordInput",
                    "controlType": "TextBox",
                    "xpath": "/Window[@AutomationId='MainWindow']/TextBox[@AutomationId='txtPassword']"
                },
                "LoginPage.btnSubmit": {
                    "automationId": "btnSubmit",
                    "name": "SubmitBtn",
                    "controlType": "Button",
                    "xpath": "/Window[@AutomationId='MainWindow']/Button[@AutomationId='btnSubmit']"
                }
            },
            "steps": [
                {"alias": "LoginPage.txtUsername", "stepType": "ValueStep", "value": "user1", "timestamp": "2025-01-01T10:00:00"},
                {"alias": "LoginPage.txtPassword", "stepType": "ValueStep", "value": "Pass@123", "timestamp": "2025-01-01T10:00:01"},
                {"alias": "LoginPage.btnSubmit", "stepType": "InvokeStep", "value": None, "timestamp": "2025-01-01T10:00:02"}
            ],
            "sequence": [
                {"timestamp": "2025-01-01T10:00:00", "eventType": "TextChanged", "automationId": "txtUsername", "name": "UsernameInput", "controlType": "TextBox", "value": "user1", "pageName": "LoginPage"},
                {"timestamp": "2025-01-01T10:00:01", "eventType": "TextChanged", "automationId": "txtPassword", "name": "PasswordInput", "controlType": "TextBox", "value": "Pass@123", "pageName": "LoginPage"},
                {"timestamp": "2025-01-01T10:00:02", "eventType": "Invoke", "automationId": "btnSubmit", "name": "SubmitBtn", "controlType": "Button", "value": None, "pageName": "LoginPage"}
            ]
        }

    def clear_recording(self) -> dict:
        """Clear recorded events (mock)."""
        self._log_ipc("ClearRecording")
        self._recording_events = []
        print("[WPFSpy Mock] Recording events cleared")
        return {"success": True, "data": "Recording cleared"}


def _make_driver():
    mode = os.environ.get("WPFSPY_MODE", "mock").lower()
    if mode == "real":
        return WPFSpyRealDriver()
    return WPFSpyMockDriver()


# Backwards-compatible name used by api/DriverAgnosticApi.py — resolved at
# import time based on WPFSPY_MODE (defaults to the mock for portability).
WPFSpyDriver = _make_driver


class WPFSpyLibrary:
    """Robot Framework library exposing WPFSpy keywords directly (rarely
    used directly by test authors — Layer 3 is the normal entry point).
    """
    ROBOT_LIBRARY_SCOPE = "GLOBAL"

    def __init__(self):
        self.driver = _make_driver()

    def wpfspy_find_element(self, name):
        return self.driver.find_element({"searchBy": "Name", "value": name})

    def wpfspy_invoke(self, name):
        el = self.wpfspy_find_element(name)
        self.driver.invoke(el)

    def wpfspy_set_value(self, name, value):
        el = self.wpfspy_find_element(name)
        self.driver.set_value(el, value)

    def wpfspy_get_text(self, name):
        el = self.wpfspy_find_element(name)
        return self.driver.get_text(el)

    def wpfspy_get_data_grid_content_ocr(self, name):
        """Captures a DataGrid screenshot and returns its
        content as CSV text using OCR."""
        el = self.wpfspy_find_element(name)
        return self.driver.get_data_grid_content_ocr(el)
