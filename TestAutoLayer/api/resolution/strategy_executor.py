"""
Strategy Executor
=================
Single implementation for both multi-app and legacy modes.
Handles: circuit breaker, healing tracking, attempt logging, screenshot on failure.
"""

import time
from typing import Callable, Dict, Any, Optional, List, Tuple
from dataclasses import dataclass

from TestAutoLayer.api.config import config
from TestAutoLayer.api.circuit_breaker import CircuitBreakerManager, CircuitState
from TestAutoLayer.api.base_driver import ElementHandle
from TestAutoLayer.api.logging_utils import get_api_logger
from TestAutoLayer.api.app_management.app_registry import get_multi_app_context
import TestAutoLayer.api.repository_access as repo

logger = get_api_logger()


@dataclass
class ExecutionResult:
    """Result of strategy execution."""
    success: bool
    result: Any = None
    error: Optional[str] = None
    strategy_desc: str = ""
    driver_name: str = ""
    duration_ms: float = 0.0
    element: Optional[ElementHandle] = None


class StrategyExecutor:
    """
    Executes element strategies with self-healing fallback logic.
    
    Uses pluggable providers for:
    - driver_provider(driver_name, app_id) -> driver instance
    - strategy_provider(alias, app_id) -> dict of driver -> sorted strategies
    - healing_tracker for recording attempts/healing/baselines
    """
    
    def __init__(
        self,
        driver_provider: Callable[[str, Optional[str]], Any],
        strategy_provider: Callable[[str, Optional[str]], Dict[str, List[Dict]]],
        healing_tracker: Optional['HealingTracker'] = None,
        circuit_breaker_manager: Optional[CircuitBreakerManager] = None,
        element_resolver: Optional['ElementResolver'] = None,
    ):
        self._driver_provider = driver_provider
        self._strategy_provider = strategy_provider
        self._healing_tracker = healing_tracker
        self._breaker_manager = circuit_breaker_manager or CircuitBreakerManager(
            threshold=config.CIRCUIT_BREAKER_THRESHOLD,
            timeout=config.CIRCUIT_BREAKER_TIMEOUT
        )
        self._element_resolver = element_resolver
        self.last_strategy_used: Optional[str] = None
        self.attempt_log: List[Tuple[str, str]] = []
    
    def execute(
        self,
        alias: str,
        action_name: str,
        app_id: Optional[str] = None,
        *args,
        active_driver: Optional[str] = None,
        element_priority: Optional[List[str]] = None,
        driver_order_override: Optional[List[str]] = None,
    ) -> Any:
        """
        Resolve element and execute action with self-healing fallback.
        
        Args:
            alias: Element alias from repository
            action_name: Method name on driver (invoke, set_value, get_text, etc.)
            app_id: Optional application context ID
            *args: Action-specific arguments
            active_driver: Override to use only this driver
            element_priority: Per-element driver priority from repository
            driver_order_override: Override driver order (e.g., from run modes)
        
        Returns:
            Result of the action
            
        Raises:
            AllStrategiesFailedError: If all strategies fail
        """
        from TestAutoLayer.api.exceptions import AllStrategiesFailedError
        
        all_strategies = self._strategy_provider(alias, app_id)
        
        if not all_strategies:
            raise AllStrategiesFailedError(
                alias=alias,
                attempts=[],
                details={"reason": "No strategies configured"}
            )
        
        # Determine driver order
        if driver_order_override is not None:
            driver_order = driver_order_override
        elif active_driver is not None:
            driver_order = [active_driver]
        elif element_priority:
            driver_order = [d for d in element_priority if d in all_strategies]
            if not driver_order:
                driver_order = self._get_default_driver_order(all_strategies)
        elif app_id:
            # Use app context driver list if available
            try:
                app_ctx = get_multi_app_context().get_app(app_id)
                driver_order = [d for d in app_ctx.driver_list if d in all_strategies]
                if not driver_order:
                    driver_order = self._get_default_driver_order(all_strategies)
            except ValueError:
                # App not registered, fall back to default
                driver_order = self._get_default_driver_order(all_strategies)
        else:
            driver_order = self._get_default_driver_order(all_strategies)
        
        logger.info(
            "StrategyExecutor driver flow",
            alias=alias,
            action=action_name,
            app_id=app_id,
            driver_order=driver_order,
            available_strategies=list(all_strategies.keys()),
        )
        
        attempts = []
        healing_info = HealingInfo()
        
        for driver_name in driver_order:
            if driver_name not in all_strategies:
                continue
            
            driver_strategies = all_strategies[driver_name]
            driver = self._driver_provider(driver_name, app_id)
            
            if driver is None:
                logger.warning(
                    f"Driver {driver_name} not available",
                    alias=alias,
                    driver=driver_name
                )
                continue
            
            # Check circuit breaker
            breaker = self._breaker_manager.get_breaker(driver_name)
            if not breaker.allow_request():
                logger.warning(
                    f"Circuit breaker open for {driver_name}, skipping",
                    alias=alias,
                    driver=driver_name
                )
                attempts.append((f"{driver_name}:*", "CIRCUIT_OPEN"))
                continue
            
            # Try each strategy for this driver in priority order
            for strategy in driver_strategies:
                result = self._execute_strategy(
                    driver, driver_name, strategy, action_name, args,
                    alias, app_id, healing_info, attempts
                )
                
                if result.success:
                    self.last_strategy_used = result.strategy_desc
                    self.attempt_log = [(result.strategy_desc, "SUCCESS")]
                    
                    logger.info(
                        f"Element found via {result.strategy_desc}",
                        alias=alias,
                        driver=driver_name,
                        searchBy=strategy.get("searchBy", ""),
                        duration_ms=round(result.duration_ms, 2)
                    )
                    return result.result
                
                # Continue to next strategy
            
            # All strategies for this driver failed
            logger.debug(
                f"All {driver_name} strategies failed, trying next driver",
                alias=alias,
                driver=driver_name
            )
        
        # All drivers and strategies failed
        error_details = {
            "attempts": attempts,
            "total_attempts": len(attempts),
            "driver_order": driver_order
        }
        
        # Record failed healing attempt
        if self._healing_tracker and healing_info.attempted:
            self._healing_tracker.record_healing(
                alias=alias,
                primary_driver=healing_info.primary_driver,
                primary_search_method=healing_info.primary_search_by,
                primary_search_value=healing_info.primary_value,
                failure_reason=healing_info.primary_error,
                healing_driver="None",
                healing_search_method="N/A",
                healing_search_value="N/A",
                healing_successful=False
            )
        
        logger.error(
            f"All strategies failed for {alias}",
            alias=alias,
            attempts=attempts
        )
        
        # Capture screenshot on failure
        self._capture_failure_screenshot(alias, healing_info.primary_driver)
        
        raise AllStrategiesFailedError(
            alias=alias,
            attempts=attempts,
            details=error_details
        )
    
    def _execute_strategy(
        self,
        driver: Any,
        driver_name: str,
        strategy: Dict[str, Any],
        action_name: str,
        args: tuple,
        alias: str,
        app_id: Optional[str],
        healing_info: 'HealingInfo',
        attempts: List[Tuple[str, str]]
    ) -> ExecutionResult:
        """Execute a single strategy and return result."""
        search_by = strategy.get("searchBy", "")
        strategy_value = strategy.get("value", "")
        priority = strategy.get("priority", 99)
        strategy_desc = f"{driver_name}:{search_by}"
        
        start_time = time.time()
        logger.debug(
            f"Trying {strategy_desc}",
            alias=alias,
            driver=driver_name,
            searchBy=search_by,
            value=strategy_value,
            priority=priority
        )
        
        try:
            element = driver.find_element(strategy)
            result = getattr(driver, action_name)(element, *args)
            duration_ms = (time.time() - start_time) * 1000
            
            # Success
            breaker = self._breaker_manager.get_breaker(driver_name)
            breaker.record_success()
            
            # Record healing/baseline
            if self._healing_tracker:
                image_score = getattr(driver, "last_match_score", None)
                self._healing_tracker.record_strategy_attempt(
                    alias=alias,
                    driver=driver_name,
                    search_method=search_by,
                    success=True,
                    duration_ms=duration_ms,
                    image_match_score=image_score,
                    app_id=app_id
                )
                
                if healing_info.attempted:
                    # This was a healing success
                    new_properties = self._capture_element_properties(driver, element)
                    self._healing_tracker.record_healing(
                        alias=alias,
                        primary_driver=healing_info.primary_driver,
                        primary_search_method=healing_info.primary_search_by,
                        primary_search_value=healing_info.primary_value,
                        failure_reason=healing_info.primary_error,
                        healing_driver=driver_name,
                        healing_search_method=search_by,
                        healing_search_value=strategy_value,
                        healing_successful=True,
                        new_properties=new_properties,
                        app_id=app_id
                    )
                    logger.info(
                        f"[Healing] Element healed via {driver_name}:{search_by}",
                        alias=alias,
                        app_id=app_id,
                        primary=healing_info.primary_driver,
                        healing=driver_name
                    )
                else:
                    # Capture baseline on first success
                    props = self._capture_element_properties(driver, element)
                    self._healing_tracker.capture_baseline(
                        alias=alias,
                        properties=props,
                        driver=driver_name,
                        search_method=search_by,
                        search_value=strategy_value,
                        app_id=app_id
                    )
            
            return ExecutionResult(
                success=True,
                result=result,
                strategy_desc=strategy_desc,
                driver_name=driver_name,
                duration_ms=duration_ms,
                element=element
            )
            
        except Exception as e:
            duration_ms = (time.time() - start_time) * 1000
            error_msg = str(e)[:100]
            attempts.append((strategy_desc, f"FAILED: {error_msg}"))
            
            # Record failure
            if self._healing_tracker:
                self._healing_tracker.record_strategy_attempt(
                    alias=alias,
                    driver=driver_name,
                    search_method=search_by,
                    success=False,
                    duration_ms=duration_ms,
                    app_id=app_id
                )
            
            # Record first failure for healing tracking
            if not healing_info.attempted:
                healing_info.attempted = True
                healing_info.primary_driver = driver_name
                healing_info.primary_search_by = search_by
                healing_info.primary_value = strategy_value
                healing_info.primary_error = error_msg
            
            logger.debug(
                f"Strategy failed, trying next",
                alias=alias,
                driver=driver_name,
                searchBy=search_by,
                error=error_msg,
                duration_ms=round(duration_ms, 2)
            )
            
            # Record failure for circuit breaker
            breaker = self._breaker_manager.get_breaker(driver_name)
            breaker.record_failure()
            
            return ExecutionResult(
                success=False,
                error=error_msg,
                strategy_desc=strategy_desc,
                driver_name=driver_name,
                duration_ms=duration_ms
            )
    
    def _get_default_driver_order(self, all_strategies: Dict[str, List[Dict]]) -> List[str]:
        """Get default driver order from config."""
        return [d for d in config.DRIVER_ORDER if d in all_strategies]
    
    def _capture_element_properties(self, driver: Any, element: ElementHandle) -> Dict[str, Any]:
        """Capture element properties for baseline storage."""
        properties = {}
        
        for attr in ["AutomationId", "Name", "ControlType"]:
            try:
                value = driver.get_attribute(element, attr)
                if value:
                    properties[attr.lower()] = value
            except Exception:
                pass
        
        try:
            properties["text"] = driver.get_text(element)
        except Exception:
            pass
        
        try:
            properties["is_visible"] = driver.is_visible(element)
        except Exception:
            pass
        
        try:
            properties["is_enabled"] = driver.is_enabled(element)
        except Exception:
            pass
        
        return properties
    
    def _capture_failure_screenshot(self, alias: str, driver_name: Optional[str]):
        """Capture screenshot on failure (non-critical)."""
        try:
            from screenshot_manager import get_screenshot_manager
            screenshot_mgr = get_screenshot_manager()
            driver = self._driver_provider(driver_name, None) if driver_name else None
            
            if driver:
                try:
                    screenshot_data = driver.capture_screenshot()
                    if screenshot_data:
                        screenshot_mgr.capture(
                            image_data=screenshot_data,
                            alias=alias,
                            error_type="AllStrategiesFailedError",
                            error_message=f"All strategies failed",
                            driver_used=driver_name,
                            prefix="failure"
                        )
                        logger.info(f"Screenshot captured on failure: {screenshot_mgr.get_latest_screenshot_path()}")
                except Exception:
                    pass
        except Exception:
            pass  # Screenshot capture is non-critical
    
    def try_all_strategies(
        self,
        alias: str,
        app_id: Optional[str],
        predicate: Callable[[Any, ElementHandle], bool],
        find_all: bool = False,
    ) -> bool:
        """Single pass through all drivers/strategies in priority order."""
        all_strategies = self._strategy_provider(alias, app_id=app_id)
        if not all_strategies:
            return False
        
        for driver_name in self._get_default_driver_order(all_strategies):
            if driver_name not in all_strategies:
                continue
            
            breaker = self._breaker_manager.get_breaker(driver_name)
            if not breaker.allow_request():
                logger.debug(
                    f"Circuit breaker open for {driver_name}, skipping",
                    alias=alias,
                    driver=driver_name,
                )
                continue
            
            driver = self._driver_provider(driver_name, app_id)
            if driver is None:
                continue
            
            for strategy in all_strategies[driver_name]:
                try:
                    resolved = self._resolve_strategy_with_parent(strategy, alias, app_id, driver_name)
                    if find_all:
                        for element in driver.find_elements(resolved):
                            breaker.record_success()
                            if predicate(driver, element):
                                return True
                    else:
                        element = driver.find_element(resolved)
                        breaker.record_success()
                        if predicate(driver, element):
                            return True
                except Exception:
                    breaker.record_failure()
                    continue
        
        return False
    
    def _resolve_strategy_with_parent(self, strategy: Dict, alias: str, app_id: Optional[str], driver_name: Optional[str]) -> Dict:
        """Resolve strategy by building full XPath from parent chain."""
        if self._element_resolver is not None:
            return self._element_resolver.resolve_strategy_with_parent(strategy, alias, app_id, driver_name)
        # Fallback for tests without element_resolver
        from DriverAgnosticApi import DriverAgnosticApi
        api = DriverAgnosticApi()
        return api._resolve_strategy_with_parent(strategy, alias, app_id, driver_name)


class HealingInfo:
    """Tracks healing state during execution."""
    def __init__(self):
        self.attempted: bool = False
        self.primary_driver: Optional[str] = None
        self.primary_search_by: Optional[str] = None
        self.primary_value: Optional[str] = None
        self.primary_error: Optional[str] = None
        self.healing_driver: Optional[str] = None
        self.healing_search_by: Optional[str] = None
        self.healing_value: Optional[str] = None