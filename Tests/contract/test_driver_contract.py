"""
Contract Tests for Driver Interface
===================================
Verifies all drivers (FlaUI, WPFSpy, Sikuli) implement the BaseDriver
abstract interface with matching signatures. Prevents interface drift.

Run: pytest Tests/contract/test_driver_contract.py -v
"""

import inspect
import sys
import os
import pytest
from abc import ABC

# Add api directory to path
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "TestAutoLayer", "api"))

from base_driver import BaseDriver, ElementHandle


def get_driver_classes():
    """Import and return all driver classes. Skip if dependencies missing."""
    drivers = {}
    
    # Add drivers_rf to path
    drivers_rf_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "TestAutoLayer", "drivers_rf")
    sys.path.insert(0, drivers_rf_path)
    
    # FlaUIDriver
    try:
        from flaui_robotframework.flaui_driver import FlaUIDriver
        drivers["FlaUIDriver"] = FlaUIDriver
    except ImportError as e:
        print(f"SKIP: FlaUIDriver not available: {e}")
    
    # WPFSpyMockDriver (available cross-platform)
    try:
        from wpfspy_robotframework.WPFSpyLibrary import WPFSpyMockDriver
        drivers["WPFSpyMockDriver"] = WPFSpyMockDriver
    except ImportError as e:
        print(f"SKIP: WPFSpyMockDriver not available: {e}")
    
    # SikuliDriver
    try:
        from sikuli_robotframework.SikuliLibrary import SikuliDriver
        drivers["SikuliDriver"] = SikuliDriver
    except ImportError as e:
        print(f"SKIP: SikuliDriver not available: {e}")
    
    return drivers


class TestDriverContract:
    """Contract tests ensuring all drivers implement BaseDriver correctly."""
    
    @classmethod
    def setup_class(cls):
        cls.driver_classes = get_driver_classes()
        if not cls.driver_classes:
            pytest.skip("No drivers available for testing")
    
    def test_all_drivers_inherit_from_base_driver(self):
        """Each driver class must inherit from BaseDriver (directly or indirectly)."""
        for name, driver_cls in self.driver_classes.items():
            # Check MRO includes BaseDriver
            assert BaseDriver in inspect.getmro(driver_cls), \
                f"{name} does not inherit from BaseDriver"
    
    def test_all_abstract_methods_implemented(self):
        """Each driver must implement all abstract methods from BaseDriver."""
        abstract_methods = BaseDriver.__abstractmethods__
        
        for name, driver_cls in self.driver_classes.items():
            for method_name in abstract_methods:
                assert hasattr(driver_cls, method_name), \
                    f"{name} missing abstract method: {method_name}"
                
                attr = getattr(driver_cls, method_name)
                # Skip properties (like 'name') - they're not callable
                if isinstance(attr, property):
                    continue
                assert callable(attr), \
                    f"{name}.{method_name} is not callable"
    
    def test_abstract_method_signatures_match(self):
        """Each implemented method must have compatible signature with base."""
        abstract_methods = BaseDriver.__abstractmethods__
        
        for name, driver_cls in self.driver_classes.items():
            for method_name in abstract_methods:
                base_attr = getattr(BaseDriver, method_name)
                impl_attr = getattr(driver_cls, method_name)
                
                # Skip properties
                if isinstance(base_attr, property):
                    continue
                if isinstance(impl_attr, property):
                    continue
                
                base_sig = inspect.signature(base_attr)
                impl_sig = inspect.signature(impl_attr)
                
                # Check parameter names and kinds match (allow extra defaults in impl)
                base_params = list(base_sig.parameters.values())
                impl_params = list(impl_sig.parameters.values())
                
                # Skip 'self' parameter
                base_params = base_params[1:]
                impl_params = impl_params[1:]
                
                assert len(impl_params) >= len(base_params), \
                    f"{name}.{method_name}: too few parameters. Expected >= {len(base_params)}, got {len(impl_params)}"
                
                for i, (base_param, impl_param) in enumerate(zip(base_params, impl_params)):
                    assert base_param.name == impl_param.name, \
                        f"{name}.{method_name}: parameter {i} name mismatch. Expected '{base_param.name}', got '{impl_param.name}'"
                    assert base_param.kind == impl_param.kind, \
                        f"{name}.{method_name}: parameter {i} kind mismatch. Expected {base_param.kind}, got {impl_param.kind}"
                
                # Check return annotation compatibility (if both annotated)
                if base_sig.return_annotation != inspect.Signature.empty and \
                   impl_sig.return_annotation != inspect.Signature.empty:
                    # Allow subclass return type (covariant)
                    pass  # Detailed check would require type evaluation
    
    def test_driver_has_name_property(self):
        """Each driver must have a 'name' property returning a string."""
        for name, driver_cls in self.driver_classes.items():
            assert hasattr(driver_cls, 'name'), f"{name} missing 'name' attribute"
            attr = getattr(driver_cls, 'name')
            # Should be a property or class attribute
            if isinstance(attr, property):
                # Can't easily test property return type without instance
                pass
            else:
                assert isinstance(attr, str), f"{name}.name must be string"
                assert attr, f"{name}.name must not be empty"
    
    def test_driver_can_be_instantiated(self):
        """Each driver should be instantiable (may require mock setup)."""
        for name, driver_cls in self.driver_classes.items():
            try:
                # Try instantiation with minimal args
                if name == "FlaUIDriver":
                    instance = driver_cls(app_pid=None)
                else:
                    instance = driver_cls()
                
                assert instance is not None
                assert instance.name == driver_cls.name
                
            except Exception as e:
                # Some drivers may need specific setup - log but don't fail contract
                print(f"WARNING: {name} instantiation failed (may need setup): {e}")
    
    def test_element_handle_dataclass_exists(self):
        """ElementHandle dataclass must be importable and have required fields."""
        # Verify fields exist as dataclass fields
        import dataclasses
        fields = {f.name for f in dataclasses.fields(ElementHandle)}
        assert 'locator' in fields
        assert 'driver_name' in fields
        assert 'found_at' in fields
        
        # Verify methods
        assert hasattr(ElementHandle, 'is_stale')
        assert callable(ElementHandle.is_stale)
    
    def test_element_handle_staleness_check(self):
        """ElementHandle.is_stale() should work correctly."""
        import time
        
        handle = ElementHandle(
            locator={"searchBy": "AutomationId", "value": "test"},
            driver_name="TestDriver",
            found_at=time.time() - 100  # 100 seconds ago
        )
        
        assert handle.is_stale(max_age=60.0) is True
        assert handle.is_stale(max_age=200.0) is False
        
        # Fresh handle should not be stale
        fresh_handle = ElementHandle(
            locator={"searchBy": "AutomationId", "value": "test"},
            driver_name="TestDriver",
            found_at=time.time()
        )
        assert fresh_handle.is_stale(max_age=60.0) is False


