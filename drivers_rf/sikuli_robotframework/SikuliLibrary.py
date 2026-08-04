"""
Sikuli.RobotFramework — Layer 4 driver wrapper for Sikuli.

Reference implementation notes (production deployment)
--------------------------------------------------------
In a real deployment, Sikuli takes a screenshot of the application window
and locates controls by matching a reference image (see
repository/elements/*.yaml -> strategies.Sikuli.imagePath) against the
screen using OpenCV-based template matching, returning screen coordinates
to click/type at. This is the fallback used for custom-rendered controls
(DirectX/GDI surfaces) that standard UI Automation cannot see at all.

Its method signatures are intentionally IDENTICAL to FlaUIDriver's and
WPFSpyDriver's — the "API parity" contract. This file implements those
same signatures against the in-repo mock WPF application, using a simple
tag-matching stand-in for real image template matching, so the framework
is fully runnable without a screen, OpenCV, or the real Sikuli engine.
"""

import sys
import os
from typing import List, Optional

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "drivers", "mock_wpf_app"))
from mock_app import APP_INSTANCE, ElementNotFoundError, ElementNotInteractableError  # noqa: E402


class SikuliDriver:
    """Sikuli driver — locates elements by simulated image match.
    
    This is the final fallback in the chain, used for custom-rendered
    controls that standard UI Automation cannot see at all.
    """

    name = "Sikuli"

    def find_element(self, strategy: dict):
        """Locates a single element using Sikuli image matching.
        
        Args:
            strategy: Dict with searchBy="Image" and value/imagePath keys.
                     In the mock, `imagePath` is treated as a semantic tag
                     rather than a real file.
                     
        Returns:
            ElementHandle for the found element.
            
        Raises:
            ElementNotFoundError: If no matching element is found.
        """
        search_by = strategy.get("searchBy", "Image")
        if search_by != "Image":
            raise ElementNotFoundError(f"Sikuli: only Image search supported, got {search_by}")
        
        # Support both 'value' and 'imagePath' keys
        tag = strategy.get("value") or strategy.get("imagePath")
        similarity = strategy.get("similarity", 0.85)
        
        print(f"[Sikuli] Matching image tag '{tag}' on screen "
              f"(similarity>={similarity})")
        ctrl = APP_INSTANCE.find_by_image_tag(tag)
        if ctrl is None:
            raise ElementNotFoundError(f"Sikuli: no on-screen match for image '{tag}'")
        return ctrl

    def find_elements(self, strategy: dict) -> List:
        """Locates all elements matching the Sikuli image.
        
        Args:
            strategy: Dict with searchBy="Image" and value/imagePath keys.
            
        Returns:
            List of ElementHandles for all matching elements.
        """
        search_by = strategy.get("searchBy", "Image")
        if search_by != "Image":
            return []
        
        tag = strategy.get("value") or strategy.get("imagePath")
        return APP_INSTANCE.find_all_by_image_tag(tag)

    def invoke(self, element):
        """Click/invoke an element."""
        APP_INSTANCE.invoke(element)

    def set_value(self, element, value: str):
        """Set text value on an input element."""
        APP_INSTANCE.set_value(element, value)

    def get_text(self, element) -> str:
        """Get the text content of an element."""
        return APP_INSTANCE.get_text(element)

    def is_visible(self, element) -> bool:
        """Check if an element is visible."""
        return APP_INSTANCE.is_visible(element)

    def is_enabled(self, element) -> bool:
        """Check if an element is enabled."""
        return APP_INSTANCE.is_enabled(element)

    def is_actionable(self, element) -> bool:
        """Check if an element is both visible and enabled."""
        return self.is_visible(element) and self.is_enabled(element)

    def get_attribute(self, element, attribute_name: str) -> Optional[str]:
        """Get a specific attribute value from an element."""
        return APP_INSTANCE.get_attribute(element, attribute_name)

    def capture_screenshot(self, element=None) -> bytes:
        """Capture a screenshot.
        
        Note: Real Sikuli would capture the actual screen region.
        This mock returns a placeholder image.
        """
        return APP_INSTANCE.capture_screenshot(element)

    def toggle(self, element, state: bool = None):
        """Toggle a checkbox or toggle button."""
        APP_INSTANCE.invoke(element)

    def close(self):
        """Clean up driver resources."""
        pass  # Mock implementation - no cleanup needed


class SikuliLibrary:
    """Robot Framework library exposing Sikuli keywords directly (rarely
    used directly by test authors — Layer 3 is the normal entry point).
    """
    ROBOT_LIBRARY_SCOPE = "GLOBAL"

    def __init__(self):
        self.driver = SikuliDriver()

    def sikuli_find_element(self, image_tag):
        return self.driver.find_element({"searchBy": "Image", "imagePath": image_tag})

    def sikuli_invoke(self, image_tag):
        el = self.sikuli_find_element(image_tag)
        self.driver.invoke(el)

    def sikuli_set_value(self, image_tag, value):
        el = self.sikuli_find_element(image_tag)
        self.driver.set_value(el, value)

    def sikuli_get_text(self, image_tag):
        el = self.sikuli_find_element(image_tag)
        return self.driver.get_text(el)
