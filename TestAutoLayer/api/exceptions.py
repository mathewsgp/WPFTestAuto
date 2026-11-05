"""
Framework Exceptions
===================
Custom exception classes for the WPF Test Automation Framework.
Provides structured error information for better debugging.
"""

from typing import List, Tuple, Optional


class WpfAutomationError(Exception):
    """Base exception for all framework errors."""
    
    def __init__(self, message: str, details: Optional[dict] = None):
        super().__init__(message)
        self.message = message
        self.details = details or {}


class ElementNotFoundError(WpfAutomationError):
    """Raised when an element cannot be found by any strategy."""
    
    def __init__(self, alias: str, details: Optional[dict] = None):
        self.alias = alias
        super().__init__(
            f"Element not found: '{alias}'",
            details={"alias": alias, **(details or {})}
        )


class ElementNotInteractableError(WpfAutomationError):
    """Raised when an element is found but cannot be interacted with."""
    
    def __init__(
        self,
        alias: str,
        reason: str,
        details: Optional[dict] = None
    ):
        self.alias = alias
        self.reason = reason
        super().__init__(
            f"Element '{alias}' is not interactable: {reason}",
            details={"alias": alias, "reason": reason, **(details or {})}
        )


class ElementNotVisibleError(WpfAutomationError):
    """Raised when an element is not visible on screen."""
    
    def __init__(self, alias: str, details: Optional[dict] = None):
        self.alias = alias
        super().__init__(
            f"Element not visible: '{alias}'",
            details={"alias": alias, **(details or {})}
        )


class ElementDisabledError(WpfAutomationError):
    """Raised when an element is disabled."""
    
    def __init__(self, alias: str, details: Optional[dict] = None):
        self.alias = alias
        super().__init__(
            f"Element is disabled: '{alias}'",
            details={"alias": alias, **(details or {})}
        )


class AllStrategiesFailedError(WpfAutomationError):
    """Raised when all configured driver strategies fail to locate or act on an element.
    
    Carries the complete attempt log for diagnosis.
    """
    
    def __init__(
        self,
        alias: str,
        attempts: List[Tuple[str, str]],
        details: Optional[dict] = None
    ):
        self.alias = alias
        self.attempts = attempts
        attempt_summary = ", ".join(
            f"{driver}: {result}" for driver, result in attempts
        )
        super().__init__(
            f"All strategies failed for '{alias}'. Attempts: [{attempt_summary}]",
            details={
                "alias": alias,
                "attempts": attempts,
                "attempt_count": len(attempts),
                **(details or {})
            }
        )
    
    def get_failed_drivers(self) -> List[str]:
        """Get list of drivers that failed."""
        return [driver for driver, result in self.attempts if "FAILED" in result]
    
    def get_successful_driver(self) -> Optional[str]:
        """Get the driver that succeeded, if any."""
        for driver, result in self.attempts:
            if "FAILED" not in result:
                return driver
        return None


class RepositoryError(WpfAutomationError):
    """Raised when there's an issue with element/step repositories."""
    
    def __init__(self, message: str, details: Optional[dict] = None):
        super().__init__(f"Repository error: {message}", details)


class RepositoryAliasNotFoundError(RepositoryError):
    """Raised when an alias is not found in the repository."""
    
    def __init__(self, alias: str, repository_type: str = "elements"):
        self.alias = alias
        self.repository_type = repository_type
        super().__init__(
            f"Alias '{alias}' not found in {repository_type} repository",
            {"alias": alias, "repository_type": repository_type}
        )


class DriverError(WpfAutomationError):
    """Base exception for driver-related errors."""
    
    def __init__(self, driver_name: str, message: str, details: Optional[dict] = None):
        self.driver_name = driver_name
        super().__init__(
            f"{driver_name} error: {message}",
            {"driver": driver_name, "message": message, **(details or {})}
        )


class DriverConnectionError(DriverError):
    """Raised when connection to driver/agent fails."""
    
    def __init__(self, driver_name: str, details: Optional[dict] = None):
        super().__init__(
            driver_name,
            "Failed to connect to driver/agent",
            details
        )


class DriverTimeoutError(DriverError):
    """Raised when a driver operation times out."""
    
    def __init__(self, driver_name: str, timeout: float, details: Optional[dict] = None):
        self.timeout = timeout
        super().__init__(
            driver_name,
            f"Operation timed out after {timeout}s",
            {"timeout": timeout, **(details or {})}
        )


class CircuitBreakerOpenError(WpfAutomationError):
    """Raised when circuit breaker is open and preventing calls."""
    
    def __init__(self, driver_name: str, details: Optional[dict] = None):
        self.driver_name = driver_name
        super().__init__(
            f"Circuit breaker is open for {driver_name}. "
            "Too many recent failures.",
            {"driver": driver_name, **(details or {})}
        )


class ConfigurationError(WpfAutomationError):
    """Raised when there's a configuration issue."""
    
    def __init__(self, message: str, details: Optional[dict] = None):
        super().__init__(f"Configuration error: {message}", details)


class StrategyNotFoundError(WpfAutomationError):
    """Raised when no strategy is configured for an alias."""
    
    def __init__(self, alias: str, details: Optional[dict] = None):
        self.alias = alias
        super().__init__(
            f"No strategy configured for alias '{alias}'",
            {"alias": alias, **(details or {})}
        )


class InvalidLocatorError(WpfAutomationError):
    """Raised when a locator is malformed or invalid."""
    
    def __init__(self, locator: dict, reason: str, details: Optional[dict] = None):
        self.locator = locator
        self.reason = reason
        super().__init__(
            f"Invalid locator: {reason}",
            {"locator": locator, "reason": reason, **(details or {})}
        )


class WaitTimeoutError(WpfAutomationError):
    """Raised when a wait operation times out."""
    
    def __init__(
        self,
        condition: str,
        timeout: float,
        details: Optional[dict] = None
    ):
        self.condition = condition
        self.timeout = timeout
        super().__init__(
            f"Wait for '{condition}' timed out after {timeout}s",
            {"condition": condition, "timeout": timeout, **(details or {})}
        )
