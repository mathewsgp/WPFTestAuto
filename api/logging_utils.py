"""
Structured Logging
==================
Provides consistent, structured logging across the framework.
Supports both standard logging and structured (JSON) output.
"""

import logging
import json
import sys
import time
from typing import Any, Dict, Optional
from datetime import datetime
from functools import wraps


class StructuredFormatter(logging.Formatter):
    """Formatter that outputs structured JSON logs."""
    
    def __init__(self, include_caller: bool = True):
        super().__init__()
        self.include_caller = include_caller
    
    def format(self, record: logging.LogRecord) -> str:
        log_data = {
            "timestamp": datetime.utcnow().isoformat() + "Z",
            "level": record.levelname,
            "logger": record.name,
            "message": record.getMessage(),
        }
        
        # Add extra fields
        if hasattr(record, "extra_data"):
            log_data.update(record.extra_data)
        
        # Add caller info
        if self.include_caller:
            log_data["caller"] = {
                "file": record.filename,
                "line": record.lineno,
                "function": record.funcName
            }
        
        # Add exception info if present
        if record.exc_info:
            log_data["exception"] = {
                "type": record.exc_info[0].__name__ if record.exc_info[0] else None,
                "message": str(record.exc_info[1]) if record.exc_info[1] else None
            }
        
        return json.dumps(log_data)


class FrameworkLogger:
    """Centralized logger for the test automation framework.
    
    Usage:
        logger = FrameworkLogger(__name__)
        logger.info("Element found", driver="WPFSpy", alias="LoginPage.btn")
        logger.debug("Retrying strategy", attempt=3, max_attempts=5)
    """
    
    _instance = None
    _initialized = False
    
    def __new__(cls):
        if cls._instance is None:
            cls._instance = super().__new__(cls)
        return cls._instance
    
    def __init__(self):
        if FrameworkLogger._initialized:
            return
        
        self._loggers: Dict[str, logging.Logger] = {}
        self._structured = True
        self._log_level = logging.INFO
        
        # Set up root logger
        self._root_logger = logging.getLogger("wpf_automation")
        self._root_logger.setLevel(logging.DEBUG)
        
        # Console handler
        if not self._root_logger.handlers:
            handler = logging.StreamHandler(sys.stdout)
            handler.setLevel(logging.DEBUG)
            
            # Use structured formatter by default
            formatter = StructuredFormatter()
            handler.setFormatter(formatter)
            self._root_logger.addHandler(handler)
        
        FrameworkLogger._initialized = True
    
    def configure(
        self,
        structured: bool = True,
        log_level: str = "INFO",
        log_file: Optional[str] = None
    ) -> None:
        """Configure logging settings.
        
        Args:
            structured: Use JSON structured logging.
            log_level: Log level (DEBUG, INFO, WARNING, ERROR).
            log_file: Optional file path for file logging.
        """
        self._structured = structured
        self._log_level = getattr(logging, log_level.upper(), logging.INFO)
        
        # Update all existing loggers
        for logger in self._loggers.values():
            logger.setLevel(self._log_level)
        
        # Update root logger
        self._root_logger.setLevel(self._log_level)
        
        # Update formatter
        for handler in self._root_logger.handlers:
            if structured:
                handler.setFormatter(StructuredFormatter())
            else:
                handler.setFormatter(
                    logging.Formatter(
                        "%(asctime)s - %(name)s - %(levelname)s - %(message)s"
                    )
                )
        
        # Add file handler if specified
        if log_file:
            file_handler = logging.FileHandler(log_file)
            file_handler.setLevel(self._log_level)
            if structured:
                file_handler.setFormatter(StructuredFormatter())
            else:
                file_handler.setFormatter(
                    logging.Formatter(
                        "%(asctime)s - %(name)s - %(levelname)s - %(message)s"
                    )
                )
            self._root_logger.addHandler(file_handler)
    
    def get_logger(self, name: str) -> logging.Logger:
        """Get or create a logger for a module.
        
        Args:
            name: Logger name (usually __name__).
        
        Returns:
            Logger instance.
        """
        if name not in self._loggers:
            logger = self._root_logger.getChild(name.replace(".", "_"))
            logger.setLevel(self._log_level)
            self._loggers[name] = logger
        return self._loggers[name]
    
    def create_logger(self, name: str) -> "ContextLogger":
        """Create a context-aware logger.
        
        Args:
            name: Logger name.
        
        Returns:
            ContextLogger instance with extra context support.
        """
        return ContextLogger(self.get_logger(name))


