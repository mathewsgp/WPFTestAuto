"""
Wait Utilities
=============
Explicit wait utilities for reliable element interaction.
Providespolling and condition-based waiting with configurable timeouts.
"""

import time
import logging
from typing import Callable, Any, Optional, List, Type
from functools import wraps

from .exceptions import WaitTimeoutError

logger = logging.getLogger(__name__)


class Wait:
    """Explicit wait utility for polling until a condition is met.
    
    Usage:
        wait = Wait(timeout=10, interval=0.5)
        
        # Wait for element to be visible
        wait.until(lambda: driver.is_visible(element))
        
        # Wait for element to contain text
        wait.until(
            lambda: "expected" in driver.get_text(element),
            message="Text not found"
        )
    """
    
    def __init__(
        self,
        timeout: float = 10.0,
        interval: float = 0.5,
        ignored_exceptions: Optional[List[Type[Exception]]] = None
    ):
        """
        Args:
            timeout: Maximum time to wait in seconds.
            interval: Time between condition checks in seconds.
            ignored_exceptions: Exceptions to catch and ignore during waits.
        """
        self.timeout = timeout
        self.interval = interval
        self.ignored_exceptions = ignored_exceptions or []
    
    def until(
        self,
        condition: Callable[[], bool],
        message: str = "",
        on_timeout: Optional[Callable] = None
    ) -> Any:
        """Wait until condition returns True.
        
        Args:
            condition: Callable that returns bool.
            message: Optional message for timeout error.
            on_timeout: Optional callable to execute on timeout.
        
        Returns:
            The return value of the condition on success.
        
        Raises:
            WaitTimeoutError: If condition not met within timeout.
        """
        end_time = time.time() + self.timeout
        last_exception = None
        
        while time.time() < end_time:
            try:
                result = condition()
                if result:
                    return result
            except Exception as e:
                if not any(isinstance(e, exc) for exc in self.ignored_exceptions):
                    raise
                last_exception = e
            
            time.sleep(self.interval)
        
        timeout_msg = message or f"Condition not met within {self.timeout}s"
        logger.warning(f"Wait timeout: {timeout_msg}")
        
        if on_timeout:
            on_timeout()
        
        raise WaitTimeoutError(
            condition=message or "condition",
            timeout=self.timeout
        )
    
    def until_not(
        self,
        condition: Callable[[], bool],
        message: str = "",
        on_timeout: Optional[Callable] = None
    ) -> None:
        """Wait until condition returns False.
        
        Args:
            condition: Callable that returns bool.
            message: Optional message for timeout error.
            on_timeout: Optional callable to execute on timeout.
        
        Raises:
            WaitTimeoutError: If condition still True after timeout.
        """
        end_time = time.time() + self.timeout
        
        while time.time() < end_time:
            if not condition():
                return
            time.sleep(self.interval)
        
        timeout_msg = message or f"Condition still true after {self.timeout}s"
        logger.warning(f"Wait timeout: {timeout_msg}")
        
        if on_timeout:
            on_timeout()
        
        raise WaitTimeoutError(
            condition=message or "condition",
            timeout=self.timeout
        )


