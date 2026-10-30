"""
Integration Tests for _resolve_and_execute
===========================================
Tests the core resolution and fallback logic with mocked drivers.
"""

import sys
import os
import pytest
from unittest.mock import Mock, MagicMock, patch
from typing import List, Dict, Any, Optional

# Add api directory to path
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "TestAutoLayer", "api"))

from TestAutoLayer.api.base_driver import BaseDriver, ElementHandle
from TestAutoLayer.api.exceptions import AllStrategiesFailedError, CircuitBreakerOpenError, WaitTimeoutError
from TestAutoLayer.api.circuit_breaker import CircuitBreakerManager, CircuitState
from TestAutoLayer.api.resolution.strategy_executor import StrategyExecutor
from TestAutoLayer.api.resolution.healing_tracker import HealingTracker
from TestAutoLayer.api.resolution.element_resolver import ElementResolver
import TestAutoLayer.api.repository_access as repo


@pytest.fixture(autouse=True)
def reset_circuit_breakers():
    """Reset circuit breakers before and after each test."""
    CircuitBreakerManager().reset_all()
    yield
    CircuitBreakerManager().reset_all()


@pytest.fixture(autouse=True)
def reset_healing_tracker():
    """Reset healing tracker store."""
    from healing_metadata_store import get_healing_store
    store = get_healing_store()
    store.clear_metadata()
    yield
    store.clear_metadata()


class MockDriver(BaseDriver):
    """Mock driver for testing."""
    
    def __init__(self, name: str, should_succeed: bool = True, fail_count: int = 0):
        self._name = name
        self._should_succeed = should_succeed
        self._fail_count = fail_count
        self._call_count = 0
        self.last_match_score = None
    
    @property
    def name(self) -> str:
        return self._name
    
    def find_element(self, locator: Dict[str, Any]) -> ElementHandle:
        self._call_count += 1
        # Fail if we're within fail_count OR if should_succeed is False and we haven't failed yet
        if self._call_count <= self._fail_count:
            from exceptions import ElementNotFoundError
            raise ElementNotFoundError(f"{self._name} failed attempt {self._call_count}")
        if not self._should_succeed:
            from exceptions import ElementNotFoundError
            raise ElementNotFoundError(f"{self._name} should not succeed")
        return ElementHandle(locator=locator, driver_name=self._name)
    
    def find_elements(self, locator: Dict[str, Any]) -> List[ElementHandle]:
        return [self.find_element(locator)]
    
    def invoke(self, element: ElementHandle) -> None:
        pass
    
    def set_value(self, element: ElementHandle, value: str) -> None:
        pass
    
    def get_text(self, element: ElementHandle) -> str:
        return "mock text"
    
    def is_visible(self, element: ElementHandle) -> bool:
        return True
    
    def is_enabled(self, element: ElementHandle) -> bool:
        return True
    
    def get_attribute(self, element: ElementHandle, attribute_name: str) -> str:
        return "mock"
    
    def toggle(self, element: ElementHandle, state: bool = None) -> bool:
        return True
    
    def double_click(self, element: ElementHandle) -> None:
        pass
    
    def right_click(self, element: ElementHandle) -> None:
        pass
    
    def press_keys(self, element: ElementHandle, keys: str) -> None:
        pass
    
    def drag_drop(self, element: ElementHandle, target_element: ElementHandle) -> None:
        pass
    
    def hover(self, element: ElementHandle) -> None:
        pass
    
    def scroll(self, element: ElementHandle, direction: str) -> None:
        pass
    
    def get_data_grid_content_ocr(self, element: ElementHandle) -> str:
        return "col1,col2\nval1,val2"
    
    def capture_screenshot(self) -> bytes:
        return b"mock screenshot"


def create_test_executor(
    driver_map: Dict[str, Any],
    strategy_map: Dict[str, List[Dict]],
    app_id: Optional[str] = None
) -> StrategyExecutor:
    """Create a StrategyExecutor with mocked providers."""
    
    def driver_provider(driver_name: str, app_id: Optional[str]) -> Any:
        return driver_map.get(driver_name)
    
    def strategy_provider(alias: str, app_id: Optional[str]) -> Dict[str, List[Dict]]:
        return strategy_map
    
    # Create a fresh healing tracker with a mock store
    healing_tracker = HealingTracker()
    mock_store = Mock()
    healing_tracker.set_store(mock_store)
    
    executor = StrategyExecutor(
        driver_provider=driver_provider,
        strategy_provider=strategy_provider,
        healing_tracker=healing_tracker,
    )
    return executor


