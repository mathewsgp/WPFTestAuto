"""
Mock WPF Application
=====================
Simulates a WPF application's visual tree with hierarchical structure support
for complex automation scenarios.

Hierarchy Support:
- Window
  - TabControl (container)
    - TabItem (container)
      - GroupBox (container)
        - StackPanel (container)
          - TextBox (leaf control)
          - Button (leaf control)

Container Types: TabControl, TabItem, TabItem+Name, GroupBox, StackPanel, 
                 Panel, Grid, DockPanel, ScrollViewer, etc.

Path Priority per Level:
1. AutomationId (most reliable)
2. Name (second choice)
3. Type + Index/Siblings (fallback when no unique identifier)

Fallback Chain: FlaUI -> WPFSpy -> Sikuli
"""

import threading
import re
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple, Any

# Container types that can hold other controls
CONTAINER_TYPES = {
    "Window", "TabControl", "TabItem", "GroupBox", "StackPanel", 
    "Panel", "Grid", "DockPanel", "ScrollViewer", "Border",
    "Canvas", "WrapPanel", "UniformGrid"
}


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
    sibling_index: int = 0            # Index among siblings of same type
    sibling_count: int = 1             # Total siblings of same type
    # Hierarchical path
    container_chain: List[Dict] = field(default_factory=list)  # List of {type, automationId, name}


@dataclass 
class Container:
    """Represents a container element in the hierarchy."""
    automation_id: Optional[str] = None
    name: str = ""
    container_type: str = ""
    index: int = 0  # Position among siblings of same type
    children: List[Any] = field(default_factory=list)


class ElementNotFoundError(Exception):
    """Raised when an element cannot be found in the mock app."""
    pass


class ElementNotInteractableError(Exception):
    """Raised when an element is found but not interactable (disabled/invisible)."""
    pass


