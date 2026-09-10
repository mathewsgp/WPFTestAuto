"""
Element Resolver with Caching
=============================
Handles strategy resolution with parent chain XPath building.
Includes caching for resolved XPaths.
"""

from typing import Dict, Any, Optional, Tuple
from functools import lru_cache
import TestAutoLayer.api.repository_access as repo
from TestAutoLayer.api.logging_utils import get_api_logger

logger = get_api_logger()


class ElementResolver:
    """
    Resolves element strategies by building full XPaths from parent chains.
    
    Cache key: (alias, app_id, driver_name)
    Invalidates on: reset_application(), load_elements(force_reload=True)
    """
    
    def __init__(self):
        self._cache: Dict[Tuple[str, Optional[str], Optional[str]], str] = {}
        self._cache_enabled = True
    
    def resolve_strategy_with_parent(
        self,
        strategy: Dict[str, Any],
        alias: str,
        app_id: Optional[str] = None,
        driver_name: Optional[str] = None
    ) -> Dict[str, Any]:
        """
        Resolve strategy by building full XPath from parent chain.
        
        Args:
            strategy: Strategy dict with searchBy and value
            alias: Element alias for parent chain lookup
            app_id: Optional app context ID
            driver_name: Optional driver name (FlaUI uses Name for Window, others use AutomationId)
        
        Returns:
            Strategy dict with resolved full XPath
        """
        resolved = strategy.copy()
        value = strategy.get("value", "")
        
        if strategy.get("searchBy") != "XPath":
            return resolved
        
        if value.startswith("/"):
            return resolved
        
        full_path = self.build_full_path_from_alias(alias, app_id, driver_name)
        resolved["value"] = f"{full_path}/{value}"
        
        return resolved
    
    def build_full_path_from_alias(
        self,
        alias: str,
        app_id: Optional[str] = None,
        driver_name: Optional[str] = None
    ) -> str:
        """
        Build full XPath by walking parent chain.
        
        Uses cache for repeated lookups.
        
        Args:
            alias: Element alias to resolve
            app_id: Optional app context ID
            driver_name: Optional driver name for driver-specific Window prefix
        
        Returns:
            Full XPath from Window to the element's parent.
        """
        cache_key = (alias, app_id, driver_name)
        
        if self._cache_enabled and cache_key in self._cache:
            logger.debug(f"Cache hit for {cache_key}")
            return self._cache[cache_key]
        
        path_parts = []
        parent_alias = repo.get_parent_alias(alias, app_id=app_id)
        current_alias = parent_alias
        visited = set()
        
        while current_alias:
            if current_alias in visited:
                logger.warning(f"Circular parent reference detected for alias: {current_alias}")
                break
            visited.add(current_alias)
            
            element = repo.get_element(current_alias, app_id=app_id)
            
            # Get the parent alias
            parent = repo.get_parent_alias(current_alias, app_id=app_id)
            
            # Build XPath prefix for this element
            control_type = element.get("controlType", "")
            window_id = element.get("windowAutomationId", "MainWindow")
            
            if control_type == "Window":
                # FlaUI uses UIA Name (Title) for Window; WPFSpy reads AutomationId directly
                if driver_name == "FlaUI":
                    window_name = element.get("name") or window_id
                    if window_name and window_name != window_id:
                        path_parts.insert(0, f"Window[@Name='{window_name}']")
                    else:
                        path_parts.insert(0, f"Window[@AutomationId='{window_id}']")
                else:
                    path_parts.insert(0, f"Window[@AutomationId='{window_id}']")
                break
            elif "automationId" in element:
                path_parts.insert(0, f"{control_type}[@AutomationId='{element['automationId']}']")
            elif "name" in element:
                path_parts.insert(0, f"{control_type}[@Name='{element['name']}']")
            
            # Move to parent
            if parent is None:
                # Reached root (Window)
                break
            current_alias = parent
        
        full_path = "/" + "/".join(path_parts)
        
        if self._cache_enabled:
            self._cache[cache_key] = full_path
        
        return full_path
    
    def invalidate_cache(self, alias: Optional[str] = None, app_id: Optional[str] = None):
        """Invalidate cache entries.
        
        Args:
            alias: If provided, only invalidate entries for this alias
            app_id: If provided, only invalidate entries for this app_id
        """
        if alias is None and app_id is None:
            self._cache.clear()
            logger.debug("ElementResolver cache cleared (all)")
        else:
            keys_to_remove = []
            for key in self._cache:
                key_alias, key_app_id, _ = key
                if (alias is None or key_alias == alias) and (app_id is None or key_app_id == app_id):
                    keys_to_remove.append(key)
            for key in keys_to_remove:
                del self._cache[key]
            logger.debug(f"ElementResolver cache invalidated for alias={alias}, app_id={app_id}")
    
    def enable_cache(self, enabled: bool = True):
        """Enable or disable caching."""
        self._cache_enabled = enabled
        if not enabled:
            self._cache.clear()
    
    def get_cache_stats(self) -> Dict[str, Any]:
        """Get cache statistics."""
        return {
            "size": len(self._cache),
            "enabled": self._cache_enabled,
            "entries": [
                {"alias": k[0], "app_id": k[1], "driver": k[2], "path": v[:100]}
                for k, v in list(self._cache.items())[:10]
            ]
        }