class TestResolveAndExecute:
    """Test the _resolve_and_execute method with various scenarios."""
    
    @pytest.fixture
    def mock_repo(self):
        """Mock repository access."""
        with patch('repository_access.get_all_driver_strategies_sorted') as mock:
            yield mock
    
    def test_single_driver_success(self, mock_repo):
        """Test successful resolution with first driver."""
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}]
        }
        
        mock_driver = MockDriver("FlaUI")
        executor = create_test_executor(
            driver_map={"FlaUI": mock_driver},
            strategy_map={"FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}]}
        )
        
        result = executor.execute("test.alias", "invoke")
        assert result is None
        assert mock_driver._call_count == 1
    
    def test_driver_fallback_on_failure(self, mock_repo):
        """Test fallback to second driver when first fails."""
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}],
            "WPFSpy": [{"searchBy": "Name", "value": "Button1", "priority": 1}]
        }
        
        flaui_driver = MockDriver("FlaUI", should_succeed=False)
        wpfspy_driver = MockDriver("WPFSpy", should_succeed=True)
        executor = create_test_executor(
            driver_map={"FlaUI": flaui_driver, "WPFSpy": wpfspy_driver},
            strategy_map={
                "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}],
                "WPFSpy": [{"searchBy": "Name", "value": "Button1", "priority": 1}]
            }
        )
        
        result = executor.execute("test.alias", "invoke")
        assert result is None
        assert flaui_driver._call_count == 1
        assert wpfspy_driver._call_count == 1
    
    def test_all_strategies_fail_raises_error(self, mock_repo):
        """Test AllStrategiesFailedError when all drivers fail."""
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}],
            "WPFSpy": [{"searchBy": "Name", "value": "Button1", "priority": 1}]
        }
        
        flaui_driver = MockDriver("FlaUI", should_succeed=False)
        wpfspy_driver = MockDriver("WPFSpy", should_succeed=False)
        executor = create_test_executor(
            driver_map={"FlaUI": flaui_driver, "WPFSpy": wpfspy_driver},
            strategy_map={
                "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}],
                "WPFSpy": [{"searchBy": "Name", "value": "Button1", "priority": 1}]
            }
        )
        
        with pytest.raises(AllStrategiesFailedError) as exc_info:
            executor.execute("test.alias", "invoke")
        
        assert exc_info.value.alias == "test.alias"
        assert len(exc_info.value.attempts) == 2
    
    def test_circuit_breaker_opens_after_threshold(self, mock_repo):
        """Test circuit breaker opens after threshold failures."""
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}]
        }
        
        flaui_driver = MockDriver("FlaUI", should_succeed=False)
        executor = create_test_executor(
            driver_map={"FlaUI": flaui_driver},
            strategy_map={"FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}]}
        )
        
        # First 3 calls should fail and increment breaker
        for i in range(3):
            with pytest.raises(AllStrategiesFailedError):
                executor.execute("test.alias", "invoke")
        
        # 4th call should be rejected by circuit breaker
        breaker = CircuitBreakerManager().get_breaker("FlaUI")
        assert not breaker.allow_request()
    
    def test_healing_metadata_recorded_on_fallback(self, mock_repo):
        """Test healing metadata is recorded when fallback succeeds."""
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}],
            "WPFSpy": [{"searchBy": "Name", "value": "Button1", "priority": 1}]
        }
        
        flaui_driver = MockDriver("FlaUI", should_succeed=False)
        wpfspy_driver = MockDriver("WPFSpy", should_succeed=True)
        executor = create_test_executor(
            driver_map={"FlaUI": flaui_driver, "WPFSpy": wpfspy_driver},
            strategy_map={
                "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}],
                "WPFSpy": [{"searchBy": "Name", "value": "Button1", "priority": 1}]
            }
        )
        
        executor.execute("test.alias", "invoke")
        
        # Verify healing store recorded the healing
        store = executor._healing_tracker._store
        store.record_healing.assert_called_once()
        call_args = store.record_healing.call_args
        assert call_args[1]["primary_driver"] == "FlaUI"
        assert call_args[1]["healing_driver"] == "WPFSpy"
        assert call_args[1]["healing_successful"] is True
    
    def test_baseline_captured_on_first_success(self, mock_repo):
        """Test baseline is captured on first successful interaction."""
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}]
        }
        
        mock_driver = MockDriver("FlaUI", should_succeed=True)
        executor = create_test_executor(
            driver_map={"FlaUI": mock_driver},
            strategy_map={"FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}]}
        )
        
        executor.execute("test.alias", "invoke")
        
        # Verify baseline captured
        store = executor._healing_tracker._store
        store.capture_baseline.assert_called_once()
        call_args = store.capture_baseline.call_args
        assert call_args[1]["alias"] == "test.alias"
        assert call_args[1]["driver"] == "FlaUI"
    
    def test_strategy_priority_order(self, mock_repo):
        """Test strategies are tried in priority order."""
        mock_repo.return_value = {
            "FlaUI": [
                {"searchBy": "XPath", "value": "//xpath", "priority": 3},
                {"searchBy": "AutomationId", "value": "btn1", "priority": 1},
                {"searchBy": "Name", "value": "Button1", "priority": 2}
            ]
        }
        
        mock_driver = MockDriver("FlaUI", should_succeed=True)
        executor = create_test_executor(
            driver_map={"FlaUI": mock_driver},
            strategy_map={"FlaUI": [
                {"searchBy": "XPath", "value": "//xpath", "priority": 3},
                {"searchBy": "AutomationId", "value": "btn1", "priority": 1},
                {"searchBy": "Name", "value": "Button1", "priority": 2}
            ]}
        )
        
        executor.execute("test.alias", "invoke")
        
        # Should only call once (first priority succeeds)
        assert mock_driver._call_count == 1
    
    def test_multi_app_context_driver_selection(self, mock_repo):
        """Test driver selection respects app context driver list."""
        from TestAutoLayer.api.app_management.app_registry import AppContext, get_multi_app_context
        
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}],
            "WPFSpy": [{"searchBy": "Name", "value": "Button1", "priority": 1}]
        }
        
        # Register app with only WPFSpy
        app_ctx = AppContext(
            app_id="test_app",
            app_name="TestApp",
            driver_list=["WPFSpy"],
            process_id=1234
        )
        get_multi_app_context().apps.clear()
        get_multi_app_context().register_app(app_ctx)
        
        flaui_driver = MockDriver("FlaUI", should_succeed=True)
        wpfspy_driver = MockDriver("WPFSpy", should_succeed=True)
        app_ctx.drivers = {"FlaUI": flaui_driver, "WPFSpy": wpfspy_driver}
        
        executor = create_test_executor(
            driver_map={"FlaUI": flaui_driver, "WPFSpy": wpfspy_driver},
            strategy_map={
                "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}],
                "WPFSpy": [{"searchBy": "Name", "value": "Button1", "priority": 1}]
            }
        )
        
        # Should only try WPFSpy (per app context)
        executor.execute("test.alias", "invoke", app_id="test_app")
        
        assert wpfspy_driver._call_count == 1
        assert flaui_driver._call_count == 0
        
        # Cleanup
        get_multi_app_context().apps.clear()


