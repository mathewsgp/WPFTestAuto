"""
Shared utilities for app_management package.
"""

import ctypes
from ctypes import wintypes
from dataclasses import dataclass
from typing import Optional


@dataclass
class PIDHolder:
    """Holder for a PID found by callback."""
    pid: Optional[int] = None


class PIDHolderCTypes(ctypes.Structure):
    """ctypes Structure for Windows EnumWindows callback."""
    _fields_ = [("pid", wintypes.DWORD), ("hwnd", wintypes.HWND)]