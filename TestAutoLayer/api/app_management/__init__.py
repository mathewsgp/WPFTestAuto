"""
App Management Package
======================
Provides multi-application automation support:

- app_registry: AppContext, MultiAppContext
- app_launcher: launch_application, attach_to_application, wait_for_application
- window_activator: activate_window
- app_terminator: terminate_application, kill_pid
"""

from .app_registry import (
    AppContext,
    MultiAppContext,
    get_multi_app_context,
    Constants,
)

from .app_launcher import (
    launch_application,
    attach_to_application,
    wait_for_application,
    close_application,
    create_driver_for_app,
    launch_app_for_context,
)

from .window_activator import (
    activate_window,
)

from .app_terminator import (
    terminate_application,
    kill_pid,
    find_pids_by_title_or_name,
)

__all__ = [
    # App Registry
    "AppContext",
    "MultiAppContext",
    "get_multi_app_context",
    "Constants",
    # App Launcher
    "launch_application",
    "attach_to_application",
    "wait_for_application",
    "close_application",
    "create_driver_for_app",
    "launch_app_for_context",
    # Window Activator
    "activate_window",
    # App Terminator
    "terminate_application",
    "kill_pid",
    "find_pids_by_title_or_name",
]