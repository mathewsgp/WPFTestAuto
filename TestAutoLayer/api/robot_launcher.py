"""
Robot Framework Library for WPF Application Launching
=====================================================
Library for launching WPF applications with Spy Agent injection.

Usage:
    *** Settings ***
    Library    ../api/robot_launcher.py

    *** Test Cases ***
    Launch App
        ${pid}=    Launch Application    C:\\path\\to\\app.exe
        Terminate Application    ${pid}
"""

import os
import sys
import subprocess
import time
import shutil
from pathlib import Path

# Global state for the library
_processes = {}
_startup_hook_path = None


def _find_startup_hook():
    """Find the StartupHook DLL in common locations."""
    global _startup_hook_path
    if _startup_hook_path is not None:
        return _startup_hook_path

    base_paths = [
        Path(__file__).parent.parent,
        Path(__file__).parent.parent / "WpfSpyAgent.StartupHook" / "bin" / "Debug" / "net6.0-windows",
        Path(__file__).parent.parent / "WpfSpyAgent.StartupHook" / "bin" / "Debug" / "net8.0-windows",
    ]

    for base in base_paths:
        dll_path = base / "WpfSpyAgent.StartupHook.dll"
        if dll_path.exists():
            _startup_hook_path = str(dll_path.resolve())
            return _startup_hook_path

    env_path = os.environ.get("WPFSPY_STARTUP_HOOK_DLL")
    if env_path and Path(env_path).exists():
        _startup_hook_path = env_path
        return _startup_hook_path

    return None


def _stage_dlls_to_app(app_path, hook_path):
    """Copy Spy Agent DLLs next to the AUT, timestamp-aware.

    Returns a list of names that were copied (for cleanup).
    """
    src_dir = Path(hook_path).parent
    target_dir = Path(app_path).parent
    target_dir.mkdir(parents=True, exist_ok=True)
    copied = []
    for name in ("WpfSpyAgent.dll", "WpfSpyAgent.StartupHook.dll"):
        src = src_dir / name
        if not src.exists():
            continue
        dst = target_dir / name
        try:
            if dst.exists() and dst.stat().st_mtime >= src.stat().st_mtime:
                continue
            shutil.copy2(src, dst)
            copied.append(name)
        except OSError:
            pass
    return copied


def _unstage_dlls_from_app(app_path, names):
    """Remove previously staged DLLs (best-effort)."""
    if not names:
        return
    target_dir = Path(app_path).parent
    for name in names:
        try:
            (target_dir / name).unlink(missing_ok=True)
        except OSError:
            pass


def launch_application(app_path, arguments=None, pipe_name="WPFSpyAgentPipe", timeout=30.0, cwd=None, stage_dlls=False):
    """Launch an application with Spy Agent injected.

    When `stage_dlls=True`, the managed Spy Agent DLLs are copied next to the
    target app first (timestamp-aware), enabling attach into AUTs in a
    different folder from the framework's bin directory. The staged files are
    removed on `terminate_application` / `terminate_all_applications`.
    """
    hook_path = _find_startup_hook()
    if not hook_path:
        hook_path = os.environ.get("WPFSPY_STARTUP_HOOK_DLL")

    if not hook_path:
        raise RuntimeError(
            "Startup hook DLL not found. Set WPFSPY_STARTUP_HOOK_DLL "
            "or build WpfSpyAgent.StartupHook project."
        )

    if not Path(app_path).exists():
        raise FileNotFoundError(f"Application not found: {app_path}")

    staged = _stage_dlls_to_app(app_path, hook_path) if stage_dlls else []

    full_env = os.environ.copy()
    full_env["DOTNET_STARTUP_HOOKS"] = hook_path
    full_env["WPFSPY_AGENT_ENABLED"] = "1"
    full_env["WPFSPY_PIPE_NAME"] = pipe_name

    cmd = [app_path]
    if arguments:
        if isinstance(arguments, list):
            cmd.extend(arguments)
        else:
            cmd.append(str(arguments))

    process = subprocess.Popen(
        cmd,
        env=full_env,
        cwd=cwd,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE
    )

    time.sleep(1)
    _processes[process.pid] = {
        'process': process,
        'pipe_name': pipe_name,
        'app_path': app_path,
        'staged_dlls': staged,
    }

    return process.pid


def attach_to_application(process_id, pipe_name="WPFSpyAgentPipe", timeout=5.0):
    """Attach to an already-running application."""
    return False  # Would need win32 API call


def terminate_application(process_id):
    """Terminate a launched application."""
    if process_id in _processes:
        proc_info = _processes[process_id]
        proc = proc_info['process']
        proc.terminate()
        try:
            proc.wait(timeout=5)
        except subprocess.TimeoutExpired:
            proc.kill()
        # Clean up any DLLs we staged into the AUT folder.
        _unstage_dlls_from_app(proc_info.get('app_path'), proc_info.get('staged_dlls', []))
        del _processes[process_id]
        return True

    try:
        if sys.platform == "win32":
            subprocess.run(['taskkill', '/F', '/PID', str(process_id)], check=True)
            return True
        else:
            subprocess.run(['kill', str(process_id)], check=True)
            return True
    except:
        return False


def terminate_all_applications():
    """Terminate all applications launched by this library."""
    count = 0
    pids = list(_processes.keys())
    for pid in pids:
        if terminate_application(pid):
            count += 1
    return count


def get_process_id(app_path=None):
    """Get PID of a running application."""
    for pid, info in _processes.items():
        if app_path is None or info['app_path'] == app_path:
            if info['process'].poll() is None:
                return pid
    return None


def is_agent_ready(pipe_name="WPFSpyAgentPipe"):
    """Check if Spy Agent is ready on the named pipe."""
    if sys.platform != "win32":
        return False

    try:
        import win32file
        pipe_path = f"\\\\.\\pipe\\{pipe_name}"
        handle = win32file.CreateFile(
            pipe_path,
            win32file.GENERIC_READ,
            0, None,
            win32file.OPEN_EXISTING,
            0, None,
        )
        win32file.CloseHandle(handle)
        return True
    except:
        return False


def get_startup_hook_path():
    """Get the path to the startup hook DLL."""
    path = _find_startup_hook()
    return path or "None"
