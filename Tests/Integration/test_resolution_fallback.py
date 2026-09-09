"""
Integration Tests for _resolve_and_execute
===========================================
Tests the core resolution and fallback logic with mocked drivers.
"""

import sys
import os
import pytest
from unittest.mock import Mock, MagicMock, patch
from typing import List, Dict, Any

# Add api directory to path
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "TestAutoLayer", "api"))

from base_driver import BaseDriver, ElementHandle
from exceptions import AllStrategiesFailedError, CircuitBreakerOpenError, WaitTimeoutError
from circuit_breaker import CircuitBreakerManager, CircuitState


@pytest.fixture(autouse=True)
def reset_circuit_breakers():
    """Reset circuit breakers before each test."""
    CircuitBreakerManager().reset_all()
    yield
    CircuitBreakerManager().reset_all()


class MockDriver(BaseDriver):
    """Mock driver for testing."""
    
    def __init__(self, name: str, should_succeed: bool = True, fail_count: int = 0):
        self._name = name
        self._should_succeed = should_succeed
        self._fail_count = fail_count
        self._call_count = 0
    
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


class TestResolveAndExecute:
    """Test the _resolve_and_execute method with various scenarios."""
    
    @pytest.fixture
    def mock_repo(self):
        """Mock repository access."""
        with patch('repository_access.get_all_driver_strategies_sorted') as mock:
            yield mock
    
    @pytest.fixture
    def mock_healing_store_class(self):
        """Mock healing metadata store class."""
        store = Mock()
        # Patch at the module where it's used
        with patch('DriverAgnosticApi.get_healing_store', return_value=store):
            with patch('healing_metadata_store.get_healing_store', return_value=store):
                with patch('healing_metadata_store.HealingMetadataStore', return_value=store):
                    with patch('healing_metadata_store._global_store', store):
                        yield store
    
    @pytest.fixture
    def mock_screenshot_manager(self):
        """Mock screenshot manager."""
        with patch('screenshot_manager.get_screenshot_manager') as mock:
            mgr = Mock()
            mock.return_value = mgr
            yield mgr
    
    @pytest.fixture
    def mock_get_drivers(self):
        """Mock the global _get_drivers function."""
        with patch('DriverAgnosticApi._get_drivers') as mock:
            yield mock
    
    @pytest.fixture
    def mock_wpfspy_mode(self):
        """Set WPFSPY_MODE to mock to avoid real driver issues."""
        with patch.dict(os.environ, {"WPFSPY_MODE": "mock"}):
            yield
    
    def test_single_driver_success(self, mock_repo, mock_healing_store_class, mock_screenshot_manager, mock_get_drivers, mock_wpfspy_mode):
        """Test successful resolution with first driver."""
        from DriverAgnosticApi import DriverAgnosticApi
        
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}]
        }
        
        api = DriverAgnosticApi()
        
        # Mock the driver pool
        mock_driver = MockDriver("FlaUI")
        mock_get_drivers.return_value = {"FlaUI": mock_driver}
        
        # Should succeed on first driver
        result = api._resolve_and_execute("test.alias", "invoke")
        assert result is None  # invoke returns None
        assert mock_driver._call_count == 1
    
    def test_driver_fallback_on_failure(self, mock_repo, mock_healing_store_class, mock_screenshot_manager, mock_get_drivers, mock_wpfspy_mode):
        """Test fallback to second driver when first fails."""
        from DriverAgnosticApi import DriverAgnosticApi
        
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}],
            "WPFSpy": [{"searchBy": "Name", "value": "Button1", "priority": 1}]
        }
        
        api = DriverAgnosticApi()
        
        # First driver fails, second succeeds
        flaui_driver = MockDriver("FlaUI", should_succeed=False)
        wpfspy_driver = MockDriver("WPFSpy", should_succeed=True)
        mock_get_drivers.return_value = {"FlaUI": flaui_driver, "WPFSpy": wpfspy_driver}
        
        result = api._resolve_and_execute("test.alias", "invoke")
        assert result is None
        assert flaui_driver._call_count == 1
        assert wpfspy_driver._call_count == 1
    
    def test_all_strategies_fail_raises_error(self, mock_repo, mock_healing_store_class, mock_screenshot_manager, mock_get_drivers, mock_wpfspy_mode):
        """Test AllStrategiesFailedError when all drivers fail."""
        from DriverAgnosticApi import DriverAgnosticApi
        
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}],
            "WPFSpy": [{"searchBy": "Name", "value": "Button1", "priority": 1}]
        }
        
        api = DriverAgnosticApi()
        
        # Both drivers fail
        flaui_driver = MockDriver("FlaUI", should_succeed=False)
        wpfspy_driver = MockDriver("WPFSpy", should_succeed=False)
        mock_get_drivers.return_value = {"FlaUI": flaui_driver, "WPFSpy": wpfspy_driver}
        
        with pytest.raises(AllStrategiesFailedError) as exc_info:
            api._resolve_and_execute("test.alias", "invoke")
        
        assert exc_info.value.alias == "test.alias"
        assert len(exc_info.value.attempts) == 2
    
    def test_circuit_breaker_opens_after_threshold(self, mock_repo, mock_healing_store_class, mock_screenshot_manager, mock_get_drivers, mock_wpfspy_mode):
        """Test circuit breaker opens after threshold failures."""
        from DriverAgnosticApi import DriverAgnosticApi
        
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}]
        }
        
        api = DriverAgnosticApi()
        
        # Driver that fails 3 times (threshold)
        flaui_driver = MockDriver("FlaUI", should_succeed=False)
        mock_get_drivers.return_value = {"FlaUI": flaui_driver}
        
        # First 3 calls should fail and increment breaker
        for i in range(3):
            with pytest.raises(AllStrategiesFailedError):
                api._resolve_and_execute("test.alias", "invoke")
        
        # 4th call should be rejected by circuit breaker
        breaker = CircuitBreakerManager().get_breaker("FlaUI")
        assert not breaker.allow_request()
    
    def test_healing_metadata_recorded_on_fallback(self, mock_repo, mock_healing_store_class, mock_screenshot_manager, mock_get_drivers, mock_wpfspy_mode):
        """Test healing metadata is recorded when fallback succeeds."""
        from DriverAgnosticApi import DriverAgnosticApi
        
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}],
            "WPFSpy": [{"searchBy": "Name", "value": "Button1", "priority": 1}]
        }
        
        api = DriverAgnosticApi()
        
        flaui_driver = MockDriver("FlaUI", should_succeed=False)
        wpfspy_driver = MockDriver("WPFSpy", should_succeed=True)
        mock_get_drivers.return_value = {"FlaUI": flaui_driver, "WPFSpy": wpfspy_driver}
        
        api._resolve_and_execute("test.alias", "invoke")
        
        # Verify healing store recorded the healing
        mock_healing_store_class.record_healing.assert_called_once()
        call_args = mock_healing_store_class.record_healing.call_args[1]
        assert call_args["primary_driver"] == "FlaUI"
        assert call_args["healing_driver"] == "WPFSpy"
        assert call_args["healing_successful"] is True
    
    def test_baseline_captured_on_first_success(self, mock_repo, mock_healing_store_class, mock_screenshot_manager, mock_get_drivers, mock_wpfspy_mode):
        """Test baseline is captured on first successful interaction."""
        from DriverAgnosticApi import DriverAgnosticApi
        
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}]
        }
        
        api = DriverAgnosticApi()
        mock_driver = MockDriver("FlaUI", should_succeed=True)
        mock_get_drivers.return_value = {"FlaUI": mock_driver}
        
        api._resolve_and_execute("test.alias", "invoke")
        
        # Verify baseline captured
        mock_healing_store_class.capture_baseline.assert_called_once()
        call_args = mock_healing_store_class.capture_baseline.call_args[1]
        assert call_args["alias"] == "test.alias"
        assert call_args["driver"] == "FlaUI"
    
    def test_strategy_priority_order(self, mock_repo, mock_healing_store_class, mock_screenshot_manager, mock_get_drivers, mock_wpfspy_mode):
        """Test strategies are tried in priority order."""
        from DriverAgnosticApi import DriverAgnosticApi
        
        mock_repo.return_value = {
            "FlaUI": [
                {"searchBy": "XPath", "value": "//xpath", "priority": 3},
                {"searchBy": "AutomationId", "value": "btn1", "priority": 1},
                {"searchBy": "Name", "value": "Button1", "priority": 2}
            ]
        }
        
        api = DriverAgnosticApi()
        mock_driver = MockDriver("FlaUI", should_succeed=True)
        mock_get_drivers.return_value = {"FlaUI": mock_driver}
        
        api._resolve_and_execute("test.alias", "invoke")
        
        # Should only call once (first priority succeeds)
        assert mock_driver._call_count == 1
    
    def test_multi_app_context_driver_selection(self, mock_repo, mock_healing_store_class, mock_screenshot_manager, mock_get_drivers, mock_wpfspy_mode):
        """Test driver selection respects app context driver list."""
        from DriverAgnosticApi import DriverAgnosticApi, _MULTI_APP_CONTEXT
        from app_context import AppContext
        
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
        _MULTI_APP_CONTEXT.apps.clear()
        _MULTI_APP_CONTEXT.register_app(app_ctx)
        
        api = DriverAgnosticApi(default_app_id="test_app")
        
        flaui_driver = MockDriver("FlaUI", should_succeed=True)
        wpfspy_driver = MockDriver("WPFSpy", should_succeed=True)
        app_ctx.drivers = {"FlaUI": flaui_driver, "WPFSpy": wpfspy_driver}
        
        # Should only try WPFSpy (per app context)
        api._resolve_and_execute("test.alias", "invoke", app_id="test_app")
        
        assert wpfspy_driver._call_count == 1
        assert flaui_driver._call_count == 0
        
        # Cleanup
        _MULTI_APP_CONTEXT.apps.clear()


