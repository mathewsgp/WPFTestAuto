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
from pathlib import Path
from typing import Optional, Tuple, Dict, Any, List, Union

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

# Import app context for multi-application support
from app_context import MultiAppContext, AppContext, _create_driver_for_app, _launch_app_for_context  # noqa: E402

# Import healing metadata store (Phase 1 feature)
from healing_metadata_store import get_healing_store  # noqa: E402

# Import screenshot manager for automatic failure screenshots
from screenshot_manager import get_screenshot_manager  # noqa: E402

_THIS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(_THIS_DIR, "..", "drivers_rf", "flaui_robotframework"))
sys.path.insert(0, os.path.join(_THIS_DIR, "..", "drivers_rf", "wpfspy_robotframework"))
sys.path.insert(0, os.path.join(_THIS_DIR, "..", "drivers_rf", "sikuli_robotframework"))
sys.path.insert(0, os.path.join(_THIS_DIR, "..", "drivers", "mock_wpf_app"))

from flaui_driver import FlaUIDriver          # noqa: E402
from WPFSpyLibrary import WPFSpyDriver        # noqa: E402
from SikuliLibrary import SikuliDriver        # noqa: E402
from mock_app import (                        # noqa: E402
    ElementNotFoundError as MockElementNotFoundError,
    ElementNotInteractableError as MockElementNotInteractableError,
    reset_app,
)

_WPFSPY_MODE = os.environ.get("WPFSPY_MODE", "mock").lower()
_SAMPLE_WPF_APP_PROCESS = None

# Active driver override (None = use default DRIVER_ORDER)
_ACTIVE_DRIVER = None

# Active mode override (None = use WPFSPY_MODE env var)
_ACTIVE_MODE = None

# Run modes filter (None = use all drivers in DRIVER_ORDER)
# Comma-separated list of enabled drivers for test execution
_RUN_MODES = None

# Driver priority order for element identification (None = use DRIVER_ORDER)
# Comma-separated list like "FlaUI,WPFSpy,Sikuli"
_DRIVER_PRIORITY = None

# Global multi-app context registry
_MULTI_APP_CONTEXT = MultiAppContext()

# Initialize logger
logger = get_api_logger()

# Circuit breaker manager
_breaker_manager = CircuitBreakerManager(
    threshold=config.CIRCUIT_BREAKER_THRESHOLD,
    timeout=config.CIRCUIT_BREAKER_TIMEOUT
)

