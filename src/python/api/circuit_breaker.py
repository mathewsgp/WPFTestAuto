"""
Circuit Breaker Pattern Implementation
=====================================
Prevents cascading failures by stopping calls to failing services.
"""

import time
import threading
from enum import Enum
from typing import Callable, Any, Optional
from functools import wraps

try:
    from exceptions import CircuitBreakerOpenError
except ImportError:
    from exceptions import CircuitBreakerOpenError


class CircuitState(Enum):
    CLOSED = "closed"      # Normal operation
    OPEN = "open"          # Failing, reject calls
    HALF_OPEN = "half_open"  # Testing if service recovered


class CircuitBreaker:
    """Circuit breaker to prevent cascading failures in driver calls.
    
    States:
        CLOSED: Normal operation, calls pass through
        OPEN: Too many failures, calls are rejected immediately
        HALF_OPEN: Testing recovery, limited calls allowed
    
    Usage:
        breaker = CircuitBreaker("WPFSpy", threshold=3, timeout=60)
        
        try:
            result = breaker.call(driver.find_element, locator)
        except CircuitBreakerOpenError:
            # Circuit is open, driver is unavailable
            pass
    """
    
    def __init__(
        self,
        name: str,
        threshold: int = 3,
        timeout: int = 60,
        half_open_max_calls: int = 1
    ):
        """
        Args:
            name: Identifier for the circuit breaker (usually driver name).
            threshold: Number of failures before opening circuit.
            timeout: Seconds to wait before trying half-open state.
            half_open_max_calls: Number of calls allowed in half-open state.
        """
        self.name = name
        self.threshold = threshold
        self.timeout = timeout
        self.half_open_max_calls = half_open_max_calls
        
        self._state = CircuitState.CLOSED
        self._failure_count = 0
        self._success_count = 0
        self._last_failure_time: Optional[float] = None
        self._half_open_calls = 0
        self._lock = threading.RLock()
    
    @property
    def state(self) -> CircuitState:
        """Get current circuit state, checking for timeout transitions."""
        with self._lock:
            if self._state == CircuitState.OPEN:
                # Check if we should transition to half-open
                if self._last_failure_time is not None:
                    if time.time() - self._last_failure_time >= self.timeout:
                        self._state = CircuitState.HALF_OPEN
                        self._half_open_calls = 0
            return self._state
    
    @property
    def failure_count(self) -> int:
        """Get current failure count."""
        with self._lock:
            return self._failure_count
    
    def record_success(self) -> None:
        """Record a successful call.
        
        In HALF_OPEN state: records success, may close circuit.
        In CLOSED state: resets failure count.
        """
        with self._lock:
            if self._state == CircuitState.HALF_OPEN:
                self._success_count += 1
                if self._success_count >= self.half_open_max_calls:
                    self._close()
            elif self._state == CircuitState.CLOSED:
                self._failure_count = 0
    
    def record_failure(self) -> None:
        """Record a failed call.
        
        In HALF_OPEN state: opens circuit immediately.
        In CLOSED state: increments failure count, may open circuit.
        """
        with self._lock:
            self._last_failure_time = time.time()
            
            if self._state == CircuitState.HALF_OPEN:
                self._open()
            elif self._state == CircuitState.CLOSED:
                self._failure_count += 1
                if self._failure_count >= self.threshold:
                    self._open()
    
    def _open(self) -> None:
        """Transition to OPEN state."""
        self._state = CircuitState.OPEN
        self._success_count = 0
    
    def _close(self) -> None:
        """Transition to CLOSED state."""
        self._state = CircuitState.CLOSED
        self._failure_count = 0
        self._success_count = 0
        self._half_open_calls = 0
    
    def allow_request(self) -> bool:
        """Check if a request should be allowed through.
        
        Returns:
            True if request is allowed, False otherwise.
        """
        with self._lock:
            if self._state == CircuitState.CLOSED:
                return True
            
            if self._state == CircuitState.OPEN:
                # Check timeout
                if (self._last_failure_time is not None and
                    time.time() - self._last_failure_time >= self.timeout):
                    self._state = CircuitState.HALF_OPEN
                    self._half_open_calls = 0
                    return True
                return False
            
            if self._state == CircuitState.HALF_OPEN:
                if self._half_open_calls < self.half_open_max_calls:
                    self._half_open_calls += 1
                    return True
                return False
            
            return False
    
    def call(self, func: Callable, *args, **kwargs) -> Any:
        """Execute a function with circuit breaker protection.
        
        Args:
            func: Function to call.
            *args: Positional arguments for func.
            **kwargs: Keyword arguments for func.
        
        Returns:
            Result of func call.
        
        Raises:
            CircuitBreakerOpenError: If circuit is open.
            Exception: Any exception from func.
        """
        if not self.allow_request():
            raise CircuitBreakerOpenError(
                self.name,
                {
                    "state": self._state.value,
                    "failure_count": self._failure_count,
                    "threshold": self.threshold
                }
            )
        
        try:
            result = func(*args, **kwargs)
            self.record_success()
            return result
        except Exception as e:
            self.record_failure()
            raise
    
    def reset(self) -> None:
        """Manually reset the circuit breaker to closed state."""
        with self._lock:
            self._close()
    
    def __repr__(self) -> str:
        return (
            f"CircuitBreaker(name='{self.name}', "
            f"state={self.state.value}, "
            f"failures={self.failure_count}/{self.threshold})"
        )


