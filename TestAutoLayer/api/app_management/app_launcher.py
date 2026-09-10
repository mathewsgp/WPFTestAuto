"""
App Launcher
============
Handles application launching and attachment for multi-app automation.
"""

import os
import time
import subprocess
from pathlib import Path
from typing import List, Optional, Dict, Any

from .app_registry import (
    AppContext,
    Constants,
    get_multi_app_context,
    _is_net_framework_app,
    _stage_framework_dlls,
)
from TestAutoLayer.api.logging_utils import get_api_logger

logger = get_api_logger()


def create_driver_for_app(driver_name: str, app_context: AppContext) -> Any:
    """Create a driver instance for a specific app context.
    
    This function replaces the old _create_driver_for_app and handles
    the current mode (real/mock) for WPFSpy.
    """
    # Import _ACTIVE_MODE lazily to avoid circular imports
    try:
        from DriverAgnosticApi import _ACTIVE_MODE
        effective_mode = _ACTIVE_MODE if _ACTIVE_MODE is not None else os.environ.get("WPFSPY_MODE", "mock").lower()
    except ImportError:
        effective_mode = os.environ.get("WPFSPY_MODE", "mock").lower()

    if driver_name == "FlaUI":
        try:
            from flaui_driver import FlaUIDriver
            return FlaUIDriver(app_pid=app_context.process_id)
        except ImportError:
            raise ImportError("FlaUI driver not available. Install: pip install robotframework-flaui")

    if driver_name == "WPFSpy":
        if effective_mode == "real":
            if app_context.pipe_name is None:
                try:
                    from WPFSpyLibrary import WPFSpyMockDriver
                    return WPFSpyMockDriver()
                except ImportError:
                    raise ImportError("WPFSpy mock driver not available")
            try:
                from WPFSpyLibrary import WPFSpyRealDriver
                return WPFSpyRealDriver(pipe_name=app_context.pipe_name)
            except ImportError:
                raise ImportError("WPFSpy driver not available")
        else:
            try:
                from WPFSpyLibrary import WPFSpyMockDriver
                return WPFSpyMockDriver()
            except ImportError:
                raise ImportError("WPFSpy mock driver not available")

    if driver_name == "Sikuli":
        try:
            from SikuliLibrary import SikuliDriver
            return SikuliDriver()
        except ImportError:
            raise ImportError("Sikuli driver not available. Install: pip install robotframework-sikuli")

    raise ValueError(f"Unknown driver: {driver_name}")


