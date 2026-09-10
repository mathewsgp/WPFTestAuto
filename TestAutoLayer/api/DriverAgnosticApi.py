"""
DriverAgnosticApi - Backward Compatibility Wrapper
===================================================
This module provides backward compatibility for existing imports.
It re-exports the new thin facade and global state.
"""

import sys
import os

# Ensure the api directory is in the path
_THIS_DIR = os.path.dirname(os.path.abspath(__file__))
if _THIS_DIR not in sys.path:
    sys.path.insert(0, _THIS_DIR)

# Re-export the new thin facade
from driver_agnostic_api import DriverAgnosticApi

# Re-export global state for backward compatibility
from driver_agnostic_api import (
    _ACTIVE_DRIVER,
    _ACTIVE_MODE,
    _WPFSPY_MODE,
    _SAMPLE_WPF_APP_PROCESS,
    _DRIVERS,
    _DRIVERS_INITIALIZED,
    _RUN_MODES,
    _DRIVER_PRIORITY,
    _MULTI_APP_CONTEXT,
    _breaker_manager,
    logger,
    config,
    _get_drivers,
    _create_wpfspy_driver,
    _reload_drivers,
    _get_run_modes,
    set_active_driver,
    set_active_mode,
    set_run_modes,
    set_driver_priority,
    _reset_real_app,
    _kill_pid,
)

# Also expose the app context classes
from app_management.app_registry import (
    AppContext,
    MultiAppContext,
    get_multi_app_context,
    Constants,
)

__all__ = [
    "DriverAgnosticApi",
    "_ACTIVE_DRIVER",
    "_ACTIVE_MODE",
    "_WPFSPY_MODE",
    "_SAMPLE_WPF_APP_PROCESS",
    "_DRIVERS",
    "_DRIVERS_INITIALIZED",
    "_RUN_MODES",
    "_DRIVER_PRIORITY",
    "_MULTI_APP_CONTEXT",
    "_breaker_manager",
    "logger",
    "config",
    "_get_drivers",
    "_create_wpfspy_driver",
    "_reload_drivers",
    "_get_run_modes",
    "set_active_driver",
    "set_active_mode",
    "set_run_modes",
    "set_driver_priority",
    "_reset_real_app",
    "_kill_pid",
    "AppContext",
    "MultiAppContext",
    "get_multi_app_context",
    "Constants",
    "_THIS_DIR",
]