"""Pytest fixtures for standalone IDE tests.

Launches WpfTestIde.exe directly via flaui-uiautomation-wrapper UI Automation.
Zero dependency on TestAutoLayer and no pywinauto compatibility shim — the
fixtures use the flaui wrapper directly (see ide_flaui.py).
"""

import os
import sys
import time

import pytest

# Make the IDE test helpers importable
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from ide_flaui import FlauiApp  # noqa: E402


IDE_EXE = r"bin\Debug\net9.0-windows\WpfTestIde.exe"


@pytest.fixture(scope="function")
def ide_app():
    """Launch WpfTestIde, yield the FlauiApp, close on teardown."""
    exe_path = os.path.join(os.getcwd(), IDE_EXE)
    if not os.path.exists(exe_path):
        pytest.skip(f"WpfTestIde.exe not found at {exe_path}")

    app = FlauiApp()
    app.launch(exe_path)
    # Give the window time to render
    time.sleep(3)

    try:
        yield app
    finally:
        try:
            app.kill()
        except Exception:
            pass


def get_main_window(app):
    """Return the main window, waiting for it to appear."""
    win = app.main_window(title_pattern=".*WPF Test IDE.*", timeout=30)
    win.wait_visible(timeout=30)
    return win


@pytest.fixture
def main_window(ide_app):
    return get_main_window(ide_app)
