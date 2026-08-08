"""
App Context Management for Multi-Application Automation
======================================================

This module provides the core abstractions for automating multiple
applications simultaneously:

- `AppContext`: per-application state (drivers, process, pipe, element scope)
- `MultiAppContext`: registry of all apps under automation
"""

from __future__ import annotations

import os
import time
import subprocess
from typing import Any, Dict, List, Optional


class AppContext:
    """State for a single application under automation."""

    def __init__(
        self,
        app_id: str,
        app_name: str,
        driver: str = "FlaUI",
        process_id: Optional[int] = None,
        pipe_name: Optional[str] = None,
        app_path: Optional[str] = None,
        launch_args: Optional[List[str]] = None,
        env: Optional[Dict[str, str]] = None,
    ):
        self.app_id = app_id
        self.app_name = app_name
        self.driver = driver
        self.process_id = process_id
        self.pipe_name = pipe_name
        self.app_path = app_path
        self.launch_args = launch_args or []
        self.env = env or {}

        self.drivers: Dict[str, Any] = {}
        self.process: Optional[subprocess.Popen] = None
        self.element_scope: Optional[str] = None  # future: per-app repo scope

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
            "driver": self.driver,
            "process_id": self.process_id,
            "pipe_name": self.pipe_name,
            "app_path": self.app_path,
            "launch_args": self.launch_args,
            "env": self.env,
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
    effective_mode = _ACTIVE_MODE if _ACTIVE_MODE is not None else os.environ.get("WPFSPY_MODE", "mock").lower()

    if driver_name == "FlaUI":
        try:
            from flaui_driver import FlaUIDriver
            return FlaUIDriver(app_pid=app_context.process_id)
        except ImportError:
            raise ImportError("FlaUI driver not available. Install: pip install robotframework-flaui")

    if driver_name == "WPFSpy":
        if effective_mode == "real":
            if app_context.pipe_name is None:
                raise ValueError("WPFSpy real driver requires pipe_name in AppContext")
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


def _launch_app_for_context(app_context: AppContext) -> subprocess.Popen:
    if not app_context.app_path:
        raise ValueError("app_path is required to launch application")

    env = os.environ.copy()
    env.update(app_context.env)

    if app_context.driver == "WPFSpy":
        from runtime_injector import RuntimeInjector
        injector = RuntimeInjector()
        if injector.startup_hook_path:
            env["DOTNET_STARTUP_HOOKS"] = injector.startup_hook_path
            env["WPFSPY_AGENT_ENABLED"] = "1"
            env["WPFSPY_PIPE_NAME"] = app_context.pipe_name or "WPFSpyAgentPipe"

    proc = subprocess.Popen(
        ["dotnet", app_context.app_path] + app_context.launch_args,
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    time.sleep(5)
    return proc
