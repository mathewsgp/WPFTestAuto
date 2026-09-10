"""
App Registry
============
Core abstractions for automating multiple applications simultaneously.

- `AppContext`: per-application state (driver priority list, process, pipe)
- `MultiAppContext`: registry of all apps under automation
"""

from __future__ import annotations

import os
import time
import subprocess
from pathlib import Path
from typing import Any, Dict, List, Optional


class Constants:
    APPDOMAIN_MANAGER_ASM = "WpfSpyAgent.FrameworkHook"
    APPDOMAIN_MANAGER_TYPE = "WpfSpyAgent.FrameworkHook.SpyAppDomainManager"
    WPFSPY_PIPE_PREFIX = "WPFSpyAgentPipe_"
    WPFSPY_PIPE_DEFAULT = "WPFSpyAgentPipe"
    WPFSPY_AGENT_ENABLED = "WPFSPY_AGENT_ENABLED"
    WPFSPY_PIPE_NAME = "WPFSPY_PIPE_NAME"
    DOTNET_STARTUP_HOOKS = "DOTNET_STARTUP_HOOKS"
    DOTNET_STARTUP_HOOK_DLL = "WpfSpyAgent.StartupHook.dll"
    FRAMEWORK_HOOK_DLL = "WpfSpyAgent.FrameworkHook.dll"
    AGENT_DLL = "WpfSpyAgent.dll"
    NEWTONSOFT_JSON_DLL = "Newtonsoft.Json.dll"
    DEFAULT_DRIVER = "FlaUI"
    LAUNCH_POLL_DELAY = 0.2
    TERMINATE_TIMEOUT = 3
    KILL_TIMEOUT = 2


def _create_driver_for_app(driver_name: str, app_context: 'AppContext') -> Any:
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


class AppContext:
    """State for a single application under automation.

    The driver list is ordered by priority. The first driver is used for
    probing/recording. Playback iterates the list as a fallback chain.
    If ``WPFSpy`` appears anywhere in the list, the spy agent will be
    injected at launch (unless ``inject_spy_agent`` is explicitly False).
    """

    def __init__(
        self,
        app_id: str,
        app_name: str,
        driver_list: Optional[List[str]] = None,
        process_id: Optional[int] = None,
        pipe_name: Optional[str] = None,
        app_path: Optional[str] = None,
        launch_args: Optional[List[str]] = None,
        env: Optional[Dict[str, str]] = None,
        start_in: Optional[str] = None,
        inject_spy_agent: Optional[bool] = None,
        attach: bool = False,
        element_repo_path: Optional[str] = None,
    ):
        self.app_id = app_id
        self.app_name = app_name
        self.driver_list = driver_list or ["FlaUI"]
        self.process_id = process_id
        self.pipe_name = pipe_name
        self.app_path = app_path
        self.launch_args = launch_args or []
        self.env = env or {}
        self.start_in = start_in
        self.attach = attach
        self.element_repo_path = element_repo_path

        # Spy-agent injection is implied by WPFSpy presence in driver_list,
        # but can be overridden explicitly.
        if inject_spy_agent is None:
            self.inject_spy_agent = any(d.lower() == "wpfspy" for d in self.driver_list)
        else:
            self.inject_spy_agent = inject_spy_agent

        # Derive pipe name from app_id when WPFSpy is enabled and no explicit pipe.
        if self.inject_spy_agent and not self.pipe_name:
            self.pipe_name = f"WPFSpyAgentPipe_{app_id}"

        self.drivers: Dict[str, Any] = {}
        self.process: Optional[subprocess.Popen] = None
        self.element_scope: Optional[str] = None  # future: per-app repo scope

    @property
    def driver(self) -> str:
        """Primary driver = first in priority list."""
        return self.driver_list[0]

    def get_driver(self, driver_name: str) -> Any:
        if driver_name not in self.drivers:
            self.drivers[driver_name] = _create_driver_for_app(driver_name, self)
        return self.drivers[driver_name]

    def close(self):
        try:
            if self.process and self.process.poll() is None:
                self.process.kill()
                self.process.wait(timeout=5)
        except Exception:
            pass
        self.process = None
        self.drivers.clear()

    def to_dict(self) -> Dict[str, Any]:
        return {
            "app_id": self.app_id,
            "app_name": self.app_name,
            "driver_list": self.driver_list,
            "process_id": self.process_id,
            "pipe_name": self.pipe_name,
            "app_path": self.app_path,
            "launch_args": self.launch_args,
            "env": self.env,
            "start_in": self.start_in,
            "inject_spy_agent": self.inject_spy_agent,
            "attach": self.attach,
            "element_repo_path": self.element_repo_path,
        }


