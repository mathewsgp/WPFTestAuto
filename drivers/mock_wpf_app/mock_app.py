"""
Mock WPF Application
=====================
Simulates a small WPF application's visual tree and behavior so the
framework's Layer 4/5 drivers have a real, deterministic target to
automate against — without requiring Windows, .NET, or a real WPF process.

In a production deployment this file does not exist: FlaUI, WPFSpy, and
Sikuli would instead talk to the REAL WPF application's process, visual
tree, and screen. This mock exists purely so the whole framework can be
exercised and demonstrated end-to-end on any platform (including this
Linux sandbox), including the runtime self-healing fallback behavior.

Drivers locate a Control via find_by_automation_id / find_by_name /
find_by_image_tag (mirroring AutomationId lookups, WPFSpy Name lookups,
and Sikuli image matching respectively), then act on the returned Control
object directly. One control ("chkPriority" on the Orders page) is
deliberately given NO reliable AutomationId — simulating a custom-rendered
control not properly exposed via UI Automation — so it can only be found
by Name (WPFSpy) or image (Sikuli). This is used by the self-healing
locator demo test to prove the FlaUI -> WPFSpy -> Sikuli fallback chain.
"""

import threading
from dataclasses import dataclass
from typing import Dict, Optional


@dataclass
class Control:
    key: str                 # internal identity, used for behavior wiring
    automation_id: Optional[str]   # None/"" simulates "not exposed via UIA"
    name: str
    control_type: str
    text: str = ""
    visible: bool = True
    enabled: bool = True
    image_tag: Optional[str] = None  # simulated Sikuli match target
    xpath: Optional[str] = None      # XPath locator for deep-hierarchy elements


class ElementNotFoundError(Exception):
    pass


class ElementNotInteractableError(Exception):
    pass


# Thread-local storage for parallel test execution support
_thread_local = threading.local()
_lock = threading.RLock()  # Reentrant lock for thread-safe operations


