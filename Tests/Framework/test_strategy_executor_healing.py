"""
Strategy Executor Healing Integration Tests
============================================

Verifies that when StrategyExecutor falls back from a failing primary
strategy to a succeeding secondary strategy, healing metadata is
correctly recorded in the HealingMetadataStore.

Run: pytest Tests/Framework/test_strategy_executor_healing.py -v
"""

import sys
import os
import tempfile
import shutil
import time
from unittest.mock import MagicMock, PropertyMock

import pytest

# Add api directory to path
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "TestAutoLayer", "api"))

from resolution.strategy_executor import StrategyExecutor
from healing_metadata_store import HealingMetadataStore
from base_driver import BaseDriver, ElementHandle


class MockDriver(BaseDriver):
    """Mock driver that can be configured to fail or succeed on find_element."""
    
    def __init__(self, name="Mock", fail_find=False, fail_invoke=False):
        self.name = name
        self.fail_find = fail_find
        self.fail_invoke = fail_invoke
        self.find_call_count = 0
        self.invoke_call_count = 0
    
    @property
    def name(self):
        return self._name
    
    @name.setter
    def name(self, value):
        self._name = value
    
    def find_element(self, locator):
        self.find_call_count += 1
        if self.fail_find:
            raise Exception(f"{self.name}: element not found")
        return ElementHandle(
            locator=locator,
            driver_name=self.name,
            found_at=time.time()
        )
    
    def find_elements(self, locator):
        self.find_call_count += 1
        if self.fail_find:
            raise Exception(f"{self.name}: no elements found")
        return [ElementHandle(
            locator=locator,
            driver_name=self.name,
            found_at=time.time()
        )]
    
    def invoke(self, element):
        self.invoke_call_count += 1
        if self.fail_invoke:
            raise Exception(f"{self.name}: cannot invoke")
    
    def set_value(self, element, value):
        pass
    
    def get_text(self, element):
        return "mock text"
    
    def is_visible(self, element):
        return True
    
    def is_enabled(self, element):
        return True
    
    def get_attribute(self, element, attribute_name):
        return None
    
    def toggle(self, element, state=None):
        return True
    
    def wait_until_actionable(self, element, timeout=None, poll_interval=0.5):
        return True
    
    def capture_screenshot(self, element=None):
        return b""
    
    def close(self):
        pass
    
    def double_click(self, element):
        pass
    
    def right_click(self, element):
        pass
    
    def press_keys(self, element, keys):
        pass
    
    def drag_drop(self, element, target_element):
        pass
    
    def hover(self, element):
        pass
    
    def scroll(self, element, direction):
        pass
    
    def get_data_grid_content_ocr(self, element):
        return ""