class MockWpfApp:
    """A WPF application simulation with hierarchical structure support.
    
    Supports complex WPF hierarchies with containers:
    - Window
      - TabControl
        - TabItem (with optional Name)
          - GroupBox
            - StackPanel
              - Controls
    
    Path Priority per Level:
    - AutomationId (highest priority)
    - Name (second priority)
    - Type + Index/Siblings (fallback)
    
    Driver path formats:
    - FlaUI: UIAutomation XPath with full hierarchy
    - WPFSpy: XPath with @AutomationId/@Name
    - Sikuli: Image-based matching
    """

    def __init__(self):
        self.current_page = "Login"
        self.controls: Dict[str, Control] = {}
        self.orders: list = []  # Track actual orders for OCR
        self.root_container: Optional[Container] = None
        self._build_login_page()

    def _build_hierarchical_path(self, control: Control) -> str:
        """Build full hierarchical XPath for a control.
        
        Only includes containers that have AutomationId or Name.
        Layout containers without identifiers are skipped.
        """
        path_parts = []
        
        # Add Window first
        window_aid = "MainWindow"
        if control.container_chain:
            # Get window from first container
            if control.container_chain[0].get("type") == "Window":
                window_aid = control.container_chain[0].get("automationId") or "MainWindow"
        
        path_parts.append(f"Window[@AutomationId='{window_aid}']")
        
        # Add container chain (only identifiable containers)
        for container in control.container_chain:
            ctype = container.get("type", "")
            if ctype == "Window":
                continue  # Already added
            
            aid = container.get("automationId")
            name = container.get("name")
            cindex = container.get("index", 0)
            
            # Only include containers with AutomationId or Name
            # Skip layout-only containers (Grid, StackPanel, etc.) without identifiers
            if aid:
                path_parts.append(f"{ctype}[@AutomationId='{aid}']")
            elif name:
                path_parts.append(f"{ctype}[@Name='{name}']")
            elif cindex is not None and cindex >= 0:
                # Only include by index if it's meaningful (first item is index 0)
                path_parts.append(f"{ctype}[{cindex + 1}]")
            # else: skip this container - no identifier
        
        return "/" + "/".join(path_parts)

    def _build_control_paths(self, control: Control) -> Control:
        """Build comprehensive paths for all drivers.
        
        Paths are constructed by walking the parent chain to build full XPath.
        Each element can specify:
        - parentAlias: Reference to another element alias (e.g., "LoginPage.MainWindow")
        - relativeXPath: XPath relative to parent (e.g., "TextBox[@AutomationId='txtUsername']")
        
        If parentAlias is not set, relativeXPath is used as-is (absolute or relative to Window).
        """
        
        ctrl_type = control.control_type
        
        # Build full path by walking parent chain
        full_path = self._build_full_path_from_chain(control)
        
        # FlaUI paths (UIAutomation XPath format)
        # Path 1: Full path with AutomationId
        if control.automation_id:
            control.flaui_paths.append(
                f"{full_path}/{ctrl_type}[@AutomationId='{control.automation_id}']"
            )
        # Path 2: Full path with Name
        control.flaui_paths.append(
            f"{full_path}/{ctrl_type}[@Name='{control.name}']"
        )
        # Path 3: Type + Index (sibling position)
        if control.sibling_count > 1:
            control.flaui_paths.append(
                f"{full_path}/{ctrl_type}[{control.sibling_index + 1}]"
            )
        
        # WPFSpy paths (similar XPath format)
        # Path 1: Full path with AutomationId
        if control.automation_id:
            control.wpfspy_paths.append(
                f"{full_path}/{ctrl_type}[@AutomationId='{control.automation_id}']"
            )
        # Path 2: Full path with Name (use @Name for window)
        wpfspy_path = full_path.replace("[@AutomationId=", "[@Name=")
        wpfspy_path = wpfspy_path.replace("Window[@Name='", "Window[@Name='")
        control.wpfspy_paths.append(
            f"{wpfspy_path}/{ctrl_type}[@Name='{control.name}']"
        )
        # Path 3: Type + Index
        if control.sibling_count > 1:
            control.wpfspy_paths.append(
                f"{full_path}/{ctrl_type}[{control.sibling_index + 1}]"
            )
        
        return control
    
    def _build_full_path_from_chain(self, control: Control) -> str:
        """Build full XPath by walking container chain."""
        return self._build_hierarchical_path(control)

    def _set_container_chain(self, control: Control, container_chain: List[Dict]) -> Control:
        """Set the container chain for a control and rebuild paths."""
        control.container_chain = container_chain
        return self._build_control_paths(control)

    def _build_login_page(self):
        """Build Login page with hierarchical container support.
        
        Structure:
        Window (MainWindow)
        └── [Layout containers without names - skipped]
            ├── TextBox (txtUsername)
            ├── TextBox (txtPassword)
            ├── Button (btnSubmit)
            └── Label (lblError)
        
        Note: Grid/StackPanel without AutomationId or Name are SKIPPED in paths.
        Only identifiable containers (Window, TabControl, GroupBox, etc.) are included.
        """
        # Container chain - only identifiable ones
        base_chain = [
            {"type": "Window", "automationId": "MainWindow"}
            # Grid is skipped - no AutomationId or Name
        ]
        
        self.controls = {
            "txtUsername": self._build_control_paths(
                self._set_container_chain(
                    Control("txtUsername", "txtUsername", "txtUsername", "TextBox",
                            image_tag="username_box"),
                    base_chain
                )
            ),
            "txtPassword": self._build_control_paths(
                self._set_container_chain(
                    Control("txtPassword", "txtPassword", "PasswordInput", "TextBox",
                            image_tag="password_box"),
                    base_chain
                )
            ),
            "btnSubmit": self._build_control_paths(
                self._set_container_chain(
                    Control("btnSubmit", "btnSubmit", "SubmitBtn", "Button", text="Login",
                            image_tag="login_button"),
                    base_chain
                )
            ),
            "lblError": self._build_control_paths(
                self._set_container_chain(
                    Control("lblError", "lblError", "ErrorLabel", "Label", text="", visible=False,
                            image_tag="error_label"),
                    base_chain
                )
            ),
        }
        
        # Build hierarchical tree for XPath matching
        self._build_hierarchical_tree()

    def _build_orders_page(self):
        """Build Orders page with hierarchical container support.
        
        Structure:
        Window (OrdersWindow)
        └── TabControl (MainTabs)
            ├── TabItem (General) [by name]
            │   └── GroupBox (OrderInfo) [by AutomationId]
            │       ├── ComboBox (cmbSku)
            │       ├── TextBox (txtQty)
            │       ├── CheckBox (chkPriority) - NO AutomationId
            │       ├── Button (btnCreateOrder)
            │       └── Label (lblConfirmation)
            └── TabItem (History) [by name]
                └── DataGrid (gridOrders)
                    └── Button (btnLogout)
        
        Note: StackPanel/Grid without names are SKIPPED in paths.
        """
        tab_index = 0  # General tab
        history_tab_index = 1  # History tab
        
        # General tab controls - only identifiable containers
        general_tab_chain = [
            {"type": "Window", "automationId": "OrdersWindow"},
            {"type": "TabControl", "automationId": "MainTabs"},
            {"type": "TabItem", "name": "General"},  # Index only if needed
            {"type": "GroupBox", "automationId": "OrderInfo"}
            # StackPanel skipped - no identifier
        ]
        
        # History tab controls
        history_tab_chain = [
            {"type": "Window", "automationId": "OrdersWindow"},
            {"type": "TabControl", "automationId": "MainTabs"},
            {"type": "TabItem", "name": "History"},
            # Grid/StackPanel skipped - no identifier
        ]
        
        self.controls = {
            "cmbSku": self._build_control_paths(
                self._set_container_chain(
                    Control("cmbSku", "cmbSku", "SkuCombo", "ComboBox", text="",
                            image_tag="sku_combo"),
                    general_tab_chain
                )
            ),
            "txtQty": self._build_control_paths(
                self._set_container_chain(
                    Control("txtQty", "txtQty", "QtyInput", "TextBox", text="1",
                            image_tag="qty_box", sibling_index=0, sibling_count=2),
                    general_tab_chain
                )
            ),
            "btnCreateOrder": self._build_control_paths(
                self._set_container_chain(
                    Control("btnCreateOrder", "btnCreateOrder", "CreateOrderBtn", "Button",
                            text="Create Order", image_tag="create_order_button",
                            sibling_index=0, sibling_count=2),
                    general_tab_chain
                )
            ),
            "lblConfirmation": self._build_control_paths(
                self._build_control_paths(
                    self._set_container_chain(
                        Control("lblConfirmation", "lblConfirmation", "ConfirmationLabel", "Label",
                                text="", visible=False, image_tag="confirmation_label"),
                        general_tab_chain
                    )
                )
            ),
            "gridOrders": self._build_control_paths(
                self._set_container_chain(
                    Control("gridOrders", "gridOrders", "OrdersGrid", "DataGrid", text="SKU,Qty\n",
                            image_tag="orders_grid"),
                    history_tab_chain
                )
            ),
            # Non-standard control: NO reliable AutomationId
            "chkPriority": self._build_control_paths(
                self._set_container_chain(
                    Control("chkPriority", None, "PriorityToggle", "CheckBox", text="Off",
                            image_tag="priority_checkbox", sibling_index=1, sibling_count=2),
                    general_tab_chain
                )
            ),
            "btnLogout": self._build_control_paths(
                self._set_container_chain(
                    Control("btnLogout", "btnLogout", "LogoutBtn", "Button", text="Logout",
                            image_tag="logout_button", sibling_index=0, sibling_count=1),
                    history_tab_chain
                )
            ),
        }
        # Rebuild grid text from orders
        self._update_grid_text()
        
        # Build hierarchical tree for XPath matching
        self._build_hierarchical_tree()

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
        """Find a single control by AutomationId."""
        for ctrl in self.controls.values():
            if ctrl.automation_id and ctrl.automation_id == automation_id:
                return ctrl
        return None

    def find_by_name(self, name: str) -> Optional[Control]:
        """Find a single control by Name."""
        for ctrl in self.controls.values():
            if ctrl.name == name:
                return ctrl
        return None

    def find_by_image_tag(self, tag: str) -> Optional[Control]:
        """Find a single control by image tag."""
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
    
    # ------------------------------------------------------------------
    # Find Multiple Elements (find_elements support)
    # ------------------------------------------------------------------
    def find_all_by_automation_id(self, automation_id: str) -> List[Control]:
        """Find all controls matching the given AutomationId.
        
        Args:
            automation_id: The AutomationId to search for.
            
        Returns:
            List of controls matching the AutomationId.
        """
        return [ctrl for ctrl in self.controls.values()
                if ctrl.automation_id and ctrl.automation_id == automation_id]
    
    def find_all_by_name(self, name: str) -> List[Control]:
        """Find all controls matching the given Name.
        
        Args:
            name: The Name property to search for.
            
        Returns:
            List of controls matching the Name.
        """
        return [ctrl for ctrl in self.controls.values()
                if ctrl.name == name]
    
    def find_all_by_control_type(self, control_type: str) -> List[Control]:
        """Find all controls of the given type.
        
        Args:
            control_type: The WPF control type (e.g., "Button", "TextBox").
            
        Returns:
            List of controls matching the type.
        """
        return [ctrl for ctrl in self.controls.values()
                if ctrl.control_type == control_type]
    
    def find_all_by_image_tag(self, tag: str) -> List[Control]:
        """Find all controls matching the given image tag.
        
        Args:
            tag: The image tag to search for.
            
        Returns:
            List of controls matching the image tag.
        """
        return [ctrl for ctrl in self.controls.values()
                if ctrl.image_tag == tag]
    
    def find_all_by_xpath(self, xpath: str) -> List[Control]:
        """Find all controls matching an XPath pattern.
        
        Note: For simplicity, this supports basic XPath patterns like
        //ControlType or ControlType[@AutomationId='xxx']
        
        Args:
            xpath: The XPath expression to evaluate.
            
        Returns:
            List of controls matching the XPath.
        """
        results = []
        
        # Handle //ControlType pattern
        if xpath.startswith("//"):
            type_match = xpath[2:].split("[")[0] if "[" in xpath else xpath[2:]
            results = [ctrl for ctrl in self.controls.values()
                     if ctrl.control_type == type_match]
            
            # Filter by AutomationId if specified
            if "[@AutomationId=" in xpath:
                import re
                match = re.search(r"\[@AutomationId='([^']+)'\]", xpath)
                if match:
                    aid = match.group(1)
                    results = [ctrl for ctrl in results if ctrl.automation_id == aid]
            
            # Filter by Name if specified
            elif "[@Name=" in xpath:
                import re
                match = re.search(r"\[@Name='([^']+)'\]", xpath)
                if match:
                    name = match.group(1)
                    results = [ctrl for ctrl in results if ctrl.name == name]
        
        return results

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

    def _build_hierarchical_tree(self):
        """Build the hierarchical virtual tree based on container chains."""
        # Group controls by their container chain
        self.root_container = {
            "tag": "Root",
            "children": [],
            "attrs": {}
        }
        
        if not self.controls:
            return
        
        # Build tree from container chains
        for key, ctrl in self.controls.items():
            # Start from root and traverse/create path
            self._add_control_to_tree(self.root_container, ctrl, key)
    
    def _add_control_to_tree(self, parent_node: dict, ctrl: Control, control_key: str):
        """Add a control to the tree based on its container chain."""
        if not ctrl.container_chain:
            # No container chain, add directly to parent
            parent_node["children"].append({
                "tag": ctrl.control_type,
                "attrs": {
                    "AutomationId": ctrl.automation_id or "",
                    "Name": ctrl.name
                },
                "control_key": control_key
            })
            return
        
        # Navigate/create path based on container chain
        current_node = parent_node
        
        for i, container in enumerate(ctrl.container_chain):
            ctype = container.get("type", "")
            aid = container.get("automationId")
            name = container.get("name", "")
            is_last = (i == len(ctrl.container_chain) - 1)
            
            # Find or create this container node
            child = self._find_or_create_container(
                current_node["children"],
                ctype,
                aid,
                name
            )
            
            if child is None:
                # Create new container
                child = {
                    "tag": ctype,
                    "attrs": {
                        "AutomationId": aid or "",
                        "Name": name
                    },
                    "children": []
                }
                current_node["children"].append(child)
            
            current_node = child
        
        # Add the actual control
        current_node["children"].append({
            "tag": ctrl.control_type,
            "attrs": {
                "AutomationId": ctrl.automation_id or "",
                "Name": ctrl.name
            },
            "control_key": control_key
        })
    
    def _find_or_create_container(self, children: list, ctype: str, aid: Optional[str], name: str) -> Optional[dict]:
        """Find an existing container or return None to create new."""
        for child in children:
            if child["tag"] != ctype:
                continue
            # Match by AutomationId or Name
            if aid and child["attrs"].get("AutomationId") == aid:
                return child
            if name and child["attrs"].get("Name") == name:
                return child
            # Match by type only if no identifier
            if not aid and not name:
                return child
        return None

    def _build_virtual_tree(self) -> dict:
        """Builds the virtual tree for XPath matching.
        
        Returns the root node with hierarchical structure.
        """
        if self.root_container is None:
            self._build_hierarchical_tree()
        return self.root_container

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
        """Check if a control is visible."""
        return bool(ctrl.visible)
    
    def is_enabled(self, ctrl: Control) -> bool:
        """Check if a control is enabled.
        
        Args:
            ctrl: The control to check.
            
        Returns:
            True if enabled, False otherwise.
        """
        return bool(ctrl.enabled)
    
    def get_attribute(self, ctrl: Control, attribute_name: str) -> Optional[str]:
        """Get a specific attribute value from a control.
        
        Args:
            ctrl: The control to get the attribute from.
            attribute_name: The attribute name (e.g., "AutomationId", "Name", 
                          "ControlType", "Text", "IsVisible", "IsEnabled").
            
        Returns:
            The attribute value as a string, or None if not supported.
        """
        attr_lower = attribute_name.lower()
        
        if attr_lower in ("automationid", "automation_id"):
            return ctrl.automation_id
        elif attr_lower == "name":
            return ctrl.name
        elif attr_lower in ("controltype", "type"):
            return ctrl.control_type
        elif attr_lower in ("text", "content"):
            return ctrl.text
        elif attr_lower in ("isvisible", "visible"):
            return str(ctrl.visible).lower()
        elif attr_lower in ("isenabled", "enabled"):
            return str(ctrl.enabled).lower()
        elif attr_lower == "imagetag":
            return ctrl.image_tag
        elif attr_lower == "xpath":
            # Return first FlaUI path as the canonical XPath
            return ctrl.flaui_paths[0] if ctrl.flaui_paths else None
        else:
            return None
    
    def capture_screenshot(self, ctrl: Optional[Control] = None) -> bytes:
        """Capture a screenshot (mock implementation).
        
        In production, this would capture the actual screen/window.
        For the mock, we return a minimal PNG placeholder.
        
        Args:
            ctrl: Optional control to capture. If None, captures entire screen.
            
        Returns:
            PNG image data as bytes.
        """
        # Minimal 1x1 transparent PNG
        return (
            b'\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR\x00\x00\x00\x01'
            b'\x00\x00\x00\x01\x08\x06\x00\x00\x00\x1f\x15\xc4\x89'
            b'\x00\x00\x00\nIDATx\x9cc\x00\x01\x00\x00\x05'
            b'\x00\x01\r\n-\xb4\x00\x00\x00\x00IEND\xaeB`\x82'
        )

    def double_click(self, ctrl: Control):
        """Double-click a control (mock: same as invoke)."""
        self.invoke(ctrl)

    def right_click(self, ctrl: Control):
        """Right-click a control (mock: same as invoke)."""
        self.invoke(ctrl)

    def press_keys(self, ctrl: Control, keys: str):
        """Press keys into a control (mock: append to text for text controls)."""
        if not ctrl.enabled or not ctrl.visible:
            raise ElementNotInteractableError(ctrl.key)
        if ctrl.control_type in ("TextBox", "PasswordBox"):
            ctrl.text += keys

    def drag_drop(self, ctrl: Control, target):
        """Drag and drop a control to a target (mock: same as invoke)."""
        self.invoke(ctrl)

    def hover(self, ctrl: Control):
        """Hover over a control (mock: same as invoke)."""
        self.invoke(ctrl)

    def scroll(self, ctrl: Control, direction: str):
        """Scroll a control (mock: no-op)."""
        if not ctrl.enabled or not ctrl.visible:
            raise ElementNotInteractableError(ctrl.key)


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
