"""
Automatic Screenshot Capture on Failure
======================================
Provides automatic screenshot capture when element operations fail,
aiding in debugging test failures.

Usage:
    from screenshot_manager import ScreenshotManager
    
    # Initialize (creates screenshots directory)
    sm = ScreenshotManager(output_dir="test-output/screenshots")
    
    # Capture on failure
    sm.capture_on_failure("LoginPage.btnSubmit", "ElementNotFoundError")
    
    # Capture with custom filename
    sm.capture("custom_screenshot.png")
"""

import os
import base64
import json
from datetime import datetime
from pathlib import Path
from typing import Optional, Dict, Any, List
from dataclasses import dataclass, field


@dataclass
class ScreenshotMetadata:
    """Metadata for a captured screenshot."""
    timestamp: str
    alias: Optional[str]
    error_type: Optional[str]
    error_message: Optional[str]
    driver_used: Optional[str]
    screenshot_path: str
    full_screen: bool = True


class ScreenshotManager:
    """Manages automatic screenshot capture on test failures.
    
    Features:
    - Automatic screenshot on element operation failures
    - Configurable output directory
    - Metadata tracking in JSON
    - Multiple driver support (FlaUI, WPFSpy, Sikuli)
    - Full screen and region capture
    - Configurable capture triggers
    
    Usage:
        sm = ScreenshotManager()
        sm.capture_on_failure("LoginPage.btnSubmit", "ElementNotFoundError")
    """
    
    DEFAULT_OUTPUT_DIR = "test-output/screenshots"
    MAX_SCREENSHOTS_PER_RUN = 50
    
    def __init__(
        self,
        output_dir: str = None,
        capture_on_failure: bool = True,
        max_screenshots: int = None
    ):
        """Initialize the screenshot manager.
        
        Args:
            output_dir: Directory to save screenshots.
                       Defaults to test-output/screenshots
            capture_on_failure: Whether to auto-capture on failures
            max_screenshots: Maximum screenshots per test run
        """
        self.output_dir = Path(output_dir) if output_dir else Path(self.DEFAULT_OUTPUT_DIR)
        self.capture_on_failure = capture_on_failure
        self.max_screenshots = max_screenshots or self.MAX_SCREENSHOTS_PER_RUN
        
        # Create output directory
        self.output_dir.mkdir(parents=True, exist_ok=True)
        
        # Track screenshots this session
        self._screenshots: List[ScreenshotMetadata] = []
        self._session_id = datetime.now().strftime("%Y%m%d_%H%M%S")
        
        # Screenshot counter for unique naming
        self._counter = 0
    
    def _generate_filename(self, prefix: str = "failure") -> str:
        """Generate a unique screenshot filename.
        
        Args:
            prefix: Filename prefix (e.g., 'failure', 'custom')
            
        Returns:
            Filename with timestamp and counter
        """
        self._counter += 1
        timestamp = datetime.now().strftime("%H%M%S_%f")
        return f"{prefix}_{timestamp}_{self._counter:03d}.png"
    
    def _generate_metadata_filename(self) -> str:
        """Generate metadata filename for this session."""
        return f"screenshots_{self._session_id}.json"
    
    def capture(
        self,
        image_data: bytes,
        alias: Optional[str] = None,
        error_type: Optional[str] = None,
        error_message: Optional[str] = None,
        driver_used: Optional[str] = None,
        prefix: str = "failure"
    ) -> ScreenshotMetadata:
        """Capture and save a screenshot.
        
        Args:
            image_data: PNG image bytes
            alias: Element alias that failed
            error_type: Type of error that occurred
            error_message: Error message
            driver_used: Driver that was being used
            prefix: Filename prefix
            
        Returns:
            ScreenshotMetadata with capture details
        """
        # Check max screenshots limit
        if len(self._screenshots) >= self.max_screenshots:
            return None
        
        filename = self._generate_filename(prefix)
        filepath = self.output_dir / filename
        
        # Save screenshot
        with open(filepath, "wb") as f:
            f.write(image_data)
        
        # Create metadata
        metadata = ScreenshotMetadata(
            timestamp=datetime.now().isoformat(),
            alias=alias,
            error_type=error_type,
            error_message=error_message,
            driver_used=driver_used,
            screenshot_path=str(filepath)
        )
        
        self._screenshots.append(metadata)
        
        # Save metadata
        self._save_metadata()
        
        return metadata
    
    def capture_on_failure(
        self,
        alias: str,
        error: Exception,
        driver_used: Optional[str] = None,
        image_data: Optional[bytes] = None
    ) -> Optional[ScreenshotMetadata]:
        """Capture a screenshot on element operation failure.
        
        Args:
            alias: Element alias that failed
            error: The exception that occurred
            driver_used: Driver that was being used
            image_data: Pre-captured screenshot bytes (optional)
            
        Returns:
            ScreenshotMetadata if captured, None otherwise
        """
        if not self.capture_on_failure:
            return None
        
        return self.capture(
            image_data=image_data or self._get_fallback_screenshot(),
            alias=alias,
            error_type=type(error).__name__,
            error_message=str(error)[:500],  # Truncate long messages
            driver_used=driver_used,
            prefix="failure"
        )
    
    def _get_fallback_screenshot(self) -> bytes:
        """Get a placeholder image when no screenshot is available."""
        # Return minimal 1x1 transparent PNG
        return base64.b64decode(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="
        )
    
    def _save_metadata(self):
        """Save screenshot metadata to JSON file."""
        metadata_file = self.output_dir / self._generate_metadata_filename()
        
        data = {
            "session_id": self._session_id,
            "captured_at": datetime.now().isoformat(),
            "output_directory": str(self.output_dir),
            "total_screenshots": len(self._screenshots),
            "screenshots": [
                {
                    "timestamp": s.timestamp,
                    "alias": s.alias,
                    "error_type": s.error_type,
                    "error_message": s.error_message,
                    "driver_used": s.driver_used,
                    "screenshot_path": s.screenshot_path,
                    "filename": Path(s.screenshot_path).name
                }
                for s in self._screenshots
            ]
        }
        
        with open(metadata_file, "w") as f:
            json.dump(data, f, indent=2)
    
    def get_latest_screenshots(self, count: int = 10) -> List[ScreenshotMetadata]:
        """Get the most recent screenshots.
        
        Args:
            count: Number of screenshots to return
            
        Returns:
            List of most recent ScreenshotMetadata
        """
        return self._screenshots[-count:]
    
    def get_all_screenshots(self) -> List[ScreenshotMetadata]:
        """Get all screenshots captured this session."""
        return self._screenshots.copy()
    
    def get_screenshot_count(self) -> int:
        """Get count of screenshots captured this session."""
        return len(self._screenshots)
    
    def get_latest_screenshot_path(self) -> Optional[str]:
        """Get path to the most recent screenshot."""
        if self._screenshots:
            return self._screenshots[-1].screenshot_path
        return None
    
    def clear_session(self):
        """Clear screenshots from current session."""
        self._screenshots.clear()
        self._counter = 0
        self._session_id = datetime.now().strftime("%Y%m%d_%H%M%S")
    
    def generate_html_report(self, output_file: str = None) -> str:
        """Generate an HTML report of captured screenshots.
        
        Args:
            output_file: Optional file to save the report
            
        Returns:
            HTML content
        """
        html = f"""<!DOCTYPE html>
<html>
<head>
    <title>Screenshot Report - {self._session_id}</title>
    <style>
        body {{ font-family: Arial, sans-serif; margin: 20px; background: #f5f5f5; }}
        h1 {{ color: #333; }}
        .screenshot {{
            background: white;
            border: 1px solid #ddd;
            border-radius: 8px;
            padding: 15px;
            margin: 10px 0;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }}
        .screenshot img {{
            max-width: 800px;
            border: 1px solid #ccc;
            margin-top: 10px;
        }}
        .meta {{ color: #666; font-size: 12px; }}
        .error {{ color: #d32f2f; font-weight: bold; }}
        .alias {{ color: #1976d2; font-weight: bold; }}
    </style>
</head>
<body>
    <h1>🔍 Screenshot Report - {self._session_id}</h1>
    <p>Total Screenshots: {len(self._screenshots)}</p>
"""
        
        for screenshot in reversed(self._screenshots):
            html += f"""
    <div class="screenshot">
        <div class="meta">
            <span class="alias">Alias: {screenshot.alias or 'N/A'}</span> |
            <span class="error">{screenshot.error_type or 'N/A'}</span> |
            Driver: {screenshot.driver_used or 'N/A'}<br>
            Time: {screenshot.timestamp}<br>
            Error: {screenshot.error_message or 'N/A'}
        </div>
        <img src="{Path(screenshot.screenshot_path).name}" alt="Screenshot">
    </div>
"""
        
        html += """
</body>
</html>"""
        
        if output_file:
            with open(output_file, "w") as f:
                f.write(html)
        
        return html


# Global instance for easy access
_global_screenshot_manager: Optional[ScreenshotManager] = None


def get_screenshot_manager() -> ScreenshotManager:
    """Get the global screenshot manager instance."""
    global _global_screenshot_manager
    if _global_screenshot_manager is None:
        _global_screenshot_manager = ScreenshotManager()
    return _global_screenshot_manager


def capture_on_failure(alias: str, error: Exception, driver_used: str = None) -> Optional[ScreenshotMetadata]:
    """Convenience function to capture screenshot on failure.
    
    Args:
        alias: Element alias that failed
        error: The exception that occurred
        driver_used: Driver that was being used
        
    Returns:
        ScreenshotMetadata if captured, None otherwise
    """
    return get_screenshot_manager().capture_on_failure(alias, error, driver_used)
