"""
Wait Keywords
=============
Element wait keywords for the DriverAgnosticApi facade.
"""

import time
from typing import Optional, List, Any
from TestAutoLayer.api.resolution.strategy_executor import StrategyExecutor
from TestAutoLayer.api.exceptions import WaitTimeoutError, AllStrategiesFailedError


class WaitKeywords:
    """Element wait keywords."""
    
    def __init__(
        self,
        strategy_executor: StrategyExecutor,
        get_default_app_id: callable,
    ):
        self._executor = strategy_executor
        self._get_default_app_id = get_default_app_id
    
    def _resolve_app_id(self, app_id: Optional[str]) -> Optional[str]:
        """Resolve app_id, falling back to default."""
        if app_id is not None:
            return app_id
        return self._get_default_app_id()
    
    def _try_all_strategies(
        self,
        alias: str,
        app_id: Optional[str],
        predicate,
        find_all: bool = False,
    ) -> bool:
        """Delegate to strategy executor."""
        return self._executor.try_all_strategies(alias, app_id, predicate, find_all)
    
    def is_element_visible(self, alias: str, app_id: Optional[str] = None) -> bool:
        """Check if element is visible without failing."""
        app_id = self._resolve_app_id(app_id)
        max_retries = 3
        retry_delay = 0.3
        for attempt in range(max_retries):
            if self._try_all_strategies(alias, app_id, lambda d, e: d.is_visible(e)):
                return True
            if attempt < max_retries - 1:
                time.sleep(retry_delay)
        return False
    
    def is_element_enabled(self, alias: str, app_id: Optional[str] = None) -> bool:
        """Check if element is enabled without failing."""
        app_id = self._resolve_app_id(app_id)
        return self._try_all_strategies(alias, app_id, lambda d, e: d.is_enabled(e))
    
    def find_elements(self, alias: str, app_id: Optional[str] = None) -> List[Any]:
        """Find all elements matching the alias across all available strategies."""
        app_id = self._resolve_app_id(app_id)
        results: List[Any] = []
        self._try_all_strategies(
            alias, app_id, lambda d, e: results.extend([e]) or False, find_all=True
        )
        return results
    
    def is_element_actionable(self, alias: str, app_id: Optional[str] = None) -> bool:
        """Check if element is both visible and enabled."""
        app_id = self._resolve_app_id(app_id)
        return self._try_all_strategies(alias, app_id, lambda d, e: d.is_actionable(e))
    
    def wait_until_element_exists(
        self,
        alias: str,
        timeout: float = 10.0,
        poll_interval: float = 0.5,
        app_id: Optional[str] = None,
    ):
        """Polls until the element can be found, or raises after `timeout` seconds."""
        app_id = self._resolve_app_id(app_id)
        
        if not self._try_all_strategies(alias, app_id, lambda d, e: True):
            # Quick check before polling
            strategies = self._executor._strategy_provider(alias, app_id=app_id)
            if not strategies:
                raise AllStrategiesFailedError(
                    alias=alias,
                    attempts=[],
                    details={"reason": "No strategies configured"}
                )
        
        start_time = time.time()
        while time.time() - start_time < timeout:
            if self._try_all_strategies(alias, app_id, lambda d, e: True):
                return True
            
            remaining = timeout - (time.time() - start_time)
            if remaining > 0:
                time.sleep(min(poll_interval, remaining))
        
        raise WaitTimeoutError(
            condition="element exists",
            timeout=timeout,
        )
    
    def wait_until_element_visible(
        self,
        alias: str,
        timeout: float = 10.0,
        poll_interval: float = 0.5,
        app_id: Optional[str] = None,
    ):
        """Polls until the element is visible, or raises after `timeout` seconds."""
        app_id = self._resolve_app_id(app_id)
        
        if not self._executor._strategy_provider(alias, app_id=app_id):
            raise AllStrategiesFailedError(
                alias=alias,
                attempts=[],
                details={"reason": "No strategies configured"}
            )
        
        start_time = time.time()
        while time.time() - start_time < timeout:
            if self._try_all_strategies(alias, app_id, lambda d, e: d.is_visible(e)):
                return True
            
            remaining = timeout - (time.time() - start_time)
            if remaining > 0:
                time.sleep(min(poll_interval, remaining))
        
        raise WaitTimeoutError(
            condition="element visible",
            timeout=timeout,
        )
    
    def wait_until_element_enabled(
        self,
        alias: str,
        timeout: float = 10.0,
        poll_interval: float = 0.5,
        app_id: Optional[str] = None,
    ):
        """Polls until the element is enabled, or raises after `timeout` seconds."""
        app_id = self._resolve_app_id(app_id)
        
        if not self._executor._strategy_provider(alias, app_id=app_id):
            raise AllStrategiesFailedError(
                alias=alias,
                attempts=[],
                details={"reason": "No strategies configured"}
            )
        
        start_time = time.time()
        while time.time() - start_time < timeout:
            if self._try_all_strategies(alias, app_id, lambda d, e: d.is_enabled(e)):
                return True
            
            remaining = timeout - (time.time() - start_time)
            if remaining > 0:
                time.sleep(min(poll_interval, remaining))
        
        raise WaitTimeoutError(
            condition="element enabled",
            timeout=timeout,
        )
    
    def wait_until_element_actionable(
        self,
        alias: str,
        timeout: float = 10.0,
        poll_interval: float = 0.5,
        app_id: Optional[str] = None,
    ):
        """Wait for element to be visible and enabled."""
        app_id = self._resolve_app_id(app_id)
        
        if not self._executor._strategy_provider(alias, app_id=app_id):
            raise AllStrategiesFailedError(
                alias=alias,
                attempts=[],
                details={"reason": "No strategies configured"}
            )
        
        start_time = time.time()
        while time.time() - start_time < timeout:
            if self._try_all_strategies(alias, app_id, lambda d, e: d.is_actionable(e)):
                return True
            
            remaining = timeout - (time.time() - start_time)
            if remaining > 0:
                time.sleep(min(poll_interval, remaining))
        
        raise WaitTimeoutError(
            condition="element actionable",
            timeout=timeout,
        )
    
    def wait_until_element_text_contains(
        self,
        alias: str,
        expected: str,
        timeout: float = 10.0,
        case_sensitive: bool = True,
        app_id: Optional[str] = None,
    ):
        """Wait for element's text to contain the expected value."""
        app_id = self._resolve_app_id(app_id)
        
        if not self._executor._strategy_provider(alias, app_id=app_id):
            raise AllStrategiesFailedError(
                alias=alias,
                attempts=[],
                details={"reason": "No strategies configured"}
            )
        
        start_time = time.time()
        while time.time() - start_time < timeout:
            if self._try_all_strategies(
                alias,
                app_id,
                lambda d, e: self._text_matches(d.get_text(e), expected, case_sensitive),
            ):
                return True
            
            remaining = timeout - (time.time() - start_time)
            if remaining > 0:
                time.sleep(min(0.5, remaining))
        
        raise WaitTimeoutError(
            condition=f"text contains '{expected}'",
            timeout=timeout,
        )
    
    @staticmethod
    def _text_matches(text: str, expected: str, case_sensitive: bool) -> bool:
        """Check whether *text* contains *expected*."""
        if case_sensitive:
            return expected in text
        return expected.lower() in text.lower()