"""
Path Builder - IDE Recording and Playback Support
=================================================

This module provides utilities for building and parsing hierarchical element paths
for use in IDE recording/playback scenarios.

Features:
- Build full hierarchical XPath from container chain
- Parse XPath into container chain and control locator
- Generate locators for all drivers (FlaUI, WPFSpy, Sikuli)
- Validate paths against the element hierarchy
"""

from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple, Any
import re


@dataclass
class ContainerLocator:
    """Represents a container in the hierarchy."""
    container_type: str
    automation_id: Optional[str] = None
    name: Optional[str] = None
    index: Optional[int] = None  # 0-based index among siblings
    
    def to_dict(self) -> Dict:
        result = {"type": self.container_type}
        if self.automation_id:
            result["automationId"] = self.automation_id
        if self.name:
            result["name"] = self.name
        if self.index is not None:
            result["index"] = self.index
        return result
    
    @classmethod
    def from_dict(cls, data: Dict) -> "ContainerLocator":
        return cls(
            container_type=data.get("type", ""),
            automation_id=data.get("automationId"),
            name=data.get("name"),
            index=data.get("index")
        )


@dataclass
class ElementLocator:
    """Represents a complete element location including its container hierarchy."""
    control_type: str
    automation_id: Optional[str] = None
    name: Optional[str] = None
    index: Optional[int] = None
    container_chain: List[ContainerLocator] = field(default_factory=list)
    image_path: Optional[str] = None
    
    def to_dict(self) -> Dict:
        result = {
            "controlType": self.control_type,
        }
        if self.automation_id:
            result["automationId"] = self.automation_id
        if self.name:
            result["name"] = self.name
        if self.index is not None:
            result["index"] = self.index
        if self.container_chain:
            result["containerPath"] = [c.to_dict() for c in self.container_chain]
        if self.image_path:
            result["imagePath"] = self.image_path
        return result
    
    @classmethod
    def from_dict(cls, data: Dict) -> "ElementLocator":
        container_chain = []
        for c in data.get("containerPath", []):
            container_chain.append(ContainerLocator.from_dict(c))
        
        return cls(
            control_type=data.get("controlType", ""),
            automation_id=data.get("automationId"),
            name=data.get("name"),
            index=data.get("index"),
            container_chain=container_chain,
            image_path=data.get("imagePath")
        )


