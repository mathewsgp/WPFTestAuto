"""
Live UIA Event Recorder
=======================

High-level interface for recording UI Automation events from a live WPF application.
Uses WPFSpy's Named Pipe IPC to communicate with the in-process Spy Agent.

Usage:
    from recorder.live_recorder import LiveRecorder
    
    # Create recorder and connect to app
    recorder = LiveRecorder()
    
    # Start recording
    recorder.start_recording()
    
    # ... user interacts with WPF app ...
    
    # Stop recording and get events
    recorder.stop_recording()
    events = recorder.get_recorded_events()
    
    # Export to JSON for converter
    recorder.export_to_json("recorded_elements.json")

Architecture:
    Python (this module)
        └── WPFSpyRealDriver.start_recording() / get_recorded_events()
                └── Named Pipe IPC
                        └── WpfSpyAgent.UiaEventRecorder (C#)
                                └── UI Automation Event Handlers
                                        └── WPF Application
"""

import json
import os
import time
from typing import Dict, List, Optional, Any


class LiveRecorder:
    """Live UIA Event Recorder.
    
    Records user interactions with a WPF application in real-time using
    UI Automation event hooks. Captures:
    - Button clicks (Invoke events)
    - Text input (TextChanged events)
    - Focus changes (FocusChanged events)
    - Selection changes (Selection events)
    
    Each recorded event includes:
    - Timestamp
    - Element properties (AutomationId, Name, ControlType)
    - XPath for reliable identification
    - Event type and value
    
    Attributes:
        driver: The WPFSpy driver instance for IPC communication.
        is_recording: Whether recording is currently active.
        recorded_data: The raw recorded data from the last recording session.
    """
    
    def __init__(self, driver=None, mode="mock"):
        """Initialize the live recorder.
        
        Args:
            driver: Optional WPFSpy driver instance. If None, creates one.
            mode: Driver mode - "real" for Windows/WPF, "mock" for testing.
        """
        self.driver = driver
        self.mode = mode
        self.is_recording = False
        self.recorded_data: Dict[str, Any] = {}
        self._output_dir = None
        
    def _get_driver(self):
        """Get or create the WPFSpy driver."""
        if self.driver is None:
            if self.mode == "real":
                # Use real driver for Windows
                from drivers_rf.wpfspy_robotframework.WPFSpyLibrary import WPFSpyRealDriver
                self.driver = WPFSpyRealDriver()
            else:
                # Use mock driver for testing
                from drivers_rf.wpfspy_robotframework.WPFSpyLibrary import WPFSpyMockDriver
                self.driver = WPFSpyMockDriver()
        return self.driver
    
    def start_recording(self) -> bool:
        """Start recording UI Automation events.
        
        Hooks into the WPF application to capture user interactions in real-time.
        Must call stop_recording() when done.
        
        Returns:
            True if recording started successfully, False otherwise.
        """
        driver = self._get_driver()
        
        if hasattr(driver, 'start_recording'):
            response = driver.start_recording()
            if response.get("success"):
                self.is_recording = True
                self.recorded_data = {}
                print("[LiveRecorder] Recording started")
                return True
            else:
                print(f"[LiveRecorder] Failed to start recording: {response.get('error')}")
                return False
        else:
            print("[LiveRecorder] Driver does not support recording")
            return False
    
    def stop_recording(self) -> bool:
        """Stop recording UI Automation events.
        
        Returns:
            True if recording stopped successfully, False otherwise.
        """
        driver = self._get_driver()
        
        if hasattr(driver, 'stop_recording'):
            response = driver.stop_recording()
            if response.get("success"):
                self.is_recording = False
                print("[LiveRecorder] Recording stopped")
                return True
            else:
                print(f"[LiveRecorder] Failed to stop recording: {response.get('error')}")
                return False
        return False
    
    def get_recording_status(self) -> Dict[str, Any]:
        """Get the current recording status.
        
        Returns:
            Dict with isRecording (bool) and eventCount (int).
        """
        driver = self._get_driver()
        
        if hasattr(driver, 'get_recording_status'):
            return driver.get_recording_status()
        return {"isRecording": False, "eventCount": 0}
    
    def get_recorded_events(self) -> Dict[str, Any]:
        """Get all recorded events from the current recording session.
        
        Returns:
            Dict with elements, steps, and sequence arrays.
        """
        driver = self._get_driver()
        
        if hasattr(driver, 'get_recorded_events'):
            self.recorded_data = driver.get_recorded_events()
            return self.recorded_data
        return {}
    
    def clear_recording(self) -> bool:
        """Clear all recorded events.
        
        Returns:
            True if events were cleared successfully.
        """
        driver = self._get_driver()
        
        if hasattr(driver, 'clear_recording'):
            response = driver.clear_recording()
            if response.get("success"):
                self.recorded_data = {}
                print("[LiveRecorder] Recording cleared")
                return True
        return False
    
    def export_to_json(self, output_file: str = None, output_dir: str = None) -> str:
        """Export recorded events to JSON files.
        
        Creates three JSON files compatible with the recorder converter:
        - recorded_elements.json: Element definitions
        - recorded_steps.json: Step definitions
        - recorded_sequence.json: Event sequence
        
        Args:
            output_file: Optional single output path (uses output_dir if None).
            output_dir: Output directory for JSON files.
            
        Returns:
            Path to the output directory.
        """
        if not self.recorded_data:
            self.get_recorded_events()
        
        # Determine output location
        if output_dir is None:
            output_dir = os.path.join(
                os.path.dirname(os.path.abspath(__file__)),
                "sample_recorded"
            )
        os.makedirs(output_dir, exist_ok=True)
        
        # Export elements
        elements_file = os.path.join(output_dir, "recorded_elements.json")
        with open(elements_file, "w") as f:
            json.dump(self.recorded_data.get("elements", {}), f, indent=2)
        
        # Export steps
        steps_file = os.path.join(output_dir, "recorded_steps.json")
        with open(steps_file, "w") as f:
            json.dump(self.recorded_data.get("steps", []), f, indent=2)
        
        # Export sequence
        sequence_file = os.path.join(output_dir, "recorded_sequence.json")
        with open(sequence_file, "w") as f:
            json.dump(self.recorded_data.get("sequence", []), f, indent=2)
        
        print(f"[LiveRecorder] Exported to {output_dir}")
        return output_dir
    
    def export_for_converter(self) -> Dict[str, Any]:
        """Export recorded data in converter-compatible format.
        
        Returns:
            Dict with elements, steps, and sequence arrays.
        """
        if not self.recorded_data:
            self.get_recorded_events()
        return self.recorded_data
    
    def get_element_count(self) -> int:
        """Get the number of unique elements recorded.
        
        Returns:
            Number of unique element aliases recorded.
        """
        return len(self.recorded_data.get("elements", {}))
    
    def get_step_count(self) -> int:
        """Get the number of steps recorded.
        
        Returns:
            Number of steps (actions) recorded.
        """
        return len(self.recorded_data.get("steps", {}))
    
    def get_sequence_count(self) -> int:
        """Get the number of events in the sequence.
        
        Returns:
            Number of raw events recorded.
        """
        return len(self.recorded_data.get("sequence", []))