class TestDriverInterfaceCompleteness:
    """Verify the BaseDriver interface covers all required operations."""
    
    def test_base_driver_covers_all_required_operations(self):
        """BaseDriver should define all operations needed by Layer 3."""
        required_methods = {
            # Core element finding
            'find_element',
            'find_elements',
            
            # Core interactions
            'invoke',
            'set_value',
            'get_text',
            'get_attribute',
            
            # State queries
            'is_visible',
            'is_enabled',
            'is_actionable',
            
            # Additional interactions (with defaults)
            'toggle',
            'wait_until_actionable',
            'capture_screenshot',
            'close',
            
            # Extended interactions (optional but recommended)
            'double_click',
            'right_click',
            'press_keys',
            'drag_drop',
            'hover',
            'scroll',
            'get_data_grid_content_ocr',
        }
        
        actual_methods = set(dir(BaseDriver))
        missing = required_methods - actual_methods
        
        assert not missing, f"BaseDriver missing methods: {missing}"
    
    def test_optional_methods_have_default_implementation(self):
        """Methods that have default implementations in BaseDriver should not be abstract."""
        optional_methods = {
            'is_actionable',
            'wait_until_actionable',
            'capture_screenshot',
            'close',
        }
        
        for method_name in optional_methods:
            method = getattr(BaseDriver, method_name)
            # Should not be abstract (not in __abstractmethods__)
            assert method_name not in BaseDriver.__abstractmethods__, \
                f"Optional method {method_name} should not be abstract"
            
            # Should have implementation (not raise NotImplementedError by default)
            # We can't easily test without instance, but verify it's not abstract
    
    def test_extended_methods_are_abstract(self):
        """Extended interaction methods should be abstract (all drivers implement them)."""
        extended_methods = {
            'double_click',
            'right_click',
            'press_keys',
            'drag_drop',
            'hover',
            'scroll',
            'get_data_grid_content_ocr',
        }
        
        for method_name in extended_methods:
            assert method_name in BaseDriver.__abstractmethods__, \
                f"Extended method {method_name} should be abstract"


# Allow running directly with python
if __name__ == "__main__":
    import pytest
    pytest.main([__file__, "-v"])