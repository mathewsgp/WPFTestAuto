"""
Healing Tracker
===============
Decoupled healing metadata recording from execution logic.
Unit-testable with fake healing store.
"""

from typing import Dict, Any, Optional
from dataclasses import dataclass
from TestAutoLayer.api.base_driver import ElementHandle
from TestAutoLayer.api.logging_utils import get_api_logger

logger = get_api_logger()


@dataclass
class HealingTrackerConfig:
    """Configuration for healing tracker."""
    enabled: bool = True
    capture_baseline: bool = True
    record_healing: bool = True
    record_attempts: bool = True


class HealingTracker:
    """
    Tracks healing metadata during element interactions.
    
    Decoupled from execution logic - can be unit tested with fake store.
    """
    
    def __init__(self, config: Optional[HealingTrackerConfig] = None):
        self._config = config or HealingTrackerConfig()
        self._store = None
        self._store_initialized = False
    
    def _get_store(self):
        """Lazily get the healing store instance."""
        if not self._store_initialized:
            try:
                from healing_metadata_store import get_healing_store
                self._store = get_healing_store()
            except Exception as e:
                logger.warning(f"Healing store not available: {e}")
                self._store = None
            self._store_initialized = True
        return self._store
    
    def set_store(self, store: Any):
        """Inject a healing store (for testing)."""
        self._store = store
        self._store_initialized = True
    
    def record_strategy_attempt(
        self,
        alias: str,
        driver: str,
        search_method: str,
        success: bool,
        duration_ms: float = 0,
        image_match_score: Optional[float] = None,
        app_id: Optional[str] = None
    ):
        """Record a strategy attempt for statistics tracking."""
        if not self._config.enabled or not self._config.record_attempts:
            return
        
        store = self._get_store()
        if store is None:
            return
        
        # Use app-scoped key if app_id provided
        store_alias = f"{app_id}:{alias}" if app_id else alias
        
        try:
            store.record_strategy_attempt(
                alias=store_alias,
                driver=driver,
                search_method=search_method,
                success=success,
                duration_ms=duration_ms,
                image_match_score=image_match_score
            )
            logger.debug(
                f"Recorded strategy attempt",
                alias=alias,
                app_id=app_id,
                driver=driver,
                search_method=search_method,
                success=success
            )
        except Exception as e:
            logger.warning(f"Failed to record strategy attempt: {e}")
    
    def record_healing(
        self,
        alias: str,
        primary_driver: str,
        primary_search_method: str,
        primary_search_value: str,
        failure_reason: str,
        healing_driver: str,
        healing_search_method: str,
        healing_search_value: str,
        healing_successful: bool,
        new_properties: Optional[Dict[str, Any]] = None,
        app_id: Optional[str] = None
    ):
        """Record a healing attempt when primary strategy fails but fallback succeeds."""
        if not self._config.enabled or not self._config.record_healing:
            return
        
        store = self._get_store()
        if store is None:
            return
        
        # Use app-scoped key if app_id provided
        store_alias = f"{app_id}:{alias}" if app_id else alias
        
        try:
            store.record_healing(
                alias=store_alias,
                primary_driver=primary_driver,
                primary_search_method=primary_search_method,
                primary_search_value=primary_search_value,
                failure_reason=failure_reason,
                healing_driver=healing_driver,
                healing_search_method=healing_search_method,
                healing_search_value=healing_search_value,
                healing_successful=healing_successful,
                new_properties=new_properties
            )
            logger.info(
                f"Recorded healing",
                alias=alias,
                app_id=app_id,
                primary=primary_driver,
                healing=healing_driver,
                successful=healing_successful
            )
        except Exception as e:
            logger.warning(f"Failed to record healing: {e}")
    
    def capture_baseline(
        self,
        alias: str,
        properties: Dict[str, Any],
        driver: str,
        search_method: str,
        search_value: str,
        app_id: Optional[str] = None
    ):
        """Capture baseline properties for an element during successful interaction."""
        if not self._config.enabled or not self._config.capture_baseline:
            return
        
        store = self._get_store()
        if store is None:
            return
        
        # Use app-scoped key if app_id provided
        store_alias = f"{app_id}:{alias}" if app_id else alias
        
        try:
            store.capture_baseline(
                alias=store_alias,
                properties=properties,
                driver=driver,
                search_method=search_method,
                search_value=search_value
            )
            logger.debug(
                f"Captured baseline",
                alias=alias,
                app_id=app_id,
                driver=driver,
                search_method=search_method
            )
        except Exception as e:
            logger.warning(f"Failed to capture baseline: {e}")
    
    def capture_element_properties(self, driver: Any, element: ElementHandle) -> Dict[str, Any]:
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