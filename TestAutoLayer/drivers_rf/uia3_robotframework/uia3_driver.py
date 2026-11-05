"""
Uia3.RobotFramework — Layer 4 driver wrapper for the UIA3 worker process.

This module provides:
1. `Uia3Driver` — Python driver class implementing the Layer 3 `BaseDriver`
   interface. Talks to a standalone `Uia3Worker.exe` (C# / System.Windows.Automation)
   over a Windows Named Pipe (JSON over pipe, mirrors the WPFSpy pattern).
2. `Uia3Library` — Robot Framework library exposing Uia3 keywords directly.

No dependency on pythonnet or flaui-uiautomation-wrapper. The worker is a
self-contained .NET 9 process launched and owned by Python via subprocess.
"""

from __future__ import annotations

import base64
import json
import os
import subprocess
import sys
import time
from typing import Any, Dict, List, Optional

# Add parent directories to path for base driver and exceptions
_this_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(_this_dir, "..", "..", "api"))
sys.path.insert(0, os.path.join(_this_dir, "..", "..", "mock_wpf_app"))

from base_driver import BaseDriver, ElementHandle  # noqa: E402
from exceptions import (  # noqa: E402
    ElementNotFoundError,
    ElementNotInteractableError,
)
from mock_app import ElementNotFoundError as MockElementNotFoundError  # noqa: E402

PIPE_PREFIX = "Uia3WorkerPipe_"
WORKER_EXE_ENV = "UIA3_WORKER_EXE"
WORKER_EXE_DEFAULT = None  # resolved at runtime


def _resolve_worker_exe() -> Optional[str]:
    """Resolve the path to Uia3Worker.exe.

    Priority:
    1. UIA3_WORKER_EXE environment variable
    2. bin/Debug/net9.0-windows/Uia3Worker.exe (relative to cwd)
    3. bin/Release/net9.0-windows/Uia3Worker.exe
    """
    env_path = os.environ.get(WORKER_EXE_ENV)
    if env_path and os.path.exists(env_path):
        return env_path

    candidates = [
        os.path.join(os.getcwd(), "bin", "Debug", "net9.0-windows", "Uia3Worker.exe"),
        os.path.join(os.getcwd(), "bin", "Release", "net9.0-windows", "Uia3Worker.exe"),
        os.path.join(os.getcwd(), "WpfTestIde.Uia3Worker", "bin", "Debug", "net9.0-windows", "Uia3Worker.exe"),
        os.path.join(os.getcwd(), "WpfTestIde.Uia3Worker", "bin", "Release", "net9.0-windows", "Uia3Worker.exe"),
    ]
    for c in candidates:
        if os.path.exists(c):
            return c
    return None


class _PipeClient:
    """Minimal named-pipe client mirroring WPFSpyRealDriver._send."""

    def __init__(self, pipe_name: str):
        self.pipe_name = pipe_name
        self.pipe_path = rf"\\.\pipe\{pipe_name}"

    def send(self, command: str, **params) -> dict:
        import win32file  # pywin32 — Windows only

        request = json.dumps({"command": command, **params}) + "\n"

        max_retries = 20
        retry_delay = 0.5
        last_error = None
        for _ in range(max_retries):
            try:
                handle = win32file.CreateFile(
                    self.pipe_path,
                    win32file.GENERIC_READ | win32file.GENERIC_WRITE,
                    0, None,
                    win32file.OPEN_EXISTING,
                    0, None,
                )
                break
            except Exception as e:
                last_error = e
                time.sleep(retry_delay)
        else:
            raise last_error

        try:
            win32file.WriteFile(handle, request.encode("utf-8"))
            buffer = b""
            while not buffer.endswith(b"\n"):
                _, chunk = win32file.ReadFile(handle, 4096)
                if not chunk:
                    break
                buffer += chunk
            return json.loads(buffer.decode("utf-8"))
        finally:
            win32file.CloseHandle(handle)


