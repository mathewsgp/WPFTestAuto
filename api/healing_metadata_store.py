"""
Locator Healing Metadata Store
==============================
Stores metadata about element interactions to enable post-run repository updates
when UI changes cause test failures.

This module captures:
1. Baseline element properties captured during successful interactions
2. Healing history (which strategies worked when others failed)
3. Success/failure rates per element and strategy
4. Suggested repository updates based on healing data

The metadata store persists to JSON files in the repository directory,
enabling version control and easy diff review.
"""

import json
import os
import time
from dataclasses import dataclass, asdict, field
from datetime import datetime
from typing import Dict, List, Optional, Any
from pathlib import Path


# Default path for metadata store
_DEFAULT_METADATA_DIR = os.path.join(
    os.path.dirname(os.path.abspath(__file__)), 
    "..", 
    "repository", 
    "healing_metadata"
)


@dataclass
class ElementBaseline:
    """Baseline properties captured during successful element interaction."""
    alias: str
    automation_id: Optional[str] = None
    name: Optional[str] = None
    control_type: Optional[str] = None
    xpath: Optional[str] = None
    parent_xpath: Optional[str] = None
    text: Optional[str] = None
    position: Optional[Dict[str, int]] = None  # {"x": int, "y": int, "width": int, "height": int}
    is_visible: bool = True
    is_enabled: bool = True
    captured_at: str = field(default_factory=lambda: datetime.now().isoformat())
    last_verified: str = field(default_factory=lambda: datetime.now().isoformat())
    verification_count: int = 1
    driver_used: Optional[str] = None
    search_method: Optional[str] = None
    search_value: Optional[str] = None


@dataclass
class HealingAttempt:
    """Record of a healing attempt when primary strategy failed."""
    timestamp: str
    primary_driver: str
    primary_search_method: str
    primary_search_value: str
    failure_reason: str
    healing_driver: str
    healing_search_method: str
    healing_search_value: str
    healing_successful: bool
    new_properties: Optional[Dict[str, Any]] = None  # Captured properties from successful healing


@dataclass
class StrategyStats:
    """Statistics for a specific strategy."""
    success_count: int = 0
    failure_count: int = 0
    total_duration_ms: float = 0.0
    last_success: Optional[str] = None
    last_failure: Optional[str] = None
    avg_duration_ms: float = 0.0

    def record_success(self, duration_ms: float):
        self.success_count += 1
        self.total_duration_ms += duration_ms
        self.last_success = datetime.now().isoformat()
        self.avg_duration_ms = self.total_duration_ms / self.success_count if self.success_count > 0 else 0

    def record_failure(self, duration_ms: float):
        self.failure_count += 1
        self.total_duration_ms += duration_ms
        self.last_failure = datetime.now().isoformat()
        self.avg_duration_ms = self.total_duration_ms / (self.success_count + self.failure_count) if (self.success_count + self.failure_count) > 0 else 0

    def success_rate(self) -> float:
        total = self.success_count + self.failure_count
        return self.success_count / total if total > 0 else 0.0


@dataclass
class ElementMetadata:
    """Complete metadata for an element."""
    alias: str
    baseline: Optional[ElementBaseline] = None
    healing_history: List[HealingAttempt] = field(default_factory=list)
    strategy_stats: Dict[str, StrategyStats] = field(default_factory=dict)  # key: "driver:searchMethod"
    total_interactions: int = 0
    first_seen: str = field(default_factory=lambda: datetime.now().isoformat())
    last_interaction: str = field(default_factory=lambda: datetime.now().isoformat())
    consecutive_failures: int = 0
    consecutive_successes: int = 0

    def record_interaction(self, success: bool, duration_ms: float = 0):
        self.total_interactions += 1
        self.last_interaction = datetime.now().isoformat()
        if success:
            self.consecutive_successes += 1
            self.consecutive_failures = 0
        else:
            self.consecutive_failures += 1
            self.consecutive_successes = 0