class ContextLogger:
    """Logger wrapper that supports extra context fields.
    
    Usage:
        logger = ContextLogger(logging.getLogger(__name__))
        
        # With context
        with logger.context(driver="WPFSpy", alias="Login.btn"):
            logger.info("Finding element")  # Logs: {"driver": "WPFSpy", "alias": "Login.btn", ...}
        
        # Direct extra
        logger.info("Retry attempt", attempt=3)
    """
    
    def __init__(self, logger: logging.Logger):
        self._logger = logger
        self._context: Dict[str, Any] = {}
    
    def context(self, **kwargs) -> "ContextLogger":
        """Create a new logger with additional context.
        
        Returns a new ContextLogger with merged context.
        """
        new_logger = ContextLogger(self._logger)
        new_logger._context = {**self._context, **kwargs}
        return new_logger
    
    def _log(
        self,
        level: int,
        msg: str,
        exc_info: bool = False,
        **kwargs
    ) -> None:
        """Internal log method with context and extra fields."""
        # Merge context with kwargs
        extra = {**self._context, **kwargs}
        
        # Create a LogRecord with extra data
        record = self._logger.makeRecord(
            self._logger.name,
            level,
            "(unknown)",
            0,
            msg,
            (),
            exc_info
        )
        record.extra_data = extra
        
        self._logger.handle(record)
    
    def debug(self, msg: str, exc_info: bool = False, **kwargs) -> None:
        self._log(logging.DEBUG, msg, exc_info, **kwargs)
    
    def info(self, msg: str, exc_info: bool = False, **kwargs) -> None:
        self._log(logging.INFO, msg, exc_info, **kwargs)
    
    def warning(self, msg: str, exc_info: bool = False, **kwargs) -> None:
        self._log(logging.WARNING, msg, exc_info, **kwargs)
    
    def error(self, msg: str, exc_info: bool = False, **kwargs) -> None:
        self._log(logging.ERROR, msg, exc_info, **kwargs)
    
    def critical(self, msg: str, exc_info: bool = False, **kwargs) -> None:
        self._log(logging.CRITICAL, msg, exc_info, **kwargs)
    
    def exception(self, msg: str, **kwargs) -> None:
        self._log(logging.ERROR, msg, exc_info=True, **kwargs)


def get_logger(name: str) -> ContextLogger:
    """Get a context logger for a module.
    
    Convenience function to get a configured logger.
    
    Args:
        name: Logger name (usually __name__).
    
    Returns:
        ContextLogger instance.
    """
    framework_logger = FrameworkLogger()
    return framework_logger.create_logger(name)


# Framework-specific loggers for common components
def get_api_logger() -> ContextLogger:
    """Get logger for Layer 3 API."""
    return get_logger("api.DriverAgnosticApi")


def get_driver_logger(driver_name: str) -> ContextLogger:
    """Get logger for a specific driver."""
    return get_logger(f"drivers.{driver_name}")


def get_repo_logger() -> ContextLogger:
    """Get logger for repository operations."""
    return get_logger("api.repository_access")


# Test execution logger for metrics and reporting
class TestExecutionLogger:
    """Logger specifically for test execution events and metrics."""
    
    def __init__(self):
        self._events: list = []
        self._logger = get_logger("test_execution")
    
    def log_strategy_attempt(
        self,
        alias: str,
        driver: str,
        strategy: Dict,
        result: str,
        duration_ms: Optional[float] = None,
        error: Optional[str] = None
    ) -> None:
        """Log a strategy attempt."""
        event = {
            "event": "strategy_attempt",
            "alias": alias,
            "driver": driver,
            "result": result,
            "timestamp": time.time()
        }
        if duration_ms is not None:
            event["duration_ms"] = duration_ms
        if error:
            event["error"] = error
        
        self._events.append(event)
        self._logger.info(
            f"Strategy attempt: {driver} for {alias}",
            alias=alias,
            driver=driver,
            result=result,
            error=error
        )
    
    def log_self_healing(
        self,
        alias: str,
        from_driver: str,
        to_driver: str,
        reason: str
    ) -> None:
        """Log a self-healing fallback."""
        event = {
            "event": "self_healing",
            "alias": alias,
            "from_driver": from_driver,
            "to_driver": to_driver,
            "reason": reason,
            "timestamp": time.time()
        }
        self._events.append(event)
        
        self._logger.warning(
            f"Self-healing: {alias} fell back from {from_driver} to {to_driver}",
            alias=alias,
            from_driver=from_driver,
            to_driver=to_driver,
            reason=reason
        )
    
    def log_circuit_breaker_state(
        self,
        driver: str,
        state: str,
        failure_count: int
    ) -> None:
        """Log circuit breaker state change."""
        self._logger.info(
            f"Circuit breaker {driver}: {state}",
            driver=driver,
            state=state,
            failure_count=failure_count
        )
    
    def get_events(self) -> list:
        """Get all logged events."""
        return self._events.copy()
    
    def clear(self) -> None:
        """Clear all logged events."""
        self._events.clear()


# Global execution logger instance
execution_logger = TestExecutionLogger()
