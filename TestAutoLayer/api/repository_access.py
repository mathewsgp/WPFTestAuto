"""
Repository Access — loads the Element & Step Repositories (YAML) at Suite
Setup and caches them as plain dictionaries, keyed by alias. This is the
single place that knows how repository files are laid out on disk; Layer 3
(DriverAgnosticApi) only ever asks it for "the locator + step for this
alias" and never touches YAML directly.

Hierarchical Element Support with parentAlias:
- Each element specifies its parent via parentAlias
- Parent can be: "Window", another element alias, or container identifier
- Relative XPath is used from the parent (not full path from Window)
- Paths are resolved by walking up the parent chain

Example:
  LoginPage.MainWindow:
    controlType: "Window"
    # No parent - this is the root
    
  LoginPage.MainWindow.txtUsername:
    parentAlias: "LoginPage.MainWindow"  # Parent element
    relativeXPath: "TextBox[@AutomationId='txtUsername']"  # Relative to parent
"""

import glob
import os
import yaml
from typing import Dict, List, Optional, Tuple

_THIS_DIR = os.path.dirname(os.path.abspath(__file__))
_REPO_ROOT = os.path.join(_THIS_DIR, "..", "..", "Tests", "repository")

_elements_cache = None
_steps_cache = None
_element_parent_cache: Dict[str, str] = {}  # alias -> parentAlias
_element_relative_xpath_cache: Dict[str, str] = {}  # alias -> relativeXPath


def _load_yaml_dir(subfolder, top_key):
    merged = {}
    pattern = os.path.join(_REPO_ROOT, subfolder, "*.yaml")
    for path in sorted(glob.glob(pattern)):
        with open(path, "r", encoding="utf-8") as f:
            data = yaml.safe_load(f) or {}
        merged.update(data.get(top_key, {}))
    return merged


def _build_element_caches():
    """Build caches for parent resolution."""
    global _element_parent_cache, _element_relative_xpath_cache
    _element_parent_cache = {}
    _element_relative_xpath_cache = {}
    
    elements = load_elements()
    for alias, element in elements.items():
        # Store parent alias
        _element_parent_cache[alias] = element.get("parentAlias")
        
        # Store relative XPath
        if "relativeXPath" in element:
            _element_relative_xpath_cache[alias] = element["relativeXPath"]
        elif "strategies" in element:
            # Extract from first strategy's XPath if it looks relative
            for driver, strategies in element.get("strategies", {}).items():
                for strategy in strategies:
                    xpath = strategy.get("value", "")
                    if xpath and not xpath.startswith("/"):
                        _element_relative_xpath_cache[alias] = xpath
                        break
                if alias in _element_relative_xpath_cache:
                    break


def load_elements(force_reload=False):
    global _elements_cache
    if _elements_cache is None or force_reload:
        _elements_cache = _load_yaml_dir("elements", "elements")
        _build_element_caches()  # Rebuild caches when elements reload
    return _elements_cache


def load_steps(force_reload=False):
    global _steps_cache
    if _steps_cache is None or force_reload:
        _steps_cache = _load_yaml_dir("steps", "steps")
    return _steps_cache


def get_element(alias: str, app_id: str = None) -> dict:
    elements = load_elements()
    if alias not in elements:
        raise KeyError(f"Element Repository: no entry for alias '{alias}'")
    element = elements[alias]
    
    # If app_id is specified, check if element is scoped to this app or is global
    if app_id and "appId" in element:
        if element["appId"] != app_id:
            raise KeyError(f"Element '{alias}' is not available for app '{app_id}'")
    
    return element


def get_step(alias: str) -> dict:
    steps = load_steps()
    if alias not in steps:
        raise KeyError(f"Step Repository: no entry for alias '{alias}'")
    return steps[alias]


