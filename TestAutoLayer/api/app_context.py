"""
App Context Management for Multi-Application Automation
======================================================

This module provides the core abstractions for automating multiple
applications simultaneously:

- `AppContext`: per-application state (driver priority list, process, pipe)
- `MultiAppContext`: registry of all apps under automation
"""

from __future__ import annotations

import os
import time
import subprocess
from pathlib import Path
from typing import Any, Dict, List, Optional


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


def _create_driver_for_app(driver_name: str, app_context: AppContext) -> Any:
    # Import _ACTIVE_MODE lazily to avoid circular imports at module load time
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
    copied = []
    try:
        target_dir = Path(app_path).parent
        # Find the net461 build output directory.
        # From TestAutoLayer/api/app_context.py, repo root is parent.parent.parent.
        repo_root = Path(__file__).parent.parent.parent
        possible_sources = [
            repo_root / "WPFSpyAgent" / "bin" / "Debug" / "net461",
            repo_root / "bin" / "Debug" / "net461",
            repo_root / "WPFSpyAgent" / "bin" / "Release" / "net461",
            repo_root / "bin" / "Release" / "net461",
        ]
        fw_source = None
        for src in possible_sources:
            if src.exists() and (src / "WpfSpyAgent.FrameworkHook.dll").exists():
                fw_source = src
                break

        if not fw_source:
            print("[LAUNCH] WARNING: .NET Framework Spy Agent source directory not found")
            return copied

        fw_dlls = [
            "WpfSpyAgent.FrameworkHook.dll",
            "WpfSpyAgent.dll",
            "Newtonsoft.Json.dll",
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


def _launch_app_for_context(app_context: AppContext) -> subprocess.Popen:
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
            from runtime_injector import RuntimeInjector
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