class TestStrategyExecutorHealing:
    """Test that StrategyExecutor records healing metadata on fallback."""
    
    @pytest.fixture
    def temp_metadata_dir(self):
        temp_dir = tempfile.mkdtemp()
        yield temp_dir
        shutil.rmtree(temp_dir, ignore_errors=True)
    
    @pytest.fixture
    def healing_store(self, temp_metadata_dir):
        return HealingMetadataStore(metadata_dir=temp_metadata_dir)
    
    @pytest.fixture
    def strategy_provider(self):
        """Strategy provider: FlaUI fails, WPFSpy succeeds."""
        def provider(alias, app_id=None):
            return {
                "FlaUI": [
                    {"searchBy": "AutomationId", "value": "btnPrimary", "priority": 1}
                ],
                "WPFSpy": [
                    {"searchBy": "AutomationId", "value": "btnFallback", "priority": 1}
                ]
            }
        return provider
    
    @pytest.fixture
    def driver_provider(self):
        """Driver provider: FlaUI always fails, WPFSpy always succeeds."""
        flaui = MockDriver(name="FlaUI", fail_find=True)
        wpfspy = MockDriver(name="WPFSpy", fail_find=False)
        
        def provider(driver_name, app_id=None):
            if driver_name == "FlaUI":
                return flaui
            elif driver_name == "WPFSpy":
                return wpfspy
            return None
        return provider, flaui, wpfspy
    
    def test_healing_recorded_on_fallback(self, healing_store, strategy_provider, driver_provider):
        """When FlaUI fails and WPFSpy succeeds, healing metadata should record the fallback."""
        provider, flaui, wpfspy = driver_provider
        
        executor = StrategyExecutor(
            driver_provider=provider,
            strategy_provider=strategy_provider,
            healing_tracker=healing_store,
        )
        
        # Execute: FlaUI fails, WPFSpy succeeds
        result = executor.execute(
            alias="TestPage.btnSubmit",
            action_name="invoke",
            app_id="test_app",
        )
        
        # execute() returns result.result (the action's return value), not ExecutionResult
        # Verify healing metadata was recorded instead
        metadata = healing_store._metadata.get("TestPage.btnSubmit")
        assert metadata is not None, "Healing metadata should be recorded for the element"
        assert len(metadata.healing_history) > 0, "Healing history should have at least one entry"
        
        healing_attempt = metadata.healing_history[0]
        assert healing_attempt.primary_driver == "FlaUI"
        assert healing_attempt.primary_search_method == "AutomationId"
        assert healing_attempt.primary_search_value == "btnPrimary"
        assert healing_attempt.healing_driver == "WPFSpy"
        assert healing_attempt.healing_search_method == "AutomationId"
        assert healing_attempt.healing_search_value == "btnFallback"
        assert healing_attempt.healing_successful is True
    
    def test_no_healing_when_primary_succeeds(self, healing_store, driver_provider):
        """When primary strategy succeeds on first try, no healing should be recorded."""
        provider, flaui, wpfspy = driver_provider
        
        # Make FlaUI succeed instead of fail
        flaui.fail_find = False
        
        def strategy_provider(alias, app_id=None):
            return {
                "FlaUI": [
                    {"searchBy": "AutomationId", "value": "btnPrimary", "priority": 1}
                ],
                "WPFSpy": [
                    {"searchBy": "AutomationId", "value": "btnFallback", "priority": 1}
                ]
            }
        
        executor = StrategyExecutor(
            driver_provider=provider,
            strategy_provider=strategy_provider,
            healing_tracker=healing_store,
        )
        
        result = executor.execute(
            alias="TestPage.btnSubmit",
            action_name="invoke",
            app_id="test_app",
        )
        
        # execute() returns None on success (invoke returns None)
        # Verify no healing was recorded since primary succeeded
        metadata = healing_store._metadata.get("TestPage.btnSubmit")
        if metadata is not None:
            assert len(metadata.healing_history) == 0, "No healing should be recorded when primary succeeds"
    
    def test_baseline_captured_on_first_success(self, healing_store, driver_provider):
        """First successful execution should capture a baseline."""
        provider, flaui, wpfspy = driver_provider
        
        # Make FlaUI succeed
        flaui.fail_find = False
        
        def strategy_provider(alias, app_id=None):
            return {
                "FlaUI": [
                    {"searchBy": "AutomationId", "value": "btnPrimary", "priority": 1}
                ]
            }
        
        executor = StrategyExecutor(
            driver_provider=provider,
            strategy_provider=strategy_provider,
            healing_tracker=healing_store,
        )
        
        executor.execute(
            alias="TestPage.btnPrimary",
            action_name="invoke",
            app_id="test_app",
        )
        
        # execute() returns None on success (invoke returns None)
        # Verify baseline was captured instead
        metadata = healing_store._metadata.get("TestPage.btnPrimary")
        assert metadata is not None, "Baseline metadata should be captured"
        assert metadata.baseline is not None, "Baseline should be set"
        assert metadata.baseline.driver_used == "FlaUI"
        assert metadata.baseline.search_method == "AutomationId"


if __name__ == "__main__":
    import pytest
    pytest.main([__file__, "-v"])