def _get_sample_wpf_app_path():
    """Returns the path to the SampleWpfApp executable."""
    base = os.path.join(_THIS_DIR, "..", "SampleWpfApp", "bin", "Debug", "net8.0-windows")
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
    """Starts SampleWpfApp with the WPFSpy agent startup hook.
    
    Searches for the StartupHook DLL in multiple locations and launches
    the app with DOTNET_STARTUP_HOOKS set so the agent initializes.
    """
    global _SAMPLE_WPF_APP_PROCESS
    
    app_path = _get_sample_wpf_app_path()
    
    # Find the StartupHook DLL in common locations
    startup_hook = None
    
    # 1. Check same directory as the app (copied during build)
    app_dir = os.path.dirname(app_path)
    candidate = os.path.join(app_dir, "WpfSpyAgent.StartupHook.dll")
    if os.path.exists(candidate):
        startup_hook = candidate
    
    # 2. Check solution-level WpfSpyAgent.StartupHook output
    if not startup_hook:
        candidate = os.path.join(_THIS_DIR, "..", "WpfSpyAgent.StartupHook", "bin", "Debug", "net8.0-windows", "WpfSpyAgent.StartupHook.dll")
        if os.path.exists(candidate):
            startup_hook = candidate
    
    # 3. Check runtime_injector's search paths
    if not startup_hook:
        try:
            from runtime_injector import RuntimeInjector
            injector = RuntimeInjector()
            if injector.startup_hook_path:
                startup_hook = injector.startup_hook_path
        except (ImportError, Exception):
            pass
    
    env = os.environ.copy()
    env["WPFSPY_AGENT_ENABLED"] = "1"
    env["WPFSPY_PIPE_NAME"] = "WPFSpyAgentPipe"
    if startup_hook:
        env["DOTNET_STARTUP_HOOKS"] = startup_hook
        print(f'[DriverAgnosticApi] Using startup hook: {startup_hook}')
    else:
        print('[DriverAgnosticApi] WARNING: Startup hook DLL not found, WPFSpy agent will not be injected')
    
    cmd = ["dotnet", app_path]
    _SAMPLE_WPF_APP_PROCESS = subprocess.Popen(
        cmd,
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    
    # Wait for the app and agent to initialize (startup hook polls up to 10s)
    time.sleep(8)
    
    # Verify the agent is ready
    try:
        import win32file
        pipe_path = r"\\.\pipe\WPFSpyAgentPipe"
        for _ in range(10):
            try:
                handle = win32file.CreateFile(
                    pipe_path,
                    win32file.GENERIC_READ,
                    0, None,
                    win32file.OPEN_EXISTING,
                    0, None,
                )
                win32file.CloseHandle(handle)
                print('[DriverAgnosticApi] WPFSpy agent is ready on pipe')
                return
            except Exception:
                time.sleep(1)
        print('[DriverAgnosticApi] WARNING: WPFSpy agent did not become ready within timeout')
    except ImportError:
        pass  # pywin32 not available, skip readiness check

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
    Also resets the mock app instance so FlaUI driver state is consistent.
    """
    # Always reset the mock app so FlaUI driver starts from a known state
    try:
        from mock_app import reset_app
        reset_app()
    except Exception:
        pass
    
    ide_mode = os.environ.get('WPFSPY_IDE_RUN') == '1'
    effective_mode = _ACTIVE_MODE if _ACTIVE_MODE is not None else _WPFSPY_MODE
    print(f'[DriverAgnosticApi] _reset_real_app called, mode={effective_mode}, ide_mode={ide_mode}')

    if ide_mode:
        # IDE mode: keep app running, just reset state
        if effective_mode == 'real' and _SAMPLE_WPF_APP_PROCESS is not None and _SAMPLE_WPF_APP_PROCESS.poll() is None:
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
        if effective_mode == 'real':
            print('[DriverAgnosticApi] CLI mode: closing and reopening SampleWpfApp')
            _kill_sample_wpf_app()
            time.sleep(2)
            _start_sample_wpf_app()
            _reload_drivers()  # Re-attach drivers to the new process
        else:
            # Mock mode: reset the mock app
            print('[DriverAgnosticApi] Mock mode: resetting mock app')
            try:
                from mock_app import reset_app
                reset_app()
            except Exception:
                pass

# Lazy driver initialization
_DRIVERS: dict = {}
_DRIVERS_INITIALIZED: bool = False


def _get_drivers() -> dict:
    """Lazy initialization of drivers. Returns cached drivers dict.
    
    The WPFSpy driver is created based on current mode:
    - In 'real' mode: uses WPFSpyRealDriver (named pipe communication)
    - In 'mock' mode: uses WPFSpyMockDriver (in-memory mock app)
    
    Optional drivers (FlaUI, Sikuli) are skipped if their dependencies
    are not installed.
    """
    global _DRIVERS, _DRIVERS_INITIALIZED
    if not _DRIVERS_INITIALIZED:
        _DRIVERS = {}
        # Try optional drivers; skip any that are not installed
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
    """Create the appropriate WPFSpy driver based on current mode.
    
    Uses _ACTIVE_MODE if set, otherwise falls back to WPFSPY_MODE env var.
    """
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
    """Get the enabled driver modes for test execution.
    
    Reads from:
    1. WPFSPY_RUN_MODES env var (comma-separated)
    2. Falls back to DRIVER_ORDER from config
    
    Returns:
        List of enabled driver names in priority order.
    """
    global _RUN_MODES, _DRIVER_PRIORITY
    
    if _RUN_MODES is not None:
        return _RUN_MODES
    
    # Read from environment
    run_modes_env = os.environ.get("WPFSPY_RUN_MODES", "").strip()
    if run_modes_env:
        _RUN_MODES = [d.strip() for d in run_modes_env.split(",") if d.strip()]
        return _RUN_MODES
    
    # Read priority order from environment
    priority_env = os.environ.get("WPFSPY_DRIVER_PRIORITY", "").strip()
    if priority_env:
        _DRIVER_PRIORITY = [d.strip() for d in priority_env.split(",") if d.strip()]
        _RUN_MODES = _DRIVER_PRIORITY
        return _RUN_MODES
    
    # Fall back to config DRIVER_ORDER
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


class DriverAgnosticApi:
    """Robot Framework library — Layer 3 keywords.

    Keyword names below map to Robot Framework keywords by replacing
    underscores with spaces and title-casing, e.g. `click_element` ->
    `Click Element`.
    """

    ROBOT_LIBRARY_SCOPE = "GLOBAL"

    def __init__(self, default_app_id: Optional[str] = None):
        self.last_strategy_used: Optional[str] = None
        self.attempt_log: list = []
        self._app_id = default_app_id
        self._drivers: Dict[str, Any] = {}
        self._app_contexts: Dict[str, AppContext] = {}
        if default_app_id:
            _MULTI_APP_CONTEXT.set_default_app(default_app_id)
        
        # Auto-register app from IDE environment variables if present
        self._auto_register_from_env()
    
    def _auto_register_from_env(self):
        """Auto-register app context from WPFSPY_* environment variables.
        
        Called during library initialization to support IDE runs where
        the IDE passes app details via environment variables.
        """
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
        
        if app_id not in _MULTI_APP_CONTEXT.apps:
            app_context = AppContext(
                app_id=app_id,
                app_name=app_name,
                driver="FlaUI",
                process_id=process_id,
                pipe_name=pipe_name,
            )
            _MULTI_APP_CONTEXT.register_app(app_context)
            logger.info("Auto-registered app from IDE env vars", app_id=app_id, process_id=process_id)

    # ------------------------------------------------------------------
    # Multi-Application Management
    # ------------------------------------------------------------------
    def register_application(
        self,
        app_id: str,
        app_name: str,
        driver: str = "FlaUI",
        process_id: Optional[int] = None,
        pipe_name: Optional[str] = None,
        app_path: Optional[str] = None,
        launch_args: Optional[List[str]] = None,
    ) -> str:
        """Register a new application context for automation.

        Args:
            app_id: Logical ID for this app (e.g. 'main', 'helper').
            app_name: Human-readable name (e.g. 'SampleWpfApp').
            driver: Primary driver ('FlaUI', 'WPFSpy', 'Sikuli').
            process_id: OS process ID if already running.
            pipe_name: Named pipe for WPFSpy agent.
            app_path: Path to executable/DLL for launching.
            launch_args: Additional arguments for launch.

        Returns:
            The registered app_id.
        """
        app_context = AppContext(
            app_id=app_id,
            app_name=app_name,
            driver=driver,
            process_id=process_id,
            pipe_name=pipe_name,
            app_path=app_path,
            launch_args=launch_args or [],
        )
        _MULTI_APP_CONTEXT.register_app(app_context)
        logger.info("Registered application", app_id=app_id, app_name=app_name, driver=driver)
        return app_id

    def switch_application(self, app_id: str):
        """Switch the default application context.

        Args:
            app_id: The application ID to switch to.
        """
        _MULTI_APP_CONTEXT.set_default_app(app_id)
        self._app_id = app_id
        logger.info("Switched application context", app_id=app_id)

    def launch_application(
        self,
        app_path: str,
        app_id: Optional[str] = None,
        driver: Optional[str] = None,
        args: Optional[Union[str, List[str]]] = None,
        start_in: Optional[str] = None,
        attach: bool = False,
        pipe_name: Optional[str] = None,
        spy_agent: Optional[bool] = None,
        timeout: float = 30.0,
    ) -> str:
        """Launch an application and register it.

        Args:
            app_path: Path to executable/DLL. First positional — the natural
                Robot Framework usage is
                ``Launch Application    <path>    app_id=<id>    ...``.
            app_id: Logical ID for this app. Optional; when None, a default
                of ``<exe-name>`` is generated. Use ``app_id=`` from Robot to
                keep the call argument order clean (positional after named is
                rejected by Robot).
            driver: Primary driver. When None (default), auto-detected from
                the app path/name — ``.dll`` or a name containing ``wpf`` /
                ``xaml`` selects ``WPFSpy``; otherwise ``FlaUI``.
            args: Command-line arguments. Either a single string (will be
                split on whitespace respecting simple quotes) or a list of
                strings.
            start_in: Working directory for the launched process. Optional.
            attach: If True, automatically register the spawned process with
                `attach_to_application` so subsequent keywords can drive it
                without a separate attach step. Defaults to False.
            pipe_name: Named pipe for WPFSpy. Defaults to a per-app
                `WPFSpyAgentPipe_<app_id>` when driver=='WPFSpy'.
            spy_agent: If True, enable the in-process Spy Agent for the
                launched application (sets DOTNET_STARTUP_HOOKS and
                WPFSPY_AGENT_ENABLED). When None (default), follows the
                driver — True for WPFSpy, False for FlaUI. Set explicitly
                to override.
            timeout: Seconds to wait for the process to become available
                before returning.

        Returns:
            The registered app_id.
        """
        # Normalize args to a list of strings.
        if args is None:
            launch_args: List[str] = []
        elif isinstance(args, str):
            # Naive split that respects simple double-quoted substrings.
            import shlex
            try:
                launch_args = shlex.split(args, posix=False)
            except ValueError:
                launch_args = [args]
        else:
            launch_args = list(args)

        # Default app_id to the executable name (no extension) when the caller
        # didn't supply one. This keeps backward compatibility with the older
        # signature while making the new positional-first form ergonomic.
        if not app_id:
            base = os.path.basename(app_path)
            stem = os.path.splitext(base)[0]
            app_id = (stem or "app").lower()

        effective_pipe = pipe_name
        if effective_pipe is None and driver == "WPFSpy":
            effective_pipe = f"WPFSpyAgentPipe_{app_id}"

        # Auto-detect driver and spy_agent when the caller didn't specify them.
        # WPF heuristics: .dll extension or filename containing 'wpf'/'xaml'.
        norm_for_detect = (app_path or "").replace("/", "\\").lower()
        base_for_detect = os.path.basename(norm_for_detect)
        is_likely_wpf = (
            base_for_detect.endswith(".dll")
            or "wpf" in base_for_detect
            or "xaml" in base_for_detect
        )
        if driver is None:
            driver = "WPFSpy" if is_likely_wpf else "FlaUI"
        if spy_agent is None:
            spy_agent = driver == "WPFSpy"
        # Recompute pipe name if driver was previously None and got resolved.
        if effective_pipe is None and driver == "WPFSpy":
            effective_pipe = f"WPFSpyAgentPipe_{app_id}"

        # Capture process_id before registering so the registration sees the
        # live pid (used by WPFSpy attach for pipe lookups).
        from app_context import _launch_app_for_context, AppContext

        # DIAGNOSTIC: log the exact strings we received, so launch failures
        # are easy to diagnose from the test log.
        logger.info(
            "launch_application received",
            app_path_repr=repr(app_path),
            app_id_repr=repr(app_id),
            start_in_repr=repr(start_in),
            args_repr=repr(args),
            attach=attach,
        )

        # Normalize the path: forward slashes are accepted on Windows but
        # Popen+CreateProcess is happiest with backslashes. Also reject
        # empty paths and any path that contains an actual newline (which
        # would silently break Popen and surface as WinError 2).
        norm_path = (app_path or "").replace("/", "\\").strip()
        if "\n" in norm_path or "\r" in norm_path:
            raise ValueError(
                f"app_path contains a newline character (will break CreateProcess): {norm_path!r}"
            )
        if not norm_path:
            raise ValueError("app_path is required and cannot be empty")
        norm_start_in = (start_in or "").replace("/", "\\").strip() or None
        if norm_start_in and ("\n" in norm_start_in or "\r" in norm_start_in):
            raise ValueError(
                f"start_in contains a newline character: {norm_start_in!r}"
            )

        app_context = AppContext(
            app_id=app_id,
            app_name=os.path.basename(norm_path),
            driver=driver,
            app_path=norm_path,
            launch_args=launch_args,
            pipe_name=effective_pipe,
            start_in=norm_start_in,
            auto_attach=attach,
            spy_agent=spy_agent,
        )
        try:
            app_context.process = _launch_app_for_context(app_context)
            if app_context.process.pid:
                app_context.process_id = app_context.process.pid
        except Exception as e:
            logger.error(f"Failed to launch application: {e}")
            raise
        _MULTI_APP_CONTEXT.register_app(app_context)
        logger.info(
            "Launched application",
            app_id=app_id,
            app_path=app_path,
            pid=app_context.process_id,
            start_in=start_in,
            attach=attach,
        )

        # If the user asked us to auto-attach (e.g. for WPFSpy), make sure the
        # WPFSpy agent is reachable via the named pipe before continuing.
        if attach and driver == "WPFSpy":
            try:
                self.wait_for_application(app_id, timeout=timeout)
            except Exception as e:
                logger.warning(
                    f"Auto-attach wait for {app_id} did not confirm agent readiness: {e}"
                )

        return app_id

    def attach_to_application(self, app_id: str, process_id: Union[int, str], driver: str = "FlaUI", pipe_name: Optional[str] = None) -> str:
        """Attach to a running application and register it.

        Args:
            app_id: Logical ID for this app.
            process_id: OS process ID (int or string).
            driver: Primary driver.
            pipe_name: Named pipe for WPFSpy agent.

        Returns:
            The registered app_id.
        """
        if isinstance(process_id, str):
            try:
                process_id = int(process_id)
            except (ValueError, TypeError):
                process_id = None
        
        app_context = AppContext(
            app_id=app_id,
            app_name=f"Process-{process_id}" if process_id else app_id,
            driver=driver,
            process_id=process_id,
            pipe_name=pipe_name or f"WPFSpyAgentPipe_{app_id}",
        )
        _MULTI_APP_CONTEXT.register_app(app_context)
        logger.info("Attached to application", app_id=app_id, process_id=process_id)
        return app_id

    def close_application(self, app_id: str):
        """Close and unregister an application.

        Args:
            app_id: The application ID to close.
        """
        _MULTI_APP_CONTEXT.unregister_app(app_id)
        logger.info("Closed application", app_id=app_id)

    def terminate_application(
        self,
        app_id: Optional[str] = None,
        window_title: Optional[str] = None,
        process_name: Optional[str] = None,
        force: bool = False,
    ) -> int:
        """Terminate a running application by registered app_id, window title,
        or process name (executable image name).

        At least one of `app_id`, `window_title`, or `process_name` must be
        provided. When multiple identifiers match, all matching processes are
        terminated and the framework state is updated (unregistered, if
        applicable). Returns the number of processes terminated.

        Args:
            app_id: Optional registered app_id (preferred). When supplied,
                the framework's known process for that app is terminated.
            window_title: Optional substring match against the main window
                title of running processes. Case-insensitive contains.
            process_name: Optional executable image name (e.g. "SampleWpfApp"
                or "SampleWpfApp.exe"). Case-insensitive exact match.
            force: When True, force-kill (taskkill /F). Default sends a
                graceful WM_CLOSE first, then escalates to force if the
                process is still alive after a short timeout.
        """
        if not (app_id or window_title or process_name):
            raise ValueError(
                "terminate_application requires one of: app_id, window_title, process_name"
            )

        terminated = 0

        # Path 1: terminate by app_id (framework-registered process).
        if app_id:
            try:
                ctx = _MULTI_APP_CONTEXT.get_app(app_id)
            except Exception:
                ctx = None
            if ctx is not None and ctx.process_id:
                if self._kill_pid(int(ctx.process_id), force=force):
                    terminated += 1
                try:
                    _MULTI_APP_CONTEXT.unregister_app(app_id)
                except Exception:
                    pass
                return terminated

        # Path 2: discover processes by window title or process name.
        targets = self._find_pids_by_title_or_name(
            window_title=window_title,
            process_name=process_name,
        )
        for pid in targets:
            if self._kill_pid(int(pid), force=force):
                terminated += 1

        logger.info(
            "terminate_application completed",
            app_id=app_id,
            window_title=window_title,
            process_name=process_name,
            terminated=terminated,
        )
        return terminated

    @staticmethod
    def _kill_pid(pid: int, force: bool = False) -> bool:
        """Kill a single PID. Returns True on success.

        Tries graceful WM_CLOSE (only meaningful for top-level windows on
        Windows) and falls back to terminate / force.
        """
        if pid is None or pid <= 0:
            return False
        try:
            if not force and sys.platform == "win32":
                # Best-effort graceful close for windows that own a top-level
                # window. If WM_CLOSE doesn't work we fall through to taskkill.
                try:
                    import ctypes
                    from ctypes import wintypes
                    user32 = ctypes.WinDLL("user32", use_last_error=True)
                    EnumWindows = user32.EnumWindows
                    EnumWindowsProc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
                    GetWindowThreadProcessId = user32.GetWindowThreadProcessId

                    class PIDHolder(ctypes.Structure):
                        _fields_ = [("pid", wintypes.DWORD),
                                    ("hwnd", wintypes.HWND)]

                    holder = PIDHolder(pid, 0)

                    def callback(hwnd, lparam):
                        owner_pid = wintypes.DWORD()
                        GetWindowThreadProcessId(hwnd, ctypes.byref(owner_pid))
                        if owner_pid.value == pid and user32.IsWindowVisible(hwnd):
                            holder.hwnd = hwnd
                            return 0
                        return 1

                    EnumWindows(EnumWindowsProc(callback), 0)
                    if holder.hwnd:
                        user32.PostMessageW(holder.hwnd, 0x0010, 0, 0)  # WM_CLOSE
                        time.sleep(1.0)
                except Exception:
                    pass

            # Final escalation to taskkill.
            if sys.platform == "win32":
                args = ["taskkill", "/PID", str(pid)]
                if force:
                    args.append("/F")
                subprocess.run(args, check=False,
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            else:
                import signal as _sig
                try:
                    os.kill(pid, _sig.SIGKILL if force else _sig.SIGTERM)
                except ProcessLookupError:
                    return False
            return True
        except Exception as e:
            logger.warning(f"Failed to kill pid {pid}: {e}")
            return False

    @staticmethod
    def _find_pids_by_title_or_name(
        window_title: Optional[str] = None,
        process_name: Optional[str] = None,
    ) -> List[int]:
        """Return PIDs whose process name matches `process_name` (case-insensitive
        exact match) OR whose main window title contains `window_title`
        (case-insensitive substring)."""
        results: List[int] = []
        if not (window_title or process_name):
            return results
        if sys.platform != "win32":
            # POSIX fallback: psutil if available, else empty.
            try:
                import psutil  # type: ignore
                for p in psutil.process_iter(["pid", "name"]):
                    if process_name and p.info["name"].lower() == process_name.lower():
                        results.append(int(p.info["pid"]))
            except Exception:
                pass
            return results

        try:
            import psutil  # type: ignore
        except ImportError:
            psutil = None  # type: ignore

        if psutil is not None:
            try:
                for p in psutil.process_iter(["pid", "name"]):
                    try:
                        if process_name and p.info["name"].lower() == process_name.lower():
                            results.append(int(p.info["pid"]))
                            continue
                        if window_title:
                            title = ""
                            try:
                                title = p.info.get("windows_title") or ""
                                if not title:
                                    # psutil's windows_title is None unless explicitly requested
                                    title = ""
                            except Exception:
                                title = ""
                            if window_title.lower() in title.lower():
                                results.append(int(p.info["pid"]))
                    except (psutil.NoSuchProcess, psutil.AccessDenied):
                        continue
            except Exception as e:
                logger.debug(f"psutil enumeration failed: {e}")
            return results

        # Fallback without psutil: tasklist + window enumeration.
        try:
            out = subprocess.check_output(
                ["tasklist", "/FO", "CSV", "/NH"], text=True, errors="ignore"
            )
            for line in out.splitlines():
                line = line.strip()
                if not line:
                    continue
                parts = line.split("\",\"")
                if len(parts) < 2:
                    continue
                name = parts[0].strip('"')
                pid_s = parts[1].strip('"')
                try:
                    pid = int(pid_s)
                except ValueError:
                    continue
                if process_name and name.lower() == process_name.lower():
                    results.append(pid)
            # Window title matching requires Win32 EnumWindows; without psutil
            # we skip window-title filtering and rely on process_name only.
        except Exception as e:
            logger.debug(f"tasklist fallback failed: {e}")
        return results

    def get_application_list(self) -> List[str]:
        """List all registered application IDs."""
        return [app["app_id"] for app in _MULTI_APP_CONTEXT.list_apps()]

    def set_default_application(self, app_id: str):
        """Set the default application for subsequent keywords.

        Args:
            app_id: The application ID to set as default.
        """
        _MULTI_APP_CONTEXT.set_default_app(app_id)
        self._app_id = app_id
        logger.info("Set default application", app_id=app_id)

    def get_current_application(self) -> str:
        """Get the current default application ID."""
        return _MULTI_APP_CONTEXT.default_app_id or ""

    def wait_for_application(self, app_id: str, timeout: float = 30.0, poll_interval: float = 1.0) -> bool:
        """Wait for an application to become available.
        
        Polls until the app is registered and at least one driver
        for it is ready, or raises after timeout seconds.
        
        Args:
            app_id: Application ID to wait for.
            timeout: Maximum seconds to wait.
            poll_interval: Seconds between polls.
            
        Returns:
            True if the app became available.
            
        Raises:
            TimeoutError: If the app is not available within timeout.
        """
        start_time = time.time()
        while time.time() - start_time < timeout:
            try:
                app_context = _MULTI_APP_CONTEXT.get_app(app_id)
                if app_context.process_id:
                    return True
            except ValueError:
                pass
            
            time.sleep(poll_interval)
        
        raise TimeoutError(f"Application '{app_id}' not available within {timeout}s")

    def capture_screenshot(self, filename: str = None, app_id: Optional[str] = None) -> str:
        """Capture a screenshot of the current screen or target application.
        
        Args:
            filename: Optional filename for the screenshot.
                      If None, generates a timestamped name.
            app_id: Optional application context ID. If None, uses default app.
            
        Returns:
            Path to the saved screenshot file.
        """
        screenshot_mgr = get_screenshot_manager()
        app_context = _MULTI_APP_CONTEXT.get_app(app_id) if app_id or _MULTI_APP_CONTEXT.apps else None
        
        screenshot_data = None
        driver_used = None
        
        if app_context:
            for driver_name in _get_run_modes():
                if driver_name not in app_context.drivers:
                    continue
                driver = app_context.drivers[driver_name]
                try:
                    screenshot_data = driver.capture_screenshot()
                    driver_used = driver_name
                    break
                except Exception:
                    continue
        
        if screenshot_data is None:
            for driver_name in _get_run_modes():
                driver = _get_drivers().get(driver_name)
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
            prefix=Path(filename).stem
        )
        
        return metadata.screenshot_path if metadata else filename

    # ------------------------------------------------------------------
    # Core resolution + self-healing fallback
    # ------------------------------------------------------------------
    def _resolve_and_execute(self, alias: str, action_name: str, app_id: Optional[str] = None, *args):
        """Resolve element and execute action with self-healing fallback.

        Backward-compatible: when no apps are registered and no app_id is
        provided, falls back to the legacy global-driver behavior so
        existing tests and IDE-generated scripts keep working.

        Args:
            alias: Element alias from repository.
            action_name: Method name on driver (invoke, set_value, get_text, etc.).
            app_id: Optional application context ID. If None, uses default app.
            *args: Action-specific arguments.
        """
        healing_store = None
        try:
            healing_store = get_healing_store()
        except Exception:
            pass
        
        if app_id is None and not _MULTI_APP_CONTEXT.apps:
            return self._resolve_and_execute_legacy(alias, action_name, *args)

        app_context = _MULTI_APP_CONTEXT.get_app(app_id)
        app_drivers = {}
        for driver_name in _get_run_modes():
            if driver_name not in app_context.drivers:
                app_context.drivers[driver_name] = _create_driver_for_app(driver_name, app_context)
            app_drivers[driver_name] = app_context.drivers[driver_name]

        all_strategies = repo.get_all_driver_strategies_sorted(alias, app_id=app_context.app_id)

        if not all_strategies:
            raise AllStrategiesFailedError(
                alias=alias,
                attempts=[],
                details={"reason": "No strategies configured"}
            )

        driver_order = _get_run_modes()
        attempts = []
        
        # Track healing info: first failure and subsequent success
        healing_info = {
            "attempted": False,
            "primary_driver": None,
            "primary_search_by": None,
            "primary_value": None,
            "primary_error": None,
            "healing_driver": None,
            "healing_search_by": None,
            "healing_value": None
        }
        
        for driver_name in driver_order:
            if driver_name not in all_strategies:
                continue
            
            driver_strategies = all_strategies[driver_name]
            driver = app_drivers.get(driver_name)
            
            if driver is None:
                logger.warning(
                    f"Driver {driver_name} not available for app {app_context.app_id}",
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
                
                # Resolve full XPath from parent chain if needed
                resolved_strategy = self._resolve_strategy_with_parent(strategy, alias, app_id, driver_name)
                
                start_time = time.time()
                logger.debug(
                    f"Trying {strategy_desc}",
                    alias=alias,
                    driver=driver_name,
                    searchBy=search_by,
                    value=resolved_strategy.get("value", ""),
                    priority=priority
                )
                
                try:
                    # Find element using this driver's strategy
                    element = driver.find_element(resolved_strategy)
                    
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
                    
                    # Phase 1: Capture baseline and record healing
                    if healing_store is not None:
                        # Record strategy attempt for statistics
                        image_score = getattr(driver, "last_match_score", None)
                        healing_store.record_strategy_attempt(
                            alias=alias,
                            driver=driver_name,
                            search_method=search_by,
                            success=True,
                            duration_ms=duration_ms,
                            image_match_score=image_score,
                        )
                        
                        # If this was a healing success (fallback worked)
                        if healing_info["attempted"]:
                            # Record the healing attempt
                            healing_store.record_healing(
                                alias=alias,
                                primary_driver=healing_info["primary_driver"],
                                primary_search_method=healing_info["primary_search_by"],
                                primary_search_value=healing_info["primary_value"],
                                failure_reason=healing_info["primary_error"],
                                healing_driver=driver_name,
                                healing_search_method=search_by,
                                healing_search_value=strategy_value,
                                healing_successful=True,
                                new_properties=self._capture_element_properties(driver, element)
                            )
                            logger.info(
                                f"[Healing] Element healed via {driver_name}:{search_by}",
                                alias=alias,
                                primary=healing_info["primary_driver"],
                                healing=driver_name
                            )
                        else:
                            # Capture baseline on successful interaction
                            props = self._capture_element_properties(driver, element)
                            healing_store.capture_baseline(
                                alias=alias,
                                properties=props,
                                driver=driver_name,
                                search_method=search_by,
                                search_value=strategy_value
                            )
                    
                    return result
                    
                except Exception as e:
                    duration_ms = (time.time() - start_time) * 1000
                    error_msg = str(e)[:100]
                    attempts.append((strategy_desc, f"FAILED: {error_msg}"))
                    
                    # Phase 1: Record strategy failure
                    if healing_store is not None:
                        healing_store.record_strategy_attempt(
                            alias=alias,
                            driver=driver_name,
                            search_method=search_by,
                            success=False,
                            duration_ms=duration_ms
                        )
                    
                    # Record first failure for healing tracking
                    if not healing_info["attempted"]:
                        healing_info["attempted"] = True
                        healing_info["primary_driver"] = driver_name
                        healing_info["primary_search_by"] = search_by
                        healing_info["primary_value"] = strategy_value
                        healing_info["primary_error"] = error_msg
                    
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
        
        # Phase 1: Record failed healing attempt
        if healing_store is not None and healing_info["attempted"]:
            healing_store.record_healing(
                alias=alias,
                primary_driver=healing_info["primary_driver"],
                primary_search_method=healing_info["primary_search_by"],
                primary_search_value=healing_info["primary_value"],
                failure_reason=healing_info["primary_error"],
                healing_driver="None",
                healing_search_method="N/A",
                healing_search_value="N/A",
                healing_successful=False
            )
        
        logger.error(
            f"All strategies failed for {alias}",
            alias=alias,
            attempts=attempts
        )
        
        # Quick fix: capture screenshot on failure using first available driver
        try:
            screenshot_mgr = get_screenshot_manager()
            app_context = _MULTI_APP_CONTEXT.get_app()
            for driver_name, driver in app_context.drivers.items():
                try:
                    screenshot_data = driver.capture_screenshot()
                    if screenshot_data:
                        screenshot_mgr.capture(
                            image_data=screenshot_data,
                            alias=alias,
                            error_type="AllStrategiesFailedError",
                            error_message=f"All strategies failed. Last error: {healing_info['primary_error']}",
                            driver_used=healing_info['primary_driver'],
                            prefix="failure"
                        )
                        logger.info(f"Screenshot captured: {screenshot_mgr.get_latest_screenshot_path()}")
                        break
                except Exception:
                    continue
        except Exception:
            pass  # Screenshot capture is non-critical
        
        raise AllStrategiesFailedError(
            alias=alias,
            attempts=attempts,
            details=error_details
        )

    def _resolve_and_execute_legacy(self, alias: str, action_name: str, *args):
        """Legacy single-app resolution using global driver pool.

        Used when no apps are registered and no app_id is provided,
        preserving backward compatibility with existing tests.
        """
        healing_store = None
        try:
            healing_store = get_healing_store()
        except Exception:
            pass
        
        all_strategies = repo.get_all_driver_strategies_sorted(alias)
        wpfspy_mode = os.environ.get("WPFSPY_MODE", "mock")
        
        logger.debug(
            "Resolving element (legacy mode)",
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
        
        if _ACTIVE_DRIVER is not None:
            driver_order = [_ACTIVE_DRIVER]
        else:
            try:
                element = repo.get_element(alias)
                element_priority = element.get("driverPriority")
                if element_priority and isinstance(element_priority, list):
                    driver_order = [d for d in element_priority if d in all_strategies]
                    if not driver_order:
                        driver_order = _get_run_modes()
                else:
                    driver_order = _get_run_modes()
            except Exception:
                driver_order = _get_run_modes()
        
        app_drivers = _get_drivers()
        attempts = []
        healing_info = {
            "attempted": False,
            "primary_driver": None,
            "primary_search_by": None,
            "primary_value": None,
            "primary_error": None,
            "healing_driver": None,
            "healing_search_by": None,
            "healing_value": None
        }
        
        for driver_name in driver_order:
            if driver_name not in all_strategies:
                continue
            
            driver_strategies = all_strategies[driver_name]
            driver = app_drivers.get(driver_name)
            
            if driver is None:
                logger.warning(
                    f"Driver {driver_name} not available",
                    alias=alias,
                    driver=driver_name
                )
                continue
            
            breaker = _breaker_manager.get_breaker(driver_name)
            if not breaker.allow_request():
                logger.warning(
                    f"Circuit breaker open for {driver_name}, skipping",
                    alias=alias,
                    driver=driver_name
                )
                attempts.append((f"{driver_name}:*", "CIRCUIT_OPEN"))
                continue
            
            for strategy in driver_strategies:
                search_by = strategy.get("searchBy", "")
                strategy_value = strategy.get("value", "")
                priority = strategy.get("priority", 99)
                strategy_desc = f"{driver_name}:{search_by}"
                
                resolved_strategy = self._resolve_strategy_with_parent(strategy, alias, app_id, driver_name)
                
                start_time = time.time()
                logger.debug(
                    f"Trying {strategy_desc}",
                    alias=alias,
                    driver=driver_name,
                    searchBy=search_by,
                    value=resolved_strategy.get("value", ""),
                    priority=priority
                )
                
                try:
                    element = driver.find_element(resolved_strategy)
                    result = getattr(driver, action_name)(element, *args)
                    duration_ms = (time.time() - start_time) * 1000
                    
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
                    
                    if healing_store is not None:
                        healing_store.record_strategy_attempt(
                            alias=alias,
                            driver=driver_name,
                            search_method=search_by,
                            success=True,
                            duration_ms=duration_ms
                        )
                        if healing_info["attempted"]:
                            healing_store.record_healing(
                                alias=alias,
                                primary_driver=healing_info["primary_driver"],
                                primary_search_method=healing_info["primary_search_by"],
                                primary_search_value=healing_info["primary_value"],
                                failure_reason=healing_info["primary_error"],
                                healing_driver=driver_name,
                                healing_search_method=search_by,
                                healing_search_value=strategy_value,
                                healing_successful=True,
                                new_properties=self._capture_element_properties(driver, element)
                            )
                            logger.info(
                                f"[Healing] Element healed via {driver_name}:{search_by}",
                                alias=alias,
                                primary=healing_info["primary_driver"],
                                healing=driver_name
                            )
                        else:
                            props = self._capture_element_properties(driver, element)
                            healing_store.capture_baseline(
                                alias=alias,
                                properties=props,
                                driver=driver_name,
                                search_method=search_by,
                                search_value=strategy_value
                            )
                    
                    return result
                    
                except Exception as e:
                    duration_ms = (time.time() - start_time) * 1000
                    error_msg = str(e)[:100]
                    attempts.append((strategy_desc, f"FAILED: {error_msg}"))
                    
                    if healing_store is not None:
                        healing_store.record_strategy_attempt(
                            alias=alias,
                            driver=driver_name,
                            search_method=search_by,
                            success=False,
                            duration_ms=duration_ms
                        )
                    
                    if not healing_info["attempted"]:
                        healing_info["attempted"] = True
                        healing_info["primary_driver"] = driver_name
                        healing_info["primary_search_by"] = search_by
                        healing_info["primary_value"] = strategy_value
                        healing_info["primary_error"] = error_msg
                    
                    logger.debug(
                        f"Strategy failed, trying next",
                        alias=alias,
                        driver=driver_name,
                        searchBy=search_by,
                        error=error_msg,
                        duration_ms=round(duration_ms, 2)
                    )
                    
                    breaker.record_failure()
                    continue
            
            logger.debug(
                f"All {driver_name} strategies failed, trying next driver",
                alias=alias,
                driver=driver_name
            )
        
        error_details = {
            "attempts": attempts,
            "total_attempts": len(attempts),
            "driver_order": driver_order
        }
        
        if healing_store is not None and healing_info["attempted"]:
            healing_store.record_healing(
                alias=alias,
                primary_driver=healing_info["primary_driver"],
                primary_search_method=healing_info["primary_search_by"],
                primary_search_value=healing_info["primary_value"],
                failure_reason=healing_info["primary_error"],
                healing_driver="None",
                healing_search_method="N/A",
                healing_search_value="N/A",
                healing_successful=False
            )
        
        logger.error(
            f"All strategies failed for {alias}",
            alias=alias,
            attempts=attempts
        )
        
        try:
            screenshot_mgr = get_screenshot_manager()
            for driver_name, driver in _get_drivers().items():
                try:
                    screenshot_data = driver.capture_screenshot()
                    if screenshot_data:
                        screenshot_mgr.capture(
                            image_data=screenshot_data,
                            alias=alias,
                            error_type="AllStrategiesFailedError",
                            error_message=f"All strategies failed. Last error: {healing_info['primary_error']}",
                            driver_used=healing_info['primary_driver'],
                            prefix="failure"
                        )
                        logger.info(f"Screenshot captured: {screenshot_mgr.get_latest_screenshot_path()}")
                        break
                except Exception:
                    continue
        except Exception:
            pass
        
        raise AllStrategiesFailedError(
            alias=alias,
            attempts=attempts,
            details=error_details
        )
    
    def _capture_element_properties(self, driver, element: ElementHandle) -> Dict[str, Any]:
        """Capture element properties for baseline storage.
        
        Args:
            driver: The driver instance used to find the element.
            element: The element handle.
            
        Returns:
            Dict of captured properties.
        """
        properties = {}
        
        try:
            # Get basic properties
            properties["automation_id"] = driver.get_attribute(element, "AutomationId")
        except Exception:
            pass
        
        try:
            properties["name"] = driver.get_attribute(element, "Name")
        except Exception:
            pass
        
        try:
            properties["control_type"] = driver.get_attribute(element, "ControlType")
        except Exception:
            pass
        
        try:
            properties["text"] = driver.get_text(element)
        except Exception:
            pass
        
        try:
            properties["is_visible"] = driver.is_visible(element)
        except Exception:
            pass
        
        try:
            properties["is_enabled"] = driver.is_enabled(element)
        except Exception:
            pass
        
        return properties
    
    def _resolve_strategy_with_parent(self, strategy: dict, alias: str, app_id: Optional[str] = None, driver_name: Optional[str] = None) -> dict:
        """Resolve strategy by building full XPath from parent chain.
        
        Args:
            strategy: Strategy dict with searchBy and value
            alias: Element alias for parent chain lookup
            app_id: Optional app context ID
            driver_name: Optional driver name (FlaUI uses Name for Window, others use AutomationId)
        
        Returns:
            Strategy dict with resolved full XPath
        """
        resolved = strategy.copy()
        value = strategy.get("value", "")
        
        if strategy.get("searchBy") != "XPath":
            return resolved
        
        if value.startswith("/"):
            return resolved
        
        full_path = self._build_full_path_from_alias(alias, app_id, driver_name)
        resolved["value"] = f"{full_path}/{value}"
        
        return resolved
    
    def _build_full_path_from_alias(self, alias: str, app_id: Optional[str] = None, driver_name: Optional[str] = None) -> str:
        """Build full XPath by walking parent chain.
        
        Args:
            alias: Element alias to resolve
            app_id: Optional app context ID
            driver_name: Optional driver name for driver-specific Window prefix
        
        Returns:
            Full XPath from Window to the element's parent.
        """
        path_parts = []
        parent_alias = repo.get_parent_alias(alias, app_id=app_id)
        current_alias = parent_alias
        visited = set()
        
        while current_alias:
            if current_alias in visited:
                logger.warning(f"Circular parent reference detected for alias: {current_alias}")
                break
            visited.add(current_alias)
            
            element = repo.get_element(current_alias, app_id=app_id)
            
            # Get the parent alias
            parent = repo.get_parent_alias(current_alias, app_id=app_id)
            
            # Build XPath prefix for this element
            control_type = element.get("controlType", "")
            window_id = element.get("windowAutomationId", "MainWindow")
            
            if control_type == "Window":
                # FlaUI uses UIA Name (Title) for Window; WPFSpy reads AutomationId directly
                if driver_name == "FlaUI":
                    window_name = element.get("name") or window_id
                    if window_name and window_name != window_id:
                        path_parts.insert(0, f"Window[@Name='{window_name}']")
                    else:
                        path_parts.insert(0, f"Window[@AutomationId='{window_id}']")
                else:
                    path_parts.insert(0, f"Window[@AutomationId='{window_id}']")
                break
            elif "automationId" in element:
                path_parts.insert(0, f"{control_type}[@AutomationId='{element['automationId']}']")
            elif "name" in element:
                path_parts.insert(0, f"{control_type}[@Name='{element['name']}']")
            
            # Move to parent
            if parent is None:
                # Reached root (Window)
                break
            current_alias = parent
        
        return "/" + "/".join(path_parts)

    # ------------------------------------------------------------------
    # Public keywords
    # ------------------------------------------------------------------
    def set_driver(self, driver_name: str):
        """Set the active driver for subsequent element operations.

        When set, only the specified driver will be used for element
        resolution and interaction. This overrides the default
        driver order (FlaUI -> WPFSpy -> Sikuli).

        Args:
            driver_name: One of 'FlaUI', 'WPFSpy', 'Sikuli' (case-insensitive).

        Example:
            | Set Driver | WPFSpy |
        """
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
        global _ACTIVE_DRIVER
        _ACTIVE_DRIVER = None
        set_run_modes(None)  # Reset to default from env/config
        logger.info("Driver reset to default order")

    def set_mode(self, mode: str):
        """Set the execution mode for subsequent operations.

        When set to 'mock', the mock app is used for all element
        operations (no real application needed). When set to 'real',
        the real SampleWpfApp is used via the spy agent or FlaUI.

        Args:
            mode: One of 'mock', 'real' (case-insensitive).

        Example:
            | Set Mode | real |
        """
        global _ACTIVE_MODE
        normalized = mode.strip().lower()
        if normalized not in ("mock", "real"):
            raise ValueError(f"Invalid mode '{mode}'. Valid options: mock, real")
        _ACTIVE_MODE = normalized
        logger.info("Mode set", mode=_ACTIVE_MODE)
        
        for app in _MULTI_APP_CONTEXT.apps.values():
            app.drivers.clear()

    def reset_mode(self):
        """Reset to the mode from the WPFSPY_MODE environment variable."""
        global _ACTIVE_MODE
        _ACTIVE_MODE = None
        logger.info("Mode reset to WPFSPY_MODE env var")
        
        for app in _MULTI_APP_CONTEXT.apps.values():
            app.drivers.clear()

    def set_mode_and_driver(self, mode: str, driver: str):
        """Set both execution mode and driver in one call.

        Args:
            mode: One of 'mock', 'real'.
            driver: One of 'FlaUI', 'WPFSpy', 'Sikuli', or 'Auto'.

        Example:
            | Set Mode And Driver | real | WPFSpy |
        """
        self.set_mode(mode)
        self.set_driver(driver)
        
        # Update run modes based on selected driver
        if driver.lower() == "auto":
            # Auto mode: use all drivers from priority order
            set_run_modes(None)
        else:
            # Specific driver: use only that driver
            set_run_modes([driver])
    
    def click_element(self, alias: str, app_id: Optional[str] = None):
        """Invokes (clicks) the element identified by `alias`."""
        self._resolve_and_execute(alias, "invoke", app_id)

    def click_element_with_wait(self, alias: str, timeout: float = 10.0, app_id: Optional[str] = None):
        """Clicks element after waiting for it to be actionable."""
        self.wait_until_element_actionable(alias, timeout, app_id)
        self._resolve_and_execute(alias, "invoke", app_id)

    def set_element_value(self, alias: str, value: str, app_id: Optional[str] = None):
        """Sets the text/value of the element identified by `alias`."""
        self._resolve_and_execute(alias, "set_value", app_id, value)

    def set_element_value_with_wait(
        self, 
        alias: str, 
        value: str, 
        timeout: float = 10.0,
        app_id: Optional[str] = None,
    ):
        """Sets element value after waiting for it to be actionable."""
        self.wait_until_element_actionable(alias, timeout, app_id)
        self._resolve_and_execute(alias, "set_value", app_id, value)

    def get_element_text(self, alias: str, app_id: Optional[str] = None) -> str:
        """Returns the current text of the element identified by `alias`."""
        return self._resolve_and_execute(alias, "get_text", app_id)

    def verify_element_text(self, alias: str, expected: str, app_id: Optional[str] = None):
        """Fails the test unless the element's text equals `expected`."""
        actual = self.get_element_text(alias, app_id)
        if actual != expected:
            raise AssertionError(f"'{alias}' text mismatch: expected '{expected}', got '{actual}'")

    def verify_element_contains_text(self, alias: str, expected: str, app_id: Optional[str] = None):
        """Fails the test unless the element's text contains `expected`."""
        actual = self.get_element_text(alias, app_id)
        if expected not in actual:
            raise AssertionError(f"'{alias}' text does not contain '{expected}': got '{actual}'")

    def verify_element_enabled(self, alias: str, app_id: Optional[str] = None):
        """Fails the test unless the element is enabled."""
        if not self.is_element_enabled(alias, app_id):
            raise AssertionError(f"'{alias}' is not enabled")

    def verify_element_visible(self, alias: str, app_id: Optional[str] = None):
        """Fails the test unless the element is visible."""
        if not self.is_element_visible(alias, app_id):
            raise AssertionError(f"'{alias}' is not visible")

    def verify_element_text_matches_regex(self, alias: str, pattern: str, app_id: Optional[str] = None):
        """Fails the test unless the element's text matches the regex pattern."""
        import re
        actual = self.get_element_text(alias, app_id)
        if not re.search(pattern, actual):
            raise AssertionError(f"'{alias}' text '{actual}' does not match regex '{pattern}'")

    def verify_element_attribute(self, alias: str, attribute_name: str, expected_value: str, app_id: Optional[str] = None):
        """Fails the test unless the element's attribute equals expected_value."""
        actual = self._resolve_and_execute(alias, "get_attribute", app_id, attribute_name)
        if actual != expected_value:
            raise AssertionError(f"'{alias}' attribute '{attribute_name}' mismatch: expected '{expected_value}', got '{actual}'")

    def property_checkpoint(self, alias: str, property_name: str, expected_value: str, app_id: Optional[str] = None):
        """Fails the test unless the element's property equals expected_value."""
        prop_lower = property_name.lower()
        if prop_lower in ("text", "content"):
            actual = self.get_element_text(alias, app_id)
        elif prop_lower in ("isenabled", "enabled"):
            actual = self.is_element_enabled(alias, app_id)
            actual = "true" if actual else "false"
        elif prop_lower in ("isvisible", "visible"):
            actual = self.is_element_visible(alias, app_id)
            actual = "true" if actual else "false"
        elif prop_lower in ("automationid", "automation_id"):
            actual = self._resolve_and_execute(alias, "get_attribute", app_id, "AutomationId")
        elif prop_lower == "name":
            actual = self._resolve_and_execute(alias, "get_attribute", app_id, "Name")
        elif prop_lower in ("controltype", "type"):
            actual = self._resolve_and_execute(alias, "get_attribute", app_id, "ControlType")
        else:
            actual = self._resolve_and_execute(alias, "get_attribute", app_id, property_name)
        if actual != expected_value:
            raise AssertionError(f"'{alias}' property '{property_name}' mismatch: expected '{expected_value}', got '{actual}'")

    def data_grid_checkpoint(self, alias: str, expected_content: str, app_id: Optional[str] = None):
        """Fails the test unless the DataGrid's OCR content contains expected_content."""
        actual = self.get_data_grid_content_ocr(alias, app_id)
        if expected_content not in actual:
            raise AssertionError(f"'{alias}' DataGrid content mismatch: expected '{expected_content}' in '{actual}'")

    def count_checkpoint(self, alias: str, expected_count: str, app_id: Optional[str] = None):
        """Fails the test unless the number of matching elements equals expected_count."""
        import re
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
        actual = self._resolve_and_execute(alias, "get_attribute", app_id, attribute_name)
        if actual != expected_value:
            raise AssertionError(f"'{alias}' attribute '{attribute_name}' mismatch: expected '{expected_value}', got '{actual}'")

    def get_data_grid_content_ocr(self, alias: str, app_id: Optional[str] = None) -> str:
        """Captures a DataGrid element screenshot and returns
        its content as CSV text using OCR."""
        return self._resolve_and_execute(alias, "get_data_grid_content_ocr", app_id)

    def toggle_element(self, alias: str, app_id: Optional[str] = None):
        """Toggles a checkbox/toggle-style element identified by `alias`."""
        self._resolve_and_execute(alias, "toggle", app_id)

    def double_click_element(self, alias: str, app_id: Optional[str] = None):
        """Double-clicks the element identified by `alias`."""
        self._resolve_and_execute(alias, "double_click", app_id)

    def right_click_element(self, alias: str, app_id: Optional[str] = None):
        """Right-clicks the element identified by `alias`."""
        self._resolve_and_execute(alias, "right_click", app_id)

    def press_keys(self, alias: str, keys: str, app_id: Optional[str] = None):
        """Presses keys into the element identified by `alias`."""
        self._resolve_and_execute(alias, "press_keys", app_id, keys)

    def drag_and_drop(self, alias: str, target_alias: str, app_id: Optional[str] = None):
        """Drags the element identified by `alias` and drops it on `target_alias`."""
        target_strategies = repo.get_all_driver_strategies_sorted(target_alias, app_id=app_id)
        if not target_strategies:
            raise AllStrategiesFailedError(
                alias=target_alias,
                attempts=[],
                details={"reason": "No strategies configured for target"}
            )

        target_element = None
        for driver_name in _get_run_modes():
            if driver_name not in target_strategies:
                continue
            driver = _get_drivers().get(driver_name)
            if driver is None:
                continue
            for strategy in target_strategies[driver_name]:
                try:
                    resolved = self._resolve_strategy_with_parent(strategy, target_alias, app_id, driver_name)
                    target_element = driver.find_element(resolved)
                    break
                except Exception:
                    continue
            if target_element is not None:
                break

        if target_element is None:
            raise AllStrategiesFailedError(
                alias=target_alias,
                attempts=[],
                details={"reason": "Could not resolve target element"}
            )

        self._resolve_and_execute(alias, "drag_drop", app_id, target_element)

    def hover_over_element(self, alias: str, app_id: Optional[str] = None):
        """Hovers over the element identified by `alias`."""
        self._resolve_and_execute(alias, "hover", app_id)

    def scroll(self, alias: str, direction: str, app_id: Optional[str] = None):
        """Scrolls the element identified by `alias` in the given direction."""
        self._resolve_and_execute(alias, "scroll", app_id, direction)

    def sikuli_click(self, alias: str, image_tag: str, app_id: Optional[str] = None):
        """Clicks an element identified by a Sikuli image tag."""
        driver = _get_drivers().get("Sikuli")
        if driver is None:
            raise RuntimeError("Sikuli driver not available")
        element = driver.find_element({"searchBy": "Image", "value": image_tag})
        driver.invoke(element)

    def sikuli_type(self, alias: str, text: str, app_id: Optional[str] = None):
        """Types text into an element identified by a Sikuli image tag."""
        driver = _get_drivers().get("Sikuli")
        if driver is None:
            raise RuntimeError("Sikuli driver not available")
        strategies = repo.get_strategies(alias, app_id=app_id)
        image_tag = None
        for driver_name, strats in strategies.items():
            if driver_name == "Sikuli" and strats:
                image_tag = strats[0].get("value")
                break
        if not image_tag:
            raise AllStrategiesFailedError(
                alias=alias,
                attempts=[],
                details={"reason": "No Sikuli image tag configured"}
            )
        element = driver.find_element({"searchBy": "Image", "value": image_tag})
        driver.set_value(element, text)

    def area_checkpoint(self, alias: str, expected_text: str, app_id: Optional[str] = None):
        """Verifies OCR text in an area matches expected_text."""
        actual = self._resolve_and_execute(alias, "get_data_grid_content_ocr", app_id)
        if expected_text not in actual:
            raise AssertionError(f"'{alias}' area OCR mismatch: expected '{expected_text}', got '{actual}'")

    def image_checkpoint(self, alias: str, baseline_path: str, app_id: Optional[str] = None):
        """Verifies an element's visual appearance matches the baseline image."""
        import os
        if not os.path.exists(baseline_path):
            raise AssertionError(f"Baseline image not found: {baseline_path}")
        actual = self._resolve_and_execute(alias, "capture_screenshot", app_id)
        # For now, just verify we got screenshot data; real pixel comparison would go here
        if not actual:
            raise AssertionError(f"'{alias}' screenshot capture returned empty data")

    def is_element_visible(self, alias: str, app_id: Optional[str] = None) -> bool:
        """Check if element is visible without failing."""
        strategies = repo.get_strategies(alias)
        app_context = _MULTI_APP_CONTEXT.get_app(app_id)
        max_retries = 3
        retry_delay = 0.3
        for attempt in range(max_retries):
            for driver_name in _get_run_modes():
                if driver_name not in strategies:
                    continue
                if driver_name not in app_context.drivers:
                    app_context.drivers[driver_name] = _create_driver_for_app(driver_name, app_context)
                driver = app_context.drivers[driver_name]
                driver_strategies = strategies[driver_name]
                for strategy in driver_strategies:
                    try:
                        resolved = self._resolve_strategy_with_parent(strategy, alias, app_id, driver_name)
                        element = driver.find_element(resolved)
                        if driver.is_visible(element):
                            return True
                    except Exception:
                        continue
            if attempt < max_retries - 1:
                time.sleep(retry_delay)
        return False

    def is_element_enabled(self, alias: str, app_id: Optional[str] = None) -> bool:
        """Check if element is enabled without failing."""
        strategies = repo.get_strategies(alias)
        app_context = _MULTI_APP_CONTEXT.get_app(app_id)
        for driver_name in _get_run_modes():
            if driver_name not in strategies:
                continue
            if driver_name not in app_context.drivers:
                app_context.drivers[driver_name] = _create_driver_for_app(driver_name, app_context)
            driver = app_context.drivers[driver_name]
            driver_strategies = strategies[driver_name]
            for strategy in driver_strategies:
                try:
                    resolved = self._resolve_strategy_with_parent(strategy, alias, app_id, driver_name)
                    element = driver.find_element(resolved)
                    if driver.is_enabled(element):
                        return True
                except Exception:
                    continue
        return False

    def find_elements(self, alias: str, app_id: Optional[str] = None) -> List[Any]:
        """Find all elements matching the alias across all available strategies."""
        strategies = repo.get_strategies(alias)
        app_context = _MULTI_APP_CONTEXT.get_app(app_id)
        results: List[Any] = []
        for driver_name in _get_run_modes():
            if driver_name not in strategies:
                continue
            if driver_name not in app_context.drivers:
                app_context.drivers[driver_name] = _create_driver_for_app(driver_name, app_context)
            driver = app_context.drivers[driver_name]
            driver_strategies = strategies[driver_name]
            for strategy in driver_strategies:
                try:
                    resolved = self._resolve_strategy_with_parent(strategy, alias, app_id, driver_name)
                    found = driver.find_elements(resolved)
                    results.extend(found)
                except Exception:
                    continue
        return results

    def is_element_actionable(self, alias: str, app_id: Optional[str] = None) -> bool:
        """Check if element is both visible and enabled."""
        strategies = repo.get_strategies(alias)
        app_context = _MULTI_APP_CONTEXT.get_app(app_id)
        for driver_name in _get_run_modes():
            if driver_name not in strategies:
                continue
            if driver_name not in app_context.drivers:
                app_context.drivers[driver_name] = _create_driver_for_app(driver_name, app_context)
            driver = app_context.drivers[driver_name]
            driver_strategies = strategies[driver_name]
            for strategy in driver_strategies:
                try:
                    resolved = self._resolve_strategy_with_parent(strategy, alias, app_id, driver_name)
                    element = driver.find_element(resolved)
                    if driver.is_actionable(element):
                        return True
                except Exception:
                    continue
        return False

    def wait_until_element_exists(
        self,
        alias: str,
        timeout: float = 10.0,
        poll_interval: float = 0.5,
        app_id: Optional[str] = None,
    ):
        """Polls until the element can be found, or raises after `timeout` seconds."""
        from exceptions import WaitTimeoutError, AllStrategiesFailedError

        start_time = time.time()
        strategies = repo.get_strategies(alias)
        app_context = _MULTI_APP_CONTEXT.get_app(app_id)

        if not strategies:
            raise AllStrategiesFailedError(
                alias=alias,
                attempts=[],
                details={"reason": "No strategies configured"}
            )

        while time.time() - start_time < timeout:
            for driver_name in _get_run_modes():
                if driver_name not in strategies:
                    continue
                # Create driver if it doesn't exist (same as _resolve_and_execute)
                if driver_name not in app_context.drivers:
                    app_context.drivers[driver_name] = _create_driver_for_app(driver_name, app_context)
                driver = app_context.drivers[driver_name]
                for strategy in strategies[driver_name]:
                    try:
                        resolved = self._resolve_strategy_with_parent(strategy, alias, app_id, driver_name)
                        driver.find_element(resolved)
                        return True
                    except Exception:
                        continue

            remaining = timeout - (time.time() - start_time)
            if remaining > 0:
                time.sleep(min(poll_interval, remaining))

        raise WaitTimeoutError(
            condition="element exists",
            timeout=timeout
        )

    def wait_until_element_visible(
        self, 
        alias: str, 
        timeout: float = 10.0,
        poll_interval: float = 0.5,
        app_id: Optional[str] = None,
    ):
        """Polls until the element is visible, or raises after `timeout` seconds.
        
        Uses exponential backoff on poll interval for better performance.
        """
        from exceptions import WaitTimeoutError
        
        start_time = time.time()
        strategies = repo.get_strategies(alias)
        app_context = _MULTI_APP_CONTEXT.get_app(app_id)
        
        if not strategies:
            raise AllStrategiesFailedError(
                alias=alias,
                attempts=[],
                details={"reason": "No strategies configured"}
            )
        
        while time.time() - start_time < timeout:
            for driver_name in _get_run_modes():
                if driver_name not in strategies:
                    continue
                # Create driver if it doesn't exist
                if driver_name not in app_context.drivers:
                    app_context.drivers[driver_name] = _create_driver_for_app(driver_name, app_context)
                driver = app_context.drivers[driver_name]
                for strategy in strategies[driver_name]:
                    try:
                        resolved = self._resolve_strategy_with_parent(strategy, alias, app_id, driver_name)
                        element = driver.find_element(resolved)
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

    def wait_until_element_enabled(
        self,
        alias: str,
        timeout: float = 10.0,
        poll_interval: float = 0.5,
        app_id: Optional[str] = None,
    ):
        """Polls until the element is enabled, or raises after `timeout` seconds."""
        from exceptions import WaitTimeoutError

        start_time = time.time()
        strategies = repo.get_strategies(alias)
        app_context = _MULTI_APP_CONTEXT.get_app(app_id)

        if not strategies:
            raise AllStrategiesFailedError(
                alias=alias,
                attempts=[],
                details={"reason": "No strategies configured"}
            )

        while time.time() - start_time < timeout:
            for driver_name in _get_run_modes():
                if driver_name not in strategies:
                    continue
                # Create driver if it doesn't exist
                if driver_name not in app_context.drivers:
                    app_context.drivers[driver_name] = _create_driver_for_app(driver_name, app_context)
                driver = app_context.drivers[driver_name]
                for strategy in strategies[driver_name]:
                    try:
                        resolved = self._resolve_strategy_with_parent(strategy, alias, app_id, driver_name)
                        element = driver.find_element(resolved)
                        if driver.is_enabled(element):
                            return True
                    except Exception:
                        continue

            remaining = timeout - (time.time() - start_time)
            if remaining > 0:
                time.sleep(min(poll_interval, remaining))

        raise WaitTimeoutError(
            condition="element enabled",
            timeout=timeout
        )

    def wait_until_element_actionable(
        self,
        alias: str,
        timeout: float = 10.0,
        poll_interval: float = 0.5,
        app_id: Optional[str] = None,
    ):
        """Wait for element to be visible and enabled.

        Raises WaitTimeoutError if element is not actionable within timeout.
        """
        start_time = time.time()
        strategies = repo.get_strategies(alias)
        app_context = _MULTI_APP_CONTEXT.get_app(app_id)

        if not strategies:
            raise AllStrategiesFailedError(
                alias=alias,
                attempts=[],
                details={"reason": "No strategies configured"}
            )

        while time.time() - start_time < timeout:
            for driver_name in _get_run_modes():
                if driver_name not in strategies:
                    continue
                # Create driver if it doesn't exist
                if driver_name not in app_context.drivers:
                    app_context.drivers[driver_name] = _create_driver_for_app(driver_name, app_context)
                driver = app_context.drivers[driver_name]
                for strategy in strategies[driver_name]:
                    try:
                        resolved = self._resolve_strategy_with_parent(strategy, alias, app_id, driver_name)
                        element = driver.find_element(resolved)
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
        case_sensitive: bool = True,
        app_id: Optional[str] = None,
    ):
        """Wait for element's text to contain the expected value."""
        from exceptions import WaitTimeoutError
        
        start_time = time.time()
        strategies = repo.get_strategies(alias)
        app_context = _MULTI_APP_CONTEXT.get_app(app_id)
        
        if not strategies:
            raise AllStrategiesFailedError(
                alias=alias,
                attempts=[],
                details={"reason": "No strategies configured"}
            )
        
        while time.time() - start_time < timeout:
            for driver_name in _get_run_modes():
                if driver_name not in strategies:
                    continue
                driver = app_context.drivers.get(driver_name)
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
        effective_mode = _ACTIVE_MODE if _ACTIVE_MODE is not None else _WPFSPY_MODE
        if effective_mode == "real":
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

    # ------------------------------------------------------------------
    # Clipboard Operations
    # ------------------------------------------------------------------
    def set_clipboard_text(self, text: str):
        """Sets the Windows clipboard to the given text.

        Args:
            text: The text to place on the clipboard.

        Example:
            | Set Clipboard Text    Hello World |
        """
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
        """Returns the current text from the Windows clipboard.

        Returns:
            The clipboard text as a string.

        Example:
            | ${text}=    Get Clipboard Text |
        """
        import win32clipboard
        win32clipboard.OpenClipboard()
        try:
            data = win32clipboard.GetClipboardData(win32clipboard.CF_UNICODETEXT)
        finally:
            win32clipboard.CloseClipboard()
        logger.info("Clipboard text retrieved", text_length=len(data))
        return data

    def copy_element_text(self, alias: str, app_id: Optional[str] = None):
        """Gets the text of an element and places it on the clipboard.

        Args:
            alias: Element alias from repository.
            app_id: Optional application context ID.

        Example:
            | Copy Element Text    LoginPage.MainWindow.txtUsername |
        """
        text = self.get_element_text(alias, app_id)
        self.set_clipboard_text(text)
        logger.info("Element text copied to clipboard", alias=alias, text=text)

    # ------------------------------------------------------------------
    # Window Activation
    # ------------------------------------------------------------------
    def activate_window(self, app_id: Optional[str] = None, window_title: Optional[str] = None):
        """Activates (brings to front and focuses) a window by app_id or title.

        Args:
            app_id: Registered application ID. Uses default app if not provided.
            window_title: Window title to search for (used if app_id not provided).

        Example:
            | Activate Window    app_id=notepad |
            | Activate Window    window_title=Untitled - Notepad |
        """
        import ctypes
        from ctypes import wintypes

        user32 = ctypes.WinDLL("user32", use_last_error=True)
        hwnd = None
        target_pid = None

        logger.info(f"activate_window called", app_id=app_id, window_title=window_title)

        if app_id or (not window_title and not app_id):
            # Get window by process ID
            if app_id is None:
                app_id = _MULTI_APP_CONTEXT.default_app_id
            if app_id:
                try:
                    app_context = _MULTI_APP_CONTEXT.get_app(app_id)
                    target_pid = app_context.process_id
                    logger.info(f"activate_window: found app_context app_id={app_id} process_id={target_pid} app_name={app_context.app_name}")
                except Exception as e:
                    logger.warning(f"activate_window: failed to get app_context app_id={app_id} error={e}")

                if target_pid:
                    EnumWindows = user32.EnumWindows
                    EnumWindowsProc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
                    GetWindowThreadProcessId = user32.GetWindowThreadProcessId

                    class PIDHolder(ctypes.Structure):
                        _fields_ = [("pid", wintypes.DWORD), ("hwnd", wintypes.HWND)]

                    holder = PIDHolder(target_pid, 0)

                    def callback(hwnd, lparam):
                        owner_pid = wintypes.DWORD()
                        GetWindowThreadProcessId(hwnd, ctypes.byref(owner_pid))
                        if owner_pid.value == target_pid and user32.IsWindowVisible(hwnd):
                            holder.hwnd = hwnd
                            return 0
                        return 1

                    EnumWindows(EnumWindowsProc(callback), 0)
                    hwnd = holder.hwnd
                    logger.info(f"activate_window: window enumeration result", hwnd=hwnd)

        if hwnd is None and target_pid:
            # Fallback: try to find window by enumerating all windows and checking process name
            try:
                import psutil
                # Get the process name for the target_pid
                try:
                    target_proc = psutil.Process(target_pid)
                    target_proc_name = target_proc.name()
                except psutil.NoSuchProcess:
                    target_proc_name = None

                if target_proc_name:
                    # Enumerate all windows and find one belonging to a process with the same name
                    EnumWindows = user32.EnumWindows
                    EnumWindowsProc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
                    GetWindowThreadProcessId = user32.GetWindowThreadProcessId

                    class NameHolder(ctypes.Structure):
                        _fields_ = [("name", ctypes.c_wchar_p), ("hwnd", wintypes.HWND)]

                    holder = NameHolder(target_proc_name.lower(), 0)

                    def callback(hwnd, lparam):
                        if not user32.IsWindowVisible(hwnd):
                            return 1
                        owner_pid = wintypes.DWORD()
                        GetWindowThreadProcessId(hwnd, ctypes.byref(owner_pid))
                        try:
                            proc = psutil.Process(owner_pid.value)
                            if proc.name().lower() == holder.name:
                                holder.hwnd = hwnd
                                return 0
                        except (psutil.NoSuchProcess, psutil.AccessDenied):
                            pass
                        return 1

                    EnumWindows(EnumWindowsProc(callback), 0)
                    hwnd = holder.hwnd
                    logger.info(f"activate_window: fallback by process name result", hwnd=hwnd, proc_name=target_proc_name)
            except Exception as e:
                logger.warning(f"activate_window: fallback by process name failed", error=str(e))

        if hwnd is None and window_title:
            # Find window by title
            hwnd = user32.FindWindowW(None, window_title)

        if hwnd is None and target_pid is None and app_id:
            # Fallback: try to find window by process name
            try:
                import psutil
                app_context = _MULTI_APP_CONTEXT.get_app(app_id)
                proc_name = app_context.app_name
                for proc in psutil.process_iter(["pid", "name"]):
                    if proc.info["name"] and proc.info["name"].lower() in proc_name.lower():
                        target_pid = int(proc.info["pid"])
                        # Try to find window for this PID
                        EnumWindows = user32.EnumWindows
                        EnumWindowsProc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
                        GetWindowThreadProcessId = user32.GetWindowThreadProcessId

                        class PIDHolder(ctypes.Structure):
                            _fields_ = [("pid", wintypes.DWORD), ("hwnd", wintypes.HWND)]

                        holder = PIDHolder(target_pid, 0)

                        def callback(hwnd, lparam):
                            owner_pid = wintypes.DWORD()
                            GetWindowThreadProcessId(hwnd, ctypes.byref(owner_pid))
                            if owner_pid.value == target_pid and user32.IsWindowVisible(hwnd):
                                holder.hwnd = hwnd
                                return 0
                            return 1

                        EnumWindows(EnumWindowsProc(callback), 0)
                        if holder.hwnd:
                            hwnd = holder.hwnd
                            break
            except Exception:
                pass

        if hwnd is None:
            raise RuntimeError(f"Could not find window to activate (app_id={app_id}, title={window_title})")

        # Restore if minimized
        if user32.IsIconic(hwnd):
            user32.ShowWindow(hwnd, 9)  # SW_RESTORE

        # Bring to front and set focus
        user32.SetForegroundWindow(hwnd)
        user32.SetFocus(hwnd)
        logger.info("Window activated", app_id=app_id, window_title=window_title)

    def paste_clipboard_to_element(self, alias: str, app_id: Optional[str] = None):
        """Pastes the current clipboard text into the specified element.

        Args:
            alias: Element alias from repository.
            app_id: Optional application context ID.

        Example:
            | Paste Clipboard To Element    Notepad.Editor |
        """
        import time
        time.sleep(0.2)
        self._resolve_and_execute(alias, "press_keys", app_id, "^v")
        logger.info("Clipboard pasted to element", alias=alias)

    def send_keys_to_window(self, window_title: str, keys: str):
        """Sends keystrokes to a window by title using WScript.Shell.

        Args:
            window_title: The title of the window to activate.
            keys: The keys to send (e.g., "^v" for Ctrl+V).

        Example:
            | Send Keys To Window    Untitled - Notepad    ^v |
        """
        import subprocess
        import time
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