class HealingMetadataStore:
    """Manages the healing metadata store for all elements.
    
    This class provides:
    - Capture baseline properties during test execution
    - Track healing attempts and outcomes
    - Generate repository update suggestions
    - Persist/load metadata to/from JSON files
    
    Usage:
        # Initialize store
        store = HealingMetadataStore()
        
        # Capture baseline during successful interaction
        store.capture_baseline("LoginPage.btnSubmit", {
            "automation_id": "btnSubmit",
            "name": "Submit",
            "control_type": "Button",
            "driver": "FlaUI",
            "search_method": "AutomationId",
            "search_value": "btnSubmit"
        })
        
        # Record healing when primary strategy fails but fallback succeeds
        store.record_healing(
            alias="OrdersPage.gridOrders",
            primary_driver="FlaUI",
            primary_search_method="AutomationId",
            primary_search_value="gridOrders",
            failure_reason="Element not found",
            healing_driver="WPFSpy",
            healing_search_method="XPath",
            healing_search_value="/Window/...customGrid",
            new_properties={"automation_id": None, "xpath": "/Window/...customGrid"}
        )
        
        # Generate update suggestions
        suggestions = store.generate_update_suggestions()
        
        # Apply accepted suggestions
        store.apply_updates(suggestions)
    """
    
    def __init__(self, metadata_dir: str = None):
        """Initialize the metadata store.
        
        Args:
            metadata_dir: Directory to store metadata files. 
                         Defaults to repository/healing_metadata.
        """
        self.metadata_dir = Path(metadata_dir) if metadata_dir else Path(_DEFAULT_METADATA_DIR)
        self.metadata_dir.mkdir(parents=True, exist_ok=True)
        
        # In-memory cache of metadata
        self._metadata: Dict[str, ElementMetadata] = {}
        self._modified = False
        
        # Load existing metadata
        self._load_all()
    
    def _get_element_file(self, alias: str) -> Path:
        """Get the metadata file path for an element alias.
        
        Creates a subdirectory structure based on the alias prefix.
        """
        # Convert alias to safe filename: LoginPage.btnSubmit -> LoginPage/btnSubmit.json
        parts = alias.split(".")
        if len(parts) > 1:
            subdir = parts[0]
            filename = parts[-1] + ".json"
        else:
            subdir = "_root"
            filename = alias + ".json"
        
        element_dir = self.metadata_dir / subdir
        element_dir.mkdir(parents=True, exist_ok=True)
        return element_dir / filename
    
    def _load_all(self):
        """Load all metadata files from disk."""
        self._metadata = {}
        for metadata_file in self.metadata_dir.rglob("*.json"):
            if metadata_file.name == "_config.json":
                continue
            try:
                with open(metadata_file, "r") as f:
                    data = json.load(f)
                    # Reconstruct ElementMetadata from dict
                    metadata = self._dict_to_metadata(data)
                    self._metadata[metadata.alias] = metadata
            except Exception as e:
                print(f"Warning: Failed to load {metadata_file}: {e}")
    
    def _dict_to_metadata(self, data: dict) -> ElementMetadata:
        """Convert dict back to ElementMetadata."""
        baseline = None
        if data.get("baseline"):
            baseline = ElementBaseline(**data["baseline"])
        
        healing_history = [HealingAttempt(**h) for h in data.get("healing_history", [])]
        
        strategy_stats = {}
        for key, stats in data.get("strategy_stats", {}).items():
            strategy_stats[key] = StrategyStats(**stats)
        
        return ElementMetadata(
            alias=data["alias"],
            baseline=baseline,
            healing_history=healing_history,
            strategy_stats=strategy_stats,
            total_interactions=data.get("total_interactions", 0),
            first_seen=data.get("first_seen", datetime.now().isoformat()),
            last_interaction=data.get("last_interaction", datetime.now().isoformat()),
            consecutive_failures=data.get("consecutive_failures", 0),
            consecutive_successes=data.get("consecutive_successes", 0)
        )
    
    def _save(self, alias: str):
        """Save metadata for a specific element to disk."""
        if alias not in self._metadata:
            return
        
        metadata = self._metadata[alias]
        file_path = self._get_element_file(alias)
        
        # Convert to serializable dict
        data = {
            "alias": metadata.alias,
            "baseline": asdict(metadata.baseline) if metadata.baseline else None,
            "healing_history": [asdict(h) for h in metadata.healing_history],
            "strategy_stats": {k: asdict(v) for k, v in metadata.strategy_stats.items()},
            "total_interactions": metadata.total_interactions,
            "first_seen": metadata.first_seen,
            "last_interaction": metadata.last_interaction,
            "consecutive_failures": metadata.consecutive_failures,
            "consecutive_successes": metadata.consecutive_successes
        }
        
        with open(file_path, "w") as f:
            json.dump(data, f, indent=2)
    
    def _get_or_create_metadata(self, alias: str) -> ElementMetadata:
        """Get existing or create new metadata for an alias."""
        if alias not in self._metadata:
            self._metadata[alias] = ElementMetadata(alias=alias)
            self._modified = True
        return self._metadata[alias]
    
    def capture_baseline(
        self,
        alias: str,
        properties: Dict[str, Any],
        driver: str = None,
        search_method: str = None,
        search_value: str = None
    ):
        """Capture baseline properties for an element during successful interaction.
        
        Args:
            alias: Element alias (e.g., "LoginPage.btnSubmit")
            properties: Dict of element properties to store:
                - automation_id: UI Automation ID
                - name: Element Name property
                - control_type: WPF control type (Button, TextBox, etc.)
                - xpath: Full XPath to element
                - parent_xpath: XPath to parent element
                - text: Current text content
                - position: Dict with x, y, width, height
                - is_visible: Visibility state
                - is_enabled: Enabled state
            driver: Driver used to find element (FlaUI, WPFSpy, Sikuli)
            search_method: Method used to locate (AutomationId, XPath, Name, etc.)
            search_value: Value used in the search
        """
        metadata = self._get_or_create_metadata(alias)
        
        baseline = ElementBaseline(
            alias=alias,
            automation_id=properties.get("automation_id"),
            name=properties.get("name"),
            control_type=properties.get("control_type"),
            xpath=properties.get("xpath"),
            parent_xpath=properties.get("parent_xpath"),
            text=properties.get("text"),
            position=properties.get("position"),
            is_visible=properties.get("is_visible", True),
            is_enabled=properties.get("is_enabled", True),
            driver_used=driver,
            search_method=search_method,
            search_value=search_value
        )
        
        # Only update if we don't have a baseline or this is newer
        if metadata.baseline is None or baseline.captured_at > metadata.baseline.captured_at:
            metadata.baseline = baseline
            self._modified = True
        
        metadata.record_interaction(success=True)
        self._save(alias)
    
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
        new_properties: Dict[str, Any] = None
    ):
        """Record a healing attempt when primary strategy fails but fallback succeeds.
        
        Args:
            alias: Element alias
            primary_driver: Driver that failed (e.g., "FlaUI")
            primary_search_method: Method that failed (e.g., "AutomationId")
            primary_search_value: Value that failed
            failure_reason: Why the primary failed
            healing_driver: Driver that succeeded
            healing_search_method: Method that succeeded
            healing_search_value: Value that succeeded
            healing_successful: Whether healing succeeded
            new_properties: Properties captured from successful healing (optional)
        """
        metadata = self._get_or_create_metadata(alias)
        
        attempt = HealingAttempt(
            timestamp=datetime.now().isoformat(),
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
        
        metadata.healing_history.append(attempt)
        
        # Update baseline with healed properties if successful
        if healing_successful and new_properties:
            metadata.baseline = ElementBaseline(
                alias=alias,
                automation_id=new_properties.get("automation_id"),
                name=new_properties.get("name"),
                control_type=new_properties.get("control_type"),
                xpath=new_properties.get("xpath"),
                parent_xpath=new_properties.get("parent_xpath"),
                text=new_properties.get("text"),
                position=new_properties.get("position"),
                is_visible=new_properties.get("is_visible", True),
                is_enabled=new_properties.get("is_enabled", True),
                driver_used=healing_driver,
                search_method=healing_search_method,
                search_value=healing_search_value
            )
        
        # Update strategy stats
        primary_key = f"{primary_driver}:{primary_search_method}"
        healing_key = f"{healing_driver}:{healing_search_method}"
        
        if primary_key not in metadata.strategy_stats:
            metadata.strategy_stats[primary_key] = StrategyStats()
        if healing_key not in metadata.strategy_stats:
            metadata.strategy_stats[healing_key] = StrategyStats()
        
        if healing_successful:
            metadata.strategy_stats[healing_key].record_success(0)
            metadata.record_interaction(success=True)
        else:
            metadata.strategy_stats[primary_key].record_failure(0)
            metadata.record_interaction(success=False)
        
        self._modified = True
        self._save(alias)
    
    def record_strategy_attempt(
        self,
        alias: str,
        driver: str,
        search_method: str,
        success: bool,
        duration_ms: float = 0
    ):
        """Record a strategy attempt for statistics tracking.
        
        Args:
            alias: Element alias
            driver: Driver used
            search_method: Search method used
            success: Whether the attempt succeeded
            duration_ms: How long the attempt took
        """
        metadata = self._get_or_create_metadata(alias)
        
        strategy_key = f"{driver}:{search_method}"
        if strategy_key not in metadata.strategy_stats:
            metadata.strategy_stats[strategy_key] = StrategyStats()
        
        if success:
            metadata.strategy_stats[strategy_key].record_success(duration_ms)
        else:
            metadata.strategy_stats[strategy_key].record_failure(duration_ms)
        
        metadata.record_interaction(success=success)
        self._modified = True
        self._save(alias)
    
    def generate_update_suggestions(self, min_healing_count: int = 2) -> List[Dict[str, Any]]:
        """Generate repository update suggestions based on healing data.
        
        Analyzes healing history and strategy stats to suggest:
        1. Add alternative strategies when primary consistently fails
        2. Update automation IDs when they change frequently
        3. Add fallback strategies for unstable elements
        
        Args:
            min_healing_count: Minimum number of healing successes to suggest update
            
        Returns:
            List of suggestion dicts with:
            - type: "add_strategy", "update_locator", "add_fallback"
            - alias: Element alias
            - reason: Why this change is suggested
            - suggestion: The specific change to make
            - confidence: 0.0-1.0 confidence score
        """
        suggestions = []
        
        for alias, metadata in self._metadata.items():
            if not metadata.healing_history:
                continue
            
            # Analyze healing attempts
            successful_heals = [h for h in metadata.healing_history if h.healing_successful]
            
            if len(successful_heals) >= min_healing_count:
                # Group by healing method
                heal_methods = {}
                for heal in successful_heals:
                    key = (heal.healing_driver, heal.healing_search_method, heal.healing_search_value)
                    if key not in heal_methods:
                        heal_methods[key] = {"count": 0, "attempts": []}
                    heal_methods[key]["count"] += 1
                    heal_methods[key]["attempts"].append(heal)
                
                # Find most consistent healing method
                if heal_methods:
                    best_method = max(heal_methods.items(), key=lambda x: x[1]["count"])
                    driver, method, value = best_method[0]
                    count = best_method[1]["count"]
                    confidence = min(count / 5.0, 1.0)  # Cap at 1.0
                    
                    # Check if this is a new strategy not in current config
                    current_strategies = self._get_current_strategies(alias)
                    strategy_exists = any(
                        s.get("searchBy") == method and s.get("value") == value
                        for s in current_strategies.get(driver, [])
                    )
                    
                    if not strategy_exists:
                        suggestions.append({
                            "type": "add_strategy",
                            "alias": alias,
                            "reason": f"Element has healed successfully {count} times using {driver}:{method}",
                            "suggestion": {
                                "driver": driver,
                                "searchBy": method,
                                "value": value,
                                "priority": 2  # Fallback priority
                            },
                            "confidence": confidence,
                            "healing_count": count
                        })
            
            # Analyze strategy failure patterns
            for strategy_key, stats in metadata.strategy_stats.items():
                driver, method = strategy_key.split(":", 1)
                if stats.failure_count > 3 and stats.success_rate() < 0.3:
                    suggestions.append({
                        "type": "deprecate_strategy",
                        "alias": alias,
                        "reason": f"Strategy {strategy_key} has only {stats.success_rate()*100:.0f}% success rate",
                        "suggestion": {
                            "driver": driver,
                            "searchBy": method,
                            "action": "lower_priority"
                        },
                        "confidence": min(stats.failure_count / 10.0, 1.0),
                        "success_rate": stats.success_rate()
                    })
        
        return sorted(suggestions, key=lambda x: x["confidence"], reverse=True)
    
    def _get_current_strategies(self, alias: str) -> Dict[str, List[Dict]]:
        """Get current strategies for an element from the repository."""
        try:
            import repository_access as repo
            return repo.get_strategies(alias)
        except Exception:
            return {}
    
    def apply_updates(
        self, 
        suggestions: List[Dict[str, Any]], 
        dry_run: bool = True,
        backup: bool = True
    ) -> Dict[str, Any]:
        """Apply accepted suggestions to the repository.
        
        Args:
            suggestions: List of suggestions from generate_update_suggestions
            dry_run: If True, don't actually modify files, just return what would change
            backup: If True, backup existing files before modifying
            
        Returns:
            Dict with:
            - applied: List of changes made (or would be made)
            - errors: Any errors encountered
            - backups: Backup file paths if backup=True
        """
        results = {
            "applied": [],
            "errors": [],
            "backups": [],
            "dry_run": dry_run
        }
        
        # Group suggestions by element file
        changes_by_file: Dict[str, List[Dict]] = {}
        
        for suggestion in suggestions:
            if suggestion.get("type") not in ("add_strategy", "update_locator"):
                continue
                
            alias = suggestion["alias"]
            file_path = self._get_element_file(alias)
            
            if file_path not in changes_by_file:
                changes_by_file[file_path] = []
            changes_by_file[file_path].append(suggestion)
        
        for file_path, changes in changes_by_file.items():
            try:
                # Read current element file
                element_file = file_path.with_name(file_path.name.replace(".json", ".yaml"))
                
                if element_file.exists():
                    if backup and not dry_run:
                        backup_path = file_path.with_name(f"{file_path.stem}_backup_{int(time.time())}.yaml")
                        import shutil
                        shutil.copy2(element_file, backup_path)
                        results["backups"].append(str(backup_path))
                    
                    if not dry_run:
                        # Apply changes to YAML
                        self._apply_changes_to_yaml(element_file, changes, results)
                else:
                    # Create new element file with healing data
                    if not dry_run:
                        self._create_element_from_healing(file_path.with_suffix(".yaml"), changes[0])
                    
                    results["applied"].append({
                        "file": str(element_file),
                        "changes": changes
                    })
                    
            except Exception as e:
                results["errors"].append({
                    "file": str(file_path),
                    "error": str(e)
                })
        
        return results
    
    def _apply_changes_to_yaml(self, yaml_file: Path, changes: List[Dict], results: Dict):
        """Apply changes to a YAML element file."""
        import yaml
        import shutil
        
        # Read existing YAML
        with open(yaml_file, "r") as f:
            data = yaml.safe_load(f)
        
        elements = data.get("elements", {})
        
        for change in changes:
            alias = change["alias"]
            if alias not in elements:
                continue
            
            suggestion = change["suggestion"]
            strategies = elements[alias].get("strategies", {})
            
            if change["type"] == "add_strategy":
                driver = suggestion["driver"]
                if driver not in strategies:
                    strategies[driver] = []
                
                # Check if strategy already exists
                exists = any(
                    s.get("searchBy") == suggestion["searchBy"] and 
                    s.get("value") == suggestion["value"]
                    for s in strategies[driver]
                )
                
                if not exists:
                    strategies[driver].append({
                        "searchBy": suggestion["searchBy"],
                        "value": suggestion["value"],
                        "priority": suggestion.get("priority", 2)
                    })
                    
                    results["applied"].append({
                        "file": str(yaml_file),
                        "alias": alias,
                        "change": f"Added {driver} strategy: {suggestion['searchBy']}={suggestion['value']}"
                    })
        
        # Write updated YAML
        with open(yaml_file, "w") as f:
            yaml.dump(data, f, default_flow_style=False, sort_keys=False)
    
    def _create_element_from_healing(self, yaml_file: Path, suggestion: Dict):
        """Create a new element file from healing suggestion."""
        import yaml
        
        alias = suggestion["alias"]
        sug = suggestion["suggestion"]
        
        data = {
            "elements": {
                alias: {
                    "displayName": alias.split(".")[-1],
                    "controlType": "Unknown",  # Would need to capture this
                    "strategies": {
                        sug["driver"]: [{
                            "searchBy": sug["searchBy"],
                            "value": sug["value"],
                            "priority": sug.get("priority", 2)
                        }]
                    }
                }
            }
        }
        
        with open(yaml_file, "w") as f:
            yaml.dump(data, f, default_flow_style=False, sort_keys=False)
    
    def get_element_health(self, alias: str) -> Dict[str, Any]:
        """Get health metrics for an element.
        
        Returns:
            Dict with health metrics like success rate, stability score, etc.
        """
        if alias not in self._metadata:
            return {"status": "unknown", "message": "No metadata found"}
        
        metadata = self._metadata[alias]
        
        total_attempts = sum(
            s.success_count + s.failure_count 
            for s in metadata.strategy_stats.values()
        )
        total_successes = sum(s.success_count for s in metadata.strategy_stats.values())
        
        success_rate = total_successes / total_attempts if total_attempts > 0 else 0
        
        # Determine health status
        if metadata.consecutive_failures >= 3:
            status = "unstable"
            reason = f"{metadata.consecutive_failures} consecutive failures"
        elif success_rate < 0.5:
            status = "degraded"
            reason = f"{success_rate*100:.0f}% success rate"
        elif success_rate >= 0.9:
            status = "healthy"
            reason = f"{success_rate*100:.0f}% success rate"
        else:
            status = "stable"
            reason = f"{success_rate*100:.0f}% success rate"
        
        return {
            "alias": alias,
            "status": status,
            "reason": reason,
            "success_rate": success_rate,
            "total_interactions": metadata.total_interactions,
            "consecutive_failures": metadata.consecutive_failures,
            "consecutive_successes": metadata.consecutive_successes,
            "healing_count": len(metadata.healing_history),
            "first_seen": metadata.first_seen,
            "last_interaction": metadata.last_interaction,
            "baseline_captured": metadata.baseline is not None
        }
    
    def export_healing_report(self, output_file: str = None) -> str:
        """Export a healing report as JSON.
        
        Args:
            output_file: Path to save report. If None, returns JSON string.
            
        Returns:
            Report as JSON string, or path to saved file.
        """
        report = {
            "generated_at": datetime.now().isoformat(),
            "total_elements": len(self._metadata),
            "elements_needing_attention": [],
            "healing_summary": {
                "total_heals": sum(len(m.healing_history) for m in self._metadata.values()),
                "successful_heals": sum(
                    sum(1 for h in m.healing_history if h.healing_successful)
                    for m in self._metadata.values()
                )
            },
            "element_details": {}
        }
        
        for alias, metadata in self._metadata.items():
            health = self.get_element_health(alias)
            if health["status"] in ("unstable", "degraded"):
                report["elements_needing_attention"].append({
                    "alias": alias,
                    **health
                })
            
            report["element_details"][alias] = {
                "health": health,
                "baseline": asdict(metadata.baseline) if metadata.baseline else None,
                "recent_heals": [
                    asdict(h) for h in metadata.healing_history[-5:]
                ],
                "strategy_stats": {
                    k: asdict(v) for k, v in metadata.strategy_stats.items()
                }
            }
        
        report_json = json.dumps(report, indent=2)
        
        if output_file:
            with open(output_file, "w") as f:
                f.write(report_json)
            return output_file
        
        return report_json
    
    def clear_metadata(self, alias: str = None):
        """Clear metadata for an element or all elements.
        
        Args:
            alias: Element alias to clear. If None, clears all.
        """
        if alias:
            if alias in self._metadata:
                del self._metadata[alias]
                file_path = self._get_element_file(alias)
                if file_path.exists():
                    file_path.unlink()
        else:
            self._metadata = {}
            for f in self.metadata_dir.rglob("*.json"):
                if f.name != "_config.json":
                    f.unlink()


# Global instance for easy access
_global_store: Optional[HealingMetadataStore] = None


def get_healing_store() -> HealingMetadataStore:
    """Get the global healing metadata store instance."""
    global _global_store
    if _global_store is None:
        _global_store = HealingMetadataStore()
    return _global_store