def get_parent_alias(alias: str, app_id: str = None) -> Optional[str]:
    """Get the parentAlias for an element.
    
    Returns None if element has no parent (is root/Window).
    """
    cache_key = f"{app_id or 'global'}:{alias}"
    if cache_key not in _element_parent_cache:
        element = get_element(alias, app_id=app_id)
        _element_parent_cache[cache_key] = element.get("parentAlias")
    return _element_parent_cache.get(cache_key)


def get_relative_xpath(alias: str, app_id: str = None) -> Optional[str]:
    """Get the relative XPath for an element.
    
    Returns None if element doesn't have a relativeXPath defined.
    """
    cache_key = f"{app_id or 'global'}:{alias}"
    if cache_key not in _element_relative_xpath_cache:
        element = get_element(alias, app_id=app_id)
        _element_relative_xpath_cache[cache_key] = element.get("relativeXPath")
    return _element_relative_xpath_cache.get(cache_key)


def resolve_full_path(alias: str, app_id: str = None, driver_name: str = None) -> Tuple[str, str]:
    """Resolve the full XPath for an element by walking up the parent chain.
    
    Args:
        alias: Element alias
        app_id: Optional app context ID to filter elements.
        driver_name: Optional driver name (FlaUI uses Name for Window, others use AutomationId).
    
    Returns:
        Tuple of (full_path, parent_alias)
    """
    path_parts = []
    parent_alias = get_parent_alias(alias, app_id=app_id)
    current_alias = parent_alias
    visited = set()
    
    while current_alias:
        if current_alias in visited:
            raise ValueError(f"Circular parent reference detected for alias: {current_alias}")
        visited.add(current_alias)
        
        element = get_element(current_alias, app_id=app_id)
        parent_alias = get_parent_alias(current_alias, app_id=app_id)
        
        control_type = element.get("controlType", "")
        window_id = element.get("windowAutomationId") or element.get("windowId", "MainWindow")
        
        if "xpathPrefix" in element:
            path_parts.insert(0, element["xpathPrefix"])
        elif control_type == "Window":
            if driver_name == "FlaUI":
                window_name = element.get("name") or window_id
                if window_name and window_name != window_id:
                    path_parts.insert(0, f"Window[@Name='{window_name}']")
                else:
                    path_parts.insert(0, f"Window[@AutomationId='{window_id}']")
            else:
                path_parts.insert(0, f"Window[@AutomationId='{window_id}']")
        elif "automationId" in element:
            path_parts.insert(0, f"{control_type}[@AutomationId='{element['automationId']}']")
        elif "name" in element:
            path_parts.insert(0, f"{control_type}[@Name='{element['name']}']")
        
        if parent_alias is None:
            break
        
        current_alias = parent_alias
    
    full_path = "/" + "/".join(path_parts)
    parent_alias = get_parent_alias(alias, app_id=app_id)
    
    return full_path, parent_alias


def build_absolute_xpath(alias: str, app_id: str = None, driver_name: str = None) -> str:
    """Build the complete absolute XPath for an element.
    
    Args:
        alias: Element alias
        app_id: Optional app context ID to filter elements.
        driver_name: Optional driver name for driver-specific Window prefix.
    """
    full_path, parent_alias = resolve_full_path(alias, app_id=app_id, driver_name=driver_name)
    relative_xpath = get_relative_xpath(alias, app_id=app_id)
    
    if relative_xpath:
        return f"{full_path}/{relative_xpath}"
    return full_path


def get_strategies(alias: str, driver: str = None, app_id: str = None) -> dict:
    """Returns strategies for a specific driver or all strategies.
    
    Args:
        alias: Element alias in repository
        driver: Driver name (FlaUI, WPFSpy, Sikuli). If None, returns all.
        app_id: Optional app context ID to filter elements.
    
    Returns:
        Dict of strategies. Each strategy has multiple search methods with priority.
    """
    element = get_element(alias, app_id=app_id)
    all_strategies = element.get("strategies", {})
    
    if driver:
        if driver in all_strategies:
            return {driver: all_strategies[driver]}
        return {}
    
    return all_strategies


