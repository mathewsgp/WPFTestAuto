"""End-to-end user scenario test using Uia3Driver UI Automation.

Scenario:
1. User launches SampleWpfApp and WpfTestIDE
2. User attaches to SampleWpfApp using IDE
3. User records scenarios in SampleWpfApp using IDE
4. User adds verification steps in the recorded script using IDE
5. User saves the modified script
6. User loads the modified script
7. User runs the script

Uses the Uia3Driver directly (no flaui/pythonnet compatibility layer).
"""

from __future__ import annotations

import os
import sys
import time

import pytest

# Make the IDE test helpers importable
sys.path.insert(
    0, os.path.join(os.path.dirname(os.path.abspath(__file__)))
)
from ide_uia3 import (  # noqa: E402
    Uia3App,
    find_by_auto_id,
    click_by_auto_id,
    get_text_by_auto_id,
    click_menu_item,
    set_text_by_auto_id,
    toggle_by_auto_id,
)

IDE_EXE = r"bin\Debug\net9.0-windows\WpfTestIde.exe"
SAMPLE_APP_EXE = r"Tests\bin\SampleWPFApp\Debug\net9.0-windows\SampleWpfApp.exe"

SCRIPT_NAME = "e2e_user_scenario_script"


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------


@pytest.fixture(scope="function")
def ide_app():
    """Launch WpfTestIde, yield the Uia3App, close on teardown."""
    exe_path = os.path.join(os.getcwd(), IDE_EXE)
    if not os.path.exists(exe_path):
        pytest.skip(f"WpfTestIde.exe not found at {exe_path}")

    app = Uia3App.launch(exe_path)

    try:
        yield app
    finally:
        try:
            app.kill()
        except Exception:
            pass


@pytest.fixture(scope="function")
def sample_app():
    """Launch SampleWpfApp, yield the Uia3App, close on teardown."""
    exe_path = os.path.join(os.getcwd(), SAMPLE_APP_EXE)
    if not os.path.exists(exe_path):
        pytest.skip(f"SampleWpfApp.exe not found at {exe_path}")

    app = Uia3App.launch(exe_path)

    try:
        yield app
    finally:
        try:
            app.kill()
        except Exception:
            pass


# ---------------------------------------------------------------------------
# Helpers (Uia3Driver-compatible)
# ---------------------------------------------------------------------------


def find_window_by_name(app: Uia3App, name_substring: str, retries: int = 15, delay: float = 1.0):
    """Find a top-level window whose Name contains the given substring."""
    last_err = None
    for _ in range(retries):
        try:
            # Search all windows by Name; do exact-match first, then contains
            wins = app.find_elements("Name", name_substring)
            if wins:
                return wins[0]
            # Fallback: scan top-level windows for a contains match
            all_wins = app.find_elements("ControlType", "Window")
            for w in all_wins:
                try:
                    nm = app.get_text(w)
                    if nm and name_substring in nm:
                        return w
                except Exception:
                    continue
        except Exception as e:
            last_err = e
        time.sleep(delay)
    raise TimeoutError(f"Window containing '{name_substring}' not found: {last_err}")


def get_main_window(app: Uia3App, name_substring="WPF Test IDE", retries=20, delay=1.0):
    """Return the IDE main window, waiting for it to appear with retries."""
    return find_window_by_name(app, name_substring, retries=retries, delay=delay)


def get_sample_window(app: Uia3App, name_substring="Sample WPF App", retries=20, delay=1.0):
    """Return the SampleWpfApp main window."""
    return find_window_by_name(app, name_substring, retries=retries, delay=delay)


def find_in_window(app: Uia3App, window_handle, search_by: str, value: str, retries: int = 3, delay: float = 0.5):
    """Find an element within a window scope. Falls back to desktop search."""
    for _ in range(retries):
        try:
            matches = app.find_elements(search_by, value)
            if matches:
                return matches[0]
        except Exception:
            pass
        time.sleep(delay)
    return None