class ElementWait(Wait):
    """Wait utilities specifically for element interactions.
    
    Provides common wait patterns for UI automation.
    """
    
    def __init__(
        self,
        driver,
        timeout: float = 10.0,
        interval: float = 0.5
    ):
        """
        Args:
            driver: The driver instance to use for checks.
            timeout: Maximum time to wait in seconds.
            interval: Time between checks in seconds.
        """
        super().__init__(timeout=timeout, interval=interval)
        self.driver = driver
    
    def for_visible(self, element, message: str = "") -> bool:
        """Wait for element to be visible.
        
        Args:
            element: Element handle from find_element.
            message: Optional custom timeout message.
        
        Returns:
            True if element became visible.
        """
        return self.until(
            lambda: self.driver.is_visible(element),
            message=message or f"Element not visible: {element.locator}"
        )
    
    def for_invisible(self, element, message: str = "") -> None:
        """Wait for element to become invisible.
        
        Args:
            element: Element handle from find_element.
            message: Optional custom timeout message.
        """
        self.until_not(
            lambda: self.driver.is_visible(element),
            message=message or f"Element still visible: {element.locator}"
        )
    
    def for_enabled(self, element, message: str = "") -> bool:
        """Wait for element to be enabled.
        
        Args:
            element: Element handle from find_element.
            message: Optional custom timeout message.
        
        Returns:
            True if element became enabled.
        """
        return self.until(
            lambda: self.driver.is_enabled(element),
            message=message or f"Element not enabled: {element.locator}"
        )
    
    def for_actionable(self, element, message: str = "") -> bool:
        """Wait for element to be actionable (visible and enabled).
        
        Args:
            element: Element handle from find_element.
            message: Optional custom timeout message.
        
        Returns:
            True if element became actionable.
        """
        return self.until(
            lambda: self.driver.is_actionable(element),
            message=message or f"Element not actionable: {element.locator}"
        )
    
    def for_text(
        self,
        element,
        text: str,
        case_sensitive: bool = True,
        message: str = ""
    ) -> bool:
        """Wait for element to contain specific text.
        
        Args:
            element: Element handle from find_element.
            text: Expected text content.
            case_sensitive: Whether comparison should be case-sensitive.
            message: Optional custom timeout message.
        
        Returns:
            True if text was found.
        """
        def check_text():
            actual = self.driver.get_text(element)
            if case_sensitive:
                return text in actual
            return text.lower() in actual.lower()
        
        return self.until(
            check_text,
            message=message or f"Text '{text}' not found in element"
        )
    
    def for_text_to_change(
        self,
        element,
        original_text: str,
        timeout: Optional[float] = None,
        message: str = ""
    ) -> str:
        """Wait for element's text to change from original value.
        
        Args:
            element: Element handle from find_element.
            original_text: The original text value to watch for.
            timeout: Optional override timeout for this specific wait.
            message: Optional custom timeout message.
        
        Returns:
            The new text value.
        """
        wait = Wait(
            timeout=timeout or self.timeout,
            interval=self.interval
        )
        
        def check_change():
            current = self.driver.get_text(element)
            if current != original_text:
                return current
            return None
        
        result = wait.until(
            check_change,
            message=message or f"Text did not change from '{original_text}'"
        )
        return result
    
    def for_not_displayed(self, element, message: str = "") -> None:
        """Wait for element to no longer be in the DOM or visible.
        
        Args:
            element: Element handle from find_element.
            message: Optional custom timeout message.
        """
        self.until_not(
            lambda: self.driver.is_visible(element),
            message=message or f"Element still displayed: {element.locator}"
        )


def wait_for(
    timeout: float = 10.0,
    interval: float = 0.5,
    ignored_exceptions: Optional[List[Type[Exception]]] = None
):
    """Decorator to add explicit wait to a function.
    
    Usage:
        @wait_for(timeout=5)
        def find_element_with_retry(locator):
            return driver.find_element(locator)
    """
    def decorator(func: Callable) -> Callable:
        @wraps(func)
        def wrapper(*args, **kwargs):
            wait = Wait(
                timeout=timeout,
                interval=interval,
                ignored_exceptions=ignored_exceptions
            )
            
            last_exception = None
            end_time = time.time() + timeout
            
            while time.time() < end_time:
                try:
                    return func(*args, **kwargs)
                except Exception as e:
                    if ignored_exceptions and any(isinstance(e, exc) for exc in ignored_exceptions):
                        raise
                    last_exception = e
                    time.sleep(interval)
            
            if last_exception:
                raise last_exception
            return func(*args, **kwargs)
        
        return wrapper
    return decorator


class PollUntil:
    """Context manager for polling during a block.
    
    Usage:
        with PollUntil(interval=0.5) as poll:
            while condition:
                do_something()
                poll.wait_if_needed()
    """
    
    def __init__(
        self,
        timeout: float = 10.0,
        interval: float = 0.5
    ):
        self.timeout = timeout
        self.interval = interval
        self.start_time = None
        self.iterations = 0
    
    def __enter__(self):
        self.start_time = time.time()
        self.iterations = 0
        return self
    
    def __exit__(self, *args):
        pass
    
    def wait_if_needed(self) -> None:
        """Wait between iterations if within timeout."""
        self.iterations += 1
        elapsed = time.time() - self.start_time
        if elapsed < self.timeout:
            time.sleep(min(self.interval, self.timeout - elapsed))
    
    @property
    def is_timed_out(self) -> bool:
        """Check if timeout has been reached."""
        return (time.time() - self.start_time) >= self.timeout