def launch_app_for_context(app_context: AppContext) -> subprocess.Popen:
    """Launch an application with the appropriate environment for WPFSpy injection."""
    print(f"[LAUNCH] ENTRY: app_id={app_context.app_id}, driver_list={app_context.driver_list}, inject_spy_agent={app_context.inject_spy_agent}, pipe_name={app_context.pipe_name}")
    if not app_context.app_path:
        raise ValueError("app_path is required to launch application")

    env = os.environ.copy()
    env.update(app_context.env)

    target_dir = Path(app_context.app_path).parent
    is_framework = _is_net_framework_app(app_context.app_path)

    if app_context.inject_spy_agent:
        if is_framework:
            print(f"[LAUNCH] Detected .NET Framework app, using AppDomainManager injection")
            staged = _stage_framework_dlls(app_context.app_path)
            print(f"[LAUNCH] staged_framework_dlls={staged}")

            env["APPDOMAIN_MANAGER_ASM"] = "WpfSpyAgent.FrameworkHook"
            env["APPDOMAIN_MANAGER_TYPE"] = "WpfSpyAgent.FrameworkHook.SpyAppDomainManager"
            env["WPFSPY_PIPE_NAME"] = app_context.pipe_name or "WPFSpyAgentPipe"
            env["WPFSPY_AGENT_ENABLED"] = "1"
            print(f"[LAUNCH] Set APPDOMAIN_MANAGER_ASM=WpfSpyAgent.FrameworkHook")
            print(f"[LAUNCH] Set WPFSPY_PIPE_NAME={env['WPFSPY_PIPE_NAME']}")
        else:
            from TestAutoLayer.api.runtime_injector import RuntimeInjector
            injector = RuntimeInjector()
            print(f"[LAUNCH] inject_spy_agent=True, pipe_name={app_context.pipe_name}")
            print(f"[LAUNCH] startup_hook_path={injector.startup_hook_path}")
            print(f"[LAUNCH] target_dir={target_dir}")
            
            if injector.startup_hook_path:
                staged = injector.stage_dlls(app_context.app_path)
                print(f"[LAUNCH] staged_dlls={staged}")
                
                if staged:
                    staged_hook = target_dir / "WpfSpyAgent.StartupHook.dll"
                    if staged_hook.exists():
                        env["DOTNET_STARTUP_HOOKS"] = str(staged_hook)
                        print(f"[LAUNCH] Using staged hook: {staged_hook}")
                    else:
                        env["DOTNET_STARTUP_HOOKS"] = injector.startup_hook_path
                        print(f"[LAUNCH] Staged hook missing, using build output: {injector.startup_hook_path}")
                else:
                    staged_hook = target_dir / "WpfSpyAgent.StartupHook.dll"
                    if staged_hook.exists():
                        env["DOTNET_STARTUP_HOOKS"] = str(staged_hook)
                        print(f"[LAUNCH] Using existing staged hook: {staged_hook}")
                    else:
                        env["DOTNET_STARTUP_HOOKS"] = injector.startup_hook_path
                        print(f"[LAUNCH] No staged hook, using build output: {injector.startup_hook_path}")
                
                env["WPFSPY_AGENT_ENABLED"] = "1"
                env["WPFSPY_PIPE_NAME"] = app_context.pipe_name or "WPFSpyAgentPipe"
                print(f"[LAUNCH] WPFSPY_PIPE_NAME={env['WPFSPY_PIPE_NAME']}")
            else:
                print("[LAUNCH] WARNING: startup_hook_path is None - cannot inject spy agent")

    cmd = [app_context.app_path] + app_context.launch_args
    if not app_context.app_path.lower().endswith(".exe"):
        cmd = ["dotnet"] + cmd

    print(f"[LAUNCH] cmd={cmd}")
    print(f"[LAUNCH] cwd={app_context.start_in or target_dir}")
    
    proc = subprocess.Popen(
        cmd,
        env=env,
        cwd=app_context.start_in or None,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    print(f"[LAUNCH] Launched PID={proc.pid}")
    time.sleep(5)
    return proc


def launch_application(
    app_path: str,
    app_id: Optional[str] = None,
    args: Optional[List[str]] = None,
    start_in: Optional[str] = None,
    drivers: Optional[List[str]] = None,
    attach: bool = False,
    timeout: float = 30.0,
) -> str:
    """Launch an application and register it in the multi-app context.
    
    Args:
        app_path: Path to executable/DLL.
        app_id: Logical ID for this app. Optional; defaults to executable name.
        args: Command-line arguments.
        start_in: Working directory for the launched process.
        drivers: Ordered list of drivers to enable for this app.
        attach: If True, wait for the spy agent to become ready after launch.
        timeout: Seconds to wait for the process to become available.
        
    Returns:
        The registered app_id.
    """
    from .app_registry import get_multi_app_context, AppContext
    
    # Normalize args
    if args is None:
        launch_args: List[str] = []
    elif isinstance(args, str):
        import shlex
        try:
            launch_args = shlex.split(args, posix=False)
        except ValueError:
            launch_args = [args]
    else:
        launch_args = list(args)

    # Normalize drivers
    if drivers is None:
        drivers = None
    elif isinstance(drivers, str):
        drivers = [d.strip() for d in drivers.split(",") if d.strip()]
    else:
        drivers = [str(d).strip() for d in drivers if str(d).strip()]

    # Default app_id to the executable name
    if not app_id:
        base = os.path.basename(app_path)
        stem = os.path.splitext(base)[0]
        app_id = (stem or "app").lower()

    # Auto-detect drivers when not specified
    if drivers is None:
        norm_for_detect = (app_path or "").replace("/", "\\").lower()
        base_for_detect = os.path.basename(norm_for_detect)
        is_likely_wpf = (
            base_for_detect.endswith(".dll")
            or "wpf" in base_for_detect
            or "xaml" in base_for_detect
        )
        drivers = ["WPFSpy"] if is_likely_wpf else ["FlaUI"]

    # Normalize path
    norm_path = (app_path or "").replace("/", "\\").strip()
    if "\n" in norm_path or "\r" in norm_path:
        raise ValueError(
            f"app_path contains a newline character (will break CreateProcess): {norm_path!r}"
        )
    if not norm_path:
        raise ValueError("app_path is required and cannot be empty")
    if not os.path.isabs(norm_path):
        norm_path = os.path.abspath(os.path.join(os.getcwd(), norm_path))
    norm_start_in = (start_in or "").replace("/", "\\").strip() or None
    if norm_start_in and not os.path.isabs(norm_start_in):
        norm_start_in = os.path.abspath(os.path.join(os.getcwd(), norm_start_in))
    if norm_start_in and ("\n" in norm_start_in or "\r" in norm_start_in):
        raise ValueError(
            f"start_in contains a newline character: {norm_start_in!r}"
        )

    app_context = AppContext(
        app_id=app_id,
        app_name=os.path.basename(norm_path),
        driver_list=drivers,
        app_path=norm_path,
        launch_args=launch_args,
        start_in=norm_start_in,
        attach=attach,
    )
    logger.info(
        "launch_application app_context created",
        app_id=app_id,
        drivers=drivers,
        pipe_name=app_context.pipe_name,
        attach=attach,
    )
    try:
        app_context.process = launch_app_for_context(app_context)
        if app_context.process.pid:
            app_context.process_id = app_context.process.pid
    except Exception as e:
        logger.error(f"Failed to launch application: {e}")
        raise

    get_multi_app_context().register_app(app_context)
    logger.info(
        "Launched application",
        app_id=app_id,
        app_path=app_path,
        pid=app_context.process_id,
        start_in=start_in,
        attach=attach,
    )

    # If auto-attach with spy agent, wait for agent readiness
    if attach and app_context.inject_spy_agent:
        try:
            wait_for_application(app_id, timeout=timeout)
        except Exception as e:
            logger.warning(
                f"Auto-attach wait for {app_id} did not confirm agent readiness: {e}"
            )

    return app_id


def attach_to_application(
    app_id: str,
    process_id: int,
    driver_list: Optional[List[str]] = None,
    pipe_name: Optional[str] = None,
) -> str:
    """Attach to a running application and register it.
    
    Args:
        app_id: Logical ID for this app.
        process_id: OS process ID.
        driver_list: Ordered list of drivers for this app. Default: ["FlaUI"].
        pipe_name: Named pipe for WPFSpy agent.
        
    Returns:
        The registered app_id.
    """
    from .app_registry import get_multi_app_context, AppContext
    
    if driver_list is None:
        driver_list = None
    elif isinstance(driver_list, str):
        driver_list = [d.strip() for d in driver_list.split(",") if d.strip()]
    else:
        driver_list = [str(d).strip() for d in driver_list if str(d).strip()]
    
    app_context = AppContext(
        app_id=app_id,
        app_name=f"Process-{process_id}" if process_id else app_id,
        driver_list=driver_list,
        process_id=process_id,
        pipe_name=pipe_name,
    )
    logger.info(
        "attach_to_application registered",
        app_id=app_id,
        process_id=process_id,
        driver_list=driver_list,
        pipe_name=pipe_name,
    )
    get_multi_app_context().register_app(app_context)
    logger.info("Attached to application", app_id=app_id, process_id=process_id)
    return app_id


def wait_for_application(app_id: str, timeout: float = 30.0, poll_interval: float = 1.0) -> bool:
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
    from .app_registry import get_multi_app_context
    
    start_time = time.time()
    while time.time() - start_time < timeout:
        try:
            app_context = get_multi_app_context().get_app(app_id)
            if app_context.process_id:
                return True
        except ValueError:
            pass
        
        time.sleep(poll_interval)
    
    raise TimeoutError(f"Application '{app_id}' not available within {timeout}s")


def close_application(app_id: str):
    """Close and unregister an application."""
    from .app_registry import get_multi_app_context
    
    get_multi_app_context().unregister_app(app_id)
    logger.info("Closed application", app_id=app_id)