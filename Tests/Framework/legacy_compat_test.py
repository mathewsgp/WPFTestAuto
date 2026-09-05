"""Test backward compatibility when no apps are registered."""
import sys
import os

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "TestAutoLayer", "api"))

from DriverAgnosticApi import DriverAgnosticApi
from DriverAgnosticApi import _MULTI_APP_CONTEXT


def test_legacy_mode_no_apps_registered():
    """Ensure keywords work without registering apps (legacy mode)."""
    _MULTI_APP_CONTEXT.apps.clear()
    _MULTI_APP_CONTEXT.default_app_id = None
    
    api = DriverAgnosticApi()
    
    # These should not raise ValueError about unregistered apps
    # They will fail at element resolution (no strategies), but that's expected
    try:
        api._resolve_and_execute("nonexistent.alias", "invoke")
    except Exception as e:
        # Should NOT be "App 'None' not registered"
        assert "App 'None' not registered" not in str(e)
        print(f"Got expected error (not app context): {type(e).__name__}: {e}")
    
    print("test_legacy_mode_no_apps_registered: PASS")


if __name__ == "__main__":
    test_legacy_mode_no_apps_registered()
    print("\nBackward compatibility test passed.")
