"""
Repository Access — loads the Element & Step Repositories (YAML) at Suite
Setup and caches them as plain dictionaries, keyed by alias. This is the
single place that knows how repository files are laid out on disk; Layer 3
(DriverAgnosticApi) only ever asks it for "the locator + step for this
alias" and never touches YAML directly.

Multi-Strategy Support:
- Each element has strategies for multiple drivers (FlaUI, WPFSpy, Sikuli)
- Each driver strategy has multiple search methods with priority
- Priority order: AutomationId -> Name -> Type+Index -> Image
"""

import glob
import os
import yaml

_THIS_DIR = os.path.dirname(os.path.abspath(__file__))
_REPO_ROOT = os.path.join(_THIS_DIR, "..", "repository")

_elements_cache = None
_steps_cache = None


def _load_yaml_dir(subfolder, top_key):
    merged = {}
    pattern = os.path.join(_REPO_ROOT, subfolder, "*.yaml")
    for path in sorted(glob.glob(pattern)):
        with open(path, "r") as f:
            data = yaml.safe_load(f) or {}
        merged.update(data.get(top_key, {}))
    return merged


def load_elements(force_reload=False):
    global _elements_cache
    if _elements_cache is None or force_reload:
        _elements_cache = _load_yaml_dir("elements", "elements")
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
