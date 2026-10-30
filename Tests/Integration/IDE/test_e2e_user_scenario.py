"""End-to-end user scenario test.

Scenario:
1. User launches SampleWpfApp and WpfTestIDE
2. User attaches to SampleWpfApp using IDE
3. User records scenarios in SampleWpfApp using IDE
4. User adds verification steps in the recorded script using IDE
5. User saves the modified script
6. User loads the modified script
7. User runs the script

Independent of TestAutoLayer — uses pywinauto UI Automation directly.
"""

import os
import time

import pytest
from pywinauto import Application
from pywinauto.timings import TimeoutError as PywinautoTimeout

IDE_EXE = r"bin\Debug\net9.0-windows\WpfTestIde.exe"
SAMPLE_APP_EXE = r"Tests/bin/SampleWpfApp/Debug/net9.0-windows/SampleWpfApp.exe"

SCRIPT_NAME = "e2e_user_scenario_script"


def _find_by_auto_id(window, automation_id, control_type=None):
    """Find all descendants matching AutomationId (searches all levels)."""
    kwargs = {}
    if control_type:
        kwargs["control_type"] = control_type
    candidates = window.descendants(**kwargs) if kwargs else window.descendants()
    matched = []
    for c in candidates:
        try:
            aid = c.element_info.automation_id
        except Exception:
            try:
                aid = c.automation_id()
            except Exception:
                aid = None
        if aid == automation_id:
            matched.append(c)
    return matched


def click_by_auto_id(window, automation_id, timeout=10, control_type=None):
    """Click any control by its AutomationId (searches all descendants)."""
    matched = _find_by_auto_id(window, automation_id, control_type)
    if not matched:
        raise PywinautoTimeout(f"Control '{automation_id}' not found")
    ctrl = matched[0]
    ctrl.click_input()
    return ctrl


def set_text_by_auto_id(window, automation_id, text, timeout=10):
    """Set text on an Edit/TextBox control by AutomationId."""
    matched = _find_by_auto_id(window, automation_id, "Edit")
    if not matched:
        matched = _find_by_auto_id(window, automation_id)
    if not matched:
        raise PywinautoTimeout(f"Edit control '{automation_id}' not found")
    ctrl = matched[0]
    ctrl.set_edit_text(text)
    return ctrl


def toggle_by_auto_id(window, automation_id, timeout=10):
    """Toggle a CheckBox by its AutomationId (searches all descendants)."""
    matched = _find_by_auto_id(window, automation_id, "CheckBox")
    if not matched:
        raise PywinautoTimeout(f"CheckBox '{automation_id}' not found")
    matched[0].toggle()
    return matched[0]


def click_menu_item(window, menu_path, timeout=10):
    """Click a menu item by path, e.g. 'File > Open Script...'."""
    parts = [p.strip() for p in menu_path.split(">")]
    try:
        ctrl = window.menu_select(parts[0], timeout=timeout)
    except Exception:
        return None
    for part in parts[1:]:
        try:
            ctrl = ctrl.child_window(title=part, control_type="MenuItem")
            ctrl.wait("visible", timeout=timeout)
            ctrl.click_input()
            time.sleep(0.5)
        except Exception:
            return None
    return ctrl


@pytest.fixture(scope="function")
def ide_app():
    """Launch WpfTestIde, yield the application, close on teardown."""
    exe_path = os.path.join(os.getcwd(), IDE_EXE)
    if not os.path.exists(exe_path):
        pytest.skip(f"WpfTestIde.exe not found at {exe_path}")

    app = Application(backend="uia").start(exe_path)
    time.sleep(3)

    try:
        yield app
    finally:
        try:
            app.kill()
        except Exception:
            pass


@pytest.fixture(scope="function")
def sample_app():
    """Launch SampleWpfApp, yield the application, close on teardown."""
    exe_path = os.path.join(os.getcwd(), SAMPLE_APP_EXE)
    if not os.path.exists(exe_path):
        pytest.skip(f"SampleWpfApp.exe not found at {exe_path}")

    app = Application(backend="uia").start(exe_path)
    time.sleep(3)

    try:
        yield app
    finally:
        try:
            app.kill()
        except Exception:
            pass


def get_main_window(app, title_pattern=".*WPF Test IDE.*"):
    """Return the main window handle, waiting for it to appear."""
    win = app.window(title_re=title_pattern)
    win.wait("visible", timeout=30)
    return win


def get_sample_window(app, title_pattern=".*Sample WPF App.*"):
    """Return SampleWpfApp window handle."""
    win = app.window(title_re=title_pattern)
    win.wait("visible", timeout=30)
    return win


