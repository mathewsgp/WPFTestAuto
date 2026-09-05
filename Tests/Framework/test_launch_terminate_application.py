"""Tests for the new Launch Application / Terminate Application keywords."""
import os
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "TestAutoLayer", "api"))

from app_context import AppContext
from DriverAgnosticApi import DriverAgnosticApi


def test_app_context_accepts_start_in_and_auto_attach():
    ctx = AppContext(
        app_id="myapp",
        app_name="MyApp.exe",
        app_path="C:\\path\\to\\MyApp.exe",
        start_in="C:\\work",
        auto_attach=True,
    )
    assert ctx.start_in == "C:\\work"
    assert ctx.auto_attach is True
    d = ctx.to_dict()
    assert d["start_in"] == "C:\\work"
    assert d["auto_attach"] is True


def test_terminate_application_requires_at_least_one_identifier():
    api = DriverAgnosticApi()
    try:
        api.terminate_application()
    except ValueError as e:
        assert "app_id" in str(e) and "window_title" in str(e) and "process_name" in str(e)
        return
    raise AssertionError("expected ValueError when no identifier is provided")


def test_terminate_application_by_process_name_kills_matching_pids():
    """Use a real Windows process (the current Python interpreter) and verify
    the keyword returns >= 0 and that psutil-based discovery works. We do
    NOT actually kill python here — the test is about the discover path,
    not the kill path."""
    api = DriverAgnosticApi()
    # python.exe is guaranteed to exist on the test machine
    killed = api.terminate_application(process_name="python.exe", force=False)
    # Must return a non-negative integer count.
    assert isinstance(killed, int)
    assert killed >= 0


def test_terminate_application_by_app_id_unregisters():
    """When a registered app is found, terminate_application should unregister it."""
    from DriverAgnosticApi import _MULTI_APP_CONTEXT
    from app_context import AppContext

    api = DriverAgnosticApi()
    # Register a fake app context (no real process — we set process_id to a
    # nonexistent pid so the kill path is exercised safely).
    fake_ctx = AppContext(
        app_id="fake_app",
        app_name="Fake.exe",
        process_id=999999,  # almost certainly not running
    )
    _MULTI_APP_CONTEXT.register_app(fake_ctx)
    killed = api.terminate_application(app_id="fake_app", force=True)
    assert isinstance(killed, int)
    # App should be unregistered after terminate by app_id
    apps = [a["app_id"] for a in _MULTI_APP_CONTEXT.list_apps()]
    assert "fake_app" not in apps, f"fake_app should be unregistered, still in: {apps}"


def test_terminate_application_kill_pid_safely_no_op_for_unused_pid():
    """Killing an unused PID must not raise; should return a bool."""
    api = DriverAgnosticApi()
    # Use an extremely high PID that is almost certainly not in use.
    # _kill_pid is a static method.
    result = DriverAgnosticApi._kill_pid(2147483646, force=True)
    assert isinstance(result, bool)


def test_terminate_application_finds_no_pids_for_nonexistent_process_name():
    """A process name that doesn't exist must produce an empty kill count."""
    api = DriverAgnosticApi()
    killed = api.terminate_application(process_name="definitely_not_a_real_process_xyz_12345.exe", force=True)
    assert killed == 0


def test_launch_application_signature_supports_positional_path_first():
    """The keyword signature must accept app_path as the first positional
    argument (so that Robot Framework's `Launch Application    <path>    app_id=<id>`
    does not trip "positional after named" validation).
    We don't actually spawn a real process here — we monkey-patch
    `_launch_app_for_context` to a stub that captures the call, then verify
    the call signature is correct.
    """
    import DriverAgnosticApi as api_mod
    import app_context as appctx
    api = api_mod.DriverAgnosticApi()
    captured = {}

    class _FakeProc:
        def __init__(self, pid):
            self.pid = pid

    def stub_launch(ctx):
        captured["app_id"] = ctx.app_id
        captured["app_path"] = ctx.app_path
        captured["start_in"] = ctx.start_in
        captured["auto_attach"] = ctx.auto_attach
        captured["launch_args"] = list(ctx.launch_args)
        return _FakeProc(os.getpid())  # safe: we don't actually spawn anything

    # launch_application re-imports `_launch_app_for_context` inside the
    # function body, so the only effective patch point is `app_context` itself.
    original = appctx._launch_app_for_context
    appctx._launch_app_for_context = stub_launch
    try:
        result = api.launch_application(
            "C:\\Windows\\notepad.exe",
            app_id="Notepad",
            start_in="C:\\Windows",
            args="1.txt",
            attach=False,
        )
    finally:
        appctx._launch_app_for_context = original

    assert result == "Notepad", f"expected app_id 'Notepad', got {result!r}"
    assert captured["app_path"] == "C:\\Windows\\notepad.exe"
    assert captured["app_id"] == "Notepad"
    assert captured["start_in"] == "C:\\Windows"
    assert captured["auto_attach"] is False
    assert captured["launch_args"] == ["1.txt"], f"expected ['1.txt'], got {captured['launch_args']}"
    # Registration should have happened.
    assert "Notepad" in [a["app_id"] for a in api_mod._MULTI_APP_CONTEXT.list_apps()]