class MultiAppContext:
    """Registry of all applications under automation."""

    def __init__(self):
        self.apps: Dict[str, AppContext] = {}
        self.default_app_id: Optional[str] = None

    def register_app(self, app_context: AppContext) -> str:
        self.apps[app_context.app_id] = app_context
        if self.default_app_id is None:
            self.default_app_id = app_context.app_id
        return app_context.app_id

    def unregister_app(self, app_id: str):
        if app_id in self.apps:
            self.apps[app_id].close()
            del self.apps[app_id]
        if self.default_app_id == app_id:
            self.default_app_id = next(iter(self.apps), None)

    def get_app(self, app_id: Optional[str] = None) -> AppContext:
        app_id = app_id or self.default_app_id
        if app_id not in self.apps:
            raise ValueError(
                f"App '{app_id}' not registered. "
                f"Registered apps: {list(self.apps.keys())}"
            )
        return self.apps[app_id]

    def set_default_app(self, app_id: str):
        if app_id not in self.apps:
            raise ValueError(f"App '{app_id}' not registered")
        self.default_app_id = app_id

    def list_apps(self) -> List[Dict[str, Any]]:
        return [app.to_dict() for app in self.apps.values()]

    def close_all(self):
        for app in list(self.apps.values()):
            app.close()
        self.apps.clear()
        self.default_app_id = None

    def save(self, file_path: str):
        """Save the multi-app context to a JSON file."""
        import json
        data = {
            "default_app_id": self.default_app_id,
            "apps": [app.to_dict() for app in self.apps.values()],
        }
        with open(file_path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)

    @classmethod
    def load(cls, file_path: str) -> 'MultiAppContext':
        """Load a multi-app context from a JSON file."""
        import json
        with open(file_path, "r", encoding="utf-8") as f:
            data = json.load(f)
        
        context = cls()
        context.default_app_id = data.get("default_app_id")
        
        for app_data in data.get("apps", []):
            app = AppContext(
                app_id=app_data["app_id"],
                app_name=app_data["app_name"],
                driver_list=app_data.get("driver_list"),
                process_id=app_data.get("process_id"),
                pipe_name=app_data.get("pipe_name"),
                app_path=app_data.get("app_path"),
                launch_args=app_data.get("launch_args"),
                env=app_data.get("env"),
                start_in=app_data.get("start_in"),
                attach=app_data.get("attach", False),
                element_repo_path=app_data.get("element_repo_path"),
            )
            # Don't set inject_spy_agent from saved data - recalculate from driver_list
            context.apps[app.app_id] = app
            if context.default_app_id is None:
                context.default_app_id = app.app_id
        
        return context


# Global instance for backward compatibility
_MULTI_APP_CONTEXT = MultiAppContext()


def get_multi_app_context() -> MultiAppContext:
    """Get the global multi-app context registry."""
    return _MULTI_APP_CONTEXT


def _is_net_framework_app(app_path: str) -> bool:
    """Check if an app path points to a .NET Framework application."""
    try:
        normalized = app_path.replace("/", "\\").lower()
        return (
            "\\net461\\" in normalized
            or "\\net48\\" in normalized
            or "\\net472\\" in normalized
            or "\\net462\\" in normalized
            or normalized.endswith("\\net461")
            or normalized.endswith("\\net48")
            or normalized.endswith("\\net472")
            or normalized.endswith("\\net462")
        )
    except Exception:
        return False


def _stage_framework_dlls(app_path: str) -> List[str]:
    """Stage .NET Framework Spy Agent DLLs next to the AUT."""
    from ..dll_config import dll_config
    copied = []
    try:
        target_dir = Path(app_path).parent
        fw_source = dll_config.get_framework_agent_dir()
        if not fw_source:
            print("[LAUNCH] WARNING: .NET Framework Spy Agent source directory not found")
            return copied

        fw_dlls = [
            dll_config.paths.wpf_spy_agent_framework_hook,
            dll_config.paths.wpf_spy_agent,
            dll_config.paths.newtonsoft_json,
        ]
        for name in fw_dlls:
            src = fw_source / name
            if not src.exists():
                continue
            dst = target_dir / name
            try:
                if dst.exists() and dst.stat().st_mtime >= src.stat().st_mtime:
                    continue
                import shutil
                shutil.copy2(src, dst)
                copied.append(name)
            except OSError:
                pass
    except Exception as e:
        print(f"[LAUNCH] Framework DLL staging error: {e}")
    return copied