class MockWpfApp:
    """A minimal, stateful simulation of a two-screen WPF application."""

    def __init__(self):
        self.current_page = "Login"
        self.controls: Dict[str, Control] = {}
        self._build_login_page()

    # ------------------------------------------------------------------
    # Page construction
    # ------------------------------------------------------------------
    def _build_login_page(self):
        self.controls = {
            "txtUsername": Control("txtUsername", "txtUsername", "UsernameInput", "TextBox",
                                    image_tag="username_box",
                                    xpath="/Window[@Name='Login']/TextBox[@Name='UsernameInput']"),
            "txtPassword": Control("txtPassword", "txtPassword", "PasswordInput", "TextBox",
                                   image_tag="password_box",
                                   xpath="/Window[@Name='Login']/TextBox[@Name='PasswordInput']"),
            "btnSubmit": Control("btnSubmit", "btnSubmit", "SubmitBtn", "Button", text="Login",
                                  image_tag="login_button",
                                  xpath="/Window[@Name='Login']/Button[@Name='SubmitBtn']"),
            "lblError": Control("lblError", "lblError", "ErrorLabel", "Label", text="", visible=False,
                                 xpath="/Window[@Name='Login']/Label[@Name='ErrorLabel']"),
        }

    def _build_orders_page(self):
        self.controls = {
            "cmbSku": Control("cmbSku", "cmbSku", "SkuCombo", "ComboBox", image_tag="sku_combo",
                               xpath="/Window[@Name='Orders']/ComboBox[@Name='SkuCombo']"),
            "txtQty": Control("txtQty", "txtQty", "QtyInput", "TextBox", text="1", image_tag="qty_box",
                               xpath="/Window[@Name='Orders']/TextBox[@Name='QtyInput']"),
            "btnCreateOrder": Control("btnCreateOrder", "btnCreateOrder", "CreateOrderBtn", "Button",
                                       text="Create Order", image_tag="create_order_button",
                                       xpath="/Window[@Name='Orders']/Button[@Name='CreateOrderBtn']"),
            "lblConfirmation": Control("lblConfirmation", "lblConfirmation", "ConfirmationLabel", "Label",
                                         text="", visible=False,
                                         xpath="/Window[@Name='Orders']/Label[@Name='ConfirmationLabel']"),
            "gridOrders": Control("gridOrders", "gridOrders", "OrdersGrid", "DataGrid", text="0 rows",
                                   xpath="/Window[@Name='Orders']/DataGrid[@Name='OrdersGrid']"),
            # Non-standard control: NO reliable AutomationId (simulates a
            # custom-rendered WPF control not properly exposed via UIA).
            # Only findable by Name (WPFSpy) or image (Sikuli).
            "chkPriority": Control("chkPriority", None, "PriorityToggle", "CheckBox", text="Off",
                                    image_tag="priority_checkbox",
                                    xpath="/Window[@Name='Orders']/CheckBox[@Name='PriorityToggle']"),
        }

    # ------------------------------------------------------------------
    # Locate (what each driver calls to resolve an element)
    # ------------------------------------------------------------------
    def find_by_automation_id(self, automation_id: str) -> Optional[Control]:
        for ctrl in self.controls.values():
            if ctrl.automation_id and ctrl.automation_id == automation_id:
                return ctrl
        return None

    def find_by_name(self, name: str) -> Optional[Control]:
        for ctrl in self.controls.values():
            if ctrl.name == name:
                return ctrl
        return None

    def find_by_image_tag(self, tag: str) -> Optional[Control]:
        for ctrl in self.controls.values():
            if ctrl.image_tag == tag:
                return ctrl
        return None

    def find_by_xpath(self, xpath: str) -> Optional[Control]:
        """Evaluates a simple WPF XPath expression against the mock app's
        virtual visual tree. Supported syntax:
          /Window[@Name='Login']/Button[@Name='SubmitBtn']
          /Window[@Name='Orders']/CheckBox[1]
        """
        if not xpath.startswith("/"):
            return None
        segments = self._parse_xpath_segments(xpath)
        return self._match_xpath_segments(segments, 0, self._build_virtual_tree())

    def _parse_xpath_segments(self, xpath: str):
        segments = []
        for part in xpath.split("/")[1:]:  # skip empty leading segment
            if not part:
                continue
            tag = part
            name_predicate = None
            index_predicate = None
            if "[" in part and part.endswith("]"):
                bracket_start = part.index("[")
                tag = part[:bracket_start]
                predicate = part[bracket_start+1:-1]
                if predicate.startswith("@Name='") and predicate.endswith("'"):
                    name_predicate = predicate[8:-1]
                elif predicate.isdigit():
                    index_predicate = int(predicate)
            segments.append((tag, name_predicate, index_predicate))
        return segments

    def _build_virtual_tree(self):
        """Builds a simple tree: root -> Window -> controls."""
        window_name = "Login" if self.current_page == "Login" else "Orders"
        return {
            "tag": "Root",
            "children": [
                {
                    "tag": "Window",
                    "attrs": {"Name": window_name},
                    "children": [
                        {"tag": ctrl.control_type, "attrs": {"Name": ctrl.name}, "control_key": key}
                        for key, ctrl in self.controls.items()
                    ],
                }
            ],
        }

    def _match_xpath_segments(self, segments, index, node):
        if index >= len(segments):
            return None
        tag, name_pred, index_pred = segments[index]
        matches = []
        for child in node.get("children", []):
            if child["tag"] != tag:
                continue
            if name_pred is not None and child.get("attrs", {}).get("Name") != name_pred:
                continue
            matches.append(child)
        if index_pred is not None:
            if 0 < index_pred <= len(matches):
                match = matches[index_pred - 1]
            else:
                return None
        else:
            match = matches[0] if matches else None
        if match is None:
            return None
        if index + 1 >= len(segments):
            return self.controls.get(match.get("control_key", "")) if "control_key" in match else None
        return self._match_xpath_segments(segments, index + 1, match)

    # ------------------------------------------------------------------
    # Act (what each driver calls once it has resolved a Control)
    # ------------------------------------------------------------------
    def invoke(self, ctrl: Control):
        if not ctrl.enabled or not ctrl.visible:
            raise ElementNotInteractableError(ctrl.key)

        if ctrl.key == "btnSubmit":
            username = self.controls["txtUsername"].text
            password = self.controls["txtPassword"].text
            if username == "user1" and password == "Pass@123":
                self.current_page = "Orders"
                self._build_orders_page()
            else:
                self.controls["lblError"].text = "Invalid username or password"
                self.controls["lblError"].visible = True

        elif ctrl.key == "btnCreateOrder":
            sku = self.controls["cmbSku"].text
            qty = self.controls["txtQty"].text
            if not sku:
                self.controls["lblConfirmation"].text = "Please select a SKU"
                self.controls["lblConfirmation"].visible = True
            else:
                self.controls["lblConfirmation"].text = f"Order confirmed: {sku} x{qty}"
                self.controls["lblConfirmation"].visible = True
                self.controls["gridOrders"].text = "1 rows"

        elif ctrl.key == "chkPriority":
            ctrl.text = "On" if ctrl.text == "Off" else "Off"

    def set_value(self, ctrl: Control, value: str):
        if not ctrl.enabled or not ctrl.visible:
            raise ElementNotInteractableError(ctrl.key)
        ctrl.text = value

    def get_text(self, ctrl: Control) -> str:
        return ctrl.text

    def is_visible(self, ctrl: Control) -> bool:
        return bool(ctrl.visible)


    def reset(self):
        """Reinitializes state in place (does NOT rebind this object) so
        that other modules holding a reference to this same instance
        (e.g. each Layer 4 driver wrapper) see the reset too.
        """
        self.current_page = "Login"
        self.controls = {}
        self._build_login_page()


# Thread-local storage for parallel test execution support
# Each thread gets its own app instance when using thread-local mode
_use_thread_local = False  # Default: use global instance for backwards compatibility


def _get_app_instance() -> MockWpfApp:
    """Get the current thread's app instance.
    
    If thread-local mode is enabled, returns thread-specific instance.
    Otherwise returns the global shared instance.
    """
    global _use_thread_local
    if _use_thread_local:
        if not hasattr(_thread_local, 'instance') or _thread_local.instance is None:
            _thread_local.instance = MockWpfApp()
        return _thread_local.instance
    return _global_app_instance


def enable_thread_local_mode():
    """Enable thread-local mode for parallel test execution.
    
    When enabled, each thread gets its own MockWpfApp instance,
    preventing race conditions in parallel test execution.
    """
    global _use_thread_local
    _use_thread_local = True


def disable_thread_local_mode():
    """Disable thread-local mode (default behavior).
    
    Uses a single shared instance across all threads.
    Note: This may cause race conditions in parallel execution.
    """
    global _use_thread_local
    _use_thread_local = False


# Global shared instance for backwards compatibility
_global_app_instance = MockWpfApp()


# Alias for backwards compatibility
APP_INSTANCE = _global_app_instance


def reset_app():
    """Test isolation helper — restarts the mock app at the Login page.
    
    In thread-local mode: resets the current thread's app instance.
    In shared mode: resets the global app instance.
    
    Mutates the existing instance in place rather than rebinding,
    since driver wrapper modules import APP_INSTANCE by reference.
    """
    app = _get_app_instance()
    app.reset()
    return app


def get_current_app() -> MockWpfApp:
    """Get the current app instance (thread-aware).
    
    Returns:
        The MockWpfApp instance for the current thread or global.
    """
    return _get_app_instance()