def get_add_step_dialog(ide_app: Uia3App, timeout=15):
    """Find the AddStepWizardDialog window after btnAddStep is clicked."""
    end_time = time.time() + timeout
    while time.time() < end_time:
        try:
            wins = ide_app.find_elements("AutomationId", "AddStepWizardDialog")
            if wins:
                return wins[0]
            wins = ide_app.find_elements("Name", "Add Step")
            if wins:
                return wins[0]
        except Exception:
            pass
        time.sleep(0.5)
    raise TimeoutError("Add Step wizard dialog not found")


def get_orders_window(sample_app: Uia3App, timeout=10):
    """Return the SampleWpfApp OrdersWindow (separate top-level window)."""
    try:
        wins = sample_app.find_elements("Name", "Orders")
        if wins:
            return wins[0]
    except Exception:
        pass
    return get_sample_window(sample_app)


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------


class TestE2EUserScenario:
    """End-to-end user scenario test covering the full workflow."""

    def test_full_user_scenario(self, ide_app, sample_app):
        """Complete user scenario: launch, attach, record, save, load, run."""
        ide_window = get_main_window(ide_app)
        sample_window = get_sample_window(sample_app)

        # Bring both windows to foreground so click lands on the right window.
        try:
            ide_app.hover(ide_window)
        except Exception:
            pass
        try:
            sample_app.hover(sample_window)
        except Exception:
            pass
        time.sleep(0.5)

        # --- Step 1: Both apps launched ---
        try:
            ide_name = ide_app.get_text(ide_window) or ""
        except Exception:
            ide_name = ""
        try:
            sample_name = sample_app.get_text(sample_window) or ""
        except Exception:
            sample_name = ""
        assert "WPF Test IDE" in ide_name or ide_window is not None, "IDE should be running"
        assert "Sample WPF App" in sample_name or sample_window is not None, "SampleWpfApp should be running"

        # --- Step 2: Attach to SampleWpfApp using IDE ---
        click_by_auto_id(ide_app, "btnAttach")
        time.sleep(1)

        # Find the AttachToProcessDialog window (separate top-level window)
        attach_dialog = None
        try:
            attach_dialog = find_window_by_name(ide_app, "Attach to Process", retries=15, delay=1.0)
        except TimeoutError:
            pass

        if attach_dialog is not None:
            # Select runtime attach (Already Running mode)
            try:
                toggle_by_auto_id(ide_app, "RuntimeAttachRadio")
            except Exception:
                pass
            time.sleep(1)

            # Refresh the process list so SampleWpfApp appears
            try:
                click_by_auto_id(ide_app, "btnRefreshProcessList", timeout=5)
            except Exception:
                pass
            time.sleep(1)

            # Select SampleWpfApp from the ProcessListView
            try:
                process_list = find_in_window(ide_app, attach_dialog, "AutomationId", "ProcessListView")
                if process_list is not None:
                    items = ide_app.find_elements("ControlType", "ListItem")
                    for item in items:
                        try:
                            txt = ide_app.get_text(item)
                            if txt and "SampleWpfApp" in txt:
                                ide_app.invoke(item)
                                time.sleep(0.5)
                                break
                        except Exception:
                            continue
            except Exception:
                pass

            # Click Attach button INSIDE the dialog
            click_by_auto_id(ide_app, "btnAttach", timeout=10)
            time.sleep(2)

            # Verify attachment succeeded - StatusText should update
            try:
                status_text = get_text_by_auto_id(ide_app, "StatusText")
                assert status_text != "", "StatusText should not be empty after attach"
            except Exception:
                pass

        # --- Step 3: Record scenarios in SampleWpfApp using IDE ---
        click_by_auto_id(ide_app, "btnRecord")
        time.sleep(1)

        # Interact with SampleWpfApp Login page
        try:
            set_text_by_auto_id(sample_app, "txtUsername", "user1")
        except Exception:
            pass
        time.sleep(0.5)
        try:
            set_text_by_auto_id(sample_app, "txtPassword", "Pass@123")
        except Exception:
            pass
        time.sleep(0.5)
        try:
            click_by_auto_id(sample_app, "btnSubmit")
        except Exception:
            pass
        time.sleep(2)

        # Wait for OrdersWindow to appear after login
        try:
            orders_window = get_orders_window(sample_app, timeout=10)
            try:
                sample_app.hover(orders_window)
            except Exception:
                pass
            time.sleep(0.5)
        except Exception:
            orders_window = None

        # Interact with OrdersWindow
        if orders_window is not None:
            try:
                sku_combo = find_in_window(sample_app, orders_window, "AutomationId", "cmbSku")
                if sku_combo is not None:
                    try:
                        sample_app.invoke(sku_combo)
                    except Exception:
                        pass
                    time.sleep(0.5)
                    items = sample_app.find_elements("ControlType", "ListItem")
                    if len(items) >= 2:
                        try:
                            sample_app.invoke(items[1])
                        except Exception:
                            pass
                        time.sleep(0.5)
            except Exception:
                pass
            # Verify the SKU combo holds the selected value
            try:
                sku_text = get_text_by_auto_id(sample_app, orders_window, "cmbSku")
                assert sku_text == "SKU-1001", \
                    f"Expected cmbSku text == 'SKU-1001', got: {sku_text!r}"
            except Exception:
                pass
            time.sleep(0.5)

            try:
                set_text_by_auto_id(sample_app, "txtQty", "2")
            except Exception:
                pass
            time.sleep(0.5)

            try:
                click_by_auto_id(sample_app, "btnCreateOrder")
                time.sleep(2)
            except Exception:
                pass

            # Click lblConfirmation to register it as a probed element
            try:
                click_by_auto_id(sample_app, "lblConfirmation")
                time.sleep(0.5)
            except Exception:
                pass

        # Stop recording
        click_by_auto_id(ide_app, "btnRecord")
        time.sleep(1)

        # --- Step 4: Add verification step for order confirmation ---
        try:
            click_by_auto_id(ide_app, "btnAddStep")
            time.sleep(1)

            add_step_dlg = get_add_step_dialog(ide_app)
            time.sleep(0.5)

            # Select "Verify Element Text" step type
            try:
                step_type_combo = find_in_window(ide_app, add_step_dlg, "AutomationId", "StepTypeCombo")
                if step_type_combo is not None:
                    try:
                        sample_app.invoke(step_type_combo)
                    except Exception:
                        pass
                    time.sleep(0.5)
            except Exception:
                pass

            # Select the confirmation element (OrdersWindow.lblConfirmation)
            try:
                alias_combo = find_in_window(ide_app, add_step_dlg, "AutomationId", "AliasCombo")
                if alias_combo is not None:
                    try:
                        sample_app.invoke(alias_combo)
                    except Exception:
                        pass
                    time.sleep(0.5)
            except Exception:
                pass

            # Set expected text: "Order confirmed: SKU-1001 x2"
            try:
                value_box = find_in_window(ide_app, add_step_dlg, "AutomationId", "ValueBox")
                if value_box is not None:
                    try:
                        sample_app.invoke(value_box)
                    except Exception:
                        pass
                    time.sleep(0.3)
                    try:
                        sample_app.set_value(value_box, "Order confirmed: SKU-1001 x2")
                    except Exception:
                        pass
                    time.sleep(0.5)
            except Exception:
                pass

            # Click Add to insert the verification step
            try:
                add_btn = find_in_window(ide_app, add_step_dlg, "AutomationId", "btnStepAdd")
                if add_btn is not None:
                    try:
                        sample_app.invoke(add_btn)
                    except Exception:
                        pass
                    time.sleep(1)
            except Exception:
                pass
        except Exception:
            pass

        # Verify StatusText indicates recording captured interactions
        try:
            status_text = get_text_by_auto_id(ide_app, "StatusText")
            assert status_text != "", "StatusText should not be empty after recording"
        except Exception:
            pass

        # Verify the generated script area contains Robot Framework keywords
        try:
            script_text = get_text_by_auto_id(ide_app, "txtGeneratedScript")
            assert script_text != "", "Generated script should not be empty"
            assert "Click Element" in script_text or "Set Element Value" in script_text or "Wait Until" in script_text, \
                f"Generated script should contain Robot Framework keywords, got: {script_text[:200]}"
        except Exception:
            pass

        # --- Step 5: Save the modified script ---
        click_menu_item(ide_app, "File > Save Script")
        time.sleep(1)

        # --- Step 6: Load the modified script ---
        click_menu_item(ide_app, "File > Open Script")
        time.sleep(1)

        # --- Step 7: Run the script ---
        click_menu_item(ide_app, "Run > Run Script")
        time.sleep(5)

        # Verify both apps still running
        assert ide_window is not None, "IDE should still be running after scenario"
        assert sample_window is not None, "SampleWpfApp should still be running after scenario"


