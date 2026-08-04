"""
Layer 3 — Driver-Agnostic API
==============================
The abstract keyword interface independent of the underlying automation
engine. Test Scripts (Layer 1) never call this directly; Reusable Test
Modules (Layer 2) do. This library resolves an alias to a locator + step
via the Element & Step Repositories, then dispatches to the currently
available driver wrapper (Layer 4) — trying each configured strategy in
order and falling back automatically if one fails ("runtime self-healing
locators" — see docs/architecture and the corresponding slide).

This is the ONE place in the whole framework that knows about all three
drivers; everything above this layer is 100% driver-agnostic.
"""

import sys
import os
import subprocess
import time
import signal
from typing import Optional, Tuple

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

# Import framework modules
from config import config, FrameworkConfig  # noqa: E402
from exceptions import (  # noqa: E402
    AllStrategiesFailedError,
    ElementNotFoundError,
    ElementNotInteractableError,
    ElementNotVisibleError,
    ElementDisabledError,
    CircuitBreakerOpenError,
)
from circuit_breaker import CircuitBreakerManager  # noqa: E402
from logging_utils import get_api_logger, execution_logger  # noqa: E402
from base_driver import ElementHandle  # noqa: E402

import repository_access as repo  # noqa: E402

_THIS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(_THIS_DIR, "..", "drivers_rf", "flaui_robotframework"))
sys.path.insert(0, os.path.join(_THIS_DIR, "..", "drivers_rf", "wpfspy_robotframework"))
sys.path.insert(0, os.path.join(_THIS_DIR, "..", "drivers_rf", "sikuli_robotframework"))
sys.path.insert(0, os.path.join(_THIS_DIR, "..", "drivers", "mock_wpf_app"))

from FlaUILibrary import FlaUIDriver          # noqa: E402
from WPFSpyLibrary import WPFSpyDriver        # noqa: E402
from SikuliLibrary import SikuliDriver        # noqa: E402
from mock_app import (                        # noqa: E402
    ElementNotFoundError as MockElementNotFoundError,
    ElementNotInteractableError as MockElementNotInteractableError,
    reset_app,
)

_WPFSPY_MODE = os.environ.get("WPFSPY_MODE", "mock").lower()
_SAMPLE_WPF_APP_PROCESS = None

# Initialize logger
logger = get_api_logger()

# Circuit breaker manager
_breaker_manager = CircuitBreakerManager(
    threshold=config.CIRCUIT_BREAKER_THRESHOLD,
    timeout=config.CIRCUIT_BREAKER_TIMEOUT
)

def _get_sample_wpf_app_path():
    """Returns the path to the SampleWpfApp executable."""
    base = os.path.join(_THIS_DIR, "..", "SampleWpfApp", "bin", "Debug", "net10.0-windows")
    dll = os.path.join(base, "SampleWpfApp.dll")
    if os.path.exists(dll):
        return dll
    exe = os.path.join(base, "SampleWpfApp.exe")
    if os.path.exists(exe):
        return exe
    raise FileNotFoundError(f"SampleWpfApp not found in {base}")

def _kill_sample_wpf_app():
    """Kills any running SampleWpfApp process by matching its window title,
    so we don't accidentally kill the IDE or other dotnet processes.
    """
    global _SAMPLE_WPF_APP_PROCESS
    proc = _SAMPLE_WPF_APP_PROCESS
    if proc is not None and proc.poll() is None:
        try:
            proc.kill()
            proc.wait(timeout=5)
        except Exception:
            pass
    _SAMPLE_WPF_APP_PROCESS = None

    try:
        subprocess.run(
            ["taskkill", "/F", "/FI", "WINDOWTITLE eq Sample WPF App*"],
            capture_output=True, timeout=10, check=False
        )
    except Exception:
        pass

