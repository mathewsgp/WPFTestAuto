"""
Test Helper — SampleWpfApp lifecycle management
================================================
Manages the SampleWpfApp process for test execution.
Moved out of DriverAgnosticApi.py to keep the framework API clean.
"""

import os
import sys
import subprocess
import time
from pathlib import Path
from typing import Optional

_THIS_DIR = os.path.dirname(os.path.abspath(__file__))
_REPO_ROOT = Path(_THIS_DIR).parent.parent
sys.path.insert(0, str(_REPO_ROOT / "TestAutoLayer" / "api"))


def get_sample_wpf_app_path() -> str:
    """Returns the path to the SampleWpfApp executable."""
    base = os.path.join(
        str(_REPO_ROOT), "Tests", "SampleWpfApp", "bin", "Debug", "net9.0-windows"
    )
    dll = os.path.join(base, "SampleWpfApp.dll")
    if os.path.exists(dll):
        return dll
    exe = os.path.join(base, "SampleWpfApp.exe")
    if os.path.exists(exe):
        return exe
    raise FileNotFoundError(f"SampleWpfApp not found in {base}")


def kill_sample_wpf_app() -> None:
    """Kills any running SampleWpfApp process by matching its window title."""
    try:
        subprocess.run(
            ["taskkill", "/F", "/FI", "WINDOWTITLE eq Sample WPF App*"],
            capture_output=True,
            timeout=10,
            check=False,
        )
    except Exception:
        pass


def is_sample_wpf_app_running() -> bool:
    """Check if SampleWpfApp is already running by window title."""
    try:
        result = subprocess.run(
            ["tasklist", "/FI", "WINDOWTITLE eq Sample WPF App*", "/FO", "CSV", "/NH"],
            capture_output=True,
            text=True,
            timeout=5,
            check=False,
        )
        return "dotnet.exe" in result.stdout or "SampleWpfApp.exe" in result.stdout
    except Exception:
        return False


def start_sample_wpf_app() -> subprocess.Popen:
    """Starts SampleWpfApp with the WPFSpy agent startup hook.

    Searches for the StartupHook DLL in multiple locations and launches
    the app with DOTNET_STARTUP_HOOKS set so the agent initializes.
    """
    app_path = get_sample_wpf_app_path()

    # Find the StartupHook DLL in common locations
    startup_hook = None

    # 1. Check same directory as the app (copied during build)
    app_dir = os.path.dirname(app_path)
    candidate = os.path.join(app_dir, "WpfSpyAgent.StartupHook.dll")
    if os.path.exists(candidate):
        startup_hook = candidate

    # 2. Check solution-level WpfSpyAgent.StartupHook output
    if not startup_hook:
        candidate = os.path.join(
            str(_REPO_ROOT),
            "WPFSpyAgent",
            "StartupHook",
            "bin",
            "Debug",
            "net9.0-windows",
            "WpfSpyAgent.StartupHook.dll",
        )
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
        print(f"[SampleWpfAppManager] Using startup hook: {startup_hook}")
    else:
        print("[SampleWpfAppManager] WARNING: Startup hook DLL not found")

    cmd = ["dotnet", app_path]
    proc = subprocess.Popen(
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
                    0,
                    None,
                    win32file.OPEN_EXISTING,
                    0,
                    None,
                )
                win32file.CloseHandle(handle)
                print("[SampleWpfAppManager] WPFSpy agent is ready on pipe")
                return proc
            except Exception:
                time.sleep(1)
        print("[SampleWpfAppManager] WARNING: WPFSpy agent did not become ready")
    except ImportError:
        pass  # pywin32 not available

    return proc


def reset_real_app(
    sample_app_process: Optional[subprocess.Popen] = None,
    mode: str = "mock",
    ide_mode: bool = False,
) -> Optional[subprocess.Popen]:
    """Resets the real SampleWpfApp state.

    In IDE mode (WPFSPY_IDE_RUN=1): keeps the app running, uses ResetState.
    In CLI/CI mode: closes and reopens the app between tests for isolation.
    """
    # IDE mode: keep app running, just reset state
    if ide_mode:
        if mode == "real" and sample_app_process is not None and sample_app_process.poll() is None:
            try:
                from WPFSpyLibrary import WPFSpyDriver
                driver = WPFSpyDriver()
                result = driver._send("ResetState")
                if not result.get("success"):
                    raise Exception(f"ResetState failed: {result.get('error')}")
                print("[SampleWpfAppManager] SampleWpfApp state reset via agent (IDE mode)")
                time.sleep(1)
                return sample_app_process
            except Exception as e:
                print(f"[SampleWpfAppManager] ResetState failed: {e}")
        else:
            print("[SampleWpfAppManager] Starting fresh in IDE mode")
            kill_sample_wpf_app()
            time.sleep(2)
            return start_sample_wpf_app()
    else:
        # CLI/CI mode: always close and reopen app between tests
        if mode == "real":
            print("[SampleWpfAppManager] CLI mode: closing and reopening")
            kill_sample_wpf_app()
            time.sleep(2)
            return start_sample_wpf_app()
        else:
            print("[SampleWpfAppManager] Mock mode: no app lifecycle needed")
            return None