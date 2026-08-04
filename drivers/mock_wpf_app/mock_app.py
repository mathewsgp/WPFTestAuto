"""
Mock WPF Application
=====================
Simulates a WPF application's visual tree with comprehensive path support
for multiple automation strategies: FlaUI, WPFSpy, and Sikuli.

Path Priority Hierarchy:
1. AutomationId (most reliable)
2. Name (second choice)
3. Type + Index/Siblings (fallback when no unique identifier)

Each driver uses its native path format:
- FlaUI: XPath with AutomationId, Name, or Type+Index
- WPFSpy: Similar XPath format with @AutomationId='...' or @Name='...'
- Sikuli: Image-based matching via image_tag

Fallback Chain: FlaUI -> WPFSpy -> Sikuli
"""

import threading
import re
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple


@dataclass
class Control:
    key: str                           # internal identity, used for behavior wiring
    automation_id: Optional[str]        # None/"" simulates "not exposed via UIA"
    name: str                          # Name property (WPFSpy/ FlaUI Name)
    control_type: str                  # Control type (Button, TextBox, etc.)
    text: str = ""                     # Current text content
    visible: bool = True
    enabled: bool = True
    image_tag: Optional[str] = None    # Sikuli image match target
    # Multi-format paths for different drivers
    flaui_paths: List[str] = field(default_factory=list)  # FlaUI XPath variants
    wpfspy_paths: List[str] = field(default_factory=list) # WPFSpy XPath variants
    # Path building blocks (for dynamic path construction)
    parent_automation_id: Optional[str] = None
    sibling_index: int = 0            # Index among siblings of same type
    sibling_count: int = 1             # Total siblings of same type


class ElementNotFoundError(Exception):
    """Raised when an element cannot be found in the mock app."""
    pass


class ElementNotInteractableError(Exception):
    """Raised when an element is found but not interactable (disabled/invisible)."""
    pass