class CircuitBreakerManager:
    """Manages multiple circuit breakers for different drivers/services."""
    
    _instance = None
    _lock = threading.RLock()
    
    def __new__(cls, threshold=3, timeout=60):
        if cls._instance is None:
            with cls._lock:
                if cls._instance is None:
                    cls._instance = super().__new__(cls)
                    cls._instance._breakers = {}
                    cls._instance._lock = threading.RLock()
                    cls._instance._default_threshold = threshold
                    cls._instance._default_timeout = timeout
        return cls._instance
    
    def get_breaker(
        self,
        name: str,
        threshold: int = None,
        timeout: int = None
    ) -> CircuitBreaker:
        """Get or create a circuit breaker for a service.
        
        Args:
            name: Service/driver name.
            threshold: Failure threshold (uses default if not specified).
            timeout: Recovery timeout in seconds (uses default if not specified).
        
        Returns:
            CircuitBreaker instance.
        """
        if threshold is None:
            threshold = self._default_threshold
        if timeout is None:
            timeout = self._default_timeout
            
        with self._lock:
            if name not in self._breakers:
                self._breakers[name] = CircuitBreaker(
                    name=name,
                    threshold=threshold,
                    timeout=timeout
                )
            return self._breakers[name]
    
    def reset_all(self) -> None:
        """Reset all circuit breakers."""
        with self._lock:
            for breaker in self._breakers.values():
                breaker.reset()
    
    def get_status(self) -> dict:
        """Get status of all circuit breakers."""
        with self._lock:
            return {
                name: {
                    "state": breaker.state.value,
                    "failure_count": breaker.failure_count,
                    "threshold": breaker.threshold
                }
                for name, breaker in self._breakers.items()
            }


def circuit_protected(breaker_name: str, **breaker_kwargs):
    """Decorator to protect a function with a circuit breaker.
    
    Usage:
        @circuit_protected("WPFSpy", threshold=3, timeout=60)
        def find_element(locator):
            return driver.find_element(locator)
    """
    def decorator(func: Callable) -> Callable:
        @wraps(func)
        def wrapper(*args, **kwargs):
            manager = CircuitBreakerManager()
            breaker = manager.get_breaker(breaker_name, **breaker_kwargs)
            return breaker.call(func, *args, **kwargs)
        return wrapper
    return decorator
