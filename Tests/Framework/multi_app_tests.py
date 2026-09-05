"""Tests for multi-application automation support."""
import sys
import os

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "TestAutoLayer", "api"))

from app_context import AppContext, MultiAppContext


def test_multi_app_context_registration():
    ctx = MultiAppContext()
    
    app1 = AppContext(app_id="main", app_name="SampleWpfApp", driver="FlaUI", process_id=1234)
    app2 = AppContext(app_id="helper", app_name="HelperApp", driver="FlaUI", process_id=5678)
    
    ctx.register_app(app1)
    ctx.register_app(app2)
    
    assert ctx.get_app("main").app_name == "SampleWpfApp"
    assert ctx.get_app("helper").process_id == 5678
    assert ctx.default_app_id == "main"
    
    apps = ctx.list_apps()
    assert len(apps) == 2
    print("test_multi_app_context_registration: PASS")


def test_multi_app_context_default():
    ctx = MultiAppContext()
    
    app1 = AppContext(app_id="main", app_name="Main", driver="FlaUI")
    ctx.register_app(app1)
    
    ctx.set_default_app("main")
    assert ctx.get_app().app_id == "main"
    
    ctx.set_default_app("main")
    print("test_multi_app_context_default: PASS")


def test_multi_app_context_unregister():
    ctx = MultiAppContext()
    
    app1 = AppContext(app_id="main", app_name="Main", driver="FlaUI")
    app2 = AppContext(app_id="helper", app_name="Helper", driver="FlaUI")
    
    ctx.register_app(app1)
    ctx.register_app(app2)
    ctx.set_default_app("main")
    
    ctx.unregister_app("main")
    
    assert "main" not in ctx.apps
    assert ctx.default_app_id == "helper"
    print("test_multi_app_context_unregister: PASS")


def test_app_context_to_dict():
    app = AppContext(
        app_id="test",
        app_name="TestApp",
        driver="WPFSpy",
        process_id=999,
        pipe_name="TestPipe",
        app_path="/path/to/app.exe",
        launch_args=["--debug"],
    )
    
    d = app.to_dict()
    assert d["app_id"] == "test"
    assert d["driver"] == "WPFSpy"
    assert d["process_id"] == 999
    assert d["pipe_name"] == "TestPipe"
    print("test_app_context_to_dict: PASS")


def test_multi_app_context_error_handling():
    ctx = MultiAppContext()
    
    try:
        ctx.get_app("nonexistent")
        assert False, "Should have raised ValueError"
    except ValueError as e:
        assert "nonexistent" in str(e)
    
    try:
        ctx.set_default_app("nonexistent")
        assert False, "Should have raised ValueError"
    except ValueError as e:
        assert "nonexistent" in str(e)
    
    print("test_multi_app_context_error_handling: PASS")


def test_multi_app_context_close_all():
    ctx = MultiAppContext()
    
    app1 = AppContext(app_id="main", app_name="Main", driver="FlaUI")
    app2 = AppContext(app_id="helper", app_name="Helper", driver="FlaUI")
    
    ctx.register_app(app1)
    ctx.register_app(app2)
    ctx.set_default_app("main")
    
    ctx.close_all()
    
    assert len(ctx.apps) == 0
    assert ctx.default_app_id is None
    print("test_multi_app_context_close_all: PASS")


if __name__ == "__main__":
    test_multi_app_context_registration()
    test_multi_app_context_default()
    test_multi_app_context_unregister()
    test_app_context_to_dict()
    test_multi_app_context_error_handling()
    test_multi_app_context_close_all()
    print("\nAll multi-app context tests passed.")
