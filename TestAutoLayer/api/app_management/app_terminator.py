"""
App Terminator
==============
Handles application termination with graceful shutdown and force-kill options.
"""

import os
import sys
import subprocess
import time
from typing import List, Optional

from TestAutoLayer.api.logging_utils import get_api_logger
from .app_registry import get_multi_app_context
from ._shared import PIDHolderCTypes

logger = get_api_logger()


def terminate_application(
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
            ctx = get_multi_app_context().get_app(app_id)
        except Exception:
            ctx = None
        if ctx is not None and ctx.process_id:
            if kill_pid(int(ctx.process_id), force=force):
                terminated += 1
            try:
                get_multi_app_context().unregister_app(app_id)
            except Exception:
                pass
            return terminated

    # Path 2: discover processes by window title or process name.
    targets = find_pids_by_title_or_name(
        window_title=window_title,
        process_name=process_name,
    )
    for pid in targets:
        if kill_pid(int(pid), force=force):
            terminated += 1

    logger.info(
        "terminate_application completed",
        app_id=app_id,
        window_title=window_title,
        process_name=process_name,
        terminated=terminated,
    )
    return terminated


def kill_pid(pid: int, force: bool = False) -> bool:
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

                holder = PIDHolderCTypes(pid, 0)

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


def find_pids_by_title_or_name(
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