class PathBuilder:
    """Builds and parses hierarchical element paths."""
    
    # Container types that can be parents of controls
    CONTAINER_TYPES = {
        "Window", "TabControl", "TabItem", "GroupBox", "StackPanel",
        "Panel", "Grid", "DockPanel", "ScrollViewer", "Border",
        "Canvas", "WrapPanel", "UniformGrid", "ContentControl"
    }
    
    @staticmethod
    def build_xpath(
        container_chain: List[Dict],
        control_type: str,
        automation_id: Optional[str] = None,
        name: Optional[str] = None,
        index: Optional[int] = None,
        driver: str = "FlaUI"
    ) -> str:
        """Build a full XPath from container chain and control info.
        
        Args:
            container_chain: List of container definitions
            control_type: Type of the control (TextBox, Button, etc.)
            automation_id: AutomationId of the control (if available)
            name: Name of the control (if available)
            index: Index among siblings (if no unique identifier)
            driver: Driver type (FlaUI, WPFSpy)
        
        Returns:
            Full XPath string
        """
        parts = []
        
        for i, container in enumerate(container_chain):
            ctype = container.get("type", "")
            aid = container.get("automationId")
            cname = container.get("name")
            cindex = container.get("index")
            
            # Build predicate for this container
            if aid:
                predicate = f"[@AutomationId='{aid}']"
            elif cname:
                predicate = f"[@Name='{cname}']"
            elif cindex is not None:
                predicate = f"[{cindex + 1}]"  # XPath is 1-based
            else:
                predicate = ""
            
            parts.append(f"{ctype}{predicate}")
        
        # Build control predicate
        if automation_id:
            control_predicate = f"[@AutomationId='{automation_id}']"
        elif name:
            control_predicate = f"[@Name='{name}']"
        elif index is not None:
            control_predicate = f"[{index + 1}]"  # XPath is 1-based
        else:
            control_predicate = ""
        
        parts.append(f"{control_type}{control_predicate}")
        
        return "/" + "/".join(parts)
    
    @staticmethod
    def parse_xpath(xpath: str) -> Tuple[List[ContainerLocator], ElementLocator]:
        """Parse an XPath into container chain and control locator.
        
        Args:
            xpath: Full XPath string
        
        Returns:
            Tuple of (container_chain, element_locator)
        """
        # Pattern to match: tag[@attr='value'] or tag[index] or just tag
        pattern = r'([^[]+)(?:\[([^\]]+)\])?'
        
        # Remove leading slash and split
        xpath = xpath.lstrip('/')
        parts = xpath.split('/')
        
        container_chain = []
        control_locator = None
        
        for i, part in enumerate(parts):
            if not part:
                continue
                
            match = re.match(pattern, part)
            if not match:
                continue
            
            tag = match.group(1)
            pred = match.group(2)
            
            automation_id = None
            name = None
            index = None
            
            if pred:
                # Check for @AutomationId='value'
                aid_match = re.match(r"@AutomationId='([^']+)'", pred)
                if aid_match:
                    automation_id = aid_match.group(1)
                # Check for @Name='value'
                elif re.match(r"@Name='([^']+)'", pred):
                    name_match = re.match(r"@Name='([^']+)'", pred)
                    name = name_match.group(1)
                # Check for numeric index
                elif pred.isdigit():
                    index = int(pred) - 1  # Convert to 0-based
            
            if tag == "Window":
                # Window is always the root, treat as first container
                container_chain.append(ContainerLocator(
                    container_type=tag,
                    automation_id=automation_id,
                    name=name,
                    index=index
                ))
            elif tag in PathBuilder.CONTAINER_TYPES:
                # Container element
                container_chain.append(ContainerLocator(
                    container_type=tag,
                    automation_id=automation_id,
                    name=name,
                    index=index
                ))
            else:
                # Control element
                control_locator = ElementLocator(
                    control_type=tag,
                    automation_id=automation_id,
                    name=name,
                    index=index
                )
        
        return container_chain, control_locator
    
    @staticmethod
    def build_all_paths(
        container_chain: List[Dict],
        control_type: str,
        automation_id: Optional[str] = None,
        name: Optional[str] = None,
        image_path: Optional[str] = None
    ) -> Dict[str, List[Dict]]:
        """Build all paths for all drivers.
        
        Args:
            container_chain: List of container definitions
            control_type: Type of the control
            automation_id: AutomationId (if available)
            name: Name (if available)
            image_path: Image path for Sikuli fallback
        
        Returns:
            Dict mapping driver name to list of strategies
        """
        strategies = {}
        priority = 1
        
        # FlaUI paths
        flaui_strategies = []
        if automation_id:
            xpath = PathBuilder.build_xpath(
                container_chain, control_type, 
                automation_id=automation_id,
                driver="FlaUI"
            )
            flaui_strategies.append({
                "searchBy": "XPath",
                "value": xpath,
                "priority": 1
            })
            flaui_strategies.append({
                "searchBy": "AutomationId",
                "value": automation_id,
                "priority": 1
            })
        
        if name:
            xpath = PathBuilder.build_xpath(
                container_chain, control_type,
                name=name,
                driver="FlaUI"
            )
            flaui_strategies.append({
                "searchBy": "XPath",
                "value": xpath,
                "priority": len(flaui_strategies) + 1
            })
            flaui_strategies.append({
                "searchBy": "Name",
                "value": name,
                "priority": len(flaui_strategies) + 1
            })
        
        if flaui_strategies:
            strategies["FlaUI"] = flaui_strategies
        
        # WPFSpy paths
        wpfspy_strategies = []
        if automation_id:
            xpath = PathBuilder.build_xpath(
                container_chain, control_type,
                automation_id=automation_id,
                driver="WPFSpy"
            )
            wpfspy_strategies.append({
                "searchBy": "XPath",
                "value": xpath,
                "priority": 1
            })
        
        if name:
            # Build with Name for window and Name for control
            xpath = PathBuilder.build_xpath(
                container_chain, control_type,
                name=name,
                driver="WPFSpy"
            )
            wpfspy_strategies.append({
                "searchBy": "XPath",
                "value": xpath,
                "priority": len(wpfspy_strategies) + 1
            })
        
        if wpfspy_strategies:
            strategies["WPFSpy"] = wpfspy_strategies
        
        # Sikuli fallback
        if image_path:
            strategies["Sikuli"] = [{
                "searchBy": "Image",
                "value": image_path,
                "priority": 99  # Lowest priority
            }]
        
        return strategies
    
    @staticmethod
    def generate_locator_record(
        control_type: str,
        automation_id: Optional[str] = None,
        name: Optional[str] = None,
        container_chain: Optional[List[Dict]] = None,
        image_path: Optional[str] = None
    ) -> Dict:
        """Generate a complete locator record for recording.
        
        This format is suitable for storing in the element repository
        or passing to the IDE for display.
        """
        container_chain = container_chain or []
        
        strategies = PathBuilder.build_all_paths(
            container_chain=container_chain,
            control_type=control_type,
            automation_id=automation_id,
            name=name,
            image_path=image_path
        )
        
        return {
            "controlType": control_type,
            "automationId": automation_id,
            "name": name,
            "containerPath": container_chain,
            "imagePath": image_path,
            "strategies": strategies
        }


