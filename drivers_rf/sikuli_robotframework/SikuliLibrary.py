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

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "drivers", "mock_wpf_app"))
from mock_app import APP_INSTANCE, ElementNotFoundError, ElementNotInteractableError  # noqa: E402


class SikuliDriver:
    """Sikuli driver — locates elements by simulated image match.
    
    This is the final fallback in the chain, used for custom-rendered
    controls that standard UI Automation cannot see at all.
    """

    name = "Sikuli"

    def find_element(self, strategy: dict):
        """Locates an element using Sikuli image matching.
        
        Args:
            strategy: Dict with searchBy="Image" and value/imagePath keys.
                     In the mock, `imagePath` is treated as a semantic tag
                     rather than a real file.
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

    def invoke(self, element):
        APP_INSTANCE.invoke(element)

    def set_value(self, element, value: str):
        APP_INSTANCE.set_value(element, value)

    def get_text(self, element) -> str:
        return APP_INSTANCE.get_text(element)

    def is_visible(self, element) -> bool:
        return APP_INSTANCE.is_visible(element)

    def toggle(self, element, state: bool = None):
        APP_INSTANCE.invoke(element)


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
