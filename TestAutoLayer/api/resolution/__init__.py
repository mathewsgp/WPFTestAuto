"""
Resolution Package
==================
Provides core resolution services for the DriverAgnosticApi:

- strategy_executor: StrategyExecutor - executes strategies with self-healing fallback
- element_resolver: ElementResolver - resolves element strategies with XPath caching
- healing_tracker: HealingTracker - records healing metadata
"""

from .strategy_executor import StrategyExecutor, ExecutionResult, HealingInfo
from .element_resolver import ElementResolver
from .healing_tracker import HealingTracker, HealingTrackerConfig

__all__ = [
    "StrategyExecutor",
    "ExecutionResult",
    "HealingInfo",
    "ElementResolver",
    "HealingTracker",
    "HealingTrackerConfig",
]