class TestTryAllStrategies:
    """Test _try_all_strategies method."""
    
    @pytest.fixture
    def mock_repo(self):
        with patch('repository_access.get_all_driver_strategies_sorted') as mock:
            yield mock
    
    def test_find_elements_returns_all_matches(self, mock_repo):
        """Test find_elements collects all matching elements."""
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "item", "priority": 1}]
        }
        
        mock_driver = MockDriver("FlaUI")
        mock_driver.find_elements = Mock(return_value=[
            ElementHandle(locator={}, driver_name="FlaUI"),
            ElementHandle(locator={}, driver_name="FlaUI"),
            ElementHandle(locator={}, driver_name="FlaUI"),
        ])
        
        executor = create_test_executor(
            driver_map={"FlaUI": mock_driver},
            strategy_map={"FlaUI": [{"searchBy": "AutomationId", "value": "item", "priority": 1}]}
        )
        
        elements = []
        def collect(driver, element):
            elements.append(element)
            return False
        
        executor.try_all_strategies("test.alias", None, collect, find_all=True)
        assert len(elements) == 3
    
    def test_is_element_visible_returns_false_when_not_visible(self, mock_repo):
        """Test try_all_strategies returns False when predicate returns False."""
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}]
        }
        
        mock_driver = MockDriver("FlaUI", should_succeed=True)
        mock_driver.is_visible = Mock(return_value=False)
        
        executor = create_test_executor(
            driver_map={"FlaUI": mock_driver},
            strategy_map={"FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}]}
        )
        
        result = executor.try_all_strategies("test.alias", None, lambda d, e: d.is_visible(e))
        assert result is False
        
        # Test with predicate returning True
        mock_driver.is_visible = Mock(return_value=True)
        result = executor.try_all_strategies("test.alias", None, lambda d, e: d.is_visible(e))
        assert result is True


class TestWaitKeywords:
    """Test wait keywords."""
    
    @pytest.fixture
    def mock_repo(self):
        with patch('repository_access.get_all_driver_strategies_sorted') as mock:
            yield mock
    
    def test_wait_until_element_exists_timeout(self, mock_repo):
        """Test WaitTimeoutError raised when element never appears."""
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}]
        }
        
        mock_driver = MockDriver("FlaUI", should_succeed=False)
        executor = create_test_executor(
            driver_map={"FlaUI": mock_driver},
            strategy_map={"FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}]}
        )
        
        # Use wait keywords directly
        from keywords.wait_keywords import WaitKeywords
        wait_keywords = WaitKeywords(executor, lambda: None)
        
        with pytest.raises(WaitTimeoutError):
            wait_keywords.wait_until_element_exists("test.alias", timeout=0.5, poll_interval=0.1)
    
    def test_wait_until_element_actionable(self, mock_repo):
        """Test wait_until_element_actionable succeeds when element becomes actionable."""
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}]
        }
        
        mock_driver = MockDriver("FlaUI", should_succeed=True)
        mock_driver.is_actionable = Mock(side_effect=[False, False, True])
        
        executor = create_test_executor(
            driver_map={"FlaUI": mock_driver},
            strategy_map={"FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}]}
        )
        
        from keywords.wait_keywords import WaitKeywords
        wait_keywords = WaitKeywords(executor, lambda: None)
        
        result = wait_keywords.wait_until_element_actionable("test.alias", timeout=1.0, poll_interval=0.1)
        assert result is True