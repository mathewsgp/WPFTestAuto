"""
Driver-Agnostic API (Thin Facade)
==================================
Layer 3 — Driver-Agnostic API

The abstract keyword interface independent of the underlying automation
engine. This is the ONE place in the whole framework that knows about all
three drivers; everything above this layer is 100% driver-agnostic.

This is a thin facade (~200 lines) that delegates to:
- StrategyExecutor for resolution/execution
- ElementResolver for XPath resolution with caching
- HealingTracker for healing metadata
- Keyword groups for organized keyword surface
"""

import os
import sys
from typing import Optional, Union, List, Any

# Framework modules
from config import config
from exceptions import (
    AllStrategiesFailedError,
    ElementNotFoundError,
    ElementNotInteractableError,
    ElementNotVisibleError,
    ElementDisabledError,
    CircuitBreakerOpenError,
)
from circuit_breaker import CircuitBreakerManager
from logging_utils import get_api_logger
from base_driver import ElementHandle

import repository_access as repo

# Resolution services
from resolution import (
    StrategyExecutor,
    ElementResolver,
    HealingTracker,
    HealingTrackerConfig,
)

# App management
from app_management import get_multi_app_context

# Keyword groups
from keywords import (
    InteractionKeywords,
    VerificationKeywords,
    WaitKeywords,
    UtilityKeywords,
)

# Initialize logger
logger = get_api_logger()

# Global state (backward compatibility)
_THIS_DIR = os.path.dirname(os.path.abspath(__file__))

# Add driver directories to path for driver module imports
sys.path.insert(0, os.path.join(_THIS_DIR, "..", "drivers_rf", "flaui_robotframework"))
sys.path.insert(0, os.path.join(_THIS_DIR, "..", "drivers_rf", "wpfspy_robotframework"))
sys.path.insert(0, os.path.join(_THIS_DIR, "..", "drivers_rf", "sikuli_robotframework"))
sys.path.insert(0, os.path.join(_THIS_DIR, "..", "mock_wpf_app"))

# Driver imports (need path for relative imports within driver modules)
from flaui_driver import FlaUIDriver          # noqa: E402
from WPFSpyLibrary import WPFSpyDriver        # noqa: E402
from SikuliLibrary import SikuliDriver        # noqa: E402
from mock_app import (                        # noqa: E402
    ElementNotFoundError as MockElementNotFoundError,
    ElementNotInteractableError as MockElementNotInteractableError,
    reset_app,
)

def _kill_pid(pid: int, force: bool = False) -> bool:
    """Module-level alias for backward compatibility."""
    return DriverAgnosticApi._kill_pid(pid, force=force)


# Re-export app management globals for backward compatibility
# Use full package path to avoid double-import issues from sys.path manipulation above
from TestAutoLayer.api.app_management.app_registry import (
    _MULTI_APP_CONTEXT,
    MultiAppContext,
    AppContext,
    Constants,
    get_multi_app_context,
)

# Re-export healing store getter for backward compatibility
from healing_metadata_store import get_healing_store

_WPFSPY_MODE = os.environ.get("WPFSPY_MODE", "mock").lower()
_SAMPLE_WPF_APP_PROCESS = None

# Active driver override (None = use default DRIVER_ORDER)
_ACTIVE_DRIVER = None

# Active mode override (None = use WPFSPY_MODE env var)
_ACTIVE_MODE = None

# Run modes filter (None = use all drivers in DRIVER_ORDER)
_RUN_MODES = None

# Driver priority order for element identification (None = use DRIVER_ORDER)
_DRIVER_PRIORITY = None

# Global driver cache
_DRIVERS: dict = {}
_DRIVERS_INITIALIZED: bool = False

# Circuit breaker manager
_breaker_manager = CircuitBreakerManager(
    threshold=config.CIRCUIT_BREAKER_THRESHOLD,
    timeout=config.CIRCUIT_BREAKER_TIMEOUT
)