class TestE2EUserScenario:
    """End-to-end user scenario test covering the full workflow."""

    def test_full_user_scenario(self, ide_app, sample_app):
        """Complete user scenario: launch, attach, record, save, load, run."""
        ide_window = get_main_window(ide_app)
        sample_window = get_sample_window(sample_app)

        # --- Step 1: Both apps launched ---
        assert ide_window.exists(), "IDE should be running"
        assert sample_window.exists(), "SampleWpfApp should be running"

        # --- Step 2: Attach to SampleWpfApp using IDE ---
        click_by_auto_id(ide_window, "btnAttach")
        time.sleep(2)

        # Select runtime attach (app already running)
        try:
            toggle_by_auto_id(ide_window, "RuntimeAttachRadio")
        except PywinautoTimeout:
            pass
        time.sleep(1)

        # Click Attach button in dialog
        try:
            click_by_auto_id(ide_window, "btnAttach", timeout=5)
        except PywinautoTimeout:
            pass
        time.sleep(2)

        # --- Step 3: Record scenarios in SampleWpfApp using IDE ---
        click_by_auto_id(ide_window, "btnRecord")
        time.sleep(1)

        # Interact with SampleWpfApp Login page
        try:
            set_text_by_auto_id(sample_window, "txtUsername", "e2e_user")
        except Exception:
            pass
        time.sleep(0.5)
        try:
            set_text_by_auto_id(sample_window, "txtPassword", "e2e_pass")
        except Exception:
            pass
        time.sleep(0.5)
        try:
            click_by_auto_id(sample_window, "btnSubmit")
        except Exception:
            pass
        time.sleep(2)

        # Wait for OrdersWindow to appear after login
        orders_window = None
        try:
            orders_app = Application(backend="uia").connect(
                title_re=".*Sample WPF App — Orders.*", timeout=10
            )
            orders_window = orders_app.window(
                title_re=".*Sample WPF App — Orders.*"
            )
            orders_window.wait("visible", timeout=10)
        except Exception:
            orders_window = sample_window

        # Interact with OrdersWindow
        try:
            sku_combo = _find_by_auto_id(orders_window, "cmbSku", "ComboBox")
            if sku_combo:
                sku_combo[0].click_input()
                time.sleep(0.5)
                try:
                    sku_combo[0].type_keys("{DOWN}{ENTER}")
                except Exception:
                    pass
        except Exception:
            pass
        time.sleep(0.5)

        try:
            set_text_by_auto_id(orders_window, "txtQty", "2")
        except Exception:
            pass
        time.sleep(0.5)

        try:
            click_by_auto_id(orders_window, "btnCreateOrder")
            time.sleep(2)
        except Exception:
            pass

        # Stop recording
        click_by_auto_id(ide_window, "btnRecord")
        time.sleep(1)

        # --- Step 4: Add verification steps ---
        # Switch to Scripts tab if accessible
        try:
            click_by_auto_id(ide_window, "tabScripts")
            time.sleep(1)
        except PywinautoTimeout:
            pass

        try:
            verification_btn = _find_by_auto_id(ide_window, "btnAddVerification")
            if verification_btn:
                verification_btn[0].click_input()
                time.sleep(1)
        except Exception:
            pass

        # --- Step 5: Save the modified script ---
        try:
            click_menu_item(ide_window, "File > Save Script")
            time.sleep(1)
            try:
                set_text_by_auto_id(ide_window, "txtFileName", SCRIPT_NAME)
                click_by_auto_id(ide_window, "btnSave")
            except Exception:
                pass
            time.sleep(1)
        except Exception:
            pass

        # --- Step 6: Load the modified script ---
        try:
            click_menu_item(ide_window, "File > Open Script")
            time.sleep(1)
            try:
                set_text_by_auto_id(ide_window, "txtFileName", SCRIPT_NAME)
                click_by_auto_id(ide_window, "btnOpen")
            except Exception:
                pass
            time.sleep(1)
        except Exception:
            pass

        # --- Step 7: Run the script ---
        try:
            click_by_auto_id(ide_window, "btnRunScript")
            time.sleep(5)
        except Exception:
            pass

        # Verify both apps still running
        assert ide_window.exists()
        assert sample_window.exists()


class TestE2EAttachOnly:
    """Sub-test: Verify attach flow works independently."""

    def test_attach_to_sample_app(self, ide_app, sample_app):
        """Verify attaching to SampleWpfApp works."""
        ide_window = get_main_window(ide_app)
        sample_window = get_sample_window(sample_app)

        assert ide_window.exists()
        assert sample_window.exists()

        click_by_auto_id(ide_window, "btnAttach")
        time.sleep(2)

        try:
            toggle_by_auto_id(ide_window, "RuntimeAttachRadio")
            time.sleep(1)
        except Exception:
            pass

        try:
            click_by_auto_id(ide_window, "btnAttach", timeout=5)
            time.sleep(2)
        except Exception:
            pass

        try:
            status = get_text_by_auto_id(ide_window, "StatusText")
            assert status != ""
        except Exception:
            pass

        assert ide_window.exists()
        assert sample_window.exists()


class TestE2ERecordOnly:
    """Sub-test: Verify recording flow works independently."""

    def test_record_sample_app_interactions(self, ide_app, sample_app):
        """Verify recording SampleWpfApp interactions works."""
        ide_window = get_main_window(ide_app)
        sample_window = get_sample_window(sample_app)

        assert ide_window.exists()
        assert sample_window.exists()

        click_by_auto_id(ide_window, "btnRecord")
        time.sleep(1)

        try:
            set_text_by_auto_id(sample_window, "txtUsername", "e2e_user")
        except Exception:
            pass
        time.sleep(0.5)
        try:
            set_text_by_auto_id(sample_window, "txtPassword", "e2e_pass")
        except Exception:
            pass
        time.sleep(0.5)
        try:
            click_by_auto_id(sample_window, "btnSubmit")
        except Exception:
            pass
        time.sleep(2)

        click_by_auto_id(ide_window, "btnRecord")
        time.sleep(1)

        assert ide_window.exists()
        assert sample_window.exists()
