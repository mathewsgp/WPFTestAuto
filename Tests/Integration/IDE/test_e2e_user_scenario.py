"""End-to-end user scenario test using pywinauto (independent of TestAutoLayer)."""

import os
import time

import pytest
from pywinauto import Application, Desktop
from pywinauto.timings import TimeoutError as PywinautoTimeout

IDE_EXE = r"bin\Debug\net9.0-windows\WpfTestIde.exe"
SAMPLE_APP_EXE = r"Tests/bin/SampleWpfApp/Debug/net9.0-windows/SampleWpfApp.exe"


def _find_by_auto_id(window, automation_id, control_type=None):
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
    matched = _find_by_auto_id(window, automation_id, control_type)
    if not matched:
        raise PywinautoTimeout(f"Control '{automation_id}' not found")
    ctrl = matched[0]
    try:
        ctrl.invoke()
    except Exception:
        ctrl.click_input()
    return ctrl


def set_text_by_auto_id(window, automation_id, text, timeout=10):
    matched = _find_by_auto_id(window, automation_id, "Edit")
    if not matched:
        matched = _find_by_auto_id(window, automation_id)
    if not matched:
        raise PywinautoTimeout(f"Edit control '{automation_id}' not found")
    ctrl = matched[0]
    ctrl.set_edit_text(text)
    return ctrl


def toggle_by_auto_id(window, automation_id, timeout=10):
    matched = _find_by_auto_id(window, automation_id, "CheckBox")
    if not matched:
        raise PywinautoTimeout(f"CheckBox '{automation_id}' not found")
    matched[0].toggle()
    return matched[0]


def get_text_by_auto_id(window, automation_id, timeout=10):
    matched = _find_by_auto_id(window, automation_id)
    if not matched:
        raise PywinautoTimeout(f"Control '{automation_id}' not found")
    return matched[0].window_text()


def click_menu_item(window, menu_path, timeout=10):
    parts = [p.strip() for p in menu_path.split(">")]
    top = parts[0]
    sub = parts[1] if len(parts) > 1 else None

    try:
        window.menu_select(top, timeout=timeout)
        if sub:
            window.menu_select(" -> ".join(parts), timeout=timeout)
        return
    except Exception:
        pass

    shortcut_map = {
        ("File", "Save Script"): "^s",
        ("File", "Open Script"): "^o",
        ("Run", "Run Script"): "^r",
    }
    key = (top, sub)
    if key in shortcut_map:
        window.type_keys(shortcut_map[key])
        return

    raise PywinautoTimeout(f"Menu '{menu_path}' not found")


@pytest.fixture(scope="function")
def ide_app():
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
    win = app.window(title_re=title_pattern)
    win.wait("visible", timeout=30)
    return win


def get_sample_window(app, title_pattern=".*Sample WPF App.*"):
    win = app.window(title_re=title_pattern)
    win.wait("visible", timeout=30)
    return win


class TestE2EUserScenario:
    def test_full_user_scenario(self, ide_app, sample_app):
        ide_window = get_main_window(ide_app)
        sample_window = get_sample_window(sample_app)

        assert ide_window.exists(), "IDE should be running"
        assert sample_window.exists(), "SampleWpfApp should be running"

        # Step 2: Attach to SampleWpfApp using IDE
        click_by_auto_id(ide_window, "btnAttach")
        time.sleep(2)

        attach_dialog = None
        end_time = time.time() + 15
        while time.time() < end_time:
            try:
                for w in Desktop(backend="uia").windows():
                    try:
                        title = w.window_text()
                        if "Attach to Process" in title:
                            w.wait("visible", timeout=5)
                            attach_dialog = w
                            break
                    except Exception:
                        continue
            except Exception:
                pass
            if attach_dialog:
                break
            time.sleep(0.5)

        if not attach_dialog:
            from pywinauto import Desktop
            desktop = Desktop(backend="uia")
            all_titles = [w.window_text() for w in desktop.windows()]
            safe_titles = [t.encode('ascii', errors='replace').decode('ascii') for t in all_titles]
            print(f"\nDEBUG: Desktop windows after btnAttach click: {safe_titles}")
            raise PywinautoTimeout("Attach to Process dialog not found")

        try:
            toggle_by_auto_id(attach_dialog, "RuntimeAttachRadio")
        except PywinautoTimeout:
            pass
        time.sleep(1)

        try:
            click_by_auto_id(attach_dialog, "btnRefreshProcessList", timeout=5)
        except PywinautoTimeout:
            pass
        time.sleep(1)

        process_list = _find_by_auto_id(attach_dialog, "ProcessListView")
        if not process_list:
            raise PywinautoTimeout("ProcessListView not found")

        list_view = process_list[0]
        list_item = None
        end_time = time.time() + 15
        while time.time() < end_time:
            try:
                all_desc = attach_dialog.descendants()
                for d in all_desc:
                    try:
                        d_cls = d.class_name()
                        d_text = d.window_text()
                        if "ListItem" in d_cls:
                            d.click_input()
                            time.sleep(0.5)
                            list_item = d
                            break
                    except Exception:
                        continue
                if list_item:
                    break
            except Exception:
                pass
            time.sleep(0.5)

        if not list_item:
            raise PywinautoTimeout("No items found in ProcessListView")

        list_item.click_input()
        time.sleep(0.5)

        click_by_auto_id(attach_dialog, "btnAttach", timeout=10)
        time.sleep(2)

        try:
            status_text = get_text_by_auto_id(ide_window, "StatusText")
            assert status_text != "", "StatusText should not be empty after attach"
        except PywinautoTimeout:
            pass

        click_by_auto_id(ide_window, "btnRecord")
        time.sleep(1)

        set_text_by_auto_id(sample_window, "txtUsername", "user1")
        time.sleep(0.5)
        set_text_by_auto_id(sample_window, "txtPassword", "Pass@123")
        time.sleep(0.5)
        click_by_auto_id(sample_window, "btnSubmit")
        time.sleep(2)

        try:
            orders_app = Application(backend="uia").connect(
                title_re=".*Sample WPF App — Orders.*", timeout=10
            )
            orders_window = orders_app.window(title_re=".*Sample WPF App — Orders.*")
            orders_window.wait("visible", timeout=10)
            assert orders_window.exists()
        except Exception as e:
            raise AssertionError(f"Login failed: {e}")

        click_by_auto_id(ide_window, "btnRecord")
        time.sleep(1)

        click_menu_item(ide_window, "File > Save Script")
        time.sleep(1)

        click_menu_item(ide_window, "File > Open Script")
        time.sleep(1)

        click_menu_item(ide_window, "Run > Run Script")
        time.sleep(5)

        assert ide_window.exists()
        assert sample_window.exists()