def _get_drivers() -> dict:
    """Lazy initialization of drivers. Returns cached drivers dict."""
    global _DRIVERS, _DRIVERS_INITIALIZED
    if not _DRIVERS_INITIALIZED:
        _DRIVERS = {}
        driver_factories = {
            "FlaUI": lambda: FlaUIDriver(),
            "WPFSpy": _create_wpfspy_driver,
            "Sikuli": lambda: SikuliDriver(),
        }
        for name, factory in driver_factories.items():
            try:
                _DRIVERS[name] = factory()
            except ImportError as e:
                logger.warning(f"Driver {name} not available: {e}")
        _DRIVERS_INITIALIZED = True
        logger.info("Drivers initialized", drivers=list(_DRIVERS.keys()))
    return _DRIVERS


def _create_wpfspy_driver():
    """Create the appropriate WPFSpy driver based on current mode."""
    effective_mode = _ACTIVE_MODE if _ACTIVE_MODE is not None else _WPFSPY_MODE
    if effective_mode == "real":
        from WPFSpyLibrary import WPFSpyRealDriver
        return WPFSpyRealDriver()
    else:
        from WPFSpyLibrary import WPFSpyMockDriver
        return WPFSpyMockDriver()


def _reload_drivers():
    """Reload drivers (useful for testing or config changes)."""
    global _DRIVERS, _DRIVERS_INITIALIZED
    for driver in _DRIVERS.values():
        if hasattr(driver, 'close'):
            driver.close()
    _DRIVERS = {}
    _DRIVERS_INITIALIZED = False


def _get_run_modes() -> list:
    """Get the enabled driver modes for test execution."""
    global _RUN_MODES, _DRIVER_PRIORITY
    
    if _RUN_MODES is not None:
        return _RUN_MODES
    
    run_modes_env = os.environ.get("WPFSPY_RUN_MODES", "").strip()
    if run_modes_env:
        _RUN_MODES = [d.strip() for d in run_modes_env.split(",") if d.strip()]
        return _RUN_MODES
    
    priority_env = os.environ.get("WPFSPY_DRIVER_PRIORITY", "").strip()
    if priority_env:
        _DRIVER_PRIORITY = [d.strip() for d in priority_env.split(",") if d.strip()]
        _RUN_MODES = _DRIVER_PRIORITY
        return _RUN_MODES
    
    _RUN_MODES = config.DRIVER_ORDER
    return _RUN_MODES


def set_active_driver(driver_name: Optional[str]):
    """Set the active driver override."""
    global _ACTIVE_DRIVER
    _ACTIVE_DRIVER = driver_name


def set_active_mode(mode: Optional[str]):
    """Set the active mode override."""
    global _ACTIVE_MODE
    _ACTIVE_MODE = mode


def set_run_modes(modes: Optional[list]):
    """Set the run modes filter."""
    global _RUN_MODES
    _RUN_MODES = modes


def set_driver_priority(priority: Optional[list]):
    """Set the driver priority order."""
    global _DRIVER_PRIORITY, _RUN_MODES
    _DRIVER_PRIORITY = priority
    _RUN_MODES = priority


def _reset_real_app():
    """Resets the real SampleWpfApp state.
    
    Kept for backward compatibility; new code should use
    Tests.helpers.sample_wpf_app_manager.reset_real_app().
    """
    from sample_wpf_app_manager import reset_real_app
    return reset_real_app(
        sample_app_process=_SAMPLE_WPF_APP_PROCESS,
        mode=_ACTIVE_MODE if _ACTIVE_MODE is not None else _WPFSPY_MODE,
        ide_mode=os.environ.get("WPFSPY_IDE_RUN") == "1",
    )


