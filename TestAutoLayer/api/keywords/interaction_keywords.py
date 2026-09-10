"""
Interaction Keywords
====================
Element interaction keywords for the DriverAgnosticApi facade.
"""

from typing import Optional, List, Any
from TestAutoLayer.api.resolution.strategy_executor import StrategyExecutor
from TestAutoLayer.api.resolution.element_resolver import ElementResolver
from TestAutoLayer.api.app_management import launch_application, attach_to_application
from TestAutoLayer.api.base_driver import ElementHandle
import TestAutoLayer.api.repository_access as repo


class InteractionKeywords:
    """Element interaction keywords."""
    
    def __init__(
        self,
        strategy_executor: StrategyExecutor,
        element_resolver: ElementResolver,
        get_default_app_id: callable,
    ):
        self._executor = strategy_executor
        self._resolver = element_resolver
        self._get_default_app_id = get_default_app_id
    
    def _resolve_app_id(self, app_id: Optional[str]) -> Optional[str]:
        """Resolve app_id, falling back to default."""
        if app_id is not None:
            return app_id
        return self._get_default_app_id()
    
    def click_element(self, alias: str, app_id: Optional[str] = None):
        """Invokes (clicks) the element identified by `alias`."""
        app_id = self._resolve_app_id(app_id)
        self._executor.execute(alias, "invoke", app_id)
    
    def click_element_with_wait(self, alias: str, timeout: float = 10.0, app_id: Optional[str] = None):
        """Clicks element after waiting for it to be actionable."""
        app_id = self._resolve_app_id(app_id)
        self.wait_until_element_actionable(alias, timeout, app_id)
        self._executor.execute(alias, "invoke", app_id)
    
    def set_element_value(self, alias: str, value: str, app_id: Optional[str] = None):
        """Sets the text/value of the element identified by `alias`."""
        app_id = self._resolve_app_id(app_id)
        self._executor.execute(alias, "set_value", app_id, value)
    
    def set_element_value_with_wait(
        self,
        alias: str,
        value: str,
        timeout: float = 10.0,
        app_id: Optional[str] = None,
    ):
        """Sets element value after waiting for it to be actionable."""
        app_id = self._resolve_app_id(app_id)
        self.wait_until_element_actionable(alias, timeout, app_id)
        self._executor.execute(alias, "set_value", app_id, value)
    
    def get_element_text(self, alias: str, app_id: Optional[str] = None) -> str:
        """Returns the current text of the element identified by `alias`."""
        app_id = self._resolve_app_id(app_id)
        return self._executor.execute(alias, "get_text", app_id)
    
    def toggle_element(self, alias: str, app_id: Optional[str] = None):
        """Toggles a checkbox/toggle-style element identified by `alias`."""
        app_id = self._resolve_app_id(app_id)
        self._executor.execute(alias, "toggle", app_id)
    
    def double_click_element(self, alias: str, app_id: Optional[str] = None):
        """Double-clicks the element identified by `alias`."""
        app_id = self._resolve_app_id(app_id)
        self._executor.execute(alias, "double_click", app_id)
    
    def right_click_element(self, alias: str, app_id: Optional[str] = None):
        """Right-clicks the element identified by `alias`."""
        app_id = self._resolve_app_id(app_id)
        self._executor.execute(alias, "right_click", app_id)
    
    def press_keys(self, alias: str, keys: str, app_id: Optional[str] = None):
        """Presses keys into the element identified by `alias`."""
        app_id = self._resolve_app_id(app_id)
        self._executor.execute(alias, "press_keys", app_id, keys)
    
    def drag_and_drop(self, alias: str, target_alias: str, app_id: Optional[str] = None):
        """Drags the element identified by `alias` and drops it on `target_alias`."""
        app_id = self._resolve_app_id(app_id)
        
        target_strategies = repo.get_all_driver_strategies_sorted(target_alias, app_id=app_id)
        if not target_strategies:
            from TestAutoLayer.api.exceptions import AllStrategiesFailedError
            raise AllStrategiesFailedError(
                alias=target_alias,
                attempts=[],
                details={"reason": "No strategies configured for target"}
            )

        target_element = None
        for driver_name in self._executor._get_default_driver_order(target_strategies):
            if driver_name not in target_strategies:
                continue
            driver = self._executor._driver_provider(driver_name, app_id)
            if driver is None:
                continue
            for strategy in target_strategies[driver_name]:
                try:
                    resolved = self._resolver.resolve_strategy_with_parent(strategy, target_alias, app_id, driver_name)
                    target_element = driver.find_element(resolved)
                    break
                except Exception:
                    continue
            if target_element is not None:
                break

        if target_element is None:
            from TestAutoLayer.api.exceptions import AllStrategiesFailedError
            raise AllStrategiesFailedError(
                alias=target_alias,
                attempts=[],
                details={"reason": "Could not resolve target element"}
            )

        self._executor.execute(alias, "drag_drop", app_id, target_element)
    
    def hover_over_element(self, alias: str, app_id: Optional[str] = None):
        """Hovers over the element identified by `alias`."""
        app_id = self._resolve_app_id(app_id)
        self._executor.execute(alias, "hover", app_id)
    
    def scroll(self, alias: str, direction: str, app_id: Optional[str] = None):
        """Scrolls the element identified by `alias` in the given direction."""
        app_id = self._resolve_app_id(app_id)
        self._executor.execute(alias, "scroll", app_id, direction)
    
    def sikuli_click(self, alias: str, image_tag: str, app_id: Optional[str] = None):
        """Clicks an element identified by a Sikuli image tag."""
        app_id = self._resolve_app_id(app_id)
        driver = self._executor._driver_provider("Sikuli", app_id)
        if driver is None:
            raise RuntimeError("Sikuli driver not available")
        element = driver.find_element({"searchBy": "Image", "value": image_tag})
        driver.invoke(element)
    
    def sikuli_type(self, alias: str, text: str, app_id: Optional[str] = None):
        """Types text into an element identified by a Sikuli image tag."""
        app_id = self._resolve_app_id(app_id)
        driver = self._executor._driver_provider("Sikuli", app_id)
        if driver is None:
            raise RuntimeError("Sikuli driver not available")
        strategies = repo.get_strategies(alias, app_id=app_id)
        image_tag = None
        for driver_name, strats in strategies.items():
            if driver_name == "Sikuli" and strats:
                image_tag = strats[0].get("value")
                break
        if not image_tag:
            from ..exceptions import AllStrategiesFailedError
            raise AllStrategiesFailedError(
                alias=alias,
                attempts=[],
                details={"reason": "No Sikuli image tag configured"}
            )
        element = driver.find_element({"searchBy": "Image", "value": image_tag})
        driver.set_value(element, text)