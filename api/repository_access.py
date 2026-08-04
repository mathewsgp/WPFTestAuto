"""
Repository Access — loads the Element & Step Repositories (YAML) at Suite
Setup and caches them as plain dictionaries, keyed by alias. This is the
single place that knows how repository files are laid out on disk; Layer 3
(DriverAgnosticApi) only ever asks it for "the locator + step for this
alias" and never touches YAML directly.
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


def get_strategies(alias: str) -> dict:
    """Returns the WPFSpy locator dict for an alias.
    
    WPFSpy-only mode: only returns the WPFSpy strategy.
    """
    element = get_element(alias)
    all_strategies = element.get("strategies", {})
    if "WPFSpy" in all_strategies:
        return {"WPFSpy": all_strategies["WPFSpy"]}
    return {}
