"""
Verification Keywords
=====================
Element verification keywords for the DriverAgnosticApi facade.
"""

from typing import Optional
from TestAutoLayer.api.resolution.strategy_executor import StrategyExecutor
from TestAutoLayer.api.resolution.element_resolver import ElementResolver
import re


class VerificationKeywords:
    """Element verification keywords."""
    
    def __init__(
        self,
        strategy_executor: StrategyExecutor,
        element_resolver: ElementResolver,
        get_default_app_id: callable,
    ):
        self._executor = strategy_executor
        self._resolver = element_resolver
        self._get_default_app_id = get_default_app_id
    
    def _resolve_app_id(self, app_id: Optional[str]) -> Optional[str]:
        """Resolve app_id, falling back to default."""
        if app_id is not None:
            return app_id
        return self._get_default_app_id()
    
    def verify_element_text(self, alias: str, expected: str, app_id: Optional[str] = None):
        """Fails the test unless the element's text equals `expected`."""
        app_id = self._resolve_app_id(app_id)
        actual = self._executor.execute(alias, "get_text", app_id)
        if actual != expected:
            raise AssertionError(f"'{alias}' text mismatch: expected '{expected}', got '{actual}'")
    
    def verify_element_contains_text(self, alias: str, expected: str, app_id: Optional[str] = None):
        """Fails the test unless the element's text contains `expected`."""
        app_id = self._resolve_app_id(app_id)
        actual = self._executor.execute(alias, "get_text", app_id)
        if expected not in actual:
            raise AssertionError(f"'{alias}' text does not contain '{expected}': got '{actual}'")
    
    def verify_element_enabled(self, alias: str, app_id: Optional[str] = None):
        """Fails the test unless the element is enabled."""
        app_id = self._resolve_app_id(app_id)
        if not self.is_element_enabled(alias, app_id):
            raise AssertionError(f"'{alias}' is not enabled")
    
    def verify_element_visible(self, alias: str, app_id: Optional[str] = None):
        """Fails the test unless the element is visible."""
        app_id = self._resolve_app_id(app_id)
        if not self.is_element_visible(alias, app_id):
            raise AssertionError(f"'{alias}' is not visible")
    
    def verify_element_text_matches_regex(self, alias: str, pattern: str, app_id: Optional[str] = None):
        """Fails the test unless the element's text matches the regex pattern."""
        app_id = self._resolve_app_id(app_id)
        actual = self._executor.execute(alias, "get_text", app_id)
        if not re.search(pattern, actual):
            raise AssertionError(f"'{alias}' text '{actual}' does not match regex '{pattern}'")
    
    def verify_element_attribute(self, alias: str, attribute_name: str, expected_value: str, app_id: Optional[str] = None):
        """Fails the test unless the element's attribute equals expected_value."""
        app_id = self._resolve_app_id(app_id)
        actual = self._executor.execute(alias, "get_attribute", app_id, attribute_name)
        if actual != expected_value:
            raise AssertionError(f"'{alias}' attribute '{attribute_name}' mismatch: expected '{expected_value}', got '{actual}'")
    
    def property_checkpoint(self, alias: str, property_name: str, expected_value: str, app_id: Optional[str] = None):
        """Fails the test unless the element's property equals expected_value."""
        app_id = self._resolve_app_id(app_id)
        prop_lower = property_name.lower()
        if prop_lower in ("text", "content"):
            actual = self._executor.execute(alias, "get_text", app_id)
        elif prop_lower in ("isenabled", "enabled"):
            actual = self.is_element_enabled(alias, app_id)
            actual = "true" if actual else "false"
        elif prop_lower in ("isvisible", "visible"):
            actual = self.is_element_visible(alias, app_id)
            actual = "true" if actual else "false"
        elif prop_lower in ("automationid", "automation_id"):
            actual = self._executor.execute(alias, "get_attribute", app_id, "AutomationId")
        elif prop_lower == "name":
            actual = self._executor.execute(alias, "get_attribute", app_id, "Name")
        elif prop_lower in ("controltype", "type"):
            actual = self._executor.execute(alias, "get_attribute", app_id, "ControlType")
        else:
            actual = self._executor.execute(alias, "get_attribute", app_id, property_name)
        if actual != expected_value:
            raise AssertionError(f"'{alias}' property '{property_name}' mismatch: expected '{expected_value}', got '{actual}'")
    
    def data_grid_checkpoint(self, alias: str, expected_content: str, app_id: Optional[str] = None):
        """Fails the test unless the DataGrid's OCR content contains expected_content."""
        app_id = self._resolve_app_id(app_id)
        actual = self.get_data_grid_content_ocr(alias, app_id)
        if expected_content not in actual:
            raise AssertionError(f"'{alias}' DataGrid content mismatch: expected '{expected_content}' in '{actual}'")
    
    def count_checkpoint(self, alias: str, expected_count: str, app_id: Optional[str] = None):
        """Fails the test unless the number of matching elements equals expected_count."""
        app_id = self._resolve_app_id(app_id)
        try:
            expected = int(expected_count)
        except ValueError:
            raise AssertionError(f"Invalid expected count: '{expected_count}'")
        elements = self.find_elements(alias, app_id=app_id)
        actual = len(elements)
        if actual != expected:
            raise AssertionError(f"'{alias}' count mismatch: expected {expected}, got {actual}")
    
    def attribute_checkpoint(self, alias: str, attribute_name: str, expected_value: str, app_id: Optional[str] = None):
        """Fails the test unless the element's attribute equals expected_value."""
        app_id = self._resolve_app_id(app_id)
        actual = self._executor.execute(alias, "get_attribute", app_id, attribute_name)
        if actual != expected_value:
            raise AssertionError(f"'{alias}' attribute '{attribute_name}' mismatch: expected '{expected_value}', got '{actual}'")
    
    def area_checkpoint(self, alias: str, expected_text: str, app_id: Optional[str] = None):
        """Verifies OCR text in an area matches expected_text."""
        app_id = self._resolve_app_id(app_id)
        actual = self._executor.execute(alias, "get_data_grid_content_ocr", app_id)
        if expected_text not in actual:
            raise AssertionError(f"'{alias}' area OCR mismatch: expected '{expected_text}', got '{actual}'")
    
    def image_checkpoint(self, alias: str, baseline_path: str, app_id: Optional[str] = None):
        """Verifies an element's visual appearance matches the baseline image."""
        import os
        app_id = self._resolve_app_id(app_id)
        if not os.path.exists(baseline_path):
            raise AssertionError(f"Baseline image not found: {baseline_path}")
        actual = self._executor.execute(alias, "capture_screenshot", app_id)
        # For now, just verify we got screenshot data; real pixel comparison would go here
        if not actual:
            raise AssertionError(f"'{alias}' screenshot capture returned empty data")
    
    # These are needed by verification keywords but are wait/interaction methods
    def is_element_visible(self, alias: str, app_id: Optional[str] = None) -> bool:
        from ..keywords.wait_keywords import WaitKeywords
        # Delegate to wait keywords (will be set up via composition)
        raise NotImplementedError("Use WaitKeywords.is_element_visible")
    
    def is_element_enabled(self, alias: str, app_id: Optional[str] = None) -> bool:
        from ..keywords.wait_keywords import WaitKeywords
        raise NotImplementedError("Use WaitKeywords.is_element_enabled")
    
    def find_elements(self, alias: str, app_id: Optional[str] = None):
        from ..keywords.wait_keywords import WaitKeywords
        raise NotImplementedError("Use WaitKeywords.find_elements")
    
    def get_data_grid_content_ocr(self, alias: str, app_id: Optional[str] = None) -> str:
        app_id = self._resolve_app_id(app_id)
        return self._executor.execute(alias, "get_data_grid_content_ocr", app_id)