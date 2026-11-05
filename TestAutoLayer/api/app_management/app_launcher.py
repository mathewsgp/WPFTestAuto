"""
App Launcher
============
Handles application launching and attachment for multi-app automation.
"""

import os
import time
import subprocess
import csv
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
    spy_agent_enabled: Optional[bool] = None,
    pipe_name: Optional[str] = None,
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
        pipe_name=pipe_name,
        inject_spy_agent=spy_agent_enabled,
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


def _detect_running_processes(skip_names: set) -> list:
    """Detect running processes that could be automation targets.

    Returns a list of dicts with 'pid' and 'name' keys. Duplicated here
    (mirrors DriverAgnosticApi._detect_running_processes) because
    app_launcher.py must not import DriverAgnosticApi (circular import).
    """
    processes = []

    # Try psutil first (most reliable)
    try:
        import psutil  # type: ignore
        for proc in psutil.process_iter(['pid', 'name']):
            try:
                info = proc.info
                name = info.get('name') or ''
                pid = info.get('pid')
                if not name or not pid:
                    continue
                if name.lower() in skip_names:
                    continue
                if not name.lower().endswith('.exe'):
                    continue
                processes.append({'pid': pid, 'name': name})
            except (psutil.NoSuchProcess, psutil.AccessDenied):
                continue
        return processes
    except ImportError:
        pass

    # Fallback: tasklist /v /fo csv
    try:
        import subprocess
        result = subprocess.run(
            ['tasklist', '/v', '/fo', 'csv'],
            capture_output=True, text=True, timeout=10,
        )
        for line in result.stdout.splitlines()[1:]:
            line = line.strip()
            if not line:
                continue
            parts = next(csv.reader([line]), None)
            if not parts or len(parts) < 2:
                continue
            name = parts[0].strip('" ')
            try:
                pid = int(parts[1].strip('" '))
            except (ValueError, TypeError):
                continue
            if not name or name.lower() in skip_names:
                continue
            if not name.lower().endswith('.exe'):
                continue
            processes.append({'pid': pid, 'name': name})
    except Exception:
        pass

    return processes


def _is_spy_pipe_alive(pipe_name: str) -> bool:
    """Check if the WPFSpy agent is alive on the named pipe."""
    try:
        import win32file  # type: ignore
        pipe_path = r"\\.\pipe\\" + pipe_name
        win32file.CreateFile(
            pipe_path,
            win32file.GENERIC_READ | win32file.GENERIC_WRITE,
            0, None, win32file.OPEN_EXISTING, 0, None,
        )
        return True
    except ImportError:
        # pywin32 not installed — cannot check pipe, assume not alive
        return False
    except Exception:
        return False


def attach_to_application(
    app_id: str,
    process_id: int,
    driver_list: Optional[List[str]] = None,
    pipe_name: Optional[str] = None,
) -> str:
    """Attach to a running application and register it.

    When ``process_id`` is ``None`` or ``0`` (playback of generated scripts
    passes ``0`` because the ``WPFSPY_*`` env vars are unavailable outside
    the IDE recording session), the running process is auto-detected by name
    (``{app_id}.exe``). If a matching process is found, it is attached to
    with the appropriate driver list and spy-agent pipe. If no matching
    process is running, a ``RuntimeError`` is raised.

    Args:
        app_id: Logical ID for this app.
        process_id: OS process ID. Pass 0/None to auto-detect.
        driver_list: Ordered list of drivers for this app. Default: ["FlaUI"].
        pipe_name: Named pipe for WPFSpy agent.

    Returns:
        The registered app_id.

    Raises:
        RuntimeError: If auto-detection is triggered and no matching process
            is found running.
    """
    from .app_registry import get_multi_app_context, AppContext

    if driver_list is None:
        driver_list = None
    elif isinstance(driver_list, str):
        driver_list = [d.strip() for d in driver_list.split(",") if d.strip()]
    else:
        driver_list = [str(d).strip() for d in driver_list if str(d).strip()]

    # Auto-detect the running process when no concrete pid was supplied.
    # Generated playback scripts pass PID=0 because WPFSPY_PROCESS_ID is not
    # available outside the IDE recording session.
    if not process_id:
        target_name = app_id if app_id.lower().endswith(".exe") else app_id + ".exe"
        detected_pid = None
        spy_pipe = None
        ctx = get_multi_app_context()
        for proc_info in _detect_running_processes(set()):
            if proc_info["name"].lower() != target_name.lower():
                continue
            pid = proc_info["pid"]
            # Skip PIDs already tracked under a different app_id.
            if any(c.process_id == pid for c in ctx.apps.values()):
                continue
            candidate_pipe = f"WPFSpyAgentPipe_{app_id}"
            if _is_spy_pipe_alive(candidate_pipe):
                detected_pid = pid
                spy_pipe = candidate_pipe
                break
            # Remember the first name match even without a live spy pipe.
            if detected_pid is None:
                detected_pid = pid
        if detected_pid is None:
            raise RuntimeError(
                f"Application '{app_id}' is not running. "
                f"Launch it first or add a 'Launch Application' step."
            )
        process_id = detected_pid
        if spy_pipe:
            pipe_name = spy_pipe
            if driver_list is None:
                driver_list = ["WPFSpy", "FlaUI"]
        elif driver_list is None:
            driver_list = ["FlaUI"]

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
    ctx = get_multi_app_context()
    ctx.register_app(app_context)
    # Ensure singleton consistency across import paths
    try:
        import sys
        for _mod_name in list(sys.modules.keys()):
            if _mod_name.endswith('.app_registry'):
                _mod = sys.modules.get(_mod_name)
                if _mod is not None and hasattr(_mod, '_MULTI_APP_CONTEXT'):
                    try:
                        _mod._MULTI_APP_CONTEXT = ctx
                    except Exception:
                        pass
    except Exception:
        pass
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
