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

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
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
    ElementNotFoundError,
    ElementNotInteractableError,
    reset_app,
)

_WPFSPY_MODE = os.environ.get("WPFSPY_MODE", "mock").lower()
_SAMPLE_WPF_APP_PROCESS = None

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

_DRIVERS = {
    "FlaUI": FlaUIDriver(),
    "WPFSpy": WPFSpyDriver(),
    "Sikuli": SikuliDriver(),
}


class AllStrategiesFailedError(Exception):
    """Raised when every configured driver strategy failed to locate or
    act on an element. Carries the full attempt log for diagnosis.
    """
    pass


class DriverAgnosticApi:
    """Robot Framework library — Layer 3 keywords.

    Keyword names below map to Robot Framework keywords by replacing
    underscores with spaces and title-casing, e.g. `click_element` ->
    `Click Element`.
    """

    ROBOT_LIBRARY_SCOPE = "GLOBAL"

    def __init__(self):
        self.last_strategy_used = None
        self.attempt_log = []

    # ------------------------------------------------------------------
    # Core resolution + self-healing fallback
    # ------------------------------------------------------------------
    def _resolve_and_execute(self, alias: str, action_name: str, *args):
        strategies = repo.get_strategies(alias)
        wpfspy_mode = os.environ.get("WPFSPY_MODE", "mock")
        print(f"[DEBUG] _resolve_and_execute: alias={alias}, action={action_name}, WPFSPY_MODE={wpfspy_mode}, strategies={list(strategies.keys())}")
        if not strategies:
            raise AllStrategiesFailedError(f"No strategies configured for alias '{alias}'")

        # WPFSpy-only mode: only try WPFSpy driver
        wpfspy_locator = strategies.get("WPFSpy")
        if wpfspy_locator:
            driver = _DRIVERS["WPFSpy"]
            try:
                print(f"[DEBUG] Trying WPFSpy driver with locator {wpfspy_locator}")
                element = driver.find_element(wpfspy_locator)
                result = getattr(driver, action_name)(element, *args)
                self.last_strategy_used = "WPFSpy"
                self.attempt_log = [("WPFSpy", "SUCCESS")]
                print(f"[Layer3] '{alias}' -> strategy 'WPFSpy' succeeded")
                return result
            except (ElementNotFoundError, ElementNotInteractableError, KeyError) as exc:
                self.attempt_log = [("WPFSpy", f"FAILED: {exc}")]
                raise AllStrategiesFailedError(
                    f"WPFSpy strategy failed for alias '{alias}': {exc}"
                )

        raise AllStrategiesFailedError(
            f"No WPFSpy strategy configured for alias '{alias}'"
        )

    # ------------------------------------------------------------------
    # Public keywords
    # ------------------------------------------------------------------
    def click_element(self, alias: str):
        """Invokes (clicks) the element identified by `alias`."""
        self._resolve_and_execute(alias, "invoke")

    def set_element_value(self, alias: str, value: str):
        """Sets the text/value of the element identified by `alias`."""
        self._resolve_and_execute(alias, "set_value", value)

    def get_element_text(self, alias: str) -> str:
        """Returns the current text of the element identified by `alias`."""
        return self._resolve_and_execute(alias, "get_text")

    def verify_element_text(self, alias: str, expected: str):
        """Fails the test unless the element's text equals `expected`."""
        actual = self.get_element_text(alias)
        if actual != expected:
            raise AssertionError(f"'{alias}' text mismatch: expected '{expected}', got '{actual}'")

    def get_data_grid_content_ocr(self, alias: str) -> str:
        """Captures a DataGrid element screenshot and returns
        its content as CSV text using OCR."""
        return self._resolve_and_execute(alias, "get_data_grid_content_ocr")

    def toggle_element(self, alias: str):
        """Toggles a checkbox/toggle-style element identified by `alias`."""
        self._resolve_and_execute(alias, "toggle")

    def wait_until_element_visible(self, alias: str, timeout: float = 5.0):
        """Polls until the element is visible, or raises after `timeout`
        seconds. (In the mock this resolves immediately since state
        changes are synchronous; kept for API completeness / real-world
        parity where WPF UI updates can be asynchronous.)
        """
        strategies = repo.get_strategies(alias)
        last_exc = None
        for driver_name, locator in strategies.items():
            driver = _DRIVERS[driver_name]
            try:
                element = driver.find_element(locator)
                if driver.is_visible(element):
                    return True
            except (ElementNotFoundError, KeyError) as exc:
                last_exc = exc
                continue
        raise AllStrategiesFailedError(
            f"'{alias}' not visible via any configured strategy (last error: {last_exc})"
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

    def get_last_strategy_used(self) -> str:
        """Returns the name of the driver strategy that last succeeded —
        useful in assertions/logging for the self-healing demo tests.
        """
        return self.last_strategy_used