class DriverAgnosticApi:
    """Robot Framework library — Layer 3 keywords.
    
    Keyword names below map to Robot Framework keywords by replacing
    underscores with spaces and title-casing, e.g. `click_element` ->
    `Click Element`.
    """

    ROBOT_LIBRARY_SCOPE = "GLOBAL"

    @staticmethod
    def _kill_pid(pid: int, force: bool = False) -> bool:
        """Kill a single PID. Returns True on success.
        
        Static method for backward compatibility with tests.
        Delegates to app_management.app_terminator.kill_pid.
        """
        from TestAutoLayer.api.app_management.app_terminator import kill_pid
        return kill_pid(pid, force=force)
    
    def __init__(self, default_app_id: Optional[str] = None):
        # Initialize resolution services
        self._element_resolver = ElementResolver()
        self._healing_tracker = HealingTracker()
        
        # Initialize strategy executor with provider functions
        self._strategy_executor = StrategyExecutor(
            driver_provider=self._driver_provider,
            strategy_provider=self._strategy_provider,
            healing_tracker=self._healing_tracker,
            element_resolver=self._element_resolver,
        )
        
        # Initialize keyword groups
        self._interaction = InteractionKeywords(
            self._strategy_executor,
            self._element_resolver,
            self._get_default_app_id,
        )
        self._verification = VerificationKeywords(
            self._strategy_executor,
            self._element_resolver,
            self._get_default_app_id,
        )
        self._wait = WaitKeywords(
            self._strategy_executor,
            self._get_default_app_id,
        )
        self._utility = UtilityKeywords(
            self._strategy_executor,
            self._get_default_app_id,
            self._set_default_app_id,
        )
        
        # Wire up cross-references for verification keywords
        self._verification.is_element_visible = self._wait.is_element_visible
        self._verification.is_element_enabled = self._wait.is_element_enabled
        self._verification.find_elements = self._wait.find_elements
        
        # App context
        self._app_id = default_app_id
        if default_app_id:
            get_multi_app_context().set_default_app(default_app_id)
        
        # Auto-register app from IDE environment variables if present
        self._auto_register_from_env()
    
    def _auto_register_from_env(self):
        """Auto-register app context from WPFSPY_* environment variables."""
        app_id = os.environ.get("WPFSPY_APP_ID")
        if not app_id:
            return
        
        app_name = os.environ.get("WPFSPY_APP_NAME", app_id)
        pipe_name = os.environ.get("WPFSPY_PIPE_NAME")
        process_id_str = os.environ.get("WPFSPY_PROCESS_ID")
        
        process_id = None
        if process_id_str:
            try:
                process_id = int(process_id_str)
            except (ValueError, TypeError):
                pass
        
        if app_id not in get_multi_app_context().apps:
            from app_management.app_registry import AppContext
            app_context = AppContext(
                app_id=app_id,
                app_name=app_name,
                driver_list=["FlaUI"],
                process_id=process_id,
                pipe_name=pipe_name,
            )
            get_multi_app_context().register_app(app_context)
            logger.info("Auto-registered app from IDE env vars", app_id=app_id, process_id=process_id)
    
    def _get_default_app_id(self) -> Optional[str]:
        """Get the default app ID."""
        return self._app_id or get_multi_app_context().default_app_id
    
    def _set_default_app_id(self, app_id: str):
        """Set the default app ID."""
        self._app_id = app_id
    
    def _driver_provider(self, driver_name: str, app_id: Optional[str]) -> Any:
        """Provider function for driver instances."""
        if app_id is not None or get_multi_app_context().apps:
            # Multi-app mode: get driver from app context
            try:
                app_context = get_multi_app_context().get_app(app_id)
                return app_context.get_driver(driver_name)
            except ValueError:
                return None
        else:
            # Legacy mode: use global driver pool
            return _get_drivers().get(driver_name)
    
    def _strategy_provider(self, alias: str, app_id: Optional[str]) -> dict:
        """Provider function for element strategies."""
        # Get element_repo_path from app context if available
        element_repo_path = None
        if app_id:
            try:
                app_ctx = get_multi_app_context().get_app(app_id)
                element_repo_path = app_ctx.element_repo_path
            except ValueError:
                pass
        return repo.get_all_driver_strategies_sorted(alias, app_id=app_id, element_repo_path=element_repo_path)
    
    # ------------------------------------------------------------------
    # Delegate to keyword groups
    # ------------------------------------------------------------------
    
    # Interaction keywords
    def click_element(self, alias: str, app_id: Optional[str] = None):
        self._interaction.click_element(alias, app_id)
    
    def click_element_with_wait(self, alias: str, timeout: float = 10.0, app_id: Optional[str] = None):
        self._interaction.click_element_with_wait(alias, timeout, app_id)
    
    def set_element_value(self, alias: str, value: str, app_id: Optional[str] = None):
        self._interaction.set_element_value(alias, value, app_id)
    
    def set_element_value_with_wait(
        self,
        alias: str,
        value: str,
        timeout: float = 10.0,
        app_id: Optional[str] = None,
    ):
        self._interaction.set_element_value_with_wait(alias, value, timeout, app_id)
    
    def get_element_text(self, alias: str, app_id: Optional[str] = None) -> str:
        return self._interaction.get_element_text(alias, app_id)
    
    def toggle_element(self, alias: str, app_id: Optional[str] = None):
        self._interaction.toggle_element(alias, app_id)
    
    def double_click_element(self, alias: str, app_id: Optional[str] = None):
        self._interaction.double_click_element(alias, app_id)
    
    def right_click_element(self, alias: str, app_id: Optional[str] = None):
        self._interaction.right_click_element(alias, app_id)
    
    def press_keys(self, alias: str, keys: str, app_id: Optional[str] = None):
        self._interaction.press_keys(alias, keys, app_id)
    
    def drag_and_drop(self, alias: str, target_alias: str, app_id: Optional[str] = None):
        self._interaction.drag_and_drop(alias, target_alias, app_id)
    
    def hover_over_element(self, alias: str, app_id: Optional[str] = None):
        self._interaction.hover_over_element(alias, app_id)
    
    def scroll(self, alias: str, direction: str, app_id: Optional[str] = None):
        self._interaction.scroll(alias, direction, app_id)
    
    def sikuli_click(self, alias: str, image_tag: str, app_id: Optional[str] = None):
        self._interaction.sikuli_click(alias, image_tag, app_id)
    
    def sikuli_type(self, alias: str, text: str, app_id: Optional[str] = None):
        self._interaction.sikuli_type(alias, text, app_id)
    
    # Verification keywords
    def verify_element_text(self, alias: str, expected: str, app_id: Optional[str] = None):
        self._verification.verify_element_text(alias, expected, app_id)
    
    def verify_element_contains_text(self, alias: str, expected: str, app_id: Optional[str] = None):
        self._verification.verify_element_contains_text(alias, expected, app_id)
    
    def verify_element_enabled(self, alias: str, app_id: Optional[str] = None):
        self._verification.verify_element_enabled(alias, app_id)
    
    def verify_element_visible(self, alias: str, app_id: Optional[str] = None):
        self._verification.verify_element_visible(alias, app_id)
    
    def verify_element_text_matches_regex(self, alias: str, pattern: str, app_id: Optional[str] = None):
        self._verification.verify_element_text_matches_regex(alias, pattern, app_id)
    
    def verify_element_attribute(self, alias: str, attribute_name: str, expected_value: str, app_id: Optional[str] = None):
        self._verification.verify_element_attribute(alias, attribute_name, expected_value, app_id)
    
    def property_checkpoint(self, alias: str, property_name: str, expected_value: str, app_id: Optional[str] = None):
        self._verification.property_checkpoint(alias, property_name, expected_value, app_id)
    
    def data_grid_checkpoint(self, alias: str, expected_content: str, app_id: Optional[str] = None):
        self._verification.data_grid_checkpoint(alias, expected_content, app_id)
    
    def count_checkpoint(self, alias: str, expected_count: str, app_id: Optional[str] = None):
        self._verification.count_checkpoint(alias, expected_count, app_id)
    
    def attribute_checkpoint(self, alias: str, attribute_name: str, expected_value: str, app_id: Optional[str] = None):
        self._verification.attribute_checkpoint(alias, attribute_name, expected_value, app_id)
    
    def area_checkpoint(self, alias: str, expected_text: str, app_id: Optional[str] = None):
        self._verification.area_checkpoint(alias, expected_text, app_id)
    
    def image_checkpoint(self, alias: str, baseline_path: str, app_id: Optional[str] = None):
        self._verification.image_checkpoint(alias, baseline_path, app_id)
    
    # Wait keywords
    def is_element_visible(self, alias: str, app_id: Optional[str] = None) -> bool:
        return self._wait.is_element_visible(alias, app_id)
    
    def is_element_enabled(self, alias: str, app_id: Optional[str] = None) -> bool:
        return self._wait.is_element_enabled(alias, app_id)
    
    def find_elements(self, alias: str, app_id: Optional[str] = None) -> List[Any]:
        return self._wait.find_elements(alias, app_id)
    
    def is_element_actionable(self, alias: str, app_id: Optional[str] = None) -> bool:
        return self._wait.is_element_actionable(alias, app_id)
    
    def wait_until_element_exists(
        self,
        alias: str,
        timeout: float = 10.0,
        poll_interval: float = 0.5,
        app_id: Optional[str] = None,
    ):
        self._wait.wait_until_element_exists(alias, timeout, poll_interval, app_id)
    
    def wait_until_element_visible(
        self,
        alias: str,
        timeout: float = 10.0,
        poll_interval: float = 0.5,
        app_id: Optional[str] = None,
    ):
        self._wait.wait_until_element_visible(alias, timeout, poll_interval, app_id)
    
    def wait_until_element_enabled(
        self,
        alias: str,
        timeout: float = 10.0,
        poll_interval: float = 0.5,
        app_id: Optional[str] = None,
    ):
        self._wait.wait_until_element_enabled(alias, timeout, poll_interval, app_id)
    
    def wait_until_element_actionable(
        self,
        alias: str,
        timeout: float = 10.0,
        poll_interval: float = 0.5,
        app_id: Optional[str] = None,
    ):
        self._wait.wait_until_element_actionable(alias, timeout, poll_interval, app_id)
    
    def wait_until_element_text_contains(
        self,
        alias: str,
        expected: str,
        timeout: float = 10.0,
        case_sensitive: bool = True,
        app_id: Optional[str] = None,
    ):
        self._wait.wait_until_element_text_contains(alias, expected, timeout, case_sensitive, app_id)
    
    # Utility keywords
    def set_driver(self, driver_name: str):
        self._utility.set_driver(driver_name)
    
    def reset_drivers(self):
        self._utility.reset_drivers()
    
    def set_mode(self, mode: str):
        self._utility.set_mode(mode)
    
    def reset_mode(self):
        self._utility.reset_mode()
    
    def set_mode_and_driver(self, mode: str, driver: str):
        self._utility.set_mode_and_driver(mode, driver)
    
    def get_data_grid_content_ocr(self, alias: str, app_id: Optional[str] = None) -> str:
        return self._utility.get_data_grid_content_ocr(alias, app_id)
    
    def capture_screenshot(self, filename: str = None, app_id: Optional[str] = None) -> str:
        return self._utility.capture_screenshot(filename, app_id)
    
    def set_clipboard_text(self, text: str):
        self._utility.set_clipboard_text(text)
    
    def get_clipboard_text(self) -> str:
        return self._utility.get_clipboard_text()
    
    def copy_element_text(self, alias: str, app_id: Optional[str] = None):
        self._utility.copy_element_text(alias, app_id)
    
    def paste_clipboard_to_element(self, alias: str, app_id: Optional[str] = None):
        self._utility.paste_clipboard_to_element(alias, app_id)
    
    def send_keys_to_window(self, window_title: str, keys: str):
        self._utility.send_keys_to_window(window_title, keys)
    
    def register_application(
        self,
        app_id: str,
        app_name: str,
        driver_list: Optional[Union[str, List[str]]] = None,
        process_id: Optional[int] = None,
        pipe_name: Optional[str] = None,
        app_path: Optional[str] = None,
        launch_args: Optional[List[str]] = None,
    ) -> str:
        return self._utility.register_application(
            app_id, app_name, driver_list, process_id, pipe_name, app_path, launch_args
        )
    
    def switch_application(self, app_id: str):
        self._utility.switch_application(app_id)
    
    def launch_application(
        self,
        app_path: str,
        app_id: Optional[str] = None,
        args: Optional[Union[str, List[str]]] = None,
        start_in: Optional[str] = None,
        drivers: Optional[Union[str, List[str]]] = None,
        attach: bool = False,
        timeout: float = 30.0,
    ) -> str:
        return self._utility.launch_application(
            app_path, app_id, args, start_in, drivers, attach, timeout
        )
    
    def attach_to_application(self, app_id: str, process_id: Union[int, str], driver_list: Optional[Union[str, List[str]]] = None, pipe_name: Optional[str] = None) -> str:
        return self._utility.attach_to_application(app_id, process_id, driver_list, pipe_name)
    
    def close_application(self, app_id: str):
        self._utility.close_application(app_id)
    
    def terminate_application(
        self,
        app_id: Optional[str] = None,
        window_title: Optional[str] = None,
        process_name: Optional[str] = None,
        force: bool = False,
    ) -> int:
        return self._utility.terminate_application(app_id, window_title, process_name, force)
    
    def get_application_list(self) -> List[str]:
        return self._utility.get_application_list()
    
    def set_default_application(self, app_id: str):
        self._utility.set_default_application(app_id)
    
    def get_current_application(self) -> str:
        return self._utility.get_current_application()
    
    def wait_for_application(self, app_id: str, timeout: float = 30.0, poll_interval: float = 1.0) -> bool:
        return self._utility.wait_for_application(app_id, timeout, poll_interval)
    
    def activate_window(self, app_id: Optional[str] = None, window_title: Optional[str] = None):
        self._utility.activate_window(app_id, window_title)
    
    def is_pipe_ready(self, pipe_name: str = "WPFSpyAgentPipe") -> bool:
        return self._utility.is_pipe_ready(pipe_name)
    
    def get_last_strategy_used(self) -> Optional[str]:
        return self._utility.get_last_strategy_used()
    
    def reset_application(self):
        self._utility.reset_application()
    
    # ------------------------------------------------------------------
    # Internal methods for backward compatibility
    # ------------------------------------------------------------------
    def _resolve_and_execute(self, alias: str, action_name: str, app_id: Optional[str] = None, *args):
        """Internal method for backward compatibility - delegates to executor."""
        # Determine driver order
        active_driver = _ACTIVE_DRIVER
        
        # Get element priority from repository
        element_priority = None
        try:
            element = repo.get_element(alias, app_id=app_id)
            element_priority = element.get("driverPriority")
            if element_priority and isinstance(element_priority, list):
                element_priority = [d for d in element_priority if d in self._strategy_provider(alias, app_id)]
        except Exception:
            pass
        
        return self._strategy_executor.execute(
            alias=alias,
            action_name=action_name,
            app_id=app_id,
            *args,
            active_driver=active_driver,
            element_priority=element_priority,
            driver_order_override=_get_run_modes(),
        )
    
    def _try_all_strategies(
        self,
        alias: str,
        app_id: Optional[str],
        predicate,
        find_all: bool = False,
    ) -> bool:
        return self._strategy_executor.try_all_strategies(alias, app_id, predicate, find_all)
    
    def _resolve_strategy_with_parent(self, strategy: dict, alias: str, app_id: Optional[str] = None, driver_name: Optional[str] = None) -> dict:
        return self._element_resolver.resolve_strategy_with_parent(strategy, alias, app_id, driver_name)
    
    def _build_full_path_from_alias(self, alias: str, app_id: Optional[str] = None, driver_name: Optional[str] = None) -> str:
        return self._element_resolver.build_full_path_from_alias(alias, app_id, driver_name)
    
    def _capture_element_properties(self, driver: Any, element: ElementHandle) -> dict:
        return self._healing_tracker.capture_element_properties(driver, element)