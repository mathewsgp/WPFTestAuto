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
_REPO_ROOT = os.path.join(_THIS_DIR, "..", "repository")

_elements_cache = None
_steps_cache = None
_element_parent_cache: Dict[str, str] = {}  # alias -> parentAlias
_element_relative_xpath_cache: Dict[str, str] = {}  # alias -> relativeXPath


def _load_yaml_dir(subfolder, top_key):
    merged = {}
    pattern = os.path.join(_REPO_ROOT, subfolder, "*.yaml")
    for path in sorted(glob.glob(pattern)):
        with open(path, "r") as f:
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


def get_element(alias: str) -> dict:
    elements = load_elements()
    if alias not in elements:
        raise KeyError(f"Element Repository: no entry for alias '{alias}'")
    return elements[alias]


def get_step(alias: str) -> dict:
    steps = load_steps()
    if alias not in steps:
        raise KeyError(f"Step Repository: no entry for alias '{alias}'")
    return steps[alias]


def get_parent_alias(alias: str) -> Optional[str]:
    """Get the parentAlias for an element.
    
    Returns None if element has no parent (is root/Window).
    """
    if alias not in _element_parent_cache:
        element = get_element(alias)
        _element_parent_cache[alias] = element.get("parentAlias")
    return _element_parent_cache.get(alias)


def get_relative_xpath(alias: str) -> Optional[str]:
    """Get the relative XPath for an element.
    
    Returns None if element doesn't have a relativeXPath defined.
    """
    if alias not in _element_relative_xpath_cache:
        element = get_element(alias)
        _element_relative_xpath_cache[alias] = element.get("relativeXPath")
    return _element_relative_xpath_cache.get(alias)


def resolve_full_path(alias: str) -> Tuple[str, str]:
    """Resolve the full XPath for an element by walking up the parent chain.
    
    Returns:
        Tuple of (full_path, parent_alias) where:
        - full_path: Complete XPath from Window to element's parent
        - parent_alias: The parent alias used (for relative XPath appending)
    
    Example:
        LoginPage.MainWindow.GroupBox.CustomerName
          parentAlias: "LoginPage.MainWindow.GroupBox"
          relativeXPath: "TextBox[@AutomationId='CustomerName']"
        
        Returns:
            ("/Window[@AutomationId='MainWindow']/GroupBox[@AutomationId='CustomerGroup']",
             "LoginPage.MainWindow.GroupBox")
    
    The caller should append the relativeXPath to the returned full_path.
    """
    path_parts = []
    current_alias = alias
    visited = set()  # Prevent infinite loops
    
    while current_alias:
        if current_alias in visited:
            raise ValueError(f"Circular parent reference detected for alias: {current_alias}")
        visited.add(current_alias)
        
        element = get_element(current_alias)
        
        # Add this element's contribution to the path
        control_type = element.get("controlType", "")
        window_id = element.get("windowAutomationId") or element.get("windowId", "MainWindow")
        
        # Get XPath prefix for this element
        if "xpathPrefix" in element:
            path_parts.insert(0, element["xpathPrefix"])
        elif control_type == "Window":
            path_parts.insert(0, f"Window[@AutomationId='{window_id}']")
        elif "automationId" in element:
            path_parts.insert(0, f"{control_type}[@AutomationId='{element['automationId']}']")
        elif "name" in element:
            path_parts.insert(0, f"{control_type}[@Name='{element['name']}']")
        
        # Get parent
        parent_alias = get_parent_alias(current_alias)
        if parent_alias is None:
            # Reached root
            break
        
        current_alias = parent_alias
    
    full_path = "/" + "/".join(path_parts)
    parent_alias = get_parent_alias(alias)
    
    return full_path, parent_alias


def build_absolute_xpath(alias: str) -> str:
    """Build the complete absolute XPath for an element.
    
    Walks up the parent chain and appends the relative XPath.
    
    Example:
        alias: "LoginPage.MainWindow.txtUsername"
        parentAlias: "LoginPage.MainWindow"
        relativeXPath: "TextBox[@AutomationId='txtUsername']"
        
        Returns:
            "/Window[@AutomationId='MainWindow']/TextBox[@AutomationId='txtUsername']"
    """
    full_path, parent_alias = resolve_full_path(alias)
    relative_xpath = get_relative_xpath(alias)
    
    if relative_xpath:
        return f"{full_path}/{relative_xpath}"
    return full_path


def get_strategies(alias: str, driver: str = None) -> dict:
    """Returns strategies for a specific driver or all strategies.
    
    Args:
        alias: Element alias in repository
        driver: Driver name (FlaUI, WPFSpy, Sikuli). If None, returns all.
    
    Returns:
        Dict of strategies. Each strategy has multiple search methods with priority.
    """
    element = get_element(alias)
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


def get_all_driver_strategies_sorted(alias: str) -> dict:
    """Returns all driver strategies sorted by priority.
    
    Returns:
        Dict mapping driver name -> sorted list of strategies.
    """
    all_strategies = get_strategies(alias)
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
