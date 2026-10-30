"""Pytest fixtures for standalone IDE tests.

Launches WpfTestIde.exe directly via flaui-uiautomation-wrapper UI Automation.
Zero dependency on TestAutoLayer framework.
"""

import os
import sys
import time

import pytest

# Make TestAutoLayer/api importable for the flaui adapter
sys.path.insert(
    0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "TestAutoLayer", "api")
)
from flaui_adapter import Application  # noqa: E402


IDE_EXE = r"bin\Debug\net9.0-windows\WpfTestIde.exe"


@pytest.fixture(scope="function")
def ide_app():
    """Launch WpfTestIde, yield the application, close on teardown."""
    exe_path = os.path.join(os.getcwd(), IDE_EXE)
    if not os.path.exists(exe_path):
        pytest.skip(f"WpfTestIde.exe not found at {exe_path}")

    app = Application(backend="uia").start(exe_path)
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
    """Return the main window handle, waiting for it to appear."""
    win = app.window(title_re=".*WPF Test IDE.*")
    win.wait("visible", timeout=30)
    return win


@pytest.fixture
def main_window(ide_app):
    return get_main_window(ide_app)