class TestTryAllStrategies:
    """Test the _try_all_strategies helper method."""
    
    @pytest.fixture
    def mock_repo(self):
        with patch('repository_access.get_all_driver_strategies_sorted') as mock:
            yield mock
    
    @pytest.fixture
    def mock_get_drivers(self):
        """Mock the global _get_drivers function."""
        with patch('DriverAgnosticApi._get_drivers') as mock:
            yield mock
    
    @pytest.fixture
    def mock_wpfspy_mode(self):
        """Set WPFSPY_MODE to mock to avoid real driver issues."""
        with patch.dict(os.environ, {"WPFSPY_MODE": "mock"}):
            yield
    
    def test_find_elements_returns_all_matches(self, mock_repo, mock_get_drivers, mock_wpfspy_mode):
        """Test find_elements collects all matching elements."""
        from DriverAgnosticApi import DriverAgnosticApi
        
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "item", "priority": 1}]
        }
        
        api = DriverAgnosticApi()
        
        # Driver returns multiple elements
        mock_driver = Mock()
        mock_driver.find_elements.return_value = [
            ElementHandle(locator={}, driver_name="FlaUI"),
            ElementHandle(locator={}, driver_name="FlaUI"),
            ElementHandle(locator={}, driver_name="FlaUI"),
        ]
        mock_get_drivers.return_value = {"FlaUI": mock_driver}
        
        elements = api.find_elements("test.alias")
        assert len(elements) == 3
    
    def test_is_element_visible_retries(self, mock_repo, mock_get_drivers, mock_wpfspy_mode):
        """Test is_element_visible retries on failure."""
        from DriverAgnosticApi import DriverAgnosticApi
        
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}]
        }
        
        api = DriverAgnosticApi()
        
        # Driver find_element succeeds, is_visible fails twice then succeeds
        mock_driver = MockDriver("FlaUI", should_succeed=True)
        mock_driver.is_visible = Mock(side_effect=[False, False, True])
        mock_get_drivers.return_value = {"FlaUI": mock_driver}
        
        result = api.is_element_visible("test.alias")
        assert result is True
        assert mock_driver.is_visible.call_count == 3


