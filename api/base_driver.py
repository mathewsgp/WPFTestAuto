"""
Base Driver Interface
=====================
Abstract base class defining the contract for all automation drivers.
Implements the Template Method pattern for consistent driver behavior.
"""

from abc import ABC, abstractmethod
from typing import Any, Dict, List, Optional
from dataclasses import dataclass
import time


@dataclass
class DriverConfig:
    """Configuration for a driver instance."""
    timeout: float = 10.0
    retry_count: int = 3
    retry_delay: float = 0.5
    screenshot_on_failure: bool = False


@dataclass
class ElementHandle:
    """Abstract handle to an element returned by the driver."""
    locator: Dict[str, str]
    driver_name: str
    found_at: float = None
    
    def __post_init__(self):
        if self.found_at is None:
            self.found_at = time.time()
    
    def is_stale(self, max_age: float = 60.0) -> bool:
        """Check if the element handle is potentially stale."""
        return (time.time() - self.found_at) > max_age


class BaseDriver(ABC):
    """Abstract base class for all automation drivers.
    
    All drivers must implement these methods with identical signatures
    to ensure driver-agnostic behavior in Layer 3.
    """
    
    @property
    @abstractmethod
    def name(self) -> str:
        """Return the driver's name (e.g., 'FlaUI', 'WPFSpy', 'Sikuli')."""
        pass
    
    @abstractmethod
    def find_element(self, locator: Dict[str, Any]) -> ElementHandle:
        """Find an element by the given locator.
        
        Args:
            locator: Dictionary containing strategy-specific locator info.
                    Common keys:
                    - searchBy: "Name", "AutomationId", "XPath", "ImageTag"
                    - value: The actual locator value
        
        Returns:
            ElementHandle: A handle to the found element.
        
        Raises:
            ElementNotFoundError: If element cannot be found.
        """
        pass
    
    @abstractmethod
    def find_elements(self, locator: Dict[str, Any]) -> List[ElementHandle]:
        """Find multiple elements matching the locator.
        
        Args:
            locator: Dictionary containing strategy-specific locator info.
        
        Returns:
            List[ElementHandle]: List of matching elements (may be empty).
        """
        pass
    
    @abstractmethod
    def invoke(self, element: ElementHandle) -> None:
        """Invoke/click the element.
        
        Args:
            element: The element handle from find_element.
        
        Raises:
            ElementNotInteractableError: If element cannot be invoked.
        """
        pass
    
    @abstractmethod
    def set_value(self, element: ElementHandle, value: str) -> None:
        """Set the value of an input element.
        
        Args:
            element: The element handle from find_element.
            value: The value to set.
        
        Raises:
            ElementNotInteractableError: If element cannot receive value.
        """
        pass
    
    @abstractmethod
    def get_text(self, element: ElementHandle) -> str:
        """Get the text content of an element.
        
        Args:
            element: The element handle from find_element.
        
        Returns:
            str: The element's text content.
        """
        pass
    
    @abstractmethod
    def is_visible(self, element: ElementHandle) -> bool:
        """Check if an element is visible.
        
        Args:
            element: The element handle from find_element.
        
        Returns:
            bool: True if visible, False otherwise.
        """
        pass
    
    @abstractmethod
    def is_enabled(self, element: ElementHandle) -> bool:
        """Check if an element is enabled.
        
        Args:
            element: The element handle from find_element.
        
        Returns:
            bool: True if enabled, False otherwise.
        """
        pass
    
    @abstractmethod
    def toggle(self, element: ElementHandle, state: Optional[bool] = None) -> bool:
        """Toggle a checkbox or toggle button.
        
        Args:
            element: The element handle from find_element.
            state: Optional target state (True=checked, False=unchecked).
                   If None, toggles current state.
        
        Returns:
            bool: The new state after toggling.
        
        Raises:
            ElementNotInteractableError: If element is not a toggle control.
        """
        pass
    
    # Optional methods with default implementations
    
    def is_actionable(self, element: ElementHandle) -> bool:
        """Check if element is both visible and enabled.
        
        Default implementation combines is_visible and is_enabled.
        Override for custom behavior.
        """
        return self.is_visible(element) and self.is_enabled(element)
    
    def wait_until_actionable(
        self,
        element: ElementHandle,
        timeout: Optional[float] = None,
        poll_interval: float = 0.5
    ) -> bool:
        """Wait until element is actionable (visible and enabled).
        
        Args:
            element: The element handle from find_element.
            timeout: Maximum time to wait in seconds.
            poll_interval: Time between checks in seconds.
        
        Returns:
            bool: True if became actionable, False if timed out.
        """
        if timeout is None:
            timeout = 10.0
        
        end_time = time.time() + timeout
        while time.time() < end_time:
            if self.is_actionable(element):
                return True
            time.sleep(poll_interval)
        return False
    
    def capture_screenshot(self, element: Optional[ElementHandle] = None) -> bytes:
        """Capture a screenshot.
        
        Args:
            element: Optional element to capture (captures element region).
                    If None, captures entire screen/window.
        
        Returns:
            bytes: PNG image data.
        
        Raises:
            NotImplementedError: If driver doesn't support screenshots.
        """
        raise NotImplementedError(f"{self.name} does not support screenshots")
    
    def get_attribute(
        self,
        element: ElementHandle,
        attribute_name: str
    ) -> Optional[str]:
        """Get a specific attribute of an element.
        
        Args:
            element: The element handle from find_element.
            attribute_name: Name of the attribute.
        
        Returns:
            Optional[str]: The attribute value or None if not supported.
        """
        return None
    
    def close(self) -> None:
        """Clean up driver resources.
        
        Override to implement driver-specific cleanup.
        """
        pass
    
    def __repr__(self) -> str:
        return f"{self.__class__.__name__}(name='{self.name}')"
