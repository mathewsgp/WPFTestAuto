"""
Utility Keywords
================
Utility and management keywords for the DriverAgnosticApi facade.
"""

import os
import time
import subprocess
import sys
from typing import Optional, List, Union, Any

from TestAutoLayer.api.resolution.strategy_executor import StrategyExecutor
from TestAutoLayer.api.app_management import (
    launch_application,
    attach_to_application,
    close_application,
    terminate_application,
    wait_for_application,
    activate_window,
    get_multi_app_context,
)
from TestAutoLayer.api.logging_utils import get_api_logger
from TestAutoLayer.api.screenshot_manager import get_screenshot_manager
from TestAutoLayer.api.base_driver import ElementHandle
from TestAutoLayer.api.config import config
from TestAutoLayer.api.circuit_breaker import CircuitBreakerManager

logger = get_api_logger()


class UtilityKeywords:
    """Utility and management keywords."""
    
    def __init__(
        self,
        strategy_executor: StrategyExecutor,
        get_default_app_id: callable,
        set_default_app_id: callable,
    ):
        self._executor = strategy_executor
        self._get_default_app_id = get_default_app_id
        self._set_default_app_id = set_default_app_id
    
    def _resolve_app_id(self, app_id: Optional[str]) -> Optional[str]:
        """Resolve app_id, falling back to default."""
        if app_id is not None:
            return app_id
        return self._get_default_app_id()
    
    # ------------------------------------------------------------------
    # Driver/Mode Management
    # ------------------------------------------------------------------
    def set_driver(self, driver_name: str):
        """Set the active driver for subsequent element operations."""
        from TestAutoLayer.api.DriverAgnosticApi import _ACTIVE_DRIVER, set_run_modes
        global _ACTIVE_DRIVER
        valid_drivers = {"FlaUI", "WPFSpy", "Sikuli"}
        normalized = driver_name.strip()
        if normalized.lower() not in {d.lower() for d in valid_drivers}:
            raise ValueError(
                f"Invalid driver '{driver_name}'. Valid options: {', '.join(sorted(valid_drivers))}"
            )
        _ACTIVE_DRIVER = normalized
        logger.info("Driver set", driver=_ACTIVE_DRIVER)
        
        # Update run modes to use only the selected driver
        set_run_modes([normalized])
    
    def reset_drivers(self):
        """Reset to default driver order (FlaUI -> WPFSpy -> Sikuli)."""
        from TestAutoLayer.api.DriverAgnosticApi import _ACTIVE_DRIVER, set_run_modes
        global _ACTIVE_DRIVER
        _ACTIVE_DRIVER = None
        set_run_modes(None)  # Reset to default from env/config
        logger.info("Driver reset to default order")
    
    def set_mode(self, mode: str):
        """Set the execution mode for subsequent operations."""
        from TestAutoLayer.api.DriverAgnosticApi import _ACTIVE_MODE
        global _ACTIVE_MODE
        normalized = mode.strip().lower()
        if normalized not in ("mock", "real"):
            raise ValueError(f"Invalid mode '{mode}'. Valid options: mock, real")
        _ACTIVE_MODE = normalized
        logger.info("Mode set", mode=_ACTIVE_MODE)
        
        for app in get_multi_app_context().apps.values():
            app.drivers.clear()
    
    def reset_mode(self):
        """Reset to the mode from the WPFSPY_MODE environment variable."""
        from TestAutoLayer.api.DriverAgnosticApi import _ACTIVE_MODE
        global _ACTIVE_MODE
        _ACTIVE_MODE = None
        logger.info("Mode reset to WPFSPY_MODE env var")
        
        for app in get_multi_app_context().apps.values():
            app.drivers.clear()
    
    def set_mode_and_driver(self, mode: str, driver: str):
        """Set both execution mode and driver in one call."""
        self.set_mode(mode)
        self.set_driver(driver)
        
        from TestAutoLayer.api.DriverAgnosticApi import set_run_modes
        # Update run modes based on selected driver
        if driver.lower() == "auto":
            # Auto mode: use all drivers from priority order
            set_run_modes(None)
        else:
            # Specific driver: use only that driver
            set_run_modes([driver])
    
    # ------------------------------------------------------------------
    # Element Operations
    # ------------------------------------------------------------------
    def get_element_text(self, alias: str, app_id: Optional[str] = None) -> str:
        """Returns the current text of the element identified by `alias`."""
        app_id = self._resolve_app_id(app_id)
        return self._executor.execute(alias, "get_text", app_id)
    
    def get_data_grid_content_ocr(self, alias: str, app_id: Optional[str] = None) -> str:
        """Captures a DataGrid element screenshot and returns
        its content as CSV text using OCR."""
        app_id = self._resolve_app_id(app_id)
        return self._executor.execute(alias, "get_data_grid_content_ocr", app_id)
    
    def capture_screenshot(self, filename: str = None, app_id: Optional[str] = None) -> str:
        """Capture a screenshot of the current screen or target application."""
        app_id = self._resolve_app_id(app_id)
        screenshot_mgr = get_screenshot_manager()
        app_context = get_multi_app_context().get_app(app_id) if app_id or get_multi_app_context().apps else None
        
        screenshot_data = None
        driver_used = None
        
        if app_context:
            for driver_name in app_context.driver_list:
                if driver_name not in app_context.drivers:
                    app_context.drivers[driver_name] = app_context.get_driver(driver_name)
                driver = app_context.drivers[driver_name]
                try:
                    screenshot_data = driver.capture_screenshot()
                    driver_used = driver_name
                    break
                except Exception:
                    continue
        
        if screenshot_data is None:
            for driver_name in config.DRIVER_ORDER:
                driver = self._executor._driver_provider(driver_name, None)
                if driver is None:
                    continue
                try:
                    screenshot_data = driver.capture_screenshot()
                    driver_used = driver_name
                    break
                except Exception:
                    continue
        
        if screenshot_data is None:
            raise RuntimeError("No driver available for screenshot capture")
        
        # Normalize screenshot data to bytes
        if isinstance(screenshot_data, str):
            import base64
            try:
                screenshot_data = base64.b64decode(screenshot_data)
            except Exception:
                screenshot_data = screenshot_data.encode("utf-8")
        elif not isinstance(screenshot_data, bytes):
            screenshot_data = str(screenshot_data).encode("utf-8")
        
        if filename is None:
            prefix = f"screenshot_{app_context.app_id}" if app_context else "screenshot"
            filename = screenshot_mgr._generate_filename(prefix)
        
        metadata = screenshot_mgr.capture(
            image_data=screenshot_data,
            alias=None,
            error_type=None,
            error_message=None,
            driver_used=driver_used,
            prefix=os.path.splitext(os.path.basename(filename))[0] if filename else None
        )
        
        return metadata.screenshot_path if metadata else filename
    
    # ------------------------------------------------------------------
    # Clipboard Operations
    # ------------------------------------------------------------------
    def set_clipboard_text(self, text: str):
        """Sets the Windows clipboard to the given text."""
        import win32clipboard
        import win32con
        win32clipboard.OpenClipboard()
        try:
            win32clipboard.EmptyClipboard()
            win32clipboard.SetClipboardText(text, win32clipboard.CF_UNICODETEXT)
        finally:
            win32clipboard.CloseClipboard()
        logger.info("Clipboard text set", text_length=len(text))
    
    def get_clipboard_text(self) -> str:
        """Returns the current text from the Windows clipboard."""
        import win32clipboard
        win32clipboard.OpenClipboard()
        try:
            data = win32clipboard.GetClipboardData(win32clipboard.CF_UNICODETEXT)
        finally:
            win32clipboard.CloseClipboard()
        logger.info("Clipboard text retrieved", text_length=len(data))
        return data
    
    def copy_element_text(self, alias: str, app_id: Optional[str] = None):
        """Gets the text of an element and places it on the clipboard."""
        app_id = self._resolve_app_id(app_id)
        text = self._executor.execute(alias, "get_text", app_id)
        self.set_clipboard_text(text)
        logger.info("Element text copied to clipboard", alias=alias, text=text)
    
    def paste_clipboard_to_element(self, alias: str, app_id: Optional[str] = None):
        """Pastes the current clipboard text into the specified element."""
        app_id = self._resolve_app_id(app_id)
        time.sleep(0.2)
        self._executor.execute(alias, "press_keys", app_id, "^v")
        logger.info("Clipboard pasted to element", alias=alias)
    
    def send_keys_to_window(self, window_title: str, keys: str):
        """Sends keystrokes to a window by title using WScript.Shell."""
        # Escape single quotes in keys for VBScript
        escaped_keys = keys.replace("'", "''")
        cmd = (
            'powershell -Command "'
            f"$wshell = New-Object -ComObject WScript.Shell; "
            f"$wshell.AppActivate('{window_title}'); "
            f"Start-Sleep -Milliseconds 300; "
            f"$wshell.SendKeys('{escaped_keys}')"
            '"'
        )
        subprocess.run(cmd, shell=True, check=False, capture_output=True)
        time.sleep(0.3)
        logger.info("Keys sent to window", window_title=window_title, keys=keys)
    
    # ------------------------------------------------------------------
    # Application Management
    # ------------------------------------------------------------------
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
        """Register a new application context for automation."""
        # Normalize driver_list from Robot Framework named-arg string.
        if driver_list is None:
            driver_list = None
        elif isinstance(driver_list, str):
            driver_list = [d.strip() for d in driver_list.split(",") if d.strip()]
        else:
            driver_list = [str(d).strip() for d in driver_list if str(d).strip()]

        from TestAutoLayer.api.app_management.app_registry import AppContext
        app_context = AppContext(
            app_id=app_id,
            app_name=app_name,
            driver_list=driver_list,
            process_id=process_id,
            pipe_name=pipe_name,
            app_path=app_path,
            launch_args=launch_args or [],
        )
        get_multi_app_context().register_app(app_context)
        logger.info("Registered application", app_id=app_id, app_name=app_name, driver_list=driver_list)
        return app_id
    
    def switch_application(self, app_id: str):
        """Switch the default application context."""
        get_multi_app_context().set_default_app(app_id)
        self._set_default_app_id(app_id)
        logger.info("Switched application context", app_id=app_id)
    
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
        """Launch an application and register it."""
        return launch_application(
            app_path=app_path,
            app_id=app_id,
            args=args,
            start_in=start_in,
            drivers=drivers,
            attach=attach,
            timeout=timeout,
        )
    
    def attach_to_application(self, app_id: str, process_id: Union[int, str], driver_list: Optional[Union[str, List[str]]] = None, pipe_name: Optional[str] = None) -> str:
        """Attach to a running application and register it."""
        if isinstance(process_id, str):
            try:
                process_id = int(process_id)
            except (ValueError, TypeError):
                process_id = None
        return attach_to_application(app_id, process_id, driver_list, pipe_name)
    
    def close_application(self, app_id: str):
        """Close and unregister an application."""
        close_application(app_id)
        logger.info("Closed application", app_id=app_id)
    
    def terminate_application(
        self,
        app_id: Optional[str] = None,
        window_title: Optional[str] = None,
        process_name: Optional[str] = None,
        force: bool = False,
    ) -> int:
        """Terminate a running application."""
        return terminate_application(app_id, window_title, process_name, force)
    
    def get_application_list(self) -> List[str]:
        """List all registered application IDs."""
        return [app["app_id"] for app in get_multi_app_context().list_apps()]
    
    def set_default_application(self, app_id: str):
        """Set the default application for subsequent keywords."""
        get_multi_app_context().set_default_app(app_id)
        self._set_default_app_id(app_id)
        logger.info("Set default application", app_id=app_id)
    
    def get_current_application(self) -> str:
        """Get the current default application ID."""
        return get_multi_app_context().default_app_id or ""
    
    def wait_for_application(self, app_id: str, timeout: float = 30.0, poll_interval: float = 1.0) -> bool:
        """Wait for an application to become available."""
        return wait_for_application(app_id, timeout, poll_interval)
    
    def activate_window(self, app_id: Optional[str] = None, window_title: Optional[str] = None):
        """Activates (brings to front and focuses) a window by app_id or title."""
        activate_window(app_id, window_title)
    
    # ------------------------------------------------------------------
    # Status/Debug
    # ------------------------------------------------------------------
    def is_pipe_ready(self, pipe_name: str = "WPFSpyAgentPipe") -> bool:
        """Check if the Spy Agent named pipe is ready for connections."""
        if sys.platform != "win32":
            return False
        try:
            from TestAutoLayer.api.robot_launcher import is_agent_ready
            return is_agent_ready(pipe_name)
        except Exception:
            return False
    
    def get_last_strategy_used(self) -> Optional[str]:
        """Returns the name of the driver strategy that last succeeded."""
        return self._executor.last_strategy_used
    
    def reset_application(self):
        """Test-isolation keyword — restarts the application at the
        Login page. In mock mode, resets the in-memory mock app.
        In real mode, restarts the actual SampleWpfApp process.
        """
        from TestAutoLayer.api.DriverAgnosticApi import _ACTIVE_MODE, _WPFSPY_MODE, _breaker_manager
        effective_mode = _ACTIVE_MODE if _ACTIVE_MODE is not None else _WPFSPY_MODE
        if effective_mode == "real":
            from TestAutoLayer.api.sample_wpf_app_manager import reset_real_app
            reset_real_app()
        else:
            from TestAutoLayer.api.mock_app import reset_app
            reset_app()
        
        # Reset circuit breakers
        _breaker_manager.reset_all()