class TestWaitKeywords:
    """Test wait_until_* keywords."""
    
    @pytest.fixture
    def mock_repo(self):
        with patch('repository_access.get_all_driver_strategies_sorted') as mock:
            yield mock
    
    @pytest.fixture
    def mock_get_drivers(self):
        """Mock the global _get_drivers function."""
        with patch('DriverAgnosticApi._get_drivers') as mock:
            yield mock
    
    @pytest.fixture
    def mock_wpfspy_mode(self):
        """Set WPFSPY_MODE to mock to avoid real driver issues."""
        with patch.dict(os.environ, {"WPFSPY_MODE": "mock"}):
            yield
    
    def test_wait_until_element_exists_timeout(self, mock_repo, mock_get_drivers, mock_wpfspy_mode):
        """Test WaitTimeoutError raised when element never appears."""
        from DriverAgnosticApi import DriverAgnosticApi
        from exceptions import WaitTimeoutError
        
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}]
        }
        
        api = DriverAgnosticApi()
        mock_driver = MockDriver("FlaUI", should_succeed=False)
        mock_get_drivers.return_value = {"FlaUI": mock_driver}
        
        with pytest.raises(WaitTimeoutError) as exc_info:
            api.wait_until_element_exists("test.alias", timeout=0.5, poll_interval=0.1)
        
        assert "element exists" in str(exc_info.value)
    
    def test_wait_until_element_actionable(self, mock_repo, mock_get_drivers, mock_wpfspy_mode):
        """Test wait_until_element_actionable succeeds when element becomes actionable."""
        from DriverAgnosticApi import DriverAgnosticApi
        
        mock_repo.return_value = {
            "FlaUI": [{"searchBy": "AutomationId", "value": "btn1", "priority": 1}]
        }
        
        api = DriverAgnosticApi()
        
        # Element becomes actionable after 2 polls
        mock_driver = MockDriver("FlaUI", should_succeed=True)
        mock_driver.is_actionable = Mock(side_effect=[False, False, True])
        mock_get_drivers.return_value = {"FlaUI": mock_driver}
        
        result = api.wait_until_element_actionable("test.alias", timeout=1.0, poll_interval=0.1)
        assert result is True
        assert mock_driver.is_actionable.call_count == 3


if __name__ == "__main__":
    pytest.main([__file__, "-v"])