class MockWpfApp:
    """A WPF application simulation with comprehensive path support.
    
    Supports multiple automation strategies with path priority:
    - AutomationId (highest priority)
    - Name (second priority)
    - Type + Index/Siblings (fallback)
    
    Driver path formats:
    - FlaUI: UIAutomation XPath
    - WPFSpy: XPath with @AutomationId/@Name
    - Sikuli: Image-based matching
    """

    def __init__(self):
        self.current_page = "Login"
        self.controls: Dict[str, Control] = {}
        self.orders: list = []  # Track actual orders for OCR
        self._build_login_page()

    def _build_control_paths(self, control: Control, parent_aid: str = "MainWindow") -> Control:
        """Build comprehensive paths for all drivers.
        
        Path priority: AutomationId -> Name -> Type+Index
        """
        control.parent_automation_id = parent_aid
        
        # FlaUI paths (UIAutomation XPath format)
        # Path 1: AutomationId-based (most reliable)
        if control.automation_id:
            control.flaui_paths.append(
                f"/Window[@AutomationId='{parent_aid}']/{control.control_type}[@AutomationId='{control.automation_id}']"
            )
        # Path 2: Name-based
        control.flaui_paths.append(
            f"/Window[@AutomationId='{parent_aid}']/{control.control_type}[@Name='{control.name}']"
        )
        # Path 3: Type + Index (sibling position)
        if control.sibling_count > 1:
            control.flaui_paths.append(
                f"/Window[@AutomationId='{parent_aid}']/{control.control_type}[{control.sibling_index + 1}]"
            )
        
        # WPFSpy paths (similar XPath format)
        # Path 1: AutomationId-based
        if control.automation_id:
            control.wpfspy_paths.append(
                f"/Window[@AutomationId='{parent_aid}']/{control.control_type}[@AutomationId='{control.automation_id}']"
            )
        # Path 2: Name-based
        control.wpfspy_paths.append(
            f"/Window[@Name='{parent_aid}']/{control.control_type}[@Name='{control.name}']"
        )
        # Path 3: Type + Index
        if control.sibling_count > 1:
            control.wpfspy_paths.append(
                f"/Window[@Name='{parent_aid}']/{control.control_type}[{control.sibling_index + 1}]"
            )
        
        return control

    def _build_login_page(self):
        """Build Login page with comprehensive path support."""
        self.controls = {
            "txtUsername": self._build_control_paths(
                Control("txtUsername", "txtUsername", "UsernameInput", "TextBox",
                        image_tag="username_box"),
                "MainWindow"
            ),
            "txtPassword": self._build_control_paths(
                Control("txtPassword", "txtPassword", "PasswordInput", "TextBox",
                        image_tag="password_box"),
                "MainWindow"
            ),
            "btnSubmit": self._build_control_paths(
                Control("btnSubmit", "btnSubmit", "SubmitBtn", "Button", text="Login",
                        image_tag="login_button"),
                "MainWindow"
            ),
            "lblError": self._build_control_paths(
                Control("lblError", "lblError", "ErrorLabel", "Label", text="", visible=False,
                        image_tag="error_label"),
                "MainWindow"
            ),
        }

    def _build_orders_page(self):
        """Build Orders page with comprehensive path support and sibling indexes."""
        # Calculate sibling counts for type-based paths
        button_count = 3  # btnCreateOrder, chkPriority, btnLogout
        textbox_count = 2  # txtQty, txtSku (in combo)
        
        self.controls = {
            "cmbSku": self._build_control_paths(
                Control("cmbSku", "cmbSku", "SkuCombo", "ComboBox", text="",
                        image_tag="sku_combo"),
                "OrdersWindow"
            ),
            "txtQty": self._build_control_paths(
                Control("txtQty", "txtQty", "QtyInput", "TextBox", text="1",
                        image_tag="qty_box", sibling_index=0, sibling_count=1),
                "OrdersWindow"
            ),
            "btnCreateOrder": self._build_control_paths(
                Control("btnCreateOrder", "btnCreateOrder", "CreateOrderBtn", "Button",
                        text="Create Order", image_tag="create_order_button",
                        sibling_index=0, sibling_count=button_count),
                "OrdersWindow"
            ),
            "lblConfirmation": self._build_control_paths(
                Control("lblConfirmation", "lblConfirmation", "ConfirmationLabel", "Label",
                        text="", visible=False, image_tag="confirmation_label"),
                "OrdersWindow"
            ),
            "gridOrders": self._build_control_paths(
                Control("gridOrders", "gridOrders", "OrdersGrid", "DataGrid", text="SKU,Qty\n",
                        image_tag="orders_grid"),
                "OrdersWindow"
            ),
            # Non-standard control: NO reliable AutomationId (simulates a
            # custom-rendered WPF control not properly exposed via UIA).
            # Only findable by Name (WPFSpy) or image (Sikuli).
            "chkPriority": self._build_control_paths(
                Control("chkPriority", None, "PriorityToggle", "CheckBox", text="Off",
                        image_tag="priority_checkbox", sibling_index=1, sibling_count=button_count),
                "OrdersWindow"
            ),
            "btnLogout": self._build_control_paths(
                Control("btnLogout", "btnLogout", "LogoutBtn", "Button", text="Logout",
                        image_tag="logout_button", sibling_index=2, sibling_count=button_count),
                "OrdersWindow"
            ),
        }
        # Rebuild grid text from orders
        self._update_grid_text()

    # ------------------------------------------------------------------
    # Grid management
    # ------------------------------------------------------------------
    def _update_grid_text(self):
        """Update grid text from orders list."""
        if "gridOrders" in self.controls:
            lines = ["SKU,Qty"]
            for order in self.orders:
                lines.append(f"{order['sku']},{order['qty']}")
            self.controls["gridOrders"].text = "\n".join(lines) if lines else "SKU,Qty\n"

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

    def find_by_control_type_and_index(self, control_type: str, index: int) -> Optional[Control]:
        """Find control by type and sibling index (1-based index)."""
        matches = [ctrl for ctrl in self.controls.values() 
                  if ctrl.control_type == control_type]
        # Convert to 0-based index
        if 0 <= index - 1 < len(matches):
            return matches[index - 1]
        return None

    def find_by_flaui_path(self, path: str) -> Optional[Control]:
        """Find control by FlaUI XPath path."""
        return self.find_by_xpath(path)

    def find_by_wpfspy_path(self, path: str) -> Optional[Control]:
        """Find control by WPFSpy XPath path."""
        return self.find_by_xpath(path)

    def get_all_paths_for_control(self, control_key: str) -> dict:
        """Get all paths for a control (for recording/debugging)."""
        ctrl = self.controls.get(control_key)
        if not ctrl:
            return {}
        return {
            "automation_id": ctrl.automation_id,
            "name": ctrl.name,
            "flaui_paths": ctrl.flaui_paths,
            "wpfspy_paths": ctrl.wpfspy_paths,
            "image_tag": ctrl.image_tag,
            "type_index": {
                "type": ctrl.control_type,
                "index": ctrl.sibling_index + 1,  # 1-based for recording
                "total": ctrl.sibling_count
            }
        }

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
        """Parse XPath into segments for matching.
        
        Supports:
        - @AutomationId='value' (highest priority)
        - @Name='value' (second priority)
        - [index] (fallback)
        """
        import re
        segments = []
        # Pattern to match: tag[@attr='value'] or tag[index] or just tag
        pattern = r'([^[]+)(?:\[([^\]]+)\])?'
        
        # Skip the leading /
        xpath = xpath.lstrip('/')
        parts = xpath.split('/')
        
        for part in parts:
            if not part:
                continue
            tag = part
            automation_id_predicate = None
            name_predicate = None
            index_predicate = None
            
            # Parse the predicate
            match = re.match(pattern, part)
            if match:
                tag = match.group(1)
                pred = match.group(2)
                if pred:
                    # Check for @AutomationId='value' pattern (highest priority)
                    aid_match = re.match(r"@AutomationId='([^']+)'", pred)
                    if aid_match:
                        automation_id_predicate = aid_match.group(1)
                    # Check for @Name='value' pattern
                    elif re.match(r"@Name='([^']+)'", pred):
                        name_match = re.match(r"@Name='([^']+)'", pred)
                        name_predicate = name_match.group(1)
                    # Check for numeric index (fallback)
                    elif pred.isdigit():
                        index_predicate = int(pred)
            
            segments.append((tag, automation_id_predicate, name_predicate, index_predicate))
        return segments

    def _match_xpath_segments(self, segments, index, node):
        """Match XPath segments against the virtual tree.
        
        Matches in priority order:
        1. AutomationId (if specified)
        2. Name (if specified)
        3. First match (if only index)
        """
        if index >= len(segments):
            return None
        tag, aid_pred, name_pred, index_pred = segments[index]
        matches = []
        for child in node.get("children", []):
            if child["tag"] != tag:
                continue
            # Check AutomationId first (highest priority)
            if aid_pred is not None:
                child_aid = child.get("attrs", {}).get("AutomationId") or child.get("attrs", {}).get("Name", "")
                if child_aid != aid_pred:
                    continue
            # Then check Name
            elif name_pred is not None:
                if child.get("attrs", {}).get("Name") != name_pred:
                    continue
            matches.append(child)
        
        # Handle index
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

    def _build_virtual_tree(self):
        """Builds a virtual tree: root -> Window -> controls.
        
        Uses the parent automation_id for both Window's AutomationId and Name.
        Each control includes both its automation_id and name for flexible matching.
        """
        # Determine window ID based on current controls
        window_aid = "MainWindow"
        if self.controls:
            first_ctrl = next(iter(self.controls.values()))
            if first_ctrl.parent_automation_id:
                window_aid = first_ctrl.parent_automation_id
        
        return {
            "tag": "Root",
            "children": [
                {
                    "tag": "Window",
                    "attrs": {"AutomationId": window_aid, "Name": window_aid},
                    "children": [
                        {
                            "tag": ctrl.control_type, 
                            "attrs": {
                                "AutomationId": ctrl.automation_id or "", 
                                "Name": ctrl.name
                            }, 
                            "control_key": key
                        }
                        for key, ctrl in self.controls.items()
                    ],
                }
            ],
        }

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
                # Add order to tracking list
                self.orders.append({"sku": sku, "qty": qty})
                self._update_grid_text()

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
        self.orders = []
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