def _start_sample_wpf_app():
    """Starts SampleWpfApp with the WPFSpy agent startup hook."""
    global _SAMPLE_WPF_APP_PROCESS
    
    app_path = _get_sample_wpf_app_path()
    startup_hook = os.path.join(os.path.dirname(app_path), "WpfSpyAgent.StartupHook.dll")
    
    env = os.environ.copy()
    env["WPFSPY_AGENT_ENABLED"] = "1"
    if os.path.exists(startup_hook):
        env["DOTNET_STARTUP_HOOKS"] = startup_hook
    
    _SAMPLE_WPF_APP_PROCESS = subprocess.Popen(
        ["dotnet", app_path],
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    time.sleep(5)  # Give the app time to start and the agent to initialize

def _is_sample_wpf_app_running():
    """Check if SampleWpfApp is already running by window title."""
    try:
        result = subprocess.run(
            ['tasklist', '/FI', 'WINDOWTITLE eq Sample WPF App*', '/FO', 'CSV', '/NH'],
            capture_output=True, text=True, timeout=5, check=False
        )
        return 'dotnet.exe' in result.stdout or 'SampleWpfApp.exe' in result.stdout
    except Exception:
        return False

def _reset_real_app():
    """Resets the real SampleWpfApp state.
    
    In IDE mode (WPFSPY_IDE_RUN=1): keeps the app running, uses ResetState.
    In CLI/CI mode: closes and reopens the app between tests for isolation.
    """
    ide_mode = os.environ.get('WPFSPY_IDE_RUN') == '1'
    print(f'[DriverAgnosticApi] _reset_real_app called, mode={_WPFSPY_MODE}, ide_mode={ide_mode}')
    
    if ide_mode:
        # IDE mode: keep app running, just reset state
        if _WPFSPY_MODE == 'real' and _SAMPLE_WPF_APP_PROCESS is not None and _SAMPLE_WPF_APP_PROCESS.poll() is None:
            try:
                driver = WPFSpyDriver()
                result = driver._send('ResetState')
                if not result.get('success'):
                    raise Exception(f"ResetState failed: {result.get('error')}")
                print('[DriverAgnosticApi] SampleWpfApp state reset via agent (IDE mode)')
                time.sleep(1)  # Wait for app to stabilize after reset
            except Exception as e:
                print(f'[DriverAgnosticApi] ResetState failed: {e}, app may need manual reset')
        else:
            # App not running in IDE mode, start it
            print('[DriverAgnosticApi] SampleWpfApp not running in IDE mode, starting fresh')
            _kill_sample_wpf_app()
            time.sleep(2)
            _start_sample_wpf_app()
    else:
        # CLI/CI mode: always close and reopen app between tests
        print('[DriverAgnosticApi] CLI mode: closing and reopening SampleWpfApp')
        _kill_sample_wpf_app()
        time.sleep(2)
        _start_sample_wpf_app()

# Lazy driver initialization
_DRIVERS: dict = {}
_DRIVERS_INITIALIZED: bool = False


def _get_drivers() -> dict:
    """Lazy initialization of drivers. Returns cached drivers dict."""
    global _DRIVERS, _DRIVERS_INITIALIZED
    if not _DRIVERS_INITIALIZED:
        _DRIVERS = {
            "FlaUI": FlaUIDriver(),
            "WPFSpy": WPFSpyDriver(),
            "Sikuli": SikuliDriver(),
        }
        _DRIVERS_INITIALIZED = True
        logger.info("Drivers initialized", drivers=list(_DRIVERS.keys()))
    return _DRIVERS


def _reload_drivers():
    """Reload drivers (useful for testing or config changes)."""
    global _DRIVERS, _DRIVERS_INITIALIZED
    for driver in _DRIVERS.values():
        if hasattr(driver, 'close'):
            driver.close()
    _DRIVERS = {}
    _DRIVERS_INITIALIZED = False


class DriverAgnosticApi:
    """Robot Framework library — Layer 3 keywords.

    Keyword names below map to Robot Framework keywords by replacing
    underscores with spaces and title-casing, e.g. `click_element` ->
    `Click Element`.
    """

    ROBOT_LIBRARY_SCOPE = "GLOBAL"

    def __init__(self):
        self.last_strategy_used: Optional[str] = None
        self.attempt_log: list = []
        self._drivers = _get_drivers()

    # ------------------------------------------------------------------
    # Core resolution + self-healing fallback
    # ------------------------------------------------------------------
    def _resolve_and_execute(self, alias: str, action_name: str, *args):
        """Resolve element and execute action with self-healing fallback.
        
        Tries each configured driver in order (FlaUI -> WPFSpy -> Sikuli),
        and for each driver, tries all strategies in priority order.
        Automatically falls back if one strategy or driver fails.
        
        Strategy Priority per Driver:
        1. AutomationId (most reliable)
        2. Name (second choice)
        3. Type + Index (sibling fallback)
        4. XPath (full path)
        5. Image (Sikuli fallback)
        
        Args:
            alias: Element alias from repository.
            action_name: Method name on driver (invoke, set_value, get_text, etc.).
            *args: Action-specific arguments.
        
        Returns:
            Driver-specific result.
        
        Raises:
            AllStrategiesFailedError: If all strategies across all drivers fail.
        """
        # Get all strategies for this element, sorted by priority
        all_strategies = repo.get_all_driver_strategies_sorted(alias)
        wpfspy_mode = os.environ.get("WPFSPY_MODE", "mock")
        
        logger.debug(
            "Resolving element",
            alias=alias,
            action=action_name,
            wpfspy_mode=wpfspy_mode,
            available_drivers=list(all_strategies.keys())
        )
        
        if not all_strategies:
            raise AllStrategiesFailedError(
                alias=alias,
                attempts=[],
                details={"reason": "No strategies configured"}
            )
        
        # Build ordered strategy list from config and available strategies
        driver_order = config.DRIVER_ORDER
        attempts = []
        
        for driver_name in driver_order:
            if driver_name not in all_strategies:
                # This driver is not configured for this element
                continue
            
            driver_strategies = all_strategies[driver_name]
            driver = self._drivers.get(driver_name)
            
            if driver is None:
                logger.warning(
                    f"Driver {driver_name} not available",
                    alias=alias,
                    driver=driver_name
                )
                continue
            
            # Check circuit breaker
            breaker = _breaker_manager.get_breaker(driver_name)
            if not breaker.allow_request():
                logger.warning(
                    f"Circuit breaker open for {driver_name}, skipping",
                    alias=alias,
                    driver=driver_name
                )
                attempts.append((f"{driver_name}:*", "CIRCUIT_OPEN"))
                continue
            
            # Try each strategy for this driver in priority order
            for strategy in driver_strategies:
                search_by = strategy.get("searchBy", "")
                strategy_value = strategy.get("value", "")
                priority = strategy.get("priority", 99)
                strategy_desc = f"{driver_name}:{search_by}"
                
                start_time = time.time()
                logger.debug(
                    f"Trying {strategy_desc}",
                    alias=alias,
                    driver=driver_name,
                    searchBy=search_by,
                    value=strategy_value,
                    priority=priority
                )
                
                try:
                    # Find element using this driver's strategy
                    element = driver.find_element(strategy)
                    
                    # Execute the action
                    result = getattr(driver, action_name)(element, *args)
                    
                    duration_ms = (time.time() - start_time) * 1000
                    
                    # Success
                    breaker.record_success()
                    self.last_strategy_used = strategy_desc
                    self.attempt_log = [(strategy_desc, "SUCCESS")]
                    
                    logger.info(
                        f"Element found via {strategy_desc}",
                        alias=alias,
                        driver=driver_name,
                        searchBy=search_by,
                        duration_ms=round(duration_ms, 2)
                    )
                    
                    return result
                    
                except Exception as e:
                    duration_ms = (time.time() - start_time) * 1000
                    error_msg = str(e)[:100]
                    attempts.append((strategy_desc, f"FAILED: {error_msg}"))
                    
                    logger.debug(
                        f"Strategy failed, trying next",
                        alias=alias,
                        driver=driver_name,
                        searchBy=search_by,
                        error=error_msg,
                        duration_ms=round(duration_ms, 2)
                    )
                    
                    # Record failure for circuit breaker
                    breaker.record_failure()
                    continue
            
            # All strategies for this driver failed
            logger.debug(
                f"All {driver_name} strategies failed, trying next driver",
                alias=alias,
                driver=driver_name
            )
        
        # All drivers and strategies failed
        error_details = {
            "attempts": attempts,
            "total_attempts": len(attempts),
            "driver_order": driver_order
        }
        
        logger.error(
            f"All strategies failed for {alias}",
            alias=alias,
            attempts=attempts
        )
        
        raise AllStrategiesFailedError(
            alias=alias,
            attempts=attempts,
            details=error_details
        )

    # ------------------------------------------------------------------
    # Public keywords
    # ------------------------------------------------------------------
    def click_element(self, alias: str):
        """Invokes (clicks) the element identified by `alias`."""
        self._resolve_and_execute(alias, "invoke")

    def click_element_with_wait(self, alias: str, timeout: float = 10.0):
        """Clicks element after waiting for it to be actionable."""
        self.wait_until_element_actionable(alias, timeout)
        self._resolve_and_execute(alias, "invoke")

    def set_element_value(self, alias: str, value: str):
        """Sets the text/value of the element identified by `alias`."""
        self._resolve_and_execute(alias, "set_value", value)

    def set_element_value_with_wait(
        self, 
        alias: str, 
        value: str, 
        timeout: float = 10.0
    ):
        """Sets element value after waiting for it to be actionable."""
        self.wait_until_element_actionable(alias, timeout)
        self._resolve_and_execute(alias, "set_value", value)

    def get_element_text(self, alias: str) -> str:
        """Returns the current text of the element identified by `alias`."""
        return self._resolve_and_execute(alias, "get_text")

    def verify_element_text(self, alias: str, expected: str):
        """Fails the test unless the element's text equals `expected`."""
        actual = self.get_element_text(alias)
        if actual != expected:
            raise AssertionError(f"'{alias}' text mismatch: expected '{expected}', got '{actual}'")

    def verify_element_contains_text(self, alias: str, expected: str):
        """Fails the test unless the element's text contains `expected`."""
        actual = self.get_element_text(alias)
        if expected not in actual:
            raise AssertionError(f"'{alias}' text does not contain '{expected}': got '{actual}'")

    def get_data_grid_content_ocr(self, alias: str) -> str:
        """Captures a DataGrid element screenshot and returns
        its content as CSV text using OCR."""
        return self._resolve_and_execute(alias, "get_data_grid_content_ocr")

    def toggle_element(self, alias: str):
        """Toggles a checkbox/toggle-style element identified by `alias`."""
        self._resolve_and_execute(alias, "toggle")

    def is_element_visible(self, alias: str) -> bool:
        """Check if element is visible without failing."""
        strategies = repo.get_strategies(alias)
        for driver_name in config.DRIVER_ORDER:
            if driver_name not in strategies:
                continue
            driver = self._drivers.get(driver_name)
            if driver is None:
                continue
            try:
                element = driver.find_element(strategies[driver_name])
                return driver.is_visible(element)
            except Exception:
                continue
        return False

    def is_element_enabled(self, alias: str) -> bool:
        """Check if element is enabled without failing."""
        strategies = repo.get_strategies(alias)
        for driver_name in config.DRIVER_ORDER:
            if driver_name not in strategies:
                continue
            driver = self._drivers.get(driver_name)
            if driver is None:
                continue
            try:
                element = driver.find_element(strategies[driver_name])
                return driver.is_enabled(element)
            except Exception:
                continue
        return False

    def is_element_actionable(self, alias: str) -> bool:
        """Check if element is both visible and enabled."""
        strategies = repo.get_strategies(alias)
        for driver_name in config.DRIVER_ORDER:
            if driver_name not in strategies:
                continue
            driver = self._drivers.get(driver_name)
            if driver is None:
                continue
            try:
                element = driver.find_element(strategies[driver_name])
                return driver.is_actionable(element)
            except Exception:
                continue
        return False

    def wait_until_element_visible(
        self, 
        alias: str, 
        timeout: float = 10.0,
        poll_interval: float = 0.5
    ):
        """Polls until the element is visible, or raises after `timeout` seconds.
        
        Uses exponential backoff on poll interval for better performance.
        """
        from wait_utils import Wait
        from exceptions import WaitTimeoutError
        
        start_time = time.time()
        strategies = repo.get_strategies(alias)
        
        if not strategies:
            raise AllStrategiesFailedError(
                alias=alias,
                attempts=[],
                details={"reason": "No strategies configured"}
            )
        
        while time.time() - start_time < timeout:
            for driver_name in config.DRIVER_ORDER:
                if driver_name not in strategies:
                    continue
                driver = self._drivers.get(driver_name)
                if driver is None:
                    continue
                try:
                    element = driver.find_element(strategies[driver_name])
                    if driver.is_visible(element):
                        return True
                except Exception:
                    continue
            
            # Exponential backoff
            remaining = timeout - (time.time() - start_time)
            if remaining > 0:
                time.sleep(min(poll_interval, remaining))
        
        raise WaitTimeoutError(
            condition="element visible",
            timeout=timeout
        )

    def wait_until_element_actionable(
        self,
        alias: str,
        timeout: float = 10.0,
        poll_interval: float = 0.5
    ):
        """Wait for element to be visible and enabled.
        
        Raises WaitTimeoutError if element is not actionable within timeout.
        """
        start_time = time.time()
        strategies = repo.get_strategies(alias)
        
        if not strategies:
            raise AllStrategiesFailedError(
                alias=alias,
                attempts=[],
                details={"reason": "No strategies configured"}
            )
        
        while time.time() - start_time < timeout:
            for driver_name in config.DRIVER_ORDER:
                if driver_name not in strategies:
                    continue
                driver = self._drivers.get(driver_name)
                if driver is None:
                    continue
                try:
                    element = driver.find_element(strategies[driver_name])
                    if driver.is_actionable(element):
                        return True
                except Exception:
                    continue
            
            remaining = timeout - (time.time() - start_time)
            if remaining > 0:
                time.sleep(min(poll_interval, remaining))
        
        raise WaitTimeoutError(
            condition="element actionable",
            timeout=timeout
        )

    def wait_until_element_text_contains(
        self,
        alias: str,
        expected: str,
        timeout: float = 10.0,
        case_sensitive: bool = True
    ):
        """Wait for element's text to contain the expected value."""
        from exceptions import WaitTimeoutError
        
        start_time = time.time()
        strategies = repo.get_strategies(alias)
        
        if not strategies:
            raise AllStrategiesFailedError(
                alias=alias,
                attempts=[],
                details={"reason": "No strategies configured"}
            )
        
        while time.time() - start_time < timeout:
            for driver_name in config.DRIVER_ORDER:
                if driver_name not in strategies:
                    continue
                driver = self._drivers.get(driver_name)
                if driver is None:
                    continue
                try:
                    element = driver.find_element(strategies[driver_name])
                    text = driver.get_text(element)
                    if case_sensitive:
                        if expected in text:
                            return True
                    else:
                        if expected.lower() in text.lower():
                            return True
                except Exception:
                    continue
            
            time.sleep(0.5)
        
        raise WaitTimeoutError(
            condition=f"text contains '{expected}'",
            timeout=timeout
        )

    def reset_application(self):
        """Test-isolation keyword — restarts the application at the
        Login page. In mock mode, resets the in-memory mock app.
        In real mode, restarts the actual SampleWpfApp process.
        """
        if _WPFSPY_MODE == "real":
            _reset_real_app()
        else:
            reset_app()
        
        # Reset circuit breakers
        _breaker_manager.reset_all()

    def get_last_strategy_used(self) -> Optional[str]:
        """Returns the name of the driver strategy that last succeeded —
        useful in assertions/logging for the self-healing demo tests.
        """
        return self.last_strategy_used