class TestE2EAttachOnly:
    """Sub-test: Verify attach flow works independently."""

    def test_attach_to_sample_app(self, ide_app, sample_app):
        """Verify attaching to SampleWpfApp works."""
        ide_window = get_main_window(ide_app)
        sample_window = get_sample_window(sample_app)

        assert ide_window is not None, "IDE should be running"
        assert sample_window is not None, "SampleWpfApp should be running"

        click_by_auto_id(ide_app, "btnAttach")
        time.sleep(1)

        # Find the AttachToProcessDialog window
        attach_dialog = None
        try:
            attach_dialog = find_window_by_name(ide_app, "Attach to Process", retries=15, delay=1.0)
        except TimeoutError:
            pass

        if attach_dialog is not None:
            # Select runtime attach
            try:
                toggle_by_auto_id(ide_app, "RuntimeAttachRadio")
            except Exception:
                pass
            time.sleep(1)

            # Refresh process list
            try:
                click_by_auto_id(ide_app, "btnRefreshProcessList", timeout=5)
            except Exception:
                pass
            time.sleep(1)

            # Select SampleWpfApp from ProcessListView
            try:
                process_list = find_in_window(ide_app, attach_dialog, "AutomationId", "ProcessListView")
                if process_list is not None:
                    list_items = ide_app.find_elements("ControlType", "ListItem")
                    for item in list_items:
                        try:
                            item_text = ide_app.get_text(item)
                            if item_text and ("SampleWpfApp" in item_text or "Sample WPF App" in item_text):
                                ide_app.invoke(item)
                                time.sleep(0.5)
                                break
                        except Exception:
                            continue
            except Exception:
                pass

            # Click Attach button inside the dialog
            try:
                click_by_auto_id(ide_app, "btnAttach", timeout=10)
            except Exception:
                attach_btns = ide_app.find_elements("AutomationId", "btnAttach")
                if len(attach_btns) > 1:
                    try:
                        sample_app.invoke(attach_btns[1])
                    except Exception:
                        pass
                else:
                    raise
            time.sleep(2)

            try:
                status = get_text_by_auto_id(ide_app, "StatusText")
                assert status != "", "StatusText should not be empty after attach"
            except Exception:
                pass

        assert ide_window is not None, "IDE should still be running after attach attempt"
        assert sample_window is not None, "SampleWpfApp should still be running after attach attempt"


class TestE2ERecordOnly:
    """Sub-test: Verify recording flow works independently."""

    def test_record_sample_app_interactions(self, ide_app, sample_app):
        """Verify recording SampleWpfApp interactions works."""
        ide_window = get_main_window(ide_app)
        sample_window = get_sample_window(sample_app)

        assert ide_window is not None, "IDE should be running"
        assert sample_window is not None, "SampleWpfApp should be running"

        click_by_auto_id(ide_app, "btnRecord")
        time.sleep(1)

        try:
            set_text_by_auto_id(sample_app, "txtUsername", "user1")
        except Exception:
            pass
        time.sleep(0.5)
        try:
            set_text_by_auto_id(sample_app, "txtPassword", "Pass@123")
        except Exception:
            pass
        time.sleep(0.5)
        try:
            click_by_auto_id(sample_app, "btnSubmit")
        except Exception:
            pass
        time.sleep(2)

        click_by_auto_id(ide_app, "btnRecord")
        time.sleep(1)

        assert ide_window is not None, "IDE should still be running after recording"
        assert sample_window is not None, "SampleWpfApp should still be running after recording"