class RecordingContext:
    """Context manager for recording sessions.
    
    Usage:
        with RecordingContext() as recorder:
            # Recording starts automatically
            # ... user interacts with app ...
        # Recording stops automatically
    
    Args:
        mode: Driver mode - "real" or "mock".
        output_dir: Output directory for recorded JSON files.
    """
    
    def __init__(self, mode="mock", output_dir=None):
        self.recorder = LiveRecorder(mode=mode)
        self.output_dir = output_dir
        
    def __enter__(self):
        self.recorder.start_recording()
        return self.recorder
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        self.recorder.stop_recording()
        self.recorder.get_recorded_events()
        if self.output_dir:
            self.recorder.export_to_json(output_dir=self.output_dir)


def record_and_export(
    mode: str = "mock",
    output_dir: str = None,
    wait_seconds: float = 0
) -> LiveRecorder:
    """Convenience function to record events and export to JSON.
    
    Args:
        mode: Driver mode - "real" for Windows/WPF, "mock" for testing.
        output_dir: Output directory for recorded JSON files.
        wait_seconds: Seconds to wait before stopping (for demo purposes).
        
    Returns:
        The LiveRecorder instance with recorded data.
    """
    recorder = LiveRecorder(mode=mode)
    
    # Start recording
    recorder.start_recording()
    
    # Wait for user interaction (or demo wait)
    if wait_seconds > 0:
        print(f"[LiveRecorder] Recording for {wait_seconds} seconds...")
        time.sleep(wait_seconds)
    
    # Stop recording
    recorder.stop_recording()
    
    # Get events
    recorder.get_recorded_events()
    
    # Export
    if output_dir:
        recorder.export_to_json(output_dir=output_dir)
    
    return recorder


# Example usage
if __name__ == "__main__":
    print("=" * 60)
    print("Live UIA Event Recorder Demo")
    print("=" * 60)
    print()
    
    print("Mode: MOCK (simulated)")
    print("In production, use mode='real' with WPFSPY_MODE=real")
    print()
    
    # Create recorder in mock mode
    recorder = LiveRecorder(mode="mock")
    
    # Start recording
    print("1. Starting recording...")
    recorder.start_recording()
    
    # Simulate user interaction
    print("2. Simulating user interactions...")
    time.sleep(1)
    
    # Stop recording
    print("3. Stopping recording...")
    recorder.stop_recording()
    
    # Get events
    print("4. Getting recorded events...")
    events = recorder.get_recorded_events()
    
    print(f"   - Elements recorded: {recorder.get_element_count()}")
    print(f"   - Steps recorded: {recorder.get_step_count()}")
    print(f"   - Sequence events: {recorder.get_sequence_count()}")
    
    # Export to JSON
    print("5. Exporting to JSON...")
    output_dir = recorder.export_to_json()
    
    print()
    print("=" * 60)
    print("Recording complete! Files exported to:")
    print(f"  {output_dir}")
    print("=" * 60)
    
    # Show sample output
    print()
    print("Sample recorded elements:")
    for alias, elem in events.get("elements", {}).items():
        print(f"  {alias}: {elem.get('controlType')} - {elem.get('automationId')}")