class Uia3Driver(BaseDriver):
    """UIA3 driver: talks to a standalone Uia3Worker.exe over a named pipe.

    The worker is a C# process using System.Windows.Automation (UIA3).
    Each driver instance owns one worker process connected via a unique
    pipe named ``Uia3WorkerPipe_<pid>``.

    Element IDs are session-local integers maintained by the worker.
    Stale elements are re-resolved automatically by the worker on action
    commands (ElementNotAvailableException / COMException -> retry once).
    """

    @property
    def name(self) -> str:
        return "UIA3"

    def __init__(self, app_pid: Optional[int] = None, pipe_name: Optional[str] = None):
        self._app_pid = app_pid
        self._pipe_name = pipe_name or (f"{PIPE_PREFIX}{app_pid}" if app_pid else f"{PIPE_PREFIX}0")
        self._worker_proc: Optional[subprocess.Popen] = None
        self._pipe: Optional[_PipeClient] = None
        self._connected = False
        self._connect()

    # ------------------------------------------------------------------
    # Worker lifecycle
    # ------------------------------------------------------------------

    def _connect(self):
        """Launch the worker process and connect to its named pipe."""
        worker_exe = _resolve_worker_exe()
        if worker_exe is None:
            raise FileNotFoundError(
                "Uia3Worker.exe not found. Set UIA3_WORKER_EXE env var or "
                "build the WpfTestIde.Uia3Worker project."
            )

        pid_arg = self._app_pid if self._app_pid else 0
        self._worker_proc = subprocess.Popen(
            [worker_exe, "--pid", str(pid_arg), "--pipe", self._pipe_name],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )

        # Wait for the pipe to become available
        self._pipe = _PipeClient(self._pipe_name)
        max_wait = 15.0
        start = time.time()
        last_error = None
        while time.time() - start < max_wait:
            try:
                resp = self._pipe.send("attach")
                if resp.get("success"):
                    self._connected = True
                    return
            except Exception as e:
                last_error = e
            time.sleep(0.3)
        raise TimeoutError(
            f"Uia3Worker did not become ready on pipe {self._pipe_name} within {max_wait}s: {last_error}"
        )

    def _restart_worker(self):
        """Kill and restart the worker, then reconnect."""
        self.close()
        time.sleep(0.5)
        self._connected = False
        self._connect()

    def _send(self, command: str, **params) -> dict:
        """Send a command to the worker, restarting on broken pipe."""
        if self._pipe is None:
            raise ElementNotInteractableError("Uia3Driver", "worker not connected")
        try:
            return self._pipe.send(command, **params)
        except Exception:
            # Broken pipe — restart worker and retry once
            self._restart_worker()
            return self._pipe.send(command, **params)

    def _resolve_element_id(self, element: ElementHandle) -> int:
        """Extract the worker element ID from an ElementHandle.

        The driver stores the element ID in the locator under ``elementId``.
        """
        locator = element.locator
        eid = locator.get("elementId")
        if eid is None:
            raise ElementNotInteractableError(
                "Uia3Driver:element_handle",
                f"element handle has no elementId: {locator}"
            )
        return int(eid)

    def close(self) -> None:
        """Clean up driver resources: close pipe, kill worker process."""
        self._connected = False
        if self._pipe is not None:
            try:
                self._pipe.send("close")
            except Exception:
                pass
            self._pipe = None
        if self._worker_proc is not None:
            try:
                if self._worker_proc.poll() is None:
                    self._worker_proc.kill()
                    self._worker_proc.wait(timeout=5)
            except Exception:
                pass
            self._worker_proc = None

    # ------------------------------------------------------------------
    # BaseDriver interface
    # ------------------------------------------------------------------

    def find_element(self, locator: Dict[str, Any]) -> ElementHandle:
        search_by = locator.get("searchBy", "AutomationId")
        value = locator.get("value", "")
        index = locator.get("index", None)

        resp = self._send("find_element", searchBy=search_by, value=value, index=index)
        if not resp.get("success"):
            raise ElementNotFoundError(
                f"UIA3: element not found: searchBy={search_by}, value={value} — {resp.get('error')}"
            )
        element_id = resp["data"]["elementId"]
        return ElementHandle(
            locator={**locator, "elementId": element_id},
            driver_name=self.name,
            found_at=time.time(),
            driver=self,
        )

    def find_elements(self, locator: Dict[str, Any]) -> List[ElementHandle]:
        search_by = locator.get("searchBy", "AutomationId")
        value = locator.get("value", "")

        resp = self._send("find_elements", searchBy=search_by, value=value)
        if not resp.get("success"):
            return []
        ids = resp["data"].get("elementIds", [])
        return [
            ElementHandle(
                locator={**locator, "elementId": eid},
                driver_name=self.name,
                found_at=time.time(),
                driver=self,
            )
            for eid in ids
        ]

    def invoke(self, element: ElementHandle) -> None:
        eid = self._resolve_element_id(element)
        resp = self._send("invoke", elementId=eid)
        if not resp.get("success"):
            raise ElementNotInteractableError(
                f"UIA3:element_{eid}",
                f"cannot invoke element id={eid} — {resp.get('error')}"
            )

    def set_value(self, element: ElementHandle, value: str) -> None:
        eid = self._resolve_element_id(element)
        resp = self._send("set_value", elementId=eid, value=value)
        if not resp.get("success"):
            raise ElementNotInteractableError(
                f"UIA3:element_{eid}",
                f"cannot set value on element id={eid} — {resp.get('error')}"
            )

    def get_text(self, element: ElementHandle) -> str:
        eid = self._resolve_element_id(element)
        resp = self._send("get_text", elementId=eid)
        if not resp.get("success"):
            return ""
        return resp["data"].get("text", "")

    def is_visible(self, element: ElementHandle) -> bool:
        eid = self._resolve_element_id(element)
        resp = self._send("is_visible", elementId=eid)
        if not resp.get("success"):
            return False
        return bool(resp["data"].get("visible"))

    def is_enabled(self, element: ElementHandle) -> bool:
        eid = self._resolve_element_id(element)
        resp = self._send("is_enabled", elementId=eid)
        if not resp.get("success"):
            return False
        return bool(resp["data"].get("enabled"))

    def toggle(self, element: ElementHandle, state: Optional[bool] = None) -> bool:
        eid = self._resolve_element_id(element)
        resp = self._send("toggle", elementId=eid, state=state)
        if not resp.get("success"):
            return False
        return bool(resp["data"].get("state", False))

    def double_click(self, element: ElementHandle) -> None:
        eid = self._resolve_element_id(element)
        resp = self._send("double_click", elementId=eid)
        if not resp.get("success"):
            raise ElementNotInteractableError(
                f"UIA3:element_{eid}",
                f"cannot double-click element id={eid} — {resp.get('error')}"
            )

    def right_click(self, element: ElementHandle) -> None:
        eid = self._resolve_element_id(element)
        resp = self._send("right_click", elementId=eid)
        if not resp.get("success"):
            raise ElementNotInteractableError(
                f"UIA3:element_{eid}",
                f"cannot right-click element id={eid} — {resp.get('error')}"
            )

    def press_keys(self, element: ElementHandle, keys: str) -> None:
        eid = self._resolve_element_id(element)
        resp = self._send("press_keys", elementId=eid, keys=keys)
        if not resp.get("success"):
            raise ElementNotInteractableError(
                f"UIA3:element_{eid}",
                f"cannot press keys on element id={eid} — {resp.get('error')}"
            )

    def drag_drop(self, element: ElementHandle, target_element: ElementHandle) -> None:
        src_id = self._resolve_element_id(element)
        dst_id = self._resolve_element_id(target_element)
        resp = self._send("drag_drop", srcElementId=src_id, dstElementId=dst_id)
        if not resp.get("success"):
            raise ElementNotInteractableError(
                f"UIA3:element_{src_id}",
                f"cannot drag-drop id={src_id} -> id={dst_id} — {resp.get('error')}"
            )

    def hover(self, element: ElementHandle) -> None:
        eid = self._resolve_element_id(element)
        resp = self._send("hover", elementId=eid)
        if not resp.get("success"):
            raise ElementNotInteractableError(
                f"UIA3:element_{eid}",
                f"cannot hover element id={eid} — {resp.get('error')}"
            )

    def scroll(self, element: ElementHandle, direction: str) -> None:
        eid = self._resolve_element_id(element)
        resp = self._send("scroll", elementId=eid, direction=direction)
        if not resp.get("success"):
            raise ElementNotInteractableError(
                f"UIA3:element_{eid}",
                f"cannot scroll element id={eid} — {resp.get('error')}"
            )

    def get_attribute(
        self,
        element: ElementHandle,
        attribute_name: str,
    ) -> Optional[str]:
        eid = self._resolve_element_id(element)
        resp = self._send("get_attribute", elementId=eid, attributeName=attribute_name)
        if not resp.get("success"):
            return None
        return resp["data"].get("value")

    def get_data_grid_content_ocr(self, element: ElementHandle) -> str:
        eid = self._resolve_element_id(element)
        resp = self._send("get_data_grid_content_ocr", elementId=eid)
        if not resp.get("success"):
            return ""
        return resp["data"].get("content", "")

    def capture_screenshot(self, element: Optional[ElementHandle] = None) -> bytes:
        eid = None
        if element is not None:
            eid = self._resolve_element_id(element)
        resp = self._send("capture_screenshot", elementId=eid)
        if not resp.get("success"):
            return b""
        data = resp["data"].get("data")
        if not data:
            return b""
        try:
            return base64.b64decode(data)
        except Exception:
            return data.encode("utf-8") if isinstance(data, str) else b""