def get_driver_strategies_sorted(alias: str, driver: str) -> list:
    """Returns driver strategies sorted by priority.
    
    Args:
        alias: Element alias in repository
        driver: Driver name (FlaUI, WPFSpy, Sikuli)
    
    Returns:
        List of strategy dicts sorted by priority (lowest first).
    """
    strategies = get_strategies(alias, driver)
    if driver not in strategies:
        return []
    
    strategy_list = strategies[driver]
    # Sort by priority (ensure priority field exists, default to 99)
    return sorted(strategy_list, key=lambda s: s.get("priority", 99))


def get_all_driver_strategies_sorted(alias: str, app_id: str = None) -> dict:
    """Returns all driver strategies sorted by priority.
    
    Args:
        alias: Element alias in repository
        app_id: Optional app context ID to filter elements.
    
    Returns:
        Dict mapping driver name -> sorted list of strategies.
    """
    all_strategies = get_strategies(alias, app_id=app_id)
    result = {}
    for driver, strategy_list in all_strategies.items():
        result[driver] = sorted(strategy_list, key=lambda s: s.get("priority", 99))
    return result


def has_automation_id(alias: str) -> bool:
    """Check if element has an AutomationId strategy.
    
    Returns False for controls that don't expose AutomationId (custom controls).
    """
    element = get_element(alias)
    if element.get("hasAutomationId", True) is False:
        return False
    
    strategies = get_strategies(alias, "FlaUI")
    if not strategies:
        return False
    
    for strategy in strategies.get("FlaUI", []):
        if strategy.get("searchBy") == "AutomationId":
            return True
    return False


# Supported search methods across all drivers
SUPPORTED_SEARCH_METHODS = {
    "FlaUI": [
        "AutomationId",    # Primary - most stable
        "Name",            # Secondary - usually stable
        "XPath",          # Fallback - can break with UI changes
        "ClassName",      # Additional - useful for custom controls
        "Index",          # Position-based - last resort
        "Text",           # Content-based - for text controls
    ],
    "WPFSpy": [
        "AutomationId",
        "Name",
        "XPath",
        "ClassName",
        "Index",
    ],
    "Sikuli": [
        "ImageTag",       # Image-based fallback
        "Name",           # Name-based as alternative
    ]
}


def expand_strategies(alias: str) -> dict:
    """Expand element strategies to include all available search methods.
    
    This function analyzes an element's properties and generates additional
    strategies that could be used as fallbacks.
    
    Args:
        alias: Element alias in repository
    
    Returns:
        Dict mapping driver name -> list of strategies
    """
    element = get_element(alias)
    
    # Get existing strategies
    existing_strategies = get_all_driver_strategies_sorted(alias)
    
    # Extract element properties that can be used for strategies
    automation_id = element.get("automationId") or element.get("AutomationId")
    name = element.get("name") or element.get("Name")
    class_name = element.get("className") or element.get("ClassName")
    control_type = element.get("controlType") or element.get("ControlType")
    display_name = element.get("displayName")
    
    expanded = {}
    
    for driver, methods in SUPPORTED_SEARCH_METHODS.items():
        driver_strategies = []
        priority = 1
        
        # Collect existing strategy values to avoid duplicates
        existing_values = set()
        if driver in existing_strategies:
            for s in existing_strategies[driver]:
                existing_values.add(s.get("searchBy", "").lower() + ":" + s.get("value", "").lower())
        
        for method in methods:
            # Check if this method should be added
            if method == "AutomationId" and automation_id:
                value = automation_id
                if f"automationid:{value.lower()}" not in existing_values:
                    driver_strategies.append({
                        "searchBy": "AutomationId",
                        "value": value,
                        "priority": priority,
                        "source": "expanded"
                    })
                    priority += 1
            
            elif method == "Name" and name and name != automation_id:
                value = name
                if f"name:{value.lower()}" not in existing_values:
                    driver_strategies.append({
                        "searchBy": "Name",
                        "value": value,
                        "priority": priority,
                        "source": "expanded"
                    })
                    priority += 1
            
            elif method == "ClassName" and class_name:
                value = class_name
                if f"classname:{value.lower()}" not in existing_values:
                    driver_strategies.append({
                        "searchBy": "ClassName",
                        "value": value,
                        "priority": priority,
                        "source": "expanded"
                    })
                    priority += 1
            
            elif method == "Text" and display_name:
                value = display_name
                if f"text:{value.lower()}" not in existing_values:
                    driver_strategies.append({
                        "searchBy": "Text",
                        "value": value,
                        "priority": priority,
                        "source": "expanded"
                    })
                    priority += 1
        
        # Merge with existing strategies (existing take priority)
        if driver in existing_strategies:
            # Add expanded strategies after existing ones
            driver_strategies = existing_strategies[driver] + driver_strategies
        
        if driver_strategies:
            expanded[driver] = driver_strategies
    
    return expanded


