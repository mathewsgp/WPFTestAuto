"""
Window Activator
================
Handles window activation using Win32 API.
"""

import ctypes
import logging
from ctypes import wintypes
from typing import Optional

from TestAutoLayer.api.logging_utils import get_api_logger
from .app_registry import get_multi_app_context

logger = get_api_logger()


def activate_window(
    app_id: Optional[str] = None,
    window_title: Optional[str] = None,
) -> bool:
    """Activates (brings to front and focuses) a window by app_id or title.

    Args:
        app_id: Registered application ID. Uses default app if not provided.
        window_title: Window title to search for (used if app_id not provided).

    Returns:
        True if window was activated.

    Raises:
        RuntimeError: If window could not be found.
    """
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    hwnd = None
    target_pid = None

    logger.info("activate_window called", app_id=app_id, window_title=window_title)

    if app_id or (not window_title and not app_id):
        # Get window by process ID
        if app_id is None:
            context = get_multi_app_context()
            app_id = context.default_app_id
        if app_id:
            try:
                app_context = get_multi_app_context().get_app(app_id)
                target_pid = app_context.process_id
                logger.info(
                    "activate_window: found app_context",
                    app_id=app_id,
                    process_id=target_pid,
                    app_name=app_context.app_name,
                )
            except Exception as e:
                logger.warning(f"activate_window: failed to get app_context app_id={app_id} error={e}")

        if target_pid:
            hwnd = _find_window_by_pid(user32, target_pid)
            logger.info(f"activate_window: window enumeration result", hwnd=hwnd)

    if hwnd is None and target_pid:
        # Fallback: try to find window by enumerating all windows and checking process name
        hwnd = _find_window_by_process_name(user32, target_pid)

    if hwnd is None and window_title:
        # Find window by title
        hwnd = user32.FindWindowW(None, window_title)

    if hwnd is None and target_pid is None and app_id:
        # Fallback: try to find window by process name
        hwnd = _find_window_by_app_name(user32, app_id)

    if hwnd is None:
        raise RuntimeError(f"Could not find window to activate (app_id={app_id}, title={window_title})")

    # Restore if minimized
    if user32.IsIconic(hwnd):
        user32.ShowWindow(hwnd, 9)  # SW_RESTORE

    # Bring to front and set focus
    user32.SetForegroundWindow(hwnd)
    user32.SetFocus(hwnd)
    logger.info("Window activated", app_id=app_id, window_title=window_title)
    return True


def _find_window_by_pid(user32, target_pid: int) -> Optional[int]:
    """Find a visible window belonging to the given PID."""
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
    return holder.hwnd if holder.hwnd else None


def _find_window_by_process_name(user32, target_pid: int) -> Optional[int]:
    """Find window by process name matching the target PID's process."""
    try:
        import psutil
        target_proc = psutil.Process(target_pid)
        target_proc_name = target_proc.name().lower()
    except Exception:
        return None

    EnumWindows = user32.EnumWindows
    EnumWindowsProc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    GetWindowThreadProcessId = user32.GetWindowThreadProcessId

    class NameHolder(ctypes.Structure):
        _fields_ = [("name", ctypes.c_wchar_p), ("hwnd", wintypes.HWND)]

    holder = NameHolder(target_proc_name, 0)

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
    return holder.hwnd if holder.hwnd else None


def _find_window_by_app_name(user32, app_id: str) -> Optional[int]:
    """Find window by app name from registered app context."""
    try:
        import psutil
        app_context = get_multi_app_context().get_app(app_id)
        proc_name = app_context.app_name.lower()
        
        for proc in psutil.process_iter(["pid", "name"]):
            if proc.info["name"] and proc.info["name"].lower() in proc_name:
                target_pid = int(proc.info["pid"])
                hwnd = _find_window_by_pid(user32, target_pid)
                if hwnd:
                    return hwnd
    except Exception:
        pass
    return None