def record_element(
    control_type: str,
    automation_id: Optional[str] = None,
    name: Optional[str] = None,
    container_chain: Optional[List[Dict]] = None,
    image_path: Optional[str] = None
) -> str:
    """Record an element and return its repository entry.
    
    Args:
        control_type: Type of the control
        automation_id: AutomationId from UIA
        name: Name from UIA
        container_chain: List of {type, automationId, name} for containers
        image_path: Path to reference image for Sikuli
    
    Returns:
        YAML-formatted repository entry
    """
    import yaml
    
    locator = PathBuilder.generate_locator_record(
        control_type=control_type,
        automation_id=automation_id,
        name=name,
        container_chain=container_chain,
        image_path=image_path
    )
    
    # Convert to YAML format
    return yaml.dump({"elements": locator}, default_flow_style=False, sort_keys=False)


# CLI tool for recording elements
if __name__ == "__main__":
    import sys
    
    if len(sys.argv) > 1:
        if sys.argv[1] == "record":
            # Example: python path_builder.py record TextBox txtName "Name Label" "MainWindow/TabControl/TabItem"
            if len(sys.argv) < 5:
                print("Usage: python path_builder.py record <type> <automationId> <name> [containerChain]")
                sys.exit(1)
            
            control_type = sys.argv[2]
            automation_id = sys.argv[3]
            name = sys.argv[4]
            container_str = sys.argv[5] if len(sys.argv) > 5 else ""
            
            # Parse container chain from string
            container_chain = []
            if container_str:
                for i, part in enumerate(container_str.split("/")):
                    if part:
                        container_chain.append({"type": part})
            
            result = record_element(
                control_type=control_type,
                automation_id=automation_id,
                name=name,
                container_chain=container_chain
            )
            print(result)
        elif sys.argv[1] == "parse":
            # Example: python path_builder.py parse "/Window/TabControl/TextBox[@Name='txtName']"
            if len(sys.argv) < 3:
                print("Usage: python path_builder.py parse <xpath>")
                sys.exit(1)
            
            xpath = sys.argv[2]
            containers, element = PathBuilder.parse_xpath(xpath)
            
            print("Container Chain:")
            for c in containers:
                print(f"  - {c.container_type}: aid={c.automation_id}, name={c.name}, index={c.index}")
            
            print(f"\nControl: {element.control_type}")
            print(f"  automationId: {element.automation_id}")
            print(f"  name: {element.name}")
            print(f"  index: {element.index}")
    else:
        print("Usage:")
        print("  python path_builder.py record <type> <automationId> <name> [containerChain]")
        print("  python path_builder.py parse <xpath>")
