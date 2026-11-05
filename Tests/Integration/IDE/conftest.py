"""Pytest fixtures for standalone IDE tests.

Launches WpfTestIde.exe directly via Uia3Driver UI Automation.
Zero dependency on TestAutoLayer and no flaui/pythonnet compatibility shim —
the fixtures use the Uia3Driver directly (see ide_uia3.py).
"""

import os
import sys
import time

import pytest

# Make the IDE test helpers importable
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from ide_uia3 import Uia3App  # noqa: E402


IDE_EXE = r"bin\Debug\net9.0-windows\WpfTestIde.exe"


@pytest.fixture(scope="function")
def ide_app():
    """Launch WpfTestIde, yield the Uia3App, close on teardown."""
    exe_path = os.path.join(os.getcwd(), IDE_EXE)
    if not os.path.exists(exe_path):
        pytest.skip(f"WpfTestIde.exe not found at {exe_path}")

    app = Uia3App.launch(exe_path, wait=5.0)

    try:
        yield app
    finally:
        try:
            app.kill()
        except Exception:
            pass


def get_main_window(app, retries=10, retry_delay=1.0):
    """Return the main window, waiting for it to appear with retries."""
    last_err = None
    for _ in range(retries):
        try:
            win = app.find_element("AutomationId", "MainWindow")
            if win is not None:
                return win
        except Exception as e:
            last_err = e
        time.sleep(retry_delay)
    raise TimeoutError(f"Main window not found after {retries} retries: {last_err}")


@pytest.fixture
def main_window(ide_app):
    return get_main_window(ide_app)