def add_index_strategies(alias: str, parent_xpath: str, sibling_count: int) -> list:
    """Generate Index-based strategies for elements with many siblings.
    
    Args:
        alias: Element alias
        parent_xpath: XPath of parent element
        sibling_count: Number of siblings of the same type
    
    Returns:
        List of index-based strategies
    """
    element = get_element(alias)
    control_type = element.get("controlType", "Unknown")
    
    strategies = []
    
    # Only add index strategy if element has siblings of same type
    if sibling_count > 1:
        # Find position among siblings (1-based)
        # This would need runtime info, so we provide a template
        strategies.append({
            "searchBy": "Index",
            "value": control_type,  # Template: find Nth control of this type
            "priority": 99,  # Lowest priority - last resort
            "source": "auto_generated",
            "note": "Use when all other strategies fail"
        })
    
    return strategies


def suggest_additional_strategies(alias: str) -> dict:
    """Suggest additional strategies that could be added to an element.
    
    Useful for repository maintenance and improving element stability.
    
    Args:
        alias: Element alias in repository
    
    Returns:
        Dict mapping driver -> list of suggested strategies
    """
    element = get_element(alias)
    current_strategies = get_strategies(alias)
    
    suggestions = {}
    
    automation_id = element.get("automationId") or element.get("AutomationId")
    name = element.get("name") or element.get("Name")
    class_name = element.get("className") or element.get("ClassName")
    display_name = element.get("displayName")
    
    for driver, methods in SUPPORTED_SEARCH_METHODS.items():
        driver_current = current_strategies.get(driver, [])
        current_by = set(s.get("searchBy", "") for s in driver_current)
        
        driver_suggestions = []
        
        for method in methods:
            if method not in current_by:
                # This method is not yet configured
                if method == "AutomationId" and automation_id:
                    driver_suggestions.append({
                        "searchBy": "AutomationId",
                        "value": automation_id,
                        "reason": "Primary locator - most stable",
                        "priority": 1
                    })
                
                elif method == "Name" and name:
                    driver_suggestions.append({
                        "searchBy": "Name",
                        "value": name,
                        "reason": "Secondary locator - good fallback",
                        "priority": 2
                    })
                
                elif method == "ClassName" and class_name:
                    driver_suggestions.append({
                        "searchBy": "ClassName",
                        "value": class_name,
                        "reason": "Useful for custom controls",
                        "priority": 3
                    })
                
                elif method == "Text" and display_name:
                    driver_suggestions.append({
                        "searchBy": "Text",
                        "value": display_name,
                        "reason": "Content-based - for text controls",
                        "priority": 4
                    })
        
        if driver_suggestions:
            suggestions[driver] = driver_suggestions
    
    return suggestions