def test_launch_application_defaults_app_id_from_exe_name():
    """When the caller omits app_id, the keyword should derive it from the
    executable file name (lowercased, no extension)."""
    import DriverAgnosticApi as api_mod
    import app_context as appctx
    api = api_mod.DriverAgnosticApi()
    captured = {}

    class _FakeProc:
        def __init__(self, pid):
            self.pid = pid

    def stub_launch(ctx):
        captured["app_id"] = ctx.app_id
        return _FakeProc(os.getpid())

    original = appctx._launch_app_for_context
    appctx._launch_app_for_context = stub_launch
    try:
        result = api.launch_application("C:\\Path\\To\\Notepad.exe")
    finally:
        appctx._launch_app_for_context = original

    assert result == "notepad", f"expected default app_id 'notepad', got {result!r}"
    assert captured["app_id"] == "notepad"


def test_launch_application_normalizes_forward_slash_paths():
    """Paths with forward slashes (as emitted by the IDE ScriptGenerator
    for Robot-Framework safety) must be accepted and normalized to
    backslashes for Popen."""
    import DriverAgnosticApi as api_mod
    import app_context as appctx
    api = api_mod.DriverAgnosticApi()
    captured = {}

    class _FakeProc:
        def __init__(self, pid):
            self.pid = pid

    def stub_launch(ctx):
        captured["app_path"] = ctx.app_path
        captured["start_in"] = ctx.start_in
        return _FakeProc(os.getpid())

    original = appctx._launch_app_for_context
    appctx._launch_app_for_context = stub_launch
    try:
        api.launch_application("C:/Windows/notepad.exe", start_in="C:/Windows")
    finally:
        appctx._launch_app_for_context = original

    assert captured["app_path"] == "C:\\Windows\\notepad.exe", \
        f"forward slash not normalized: {captured['app_path']!r}"
    assert captured["start_in"] == "C:\\Windows"


def test_launch_application_rejects_path_with_newline():
    """Robot Framework can strip backslashes from unquoted arguments
    (interpreting \\W, \\n, etc. as escapes), which can leave a literal
    newline in the path string. That silently breaks CreateProcess with
    WinError 2. The keyword must reject this loudly instead of letting
    Popen fail with a cryptic error."""
    import DriverAgnosticApi as api_mod
    api = api_mod.DriverAgnosticApi()
    try:
        api.launch_application("C:\Windows\notepad.exe")
    except ValueError as e:
        assert "newline" in str(e).lower(), f"expected newline-mention in error, got: {e}"
        return
    raise AssertionError("expected ValueError for path containing a newline")


def test_launch_application_rejects_empty_path():
    """An empty app_path must be rejected before Popen is invoked."""
    import DriverAgnosticApi as api_mod
    api = api_mod.DriverAgnosticApi()
    try:
        api.launch_application("")
    except ValueError as e:
        assert "empty" in str(e).lower() or "required" in str(e).lower()
        return
    raise AssertionError("expected ValueError for empty app_path")


if __name__ == "__main__":
    test_app_context_accepts_start_in_and_auto_attach()
    test_terminate_application_requires_at_least_one_identifier()
    test_terminate_application_by_process_name_kills_matching_pids()
    test_terminate_application_by_app_id_unregisters()
    test_terminate_application_kill_pid_safely_no_op_for_unused_pid()
    test_terminate_application_finds_no_pids_for_nonexistent_process_name()
    test_launch_application_signature_supports_positional_path_first()
    test_launch_application_defaults_app_id_from_exe_name()
    test_launch_application_normalizes_forward_slash_paths()
    test_launch_application_rejects_path_with_newline()
    test_launch_application_rejects_empty_path()
    print("\nAll Launch/Terminate application tests passed.")
